using DotNetAxi.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural.Tests;

public sealed class RoslynSyntaxEngineTests
{
    [Fact]
    public async Task Query_parses_valid_and_malformed_csharp_without_a_compilation()
    {
        using var workspace = new TestWorkspace();
        var valid = await workspace.WriteAsync(
            "z-valid.cs",
            "namespace Demo;\nclass Valid { }");
        var malformed = await workspace.WriteAsync(
            "a-malformed.cs",
            "class Broken {");
        var traverser = new StubTraverser(
        [
            Path(valid, "z-valid.cs"),
            Path(malformed, "a-malformed.cs"),
        ]);

        var result = await new RoslynSyntaxEngine(traverser).QueryAsync(
            Request(workspace.Root),
            new DescendantQuery<ClassDeclarationSyntax>("class"));

        Assert.Equal(
            ["a-malformed.cs", "z-valid.cs"],
            result.Candidates.Select(candidate => candidate.Range.Start.Path));
        Assert.Equal(2, result.Observations.Count);
        Assert.True(result.Observations[0].DiagnosticCount > 0);
        Assert.Equal(0, result.Observations[1].DiagnosticCount);
        Assert.All(result.Candidates, candidate => Assert.Equal("class", candidate.QueryKind));
        Assert.All(
            result.Candidates,
            candidate => Assert.Equal("class/v1", candidate.QueryIdentity));
        Assert.All(result.Candidates, candidate => Assert.StartsWith("syntax/v1/", candidate.Id));
        Assert.StartsWith("ws_", result.Snapshot);
    }

    [Fact]
    public async Task Query_normalizes_one_based_utf16_start_and_exclusive_end()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync(
            "Unicode.cs",
            "class C { string Value = \"😀\"; }");
        var engine = new RoslynSyntaxEngine(
            new StubTraverser([Path(source, "Unicode.cs")]));

