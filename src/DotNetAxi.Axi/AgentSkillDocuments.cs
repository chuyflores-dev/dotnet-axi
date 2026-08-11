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
