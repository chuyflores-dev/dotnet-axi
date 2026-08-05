using DotNetAxi.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural.Tests;

public sealed class CatchClauseSyntaxQueryTests
{
    [Fact]
    public void Query_matches_typed_untyped_filtered_and_nested_catches()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                class C
                {
                    void M()
                    {
                        try { } catch { }
                        try { } catch (Exception) { }
                        try { } catch (System.Exception ex) when (ex is not null) { }
                        try { } catch (global::Demo.Exception) { }
                        try { } catch (@Exception) { }
                        try { } catch (exception) { }
                        try { } catch (ExceptionFactory) { }

                        void Local()
                        {
                            try { } catch (Exception) { }
                        }
                    }
                }
                """)
            .GetCompilationUnitRoot();

        var all = Types(new CatchClauseSyntaxQuery(), root);
        var typed = Types(new CatchClauseSyntaxQuery("Exception"), root);

        Assert.Equal(
            [
                "<untyped>",
                "Exception",
                "System.Exception",
                "global::Demo.Exception",
                "@Exception",
                "exception",
                "ExceptionFactory",
                "Exception",
            ],
            all);
        Assert.Equal(
            ["Exception", "System.Exception", "global::Demo.Exception", "@Exception", "Exception"],
            typed);
    }

    [Fact]
    public void Empty_filter_uses_parsed_statements_and_type_filter_excludes_untyped_catches()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                class C
                {
                    void M()
                    {
                        try { } catch { }
                        try { } catch (Exception) { /* comment-only */ }
                        try { } catch (Exception ex) when (ex is not null) { }
                        try { } catch (Exception) { ; }
                        try { } catch (Exception) { Handle(); }
                    }
                }
                """)
            .GetCompilationUnitRoot();

        var empty = new CatchClauseSyntaxQuery(emptyOnly: true)
            .FindCandidates(root)
            .Cast<CatchClauseSyntax>()
            .ToArray();
        var typedEmpty = new CatchClauseSyntaxQuery("Exception", emptyOnly: true)
            .FindCandidates(root)
            .Cast<CatchClauseSyntax>()
            .ToArray();

        Assert.Equal(3, empty.Length);
        Assert.Equal(2, typedEmpty.Length);
        Assert.All(empty, clause => Assert.Empty(clause.Block.Statements));
        Assert.All(typedEmpty, clause => Assert.NotNull(clause.Declaration));
    }

    [Fact]
    public void Query_keeps_a_recoverable_malformed_catch_candidate()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { void M() { try { } catch (Exception ex) {");
        var root = tree.GetCompilationUnitRoot();

        var match = Assert.Single(
            new CatchClauseSyntaxQuery("Exception", emptyOnly: true)
                .FindCandidates(root));

        Assert.IsType<CatchClauseSyntax>(match);
        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Fact]
    public void Query_identity_covers_type_and_empty_parameters_and_validates_input()
    {
        var all = new CatchClauseSyntaxQuery();
        var empty = new CatchClauseSyntaxQuery(emptyOnly: true);
        var typed = new CatchClauseSyntaxQuery("Exception");
        var other = new CatchClauseSyntaxQuery("IOException");

        Assert.Equal("catch", all.Kind);
        Assert.Null(all.Type);
        Assert.False(all.EmptyOnly);
        Assert.NotEqual(all.Identity, empty.Identity);
        Assert.NotEqual(all.Identity, typed.Identity);
        Assert.NotEqual(typed.Identity, other.Identity);
        Assert.Throws<ArgumentException>(() => new CatchClauseSyntaxQuery(" "));
        Assert.Throws<ArgumentException>(() => new CatchClauseSyntaxQuery("Bad\0Type"));
    }

    [Fact]
    public void Query_honors_pre_cancelled_discovery_even_without_catches()
    {
        var root = CSharpSyntaxTree.ParseText("class C { }")
            .GetCompilationUnitRoot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new CatchClauseSyntaxQuery()
                .FindCandidates(root, cancellation.Token)
                .ToArray());
    }

    [Fact]
    public async Task Query_integrates_with_path_and_generated_traversal_scope()
    {
        using var workspace = new TestWorkspace();
        var selected = await workspace.WriteAsync(
            "selected/Selected.cs",
            "class C { void M() { try { } catch (IOException) { } } }");
        var generated = await workspace.WriteAsync(
            "selected/Generated.g.cs",
            "class G { void M() { try { } catch (IOException) { } } }");
        var other = await workspace.WriteAsync(
            "other/Other.cs",
            "class O { void M() { try { } catch (IOException) { } } }");
        var traverser = new ScopedStubTraverser(
            Path(selected, "selected/Selected.cs"),
            Path(generated, "selected/Generated.g.cs"),
            Path(other, "other/Other.cs"));
        var engine = new RoslynSyntaxEngine(traverser);

        var normal = await engine.QueryAsync(
            Request(workspace.Root, includeGenerated: false),
            new CatchClauseSyntaxQuery("IOException"));
        var withGenerated = await engine.QueryAsync(
            Request(workspace.Root, includeGenerated: true),
            new CatchClauseSyntaxQuery("IOException"));

        Assert.Equal(
            ["selected/Selected.cs"],
            normal.Candidates.Select(candidate => candidate.Range.Start.Path));
        Assert.Equal(
            ["selected/Generated.g.cs", "selected/Selected.cs"],
            withGenerated.Candidates.Select(candidate => candidate.Range.Start.Path));
        Assert.All(
            traverser.Requests,
            request => Assert.Equal(["selected"], request.ExplicitPaths));
    }

    private static string[] Types(
        CatchClauseSyntaxQuery query,
        CompilationUnitSyntax root) =>
        query.FindCandidates(root)
            .Cast<CatchClauseSyntax>()
            .Select(clause => clause.Declaration?.Type.ToString() ?? "<untyped>")
            .ToArray();

    private static RoslynSyntaxQueryRequest Request(
        string root,
        bool includeGenerated) =>
        new(new WorkspaceTraversalRequest(
            root,
            explicitPaths: ["selected"],
            includeGenerated: includeGenerated));

    private static WorkspaceTraversalPath Path(string fullPath, string relativePath) =>
        new(fullPath, relativePath, isExternal: false);

    private sealed class ScopedStubTraverser(
        WorkspaceTraversalPath selected,
        WorkspaceTraversalPath generated,
        WorkspaceTraversalPath other) : IWorkspacePathTraverser
    {
        public List<WorkspaceTraversalRequest> Requests { get; } = [];

        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Assert.Equal(["selected"], request.ExplicitPaths);
            Assert.DoesNotContain(other, SelectedPaths(request));
            return SelectedPaths(request);
        }

        private IReadOnlyList<WorkspaceTraversalPath> SelectedPaths(
            WorkspaceTraversalRequest request) =>
            request.IncludeGenerated == true
                ? [selected, generated]
                : [selected];
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotnet-axi-catch-syntax-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task<string> WriteAsync(string relativePath, string contents)
        {
            var path = System.IO.Path.Combine(Root, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
