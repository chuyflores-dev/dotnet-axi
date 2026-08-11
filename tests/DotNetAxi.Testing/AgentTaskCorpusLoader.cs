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

    private static readonly string[] Pre06UnavailableCapabilityFamilies =
    [
        "analysis.impact",
        "context.callees",
        "context.callers",
        "context.derived",
        "context.derived-types",
        "context.implementations",
        "context.inheritance",
        "context.overrides",
        "context.references",
        "context.relationship",
        "context.relationships",
        "context.tests",
        "context.symbol.callees",
        "context.symbol.callers",
        "context.symbol.derived",
        "context.symbol.derived-types",
        "context.symbol.implementations",
        "context.symbol.inheritance",
        "context.symbol.overrides",
        "context.symbol.references",
        "context.symbol.relationship",
        "context.symbol.relationships",
        "context.symbol.tests",
        "dependency.graph",
        "graph",
        "impact",
        "mutation",
        "project.graph",
        "relationship",
        "search.bases",
        "search.callees",
        "search.callers",
        "search.derived",
        "search.implementations",
        "search.overrides",
        "search.references",
        "search.relationship",
        "search.relationships",
        "search.symbol.callees",
        "search.symbol.callers",
        "search.symbol.derived-types",
        "search.symbol.implementations",
        "search.symbol.inheritance",
        "search.symbol.overrides",
        "search.symbol.references",
        "search.symbol.relationship",
        "search.symbol.relationships",
    ];

    private const string Pre06UnavailableExpectationPattern =
        @"(?ix)(?<![\w-])(?:"
        + @"references|implementations|inheritance|overrides|"
        + @"derived[ /\\-]+types?|callers?|callees?|mutations?|renames?"
        + @")(?![\w-])|"
        + @"(?<![\w-])(?:symbol|search|context)[ /\\.-]+(?:"
        + @"references?|implementations?|derived(?:[ /\\-]+types?)?|"
        + @"bases?|overrides?|callers?|callees?|tests)(?![\w-])|"
        + @"(?<![\w-])(?:references?|implementations?|derived|bases?|"
        + @"overrides?|callers?|callees?|tests)[ /\\.-]+(?:symbol|search|context)"
        + @"(?![\w-])|"
        + @"(?<![\w-])graph[ /\\.-]+(?:"
        + @"projects?|dependencies|paths?|cycles?|impact)(?![\w-])|"
        + @"(?<![\w-])(?:projects?|dependencies|paths?|cycles?|impact)"
        + @"[ /\\.-]+graph(?![\w-])|"
        + @"(?<![\w-])(?:project|dependency|code)[ /\\-]+graphs?(?![\w-])|"
        + @"(?<![\w-])(?:graph|dependency)[ /\\-]+paths?(?![\w-])|"
        + @"(?<![\w-])(?:project|dependency|graph)[ /\\-]+cycles?(?![\w-])|"
        + @"(?<![\w-])impact[ /\\-]+analysis(?![\w-])|"
        + @"(?<![\w-])(?:edit|modify|delete|rename|update)\s+(?:the\s+)?"
        + @"(?:<path>|files?|source|documents?|projects?|solutions?|"
        + @"workspace|repository|declarations?|symbols?|code)(?![\w-])|"
        + @"(?<![\w-])change\s+(?:the\s+)?(?:<path>|workspace|repository|"
        + @"declarations?|symbols?|code)(?![\w-])";

    private const string DeclaredPathCuePattern =
        @"(?ix)(?:\b(?:path|file|directory|folder|document|source|repository|"
        + @"in|from|at|edit|modify|delete|rename|update|change)\b"
        + @"(?:\s+(?:the|declared|repository|source))*"
        + @"\s*[:=]?\s*)$";

    private static readonly HashSet<string> Pre06UnavailableBarePathSegments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "analysis",
            "callee",
            "callees",
            "caller",
            "callers",
            "code",
            "cycle",
            "cycles",
            "dependency",
            "derived-type",
            "derived-types",
            "graph",
            "graphs",
            "impact",
            "implementation",
            "implementations",
            "inheritance",
            "mutation",
            "mutations",
            "override",
            "overrides",
            "path",
            "paths",
            "project",
            "reference",
            "references",
            "relationship",
            "relationships",
            "rename",
            "renames",
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
        var isPre06 = System.Version.Parse(milestone)
            < new System.Version(0, 6, 0);
        var requiredCapabilities = ValidateIdentifiers(
            task.RequiredCapabilities,
            $"Task '{id}' required capability",
            requireNonEmpty: true);
        if (isPre06 && requiredCapabilities.Any(IsUnavailableBefore06))
        {
            throw new AgentTaskCorpusException(
                $"Task '{id}' cannot require unshipped relationship or mutation capabilities before milestone 0.6.0.");
        }
        var prompt = ValidateNeutralText(
            task.Prompt,
            $"Task '{id}' prompt");
        var repositoryValidation = await ValidateRepositoryAsync(
            id,
            task.Repository,
            corpusDirectory,
            cancellationToken);
        var repository = repositoryValidation.State;
        if (isPre06
            && ContainsUnavailableExpectationBefore06(
                prompt,
                repositoryValidation.DeclaredPathTokens))
        {
            throw new AgentTaskCorpusException(
                $"Task '{id}' prompt cannot require unshipped relationship, graph, impact, or mutation outcomes before milestone 0.6.0.");
        }
        var applicability = ValidateApplicability(id, task.Applicability);
        var execution = ValidateExecution(id, task.Execution);
        if (isPre06
            && execution.PermittedTools.Any(IsWorkspaceMutationTool))
        {
            throw new AgentTaskCorpusException(
                $"Task '{id}' cannot permit workspace mutation tools before milestone 0.6.0.");
        }
        var successOracle = ValidateSuccessOracle(
            id,
            task.SuccessOracle);
        if (isPre06
            && successOracle.ExpectedFacts.Any(
                fact => ContainsUnavailableExpectationBefore06(
                    fact,
                    repositoryValidation.DeclaredPathTokens)))
        {
            throw new AgentTaskCorpusException(
                $"Task '{id}' expected facts cannot require unshipped relationship, graph, impact, or mutation outcomes before milestone 0.6.0.");
        }
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

    private static async ValueTask<ValidatedRepositoryState>
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

        return new ValidatedRepositoryState(
            new AgentTaskRepositoryState(
                fixtureManifest,
                fixtureName,
                repository.FixtureSeed.Value,
                contentHash,
                "materialized-clean"),
            CreateDeclaredPathTokens(plan.Files));
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

    private static bool IsUnavailableBefore06(string capability) =>
        Pre06UnavailableCapabilityFamilies.Any(family =>
            string.Equals(capability, family, StringComparison.Ordinal)
            || capability.StartsWith(
                family + ".",
                StringComparison.Ordinal));

    private static bool IsWorkspaceMutationTool(string tool)
    {
        var segments = tool.Split(
            ['.', '-', ':'],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments[0] is "mutation" or "rename")
        {
            return true;
        }

        return segments[0] is "workspace" or "repository"
            && segments.Skip(1).Any(static segment => segment is
                "apply" or
                "delete" or
                "edit" or
                "modify" or
                "mutation" or
                "rename" or
                "write");
    }

    private static bool ContainsUnavailableExpectationBefore06(
        string value,
        IReadOnlyList<string> declaredPathTokens)
    {
        var withoutRepositoryPaths = value;
        foreach (var pathToken in declaredPathTokens)
        {
            withoutRepositoryPaths = Regex.Replace(
                withoutRepositoryPaths,
                $@"(?<![\w.-]){Regex.Escape(pathToken)}(?![\w-])",
                match => IsDeclaredPathUsage(
                        withoutRepositoryPaths,
                        match.Index)
                    ? "<path>"
                    : match.Value,
                RegexOptions.CultureInvariant);
        }

        return Regex.IsMatch(
            withoutRepositoryPaths,
            Pre06UnavailableExpectationPattern,
            RegexOptions.CultureInvariant);
    }

    private static bool IsDeclaredPathUsage(string value, int pathIndex) =>
        Regex.IsMatch(
            value[..pathIndex],
            DeclaredPathCuePattern,
            RegexOptions.CultureInvariant);

    private static IReadOnlyList<string> CreateDeclaredPathTokens(
        IReadOnlyList<FixtureMaterializedFile> files)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var segments = file.RelativePath.Split('/');
            for (var length = 1; length <= segments.Length; length++)
            {
                AddDeclaredPathToken(
                    tokens,
                    string.Join('/', segments.Take(length)));
            }

            AddDeclaredPathToken(tokens, segments[^1]);
        }

        return Array.AsReadOnly(
            tokens
                .OrderByDescending(static token => token.Length)
                .ThenBy(static token => token, StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddDeclaredPathToken(
        ISet<string> tokens,
        string token)
    {
        if (!token.Contains('/')
            && Pre06UnavailableBarePathSegments.Contains(token))
        {
            return;
        }

        tokens.Add(token);
        if (token.Contains('/'))
        {
            tokens.Add(token.Replace('/', '\\'));
        }
    }

    private static string ValidateRelativePath(string? value, string field)
    {
        if (!PortableRelativePath.TryNormalize(
                value,
                normalizeBackslashes: false,
                out var normalized))
        {
            throw new AgentTaskCorpusException(
                $"{field} must be a portable relative path.");
        }

        return normalized;
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

    private sealed record ValidatedRepositoryState(
        AgentTaskRepositoryState State,
        IReadOnlyList<string> DeclaredPathTokens);

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