        var result = await engine.QueryAsync(
            Request(workspace.Root),
            new DescendantQuery<LiteralExpressionSyntax>("literal"));

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(new SourceLocation("Unicode.cs", 1, 26), candidate.Range.Start);
        Assert.Equal(new SourceLocation("Unicode.cs", 1, 30), candidate.Range.End);
        Assert.Equal("\"😀\"", candidate.Text);
    }

    [Fact]
    public async Task Query_forwards_path_and_generated_scope_and_selects_only_csharp()
    {
        using var workspace = new TestWorkspace();
        var generated = await workspace.WriteAsync("Generated.g.CS", "class Generated { }");
        var ignored = await workspace.WriteAsync("notes.txt", "class NotCSharp { }");
        var traverser = new StubTraverser(
        [
            Path(ignored, "notes.txt"),
            Path(generated, "Generated.g.CS"),
        ]);
        var traversal = new WorkspaceTraversalRequest(
            workspace.Root,
            explicitPaths: ["Generated.g.CS"],
            includeGenerated: true,
            currentDirectory: workspace.Root);

        var result = await new RoslynSyntaxEngine(traverser).QueryAsync(
            new RoslynSyntaxQueryRequest(traversal),
            new DescendantQuery<ClassDeclarationSyntax>("class"));

        Assert.Same(traversal, Assert.Single(traverser.Requests));
        Assert.Equal("Generated.g.CS", Assert.Single(result.Observations).Path);
        Assert.Equal("Generated.g.CS", Assert.Single(result.Candidates).Range.Start.Path);
    }

    [Fact]
    public async Task Query_is_deterministic_for_traversal_order_duplicates_and_empty_results()
    {
        using var workspace = new TestWorkspace();
        var first = await workspace.WriteAsync("a.cs", "class A { }");
        var second = await workspace.WriteAsync("b.cs", "class B { }");
        var calls = 0;
        var traverser = new StubTraverser(_ =>
        {
            calls++;
            return calls == 1
                ? [Path(second, "b.cs"), Path(first, "a.cs"), Path(first, "a.cs")]
                : [Path(first, "a.cs"), Path(second, "b.cs")];
        });
        var engine = new RoslynSyntaxEngine(traverser);
        var query = new DescendantQuery<InvocationExpressionSyntax>("invocation");

        var firstResult = await engine.QueryAsync(Request(workspace.Root), query);
        var secondResult = await engine.QueryAsync(Request(workspace.Root), query);

        Assert.Empty(firstResult.Candidates);
        Assert.Equal(firstResult.Snapshot, secondResult.Snapshot);
        Assert.Equal(
            firstResult.Observations.Select(observation => observation.Path),
            secondResult.Observations.Select(observation => observation.Path));
        Assert.Equal(["a.cs", "b.cs"], firstResult.Observations.Select(item => item.Path));
    }

    [Fact]
    public async Task Query_identity_covers_parameters_in_snapshots_and_candidate_ids()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync("Class.cs", "class Shared { }");
        var engine = new RoslynSyntaxEngine(
            new StubTraverser([Path(source, "Class.cs")]));

        var first = await engine.QueryAsync(
            Request(workspace.Root),
            new DescendantQuery<ClassDeclarationSyntax>(
                "class",
                "class/v1?attribute=Authorize"));
        var second = await engine.QueryAsync(
            Request(workspace.Root),
            new DescendantQuery<ClassDeclarationSyntax>(
                "class",
                "class/v1?attribute=Serializable"));

        Assert.NotEqual(first.QueryIdentity, second.QueryIdentity);
        Assert.NotEqual(first.Snapshot, second.Snapshot);
        Assert.NotEqual(
            Assert.Single(first.Candidates).Id,
            Assert.Single(second.Candidates).Id);
    }

    [Fact]
    public async Task Query_honors_cancellation_before_traversal_and_during_discovery()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync("File.cs", "class C { }");
        var traverser = new StubTraverser([Path(source, "File.cs")]);
        var engine = new RoslynSyntaxEngine(traverser);
        using var before = new CancellationTokenSource();
        before.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await engine.QueryAsync(
                Request(workspace.Root),
                new DescendantQuery<ClassDeclarationSyntax>("class"),
                before.Token));
        Assert.Empty(traverser.Requests);

        using var during = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await engine.QueryAsync(
                Request(workspace.Root),
                new CancellingQuery(during),
                during.Token));
    }

    [Fact]
    public async Task Query_rejects_candidates_from_another_syntax_tree()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync("File.cs", "class C { }");
        var engine = new RoslynSyntaxEngine(
            new StubTraverser([Path(source, "File.cs")]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await engine.QueryAsync(
                Request(workspace.Root),
                new ForeignTreeQuery()));

        Assert.Contains("outside the selected syntax tree", error.Message);
    }

    private static RoslynSyntaxQueryRequest Request(string root) =>
        new(new WorkspaceTraversalRequest(root));

    private static WorkspaceTraversalPath Path(string fullPath, string relativePath) =>
        new(fullPath, relativePath, isExternal: false);

    private sealed class DescendantQuery<TNode>(
        string kind,
        string? identity = null) : IRoslynSyntaxQuery
        where TNode : SyntaxNode
    {
        public string Kind => kind;

        public string Identity => identity ?? kind + "/v1";

        public IEnumerable<SyntaxNode> FindCandidates(
            CompilationUnitSyntax root,
            CancellationToken cancellationToken = default) =>
            root.DescendantNodes().OfType<TNode>();
    }

    private sealed class CancellingQuery(CancellationTokenSource source)
        : IRoslynSyntaxQuery
    {
        public string Kind => "cancel";

        public string Identity => "cancel/v1";

        public IEnumerable<SyntaxNode> FindCandidates(
            CompilationUnitSyntax root,
            CancellationToken cancellationToken = default)
        {
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }
    }

    private sealed class ForeignTreeQuery : IRoslynSyntaxQuery
    {
        public string Kind => "foreign";

        public string Identity => "foreign/v1";

        public IEnumerable<SyntaxNode> FindCandidates(
            CompilationUnitSyntax root,
            CancellationToken cancellationToken = default) =>
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
                .ParseText("class Other { }")
                .GetRoot(cancellationToken)
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>();
    }

    private sealed class StubTraverser : IWorkspacePathTraverser
    {
        private readonly Func<WorkspaceTraversalRequest, IReadOnlyList<WorkspaceTraversalPath>>
            _paths;

        public StubTraverser(IReadOnlyList<WorkspaceTraversalPath> paths)
            : this(_ => paths)
        {
        }

        public StubTraverser(
            Func<WorkspaceTraversalRequest, IReadOnlyList<WorkspaceTraversalPath>> paths)
        {
            _paths = paths;
        }

        public List<WorkspaceTraversalRequest> Requests { get; } = [];

        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return _paths(request);
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotnet-axi-structural-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task<string> WriteAsync(string relativePath, string contents)
        {
            var path = System.IO.Path.Combine(Root, relativePath);
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
