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
        SafetyFlow = Copy(safetyFlow, nameof(safetyFlow));
        Completion = RequiredText(completion, nameof(completion));
        EvidenceReport = RequiredText(evidenceReport, nameof(evidenceReport));
        Summary = new AgentCommandGuidanceSummary(
            Invocation,
            Authority,
            ActivationFlow);
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

    public IReadOnlyList<string> SafetyFlow { get; }

    public string Completion { get; }

    public string EvidenceReport { get; }

    public AgentCommandGuidanceSummary Summary { get; }

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

public sealed class AgentCommandGuidanceSummary
{
    internal AgentCommandGuidanceSummary(
        string invocation,
        string authority,
        IReadOnlyList<string> nextSteps)
    {
        Invocation = invocation;
        Authority = authority;
        NextSteps = nextSteps;
    }

    public string Invocation { get; }

    public string Authority { get; }

    public IReadOnlyList<string> NextSteps { get; }
}

public sealed class CodexAgentGuidance
{
    internal CodexAgentGuidance(
        IEnumerable<string> boundaries,
        IEnumerable<string> worktrees,
        IEnumerable<string> networkAndMetadata,
        IEnumerable<string> workerStartup,
        IEnumerable<string> nonInteractive,
        IEnumerable<string> recovery,
        string skillsLink,
        string repositoryInstructionsLink,
        string sandboxingLink,
        string approvalsLink,
        string worktreesLink,
        string subagentsLink,
        string nonInteractiveLink)
    {
        Boundaries = Copy(boundaries, nameof(boundaries));
        Worktrees = Copy(worktrees, nameof(worktrees));
        NetworkAndMetadata = Copy(
            networkAndMetadata,
            nameof(networkAndMetadata));
        WorkerStartup = Copy(workerStartup, nameof(workerStartup));
        NonInteractive = Copy(nonInteractive, nameof(nonInteractive));
        Recovery = Copy(recovery, nameof(recovery));
        SkillsLink = RequiredText(skillsLink, nameof(skillsLink));
        RepositoryInstructionsLink = RequiredText(
            repositoryInstructionsLink,
            nameof(repositoryInstructionsLink));
        SandboxingLink = RequiredText(sandboxingLink, nameof(sandboxingLink));
        ApprovalsLink = RequiredText(approvalsLink, nameof(approvalsLink));
        WorktreesLink = RequiredText(worktreesLink, nameof(worktreesLink));
        SubagentsLink = RequiredText(subagentsLink, nameof(subagentsLink));
        NonInteractiveLink = RequiredText(
            nonInteractiveLink,
            nameof(nonInteractiveLink));
    }

    public IReadOnlyList<string> Boundaries { get; }

    public IReadOnlyList<string> Worktrees { get; }

    public IReadOnlyList<string> NetworkAndMetadata { get; }

    public IReadOnlyList<string> WorkerStartup { get; }

    public IReadOnlyList<string> NonInteractive { get; }

    public IReadOnlyList<string> Recovery { get; }

    public string SkillsLink { get; }

    public string RepositoryInstructionsLink { get; }

    public string SandboxingLink { get; }

    public string ApprovalsLink { get; }

    public string WorktreesLink { get; }

    public string SubagentsLink { get; }

    public string NonInteractiveLink { get; }

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
                "At least one Codex guidance item is required.",
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
    public const string SkillPackageVersion = "0.4.0";

    public const string SkillName = "dotnet-axi";

    public const string SkillDescription =
        "Use dotnet-axi for deterministic .NET repository evidence. Trigger for finding .NET files by path, searching literal or regular-expression text, locating stable C# syntax shapes, inspecting workspace, semantic, impact, or analysis evidence, and validating completion. When a controlled benchmark supplies the local feed, route applicable source discovery through dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- <command>; skip non-.NET work and direct reads of already-known files.";

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

