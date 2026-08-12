using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DotNetAxi.Testing;

internal static class CodexBenchmarkStructuredOutputReader
{
    private static readonly HashSet<string> KnownRootFields =
    [
        "schema",
        "command",
        "status",
        "snapshot",
        "classification",
        "resolution",
        "coverage",
        "confidence",
        "scope",
        "query",
        "candidate_count",
        "count",
        "total_known",
        "total",
        "omitted",
        "truncated",
        "retrieval_command",
        "matches",
        "candidates",
        "discovered",
        "verified",
        "rejected",
        "unresolved",
        "id",
        "kind",
        "name",
        "fully_qualified_name",
        "signature",
        "accessibility",
        "containing_type",
        "owner",
        "location",
        "documentation",
        "body",
        "relationships",
        "path",
        "external",
        "generated",
        "owning_project_count",
        "owning_projects",
        "encoding",
        "byte_order_mark",
        "byte_count",
        "line_count",
        "requested_span",
        "actual_span",
        "preview",
        "included_characters",
        "total_characters",
        "omitted_characters",
        "outline_reference",
        "target_kind",
        "diagnostic_count",
        "items",
        "target",
        "budget_mode",
        "maximum_characters",
        "sections",
        "omitted_sections",
        "approximate_tokens",
        "error",
        "errors",
        "suggestions",
    ];

    private static readonly HashSet<string> ScopeFields =
    [
        "root",
        "analyzed_portion",
        "solution",
        "projects",
        "frameworks",
        "configuration",
        "paths",
        "eligibility",
        "considered",
        "analyzed",
        "remaining",
        "excluded",
        "failed",
        "partial_reason",
    ];

    private static readonly HashSet<string> CandidateFields =
    [
        "id",
        "kind",
        "name",
        "signature",
        "file",
        "line",
        "context_command",
        "construct",
        "column",
        "end_line",
        "end_column",
        "external",
        "type_match",
        "status",
        "variants",
    ];

    private static readonly HashSet<string> CandidateScalarFields =
    [
        "id",
        "kind",
        "name",
        "signature",
        "file",
        "line",
        "context_command",
        "construct",
        "column",
        "end_line",
        "end_column",
        "external",
        "type_match",
        "status",
    ];

    private static readonly HashSet<string> MatchFields =
    [
        "id",
        "kind",
        "name",
        "file",
        "line",
        "signature",
        "owning_projects",
        "variant_count",
        "variants",
    ];

    private static readonly HashSet<string> OutlineItemFields =
    [
        "id",
        "kind",
        "name",
        "signature",
        "attributes",
        "depth",
        "range",
    ];

    private static readonly HashSet<string> SharedEnvelopeRootFields =
    [
        "schema",
        "command",
        "status",
        "snapshot",
        "resolution",
        "coverage",
        "confidence",
        "scope",
    ];

    private static readonly HashSet<string> SymbolSearchRootFields =
        CommandRootFields(
            "count",
            "total_known",
            "total",
            "omitted",
            "truncated",
            "retrieval_command",
            "matches");

    private static readonly HashSet<string> SymbolShowRootFields =
        CommandRootFields(
            "query",
            "candidate_count",
            "count",
            "total_known",
            "total",
            "omitted",
            "truncated",
            "retrieval_command",
            "candidates",
            "id",
            "kind",
            "name",
            "fully_qualified_name",
            "signature",
            "accessibility",
            "containing_type",
            "owner",
            "location",
            "documentation",
            "body",
            "relationships",
            "error");

    private static readonly HashSet<string> SyntaxSearchRootFields =
        CommandRootFields(
            "classification",
            "discovered",
            "verified",
            "rejected",
            "unresolved",
            "count",
            "total_known",
            "total",
            "omitted",
            "truncated",
            "retrieval_command",
            "matches",
            "candidates");

    private static readonly HashSet<string> DocumentShowRootFields =
        CommandRootFields(
            "id",
            "path",
            "external",
            "generated",
            "owning_project_count",
            "owning_projects",
            "encoding",
            "byte_order_mark",
            "byte_count",
            "line_count",
            "requested_span",
            "actual_span",
            "preview",
            "included_characters",
            "total_known",
            "total_characters",
            "omitted_characters",
            "truncated",
            "retrieval_command",
            "outline_reference");

    private static readonly HashSet<string> OutlineRootFields =
        CommandRootFields(
            "target_kind",
            "id",
            "path",
            "external",
            "generated",
            "owning_project_count",
            "owning_projects",
            "diagnostic_count",
            "count",
            "total_known",
            "total",
            "omitted",
            "truncated",
            "retrieval_command",
            "items");

    private static readonly HashSet<string> ContextSymbolRootFields =
        CommandRootFields(
            "target",
            "budget_mode",
            "maximum_characters",
            "sections",
            "included_characters",
            "total_known",
            "total_characters",
            "omitted_characters",
            "omitted_sections",
            "approximate_tokens",
            "truncated",
            "retrieval_command");

