namespace DotNetAxi.Axi;

public sealed class AgentCommandGuidance
{
    internal AgentCommandGuidance(
        string invocation,
        string homeInvocation,
        string helpInvocation,
        string versionInvocation,
        string authority,
        IEnumerable<string> boundaries,
        IEnumerable<string> useWhen,
        IEnumerable<string> skipWhen,
        IEnumerable<string> activationFlow,
        IEnumerable<string> invocationFlow,
        string capabilityCondition,
        IEnumerable<string> capabilityFlow,
        IEnumerable<string> sourceDiscoveryFlow,
        IEnumerable<string> symbolContextFlow,
        IEnumerable<string> safetyFlow,
        string completion,
        string evidenceReport)
    {
        Invocation = RequiredText(invocation, nameof(invocation));
        HomeInvocation = RequiredText(homeInvocation, nameof(homeInvocation));
        HelpInvocation = RequiredText(helpInvocation, nameof(helpInvocation));
        VersionInvocation = RequiredText(versionInvocation, nameof(versionInvocation));
        Authority = RequiredText(authority, nameof(authority));
        Boundaries = Copy(boundaries, nameof(boundaries));
        UseWhen = Copy(useWhen, nameof(useWhen));
        SkipWhen = Copy(skipWhen, nameof(skipWhen));
        ActivationFlow = Copy(activationFlow, nameof(activationFlow));
        InvocationFlow = Copy(invocationFlow, nameof(invocationFlow));
        CapabilityCondition = RequiredText(
            capabilityCondition,
            nameof(capabilityCondition));
        CapabilityFlow = Copy(capabilityFlow, nameof(capabilityFlow));
        SourceDiscoveryFlow = Copy(
            sourceDiscoveryFlow,
            nameof(sourceDiscoveryFlow));
        SymbolContextFlow = Copy(
            symbolContextFlow,
            nameof(symbolContextFlow));
        SafetyFlow = Copy(safetyFlow, nameof(safetyFlow));
        Completion = RequiredText(completion, nameof(completion));
        EvidenceReport = RequiredText(evidenceReport, nameof(evidenceReport));
    }

    public string Invocation { get; }

    public string HomeInvocation { get; }

    public string HelpInvocation { get; }

    public string VersionInvocation { get; }

    public string Authority { get; }

    public IReadOnlyList<string> Boundaries { get; }

    public IReadOnlyList<string> UseWhen { get; }

    public IReadOnlyList<string> SkipWhen { get; }

    public IReadOnlyList<string> ActivationFlow { get; }

    public IReadOnlyList<string> InvocationFlow { get; }

    public string CapabilityCondition { get; }

    public IReadOnlyList<string> CapabilityFlow { get; }

    public IReadOnlyList<string> SourceDiscoveryFlow { get; }

    public IReadOnlyList<string> SymbolContextFlow { get; }

    public IReadOnlyList<string> SafetyFlow { get; }

    public string Completion { get; }

    public string EvidenceReport { get; }

    private static IReadOnlyList<string> Copy(
        IEnumerable<string> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);

        var copy = values
            .Select(value => RequiredText(value, parameterName))
            .ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException(
                "At least one guidance item is required.",
                parameterName);
        }

        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException(
                "Guidance items must be distinct.",
                parameterName);
        }

        return Array.AsReadOnly(copy);
    }

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

public static class AgentGuidanceCatalog
{
    public const string SkillPackageVersion = "0.5.0";

    public const string SkillName = "dotnet-axi";

    public const string SkillDescription =
        "Use dotnet-axi for deterministic .NET repository evidence. Trigger for finding .NET files by path, searching literal or regular-expression text, locating stable C# syntax shapes or declarations, resolving symbol identity, retrieving bounded source context, inspecting workspace, semantic, impact, or analysis evidence, and validating completion. When a controlled benchmark supplies the local feed, route applicable discovery through dnx dnaxi@0.5.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- <command>; skip non-.NET work and direct reads of already-known files.";

    public static AgentCommandGuidance Command { get; } =
        CreateCommand(SkillPackageVersion);

