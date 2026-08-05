using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotNetAxi.Contracts;
using DotNetAxi.Search;

namespace DotNetAxi.Search.Tests;

public sealed class RegexTextSearcherTests
{
    [Fact]
    public void Searches_with_dotnet_patterns_and_preserves_supported_encoding_and_skip_behavior()
    {
        var root = CreateDirectory();
        try
        {
            var utf8 = Path.Combine(root, "utf8.cs");
            var utf16 = Path.Combine(root, "utf16.cs");
            var binary = Path.Combine(root, "binary.dat");
            var invalid = Path.Combine(root, "invalid.txt");
            File.WriteAllText(
                utf8,
                "before\nneedle-42",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.WriteAllText(
                utf16,
                "needle-7",
                new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            File.WriteAllBytes(binary, [0, 1, 2]);
            File.WriteAllBytes(invalid, [0xff]);

            var result = Search(
                @"(?<word>needle)-\d+",
                [
                    Entry(utf8, "utf8.cs"),
                    Entry(utf16, "utf16.cs"),
                    Entry(binary, "binary.dat"),
                    Entry(invalid, "invalid.txt"),
                ]);

            Assert.Equal(
                ["utf8.cs", "utf16.cs"],
                result.Matches.Select(match => match.Location.Path));
            Assert.Equal(2, result.Total);
            Assert.True(result.TotalKnown);
            Assert.Empty(result.Errors);
            Assert.Equal(1, result.SkippedBinary);
            Assert.Equal(1, result.SkippedUndecodable);
            Assert.Equal(2, result.Matches[0].Location.Line);
            Assert.Equal(1, result.Matches[0].Location.Column);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Invalid_pattern_is_a_query_outcome_and_does_not_start_traversal()
    {
        var traverser = new CountingTraverser();
        var request = new RegexTextSearchRequest(
            "[invalid",
            new WorkspaceTraversalRequest(Directory.GetCurrentDirectory()),
            TimeSpan.FromSeconds(1));

        var result = new RegexTextSearcher(traverser).Search(request);

        Assert.Equal(0, traverser.Calls);
        Assert.Empty(result.Matches);
        Assert.False(result.TotalKnown);
        var error = Assert.Single(result.Errors);
        Assert.Equal(TextSearchErrorKind.InvalidRegularExpression, error.Kind);
        Assert.Equal("[invalid", error.Query);
        Assert.Null(error.Path);
    }

    [Fact]
    public void Catastrophic_input_times_out_one_file_and_scanning_continues()
    {
        var root = CreateDirectory();
        try
        {
            var catastrophic = Path.Combine(root, "catastrophic.txt");
            var later = Path.Combine(root, "later.txt");
            File.WriteAllText(catastrophic, new string('a', 100_000) + "!");
            File.WriteAllText(later, "aaaa");
            const string query = "^(a+)+$";

            var result = Search(
                query,
                [
                    Entry(catastrophic, "catastrophic.txt"),
                    Entry(later, "later.txt"),
                ],
                TimeSpan.FromMilliseconds(1),
                caseSensitive: true);

            var match = Assert.Single(result.Matches);
            Assert.Equal("later.txt", match.Location.Path);
            Assert.False(result.TotalKnown);
            Assert.Null(result.Total);
            var error = Assert.Single(result.Errors);
            Assert.Equal(TextSearchErrorKind.RegularExpressionTimeout, error.Kind);
            Assert.Equal(query, error.Query);
            Assert.Equal("catastrophic.txt", error.Path);
            Assert.Equal(
                [
                    TextSearchFileStatus.RegularExpressionTimeout,
                    TextSearchFileStatus.Analyzed,
                ],
                result.Observations.Select(item => item.Status));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Per_file_deadline_discards_timing_dependent_matches_and_scanning_continues()
    {
        var root = CreateDirectory();
        try
        {
            var repeated = Path.Combine(root, "repeated.txt");
            var later = Path.Combine(root, "later.txt");
            File.WriteAllText(repeated, new string('a', 5_000_000));
            File.WriteAllText(later, "a");

            var result = Search(
                "a",
                [Entry(repeated, "repeated.txt"), Entry(later, "later.txt")],
                TimeSpan.FromMilliseconds(1),
                caseSensitive: true,
                limit: int.MaxValue);

            var match = Assert.Single(result.Matches);
            Assert.Equal("later.txt", match.Location.Path);
            Assert.False(result.TotalKnown);
            var error = Assert.Single(result.Errors);
            Assert.Equal(TextSearchErrorKind.RegularExpressionTimeout, error.Kind);
            Assert.Equal("a", error.Query);
            Assert.Equal("repeated.txt", error.Path);
            Assert.Equal(
                TextSearchFileStatus.Analyzed,
                result.Observations[^1].Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("a*", "ba")]
    [InlineData("(?=a)", "aba")]
    [InlineData(@"\G.", "abc")]
    [InlineData(@"\G(?:a)?", "ab")]
    [InlineData(@"(?=\G)", "ab")]
    [InlineData("^|$", "a")]
    public void Per_file_budgeting_preserves_dotnet_match_progression(
        string query,
        string content)
    {
        var root = CreateDirectory();
        try
        {
            var path = Path.Combine(root, "progression.txt");
            File.WriteAllText(path, content);
            var expectedColumns = new Regex(
                    query,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1))
                .Matches(content)
                .Select(match => match.Index + 1);

            var result = Search(
                query,
                [Entry(path, "progression.txt")]);

            Assert.Equal(
                expectedColumns,
                result.Matches.Select(match => match.Location.Column));
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Result_limit_preserves_stable_first_matches_and_unknown_total()
    {
        var root = CreateDirectory();
        try
        {
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            File.WriteAllText(first, "hit-1\nhit-2");
            File.WriteAllText(second, "hit-3");

            var result = Search(
                @"hit-\d",
                [Entry(first, "first.txt"), Entry(second, "second.txt")],
                limit: 2);

            Assert.Equal(2, result.Matches.Count);
            Assert.False(result.TotalKnown);
            Assert.Null(result.Total);
            Assert.Empty(result.Errors);
            Assert.Equal(
                [1, 2],
                result.Matches.Select(match => match.Location.Line));
            Assert.Equal(
                TextSearchFileStatus.LimitReached,
                result.Observations[^1].Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Case_modes_are_culture_invariant()
    {
        var root = CreateDirectory();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var ascii = Path.Combine(root, "ascii.txt");
            var dotted = Path.Combine(root, "dotted.txt");
            var upper = Path.Combine(root, "upper.txt");
            File.WriteAllText(ascii, "I");
            File.WriteAllText(dotted, "İ");
            File.WriteAllText(upper, "NEEDLE");
            var turkish = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = turkish;
            CultureInfo.CurrentUICulture = turkish;

            var invariant = Search(
                "^i$",
                [Entry(ascii, "ascii.txt"), Entry(dotted, "dotted.txt")]);
            var sensitive = Search(
                "^needle$",
                [Entry(upper, "upper.txt")],
                caseSensitive: true);
            var insensitive = Search(
                "^needle$",
                [Entry(upper, "upper.txt")]);

            Assert.Equal("ascii.txt", Assert.Single(invariant.Matches).Location.Path);
            Assert.Empty(sensitive.Matches);
            Assert.Single(insensitive.Matches);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Request_requires_a_positive_bounded_per_file_timeout()
    {
        var traversal = new WorkspaceTraversalRequest(Directory.GetCurrentDirectory());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RegexTextSearchRequest("query", traversal, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RegexTextSearchRequest("query", traversal, TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RegexTextSearchRequest("query", traversal, TimeSpan.MaxValue));
    }

    [Fact]
    public async Task Pre_cancelled_search_does_not_start_traversal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var traverser = new CountingTraverser();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new RegexTextSearcher(traverser).SearchAsync(
                new RegexTextSearchRequest(
                    "query",
                    new WorkspaceTraversalRequest(Directory.GetCurrentDirectory()),
                    TimeSpan.FromSeconds(1)),
                cancellation.Token));

        Assert.Equal(0, traverser.Calls);
    }

    private static TextSearchResult Search(
        string query,
        WorkspaceTraversalPath[] paths,
        TimeSpan? perFileTimeout = null,
        bool caseSensitive = false,
        int limit = 100) =>
        new RegexTextSearcher(new Traverser(paths)).Search(
            new RegexTextSearchRequest(
                query,
                new WorkspaceTraversalRequest(Directory.GetCurrentDirectory()),
                perFileTimeout ?? TimeSpan.FromSeconds(1),
                caseSensitive,
                limit));

    private static WorkspaceTraversalPath Entry(
        string fullPath,
        string relativePath) =>
        new(fullPath, relativePath, isExternal: false);

    private static string CreateDirectory()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dotnet-axi-regex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class Traverser(IEnumerable<WorkspaceTraversalPath> paths)
        : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default) =>
            paths.ToArray();
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
}
