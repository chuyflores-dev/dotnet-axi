using System.CommandLine;
using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class OutputShapingGoldenTests
{
    private readonly ToonResultSerializer _serializer = new();

    [Fact]
    public void Bounded_projected_result_matches_the_golden_contract()
    {
        var fields = CreateFields();
        var ordered = OutputOrdering.ByEvidence(
            new[]
            {
                new Match("sym_3", "src/C.cs", 30, "Third"),
                new Match("sym_1", "src/A.cs", 10, "First"),
                new Match("sym_2", "src/B.cs", 20, "Second"),
            },
            static row => new EvidenceOrderKey(
                row.File,
                row.Line,
                column: 1,
                kind: "method",
                fullyQualifiedName: $"Example.{row.Id}",
                stableId: row.Id));
        var bounded = BoundedCollection<Match>.Create(
            ordered,
            limit: 2,
            knownTotal: 3,
            retrievalCommand:
                "Run `dnaxi search symbol Widget --limit 3`");
        var selection = fields.Select(["detail"]);
        var text = BoundedText.Create(
            "A😀BCD",
            maxCharacters: 3,
            retrievalCommand:
                "Run `dnaxi show document src/A.cs --full`");
        var result = CommandResult<BoundedPayload>.Success(
            "search symbol",
            new BoundedPayload(
                bounded.Count,
                bounded.TotalKnown,
                bounded.Total,
                bounded.Omitted,
                bounded.Truncated,
                bounded.RetrievalCommand,
                selection.Project(bounded.Items),
                text));

        var document = _serializer.Serialize(result);

        Assert.Equal(ReadFixture("bounded-output.toon"), document);
    }

    [Fact]
    public void Bounded_context_matches_the_golden_contract()
    {
        var context = ContextBudgeter.Apply(
            [
                ContextSection<string>.Create(
                    "document",
                    order: 2,
                    value: "body",
                    emittedText: "body"),
                ContextSection<string>.Create(
                    "owner",
                    order: 1,
                    value: "xyz",
                    emittedText: "xyz"),
                ContextSection<string>.Create(
                    "declaration",
                    order: 0,
                    value: "A😀",
                    emittedText: "A😀"),
            ],
            ContextBudget.Resolve(
                defaultMaximumCharacters: 2,
                explicitMaximumCharacters: 5),
            maximum =>
                $"Run `dnaxi context symbol Example.Widget --max-chars {maximum}`",
            "Run `dnaxi context symbol Example.Widget --full`");
        var result = CommandResult<BoundedContext<string>>.Success(
            "context symbol",
            context);

        var document = _serializer.Serialize(result);

        Assert.Equal(ReadFixture("context-budget.toon"), document);
    }

    [Fact]
    public async Task Unknown_field_matches_the_usage_golden_without_creating_handler()
    {
        var rootCommand = new RootCommand();
        var searchCommand = new Command("search");
        var symbolCommand = new Command("symbol");
        var output = new StringWriter();
        var host = new CommandHost(
            rootCommand,
            OperationPolicy.Passive,
            [
                "dnaxi",
                "dnaxi --help",
            ],
            output,
            new StringWriter());
        host.RegisterCommand(
            rootCommand,
            searchCommand,
            OperationPolicy.Passive,
            [
                "dnaxi search",
                "dnaxi search --help",
            ]);
        host.RegisterCommand(
            searchCommand,
            symbolCommand,
            OperationPolicy.Passive,
            [
                "dnaxi search symbol Widget",
                "dnaxi search symbol --help",
            ]);
        var factoryCalls = 0;
        symbolCommand.BindHandler(
            _ => CreateFields().Select(["owner"]),
            () =>
            {
                factoryCalls++;
                return new NeverInvokedHandler();
            },
            host.ResponseWriter);

        var exitCode = await host.InvokeAsync(["search", "symbol"]);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(
            ReadFixture("unknown-field.toon"),
            output.ToString());
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

    private static string ReadFixture(string name) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            .TrimEnd('\r', '\n');

    private sealed record Match(
        string Id,
        string File,
        int Line,
        string Detail);

    private sealed record BoundedPayload(
        int Count,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Matches,
        BoundedText Text);

    private sealed class NeverInvokedHandler :
        ICommandHandler<OutputFieldSelection<Match>>
    {
        public ValueTask<ICommandResult> HandleAsync(
            OutputFieldSelection<Match> request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The handler must not be created or invoked.");
    }
}
