using System.Buffers;
using System.Text;

namespace DotNetAxi.Contracts;

public enum ContextBudgetMode
{
    Default,
    Configured,
    Explicit,
    Full,
}

public sealed class ContextBudget
{
    private ContextBudget(ContextBudgetMode mode, int? maximumCharacters)
    {
        Mode = mode;
        MaximumCharacters = maximumCharacters;
    }

    public ContextBudgetMode Mode { get; }

    public int? MaximumCharacters { get; }

    public static ContextBudget Resolve(
        int defaultMaximumCharacters,
        int? configuredMaximumCharacters = null,
        int? explicitMaximumCharacters = null,
        bool full = false)
    {
        ValidateMaximum(
            defaultMaximumCharacters,
            nameof(defaultMaximumCharacters));
        ValidateMaximum(
            configuredMaximumCharacters,
            nameof(configuredMaximumCharacters));
        ValidateMaximum(
            explicitMaximumCharacters,
            nameof(explicitMaximumCharacters));

        if (full && explicitMaximumCharacters is not null)
        {
            throw new ArgumentException(
                "A full context request cannot also specify a character budget.",
                nameof(explicitMaximumCharacters));
        }

        if (full)
        {
            return new ContextBudget(ContextBudgetMode.Full, null);
        }

        if (explicitMaximumCharacters is { } explicitMaximum)
        {
            return new ContextBudget(
                ContextBudgetMode.Explicit,
                explicitMaximum);
        }

        if (configuredMaximumCharacters is { } configuredMaximum)
        {
            return new ContextBudget(
                ContextBudgetMode.Configured,
                configuredMaximum);
        }

        return new ContextBudget(
            ContextBudgetMode.Default,
            defaultMaximumCharacters);
    }

    private static void ValidateMaximum(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A context character budget cannot be negative.");
        }
    }
}

public sealed class ContextSection<T>
{
    private ContextSection(
        string name,
        int order,
        T value,
        int includedCharacters,
        int? totalCharacters)
    {
        Name = name;
        Order = order;
        Value = value;
        IncludedCharacters = includedCharacters;
        TotalCharacters = totalCharacters;
    }

    public string Name { get; }

    public int Order { get; }

    public T Value { get; }

    public int IncludedCharacters { get; }

    public bool TotalKnown => TotalCharacters is not null;

    public int? TotalCharacters { get; }

    public bool Complete =>
        TotalCharacters == IncludedCharacters;

    public static ContextSection<T> Create(
        string name,
        int order,
        T value,
        string emittedText)
    {
        var includedCharacters = CountUnicodeScalars(
            emittedText,
            nameof(emittedText));
        return CreateCore(
            name,
            order,
            value,
            includedCharacters,
            includedCharacters);
    }

    public static ContextSection<T> CreateObserved(
        string name,
        int order,
        T value,
        string emittedText,
        int? knownTotalCharacters)
    {
        var includedCharacters = CountUnicodeScalars(
            emittedText,
            nameof(emittedText));
        return CreateCore(
            name,
            order,
            value,
            includedCharacters,
            knownTotalCharacters);
    }

    private static ContextSection<T> CreateCore(
        string name,
        int order,
        T value,
        int includedCharacters,
        int? knownTotalCharacters)
    {
        var validatedName = ContractGuards.RequiredText(name, nameof(name));
        if (order < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order),
                order,
                "A context section order cannot be negative.");
        }

        if (value is null)
        {
            throw new ArgumentException(
                "A context section value cannot be null.",
                nameof(value));
        }

        if (knownTotalCharacters < includedCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(knownTotalCharacters),
                knownTotalCharacters,
                "A known section total cannot be smaller than its emitted text.");
        }

        return new ContextSection<T>(
            validatedName,
            order,
            value,
            includedCharacters,
            knownTotalCharacters);
    }

    private static int CountUnicodeScalars(string text, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(text, parameterName);
        var count = 0;
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(
                remaining,
                out _,
                out var consumed);
            if (status is not OperationStatus.Done)
            {
                throw new ArgumentException(
                    "Context section text cannot contain invalid UTF-16.",
                    parameterName);
            }

            count = checked(count + 1);
            remaining = remaining[consumed..];
        }

        return count;
    }
}

public sealed record ApproximateTokenRange(long Minimum, long Maximum);

public sealed class BoundedContext<T>
{
    internal BoundedContext(
        ContextBudget budget,
        IReadOnlyList<ContextSection<T>> sections,
        long includedCharacters,
        long? totalCharacters,
        IReadOnlyList<string> omittedSections,
        ApproximateTokenRange approximateTokens,
        string? retrievalCommand)
    {
        BudgetMode = budget.Mode;
        MaximumCharacters = budget.MaximumCharacters;
        Sections = sections;
        IncludedCharacters = includedCharacters;
        TotalCharacters = totalCharacters;
        OmittedSections = omittedSections;
        ApproximateTokens = approximateTokens;
        RetrievalCommand = retrievalCommand;
    }

