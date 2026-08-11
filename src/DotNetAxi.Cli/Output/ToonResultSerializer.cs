using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Output;

public sealed class ToonResultSerializer
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions PayloadOptions =
        CreatePayloadOptions();

    private static readonly HashSet<string> ReservedFields =
    [
        "schema",
        "command",
        "status",
        "snapshot",
        "resolution",
        "coverage",
        "confidence",
        "scope",
        "error",
        "errors",
        "suggestions",
        "data",
    ];

    public string Serialize(ICommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var document = BuildDocument(result);
        return ToonV41Encoder.Encode(
            document,
            result.Command == "context symbol"
                ? static key => key == "sections"
                : null);
    }

    public byte[] SerializeToUtf8(ICommandResult result) =>
        Utf8.GetBytes(Serialize(result));

    internal static string SerializePayloadValue(
        object value,
        bool expandContextSections = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ToonV41Encoder.Encode(
            JsonSerializer.SerializeToNode(
                value,
                value.GetType(),
                PayloadOptions),
            expandContextSections
                ? static key => key == "sections"
                : null);
    }

    internal static ContextSection<T> CreateContextSectionForBudget<T>(
        string name,
        int order,
        T value,
        bool hasPreviousSection) =>
        StabilizeContextSection(
            name,
            order,
            value,
            hasPreviousSection);

    private static ContextSection<T> StabilizeContextSection<T>(
        string name,
        int order,
        T value,
        bool hasPreviousSection)
    {
        var representation = SerializePayloadValue(value!);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var emittedRepresentation = hasPreviousSection
                ? "\n" + representation
                : representation;
            var section = ContextSection<T>.Create(
                name,
                order,
                value,
                emittedRepresentation);
            var document = SerializePayloadValue(
                new ContextSectionBudgetDocument<T>([section]),
                expandContextSections: true);
            var markerEnd = document.IndexOf('\n');
            if (markerEnd < 0)
            {
                throw new InvalidOperationException(
                    "A serialized context section did not contain a TOON array body.");
            }

            var emitted = document[(markerEnd + 1)..];
            emittedRepresentation = hasPreviousSection
                ? "\n" + emitted
                : emitted;
            var measured = ContextSection<T>.Create(
                name,
                order,
                value,
                emittedRepresentation);
            if (measured.IncludedCharacters == section.IncludedCharacters)
            {
                return measured;
            }

            representation = emitted;
        }

        throw new InvalidOperationException(
            "A serialized context section character count did not stabilize.");
    }

    private static JsonObject BuildDocument(ICommandResult result)
    {
        var document = new JsonObject
        {
            ["schema"] = result.Schema,
            ["command"] = result.Command,
            ["status"] = EnumText(result.Status),
        };

        if (result.Evidence is not null)
        {
            AddEvidence(document, result.Evidence);
        }

        AddPayload(document, result.Payload);
        AddErrors(document, result.Errors);
        AddSuggestions(document, result.Suggestions);

        return document;
    }

    private static void AddEvidence(JsonObject document, Evidence evidence)
    {
        document["snapshot"] = evidence.Snapshot;
        document["resolution"] = EnumText(evidence.Resolution);
        document["coverage"] = EnumText(evidence.Coverage.Level);
        if (!ConfidenceIsImplied(evidence))
        {
            document["confidence"] = EnumText(evidence.Confidence);
        }

        var scope = new JsonObject
        {
            ["root"] = evidence.Scope.WorkspaceRoot,
        };

        if (!AnalyzedPortionIsImplied(evidence.Scope.AnalyzedPortion))
        {
            scope["analyzed_portion"] = evidence.Scope.AnalyzedPortion;
        }

        AddOptional(scope, "solution", evidence.Scope.Solution);
        AddOptionalArray(scope, "projects", evidence.Scope.Projects);
        AddOptionalArray(scope, "frameworks", evidence.Scope.Frameworks);
        AddOptional(scope, "configuration", evidence.Scope.Configuration);
        AddOptionalArray(scope, "paths", evidence.Scope.Paths);
        if (evidence.Scope.Eligibility is not null)
        {
            scope["eligibility"] = JsonSerializer.SerializeToNode(
                evidence.Scope.Eligibility,
                PayloadOptions);
        }
        AddOptional(scope, "considered", evidence.Coverage.Considered);
        if (evidence.Coverage.Level is CoverageLevel.Complete)
        {
            if (evidence.Coverage.Analyzed != evidence.Coverage.Considered)
            {
                AddOptional(scope, "analyzed", evidence.Coverage.Analyzed);
            }

            AddPositive(scope, "remaining", evidence.Coverage.Remaining);
            AddPositive(scope, "excluded", evidence.Coverage.Excluded);
            AddPositive(scope, "failed", evidence.Coverage.Failed);
        }
        else
        {
            AddOptional(scope, "analyzed", evidence.Coverage.Analyzed);
            AddOptional(scope, "remaining", evidence.Coverage.Remaining);
            AddOptional(scope, "excluded", evidence.Coverage.Excluded);
            AddOptional(scope, "failed", evidence.Coverage.Failed);
        }
        AddOptional(scope, "partial_reason", evidence.Coverage.PartialReason);

        document["scope"] = scope;
    }

    private static bool ConfidenceIsImplied(Evidence evidence) =>
        evidence is
    {
        Resolution: EvidenceResolution.Text,
        Confidence: EvidenceConfidence.Verified,
    }
    or
    {
        Resolution: EvidenceResolution.Syntax,
        Confidence: EvidenceConfidence.Candidate,
    };

    private static bool AnalyzedPortionIsImplied(string value) =>
        value is "workspace paths"
            or "eligible workspace paths"
            or "eligible C# workspace paths";

    private static void AddPayload(JsonObject document, object? payload)
    {
        if (payload is null)
        {
            return;
        }

        var payloadNode = JsonSerializer.SerializeToNode(
            payload,
            payload.GetType(),
            PayloadOptions);

        if (payloadNode is JsonObject payloadObject)
        {
            foreach (var property in payloadObject)
            {
                if (ReservedFields.Contains(property.Key))
                {
                    throw new InvalidOperationException(
                        $"Payload field '{property.Key}' conflicts with the output envelope.");
                }

                document[property.Key] = property.Value?.DeepClone();
            }

            return;
        }

        document["data"] = payloadNode;
    }

    private static void AddErrors(
        JsonObject document,
        IReadOnlyList<ResultError> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        if (errors.Count == 1)
        {
            document["error"] = CreateError(errors[0]);
            return;
        }

        document["errors"] = new JsonArray(
            errors
                .Select(error => (JsonNode?)CreateError(error))
                .ToArray());
    }

    private static JsonObject CreateError(ResultError error) =>
        new()
        {
            ["code"] = error.Code,
            ["message"] = error.Message,
            ["correction"] = error.Correction,
        };

    private static void AddSuggestions(
        JsonObject document,
        IReadOnlyList<ResultSuggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            return;
        }

        document["suggestions"] = new JsonArray(
            suggestions
                .Select(suggestion => (JsonNode?)new JsonObject
                {
                    ["command"] = suggestion.Command,
                    ["arguments"] = new JsonArray(
                        suggestion.Arguments
                            .Select(argument => (JsonNode?)argument)
                            .ToArray()),
                })
                .ToArray());
    }

    private static void AddOptional(
        JsonObject target,
        string name,
        string? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
    }

    private static void AddOptional(
        JsonObject target,
        string name,
        int? value)
    {
        if (value is not null)
        {
            target[name] = value.Value;
        }
    }

    private static void AddPositive(
        JsonObject target,
        string name,
        int? value)
    {
        if (value is > 0)
        {
            target[name] = value.Value;
        }
    }

    private static void AddOptionalArray(
        JsonObject target,
        string name,
        IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        target[name] = new JsonArray(
            values.Select(value => (JsonNode?)value).ToArray());
    }

    private static string EnumText<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = Enum.GetName(value)
            ?? throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The enum value is not defined.");
        var output = new StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (index > 0 && char.IsUpper(character))
            {
                output.Append('-');
            }

            output.Append(char.ToLowerInvariant(character));
        }

        return output.ToString();
    }

    private static JsonSerializerOptions CreatePayloadOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(
            new CanonicalStringDictionaryConverterFactory());
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new ValidatingStringConverter());
        options.Converters.Add(new FiniteDoubleConverter());
        options.Converters.Add(new FiniteSingleConverter());
        return options;
    }

    private sealed class CanonicalStringDictionaryConverterFactory :
        JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            !typeof(IHasDeclaredOutputOrder).IsAssignableFrom(typeToConvert) &&
            FindDictionaryInterface(typeToConvert) is not null;

        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var dictionaryInterface = FindDictionaryInterface(typeToConvert)
                ?? throw new InvalidOperationException(
                    $"Type '{typeToConvert}' is not a string-keyed dictionary.");
            var valueType = dictionaryInterface.GetGenericArguments()[1];
            var converterType = typeof(CanonicalStringDictionaryConverter<,>)
                .MakeGenericType(typeToConvert, valueType);

            return (JsonConverter)(Activator.CreateInstance(converterType)
                ?? throw new InvalidOperationException(
                    $"Could not create a dictionary converter for '{typeToConvert}'."));
        }

        private static Type? FindDictionaryInterface(Type type)
        {
            var candidates = type.GetInterfaces().Prepend(type);
            foreach (var candidate in candidates)
            {
                if (!candidate.IsGenericType)
                {
                    continue;
                }

                var definition = candidate.GetGenericTypeDefinition();
                if (definition != typeof(IDictionary<,>) &&
                    definition != typeof(IReadOnlyDictionary<,>))
                {
                    continue;
                }

                if (candidate.GetGenericArguments()[0] == typeof(string))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    private sealed class CanonicalStringDictionaryConverter<TDictionary, TValue> :
        JsonConverter<TDictionary>
        where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
    {
        public override TDictionary? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException(
                "The output serializer does not deserialize payloads.");

        public override void Write(
            Utf8JsonWriter writer,
            TDictionary value,
            JsonSerializerOptions options)
        {
            var projectedEntries = value
                .Select(entry =>
                {
                    ToonV41Encoder.ValidateUnicode(entry.Key);
                    var outputKey = options.DictionaryKeyPolicy?
                        .ConvertName(entry.Key) ?? entry.Key;
                    return new CanonicalDictionaryEntry<TValue>(
                        outputKey,
                        entry.Value);
                });
            var entries = value is IHasDeclaredOutputOrder
                ? projectedEntries.ToArray()
                : projectedEntries
                    .OrderBy(
                        static entry => entry.OutputKey,
                        StringComparer.Ordinal)
                    .ToArray();

            if (entries
                .Select(static entry => entry.OutputKey)
                .Distinct(StringComparer.Ordinal)
                .Count() != entries.Length)
            {
                throw new InvalidOperationException(
                    "Dictionary keys must remain unique after output naming is applied.");
            }

            writer.WriteStartObject();
            foreach (var entry in entries)
            {
                writer.WritePropertyName(entry.OutputKey);
                JsonSerializer.Serialize(writer, entry.Value, options);
            }

            writer.WriteEndObject();
        }
    }

    private sealed record CanonicalDictionaryEntry<TValue>(
        string OutputKey,
        TValue Value);

    private sealed class ValidatingStringConverter : JsonConverter<string>
    {
        public override string? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.GetString();

        public override void Write(
            Utf8JsonWriter writer,
            string value,
            JsonSerializerOptions options)
        {
            ToonV41Encoder.ValidateUnicode(value);
            writer.WriteStringValue(value);
        }
    }

    private sealed class FiniteDoubleConverter : JsonConverter<double>
    {
        public override double Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.GetDouble();

        public override void Write(
            Utf8JsonWriter writer,
            double value,
            JsonSerializerOptions options)
        {
            if (double.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    private sealed class FiniteSingleConverter : JsonConverter<float>
    {
        public override float Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.GetSingle();

        public override void Write(
            Utf8JsonWriter writer,
            float value,
            JsonSerializerOptions options)
        {
            if (float.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}

internal sealed record ContextSectionBudgetDocument<T>(
    IReadOnlyList<ContextSection<T>> Sections);
