using System.Globalization;
using DotNetAxi.Contracts;

namespace DotNetAxi.Contracts.Tests;

public sealed class OutputShapingTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    public void Evidence_ordering_is_invariant_and_uses_every_shared_tie_breaker(
        string cultureName)
    {
        var rows = new[]
        {
            Row("id-5", "src/B.cs", 1, 1, "method", "Type.M", "sym_5"),
            Row("id-4", "src\\A.cs", 2, 2, "method", "Type.M", "sym_4"),
            Row("id-3", "src/A.cs", 2, 1, "method", "Type.Z", "sym_3"),
            Row("id-2", "src/A.cs", 2, 1, "method", "Type.A", "sym_2"),
            Row("id-1", "src/A.cs", 1, 9, "type", "Type", "sym_1"),
        };
        var expected = new[] { "id-1", "id-2", "id-3", "id-4", "id-5" };
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var forward = OutputOrdering
                .ByEvidence(rows, row => row.OrderKey)
                .Select(row => row.Id);
            var reversed = OutputOrdering
                .ByEvidence(rows.Reverse(), row => row.OrderKey)
                .Select(row => row.Id);

            Assert.Equal(expected, forward);
            Assert.Equal(expected, reversed);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Requested_fields_augment_defaults_in_canonical_order()
    {
        var fields = CreateFields();
        var defaults = fields.Select();
        var expanded = fields.Select(["detail", "line", "detail"]);
        var row = new Match("sym_1", "src/A.cs", 7, "public method");

        Assert.Equal(["id", "file", "line"], defaults.Fields);
        Assert.Equal(["id", "file", "line", "detail"], expanded.Fields);
        Assert.Equal(
            ["id", "file", "line", "detail"],
            expanded.Project(row).Keys);
        Assert.Equal("public method", expanded.Project(row)["detail"]);
    }

    [Fact]
    public void Unknown_fields_are_rejected_with_the_available_field_set()
    {
        var exception = Assert.Throws<UnknownOutputFieldsException>(
            () => CreateFields().Select(
                ["owner", "detail", "signature", "owner"]));

        Assert.Equal(["owner", "signature"], exception.UnknownFields);
        Assert.Equal(
            ["id", "file", "line", "detail"],
            exception.AvailableFields);
    }

    [Fact]
    public void Collection_limit_reports_known_total_and_omissions()
    {
        var result = BoundedCollection<int>.Create(
            [1, 2, 3],
            limit: 2,
            knownTotal: 5,
            retrievalCommand: "Run `dnaxi search symbol Widget --limit 5`");

        Assert.Equal([1, 2], result.Items);
        Assert.Equal(2, result.Count);
        Assert.True(result.TotalKnown);
        Assert.Equal(5, result.Total);
        Assert.Equal(3, result.Omitted);
        Assert.True(result.Truncated);
        Assert.Equal(
            "Run `dnaxi search symbol Widget --limit 5`",
            result.RetrievalCommand);
    }

    [Fact]
    public void Collection_limit_probes_only_enough_to_report_unknown_total()
    {
        var result = BoundedCollection<int>.Create(
            MoreThanTwo(),
            limit: 2,
            retrievalCommand: "Run `dnaxi search text Widget --limit 100`");

        Assert.Equal([1, 2], result.Items);
        Assert.False(result.TotalKnown);
        Assert.Null(result.Total);
        Assert.Null(result.Omitted);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Exhausted_collection_derives_total_and_omits_unused_escape_hatch()
    {
        var result = BoundedCollection<int>.Create(
            [1, 2],
            limit: 2,
            retrievalCommand: "Run `dnaxi search symbol Widget --full`");

        Assert.Equal(2, result.Total);
        Assert.Equal(0, result.Omitted);
        Assert.False(result.Truncated);
        Assert.Null(result.RetrievalCommand);
    }

    [Fact]
    public void Truncated_collection_requires_a_retrieval_command()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => BoundedCollection<int>.Create([1, 2], limit: 1));

        Assert.Equal("retrievalCommand", exception.ParamName);
    }

    [Fact]
    public void Text_limit_counts_unicode_scalars_without_splitting_surrogates()
    {
        var result = BoundedText.Create(
            "A😀BC",
            maxCharacters: 2,
            retrievalCommand: "Run `dnaxi show document src/A.cs --full`");

        Assert.Equal("A😀", result.Preview);
        Assert.Equal(2, result.IncludedCharacters);
        Assert.Equal(4, result.TotalCharacters);
        Assert.Equal(2, result.OmittedCharacters);
        Assert.True(result.Truncated);
        Assert.Equal(
            "Run `dnaxi show document src/A.cs --full`",
            result.RetrievalCommand);
    }

    [Fact]
    public void Invalid_utf16_is_rejected_before_output()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => BoundedText.Create("\ud800", maxCharacters: 1));

        Assert.Equal("text", exception.ParamName);
    }

    private static OutputFieldSet<Match> CreateFields() =>
        new(
            [
                new OutputField<Match>(
                    "id",
                    static row => row.Id,
                    includedByDefault: true),
                new OutputField<Match>(
                    "file",
                    static row => row.File,
                    includedByDefault: true),
                new OutputField<Match>(
                    "line",
                    static row => row.Line,
                    includedByDefault: true),
                new OutputField<Match>(
                    "detail",
                    static row => row.Detail),
            ]);

    private static OrderedRow Row(
        string id,
        string path,
        int line,
        int column,
        string kind,
        string fullyQualifiedName,
        string stableId) =>
        new(
            id,
            new EvidenceOrderKey(
                path,
                line,
                column,
                kind,
                fullyQualifiedName,
                stableId));

    private static IEnumerable<int> MoreThanTwo()
    {
        yield return 1;
        yield return 2;
        yield return 3;
        throw new InvalidOperationException(
            "The bounded collector enumerated beyond its one-item probe.");
    }

    private sealed record OrderedRow(
        string Id,
        EvidenceOrderKey OrderKey);

    private sealed record Match(
        string Id,
        string File,
        int Line,
        string Detail);
}
