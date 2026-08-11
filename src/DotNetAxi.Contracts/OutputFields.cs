using System.Collections.ObjectModel;

namespace DotNetAxi.Contracts;

public static class OutputFieldSelection
{
    public static IReadOnlyList<string> Parse(
        IEnumerable<string>? requestedFields)
    {
        if (requestedFields is null)
        {
            return Array.Empty<string>();
        }

        return Array.AsReadOnly(
            requestedFields
                .SelectMany(static value => (value ?? string.Empty).Split(','))
                .Select(static field => field.Trim())
                .ToArray());
    }

    public static string CanonicalValue(
        IEnumerable<string> requestedFields)
    {
        ArgumentNullException.ThrowIfNull(requestedFields);

        return string.Join(
            ',',
            Parse(requestedFields).Distinct(StringComparer.Ordinal));
    }
}

public sealed class OutputField<T>
{
    public OutputField(
        string name,
        Func<T, object?> valueSelector,
        bool includedByDefault = false)
    {
        Name = ContractGuards.RequiredText(name, nameof(name));
        ValueSelector = valueSelector
            ?? throw new ArgumentNullException(nameof(valueSelector));
        IncludedByDefault = includedByDefault;
    }

    public string Name { get; }

    public Func<T, object?> ValueSelector { get; }

    public bool IncludedByDefault { get; }
}

public sealed class OutputFieldSet<T>
{
    private readonly IReadOnlyList<OutputField<T>> _fields;
    private readonly IReadOnlyDictionary<string, OutputField<T>> _fieldsByName;

    public OutputFieldSet(IEnumerable<OutputField<T>> fields)
    {
        _fields = ContractGuards.Copy(fields);
        if (_fields.Count == 0)
        {
            throw new ArgumentException(
                "At least one output field is required.",
                nameof(fields));
        }

        var duplicate = _fields
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Output field '{duplicate.Key}' is declared more than once.",
                nameof(fields));
        }

        if (!_fields.Any(field => field.IncludedByDefault))
        {
            throw new ArgumentException(
                "At least one output field must be included by default.",
                nameof(fields));
        }

        _fieldsByName = new ReadOnlyDictionary<string, OutputField<T>>(
            _fields.ToDictionary(field => field.Name, StringComparer.Ordinal));
        AvailableFields = Array.AsReadOnly(
            _fields.Select(field => field.Name).ToArray());
        DefaultFields = Array.AsReadOnly(
            _fields
                .Where(field => field.IncludedByDefault)
                .Select(field => field.Name)
                .ToArray());
    }

    public IReadOnlyList<string> AvailableFields { get; }

    public IReadOnlyList<string> DefaultFields { get; }

    public OutputFieldSelection<T> Select(
        IEnumerable<string>? requestedFields = null)
    {
        var requested = OutputFieldSelection.Parse(requestedFields)
            .Select(field => ContractGuards.RequiredText(
                field,
                nameof(requestedFields)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknown = requested
            .Where(field => !_fieldsByName.ContainsKey(field))
            .ToArray();

        if (unknown.Length > 0)
        {
            throw new UnknownOutputFieldsException(
                unknown,
                AvailableFields);
        }

        var requestedSet = requested.ToHashSet(StringComparer.Ordinal);
        var selected = _fields
            .Where(field =>
                field.IncludedByDefault ||
                requestedSet.Contains(field.Name))
            .ToArray();

        return new OutputFieldSelection<T>(selected);
    }
}

public sealed class OutputFieldSelection<T>
{
    private readonly IReadOnlyList<OutputField<T>> _fields;

    internal OutputFieldSelection(IEnumerable<OutputField<T>> fields)
    {
        _fields = ContractGuards.Copy(fields);
        Fields = Array.AsReadOnly(
            _fields.Select(field => field.Name).ToArray());
    }

    public IReadOnlyList<string> Fields { get; }

    public IReadOnlyDictionary<string, object?> Project(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var projection = new Dictionary<string, object?>(
            _fields.Count,
            StringComparer.Ordinal);

        foreach (var field in _fields)
        {
            projection.Add(field.Name, field.ValueSelector(value));
        }

        return new DeclaredOrderDictionary<object?>(projection);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Project(
        IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return Array.AsReadOnly(
            values.Select(Project).ToArray());
    }
}

internal interface IHasDeclaredOutputOrder
{
}

internal sealed class DeclaredOrderDictionary<TValue> :
    ReadOnlyDictionary<string, TValue>,
    IHasDeclaredOutputOrder
{
    public DeclaredOrderDictionary(IDictionary<string, TValue> dictionary)
        : base(dictionary)
    {
    }
}

public sealed class UnknownOutputFieldsException : ArgumentException
{
    public UnknownOutputFieldsException(
        IEnumerable<string> unknownFields,
        IEnumerable<string> availableFields)
        : this(CreateDetails(unknownFields, availableFields))
    {
    }

    private UnknownOutputFieldsException(FieldErrorDetails details)
        : base(
            CreateMessage(details.UnknownFields),
            "requestedFields")
    {
        UnknownFields = details.UnknownFields;
        AvailableFields = details.AvailableFields;
    }

    public IReadOnlyList<string> UnknownFields { get; }

    public IReadOnlyList<string> AvailableFields { get; }

    private static string CreateMessage(IReadOnlyList<string> unknownFields) =>
        unknownFields.Count == 1
            ? $"Unknown output field '{unknownFields[0]}'."
            : $"Unknown output fields: {string.Join(", ", unknownFields)}.";

    private static FieldErrorDetails CreateDetails(
        IEnumerable<string> unknownFields,
        IEnumerable<string> availableFields)
    {
        var unknown = CopyNames(unknownFields, nameof(unknownFields));
        if (unknown.Count == 0)
        {
            throw new ArgumentException(
                "At least one unknown field is required.",
                nameof(unknownFields));
        }

        return new FieldErrorDetails(
            unknown,
            CopyNames(availableFields, nameof(availableFields)));
    }

    private static IReadOnlyList<string> CopyNames(
        IEnumerable<string> names,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(names);
        return Array.AsReadOnly(
            names
                .Select(name => ContractGuards.RequiredText(
                    name,
                    parameterName))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private sealed record FieldErrorDetails(
        IReadOnlyList<string> UnknownFields,
        IReadOnlyList<string> AvailableFields);
}
