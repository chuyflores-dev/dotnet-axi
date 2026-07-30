using DotNetAxi.Contracts;

namespace DotNetAxi.Contracts.Tests;

public sealed class ContextualSuggestionsTests
{
    [Fact]
    public void Suggestions_are_deterministic_deduplicated_and_bounded()
    {
        var suggestions = ContextualSuggestions.Compose(
            [
                Template(40, "help"),
                Template(20, "analyze", "changed"),
                Template(10, "search", "symbol"),
                Template(30, "validate"),
                Template(5, "search", "symbol"),
            ],
            WorkspaceSelectors.Empty);

        Assert.Equal(
            [
                "Run `dnaxi search symbol`",
                "Run `dnaxi analyze changed`",
                "Run `dnaxi validate`",
            ],
            suggestions.Select(static suggestion => suggestion.Command));
    }

    [Fact]
    public void Suggestions_preserve_fixed_selectors_in_canonical_order()
    {
        var suggestions = ContextualSuggestions.Compose(
            [
                new SuggestionTemplate(
                    priority: 0,
                    [
                        SuggestionToken.Literal("show"),
                        SuggestionToken.Literal("symbol"),
                        SuggestionToken.RuntimeValue("sym_123"),
                    ]),
            ],
            new WorkspaceSelectors(
                solution: "Repository Suite.slnx",
                project: "src/App/App.csproj",
                configuration: "Release Candidate",
                framework: "net10.0"));

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(
            "Run `dnaxi show symbol sym_123 --solution 'Repository Suite.slnx' --project src/App/App.csproj --configuration 'Release Candidate' --framework net10.0`",
            suggestion.Command);
    }

    [Fact]
    public void Placeholder_is_explicit_and_runtime_value_is_not_replaced()
    {
        var suggestions = ContextualSuggestions.Compose(
            [
                new SuggestionTemplate(
                    priority: 0,
                    [
                        SuggestionToken.Literal("search"),
                        SuggestionToken.Literal("symbol"),
                        SuggestionToken.Placeholder("name"),
                        SuggestionToken.Literal("--kind"),
                        SuggestionToken.RuntimeValue("extension-method"),
                    ]),
            ],
            WorkspaceSelectors.Empty);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(
            "Run `dnaxi search symbol '<name>' --kind extension-method`",
            suggestion.Command);
    }

    [Fact]
    public void Self_contained_response_omits_supplied_templates()
    {
        var suggestions = ContextualSuggestions.Compose(
            [Template(0, "show", "symbol")],
            WorkspaceSelectors.Empty,
            selfContained: true);

        Assert.Empty(suggestions);
    }

    private static SuggestionTemplate Template(
        int priority,
        params string[] literals) =>
        new(
            priority,
            literals.Select(SuggestionToken.Literal));
}
