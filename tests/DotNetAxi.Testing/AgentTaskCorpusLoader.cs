using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DotNetAxi.Testing;

public static partial class AgentTaskCorpusLoader
{
    private const string SupportedSchema =
        "dotnet-axi/agent-task-corpus/v1";

    private static readonly JsonSerializerOptions CorpusOptions =
        new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

    private static readonly string[] RequiredSafetyChecks =
    [
        "claims-supported",
        "network-unused",
        "workspace-unchanged",
    ];

    private static readonly string[] RequiredValidationRules =
    [
        "fixture-content-hash",
        "safety-oracle",
        "success-oracle",
    ];

    private static readonly string[] CandidateGuidanceMarkers =
    [
        "baseline condition",
        "baseline-only",
        "candidate condition",
        "candidate-only",
        "dnaxi",
        "dotnet-axi",
    ];

    private static readonly HashSet<string> WindowsDeviceNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "CLOCK$",
        };

    public static async ValueTask<AgentTaskCorpus> LoadAsync(
        string corpusPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusPath);
        var fullCorpusPath = Path.GetFullPath(corpusPath);
        if (!File.Exists(fullCorpusPath))
        {
            throw new AgentTaskCorpusException(
                $"Agent-task corpus '{fullCorpusPath}' does not exist.");
        }

        CorpusDocument? document;
        try
        {
            await using var stream = File.OpenRead(fullCorpusPath);
            document = await JsonSerializer.DeserializeAsync<CorpusDocument>(
                stream,
                CorpusOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new AgentTaskCorpusException(
                $"Agent-task corpus '{fullCorpusPath}' is not valid JSON.",
                exception);
        }

        if (document is null)
        {
            throw new AgentTaskCorpusException(
                $"Agent-task corpus '{fullCorpusPath}' is empty.");
        }

        if (!string.Equals(
                document.Schema,
                SupportedSchema,
                StringComparison.Ordinal))
        {
            throw new AgentTaskCorpusException(
                $"Agent-task corpus schema must be '{SupportedSchema}'.");
        }

        var id = ValidateIdentifier(document.Id, "Corpus id");
        var version = ValidateVersion(document.Version, "Corpus version");
        var corpusDirectory = Path.GetDirectoryName(fullCorpusPath)
            ?? throw new AgentTaskCorpusException(
                "The agent-task corpus must have a parent directory.");
        if (document.Tasks is null || document.Tasks.Count == 0)
        {
            throw new AgentTaskCorpusException(
                "Agent-task corpus must declare at least one task.");
        }

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        var tasks = new List<AgentTaskDefinition>(document.Tasks.Count);
        foreach (var task in document.Tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tasks.Add(
                await ValidateTaskAsync(
                    task,
                    corpusDirectory,
                    taskIds,
                    cancellationToken));
        }

        return new AgentTaskCorpus(
            id,
            version,
            Array.AsReadOnly(tasks.ToArray()));
    }

    private static async ValueTask<AgentTaskDefinition> ValidateTaskAsync(
        TaskDocument task,
        string corpusDirectory,
        ISet<string> taskIds,
        CancellationToken cancellationToken)
    {
        var id = ValidateIdentifier(task.Id, "Task id");
        if (!taskIds.Add(id))
        {
            throw new AgentTaskCorpusException(
                $"Agent-task id '{id}' is duplicated.");
        }

        var milestone = ValidateVersion(
            task.Milestone,
            $"Task '{id}' milestone");
        var requiredCapabilities = ValidateIdentifiers(
            task.RequiredCapabilities,
            $"Task '{id}' required capability",
            requireNonEmpty: true);
        var prompt = ValidateNeutralText(
            task.Prompt,
            $"Task '{id}' prompt");
        var repository = await ValidateRepositoryAsync(
            id,
            task.Repository,
            corpusDirectory,
            cancellationToken);
        var applicability = ValidateApplicability(id, task.Applicability);
        var execution = ValidateExecution(id, task.Execution);
        var successOracle = ValidateSuccessOracle(
            id,
            task.SuccessOracle);
        var safetyOracle = ValidateSafetyOracle(id, task.SafetyOracle);
        var requiredValidation = ValidateIdentifiers(
            task.RequiredValidation,
            $"Task '{id}' required validation",
            requireNonEmpty: true);
        foreach (var requiredRule in RequiredValidationRules)
        {
            if (!requiredValidation.Contains(
                    requiredRule,
                    StringComparer.Ordinal))
            {
                throw new AgentTaskCorpusException(
                    $"Task '{id}' required validation must include '{requiredRule}'.");
            }
        }

        if (string.Equals(
                successOracle.Kind,
                "model-judged",
                StringComparison.Ordinal)
            && !requiredValidation.Contains(
                "model-judge",
                StringComparer.Ordinal))
        {
            throw new AgentTaskCorpusException(
                $"Task '{id}' required validation must include 'model-judge'.");
        }

        return new AgentTaskDefinition(
            id,
            milestone,
            requiredCapabilities,
            prompt,
            repository,
            applicability,
            execution,
            successOracle,
            safetyOracle,
            requiredValidation);
    }

    private static async ValueTask<AgentTaskRepositoryState>
        ValidateRepositoryAsync(
            string taskId,
            RepositoryDocument? repository,
            string corpusDirectory,
            CancellationToken cancellationToken)
    {
        if (repository is null)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' repository state is required.");
        }

        var fixtureManifest = ValidateRelativePath(
            repository.FixtureManifest,
            $"Task '{taskId}' fixture manifest");
        var fixtureName = ValidateIdentifier(
            repository.FixtureName,
            $"Task '{taskId}' fixture name");
        if (repository.FixtureSeed is null)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' fixture seed is required.");
        }

        var contentHash = repository.ContentHash;
        if (contentHash is null
            || !Sha256Regex().IsMatch(contentHash))
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' fixture content hash must be a lowercase SHA-256 value.");
        }

        if (!string.Equals(
                repository.State,
                "materialized-clean",
                StringComparison.Ordinal))
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' repository state must be 'materialized-clean'.");
        }

        var manifestPath = ResolveContainedPath(
            corpusDirectory,
            fixtureManifest,
            $"Task '{taskId}' fixture manifest");
        FixtureMaterializationPlan plan;
        try
        {
            plan = await FixtureManifestLoader.LoadAsync(
                manifestPath,
                cancellationToken);
        }
        catch (FixtureManifestException exception)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' fixture manifest is invalid.",
                exception);
        }

        if (!string.Equals(
                plan.Identity.Name,
                fixtureName,
                StringComparison.Ordinal)
            || plan.Identity.Seed != repository.FixtureSeed.Value)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' fixture identity does not match its manifest.");
        }

        if (plan.Git is not null)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' materialized-clean fixture cannot declare mutable Git preparation.");
        }

        var actualContentHash = FixtureContentHasher.Compute(plan.Files);
        if (!string.Equals(
                actualContentHash,
                contentHash,
                StringComparison.Ordinal))
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' fixture content hash does not match the materialized fixture; expected '{contentHash}', actual '{actualContentHash}'.");
        }

        return new AgentTaskRepositoryState(
            fixtureManifest,
            fixtureName,
            repository.FixtureSeed.Value,
            contentHash,
            "materialized-clean");
    }

    private static AgentTaskApplicability ValidateApplicability(
        string taskId,
        ApplicabilityDocument? applicability)
    {
        if (applicability?.Baseline is null
            || applicability.Candidate is null)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' must explicitly declare baseline and candidate applicability.");
        }

        if (!applicability.Baseline.Value
            && !applicability.Candidate.Value)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' must apply to at least one condition.");
        }

        return new AgentTaskApplicability(
            applicability.Baseline.Value,
            applicability.Candidate.Value);
    }

    private static AgentTaskExecutionPolicy ValidateExecution(
        string taskId,
        ExecutionDocument? execution)
    {
        if (execution is null)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' execution policy is required.");
        }

        var permittedTools = ValidateIdentifiers(
            execution.PermittedTools,
            $"Task '{taskId}' permitted tool",
            requireNonEmpty: true);
        foreach (var permittedTool in permittedTools)
        {
            if (FindCandidateGuidanceMarker(permittedTool) is { } marker)
            {
                throw new AgentTaskCorpusException(
                    $"Task '{taskId}' permitted tools leak condition-specific candidate guidance through '{marker}'.");
            }
        }

        if (execution.TimeoutSeconds is null
            || execution.TimeoutSeconds is < 1 or > 1800)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' timeoutSeconds must be between 1 and 1800.");
        }

        if (!string.Equals(
                execution.Network,
                "disabled",
                StringComparison.Ordinal)
            || !string.Equals(
                execution.Locale,
                "invariant",
                StringComparison.Ordinal)
            || !string.Equals(
                execution.TimeZone,
                "UTC",
                StringComparison.Ordinal))
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' setup must disable network and pin the invariant locale and UTC time zone.");
        }

        return new AgentTaskExecutionPolicy(
            permittedTools,
            execution.TimeoutSeconds.Value,
            "disabled",
            "invariant",
            "UTC");
    }

    private static AgentTaskSuccessOracle ValidateSuccessOracle(
        string taskId,
        SuccessOracleDocument? oracle)
    {
        if (oracle is null)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' success oracle is required.");
        }

        if (string.Equals(
                oracle.Kind,
                "exact-fact-set",
                StringComparison.Ordinal))
        {
            if (!string.Equals(
                    oracle.Normalizer,
                    "ordinal-lines/v1",
                    StringComparison.Ordinal))
            {
                throw new AgentTaskCorpusException(
                    $"Task '{taskId}' exact success oracle must use 'ordinal-lines/v1'.");
            }

            if (oracle.ModelJudge is not null)
            {
                throw new AgentTaskCorpusException(
                    $"Task '{taskId}' deterministic success oracle cannot declare a model judge.");
            }

            var expectedFacts = ValidateFacts(taskId, oracle.ExpectedFacts);
            return new AgentTaskSuccessOracle(
                "exact-fact-set",
                oracle.Normalizer,
                expectedFacts,
                null);
        }

        if (!string.Equals(
                oracle.Kind,
                "model-judged",
                StringComparison.Ordinal))
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' success oracle kind must be 'exact-fact-set' or 'model-judged'.");
        }

        if (oracle.Normalizer is not null
            || oracle.ExpectedFacts is { Count: > 0 }
            || oracle.ModelJudge is null)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' model-judged oracle must declare only a model judge.");
        }

        var judgeVersion = ValidateVersion(
            oracle.ModelJudge.Version,
            $"Task '{taskId}' model judge version");
        if (oracle.ModelJudge.ConditionBlinded is not true)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' model judge must be condition-blinded.");
        }

        var rubric = ValidateNeutralText(
            oracle.ModelJudge.Rubric,
            $"Task '{taskId}' model judge rubric");
        var judge = new AgentTaskModelJudge(
            judgeVersion,
            true,
            rubric);
        return new AgentTaskSuccessOracle(
            "model-judged",
            null,
            Array.Empty<string>(),
            judge);
    }

    private static IReadOnlyList<string> ValidateFacts(
        string taskId,
        IReadOnlyList<string>? facts)
    {
        if (facts is null || facts.Count == 0)
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' exact success oracle must declare expected facts.");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        foreach (var fact in facts)
        {
            if (string.IsNullOrWhiteSpace(fact)
                || fact.Contains('\r')
                || fact.Contains('\n'))
            {
                throw new AgentTaskCorpusException(
                    $"Task '{taskId}' expected facts must be non-empty single lines.");
            }

            if (!unique.Add(fact))
            {
                throw new AgentTaskCorpusException(
                    $"Task '{taskId}' exact success oracle contains ambiguous duplicate fact '{fact}'.");
            }

            if (previous is not null
                && string.CompareOrdinal(previous, fact) >= 0)
            {
                throw new AgentTaskCorpusException(
                    $"Task '{taskId}' expected facts must be strictly ordinal-sorted.");
            }

            previous = fact;
        }

        return Array.AsReadOnly(facts.ToArray());
    }

    private static AgentTaskSafetyOracle ValidateSafetyOracle(
        string taskId,
        SafetyOracleDocument? oracle)
    {
        if (oracle is null
            || !string.Equals(oracle.Kind, "all", StringComparison.Ordinal))
        {
            throw new AgentTaskCorpusException(
                $"Task '{taskId}' safety oracle kind must be 'all'.");
        }

        var checks = ValidateIdentifiers(
            oracle.Checks,
            $"Task '{taskId}' safety check",
            requireNonEmpty: true);
        foreach (var requiredCheck in RequiredSafetyChecks)
        {
            if (!checks.Contains(requiredCheck, StringComparer.Ordinal))
            {
                throw new AgentTaskCorpusException(
                    $"Task '{taskId}' safety oracle must include '{requiredCheck}'.");
            }
        }

        return new AgentTaskSafetyOracle("all", checks);
    }

    private static IReadOnlyList<string> ValidateIdentifiers(
        IReadOnlyList<string>? values,
        string field,
        bool requireNonEmpty)
    {
        if (values is null || (requireNonEmpty && values.Count == 0))
        {
            throw new AgentTaskCorpusException(
                $"{field} must declare at least one value.");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        foreach (var value in values)
        {
            var identifier = ValidateIdentifier(value, field);
            if (!unique.Add(identifier))
            {
                throw new AgentTaskCorpusException(
                    $"{field} '{identifier}' is duplicated.");
            }

            if (previous is not null
                && string.CompareOrdinal(previous, identifier) >= 0)
            {
                throw new AgentTaskCorpusException(
                    $"{field} values must be strictly ordinal-sorted.");
            }

            previous = identifier;
        }

        return Array.AsReadOnly(values.ToArray());
    }

    private static string ValidateIdentifier(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !IdentifierRegex().IsMatch(value))
        {
            throw new AgentTaskCorpusException(
                $"{field} must be a lowercase portable identifier.");
        }

        return value;
    }

    private static string ValidateVersion(string? value, string field)
    {
        if (value is null
            || !Version.TryParse(value, out var version)
            || version.Major < 0
            || version.Minor < 0
            || version.Build < 0
            || version.Revision >= 0)
        {
            throw new AgentTaskCorpusException(
                $"{field} must be an explicit major.minor.patch version.");
        }

        return value;
    }

    private static string ValidateNeutralText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AgentTaskCorpusException($"{field} is required.");
        }

        if (FindCandidateGuidanceMarker(value) is { } marker)
        {
            throw new AgentTaskCorpusException(
                $"{field} leaks condition-specific candidate guidance through '{marker}'.");
        }

        return value;
    }

    private static string? FindCandidateGuidanceMarker(string value) =>
        CandidateGuidanceMarkers.FirstOrDefault(
            marker => value.Contains(
                marker,
                StringComparison.OrdinalIgnoreCase));

    private static string ValidateRelativePath(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathFullyQualified(value)
            || value.Contains('\\')
            || value.Contains(':')
            || value.Any(static character =>
                char.IsControl(character)
                || character is '*' or '?' or '"' or '<' or '>' or '|'))
        {
            throw new AgentTaskCorpusException(
                $"{field} must be a portable relative path.");
        }

        var segments = value.Split('/');
        if (segments.Any(static segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."))
        {
            throw new AgentTaskCorpusException(
                $"{field} cannot contain empty, '.' or '..' segments.");
        }


        foreach (var segment in segments)
        {
            if (!segment.IsNormalized(NormalizationForm.FormC)
                || segment[^1] is ' ' or '.'
                || IsWindowsDeviceName(segment))
            {
                throw new AgentTaskCorpusException(
                    $"{field} must use portable NFC-normalized path segments.");
            }
        }

        return string.Join('/', segments);
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        var extensionSeparator = segment.IndexOf('.');
        var baseName = extensionSeparator < 0
            ? segment
            : segment[..extensionSeparator];
        return WindowsDeviceNames.Contains(baseName)
            || (baseName.Length == 4
                && baseName[3] is (>= '1' and <= '9')
                    or '\u00b9'
                    or '\u00b2'
                    or '\u00b3'
                && (baseName.StartsWith(
                        "COM",
                        StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith(
                        "LPT",
                        StringComparison.OrdinalIgnoreCase)));
    }

    private static string ResolveContainedPath(
        string root,
        string relativePath,
        string field)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            throw new AgentTaskCorpusException(
                $"{field} escapes the corpus directory.");
        }

        return candidate;
    }

    [GeneratedRegex("^[a-z0-9]+(?:[.:-][a-z0-9]+)*$")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Regex();

    private sealed class CorpusDocument
    {
        public string? Schema { get; init; }

        public string? Id { get; init; }

        public string? Version { get; init; }

        public IReadOnlyList<TaskDocument>? Tasks { get; init; }
    }

    private sealed class TaskDocument
    {
        public string? Id { get; init; }

        public string? Milestone { get; init; }

        public IReadOnlyList<string>? RequiredCapabilities { get; init; }

        public string? Prompt { get; init; }

        public RepositoryDocument? Repository { get; init; }

        public ApplicabilityDocument? Applicability { get; init; }

        public ExecutionDocument? Execution { get; init; }

        public SuccessOracleDocument? SuccessOracle { get; init; }

        public SafetyOracleDocument? SafetyOracle { get; init; }

        public IReadOnlyList<string>? RequiredValidation { get; init; }
    }

    private sealed class RepositoryDocument
    {
        public string? FixtureManifest { get; init; }

        public string? FixtureName { get; init; }

        public int? FixtureSeed { get; init; }

        public string? ContentHash { get; init; }

        public string? State { get; init; }
    }

    private sealed class ApplicabilityDocument
    {
        public bool? Baseline { get; init; }

        public bool? Candidate { get; init; }
    }

    private sealed class ExecutionDocument
    {
        public IReadOnlyList<string>? PermittedTools { get; init; }

        public int? TimeoutSeconds { get; init; }

        public string? Network { get; init; }

        public string? Locale { get; init; }

        public string? TimeZone { get; init; }
    }

    private sealed class SuccessOracleDocument
    {
        public string? Kind { get; init; }

        public string? Normalizer { get; init; }

        public IReadOnlyList<string>? ExpectedFacts { get; init; }

        public ModelJudgeDocument? ModelJudge { get; init; }
    }

    private sealed class ModelJudgeDocument
    {
        public string? Version { get; init; }

        public bool? ConditionBlinded { get; init; }

        public string? Rubric { get; init; }
    }

    private sealed class SafetyOracleDocument
    {
        public string? Kind { get; init; }

        public IReadOnlyList<string>? Checks { get; init; }
    }
}