    public ContextBudgetMode BudgetMode { get; }

    public int? MaximumCharacters { get; }

    public IReadOnlyList<ContextSection<T>> Sections { get; }

    public long IncludedCharacters { get; }

    public bool TotalKnown => TotalCharacters is not null;

    public long? TotalCharacters { get; }

    public long? OmittedCharacters =>
        TotalCharacters - IncludedCharacters;

    public IReadOnlyList<string> OmittedSections { get; }

    public ApproximateTokenRange ApproximateTokens { get; }

    public bool Truncated => OmittedSections.Count > 0;

    public string? RetrievalCommand { get; }
}

public static class ContextBudgeter
{
    public static BoundedContext<T> Apply<T>(
        IEnumerable<ContextSection<T>> sections,
        ContextBudget budget,
        Func<int, string>? largerBudgetCommand = null,
        string? fullRetrievalCommand = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(budget);
        var ordered = sections
            .Select(RequiredSection)
            .OrderBy(static section => section.Order)
            .ThenBy(static section => section.Name, StringComparer.Ordinal)
            .ToArray();
        EnsureUniqueNames(ordered);

        var selected = new List<ContextSection<T>>(ordered.Length);
        var omitted = new HashSet<string>(StringComparer.Ordinal);
        long includedCharacters = 0;
        var omittedByBudget = false;
        foreach (var section in ordered)
        {
            var fits = budget.MaximumCharacters is null
                || includedCharacters + section.IncludedCharacters
                    <= budget.MaximumCharacters.Value;
            if (fits)
            {
                selected.Add(section);
                includedCharacters = checked(
                    includedCharacters + section.IncludedCharacters);
            }
            else
            {
                omitted.Add(section.Name);
                omittedByBudget = true;
            }

            if (!section.Complete)
            {
                omitted.Add(section.Name);
            }
        }

        var totalKnown = ordered.All(static section => section.TotalKnown);
        var allSectionsComplete = ordered.All(static section => section.Complete);
        long? totalCharacters = totalKnown
            ? ordered.Aggregate(
                0L,
                static (total, section) => checked(
                    total + section.TotalCharacters!.Value))
            : null;
        var omittedSections = Array.AsReadOnly(
            ordered
                .Where(section => omitted.Contains(section.Name))
                .Select(static section => section.Name)
                .ToArray());
        var truncated = omittedSections.Count > 0;
        var retrievalCommand = truncated
            ? RetrievalCommand(
                budget,
                omittedByBudget && allSectionsComplete,
                totalCharacters,
                largerBudgetCommand,
                fullRetrievalCommand)
            : null;

        return new BoundedContext<T>(
            budget,
            Array.AsReadOnly(selected.ToArray()),
            includedCharacters,
            totalCharacters,
            omittedSections,
            ApproximateTokens(includedCharacters),
            retrievalCommand);
    }

    private static string RetrievalCommand(
        ContextBudget budget,
        bool budgetOnlyTruncation,
        long? totalCharacters,
        Func<int, string>? largerBudgetCommand,
        string? fullRetrievalCommand)
    {
        if (budgetOnlyTruncation
            && budget.Mode is not ContextBudgetMode.Full
            && totalCharacters is >= 0 and <= int.MaxValue
            && largerBudgetCommand is not null)
        {
            var command = ContractGuards.OptionalText(
                largerBudgetCommand(checked((int)totalCharacters.Value)),
                nameof(largerBudgetCommand));
            if (command is not null)
            {
                return command;
            }
        }

        var fullCommand = ContractGuards.OptionalText(
            fullRetrievalCommand,
            nameof(fullRetrievalCommand));
        if (fullCommand is null)
        {
            throw new ArgumentException(
                "Truncated context requires a concrete full or larger-budget command.",
                nameof(fullRetrievalCommand));
        }

        return fullCommand;
    }

    private static ApproximateTokenRange ApproximateTokens(
        long includedCharacters) =>
        new(
            DivideCeiling(includedCharacters, 6),
            DivideCeiling(includedCharacters, 2));

    private static long DivideCeiling(long value, long divisor) =>
        value == 0 ? 0 : ((value - 1) / divisor) + 1;

    private static ContextSection<T> RequiredSection<T>(
        ContextSection<T> section)
    {
        if (section is null)
        {
            throw new ArgumentException(
                "Context sections cannot contain null values.",
                "sections");
        }

        return section;
    }

    private static void EnsureUniqueNames<T>(
        IReadOnlyCollection<ContextSection<T>> sections)
    {
        if (sections
            .GroupBy(static section => section.Name, StringComparer.Ordinal)
            .Any(static group => group.Skip(1).Any()))
        {
            throw new ArgumentException(
                "Context section names must be unique.",
                nameof(sections));
        }
    }
}
