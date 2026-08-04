using System.Text;
using DotNetAxi.Contracts;
using DotNetAxi.Search;

namespace DotNetAxi.Search.Tests;

public sealed class LiteralTextSearcherTests
{
    [Fact]
    public void Searches_utf8_bom_and_utf16_with_ordinal_case_and_skips_bad_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var utf8 = Path.Combine(root, "utf8.cs");
            var utf16 = Path.Combine(root, "utf16.cs");
            var binary = Path.Combine(root, "binary.dat");
            var invalid = Path.Combine(root, "invalid.txt");
            File.WriteAllText(utf8, "alpha\nİstanbul NEEDLE", new UTF8Encoding(true));
            File.WriteAllText(utf16, "needle", new UnicodeEncoding(false, true));
            File.WriteAllBytes(binary, [0, 1, 2]);
            File.WriteAllBytes(invalid, [0xff]);
            var result = Search(
                "needle",
                new WorkspaceTraversalPath(utf8, "utf8.cs", false),
                new WorkspaceTraversalPath(utf16, "utf16.cs", false),
                new WorkspaceTraversalPath(binary, "binary.dat", false),
                new WorkspaceTraversalPath(invalid, "invalid.txt", false));

            Assert.Equal(
                ["utf8.cs", "utf16.cs"],
                result.Matches.Select(match => match.Location.Path));
            Assert.Equal(2, result.Total);
            Assert.True(result.TotalKnown);
            Assert.Equal(1, result.SkippedBinary);
            Assert.Equal(1, result.SkippedUndecodable);
            Assert.Equal(2, result.Matches[0].Location.Line);
            Assert.Equal(10, result.Matches[0].Location.Column);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Limit_produces_stable_first_matches_and_unknown_total()
    {
        var result = Search(
            "hit",
            new WorkspaceTraversalPath(CreateFile("a.cs", "hit\nhit"), "a.cs", false),
            new WorkspaceTraversalPath(CreateFile("b.cs", "hit"), "b.cs", false),
            limit: 2);

        Assert.Equal(2, result.Matches.Count);
        Assert.False(result.TotalKnown);
        Assert.Null(result.Total);
        Assert.Equal([1, 2], result.Matches.Select(match => match.Location.Line));
        Assert.All(result.Matches, match => Assert.StartsWith("text/v1/", match.Id));
    }

    [Fact]
    public void Empty_results_are_successful_and_previews_stay_on_the_matching_line()
    {
        var path = new WorkspaceTraversalPath(
            CreateFile("preview.cs", "first line\nsecond needle line"),
            "preview.cs",
            false);
        var match = Assert.Single(Search("needle", path).Matches);

        Assert.Equal("second needle line", match.Preview);
        Assert.Equal(2, match.Location.Line);
        Assert.Equal(8, match.Location.Column);
        var empty = Search("missing", path);
        Assert.Empty(empty.Matches);
        Assert.Equal(0, empty.Total);
        Assert.True(empty.TotalKnown);
    }

    [Fact]
    public void Rejects_utf32_and_changes_match_identity_when_content_changes()
    {
        var utf32 = CreateFile("utf32.txt", string.Empty);
        File.WriteAllBytes(utf32, [0xff, 0xfe, 0, 0, 65, 0, 0, 0]);
        var first = new WorkspaceTraversalPath(CreateFile("identity.cs", "needle"), "identity.cs", false);
        var firstResult = Search("needle", first);
        File.WriteAllText(first.FullPath, "needle changed");
        var secondResult = Search("needle", first);

        Assert.Single(Search("needle", new WorkspaceTraversalPath(utf32, "utf32.txt", false)).SkippedFiles);
        Assert.Equal("unsupported_encoding", Search("needle", new WorkspaceTraversalPath(utf32, "utf32.txt", false)).SkippedFiles[0].Reason);
        Assert.NotEqual(firstResult.Snapshot, secondResult.Snapshot);
        Assert.NotEqual(firstResult.Matches[0].Id, secondResult.Matches[0].Id);
    }

    [Theory]
    [InlineData("before\rafter needle", 2, 7)]
    [InlineData("before\nafter needle", 2, 7)]
    [InlineData("before\r\nafter needle", 2, 7)]
    public void Reports_correct_locations_for_each_line_ending(string content, int line, int column)
    {
        var match = Assert.Single(Search("needle", new WorkspaceTraversalPath(CreateFile("lines.txt", content), "lines.txt", false)).Matches);

        Assert.Equal(line, match.Location.Line);
        Assert.Equal(column, match.Location.Column);
    }

    [Fact]
    public void Empty_leading_line_preview_and_scalar_budget_are_safe()
    {
        var newline = Assert.Single(Search("\nneedle", new WorkspaceTraversalPath(CreateFile("newline.txt", "\nneedle"), "newline.txt", false)).Matches);
        var longLine = Assert.Single(new LiteralTextSearcher(new Traverser([
            new WorkspaceTraversalPath(CreateFile("unicode.txt", "😀😀😀needle😀😀😀"), "unicode.txt", false)]))
            .Search(new TextSearchRequest("needle", new WorkspaceTraversalRequest(Directory.GetCurrentDirectory()), previewLength: 3)).Matches);

        Assert.Equal("…", newline.Preview);
        Assert.InRange(longLine.Preview.EnumerateRunes().Count(), 1, 3);
    }

    [Fact]
    public void Decoding_failure_retains_raw_bytes_in_the_snapshot()
    {
        var path = CreateFile("invalid.txt", string.Empty);
        var entry = new WorkspaceTraversalPath(path, "invalid.txt", false);
        File.WriteAllBytes(path, [0xff]);
        var first = Search("needle", entry).Snapshot;
        File.WriteAllBytes(path, [0xfe]);
        var second = Search("needle", entry).Snapshot;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Literal_ignore_case_is_independent_of_the_current_culture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            var turkish = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            System.Globalization.CultureInfo.CurrentCulture = turkish;
            System.Globalization.CultureInfo.CurrentUICulture = turkish;

            Assert.Single(Search("istanbul", new WorkspaceTraversalPath(CreateFile("culture.txt", "ISTANBUL"), "culture.txt", false)).Matches);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public async Task Pre_cancelled_search_does_not_start_traversal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var traverser = new CountingTraverser();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new LiteralTextSearcher(traverser).SearchAsync(
            new TextSearchRequest("needle", new WorkspaceTraversalRequest(Directory.GetCurrentDirectory())), cancellation.Token));

        Assert.Equal(0, traverser.Calls);
    }

    private static TextSearchResult Search(string query, params WorkspaceTraversalPath[] paths) =>
        Search(query, paths, 100);

    private static TextSearchResult Search(string query, WorkspaceTraversalPath first, WorkspaceTraversalPath second, int limit) =>
        Search(query, [first, second], limit);

    private static TextSearchResult Search(string query, WorkspaceTraversalPath[] paths, int limit) =>
        new LiteralTextSearcher(new Traverser(paths)).Search(new TextSearchRequest(
            query,
            new WorkspaceTraversalRequest(Directory.GetCurrentDirectory()),
            limit: limit));

    private static string CreateFile(string name, string text)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + name);
        File.WriteAllText(path, text);
        return path;
    }

    private sealed class Traverser(IEnumerable<WorkspaceTraversalPath> paths) : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(WorkspaceTraversalRequest request, CancellationToken cancellationToken = default) =>
            paths.ToArray();
    }

    private sealed class CountingTraverser : IWorkspacePathTraverser
    {
        public int Calls { get; private set; }

        public IReadOnlyList<WorkspaceTraversalPath> Traverse(WorkspaceTraversalRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return [];
        }
    }
}
