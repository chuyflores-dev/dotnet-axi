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
    private static readonly string[] ManagedSelectorFlags =
    [
        "--solution",
        "--project",
        "--configuration",
        "--framework",
    ];

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

        if (Tokens.Any(static token => IsManagedSelector(token.Value)))
        {
            throw new ArgumentException(
                "Suggestion templates cannot contain workspace selector flags; the composer owns fixed scope.",
                nameof(tokens));
        }
    }

    public int Priority { get; }

    public IReadOnlyList<SuggestionToken> Tokens { get; }

    private static bool IsManagedSelector(string value) =>
        ManagedSelectorFlags.Any(flag =>
            value.Equals(flag, StringComparison.Ordinal) ||
            value.StartsWith($"{flag}=", StringComparison.Ordinal));
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
                return new RankedInvocation(
                    template.Priority,
                    ComposeArguments(template, selectors));
            })
            .OrderBy(static suggestion => suggestion.Priority)
            .ThenBy(
                static suggestion => suggestion.Arguments,
                SuggestionArgumentsComparer.Instance)
            .DistinctBy(
                static suggestion => suggestion.Arguments,
                SuggestionArgumentsComparer.Instance)
            .Take(MaximumCount)
            .Select(static suggestion => new ResultSuggestion(
                "dnaxi",
                suggestion.Arguments))
            .ToArray();

        return Array.AsReadOnly(suggestions);
    }

    private static IReadOnlyList<string> ComposeArguments(
        SuggestionTemplate template,
        WorkspaceSelectors selectors)
    {
        var tokens = new List<string>(template.Tokens.Count + 8);
        tokens.AddRange(template.Tokens.Select(RenderToken));
        AddSelector(tokens, "--solution", selectors.Solution);
        AddSelector(tokens, "--project", selectors.Project);
        AddSelector(tokens, "--configuration", selectors.Configuration);
        AddSelector(tokens, "--framework", selectors.Framework);
        return Array.AsReadOnly(tokens.ToArray());
    }

    private static string RenderToken(SuggestionToken token) =>
        token.Kind switch
        {
            SuggestionTokenKind.Literal => token.Value,
            SuggestionTokenKind.RuntimeValue => token.Value,
            SuggestionTokenKind.Placeholder =>
                $"<{token.Value}>",
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
        tokens.Add(value);
    }

    private sealed record RankedInvocation(
        int Priority,
        IReadOnlyList<string> Arguments);

    private sealed class SuggestionArgumentsComparer :
        IComparer<IReadOnlyList<string>>,
        IEqualityComparer<IReadOnlyList<string>>
    {
        public static SuggestionArgumentsComparer Instance { get; } = new();

        public int Compare(
            IReadOnlyList<string>? left,
            IReadOnlyList<string>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            for (var index = 0;
                 index < Math.Min(left.Count, right.Count);
                 index++)
            {
                var tokenComparison = string.Compare(
                    left[index],
                    right[index],
                    StringComparison.Ordinal);
                if (tokenComparison != 0)
                {
                    return tokenComparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        public bool Equals(
            IReadOnlyList<string>? left,
            IReadOnlyList<string>? right) =>
            Compare(left, right) == 0;

        public int GetHashCode(IReadOnlyList<string> value)
        {
            var hash = new HashCode();
            foreach (var token in value)
            {
                hash.Add(token, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
