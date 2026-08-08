using System.Text;

namespace DotNetAxi.Axi;

public enum GeneratedDocumentState
{
    Missing,
    Different,
}

public sealed record GeneratedAgentSkillDocument(
    string RelativePath,
    string Content);

public sealed record StaleGeneratedDocument(
    string RelativePath,
    GeneratedDocumentState State);

public static class AgentSkillDocuments
{
    public const string SkillRelativePath = "skills/dotnet-axi/SKILL.md";

    public const string CodexReferenceRelativePath =
        "skills/dotnet-axi/references/codex.md";

    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<GeneratedAgentSkillDocument> Render()
    {
        var guidance = AgentGuidanceCatalog.Command;
        var codex = AgentGuidanceCatalog.Codex;

        return Array.AsReadOnly(
        [
            new GeneratedAgentSkillDocument(
                SkillRelativePath,
                RenderSkill(guidance)),
            new GeneratedAgentSkillDocument(
                CodexReferenceRelativePath,
                RenderCodexReference(codex)),
        ]);
    }

    public static IReadOnlyList<StaleGeneratedDocument> FindStale(
        string repositoryRoot)
    {
        var root = ResolveRoot(repositoryRoot);
        var stale = new List<StaleGeneratedDocument>();

        foreach (var document in Render())
        {
            var path = ResolveDocumentPath(root, document.RelativePath);
            if (!File.Exists(path))
            {
                stale.Add(new StaleGeneratedDocument(
                    document.RelativePath,
                    GeneratedDocumentState.Missing));
                continue;
            }

            var expected = Utf8.GetBytes(document.Content);
            var actual = File.ReadAllBytes(path);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                stale.Add(new StaleGeneratedDocument(
                    document.RelativePath,
                    GeneratedDocumentState.Different));
            }
        }

        return stale.AsReadOnly();
    }

    public static void Write(string repositoryRoot)
    {
        var root = ResolveRoot(repositoryRoot);
        foreach (var document in Render())
        {
            var path = ResolveDocumentPath(root, document.RelativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException(
                    $"Generated path '{path}' has no directory."));
            File.WriteAllBytes(path, Utf8.GetBytes(document.Content));
        }
    }

    private static string RenderSkill(AgentCommandGuidance guidance)
    {
        var lines = new List<string>
        {
            "---",
            $"name: {AgentGuidanceCatalog.SkillName}",
            $"description: {AgentGuidanceCatalog.SkillDescription}",
            "---",
            string.Empty,
            "# Use dotnet-axi",
            string.Empty,
        };
        AddBullets(lines, guidance.Boundaries);

        lines.AddRange(
        [
            string.Empty,
            "## Route the task",
            string.Empty,
        ]);
        AddBullets(lines, guidance.UseWhen);
        AddBullets(lines, guidance.SkipWhen);

        lines.AddRange(
        [
            string.Empty,
            "## Start with dnx",
            string.Empty,
        ]);
        AddNumbered(lines, guidance.ActivationFlow);

        lines.AddRange(
        [
            string.Empty,
            "## Invoke on demand",
            string.Empty,
        ]);
        AddNumbered(lines, guidance.InvocationFlow);

        lines.AddRange(
        [
            string.Empty,
            "## Follow reported capabilities",
            string.Empty,
            guidance.CapabilityCondition,
            string.Empty,
        ]);
        AddBullets(lines, guidance.CapabilityFlow);

        lines.AddRange(
        [
            string.Empty,
            "## Discover source with bounded queries",
            string.Empty,
        ]);
        AddNumbered(lines, guidance.SourceDiscoveryFlow);

        lines.AddRange(
        [
            string.Empty,
            "## Preserve evidence and safety",
            string.Empty,
        ]);
        AddBullets(lines, guidance.SafetyFlow);

        lines.AddRange(
        [
            string.Empty,
            "## Complete with evidence",
            string.Empty,
            guidance.Completion,
            string.Empty,
            guidance.EvidenceReport,
            string.Empty,
            "## Load host-specific guidance only when needed",
            string.Empty,
            "When running under Codex, read [Codex sandbox operation](references/codex.md) before requesting access, operating in a worktree, or starting a noninteractive worker. Other agents must follow their own host controls and must not treat Codex flags as portable requirements.",
        ]);

        return JoinLines(lines);
    }

    private static string RenderCodexReference(CodexAgentGuidance guidance)
    {
        var lines = new List<string>
        {
            "# Codex sandbox operation",
            string.Empty,
            "Read this reference only when dotnet-axi is being used from Codex. The portable workflow remains in `../SKILL.md`.",
            string.Empty,
            "## Sandbox and approvals",
            string.Empty,
        };
        AddBullets(lines, guidance.Boundaries);

        lines.AddRange(
        [
            string.Empty,
            $"See [Codex sandboxing]({guidance.SandboxingLink}) and [agent approvals and security]({guidance.ApprovalsLink}).",
            string.Empty,
            "## Writable worktree roots",
            string.Empty,
        ]);
        AddBullets(lines, guidance.Worktrees);

        lines.AddRange(
        [
            string.Empty,
            $"See [Codex worktrees]({guidance.WorktreesLink}).",
            string.Empty,
            "## Network and protected metadata",
            string.Empty,
        ]);
        AddBullets(lines, guidance.NetworkAndMetadata);

        lines.AddRange(
        [
            string.Empty,
            "## Worker startup boundary",
            string.Empty,
        ]);
        AddBullets(lines, guidance.WorkerStartup);

        lines.AddRange(
        [
            string.Empty,
            $"See [Codex subagents]({guidance.SubagentsLink}).",
            string.Empty,
            "## Noninteractive workers",
            string.Empty,
        ]);
        AddBullets(lines, guidance.NonInteractive);

        lines.AddRange(
        [
            string.Empty,
            $"See [Codex non-interactive mode]({guidance.NonInteractiveLink}).",
            string.Empty,
            "## Bounded recovery",
            string.Empty,
        ]);
        AddBullets(lines, guidance.Recovery);

        lines.AddRange(
        [
            string.Empty,
            "## Instruction boundaries",
            string.Empty,
            $"Keep tool procedure in the skill and durable repository conventions in AGENTS.md. See [Codex skills]({guidance.SkillsLink}) and [repository instructions]({guidance.RepositoryInstructionsLink}).",
        ]);

        return JoinLines(lines);
    }

    private static void AddBullets(
        ICollection<string> lines,
        IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            lines.Add($"- {item}");
        }
    }

    private static void AddNumbered(
        ICollection<string> lines,
        IEnumerable<string> items)
    {
        var index = 1;
        foreach (var item in items)
        {
            lines.Add($"{index}. {item}");
            index++;
        }
    }

    private static string JoinLines(IEnumerable<string> lines) =>
        string.Join('\n', lines) + '\n';

    private static string ResolveRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Path.GetFullPath(repositoryRoot);
    }

    private static string ResolveDocumentPath(
        string repositoryRoot,
        string relativePath)
    {
        var path = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(repositoryRoot, path);
        if (Path.IsPathFullyQualified(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generated path '{relativePath}' escapes the repository root.");
        }

        return path;
    }
}
