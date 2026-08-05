using DotNetAxi.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural.Tests;

public sealed class ObjectCreationSyntaxQueryTests
{
    [Fact]
    public void Query_matches_explicit_generic_qualified_and_array_shapes()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                class C
                {
                    void M()
                    {
                        _ = new HttpClient();
                        _ = new HttpClient<int>();
                        _ = new System.Net.Http.HttpClient();
                        _ = new global::HttpClient();
                        _ = new HttpClient[3];
                        _ = new HttpClientFactory();
                        _ = HttpClient.Create();
                        _ = typeof(HttpClient);
                        _ = new { Name = "anonymous" };
                    }
                }
                """)
            .GetCompilationUnitRoot();

        var matches = new ObjectCreationSyntaxQuery("HttpClient")
            .FindCandidates(root)
            .Select(candidate => candidate.ToString())
            .ToArray();

        Assert.Equal(
            [
                "new HttpClient()",
                "new HttpClient<int>()",
                "new System.Net.Http.HttpClient()",
                "new global::HttpClient()",
                "new HttpClient[3]",
            ],
            matches);
    }

    [Fact]
    public void Query_retains_target_typed_creations_as_unresolved_candidates()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                class C
                {
                    HttpClient First = new();
                    Other Second = new();
                    Other Third = new Other();
                    int[] Values = new[] { 1 };
                }
                """)
            .GetCompilationUnitRoot();

        var matches = new ObjectCreationSyntaxQuery("HttpClient")
            .FindCandidates(root)
            .ToArray();

        Assert.Equal(2, matches.Length);
        Assert.All(matches, match => Assert.IsType<ImplicitObjectCreationExpressionSyntax>(match));
        Assert.All(matches, match => Assert.Equal("new()", match.ToString()));
    }

    [Fact]
    public void Query_keeps_a_recoverable_malformed_creation_candidate()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { object M() => new HttpClient(");
        var root = tree.GetCompilationUnitRoot();

        var match = Assert.Single(
            new ObjectCreationSyntaxQuery("HttpClient").FindCandidates(root));

        Assert.IsType<ObjectCreationExpressionSyntax>(match);
        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Fact]
    public void Query_returns_empty_for_false_candidates_and_ordinal_mismatches()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                class C
                {
                    HttpClient Field;

                    void M()
                    {
                        _ = new httpClient();
                        _ = new HttpClientFactory();
                        _ = HttpClient.Create();
                        _ = typeof(HttpClient);
                        _ = new { Value = 1 };
                        _ = new[] { new HttpClientFactory() };
                    }
                }
                """)
            .GetCompilationUnitRoot();

        var matches = new ObjectCreationSyntaxQuery("HttpClient")
            .FindCandidates(root)
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void Query_identity_is_parameter_sensitive_and_input_is_validated()
    {
        var first = new ObjectCreationSyntaxQuery("HttpClient");
        var second = new ObjectCreationSyntaxQuery("Other");

        Assert.Equal("object-creation", first.Kind);
        Assert.Equal("HttpClient", first.Type);
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Throws<ArgumentException>(() => new ObjectCreationSyntaxQuery(" "));
        Assert.Throws<ArgumentException>(() => new ObjectCreationSyntaxQuery("Bad\0Type"));
    }

    [Fact]
    public void Query_honors_pre_cancelled_discovery_even_without_creations()
    {
        var root = CSharpSyntaxTree.ParseText("class C { }")
            .GetCompilationUnitRoot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new ObjectCreationSyntaxQuery("HttpClient")
                .FindCandidates(root, cancellation.Token)
                .ToArray());
    }

    [Fact]
    public async Task Query_integrates_with_path_and_generated_traversal_scope()
    {
        using var workspace = new TestWorkspace();
        var selected = await workspace.WriteAsync(
            "selected/Selected.cs",
            "class C { object Value = new HttpClient(); }");
        var generated = await workspace.WriteAsync(
            "selected/Generated.g.cs",
            "class G { object Value = new HttpClient(); }");
        var other = await workspace.WriteAsync(
            "other/Other.cs",
            "class O { object Value = new HttpClient(); }");
        var traverser = new ScopedStubTraverser(
            Path(selected, "selected/Selected.cs"),
            Path(generated, "selected/Generated.g.cs"),
            Path(other, "other/Other.cs"));
        var engine = new RoslynSyntaxEngine(traverser);

        var normal = await engine.QueryAsync(
            Request(workspace.Root, includeGenerated: false),
            new ObjectCreationSyntaxQuery("HttpClient"));
        var withGenerated = await engine.QueryAsync(
            Request(workspace.Root, includeGenerated: true),
            new ObjectCreationSyntaxQuery("HttpClient"));

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
                "dotnet-axi-object-creation-tests",
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
