using System.Globalization;
using DotNetAxi.Contracts;
using DotNetAxi.Search;

namespace DotNetAxi.Search.Tests;

public sealed class FileSearcherTests
{
    [Fact]
    public void Ranks_file_name_matches_before_directory_only_matches()
    {
        var result = Search(
            "widget",
            [
                Path("src/widget/Other.cs"),
                Path("docs/MyWidget.md"),
                Path("src/WidgetTests.cs"),
                Path("src/Widget.cs"),
                Path("Widget"),
            ]);

        Assert.Equal(
            [
                "Widget",
                "src/Widget.cs",
                "src/WidgetTests.cs",
                "docs/MyWidget.md",
                "src/widget/Other.cs",
            ],
            result.Matches.Select(static match => match.Path));
        Assert.Equal(5, result.Total);
        Assert.All(
            result.Matches,
            static match => Assert.StartsWith("file/v1/", match.Id));
        Assert.Equal("source", result.Matches[1].Kind);
    }

    [Fact]
    public void Applies_case_extension_and_repeated_glob_filters_before_limit()
    {
        var paths = new[]
        {
            Path("src/Alpha.cs"),
            Path("src/alpha.md"),
            Path("tests/AlphaTests.CS"),
            Path("other/Alpha.cs"),
        };

        var sensitive = Search(
            "Alpha",
            paths,
            caseSensitive: true,
            extensions: [".cs"],
            globs: ["src/**"]);
        var insensitive = Search(
            "alpha",
            paths,
            extensions: ["CS"],
            globs: ["src/**", "tests/**"],
            limit: 1);

        Assert.Equal(
            ["src/Alpha.cs"],
            sensitive.Matches.Select(static match => match.Path));
        Assert.Equal(1, sensitive.Total);
        Assert.Equal(
            ["src/Alpha.cs"],
            insensitive.Matches.Select(static match => match.Path));
        Assert.Equal(2, insensitive.Total);
    }

    [Fact]
    public void Ownership_is_sorted_and_deduplicated_without_reading_files()
    {
        var path = Path("src/Shared.cs");
        var ownership = new OwnershipResolver(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [path.RelativePath] =
                [
                    "src/Nested.csproj",
                    "Root.csproj",
                    "Root.csproj",
                ],
            });

        var result = new FileSearcher(
                new Traverser([path]),
                ownership)
            .Search(new FileSearchRequest(
                "shared",
                TraversalRequest()));

        var match = Assert.Single(result.Matches);
        Assert.Equal(2, match.OwningProjectCount);
        Assert.Equal(
            ["Root.csproj", "src/Nested.csproj"],
            match.OwningProjects);
    }

    [Fact]
    public void Matching_is_ordinal_ignore_case_in_every_current_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var turkish = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = turkish;
            CultureInfo.CurrentUICulture = turkish;

            var result = Search("istanbul", [Path("ISTANBUL.cs")]);

            Assert.Single(result.Matches);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Empty_results_have_a_known_zero_total()
    {
        var result = Search("missing", [Path("src/Present.cs")]);

        Assert.Empty(result.Matches);
        Assert.Equal(0, result.Total);
        Assert.StartsWith("ws_", result.Snapshot);
    }

    [Fact]
    public void Pre_cancelled_search_does_not_start_traversal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var traverser = new CountingTraverser();

        Assert.Throws<OperationCanceledException>(() =>
            new FileSearcher(traverser, EmptyOwnership.Instance).Search(
                new FileSearchRequest("file", TraversalRequest()),
                cancellation.Token));

        Assert.Equal(0, traverser.Calls);
    }

    private static FileSearchResult Search(
        string query,
        IReadOnlyList<WorkspaceTraversalPath> paths,
        bool caseSensitive = false,
        IEnumerable<string>? extensions = null,
        IEnumerable<string>? globs = null,
        int limit = 100) =>
        new FileSearcher(new Traverser(paths), EmptyOwnership.Instance).Search(
            new FileSearchRequest(
                query,
                TraversalRequest(),
                caseSensitive,
                extensions,
                globs,
                limit));

    private static WorkspaceTraversalRequest TraversalRequest() =>
        new(Directory.GetCurrentDirectory());

    private static WorkspaceTraversalPath Path(string relativePath) =>
        new(
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid().ToString("N")),
            relativePath,
            isExternal: false);

    private sealed class Traverser(
        IReadOnlyList<WorkspaceTraversalPath> paths) :
        IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default) =>
            paths;
    }

    private sealed class CountingTraverser : IWorkspacePathTraverser
    {
        public int Calls { get; private set; }

        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return [];
        }
    }

    private sealed class OwnershipResolver(
        IReadOnlyDictionary<string, IReadOnlyList<string>> owners) :
        IFileOwnershipResolver
    {
        public IReadOnlyList<string> GetOwningProjects(
            WorkspaceTraversalPath path) =>
            owners.GetValueOrDefault(path.RelativePath) ?? [];
    }

    private sealed class EmptyOwnership : IFileOwnershipResolver
    {
        public static EmptyOwnership Instance { get; } = new();

        public IReadOnlyList<string> GetOwningProjects(
            WorkspaceTraversalPath path) =>
            [];
    }
}