    public static AgentCommandGuidance ForVersion(string exactVersion)
    {
        if (string.IsNullOrWhiteSpace(exactVersion) ||
            exactVersion.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '.' or '-' or '+')))
        {
            throw new ArgumentException(
                "A package-safe exact version is required.",
                nameof(exactVersion));
        }

        return CreateCommand(exactVersion);
    }

    private static AgentCommandGuidance CreateCommand(string exactVersion)
    {
        var commandPrefix =
            $"dnx dnaxi@{exactVersion} --verbosity quiet --";
        var invocation = $"{commandPrefix} <command>";
        var homeInvocation = commandPrefix;
        var helpInvocation = $"{commandPrefix} --help";
        var versionInvocation = $"{commandPrefix} --version";
        const string authority =
            "Treat the invoked version's structured help, version, and reported capabilities as authoritative. Never use a command or option that it does not expose.";

        return new AgentCommandGuidance(
        invocation: invocation,
        homeInvocation: homeInvocation,
        helpInvocation: helpInvocation,
        versionInvocation: versionInvocation,
        authority: authority,
        boundaries:
        [
            "Use this skill as an on-demand guide. Do not install hooks, edit agent configuration, include live workspace state, or change the host sandbox, approvals, trust, or network policy.",
            "Treat portable discovery by a harness as skill availability, not evidence that the harness is a supported setup adapter.",
        ],
        useWhen:
        [
            "Use dotnet-axi for a .NET workspace when deterministic structured evidence is useful.",
            "Use only a workspace, source, semantic, impact, analysis, or validation capability reported by the invoked version.",
        ],
        skipWhen:
        [
            "Skip dotnet-axi for non-.NET work.",
            "Skip dotnet-axi when a direct read of an already-known file is the smaller operation.",
            "Skip any capability that the invoked version does not report and use an available direct tool instead.",
        ],
        activationFlow:
        [
            $"For .NET file, literal, regular-expression, stable-syntax, declaration, or bounded symbol-context discovery, run the matching route through `{commandPrefix}` when the invoked version reports it.",
            "Invoke known source-discovery routes directly; do not add a help probe before a known route. Inspect only the narrowest relevant help once when no documented route or option applies.",
            "Read an already-known file directly when that is smaller. If the required capability is unavailable, use an available direct tool and report the gap.",
        ],
        invocationFlow:
        [
            $"Default to one-shot `{invocation}`. Keep the exact version pin and do not require a permanent installation.",
            $"When a controlled harness supplies `DNAXI_LOCAL_FEED`, keep candidate resolution source-pinned with `dnx dnaxi@{exactVersion} --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- <command>`.",
            "Use a global `dnaxi <command>` or local `dotnet tool run dnaxi -- <command>` only when that persistent invocation was explicitly selected and verified.",
            $"Use `{homeInvocation}` for a passive workspace summary, `{helpInvocation}` only when command grammar is unknown, and `{versionInvocation}` when version identity matters.",
            authority,
            "Remember that `dnx` package resolution may download or restore the tool. Keep that network operation explicit and subject to host policy.",
        ],
        capabilityCondition:
            "Apply this flow only when the invoked version reports the relevant capability.",
        capabilityFlow:
        [
            "Use text search for literals.",
            "Use stable syntax queries for syntax shape.",
            "Use declaration search and resolved symbol operations for exact source identity.",
            "Inspect impact before public changes.",
            "Request bounded context.",
            "Run fast validation during work.",
            "Run standard validation before completion.",
        ],
        sourceDiscoveryFlow:
        [
            "Use the exact routes below directly when the invoked version reports them. Do not run a redundant help command before a known file, literal, regular-expression, or stable-syntax route. If a route is unavailable, use an available direct tool and report the capability gap instead of inventing a command.",
            $"Find a file by normalized path with `{commandPrefix} search file '<path-fragment>' --path <scope> --limit 20`. If the exact file is already known and a direct read is smaller, read it directly.",
            $"Find literal text with `{commandPrefix} search text '<literal>' --path <scope> --limit 20`.",
            $"Find a .NET regular expression with `{commandPrefix} search text '<dotnet-regex>' --regex --path <scope> --limit 20`; narrow the expression or path when a file times out.",
            $"Find a known C# syntax shape directly with one of these stable routes: `{commandPrefix} search syntax invocation --name SaveChangesAsync --path <scope> --limit 20`, `{commandPrefix} search syntax class --attribute <attribute> --path <scope> --limit 20`, `{commandPrefix} search syntax object-creation --type <type> --path <scope> --limit 20`, or `{commandPrefix} search syntax catch --type <type> --path <scope> --limit 20`.",
            "When a request requires object-creation syntax to expose the requested type, keep only `type_match: exact`; do not report `type_match: unresolved` target-typed `new()` as a requested-type match because resolving it requires compiler semantics.",
            "When a bounded result reports complete coverage, return its requested facts directly without a redundant help probe or matched-file reread.",
            "Treat stable syntax results as syntax candidates, never as compiler-verified symbol or type identity.",
            "Text search may use compatible `rg` acceleration. When that optional engine is absent, incompatible, or unsuitable for the query, `search text` degrades to its built-in engine with the same stable command behavior.",
            "Keep discovery bounded with a narrow `--path` and `--limit`. If output is truncated, follow its `retrieval_command` only when the remaining rows are needed; otherwise use the returned path or match to issue the next narrower file, text, or syntax query instead of dumping broad source.",
        ],
        symbolContextFlow:
        [
            $"Find C# declarations with `{commandPrefix} search symbol '<name>' --solution <solution> --fields id,kind,signature,owning_projects,variant_count,variants --limit 20`. Select `--solution` or `--project` explicitly when a repository has multiple entry points, and add `--include-tests` when the target may be test-only.",
            "Treat `search symbol` rows, owner projects, and framework/configuration variants as passive declaration candidates with unresolved compiler meaning. Preserve all reported variants; do not select one implicitly or call the row compiler-verified.",
            $"When compiler proof of a supported syntax construct is required and repository code execution is allowed, rerun its stable syntax query with `--verify`, for example `{commandPrefix} search syntax invocation --name SaveChangesAsync --path <scope> --verify --limit 20`. Report each construct and owner/framework variant as `verified`, `rejected`, or `unresolved`; do not generalize that proof into a different symbol claim.",
            $"Resolve one selected canonical `symbol/v2` identity with `{commandPrefix} show symbol '<symbol/v2/...>' --solution <solution> --max-chars 2000`. Reuse the complete discovery scope, including project, paths, tests, and generated-source eligibility. If the ID is stale or ambiguous, follow the structured correction and bounded replacement candidates, rerun the reported symbol query when needed, and select a replacement explicitly; never silently bind it.",
            $"Retrieve an exact document span with `{commandPrefix} show document '<path>' --start-line <line> --end-line <line> --max-chars 4000`. Follow its larger-budget recovery only when omitted characters matter; use `--full` only for an explicitly required complete document.",
            $"Inspect source structure with `{commandPrefix} outline '<path-or-symbol>' --limit 100`. Keep symbol scope consistent, and use the reported full retrieval command only when omitted outline items matter.",
            $"Compose bounded symbol evidence with `{commandPrefix} context symbol '<symbol/v2/...>' --include declaration,owner,document,outline --max-chars 12000`. Reuse the selected symbol scope. Increase the budget or use `--full` only when the omitted whole sections are required.",
            "In 0.5.0, `context symbol` supports only `declaration`, `owner`, `document`, and `outline`. Treat `references`, `callers`, `callees`, `tests`, implementations, and other relationship or graph requests as unavailable capability corrections; do not invent commands, sections, or conclusions.",
        ],
        safetyFlow:
        [
            "Start with passive discovery and inspect command classification before allowing repository-code execution, network access, or writes.",
            "Treat dnx package resolution as an explicit operation that may require network access; never bypass the host policy.",
            "Keep evidence scoped and distinguish verified facts from candidates, uncertainty, and incomplete coverage.",
            "Treat denied filesystem, process, or network access as a host restriction; retry only after a confirmed policy change and do not loop.",
        ],
        completion:
            $"Do not claim completion solely because files changed. When the invoked version exposes validate, use the strongest applicable `{commandPrefix} validate` evidence available within the requested scope. Otherwise run the strongest applicable project validation and report the evidence and any gaps.",
        evidenceReport:
            "Report the command, requested scope, result status, resolution, coverage, confidence when applicable, and any remaining blocker or validation gap.");
    }
}
