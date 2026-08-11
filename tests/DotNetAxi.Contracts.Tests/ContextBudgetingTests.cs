using DotNetAxi.Contracts;

namespace DotNetAxi.Contracts.Tests;

public sealed class ContextBudgetingTests
{
    [Fact]
    public void Budget_resolution_uses_explicit_configured_default_and_full_modes()
    {
        var defaultBudget = ContextBudget.Resolve(100);
        var configuredBudget = ContextBudget.Resolve(
            100,
            configuredMaximumCharacters: 200);
        var explicitBudget = ContextBudget.Resolve(
            100,
            configuredMaximumCharacters: 200,
            explicitMaximumCharacters: 300);
        var fullBudget = ContextBudget.Resolve(
            100,
            configuredMaximumCharacters: 200,
            full: true);

        Assert.Equal(ContextBudgetMode.Default, defaultBudget.Mode);
        Assert.Equal(100, defaultBudget.MaximumCharacters);
        Assert.Equal(ContextBudgetMode.Configured, configuredBudget.Mode);
        Assert.Equal(200, configuredBudget.MaximumCharacters);
        Assert.Equal(ContextBudgetMode.Explicit, explicitBudget.Mode);
        Assert.Equal(300, explicitBudget.MaximumCharacters);
        Assert.Equal(ContextBudgetMode.Full, fullBudget.Mode);
        Assert.Null(fullBudget.MaximumCharacters);
    }

    [Fact]
    public void Exact_unicode_boundary_reports_sizes_omissions_tokens_and_recovery()
    {
        var result = ContextBudgeter.Apply(
            CreateSections(),
            ContextBudget.Resolve(
                defaultMaximumCharacters: 2,
                explicitMaximumCharacters: 5),
            maximum =>
                $"Run `dnaxi context symbol Example.Widget --max-chars {maximum}`",
            "Run `dnaxi context symbol Example.Widget --full`");

        Assert.Equal(ContextBudgetMode.Explicit, result.BudgetMode);
        Assert.Equal(5, result.MaximumCharacters);
        Assert.Equal(["declaration", "owner"], SectionNames(result));
        Assert.Equal(5, result.IncludedCharacters);
        Assert.True(result.TotalKnown);
        Assert.Equal(9, result.TotalCharacters);
        Assert.Equal(4, result.OmittedCharacters);
        Assert.Equal(["document"], result.OmittedSections);
        Assert.Equal(new ApproximateTokenRange(1, 3), result.ApproximateTokens);
        Assert.True(result.Truncated);
        Assert.Equal(
            "Run `dnaxi context symbol Example.Widget --max-chars 9`",
            result.RetrievalCommand);
    }

    [Fact]
    public void Suggested_larger_budget_is_sufficient_for_a_complete_rerun()
    {
        var initial = ContextBudgeter.Apply(
            CreateSections(),
            ContextBudget.Resolve(5),
            maximum =>
                $"Run `dnaxi context symbol Example.Widget --max-chars {maximum}`",
            "Run `dnaxi context symbol Example.Widget --full`");
        var larger = ContextBudgeter.Apply(
            CreateSections(),
            ContextBudget.Resolve(
                defaultMaximumCharacters: 5,
                explicitMaximumCharacters: checked((int)initial.TotalCharacters!.Value)),
            maximum =>
                $"Run `dnaxi context symbol Example.Widget --max-chars {maximum}`",
            "Run `dnaxi context symbol Example.Widget --full`");

        Assert.Equal(9, initial.TotalCharacters);
        Assert.Equal(ContextBudgetMode.Explicit, larger.BudgetMode);
        Assert.Equal(9, larger.MaximumCharacters);
        Assert.Equal(["declaration", "owner", "document"], SectionNames(larger));
        Assert.False(larger.Truncated);
        Assert.Null(larger.RetrievalCommand);
    }

