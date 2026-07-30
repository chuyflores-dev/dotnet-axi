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
                ["search", "symbol"],
                ["analyze", "changed"],
                ["validate"],
            ],
            suggestions.Select(static suggestion => suggestion.Arguments));
        Assert.All(
            suggestions,
            static suggestion => Assert.Equal("dnaxi", suggestion.Command));
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
        Assert.Equal("dnaxi", suggestion.Command);
        Assert.Equal(
            [
                "show",
                "symbol",
                "sym_123",
                "--solution",
                "Repository Suite.slnx",
                "--project",
                "src/App/App.csproj",
                "--configuration",
                "Release Candidate",
                "--framework",
                "net10.0",
            ],
            suggestion.Arguments);
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
        Assert.Equal("dnaxi", suggestion.Command);
        Assert.Equal(
            ["search", "symbol", "<name>", "--kind", "extension-method"],
            suggestion.Arguments);
    }

    [Fact]
    public void Runtime_values_are_preserved_without_shell_specific_quoting()
    {
        var suggestions = ContextualSuggestions.Compose(
            [
                new SuggestionTemplate(
                    priority: 0,
                    [
                        SuggestionToken.Literal("show"),
                        SuggestionToken.Literal("document"),
                        SuggestionToken.RuntimeValue(
                            "src/It's $100 `% ready.cs"),
                    ]),
            ],
            WorkspaceSelectors.Empty);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(
            ["show", "document", "src/It's $100 `% ready.cs"],
            suggestion.Arguments);
    }

    [Fact]
    public void Distinct_invocations_cannot_collide_through_token_content()
    {
        var suggestions = ContextualSuggestions.Compose(
            [
                new SuggestionTemplate(
                    priority: 0,
                    [
                        SuggestionToken.Literal("show"),
                        SuggestionToken.RuntimeValue("a\u001fb"),
                    ]),
                new SuggestionTemplate(
                    priority: 0,
                    [
                        SuggestionToken.Literal("show"),
                        SuggestionToken.RuntimeValue("a"),
                        SuggestionToken.RuntimeValue("b"),
                    ]),
            ],
            WorkspaceSelectors.Empty);

        Assert.Equal(2, suggestions.Count);
    }

    [Theory]
    [InlineData("--project")]
    [InlineData("--solution=Other.slnx")]
    [InlineData("--configuration")]
    [InlineData("--framework=net9.0")]
    public void Templates_cannot_override_composer_owned_selectors(
        string selector)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SuggestionTemplate(
                priority: 0,
                [
                    SuggestionToken.Literal("search"),
                    SuggestionToken.Literal(selector),
                ]));

        Assert.Equal("tokens", exception.ParamName);
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
