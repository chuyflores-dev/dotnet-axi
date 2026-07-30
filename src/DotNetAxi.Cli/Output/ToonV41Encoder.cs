using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetAxi.Cli.Output;

internal static class ToonV41Encoder
{
    private const char Delimiter = ',';
    private const int IndentSize = 2;

    public static string Encode(JsonNode? root)
    {
        var writer = new Writer();
        writer.WriteRoot(root);
        return writer.ToString();
    }

    public static void ValidateUnicode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length ||
                    !char.IsLowSurrogate(value[index + 1]))
                {
                    throw new ArgumentException(
                        "TOON strings cannot contain unpaired UTF-16 surrogates.",
                        nameof(value));
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(character))
            {
                throw new ArgumentException(
                    "TOON strings cannot contain unpaired UTF-16 surrogates.",
                    nameof(value));
            }
        }
    }

    private static bool IsPrimitive(JsonNode? node) =>
        node is null ||
        node.GetValueKind() is
            JsonValueKind.String or
            JsonValueKind.Number or
            JsonValueKind.True or
            JsonValueKind.False or
            JsonValueKind.Null;

    private static string EncodePrimitive(
        JsonNode? node,
        char activeDelimiter = Delimiter)
    {
        if (node is null || node.GetValueKind() is JsonValueKind.Null)
        {
            return "null";
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.String => EncodeString(
                node.GetValue<string>(),
                activeDelimiter),
            JsonValueKind.Number => CanonicalizeNumber(node.ToJsonString()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new ArgumentException(
                "Only JSON primitives can be encoded as TOON values.",
                nameof(node)),
        };
    }

    private static string EncodeString(string value, char activeDelimiter)
    {
        ValidateUnicode(value);

        if (!RequiresQuotes(value, activeDelimiter))
        {
            return value;
        }

        var output = new StringBuilder(value.Length + 2);
        output.Append('"');

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '\\':
                    output.Append(@"\\");
                    break;
                case '"':
                    output.Append("\\\"");
                    break;
                case '\n':
                    output.Append(@"\n");
                    break;
                case '\r':
                    output.Append(@"\r");
                    break;
                case '\t':
                    output.Append(@"\t");
                    break;
                default:
                    if (character <= '\u001f')
                    {
                        output.Append(@"\u");
                        output.Append(
                            ((int)character).ToString(
                                "x4",
                                CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        output.Append(character);
                    }

                    break;
            }
        }

        output.Append('"');
        return output.ToString();
    }

    private static bool RequiresQuotes(string value, char activeDelimiter)
    {
        if (value.Length == 0 ||
            value[0] is ' ' or '\t' or '-' or '#' ||
            value[^1] is ' ' or '\t' ||
            value is "true" or "false" or "null" ||
            IsNumericLike(value))
        {
            return true;
        }

        foreach (var character in value)
        {
            if (character <= '\u001f' ||
                character == activeDelimiter ||
                character is ':' or '"' or '\\' or '[' or ']' or '{' or '}')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNumericLike(string value)
    {
        var index = 0;
        if (value[index] is '+' or '-')
        {
            index++;
        }

        if (!ConsumeDigits(value, ref index))
        {
            return false;
        }

        if (index < value.Length && value[index] == '.')
        {
            index++;
            if (!ConsumeDigits(value, ref index))
            {
                return false;
            }
        }

        if (index < value.Length && value[index] is 'e' or 'E')
        {
            index++;
            if (index < value.Length && value[index] is '+' or '-')
            {
                index++;
            }

            if (!ConsumeDigits(value, ref index))
            {
                return false;
            }
        }

        return index == value.Length;
    }

    private static bool ConsumeDigits(string value, ref int index)
    {
        var start = index;
        while (index < value.Length && value[index] is >= '0' and <= '9')
        {
            index++;
        }

        return index > start;
    }

    private static string EncodeKey(string key)
    {
        ValidateUnicode(key);

        if (IsUnquotedKey(key))
        {
            return key;
        }

        return EncodeQuoted(key);
    }

    private static bool IsUnquotedKey(string key)
    {
        if (key.Length == 0 ||
            !IsAsciiLetterOrUnderscore(key[0]))
        {
            return false;
        }

        for (var index = 1; index < key.Length; index++)
        {
            var character = key[index];
            if (!IsAsciiLetterOrUnderscore(character) &&
                character is not (>= '0' and <= '9') &&
                character != '.')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrUnderscore(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static string EncodeQuoted(string value)
    {
        var encoded = EncodeString(value, Delimiter);
        if (encoded.Length > 0 && encoded[0] == '"')
        {
            return encoded;
        }

        var output = new StringBuilder(value.Length + 2);
        output.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    output.Append(@"\\");
                    break;
                case '"':
                    output.Append("\\\"");
                    break;
                case '\n':
                    output.Append(@"\n");
                    break;
                case '\r':
                    output.Append(@"\r");
                    break;
                case '\t':
                    output.Append(@"\t");
                    break;
                default:
                    if (character <= '\u001f')
                    {
                        output.Append(@"\u");
                        output.Append(
                            ((int)character).ToString(
                                "x4",
                                CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        output.Append(character);
                    }

                    break;
            }
        }

        output.Append('"');
        return output.ToString();
    }

    private static string CanonicalizeNumber(string value)
    {
        var negative = value[0] == '-';
        var unsigned = negative ? value[1..] : value;
        var exponentSeparator = unsigned.IndexOfAny(['e', 'E']);
        var mantissa = exponentSeparator >= 0
            ? unsigned[..exponentSeparator]
            : unsigned;
        var exponent = exponentSeparator >= 0
            ? int.Parse(
                unsigned[(exponentSeparator + 1)..],
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture)
            : 0;
        var decimalSeparator = mantissa.IndexOf('.');
        var decimalPosition = (
            decimalSeparator >= 0
                ? decimalSeparator
                : mantissa.Length) + exponent;
        var digits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);

        string expanded;
        if (decimalPosition <= 0)
        {
            expanded = $"0.{new string('0', -decimalPosition)}{digits}";
        }
        else if (decimalPosition >= digits.Length)
        {
            expanded = $"{digits}{new string('0', decimalPosition - digits.Length)}";
        }
        else
        {
            expanded = string.Concat(
                digits.AsSpan(0, decimalPosition),
                ".",
                digits.AsSpan(decimalPosition));
        }

        var parts = expanded.Split('.', count: 2);
        var integer = parts[0].TrimStart('0');
        integer = integer.Length == 0 ? "0" : integer;
        var fraction = parts.Length == 2
            ? parts[1].TrimEnd('0')
            : string.Empty;
        var isZero = integer == "0" &&
            (fraction.Length == 0 || fraction.All(character => character == '0'));
        var sign = negative && !isZero ? "-" : string.Empty;

        return fraction.Length == 0
            ? $"{sign}{integer}"
            : $"{sign}{integer}.{fraction}";
    }

    private sealed record FieldShape(
        string Name,
        IReadOnlyList<FieldShape>? Children);

    private sealed class Writer
    {
        private readonly List<string> _lines = [];

        public override string ToString() => string.Join('\n', _lines);

        public void WriteRoot(JsonNode? node)
        {
            if (node is null || IsPrimitive(node))
            {
                AddLine(0, EncodePrimitive(node));
                return;
            }

            if (node is JsonObject objectNode)
            {
                if (TryGetKeyedShape(objectNode, out var keyedFields))
                {
                    WriteKeyedTable(
                        key: null,
                        objectNode,
                        keyedFields,
                        lineDepth: 0,
                        rowDepth: 1,
                        prefix: string.Empty);
                }
                else
                {
                    WriteObject(objectNode, depth: 0);
                }

                return;
            }

            if (node is JsonArray arrayNode)
            {
                WriteRootArray(arrayNode);
                return;
            }

            throw new ArgumentException(
                "The value cannot be represented by the TOON JSON data model.",
                nameof(node));
        }

        private void WriteObject(JsonObject value, int depth)
        {
            foreach (var property in value)
            {
                WriteProperty(
                    property.Key,
                    property.Value,
                    lineDepth: depth,
                    childDepth: depth + 1,
                    prefix: string.Empty);
            }
        }

        private void WriteProperty(
            string key,
            JsonNode? value,
            int lineDepth,
            int childDepth,
            string prefix)
        {
            var encodedKey = EncodeKey(key);

            if (IsPrimitive(value))
            {
                AddLine(
                    lineDepth,
                    $"{prefix}{encodedKey}: {EncodePrimitive(value)}");
                return;
            }

            if (value is JsonObject objectValue)
            {
                if (TryGetKeyedShape(objectValue, out var keyedFields))
                {
                    WriteKeyedTable(
                        key,
                        objectValue,
                        keyedFields,
                        lineDepth,
                        childDepth,
                        prefix);
                }
                else
                {
                    AddLine(lineDepth, $"{prefix}{encodedKey}:");
                    WriteObject(objectValue, childDepth);
                }

                return;
            }

            if (value is JsonArray arrayValue)
            {
                WriteArrayProperty(
                    key,
                    arrayValue,
                    lineDepth,
                    childDepth,
                    prefix);
                return;
            }

            throw new ArgumentException(
                $"Property '{key}' cannot be represented by the TOON JSON data model.",
                nameof(value));
        }

        private void WriteArrayProperty(
            string key,
            JsonArray value,
            int lineDepth,
            int itemDepth,
            string prefix)
        {
            var encodedKey = EncodeKey(key);

            if (value.Count == 0)
            {
                AddLine(lineDepth, $"{prefix}{encodedKey}: []");
                return;
            }

            if (value.All(IsPrimitive))
            {
                AddLine(
                    lineDepth,
                    $"{prefix}{encodedKey}[{value.Count}]: {JoinPrimitives(value)}");
                return;
            }

            if (TryGetTabularShape(value, out var fields))
            {
                WriteTabularArray(
                    key,
                    value,
                    fields,
                    lineDepth,
                    itemDepth,
                    prefix);
                return;
            }

            AddLine(lineDepth, $"{prefix}{encodedKey}[{value.Count}]:");
            WriteListItems(value, itemDepth);
        }

        private void WriteRootArray(JsonArray value)
        {
            if (value.Count == 0)
            {
                AddLine(0, "[]");
                return;
            }

            if (value.All(IsPrimitive))
            {
                AddLine(0, $"[{value.Count}]: {JoinPrimitives(value)}");
                return;
            }

            if (TryGetTabularShape(value, out var fields))
            {
                WriteTabularArray(
                    key: null,
                    value,
                    fields,
                    lineDepth: 0,
                    rowDepth: 1,
                    prefix: string.Empty);
                return;
            }

            AddLine(0, $"[{value.Count}]:");
            WriteListItems(value, itemDepth: 1);
        }

        private void WriteListItems(JsonArray value, int itemDepth)
        {
            foreach (var item in value)
            {
                if (IsPrimitive(item))
                {
                    AddLine(itemDepth, $"- {EncodePrimitive(item)}");
                    continue;
                }

                if (item is JsonObject objectItem)
                {
                    WriteObjectListItem(objectItem, itemDepth);
                    continue;
                }

                if (item is JsonArray arrayItem)
                {
                    WriteArrayListItem(arrayItem, itemDepth);
                    continue;
                }

                throw new ArgumentException(
                    "An array item cannot be represented by the TOON JSON data model.",
                    nameof(value));
            }
        }

        private void WriteObjectListItem(JsonObject value, int itemDepth)
        {
            var properties = value.ToArray();
            if (properties.Length == 0)
            {
                AddLine(itemDepth, "-");
                return;
            }

            WriteProperty(
                properties[0].Key,
                properties[0].Value,
                lineDepth: itemDepth,
                childDepth: itemDepth + 2,
                prefix: "- ");

            foreach (var property in properties.Skip(1))
            {
                WriteProperty(
                    property.Key,
                    property.Value,
                    lineDepth: itemDepth + 1,
                    childDepth: itemDepth + 2,
                    prefix: string.Empty);
            }
        }

        private void WriteArrayListItem(JsonArray value, int itemDepth)
        {
            if (value.Count == 0)
            {
                AddLine(itemDepth, "- [0]:");
                return;
            }

            if (value.All(IsPrimitive))
            {
                AddLine(
                    itemDepth,
                    $"- [{value.Count}]: {JoinPrimitives(value)}");
                return;
            }

            AddLine(itemDepth, $"- [{value.Count}]:");
            WriteListItems(value, itemDepth + 1);
        }

        private void WriteTabularArray(
            string? key,
            JsonArray value,
            IReadOnlyList<FieldShape> fields,
            int lineDepth,
            int rowDepth,
            string prefix)
        {
            var encodedKey = key is null ? string.Empty : EncodeKey(key);
            AddLine(
                lineDepth,
                $"{prefix}{encodedKey}[{value.Count}]{{{EncodeFields(fields)}}}:");

            foreach (var item in value)
            {
                var cells = new List<string>();
                FlattenRow(
                    item!.AsObject(),
                    fields,
                    cells);
                AddLine(rowDepth, string.Join(Delimiter, cells));
            }
        }

        private void WriteKeyedTable(
            string? key,
            JsonObject value,
            IReadOnlyList<FieldShape> fields,
            int lineDepth,
            int rowDepth,
            string prefix)
        {
            var encodedKey = key is null ? string.Empty : EncodeKey(key);
            AddLine(
                lineDepth,
                $"{prefix}{encodedKey}[{value.Count}:]{{{EncodeFields(fields)}}}:");

            foreach (var entry in value)
            {
                var cells = new List<string>();
                FlattenRow(
                    entry.Value!.AsObject(),
                    fields,
                    cells);
                AddLine(
                    rowDepth,
                    $"{EncodeKey(entry.Key)}: {string.Join(Delimiter, cells)}");
            }
        }

        private static string JoinPrimitives(JsonArray value) =>
            string.Join(
                Delimiter,
                value.Select(item => EncodePrimitive(item)));

        private static string EncodeFields(IReadOnlyList<FieldShape> fields) =>
            string.Join(
                Delimiter,
                fields.Select(field =>
                    field.Children is null
                        ? EncodeKey(field.Name)
                        : $"{EncodeKey(field.Name)}{{{EncodeFields(field.Children)}}}"));

        private static void FlattenRow(
            JsonObject value,
            IReadOnlyList<FieldShape> fields,
            List<string> cells)
        {
            foreach (var field in fields)
            {
                var fieldValue = value[field.Name];
                if (field.Children is null)
                {
                    cells.Add(EncodePrimitive(fieldValue));
                }
                else
                {
                    FlattenRow(
                        fieldValue!.AsObject(),
                        field.Children,
                        cells);
                }
            }
        }

        private static bool TryGetTabularShape(
            JsonArray value,
            out IReadOnlyList<FieldShape> fields)
        {
            if (value.Count == 0 ||
                value.Any(item => item is not JsonObject))
            {
                fields = [];
                return false;
            }

            return TryGetUniformShape(
                value.Select(item => item!.AsObject()).ToArray(),
                out fields);
        }

        private static bool TryGetKeyedShape(
            JsonObject value,
            out IReadOnlyList<FieldShape> fields)
        {
            if (value.Count < 2 ||
                value.Any(entry => entry.Value is not JsonObject))
            {
                fields = [];
                return false;
            }

            return TryGetUniformShape(
                value.Select(entry => entry.Value!.AsObject()).ToArray(),
                out fields);
        }

        private static bool TryGetUniformShape(
            IReadOnlyList<JsonObject> values,
            out IReadOnlyList<FieldShape> fields)
        {
            if (values.Count == 0 || values.Any(value => value.Count == 0))
            {
                fields = [];
                return false;
            }

            var firstKeys = values[0]
                .Select(property => property.Key)
                .ToArray();
            var keySet = firstKeys.ToHashSet(StringComparer.Ordinal);
            if (values.Any(value =>
                    value.Count != firstKeys.Length ||
                    value.Any(property => !keySet.Contains(property.Key))))
            {
                fields = [];
                return false;
            }

            var shape = new List<FieldShape>(firstKeys.Length);
            foreach (var key in firstKeys)
            {
                var column = values.Select(value => value[key]).ToArray();
                if (column.All(IsPrimitive))
                {
                    shape.Add(new FieldShape(key, Children: null));
                    continue;
                }

                if (column.All(item =>
                        item is JsonObject objectItem &&
                        objectItem.Count > 0) &&
                    TryGetUniformShape(
                        column.Select(item => item!.AsObject()).ToArray(),
                        out var children))
                {
                    shape.Add(new FieldShape(key, children));
                    continue;
                }

                fields = [];
                return false;
            }

            fields = shape;
            return true;
        }

        private void AddLine(int depth, string content) =>
            _lines.Add($"{new string(' ', depth * IndentSize)}{content}");
    }
}
