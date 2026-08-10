using DotNetAxi.Contracts;

namespace DotNetAxi.Structural.Tests;

public sealed class SymbolDeclarationSearcherTests
{
    [Fact]
    public async Task Search_ranks_declarations_and_keeps_overloads_and_partials()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync(
            "Symbols.cs",
            """
            namespace Demo.Tools;

            partial class Widget
            {
                public void Run() { }
                public void Run(int count) { }
            }

            partial class Widget { }
            class WidgetFactory { }
            class widget { }
            class SuperWidgetBuilder { }
            class HTTPServer { }
            class StorageDeclarationResolver { }
            """);
        var searcher = Searcher(
            [Path(source, "Symbols.cs")],
            new StubOwnershipResolver(["src/App/App.csproj"]));

        var result = await searcher.SearchAsync(Request(workspace.Root, "Widget"));

        Assert.Equal(
            ["Widget", "Widget", "widget", "WidgetFactory", "SuperWidgetBuilder"],
            result.Matches.Select(match => match.Name));
        Assert.Equal([1, 1, 2, 3, 4], result.Matches.Select(match => match.Rank));

        var qualified = await searcher.SearchAsync(
            Request(workspace.Root, "Demo.Tools.Widget"));
        Assert.Equal([0, 0, 2], qualified.Matches.Select(match => match.Rank));

        var token = await searcher.SearchAsync(Request(workspace.Root, "WF"));
        Assert.Equal("WidgetFactory", Assert.Single(token.Matches).Name);
        Assert.Equal(4, Assert.Single(token.Matches).Rank);

        var acronym = await searcher.SearchAsync(Request(workspace.Root, "HS"));
        Assert.Equal("HTTPServer", Assert.Single(acronym.Matches).Name);
        Assert.Equal(4, Assert.Single(acronym.Matches).Rank);

        var identifierToken = await searcher.SearchAsync(
            Request(workspace.Root, "Declaration"));
        Assert.Equal(
            "StorageDeclarationResolver",
            Assert.Single(identifierToken.Matches).Name);
        Assert.Equal(4, Assert.Single(identifierToken.Matches).Rank);

        var overloads = await searcher.SearchAsync(Request(workspace.Root, "Run"));
        Assert.Equal(2, overloads.Matches.Count);
        Assert.All(overloads.Matches, match => Assert.Equal("method", match.Kind));
        Assert.Equal(
            ["Run()", "Run(int)"],
            overloads.Matches.Select(match => match.Signature));
    }

    [Fact]
    public async Task Search_applies_kind_namespace_project_accessibility_test_and_generated_filters()
    {
        using var workspace = new TestWorkspace();
        var production = await workspace.WriteAsync(
            "src/App/Service.cs",
            "namespace Product.App; public class Service { private void Save() { } }");
        var test = await workspace.WriteAsync(
            "tests/App.Tests/ServiceTests.cs",
            "namespace Product.Tests; public class ServiceTests { public void Save() { } }");
        var generated = await workspace.WriteAsync(
            "src/App/Service.g.cs",
            "namespace Product.App; public class GeneratedService { }");
        var paths = new[]
        {
            Path(production, "src/App/Service.cs"),
            Path(test, "tests/App.Tests/ServiceTests.cs"),
            Path(generated, "src/App/Service.g.cs", isGenerated: true),
        };
        var ownership = new PathOwnershipResolver(new Dictionary<string, string[]>
        {
            ["src/App/Service.cs"] = ["src/App/App.csproj"],
            ["src/App/Service.g.cs"] = ["src/App/App.csproj"],
            ["tests/App.Tests/ServiceTests.cs"] = ["tests/App.Tests/App.Tests.csproj"],
        });
        var searcher = Searcher(paths, ownership);

        var result = await searcher.SearchAsync(new SymbolDeclarationSearchRequest(
            "Service",
            Traversal(workspace.Root),
            kinds: ["class"],
            namespaceFilter: "Product.App",
            project: "src/App/App.csproj",
            accessibilities: ["public"],
            includeTests: false));

        var match = Assert.Single(result.Matches);
        Assert.Equal("Service", match.Name);
        Assert.False(match.IsTest);
        Assert.False(match.IsGenerated);

        var included = await searcher.SearchAsync(new SymbolDeclarationSearchRequest(
            "Service",
            Traversal(workspace.Root, includeGenerated: true),
            includeTests: true));
        Assert.Contains(included.Matches, candidate => candidate.IsTest);
        Assert.Contains(included.Matches, candidate => candidate.IsGenerated);
    }

    [Fact]
    public async Task Search_reports_all_linked_file_owners_and_breaks_ties_deterministically()
    {
        using var workspace = new TestWorkspace();
        var second = await workspace.WriteAsync("z.cs", "namespace Shared; class Match { }");
        var first = await workspace.WriteAsync("a.cs", "namespace Shared; class Match { }");
        var ownership = new StubOwnershipResolver(
            ["src/A/A.csproj", "src/B/B.csproj"]);
        var searcher = Searcher(
            [Path(second, "z.cs"), Path(first, "a.cs")],
            ownership);

        var result = await searcher.SearchAsync(Request(workspace.Root, "Match"));

        Assert.Equal(["a.cs", "z.cs"], result.Matches.Select(match => match.Range.Start.Path));
        Assert.All(result.Matches, match => Assert.Equal(2, match.OwningProjectCount));
        Assert.All(
            result.Matches,
            match => Assert.Equal(
                ["src/A/A.csproj", "src/B/B.csproj"],
                match.OwningProjects));
        Assert.StartsWith("ws_", result.Snapshot);
        Assert.Equal(2, result.Observations.Count);
    }

    private static SymbolDeclarationSearcher Searcher(
        IReadOnlyList<WorkspaceTraversalPath> paths,
        IFileOwnershipResolver ownership) =>
        new(new StubTraverser(paths), ownership);

    private static SymbolDeclarationSearchRequest Request(string root, string query) =>
        new(query, Traversal(root));

    private static WorkspaceTraversalRequest Traversal(
        string root,
        bool includeGenerated = false) =>
        new(root, includeGenerated: includeGenerated);

    private static WorkspaceTraversalPath Path(
        string fullPath,
        string relativePath,
        bool isGenerated = false) =>
        new(fullPath, relativePath, isExternal: false, isGenerated: isGenerated);

    private sealed class StubTraverser(IReadOnlyList<WorkspaceTraversalPath> paths)
        : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.IncludeGenerated == true)
            {
                return paths;
            }

            return paths
                .Where(path => !path.RelativePath.EndsWith(
                    ".g.cs",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    private sealed class StubOwnershipResolver(IReadOnlyList<string> owners)
        : IFileOwnershipResolver
    {
        public IReadOnlyList<string> GetOwningProjects(WorkspaceTraversalPath path) => owners;
    }

    private sealed class PathOwnershipResolver(
        IReadOnlyDictionary<string, string[]> owners) : IFileOwnershipResolver
    {
        public IReadOnlyList<string> GetOwningProjects(WorkspaceTraversalPath path) =>
            owners.GetValueOrDefault(path.RelativePath) ?? [];
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotnet-axi-symbol-search-tests",
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