    [Fact]
    public void Full_mode_includes_every_complete_section()
    {
        var result = ContextBudgeter.Apply(
            CreateSections(),
            ContextBudget.Resolve(1, full: true));

        Assert.Equal(ContextBudgetMode.Full, result.BudgetMode);
        Assert.Null(result.MaximumCharacters);
        Assert.Equal(["declaration", "owner", "document"], SectionNames(result));
        Assert.Equal(9, result.IncludedCharacters);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Section_selection_is_canonical_and_repeatable()
    {
        var sections = new[]
        {
            ContextSection<string>.Create("beta", 0, "B", "1234"),
            ContextSection<string>.Create("gamma", 1, "G", "7"),
            ContextSection<string>.Create("alpha", 0, "A", "56"),
        };
        var budget = ContextBudget.Resolve(3);

        var forward = ContextBudgeter.Apply(
            sections,
            budget,
            maximum => $"Run with {maximum}",
            "Run full");
        var reversed = ContextBudgeter.Apply(
            sections.Reverse(),
            budget,
            maximum => $"Run with {maximum}",
            "Run full");

        Assert.Equal(["alpha", "gamma"], SectionNames(forward));
        Assert.Equal(["beta"], forward.OmittedSections);
        Assert.Equal(SectionNames(forward), SectionNames(reversed));
        Assert.Equal(forward.OmittedSections, reversed.OmittedSections);
        Assert.Equal(forward.RetrievalCommand, reversed.RetrievalCommand);
    }

    [Fact]
    public void Unknown_section_total_uses_full_recovery_and_preserves_unknown_totals()
    {
        var result = ContextBudgeter.Apply(
            [
                ContextSection<string>.CreateObserved(
                    "document",
                    order: 0,
                    value: "partial",
                    emittedText: "A😀",
                    knownTotalCharacters: null),
            ],
            ContextBudget.Resolve(10),
            maximum => $"Run with {maximum}",
            "Run `dnaxi context symbol Example.Widget --full`");

        Assert.Equal(["document"], SectionNames(result));
        Assert.Equal(2, result.IncludedCharacters);
        Assert.False(result.TotalKnown);
        Assert.Null(result.TotalCharacters);
        Assert.Null(result.OmittedCharacters);
        Assert.Equal(["document"], result.OmittedSections);
        Assert.True(result.Truncated);
        Assert.Equal(
            "Run `dnaxi context symbol Example.Widget --full`",
            result.RetrievalCommand);
    }

    [Fact]
    public void Mixed_budget_and_incomplete_truncation_uses_full_recovery()
    {
        var result = ContextBudgeter.Apply(
            [
                ContextSection<string>.CreateObserved(
                    "declaration",
                    order: 0,
                    value: "partial",
                    emittedText: "ab",
                    knownTotalCharacters: 4),
                ContextSection<string>.Create(
                    "document",
                    order: 1,
                    value: "body",
                    emittedText: "body"),
            ],
            ContextBudget.Resolve(2),
            maximum => $"Run with {maximum}",
            "Run `dnaxi context symbol Example.Widget --full`");

        Assert.Equal(["declaration"], SectionNames(result));
        Assert.Equal(["declaration", "document"], result.OmittedSections);
        Assert.Equal(8, result.TotalCharacters);
        Assert.True(result.Truncated);
        Assert.Equal(
            "Run `dnaxi context symbol Example.Widget --full`",
            result.RetrievalCommand);
    }

    [Fact]
    public void Invalid_utf16_is_rejected_before_section_creation()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ContextSection<string>.Create(
                "document",
                order: 0,
                value: "invalid",
                emittedText: "\ud800"));

        Assert.Equal("emittedText", exception.ParamName);
    }

    [Fact]
    public void Full_and_explicit_budget_options_are_mutually_exclusive()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ContextBudget.Resolve(
                defaultMaximumCharacters: 100,
                explicitMaximumCharacters: 200,
                full: true));

        Assert.Equal("explicitMaximumCharacters", exception.ParamName);
    }

    private static ContextSection<string>[] CreateSections() =>
    [
        ContextSection<string>.Create("document", 2, "body", "body"),
        ContextSection<string>.Create("owner", 1, "owner", "xyz"),
        ContextSection<string>.Create("declaration", 0, "declaration", "A😀"),
    ];

    private static string[] SectionNames(BoundedContext<string> context) =>
        context.Sections.Select(static section => section.Name).ToArray();
}
