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
        IEnumerable<string> invocationFlow,
        string capabilityCondition,
        IEnumerable<string> capabilityFlow,
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
        InvocationFlow = Copy(invocationFlow, nameof(invocationFlow));
        CapabilityCondition = RequiredText(
            capabilityCondition,
            nameof(capabilityCondition));
        CapabilityFlow = Copy(capabilityFlow, nameof(capabilityFlow));
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

    public IReadOnlyList<string> InvocationFlow { get; }

    public string CapabilityCondition { get; }

    public IReadOnlyList<string> CapabilityFlow { get; }

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
    public const string SkillName = "dotnet-axi";

    public const string SkillDescription =
        "Use dotnet-axi to obtain deterministic structured evidence for .NET workspaces when the invoked version reports the needed capability, including workspace or source discovery, semantic evidence, impact, analysis, and validation. Use for .NET repository investigation and completion checks; skip for non-.NET work and direct reads of already-known files.";

    public static AgentCommandGuidance Command { get; } = CreateCommand();

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

    private static AgentCommandGuidance CreateCommand()
    {
        const string invocation = "dnx dotnet-axi -- <command>";
        const string homeInvocation = "dnx dotnet-axi --";
        const string helpInvocation = "dnx dotnet-axi -- --help";
        const string versionInvocation = "dnx dotnet-axi -- --version";
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
        invocationFlow:
        [
            "Prefer a verified local or global `dnaxi <command>` invocation only when one is already available.",
            $"Otherwise run one shot with `{invocation}`. Do not require a permanent global installation.",
            $"Start with `{homeInvocation}` for the passive home view or `{helpInvocation}` for structured help. Use `{versionInvocation}` when version identity matters.",
            authority,
            "Remember that `dnx` package resolution may download or restore the tool. Keep that network operation explicit and subject to host policy.",
        ],
        capabilityCondition:
            "Apply this flow only when the invoked version reports the relevant capability.",
        capabilityFlow:
        [
            "Use text search for literals.",
            "Use structural search for syntax shape.",
            "Use Roslyn operations for exact identity.",
            "Inspect impact before public changes.",
            "Request bounded context.",
            "Run fast validation during work.",
            "Run standard validation before completion.",
        ],
        safetyFlow:
        [
            "Start with passive discovery and inspect command classification before allowing repository-code execution, network access, or writes.",
            "Treat dnx package resolution as an explicit operation that may require network access; never bypass the host policy.",
            "Keep evidence scoped and distinguish verified facts from candidates, uncertainty, and incomplete coverage.",
            "Treat denied filesystem, process, or network access as a host restriction; retry only after a confirmed policy change and do not loop.",
        ],
        completion:
            "Do not claim completion solely because files changed. When the invoked version exposes validate, use the strongest applicable `dnaxi validate` evidence available within the requested scope. Otherwise run the strongest applicable project validation and report the evidence and any gaps.",
        evidenceReport:
            "Report the command, requested scope, result status, resolution, coverage, confidence when applicable, and any remaining blocker or validation gap.");
    }
}
