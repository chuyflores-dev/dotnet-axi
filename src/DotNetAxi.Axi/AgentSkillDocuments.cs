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
    public const string AdvancedEvidenceRelativePath =
        "skills/dotnet-axi/references/advanced-evidence.md";

    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<GeneratedAgentSkillDocument> Render()
    {
        var guidance = AgentGuidanceCatalog.Command;

        return Array.AsReadOnly(
        [
            new GeneratedAgentSkillDocument(
                SkillRelativePath,
                RenderSkill(guidance)),
            new GeneratedAgentSkillDocument(
                AdvancedEvidenceRelativePath,
                RenderAdvancedEvidence(guidance)),
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
        var commandPrefix =
            $"dnx dnaxi@{AgentGuidanceCatalog.SkillPackageVersion} --verbosity quiet --";
        var sourcePinnedPrefix =
            $"dnx dnaxi@{AgentGuidanceCatalog.SkillPackageVersion} --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet --";
        var lines = new List<string>
        {
            "---",
            $"name: {AgentGuidanceCatalog.SkillName}",
            $"description: {AgentGuidanceCatalog.SkillDescription}",
            "---",
            string.Empty,
            "# Use dotnet-axi",
            string.Empty,
            "## Invoke safely",
            string.Empty,
            $"- When `DNAXI_LOCAL_FEED` is set, use `{sourcePinnedPrefix} <command>`.",
            $"- Otherwise use the exact installed version: `{commandPrefix} <command>`.",
            "- Do not install hooks, edit agent configuration, or change sandbox, approval, trust, or network policy.",
            "- Run documented routes directly. Use narrow help once only when the required grammar is unknown.",
            "- When the task does not provide the exact target file or declaration, use one narrow `dnaxi` discovery route before opening source; do not guess a path from names.",
            "- Read an already-known file directly when that is smaller. Fall back to ordinary tools when the invoked version does not expose the required capability.",
            string.Empty,
            "## Discover source",
            string.Empty,
            "Use a narrow `--path` and bounded `--limit`:",
            string.Empty,
            "- File path: `search file '<path-fragment>' --path <scope> --limit 20`",
            "- Literal text: `search text '<literal>' --path <scope> --limit 20`",
            "- .NET regex: `search text '<dotnet-regex>' --regex --path <scope> --limit 20`",
            "- Invocation: `search syntax invocation --name <method> --path <scope> --limit 20`",
            "- Attributed class: `search syntax class --attribute <attribute> --path <scope> --limit 20`",
            "- Object creation: `search syntax object-creation --type <type> --path <scope> --limit 20`",
            "- Catch clause: `search syntax catch --type <type> --path <scope> --limit 20`",
            "- Declaration owner: `search symbol '<name>' --project <csproj> --fields id,kind,signature,owning_projects,variant_count,variants --limit 20`; use `--solution <sln>` instead of `--project` when solution scope is required, never both",
            string.Empty,
            "Increase the limit only when exhaustive output requires it. Follow a reported `retrieval_command` only when omitted rows matter. When coverage is complete, use the returned facts without a redundant help probe or matched-file reread.",
            string.Empty,
            "Treat syntax results as syntax candidates, not compiler-proven identity. For object creation, keep only `type_match: exact`; do not report `type_match: unresolved` target-typed `new()` unless compiler verification is explicitly requested and allowed.",
            string.Empty,
            "## Use advanced evidence on demand",
            string.Empty,
            "Read [references/advanced-evidence.md](references/advanced-evidence.md) only when the task requires symbol identity, document spans, outlines, composed context, or compiler verification beyond declaration ownership.",
            "When a target is identified by symbol name, namespace, or owner project rather than exact file, use its Roslyn/MSBuild declaration search; do not substitute text search for semantic ownership.",
            string.Empty,
            "## Preserve evidence",
            string.Empty,
            "- Start passive and keep commands scoped and bounded.",
            "- Treat package acquisition, repository-code execution, network access, and writes as explicit operations subject to host policy.",
            "- Report the command, scope, result status, coverage, and any uncertainty or validation gap.",
            "- Do not retry denied access until policy changes, and never invent unsupported commands or conclusions.",
        };

        return JoinLines(lines);
    }

    private static string RenderAdvancedEvidence(
        AgentCommandGuidance guidance)
    {
        var lines = new List<string>
        {
            "# Advanced dnaxi evidence",
            string.Empty,
            "Read this reference only for declarations, symbol identity, bounded source context, or compiler verification.",
            string.Empty,
            "## Follow reported capabilities",
            string.Empty,
            guidance.CapabilityCondition,
            string.Empty,
            guidance.Authority,
            string.Empty,
        };
        AddBullets(lines, guidance.CapabilityFlow);

        lines.AddRange(
        [
            string.Empty,
            "## Resolve symbols and compose bounded context",
            string.Empty,
        ]);
        AddNumbered(lines, guidance.SymbolContextFlow);

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
