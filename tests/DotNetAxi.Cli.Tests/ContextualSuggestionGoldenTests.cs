using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class ContextualSuggestionGoldenTests
{
    private readonly ToonResultSerializer _serializer = new();

    [Theory]
    [InlineData("home")]
    [InlineData("empty")]
    [InlineData("ambiguous")]
    [InlineData("partial")]
    [InlineData("error")]
    [InlineData("self-contained")]
    [InlineData("fixed-scope")]
    public void Contextual_scenario_matches_golden_output(string scenario)
    {
        var document = _serializer.Serialize(CreateResult(scenario));

        Assert.Equal(
            ReadFixture($"suggestions-{scenario}.toon"),
            document);
    }

    private static ICommandResult CreateResult(string scenario) =>
        scenario switch
        {
            "home" => Success(
                "home",
                scenario,
                ContextualSuggestions.Compose(
                    [
                        Template(40, Literal("help")),
                        Template(
                            10,
                            Literal("search"),
                            Literal("symbol"),
                            Placeholder("name")),
                        Template(
                            30,
                            Literal("validate"),
                            Literal("--profile"),
                            Literal("fast")),
                        Template(
                            20,
                            Literal("analyze"),
                            Literal("changed")),
                    ],
                    WorkspaceSelectors.Empty)),
            "empty" => Success(
                "search symbol",
                scenario,
                ContextualSuggestions.Compose(
                    [Template(0, Literal("search"), Literal("symbol"))],
                    WorkspaceSelectors.Empty,
                    selfContained: true)),
            "ambiguous" => Failed(
                "home",
                scenario,
                new ResultError(
                    "workspace.ambiguous",
                    "More than one solution was found.",
                    "Select one of the reported solution paths."),
                ContextualSuggestions.Compose(
                    [
                        Template(
                            0,
                            Literal("search"),
                            Literal("symbol"),
                            Placeholder("name")),
                    ],
                    new WorkspaceSelectors(
                        solution: "Credit Platform.slnx"))),
            "partial" => Partial(
                "search callers",
                scenario,
                ContextualSuggestions.Compose(
                    [
                        Template(
                            0,
                            Literal("search"),
                            Literal("callers"),
                            Runtime("sym_8k2m"),
                            Literal("--complete")),
                    ],
                    new WorkspaceSelectors(
                        project: "src/Core/Core.csproj"))),
            "error" => Failed(
                "validate",
                scenario,
                new ResultError(
                    "restore.required",
                    "Assets required for validation are missing.",
                    "Restore the selected project."),
                ContextualSuggestions.Compose(
                    [Template(0, Literal("restore"))],
                    new WorkspaceSelectors(
                        project: "src/Core/Core.csproj"))),
            "self-contained" => Success(
                "show symbol",
                scenario,
                ContextualSuggestions.Compose(
                    [Template(0, Literal("search"), Literal("callers"))],
                    WorkspaceSelectors.Empty,
                    selfContained: true)),
            "fixed-scope" => Success(
                "search symbol",
                scenario,
                ContextualSuggestions.Compose(
                    [
                        Template(
                            0,
                            Literal("show"),
                            Literal("symbol"),
                            Runtime("sym_123")),
                    ],
                    new WorkspaceSelectors(
                        solution: "Repository.slnx",
                        project: "src/App/App.csproj",
                        configuration: "Release",
                        framework: "net10.0"))),
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown suggestion scenario."),
        };

    private static ICommandResult Success(
        string command,
        string scenario,
        IReadOnlyList<ResultSuggestion> suggestions) =>
        CommandResult<ScenarioPayload>.Success(
            command,
            new ScenarioPayload(scenario),
            suggestions: suggestions);

    private static ICommandResult Partial(
        string command,
        string scenario,
        IReadOnlyList<ResultSuggestion> suggestions) =>
        CommandResult<ScenarioPayload>.Partial(
            command,
            new ScenarioPayload(scenario),
            suggestions: suggestions);

    private static ICommandResult Failed(
        string command,
        string scenario,
        ResultError error,
        IReadOnlyList<ResultSuggestion> suggestions) =>
        CommandResult<ScenarioPayload>.Failed(
            command,
            [error],
            new ScenarioPayload(scenario),
            suggestions: suggestions);

    private static SuggestionTemplate Template(
        int priority,
        params SuggestionToken[] tokens) =>
        new(priority, tokens);

    private static SuggestionToken Literal(string value) =>
        SuggestionToken.Literal(value);

    private static SuggestionToken Runtime(string value) =>
        SuggestionToken.RuntimeValue(value);

    private static SuggestionToken Placeholder(string name) =>
        SuggestionToken.Placeholder(name);

    private static string ReadFixture(string name) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            .TrimEnd('\r', '\n');

    private sealed record ScenarioPayload(string Case);
}