    public static CodexAgentGuidance Codex { get; } = new(
        boundaries:
        [
            "Treat the sandbox as the technical boundary and approvals as the mechanism for crossing it; changing the reviewer does not expand access.",
            "Request only the narrow scope needed for the operation and never recommend full access as an automatic recovery.",
            "Keep repository instructions durable and shared; do not place user-specific permission profiles or duplicate tool workflows in AGENTS.md.",
        ],
        worktrees:
        [
            "Run from the selected repository or worktree as an active writable workspace root for implementation.",
            "If an external worktree is not writable, request that exact root instead of redirecting build outputs into another checkout.",
            "Respect protected Git metadata and Git's one-mutable-branch-per-worktree rule.",
        ],
        networkAndMetadata:
        [
            "Prefer passive, network-free discovery first.",
            "Treat restore, dnx package download, and every other networked operation as explicit; require the host policy to allow the needed destination.",
            "Treat protected Git or agent configuration metadata, read-only source, denied network, and denied process launch as host restrictions rather than proof that dotnet-axi is unsupported.",
        ],
        workerStartup:
        [
            "Prefer native Codex subagents for clean-context delegation; they inherit the parent turn's sandbox and live approval overrides.",
            "Start standalone `codex exec` only from an owning host or automation boundary already permitted to initialize Codex and access the selected worktree; never launch it as a sandboxed child to escape the current boundary.",
            "Treat a denial before `thread.started` as no observed Codex thread identity, not proof that the launcher process exited; preserve the diagnostic and stop polling an event stream that never began.",
            "Before any replacement, observe the exact launcher process and confirm exit or terminate, wait for, and reap that child; event absence or silence never authorizes a duplicate, and retry still requires a confirmed boundary change.",
        ],
        nonInteractive:
        [
            "Choose the sandbox explicitly: use workspace-write for implementation and read-only for review.",
            "Prefer ephemeral JSONL execution and capture the final response separately.",
            "Bound both event-stream silence and total runtime.",
        ],
        recovery:
        [
            "Retry at most once, and only after a confirmed permission or policy change addresses the blocker.",
            "Otherwise stop and return the denied resource or operation, the governing host restriction, and the narrow access needed.",
            "Never widen access, rewrite protected metadata, redirect work to a different checkout, or enter an approval retry loop.",
        ],
        skillsLink: "https://learn.chatgpt.com/docs/build-skills",
        repositoryInstructionsLink:
            "https://learn.chatgpt.com/docs/agent-configuration/agents-md",
        sandboxingLink: "https://learn.chatgpt.com/docs/sandboxing",
        approvalsLink:
            "https://learn.chatgpt.com/docs/agent-approvals-security",
        worktreesLink:
            "https://learn.chatgpt.com/docs/environments/git-worktrees",
        subagentsLink:
            "https://learn.chatgpt.com/docs/agent-configuration/subagents",
        nonInteractiveLink:
            "https://learn.chatgpt.com/docs/non-interactive-mode");

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
            $"For .NET file, literal, regular-expression, or stable-syntax discovery, run the matching bounded `search` route through `{commandPrefix}` when the invoked version reports it.",
            $"Invoke known source-discovery routes directly; do not add a help probe before a known route. If its options are unknown, inspect that selected leaf once, for example `{commandPrefix} search file --help`, `{commandPrefix} search text --help`, or `{commandPrefix} search syntax invocation --help`. Use `{commandPrefix} search --help` only when the route itself is unknown.",
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
            "Use Roslyn operations for exact identity.",
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
            $"Find a known C# syntax shape directly with an exposed stable query, for example `{commandPrefix} search syntax invocation --name SaveChangesAsync --path <scope> --limit 20`. Inspect `{commandPrefix} search syntax --help` once only when the query kind is unknown, or the selected leaf such as `{commandPrefix} search syntax invocation --help` when its options are unknown.",
            "When a bounded result reports complete coverage, return its requested facts directly without a redundant help probe or matched-file reread.",
            "Treat stable syntax results as syntax candidates, never as compiler-verified symbol or type identity.",
            "Text search may use compatible `rg` acceleration. When that optional engine is absent, incompatible, or unsuitable for the query, `search text` degrades to its built-in engine with the same stable command behavior.",
            "Keep discovery bounded with a narrow `--path` and `--limit`. If output is truncated, follow its `retrieval_command` only when the remaining rows are needed; otherwise use the returned path or match to issue the next narrower file, text, or syntax query instead of dumping broad source.",
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
