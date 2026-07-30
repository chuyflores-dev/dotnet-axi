using DotNetAxi.Contracts;

namespace DotNetAxi.Validation;

public sealed record ValidationCheck
{
    public ValidationCheck(string name, OperationPolicy policy)
    {
        Name = RequiredText(name, nameof(name));
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
    }

    public string Name { get; }

    public OperationPolicy Policy { get; }

    private static string RequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A non-empty value is required.",
                parameterName);
        }

        return value;
    }
}

public sealed class ValidationCheckRegistry
{
    private readonly Dictionary<string, ValidationCheck> _checks =
        new(StringComparer.Ordinal);

    public IReadOnlyList<ValidationCheck> Checks =>
        Array.AsReadOnly(_checks.Values.ToArray());

    public void Add(ValidationCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (check.Policy.MayWriteSource)
        {
            throw new ArgumentException(
                "Validation checks cannot write source files.",
                nameof(check));
        }

        if (!_checks.TryAdd(check.Name, check))
        {
            throw new InvalidOperationException(
                $"Validation check '{check.Name}' is already registered.");
        }
    }

    public ValidationCheck Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A non-empty value is required.",
                nameof(name));
        }

        var requiredName = name;
        return _checks.TryGetValue(requiredName, out var check)
            ? check
            : throw new KeyNotFoundException(
                $"Validation check '{requiredName}' is not registered.");
    }
}
