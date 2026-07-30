using System.Text;
using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class ToonResultSerializerTests
{
    private readonly ToonResultSerializer _serializer = new();

    [Fact]
    public void Normal_result_uses_the_v1_envelope_and_canonical_v4_1_shapes()
    {
        var result = CommandResult<SearchPayload>.Success(
            "search symbol",
            new SearchPayload(
                "Widget:\nstatus: failed",
                2,
                [
                    new SearchMatch("sym_1", "src/A.cs", 3),
                    new SearchMatch("sym_2", "src/B,Injected.cs", 9),
                ]),
            suggestions:
            [
                new ResultSuggestion("Run `dnaxi show symbol sym_1`"),
            ]);

        var document = _serializer.Serialize(result);

        Assert.Equal(ReadFixture("success.toon"), document);
    }

    [Fact]
    public void Evidence_result_expresses_partial_scope_and_errors()
    {
        var evidence = new Evidence(
            "ws_123",
            EvidenceResolution.Semantic,
            new EvidenceCoverage(
                CoverageLevel.Partial,
                considered: 8,
                analyzed: 6,
                remaining: 2,
                partialReason: "Two projects require restore."),
            EvidenceConfidence.Verified,
            new EvidenceScope(
                "/work/repository",
                "Selected project graph",
                solution: "Repository.slnx",
                projects:
                [
                    "src/Core/Core.csproj",
                    "src/Api/Api.csproj",
                ],
                frameworks: ["net10.0"],
                configuration: "Debug"));
        var result = CommandResult<CountPayload>.Partial(
            "search callers",
            new CountPayload(2),
            evidence,
            errors:
            [
                new ResultError(
                    "analysis.incomplete",
                    "Two projects were not analyzed.",
                    "Run `dnaxi restore`, then repeat with `--complete`.")
            ]);

        var document = _serializer.Serialize(result);

        Assert.Equal(ReadFixture("evidence.toon"), document);
    }

    [Fact]
    public void Cancelled_result_matches_the_golden_contract()
    {
        var result = CommandResult<MessagePayload>.Cancelled(
            "analyze",
            errors:
            [
                new ResultError(
                    "operation.cancelled",
                    "Analysis was cancelled.",
                    "Run the command again when analysis can complete."),
            ]);

        var document = _serializer.Serialize(result);

        Assert.Equal(ReadFixture("cancelled.toon"), document);
    }

    [Fact]
    public void Query_plan_reports_level_scope_project_loads_and_selectors()
    {
        var plan = new QueryPlan(
            "roslyn-semantic",
            QueryEngineClass.Semantic,
            new QueryPlanCandidate(
                QueryAnalysisLevel.Complete,
                EvidenceResolution.Semantic,
                CoverageLevel.Complete,
                "Selected solution and all target frameworks",
                [
                    "src/Core/Core.csproj",
                    "src/App/App.csproj",
                ]),
            new WorkspaceSelectors(
                solution: "Repository.slnx",
                project: "src/App/App.csproj",
                configuration: "Release",
                framework: "net10.0"));
        var result = CommandResult<QueryPlan>.Success(
            "search callers",
            plan);

        var document = _serializer.Serialize(result);

        Assert.Equal(ReadFixture("query-plan.toon"), document);
    }

    [Fact]
    public void Empty_collections_remain_explicit_empty_arrays()
    {
        var result = CommandResult<SearchPayload>.Success(
            "search symbol",
            new SearchPayload("Missing", 0, []));

        var document = _serializer.Serialize(result);

        Assert.EndsWith(
            """
            query: Missing
            count: 0
            matches: []
            """,
            document,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Utf8_output_has_no_bom_cr_or_trailing_newline()
    {
        var result = CommandResult<MessagePayload>.Success(
            "show document",
            new MessagePayload("Hello 世界 👋"));

        var bytes = _serializer.SerializeToUtf8(result);
        var document = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(bytes);

        Assert.Equal("message: Hello 世界 👋", document.Split('\n')[^1]);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain('\r', document);
        Assert.False(document.EndsWith('\n'));
    }

    [Fact]
    public void Non_finite_host_numbers_normalize_to_null()
    {
        var result = CommandResult<MetricsPayload>.Success(
            "analyze",
            new MetricsPayload(double.NaN, double.PositiveInfinity, -0d));

        var document = _serializer.Serialize(result);

        Assert.EndsWith(
            """
            score: null
            maximum: null
            zero: 0
            """,
            document,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_cannot_replace_envelope_fields()
    {
        var result = CommandResult<Dictionary<string, string>>.Success(
            "search symbol",
            new Dictionary<string, string>
            {
                ["status"] = "failed",
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => _serializer.Serialize(result));

        Assert.Contains("status", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unpaired_surrogates_are_rejected_instead_of_replaced()
    {
        var result = CommandResult<MessagePayload>.Success(
            "show document",
            new MessagePayload("\ud800"));

        Assert.Throws<ArgumentException>(() => _serializer.Serialize(result));
    }

    [Fact]
    public void Nested_uniform_columns_use_v4_1_field_groups()
    {
        var result = CommandResult<OrdersPayload>.Success(
            "search orders",
            new OrdersPayload(
                [
                    new Order(1, new Customer("Ada", "DK"), 99),
                    new Order(2, new Customer("Bob", "UK"), 149),
                ]));

        var document = _serializer.Serialize(result);

        Assert.EndsWith(
            """
            orders[2]{id,customer{name,country},total}:
              1,Ada,DK,99
              2,Bob,UK,149
            """,
            document,
            StringComparison.Ordinal);
    }

    private sealed record SearchPayload(
        string Query,
        int Count,
        IReadOnlyList<SearchMatch> Matches);

    private sealed record SearchMatch(string Id, string File, int Line);

    private sealed record CountPayload(int Count);

    private sealed record MessagePayload(string Message);

    private sealed record MetricsPayload(double Score, double Maximum, double Zero);

    private sealed record OrdersPayload(IReadOnlyList<Order> Orders);

    private sealed record Order(int Id, Customer Customer, int Total);

    private sealed record Customer(string Name, string Country);

    private static string ReadFixture(string name) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            .TrimEnd('\r', '\n');
}
