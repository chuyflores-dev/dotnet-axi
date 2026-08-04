using System.Buffers;
using System.Text;

namespace DotNetAxi.Contracts;

public sealed class BoundedCollection<T>
{
    private BoundedCollection(
        IReadOnlyList<T> items,
        int? total,
        bool truncated,
        string? retrievalCommand)
    {
        Items = items;
        Total = total;
        Truncated = truncated;
        RetrievalCommand = retrievalCommand;
    }

    public int Count => Items.Count;

    public bool TotalKnown => Total is not null;

    public int? Total { get; }

    public int? Omitted => Total - Count;

    public bool Truncated { get; }

    public string? RetrievalCommand { get; }

    public IReadOnlyList<T> Items { get; }

    public static BoundedCollection<T> Create(
        IEnumerable<T> orderedItems,
        int limit,
        int? knownTotal = null,
        string? retrievalCommand = null)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                "The output limit cannot be negative.");
        }

        if (knownTotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(knownTotal),
                knownTotal,
                "The known total cannot be negative.");
        }

        var items = new List<T>(Math.Min(limit, 256));
        bool hasAdditional;

        using (var enumerator = orderedItems.GetEnumerator())
        {
            while (items.Count < limit && enumerator.MoveNext())
            {
                items.Add(RequiredItem(enumerator.Current));
            }

            hasAdditional = enumerator.MoveNext();
            if (hasAdditional)
            {
                _ = RequiredItem(enumerator.Current);
            }
        }

        if (knownTotal < items.Count ||
            (hasAdditional && knownTotal == items.Count))
        {
            throw new ArgumentException(
                "The known total cannot be smaller than the observed items.",
                nameof(knownTotal));
        }

        var total = knownTotal ?? (hasAdditional ? null : items.Count);
        var truncated = hasAdditional || total > items.Count;
        var command = ContractGuards.OptionalText(
            retrievalCommand,
            nameof(retrievalCommand));

        if (truncated && command is null)
        {
            throw new ArgumentException(
                "Truncated output requires a concrete retrieval command.",
                nameof(retrievalCommand));
        }

        return new BoundedCollection<T>(
            Array.AsReadOnly(items.ToArray()),
            total,
            truncated,
            truncated ? command : null);
    }

    /// <summary>Creates a bounded collection from an engine observation.</summary>
    public static BoundedCollection<T> FromObserved(
        IEnumerable<T> items,
        int? total,
        bool totalKnown,
        string? retrievalCommand = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copied = Array.AsReadOnly(items.Select(RequiredItem).ToArray());
        if (total < 0 || (totalKnown && total is null) || (total is not null && total < copied.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }

        var truncated = !totalKnown || total > copied.Count;
        var command = ContractGuards.OptionalText(retrievalCommand, nameof(retrievalCommand));
        if (truncated && command is null)
        {
            throw new ArgumentException("Truncated output requires a concrete retrieval command.", nameof(retrievalCommand));
        }

        return new BoundedCollection<T>(copied, totalKnown ? total : null, truncated, truncated ? command : null);
    }

    private static T RequiredItem(T item)
    {
        if (item is null)
        {
            throw new ArgumentException(
                "Output collections cannot contain null items.",
                "orderedItems");
        }

        return item;
    }
}

public sealed class BoundedText
{
    private BoundedText(
        string preview,
        int includedCharacters,
        int totalCharacters,
        string? retrievalCommand)
    {
        Preview = preview;
        IncludedCharacters = includedCharacters;
        TotalCharacters = totalCharacters;
        RetrievalCommand = retrievalCommand;
    }

    public string Preview { get; }

    public int IncludedCharacters { get; }

    public int TotalCharacters { get; }

    public int OmittedCharacters =>
        TotalCharacters - IncludedCharacters;

    public bool Truncated =>
        OmittedCharacters > 0;

    public string? RetrievalCommand { get; }

    public static BoundedText Create(
        string text,
        int maxCharacters,
        string? retrievalCommand = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                maxCharacters,
                "The character limit cannot be negative.");
        }

        var preview = new StringBuilder();
        var included = 0;
        var total = 0;
        var remaining = text.AsSpan();

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(
                remaining,
                out var rune,
                out var consumed);
            if (status is not OperationStatus.Done)
            {
                throw new ArgumentException(
                    "Text cannot contain invalid UTF-16.",
                    nameof(text));
            }

            if (included < maxCharacters)
            {
                preview.Append(rune);
                included++;
            }

            total++;
            remaining = remaining[consumed..];
        }

        var truncated = included < total;
        var command = ContractGuards.OptionalText(
            retrievalCommand,
            nameof(retrievalCommand));
        if (truncated && command is null)
        {
            throw new ArgumentException(
                "Truncated text requires a concrete retrieval command.",
                nameof(retrievalCommand));
        }

        return new BoundedText(
            preview.ToString(),
            included,
            total,
            truncated ? command : null);
    }
}