    public static CodexBenchmarkStructuredOutput Read(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        string? schema = null;
        string? command = null;
        string? status = null;
        string? query = null;
        string? errorCode = null;
        string? correction = null;
        string? solutionSelector = null;
        IReadOnlyList<string>? projectSelectors = null;
        IReadOnlyList<string>? pathSelectors = null;
        bool? includeTests = null;
        bool? includeGenerated = null;
        int? considered = null;
        int? candidateCount = null;
        int? declaredCandidates = null;
        var candidates = new List<CodexBenchmarkStructuredCandidate>();
        var rootFields = new HashSet<string>(StringComparer.Ordinal);
        var scopeFields = new HashSet<string>(StringComparer.Ordinal);
        var errorFields = new HashSet<string>(StringComparer.Ordinal);
        var tables = new Stack<TableState>();
        CandidateBuilder? candidate = null;
        HashSet<string>? candidateFields = null;
        var malformed = false;
        var sawContent = false;
        var sawScope = false;
        var sawError = false;
        var outsideCandidateSignature = false;
        var currentRoot = string.Empty;
        var currentScopeChild = string.Empty;
        var currentNestedChild = string.Empty;
        var nestedFields = new HashSet<string>(StringComparer.Ordinal);
        var nestedChildFields = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string>? nestedItemFields = null;
        ParsedProperty? previousProperty = null;
        var previousIndentation = 0;
        var previousWasTableRow = false;

        var lines = output.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.Length == 0)
            {
                malformed |= index != lines.Length - 1;
                continue;
            }

            if (line.Contains('\t', StringComparison.Ordinal))
            {
                malformed = true;
                continue;
            }

            var indentation = line.TakeWhile(static value => value == ' ')
                .Count();
            var value = line[indentation..];
            malformed |= indentation % 2 != 0;
            if (!sawContent)
            {
                sawContent = true;
                malformed |= indentation != 0;
            }

            while (tables.Count > 0
                   && indentation <= tables.Peek().Indentation)
            {
                malformed |= !tables.Pop().Complete;
            }

            if (tables.TryPeek(out var table)
                && indentation == table.Indentation + 2)
            {
                malformed |= !table.AddRow(value, candidates);
                previousProperty = null;
                previousIndentation = indentation;
                previousWasTableRow = true;
                continue;
            }

            malformed |= !ValidateIndentation(
                indentation,
                previousIndentation,
                previousProperty,
                previousWasTableRow,
                sawContent && index > 0);
            previousWasTableRow = false;

            if (!TryParseProperty(value, out var property))
            {
                malformed = true;
                previousProperty = null;
                previousIndentation = indentation;
                continue;
            }

            malformed |= !ValidateScalar(property.EncodedValue);

            if (indentation == 0)
            {
                if (currentRoot == "candidates")
                {
                    malformed |= !CompleteCandidate(candidate, candidates);
                    candidate = null;
                    candidateFields = null;
                }

                currentRoot = property.Key;
                currentScopeChild = string.Empty;
                currentNestedChild = string.Empty;
                nestedFields.Clear();
                nestedChildFields.Clear();
                nestedItemFields = null;
                malformed |= property.IsListItem
                             || !KnownRootFields.Contains(property.Key)
                             || !rootFields.Add(property.Key)
                             || !ValidateRootProperty(property);
                if (!sawScope && rootFields.Count == 1)
                {
                    malformed |= property.Key != "schema";
                }

                switch (property.Key)
                {
                    case "schema":
                        malformed |= !TrySetScalar(
                            property,
                            ref schema);
                        break;
                    case "command":
                        malformed |= !TrySetScalar(
                            property,
                            ref command);
                        break;
                    case "status":
                        malformed |= !TrySetScalar(
                            property,
                            ref status);
                        break;
                    case "scope":
                        malformed |= !property.IsPlainContainer || sawScope;
                        sawScope = true;
                        break;
                    case "query":
                        malformed |= !TrySetScalar(
                            property,
                            ref query);
                        break;
                    case "candidate_count":
                        malformed |= !TryReadNonNegativeInteger(
                            property,
                            out var parsedCandidateCount);
                        candidateCount = parsedCandidateCount;
                        break;
                    case "candidates":
                        malformed |= !TryStartCandidateCollection(
                            property,
                            out declaredCandidates);
                        break;
                    case "error":
                        malformed |= !property.IsPlainContainer || sawError;
                        sawError = true;
                        break;
                    case "generated":
                        malformed |= !TryReadBoolean(
                            property,
                            out var generated);
                        includeGenerated = generated;
                        break;
                }
            }
            else if (currentRoot == "scope")
            {
                malformed |= !ReadScopeProperty(
                    property,
                    indentation,
                    ref currentScopeChild,
                    scopeFields,
                    ref solutionSelector,
                    ref projectSelectors,
                    ref pathSelectors,
                    ref includeTests,
                    ref includeGenerated,
                    ref considered);
            }
            else if (currentRoot == "error")
            {
                malformed |= !ReadErrorProperty(
                    property,
                    indentation,
                    errorFields,
                    ref errorCode,
                    ref correction);
            }
            else if (currentRoot == "candidates")
            {
                malformed |= !ReadCandidateProperty(
                    property,
                    indentation,
                    ref candidate,
                    ref candidateFields,
                    candidates);
            }
            else
            {
                if (property.Key == "signature")
                {
                    outsideCandidateSignature = true;
                }

                malformed |= !ReadNestedRootProperty(
                    currentRoot,
                    property,
                    indentation,
                    ref currentNestedChild,
                    nestedFields,
                    nestedChildFields,
                    ref nestedItemFields);
            }

