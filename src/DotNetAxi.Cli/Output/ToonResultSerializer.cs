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
        return ToonV41Encoder.Encode(document);
    }

    public byte[] SerializeToUtf8(ICommandResult result) =>
        Utf8.GetBytes(Serialize(result));

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
        document["confidence"] = EnumText(evidence.Confidence);

        var scope = new JsonObject
        {
            ["root"] = evidence.Scope.WorkspaceRoot,
            ["analyzed_portion"] = evidence.Scope.AnalyzedPortion,
        };

        AddOptional(scope, "solution", evidence.Scope.Solution);
        AddOptionalArray(scope, "projects", evidence.Scope.Projects);
        AddOptionalArray(scope, "frameworks", evidence.Scope.Frameworks);
        AddOptional(scope, "configuration", evidence.Scope.Configuration);
        AddOptional(scope, "considered", evidence.Coverage.Considered);
        AddOptional(scope, "analyzed", evidence.Coverage.Analyzed);
        AddOptional(scope, "remaining", evidence.Coverage.Remaining);
        AddOptional(scope, "excluded", evidence.Coverage.Excluded);
        AddOptional(scope, "failed", evidence.Coverage.Failed);
        AddOptional(scope, "partial_reason", evidence.Coverage.PartialReason);

        document["scope"] = scope;
    }

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
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new ValidatingStringConverter());
        options.Converters.Add(new FiniteDoubleConverter());
        options.Converters.Add(new FiniteSingleConverter());
        return options;
    }

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
