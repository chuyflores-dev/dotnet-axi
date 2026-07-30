namespace DotNetAxi.Contracts;

internal static class ContractGuards
{
    public static string RequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A non-empty value is required.",
                parameterName);
        }

        return value;
    }

    public static string? OptionalText(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "When supplied, the value cannot be empty.",
                parameterName);
        }

        return value;
    }

    public static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values)
    {
        if (values is null)
        {
            return Array.Empty<T>();
        }

        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Collections cannot contain null values.",
                nameof(values));
        }

        return Array.AsReadOnly(copy);
    }

    public static IReadOnlyList<string> CopyText(
        IEnumerable<string>? values,
        string parameterName)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        var copy = values
            .Select(value => RequiredText(value, parameterName))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return Array.AsReadOnly(copy);
    }
}
