namespace DotNetAxi.Contracts;

public sealed record EvidenceOrderKey
{
    public EvidenceOrderKey(
        string? path,
        int? line,
        int? column,
        string? kind,
        string? fullyQualifiedName,
        string stableId)
    {
        NormalizedPath = NormalizePath(path);
        Line = Positive(line, nameof(line));
        Column = Positive(column, nameof(column));
        Kind = ContractGuards.OptionalText(kind, nameof(kind));
        FullyQualifiedName = ContractGuards.OptionalText(
            fullyQualifiedName,
            nameof(fullyQualifiedName));
        StableId = ContractGuards.RequiredText(stableId, nameof(stableId));
    }

    public string? NormalizedPath { get; }

    public int? Line { get; }

    public int? Column { get; }

    public string? Kind { get; }

    public string? FullyQualifiedName { get; }

    public string StableId { get; }

    private static string? NormalizePath(string? path)
    {
        var value = ContractGuards.OptionalText(path, nameof(path));
        return value?.Replace('\\', '/');
    }

    private static int? Positive(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Locations are one-based when supplied.");
        }

        return value;
    }
}

public sealed class EvidenceOrderKeyComparer : IComparer<EvidenceOrderKey>
{
    public static EvidenceOrderKeyComparer Ordinal { get; } = new();

    private EvidenceOrderKeyComparer()
    {
    }

    public int Compare(EvidenceOrderKey? x, EvidenceOrderKey? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        var comparison = CompareText(x.NormalizedPath, y.NormalizedPath);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumber(x.Line, y.Line);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumber(x.Column, y.Column);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareText(x.Kind, y.Kind);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareText(
            x.FullyQualifiedName,
            y.FullyQualifiedName);
        if (comparison != 0)
        {
            return comparison;
        }

        return StringComparer.Ordinal.Compare(x.StableId, y.StableId);
    }

    private static int CompareText(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        return StringComparer.Ordinal.Compare(x, y);
    }

    private static int CompareNumber(int? x, int? y)
    {
        if (x == y)
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        return x.Value.CompareTo(y.Value);
    }
}

public static class OutputOrdering
{
    public static IOrderedEnumerable<T> ByEvidence<T>(
        IEnumerable<T> values,
        Func<T, EvidenceOrderKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(keySelector);

        return values.OrderBy(keySelector, EvidenceOrderKeyComparer.Ordinal);
    }
}