            if (property.Fields is not null)
            {
                if (property.Count is not { } tableCount
                    || property.EncodedValue is not null)
                {
                    malformed = true;
                }
                else
                {
                    tables.Push(new TableState(
                        indentation,
                        property.Key,
                        tableCount,
                        property.Fields,
                        property.IsKeyedTable));
                }
            }

            previousProperty = property;
            previousIndentation = indentation;
        }

        while (tables.Count > 0)
        {
            malformed |= !tables.Pop().Complete;
        }

        if (currentRoot == "candidates")
        {
            malformed |= !CompleteCandidate(candidate, candidates);
        }

        malformed |= declaredCandidates is not null
                     && declaredCandidates != candidates.Count;
        malformed |= candidateCount is not null
                     && (declaredCandidates is null
                         || candidateCount != declaredCandidates
                         || outsideCandidateSignature);
        malformed |= command is null
                     || !ValidateCommandRootFields(command, rootFields);

        var selectorKind = pathSelectors is { Count: > 0 }
            ? "path"
            : solutionSelector is not null
                ? "solution"
                : projectSelectors is { Count: 1 }
                  && command is "search symbol" or "show symbol" or "outline"
                      or "context symbol"
                    ? "project"
                    : null;
        var selectorValue = selectorKind switch
        {
            "path" => pathSelectors![0],
            "solution" => solutionSelector,
            "project" => projectSelectors![0],
            _ => null,
        };

        return new CodexBenchmarkStructuredOutput(
            !malformed
            && schema is not null
            && command is not null
            && status is not null
            && sawScope,
            schema,
            command,
            status,
            new CodexDiscoveryRawScopeEvidence(
                selectorKind,
                selectorValue,
                includeTests,
                includeGenerated,
                considered),
            query,
            candidateCount,
            Array.AsReadOnly(candidates.ToArray()),
            errorCode,
            correction);
    }

    private static HashSet<string> CommandRootFields(params string[] fields)
    {
        var allowed = new HashSet<string>(
            SharedEnvelopeRootFields,
            StringComparer.Ordinal);
        allowed.UnionWith(fields);
        return allowed;
    }

    private static bool ValidateCommandRootFields(
        string command,
        HashSet<string> observed)
    {
        var allowed = command switch
        {
            "search symbol" => SymbolSearchRootFields,
            "show symbol" => SymbolShowRootFields,
            "search syntax invocation" or "search syntax class"
                or "search syntax catch"
                or "search syntax object-creation" => SyntaxSearchRootFields,
            "show document" => DocumentShowRootFields,
            "outline" => OutlineRootFields,
            "context symbol" => ContextSymbolRootFields,
            _ => null,
        };

        return allowed is not null && observed.All(allowed.Contains);
    }

    private static bool ValidateIndentation(
        int indentation,
        int previousIndentation,
        ParsedProperty? previous,
        bool previousWasTableRow,
        bool hasPreviousLine)
    {
        if (!hasPreviousLine || indentation <= previousIndentation)
        {
            return true;
        }

        if (previousWasTableRow || previous is null)
        {
            return false;
        }

        var increase = indentation - previousIndentation;
        return increase == 2
                   && (previous.IsContainer || previous.IsListItem)
               || increase == 4
                   && previous.IsContainer
                   && previous.IsListItem;
    }

    private static bool ValidateRootProperty(ParsedProperty property)
    {
        if (property.IsListItem)
        {
            return false;
        }

        return property.Key switch
        {
            "scope" or "error" or "owner" or "location"
                or "documentation" or "body" or "relationships"
                or "requested_span" or "actual_span"
                or "outline_reference" or "target"
                or "approximate_tokens" => property.IsPlainContainer,
            "matches" => IsObjectCollection(
                property,
                MatchFields,
                allowTable: true),
            "candidates" => IsObjectCollection(
                property,
                CandidateScalarFields,
                allowTable: true),
            "items" => IsObjectCollection(
                property,
                allowedTableFields: null,
                allowTable: false),
            "owning_projects" or "omitted_sections" =>
                IsInlineCollection(property),
            "sections" => IsEmptyCollection(property),
            "candidate_count" or "count" or "total" or "omitted"
                or "discovered" or "verified" or "rejected"
                or "unresolved" or "owning_project_count"
                or "byte_count" or "line_count" or "included_characters"
                or "total_characters" or "omitted_characters"
                or "diagnostic_count" or "maximum_characters" =>
                IsNonNegativeIntegerProperty(property),
            "total_known" or "truncated" or "external"
                or "generated" or "byte_order_mark" =>
                IsBooleanProperty(property),
            "schema" or "command" or "status" or "snapshot"
                or "classification" or "resolution" or "coverage"
                or "confidence" or "query" or "retrieval_command"
                or "id" or "kind" or "name"
                or "fully_qualified_name" or "signature"
                or "accessibility" or "containing_type" or "path"
                or "encoding" or "preview" or "target_kind"
                or "budget_mode" => IsScalarProperty(property),
            _ => false,
        };
    }

    private static bool ReadNestedRootProperty(
        string root,
        ParsedProperty property,
        int indentation,
        ref string currentChild,
        HashSet<string> fields,
        HashSet<string> childFields,
        ref HashSet<string>? itemFields) =>
        root switch
        {
            "matches" => ReadMatchProperty(
                property,
                indentation,
                ref itemFields),
            "items" => ReadOutlineItemProperty(
                property,
                indentation,
                ref itemFields),
            "target" => ReadTargetProperty(
                property,
                indentation,
                ref currentChild,
                fields,
                childFields),
            "owner" or "location" or "documentation" or "body"
                or "relationships" or "requested_span" or "actual_span"
                or "outline_reference" or "approximate_tokens" =>
                ReadFlatRootObjectProperty(
                    root,
                    property,
                    indentation,
                    fields),
            _ => false,
        };

    private static bool ReadMatchProperty(
        ParsedProperty property,
        int indentation,
        ref HashSet<string>? fields)
    {
        if (indentation == 2)
        {
            if (!property.IsListItem)
            {
                return false;
            }

            fields = new HashSet<string>(StringComparer.Ordinal);
        }
        else if (indentation != 4
                 || property.IsListItem
                 || fields is null)
        {
            return false;
        }

        if (!MatchFields.Contains(property.Key)
            || !fields!.Add(property.Key))
        {
            return false;
        }

        return property.Key switch
        {
            "id" or "kind" or "name" or "file" or "signature" =>
                HasScalarValue(property),
            "line" => HasPositiveIntegerValue(property),
            "variant_count" => HasNonNegativeIntegerValue(property),
            "owning_projects" => IsInlineCollection(property),
            "variants" => IsExactTable(
                property,
                keyed: false,
                "configuration",
                "framework",
                "meaning",
                "project"),
            _ => false,
        };
    }

    private static bool ReadOutlineItemProperty(
        ParsedProperty property,
        int indentation,
        ref HashSet<string>? fields)
    {
        if (indentation == 2)
        {
            if (!property.IsListItem)
            {
                return false;
            }

            fields = new HashSet<string>(StringComparer.Ordinal);
        }
        else if (indentation != 4
                 || property.IsListItem
                 || fields is null)
        {
            return false;
        }

        if (!OutlineItemFields.Contains(property.Key)
            || !fields!.Add(property.Key))
        {
            return false;
        }

        return property.Key switch
        {
            "id" or "kind" or "name" or "signature" =>
                HasScalarValue(property),
            "attributes" => IsEmptyCollection(property),
            "depth" => HasNonNegativeIntegerValue(property),
            "range" => IsExactTable(
                property,
                keyed: true,
                "path",
                "line",
                "column",
                "is_external"),
            _ => false,
        };
    }

    private static bool ReadTargetProperty(
        ParsedProperty property,
        int indentation,
        ref string currentChild,
        HashSet<string> fields,
        HashSet<string> childFields)
    {
        if (indentation == 2)
        {
            currentChild = property.Key;
            childFields.Clear();
            if (property.IsListItem || !fields.Add(property.Key))
            {
                return false;
            }

            return property.Key switch
            {
                "id" or "document_ref" => IsScalarProperty(property),
                "location" => property.IsPlainContainer,
                _ => false,
            };
        }

        return indentation == 4
               && currentChild == "location"
               && ReadLocationProperty(property, childFields);
    }

    private static bool ReadFlatRootObjectProperty(
        string root,
        ParsedProperty property,
        int indentation,
        HashSet<string> fields)
    {
        if (indentation != 2
            || property.IsListItem
            || !fields.Add(property.Key))
        {
            return false;
        }

        return root switch
        {
            "owner" => property.Key switch
            {
                "project_count" or "variant_count" =>
                    IsNonNegativeIntegerProperty(property),
                "projects" => IsInlineCollection(property),
                "variants" => IsExactTable(
                    property,
                    keyed: false,
                    "project",
                    "framework",
                    "meaning"),
                _ => false,
            },
            "location" => ReadLocationProperty(property, fields, fieldAdded: true),
            "documentation" or "body" => property.Key switch
            {
                "preview" or "retrieval_command" => IsScalarProperty(property),
                "included_characters" or "total_characters"
                    or "omitted_characters" =>
                    IsNonNegativeIntegerProperty(property),
                "truncated" => IsBooleanProperty(property),
                _ => false,
            },
            "relationships" => property.Key switch
            {
                "attribute_count" or "parameter_count"
                    or "type_parameter_count" or "member_count"
                    or "base_type_count" or "overload_count" =>
                    IsNonNegativeIntegerProperty(property),
                _ => false,
            },
            "requested_span" or "actual_span" => property.Key switch
            {
                "start_line" or "end_line" =>
                    IsPositiveIntegerProperty(property),
                _ => false,
            },
            "outline_reference" => property.Key switch
            {
                "path" => IsScalarProperty(property),
                "available" => IsBooleanProperty(property),
                _ => false,
            },
            "approximate_tokens" => property.Key switch
            {
                "minimum" or "maximum" =>
                    IsNonNegativeIntegerProperty(property),
                _ => false,
            },
            _ => false,
        };
    }

    private static bool ReadLocationProperty(
        ParsedProperty property,
        HashSet<string> fields,
        bool fieldAdded = false)
    {
        if (property.IsListItem
            || !fieldAdded && !fields.Add(property.Key))
        {
            return false;
        }

        return property.Key switch
        {
            "file" => IsScalarProperty(property),
            "line" or "column" or "end_line" or "end_column" =>
                IsPositiveIntegerProperty(property),
            "external" => IsBooleanProperty(property),
            _ => false,
        };
    }

    private static bool IsObjectCollection(
        ParsedProperty property,
        HashSet<string>? allowedTableFields,
        bool allowTable)
    {
        if (property.IsListItem)
        {
            return false;
        }

        if (IsEmptyCollection(property))
        {
            return true;
        }

        if (property.Count is not > 0 || property.EncodedValue is not null)
        {
            return false;
        }

        if (property.Fields is null)
        {
            return !property.IsKeyedTable;
        }

        return allowTable
               && !property.IsKeyedTable
               && allowedTableFields is not null
               && TryReadTableFields(property.Fields, out var tableFields)
               && tableFields.All(allowedTableFields.Contains);
    }

    private static bool IsInlineCollection(ParsedProperty property)
    {
        if (IsEmptyCollection(property))
        {
            return true;
        }

        return TryReadSelectorCollection(property, out _);
    }

    private static bool IsEmptyCollection(ParsedProperty property) =>
        !property.IsListItem
        && property.Count is null
        && property.Fields is null
        && property.EncodedValue == "[]";

    private static bool IsScalarProperty(ParsedProperty property) =>
        !property.IsListItem
        && HasScalarValue(property);

    private static bool HasScalarValue(ParsedProperty property) =>
        property.Count is null
        && property.Fields is null
        && property.EncodedValue is not null
        && property.EncodedValue != "[]";

    private static bool HasNonNegativeIntegerValue(ParsedProperty property) =>
        TryReadNonNegativeInteger(property, out _);

    private static bool HasPositiveIntegerValue(ParsedProperty property) =>
        TryReadNonNegativeInteger(property, out var value)
        && value is > 0;

    private static bool IsBooleanProperty(ParsedProperty property) =>
        !property.IsListItem
        && TryReadBoolean(property, out _);

    private static bool IsNonNegativeIntegerProperty(ParsedProperty property) =>
        !property.IsListItem
        && HasNonNegativeIntegerValue(property);

    private static bool IsPositiveIntegerProperty(ParsedProperty property) =>
        !property.IsListItem
        && HasPositiveIntegerValue(property);

    private static bool IsExactTable(
        ParsedProperty property,
        bool keyed,
        params string[] expectedFields) =>
        !property.IsListItem
        && property.Count is > 0
        && property.EncodedValue is null
        && property.IsKeyedTable == keyed
        && property.Fields is not null
        && TryReadTableFields(property.Fields, out var fields)
        && fields.SequenceEqual(expectedFields, StringComparer.Ordinal);

    private static bool TryReadTableFields(
        string encoded,
        out IReadOnlyList<string> fields)
    {
        var values = encoded.Split(',');
        if (values.Length == 0
            || values.Any(value => !IsKey(value))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            fields = [];
            return false;
        }

        fields = values;
        return true;
    }

    private static bool ReadScopeProperty(
        ParsedProperty property,
        int indentation,
        ref string currentScopeChild,
        HashSet<string> fields,
        ref string? solution,
        ref IReadOnlyList<string>? projects,
        ref IReadOnlyList<string>? paths,
        ref bool? includeTests,
        ref bool? includeGenerated,
        ref int? considered)
    {
        if (indentation == 2)
        {
            currentScopeChild = property.Key;
            if (property.IsListItem
                || !ScopeFields.Contains(property.Key)
                || !fields.Add(property.Key))
            {
                return false;
            }

            return property.Key switch
            {
                "solution" => TrySetScalar(property, ref solution),
                "projects" => TryReadSelectorCollection(
                    property,
                    out projects),
                "frameworks" => TryReadSelectorCollection(
                    property,
                    out _),
                "paths" => TryReadSelectorCollection(property, out paths),
                "eligibility" => property.IsPlainContainer,
                "considered" => TryReadNonNegativeInteger(
                    property,
                    out considered),
                "analyzed" or "remaining" or "excluded" or "failed" =>
                    TryReadNonNegativeInteger(property, out _),
                _ => property.EncodedValue is not null
                     && property.Count is null
                     && property.Fields is null,
            };
        }

        if (indentation != 4
            || currentScopeChild != "eligibility"
            || property.IsListItem
            || property.Count is not null
            || property.Fields is not null)
        {
            return false;
        }

        return property.Key switch
        {
            "include_tests" when includeTests is null =>
                TryReadBoolean(property, out includeTests),
            "include_generated" when includeGenerated is null =>
                TryReadBoolean(property, out includeGenerated),
            _ => false,
        };
    }

    private static bool ReadErrorProperty(
        ParsedProperty property,
        int indentation,
        HashSet<string> fields,
        ref string? code,
        ref string? correction)
    {
        if (indentation != 2
            || property.IsListItem
            || property.Count is not null
            || property.Fields is not null
            || property.EncodedValue is null
            || property.Key is not ("code" or "message" or "correction")
            || !fields.Add(property.Key))
        {
            return false;
        }

        return property.Key switch
        {
            "code" => TrySetScalar(property, ref code),
            "correction" => TrySetScalar(property, ref correction),
            _ => true,
        };
    }

    private static bool ReadCandidateProperty(
        ParsedProperty property,
        int indentation,
        ref CandidateBuilder? candidate,
        ref HashSet<string>? fields,
        List<CodexBenchmarkStructuredCandidate> candidates)
    {
        if (indentation == 2)
        {
            if (!property.IsListItem)
            {
                return false;
            }

            CompleteCandidate(candidate, candidates);
            candidate = new CandidateBuilder();
            fields = new HashSet<string>(StringComparer.Ordinal);
        }
        else if (indentation == 4)
        {
            if (property.IsListItem || candidate is null || fields is null)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (!CandidateFields.Contains(property.Key)
            || !fields!.Add(property.Key))
        {
            return false;
        }

        if (property.Key == "id")
        {
            if (!TryReadScalar(property, out var id)
                || !IsCanonicalSymbolId(id, expectedName: null))
            {
                return false;
            }

            candidate!.Id = id;
            return true;
        }

        if (property.Key == "name")
        {
            if (!TryReadScalar(property, out var name))
            {
                return false;
            }

            candidate!.Name = name;
            return true;
        }

        if (property.Key == "signature")
        {
            if (!TryReadScalar(property, out var signature))
            {
                return false;
            }

            candidate!.Signature = signature;
            return true;
        }

        if (property.Key == "file")
        {
            if (!TryReadScalar(property, out var file))
            {
                return false;
            }

            candidate!.File = file;
            return true;
        }

        if (property.Key == "line")
        {
            if (!TryReadNonNegativeInteger(property, out var line)
                || line is not > 0)
            {
                return false;
            }

            candidate!.Line = line;
            return true;
        }

        return property.Key switch
        {
            "kind" or "context_command" or "construct" or "type_match"
                or "status" => HasScalarValue(property),
            "column" or "end_line" or "end_column" =>
                HasPositiveIntegerValue(property),
            "external" => TryReadBoolean(property, out _),
            "variants" => IsExactTable(
                property,
                keyed: false,
                "configuration",
                "framework",
                "project",
                "reason",
                "status",
                "symbol"),
            _ => false,
        };
    }

    private static bool CompleteCandidate(
        CandidateBuilder? candidate,
        List<CodexBenchmarkStructuredCandidate> candidates)
    {
        if (candidate is null)
        {
            return true;
        }

        candidates.Add(new CodexBenchmarkStructuredCandidate(
            candidate.Id,
            candidate.Name,
            candidate.Signature,
            candidate.File,
            candidate.Line));
        return candidate.Id is null
               || IsCanonicalSymbolId(candidate.Id, candidate.Name);
    }

    private static bool TryStartCandidateCollection(
        ParsedProperty property,
        out int? declared)
    {
        declared = null;
        if (property.IsListItem)
        {
            return false;
        }

        if (property.EncodedValue == "[]"
            && property.Count is null
            && property.Fields is null)
        {
            declared = 0;
            return true;
        }

        if (property.Count is not { } count
            || count <= 0
            || property.EncodedValue is not null)
        {
            return false;
        }

        declared = count;
        return true;
    }

    private static bool TryReadSelectorCollection(
        ParsedProperty property,
        out IReadOnlyList<string>? values)
    {
        values = null;
        if (property.IsListItem || property.Fields is not null)
        {
            return false;
        }

        if (property.Count is not { } count
            || count <= 0
            || property.EncodedValue is null
            || !TrySplitCells(property.EncodedValue, out var cells)
            || cells.Count != count
            || cells.Any(string.IsNullOrEmpty)
            || cells.Distinct(StringComparer.Ordinal).Count() != cells.Count)
        {
            return false;
        }

        values = Array.AsReadOnly(cells.ToArray());
        return true;
    }

    private static bool TrySetScalar(
        ParsedProperty property,
        ref string? destination)
    {
        if (destination is not null
            || !TryReadScalar(property, out var value))
        {
            return false;
        }

        destination = value;
        return true;
    }

    private static bool TryReadScalar(
        ParsedProperty property,
        out string value)
    {
        value = string.Empty;
        return property.Count is null
               && property.Fields is null
               && property.EncodedValue is not null
               && TryDecodeScalar(property.EncodedValue, out value)
               && value.Length > 0;
    }

    private static bool TryReadBoolean(
        ParsedProperty property,
        out bool? value)
    {
        value = null;
        if (property.Count is not null
            || property.Fields is not null
            || property.EncodedValue is null
            || !bool.TryParse(property.EncodedValue, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadNonNegativeInteger(
        ParsedProperty property,
        out int? value)
    {
        value = null;
        if (property.Count is not null
            || property.Fields is not null
            || property.EncodedValue is null
            || !int.TryParse(
                property.EncodedValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed < 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    internal static bool IsCanonicalSymbolId(
        string? id,
        string? expectedName)
    {
        const string prefix = "symbol/v2/";
        if (string.IsNullOrWhiteSpace(id)
            || !id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = id[prefix.Length..].Split('/');
        if (segments.Length != 3
            || !TryDecodeCanonicalBase64Url(segments[0], out var name)
            || !IsLowerHex(segments[1], 64)
            || !IsLowerHex(segments[2], 64))
        {
            return false;
        }

        return expectedName is null
               || string.Equals(name, expectedName, StringComparison.Ordinal);
    }

    private static bool TryDecodeCanonicalBase64Url(
        string encoded,
        out string value)
    {
        value = string.Empty;
        if (encoded.Length == 0
            || encoded.Any(static character =>
                character is not (>= 'A' and <= 'Z')
                    and not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9')
                    and not '-'
                    and not '_'))
        {
            return false;
        }

        var base64 = encoded.Replace('-', '+').Replace('_', '/');
        if (base64.Length % 4 == 1)
        {
            return false;
        }

        var remainder = base64.Length % 4;
        if (remainder > 0)
        {
            base64 = base64.PadRight(base64.Length + (4 - remainder), '=');
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            value = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
            return !string.IsNullOrWhiteSpace(value)
                   && !value.Contains('\0', StringComparison.Ordinal)
                   && string.Equals(
                       Convert.ToBase64String(bytes)
                           .TrimEnd('=')
                           .Replace('+', '-')
                           .Replace('/', '_'),
                       encoded,
                       StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length
        && value.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static bool TryParseProperty(
        string line,
        out ParsedProperty property)
    {
        var isListItem = line.StartsWith("- ", StringComparison.Ordinal);
        var content = isListItem ? line[2..] : line;
        var separator = FindPropertySeparator(content);
        if (separator <= 0)
        {
            property = null!;
            return false;
        }

        var shape = content[..separator];
        var encodedValue = content[(separator + 1)..];
        if (encodedValue.Length > 0)
        {
            if (encodedValue[0] != ' ' || encodedValue.Length == 1)
            {
                property = null!;
                return false;
            }

            encodedValue = encodedValue[1..];
        }
        else
        {
            encodedValue = null;
        }

        var keyEnd = shape.IndexOfAny(['[', '{']);
        var key = keyEnd < 0 ? shape : shape[..keyEnd];
        if (!IsKey(key))
        {
            property = null!;
            return false;
        }

        int? count = null;
        var isKeyedTable = false;
        string? fields = null;
        var offset = key.Length;
        if (offset < shape.Length && shape[offset] == '[')
        {
            var end = shape.IndexOf(']', offset + 1);
            var countText = end < 0
                ? ReadOnlySpan<char>.Empty
                : shape.AsSpan(offset + 1, end - offset - 1);
            if (countText.Length > 0 && countText[^1] == ':')
            {
                isKeyedTable = true;
                countText = countText[..^1];
            }

            if (end < 0
                || !int.TryParse(
                    countText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedCount)
                || parsedCount < 0)
            {
                property = null!;
                return false;
            }

            count = parsedCount;
            offset = end + 1;
        }

        if (offset < shape.Length && shape[offset] == '{')
        {
            if (shape[^1] != '}')
            {
                property = null!;
                return false;
            }

            fields = shape[(offset + 1)..^1];
            if (fields.Length == 0)
            {
                property = null!;
                return false;
            }

            offset = shape.Length;
        }

        if (offset != shape.Length
            || fields is not null && count is null
            || isKeyedTable && fields is null)
        {
            property = null!;
            return false;
        }

        property = new ParsedProperty(
            key,
            count,
            fields,
            encodedValue,
            isListItem,
            isKeyedTable);
        return true;
    }

    private static int FindPropertySeparator(string value)
    {
        var brackets = 0;
        var braces = 0;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case ':' when brackets == 0 && braces == 0:
                    return index;
            }

            if (brackets < 0 || braces < 0)
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool IsKey(string value)
    {
        if (value.Length == 0
            || value[0] is not (>= 'a' and <= 'z') and not '_')
        {
            return false;
        }

        return value.Skip(1).All(static character =>
            character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '.');
    }

    private static bool ValidateScalar(string? encoded)
    {
        if (encoded is null)
        {
            return true;
        }

        if (encoded[0] != '"')
        {
            return !encoded.Contains('"', StringComparison.Ordinal)
                   && !encoded.Contains('\r', StringComparison.Ordinal)
                   && !encoded.Contains('\n', StringComparison.Ordinal);
        }

        return TryDecodeScalar(encoded, out _);
    }

    private static bool TryDecodeScalar(string encoded, out string value)
    {
        if (encoded.Length > 0 && encoded[0] == '"')
        {
            try
            {
                value = JsonSerializer.Deserialize<string>(encoded)
                        ?? string.Empty;
                return true;
            }
            catch (JsonException)
            {
                value = string.Empty;
                return false;
            }
        }

        value = encoded;
        return true;
    }

    private static bool TrySplitCells(
        string encoded,
        out IReadOnlyList<string> cells)
    {
        var result = new List<string>();
        var start = 0;
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < encoded.Length; index++)
        {
            var character = encoded[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && character == ',')
            {
                if (!TryDecodeScalar(encoded[start..index], out var cell))
                {
                    cells = [];
                    return false;
                }

                result.Add(cell);
                start = index + 1;
            }
        }

        if (quoted
            || escaped
            || !TryDecodeScalar(encoded[start..], out var last))
        {
            cells = [];
            return false;
        }

        result.Add(last);
        cells = result;
        return true;
    }

    private sealed record ParsedProperty(
        string Key,
        int? Count,
        string? Fields,
        string? EncodedValue,
        bool IsListItem,
        bool IsKeyedTable)
    {
        public bool IsContainer => EncodedValue is null;

        public bool IsPlainContainer =>
            !IsListItem
            && Count is null
            && Fields is null
            && EncodedValue is null;
    }

    private sealed class CandidateBuilder
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Signature { get; set; }

        public string? File { get; set; }

        public int? Line { get; set; }
    }

    private sealed class TableState
    {
        private readonly string[] _fields;
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly bool _keyed;
        private int _rows;

        public TableState(
            int indentation,
            string key,
            int count,
            string fields,
            bool keyed)
        {
            Indentation = indentation;
            Key = key;
            Count = count;
            _fields = fields.Split(',');
            _keyed = keyed;
        }

        public int Indentation { get; }

        public string Key { get; }

        public int Count { get; }

        public bool Complete => _rows == Count;

        public bool AddRow(
            string encoded,
            List<CodexBenchmarkStructuredCandidate> candidates)
        {
            _rows++;
            if (_keyed)
            {
                var separator = encoded.IndexOf(": ", StringComparison.Ordinal);
                if (separator <= 0
                    || !IsKey(encoded[..separator])
                    || !_keys.Add(encoded[..separator]))
                {
                    return false;
                }

                encoded = encoded[(separator + 2)..];
            }

            if (_rows > Count
                || !TrySplitCells(encoded, out var cells)
                || cells.Count != _fields.Length)
            {
                return false;
            }

            if (Key != "candidates")
            {
                return true;
            }

            if (_fields.Any(field => !CandidateScalarFields.Contains(field))
                || _fields.Distinct(StringComparer.Ordinal).Count()
                != _fields.Length)
            {
                return false;
            }

            var idIndex = Array.IndexOf(_fields, "id");
            var nameIndex = Array.IndexOf(_fields, "name");
            var signatureIndex = Array.IndexOf(_fields, "signature");
            var fileIndex = Array.IndexOf(_fields, "file");
            var lineIndex = Array.IndexOf(_fields, "line");
            var id = idIndex >= 0 ? cells[idIndex] : null;
            var name = nameIndex >= 0 ? cells[nameIndex] : null;
            var signature = signatureIndex >= 0 ? cells[signatureIndex] : null;
            var file = fileIndex >= 0 ? cells[fileIndex] : null;
            int? line = null;
            if (id is not null
                && !IsCanonicalSymbolId(id, name)
                || name is not null && name.Length == 0
                || signature is not null && signature.Length == 0
                || file is not null && file.Length == 0
                || lineIndex >= 0
                && (!int.TryParse(
                        cells[lineIndex],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedLine)
                    || parsedLine <= 0))
            {
                return false;
            }

            if (lineIndex >= 0)
            {
                line = int.Parse(
                    cells[lineIndex],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture);
            }

            candidates.Add(new CodexBenchmarkStructuredCandidate(
                id,
                name,
                signature,
                file,
                line));
            return true;
        }
    }
}

internal sealed record CodexBenchmarkStructuredOutput(
    bool WellFormed,
    string? Schema,
    string? Command,
    string? Status,
    CodexDiscoveryRawScopeEvidence Scope,
    string? Query,
    int? CandidateCount,
    IReadOnlyList<CodexBenchmarkStructuredCandidate> Candidates,
    string? ErrorCode,
    string? Correction)
{
    public static CodexBenchmarkStructuredOutput Empty { get; } = new(
        WellFormed: false,
        Schema: null,
        Command: null,
        Status: null,
        CodexDiscoveryRawScopeEvidence.Empty,
        Query: null,
        CandidateCount: null,
        Array.Empty<CodexBenchmarkStructuredCandidate>(),
        ErrorCode: null,
        Correction: null);
}

internal sealed record CodexBenchmarkStructuredCandidate(
    string? Id,
    string? Name,
    string? Signature,
    string? File,
    int? Line);
