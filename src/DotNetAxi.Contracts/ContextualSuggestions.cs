namespace DotNetAxi.Contracts;

public enum SuggestionTokenKind
{
    Literal,
    RuntimeValue,
    Placeholder,
}

public sealed record SuggestionToken
{
    private SuggestionToken(SuggestionTokenKind kind, string value)
    {
        Kind = kind;
        Value = ContractGuards.RequiredText(value, nameof(value));
    }

    public SuggestionTokenKind Kind { get; }

    public string Value { get; }

    public static SuggestionToken Literal(string value) =>
        new(SuggestionTokenKind.Literal, value);

    public static SuggestionToken RuntimeValue(string value) =>
        new(SuggestionTokenKind.RuntimeValue, value);

    public static SuggestionToken Placeholder(string name)
    {
        var value = ContractGuards.RequiredText(name, nameof(name));
        if (value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "Placeholder names may contain only ASCII letters, digits, hyphens, and underscores.",
                nameof(name));
        }

        return new SuggestionToken(
            SuggestionTokenKind.Placeholder,
            value);
    }
}

public sealed record SuggestionTemplate
{
    public SuggestionTemplate(
        int priority,
        IEnumerable<SuggestionToken> tokens)
    {
        if (priority < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priority),
                priority,
                "Suggestion priority cannot be negative.");
        }

        Priority = priority;
        Tokens = ContractGuards.Copy(tokens);
        if (Tokens.Count == 0)
        {
            throw new ArgumentException(
                "A suggestion requires at least one command token.",
                nameof(tokens));
        }

        if (Tokens[0].Kind is not SuggestionTokenKind.Literal)
        {
            throw new ArgumentException(
                "A suggestion command must begin with a literal token.",
                nameof(tokens));
        }
    }

    public int Priority { get; }

    public IReadOnlyList<SuggestionToken> Tokens { get; }
}

public static class ContextualSuggestions
{
    public const int MaximumCount = 3;

    public static IReadOnlyList<ResultSuggestion> Compose(
        IEnumerable<SuggestionTemplate> templates,
        WorkspaceSelectors selectors,
        bool selfContained = false)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(selectors);

        if (selfContained)
        {
            return Array.Empty<ResultSuggestion>();
        }

        var suggestions = templates
            .Select(template =>
            {
                ArgumentNullException.ThrowIfNull(template);
                return new RankedCommand(
                    template.Priority,
                    Render(template, selectors));
            })
            .OrderBy(static suggestion => suggestion.Priority)
            .ThenBy(
                static suggestion => suggestion.Command,
                StringComparer.Ordinal)
            .DistinctBy(
                static suggestion => suggestion.Command,
                StringComparer.Ordinal)
            .Take(MaximumCount)
            .Select(static suggestion => new ResultSuggestion(
                $"Run `{suggestion.Command}`"))
            .ToArray();

        return Array.AsReadOnly(suggestions);
    }

    private static string Render(
        SuggestionTemplate template,
        WorkspaceSelectors selectors)
    {
        var tokens = new List<string>(template.Tokens.Count + 9)
        {
            "dnaxi",
        };
        tokens.AddRange(template.Tokens.Select(Render));
        AddSelector(tokens, "--solution", selectors.Solution);
        AddSelector(tokens, "--project", selectors.Project);
        AddSelector(tokens, "--configuration", selectors.Configuration);
        AddSelector(tokens, "--framework", selectors.Framework);
        return string.Join(' ', tokens);
    }

    private static string Render(SuggestionToken token) =>
        token.Kind switch
        {
            SuggestionTokenKind.Literal => QuoteWhenNeeded(token.Value),
            SuggestionTokenKind.RuntimeValue => QuoteWhenNeeded(token.Value),
            SuggestionTokenKind.Placeholder =>
                Quote($"<{token.Value}>"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(token),
                token.Kind,
                "The suggestion token kind is not defined."),
        };

    private static void AddSelector(
        ICollection<string> tokens,
        string flag,
        string? value)
    {
        if (value is null)
        {
            return;
        }

        tokens.Add(flag);
        tokens.Add(QuoteWhenNeeded(value));
    }

    private static string QuoteWhenNeeded(string value) =>
        value.All(IsSafeCharacter)
            ? value
            : Quote(value);

    private static bool IsSafeCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) ||
        character is '-' or '_' or '.' or '/' or ':' or '@' or '+' or '=' or ',';

    private static string Quote(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private sealed record RankedCommand(int Priority, string Command);
}
