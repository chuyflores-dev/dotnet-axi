using System.Text;
using DotNetAxi.Axi;
using DotNetAxi.DotNet;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli.Tests;

public sealed class AgentSkillGenerationTests
{
    [Fact]
    public void Generated_documents_are_deterministic_bounded_and_portable()
    {
        var first = AgentSkillDocuments.Render().ToArray();
        var second = AgentSkillDocuments.Render().ToArray();

        Assert.Equal(first, second);
        Assert.Equal(
            [
                AgentSkillDocuments.SkillRelativePath,
                AgentSkillDocuments.AdvancedEvidenceRelativePath,
            ],
            first.Select(static document => document.RelativePath));

        var skill = first.Single(document =>
            document.RelativePath == AgentSkillDocuments.SkillRelativePath).Content;
        var advanced = first.Single(document =>
            document.RelativePath ==
            AgentSkillDocuments.AdvancedEvidenceRelativePath).Content;

        AssertPortableMetadata(skill);
        AssertBounded(skill, maximumLines: 60, maximumUtf8Bytes: 5_000);
        AssertBounded(advanced, maximumLines: 80, maximumUtf8Bytes: 12_000);
        Assert.DoesNotContain('\r', skill);
        Assert.DoesNotContain('\r', advanced);
        Assert.EndsWith("\n", skill);
        Assert.EndsWith("\n", advanced);

        var guidance = AgentGuidanceCatalog.Command;
        Assert.Equal(
            "dnx dnaxi@0.5.0 --verbosity quiet -- <command>",
            guidance.Invocation);
        Assert.DoesNotContain("<exact-version>", skill);
        Assert.Contains(guidance.Invocation, skill);
        Assert.Contains(
            $"dnx dnaxi@{AgentGuidanceCatalog.SkillPackageVersion} --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- <command>",
            skill);
        Assert.Contains(guidance.Authority, advanced);
        Assert.Contains(guidance.CapabilityCondition, advanced);
        Assert.Contains(guidance.Completion, advanced);
        Assert.Contains(
            "does not include a `validate` route",
            advanced,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "-- validate",
            skill + advanced,
            StringComparison.Ordinal);
        Assert.Contains("references/advanced-evidence.md", skill);
        Assert.DoesNotContain("Codex", skill);
        Assert.Contains("## Invoke safely", skill);
        Assert.Contains("Run documented routes directly", skill);
        Assert.Contains(
            "does not provide the exact target file or declaration",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "first run Roslyn/MSBuild-backed search symbol",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "do not substitute text search for semantic ownership",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("search file --help", skill);
        Assert.DoesNotContain("search text --help", skill);
        Assert.DoesNotContain("search syntax --help", skill);
        Assert.DoesNotContain("search syntax invocation --help", skill);
        Assert.Contains(
            "use the returned facts without a redundant help probe or matched-file reread",
            skill);
        Assert.Contains(
            "search symbol '<name>' --project <csproj>",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "never both",
            skill,
            StringComparison.Ordinal);

        AssertSourceDiscoveryGuidance(skill);
        AssertSymbolContextGuidance(advanced);

        Assert.DoesNotContain("dnx dotnet-axi", skill);
        Assert.DoesNotContain("dnx dnaxi --", skill);
        Assert.DoesNotContain("dnx dotnet-axi -- search", skill);
        Assert.DoesNotContain("dnx dotnet-axi -- analyze", skill);
        Assert.DoesNotContain("dnx dotnet-axi -- validate", skill);
        foreach (var codexWorkerToken in new[]
                 {
                     "codex exec",
                     "thread.started",
                     "subagents",
                     "--sandbox",
                     "--ephemeral",
                     "--json",
                     "--output-last-message",
                 })
        {
            Assert.DoesNotContain(codexWorkerToken, skill + advanced);
        }
        const string shortOutputFlagPattern =
            @"(?<![\p{L}\p{N}])-o(?=$|[\s`])";
        Assert.Matches(shortOutputFlagPattern, "Use `-o result.txt`.");
        Assert.DoesNotMatch(shortOutputFlagPattern, skill + advanced);

    }

    [Fact]
    public void Committed_generated_documents_are_current()
    {
        var stale = AgentSkillDocuments.FindStale(RepositoryRoot());

        Assert.Empty(stale);
    }

    [Fact]
    public void Generation_check_reports_changed_and_missing_documents()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            AgentSkillDocuments.Write(root);
            File.AppendAllText(
                Path.Combine(
                    root,
                    AgentSkillDocuments.SkillRelativePath),
                "stale\n",
                new UTF8Encoding(false));
            var stale = AgentSkillDocuments.FindStale(root);

            Assert.Equal(
                [
                    new StaleGeneratedDocument(
                        AgentSkillDocuments.SkillRelativePath,
                        GeneratedDocumentState.Different),
                ],
                stale);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Portable_skill_is_discoverable_after_repository_and_user_install()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(
                RepositoryRoot(),
                "skills",
                AgentGuidanceCatalog.SkillName);
            var repositoryInstall = Path.Combine(
                root,
                "repository",
                ".agents",
                "skills",
                AgentGuidanceCatalog.SkillName);
            var userInstall = Path.Combine(
                root,
                "user-home",
                ".agents",
                "skills",
                AgentGuidanceCatalog.SkillName);

            InstallSkill(source, repositoryInstall);
            InstallSkill(source, userInstall);

            AssertInstalledSkill(source, repositoryInstall);
            AssertInstalledSkill(source, userInstall);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Home_and_structured_help_do_not_repeat_installed_skill_guidance()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var homeOutput = new StringWriter();
            var homeError = new StringWriter();
            var homeHost = CliApplication.Create(
                homeOutput,
                homeError,
                () => new HomeInvocationContext(
                    root,
                    Path.Combine(root, "dnaxi"),
                    root),
                static () => new WorkspaceDiscoverer(),
                static () => new WorktreeStateInspector(
                    new ProcessRunner()));

            var homeExitCode = await homeHost.InvokeAsync([]);

            var helpOutput = new StringWriter();
            var helpError = new StringWriter();
            var helpHost = CliApplication.Create(
                helpOutput,
                helpError,
                static () => throw new InvalidOperationException(
                    "Help must not capture home context."),
                static () => throw new InvalidOperationException(
                    "Help must not discover a workspace."),
                static () => throw new InvalidOperationException(
                    "Help must not inspect Git."));

            var helpExitCode = await helpHost.InvokeAsync(["--help"]);

            Assert.Equal(0, homeExitCode);
            Assert.Equal(0, helpExitCode);
            Assert.Equal(string.Empty, homeError.ToString());
            Assert.Equal(string.Empty, helpError.ToString());
            Assert.DoesNotContain("guidance:", homeOutput.ToString());
            Assert.DoesNotContain("guidance:", helpOutput.ToString());
            Assert.DoesNotContain("next_steps", homeOutput.ToString());
            Assert.DoesNotContain("next_steps", helpOutput.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertPortableMetadata(string skill)
    {
        var lines = skill.Split('\n');
        Assert.Equal("---", lines[0]);
        Assert.Equal($"name: {AgentGuidanceCatalog.SkillName}", lines[1]);
        Assert.Equal(
            $"description: {AgentGuidanceCatalog.SkillDescription}",
            lines[2]);
        Assert.Equal("---", lines[3]);
        Assert.Contains(
            "first run Roslyn/MSBuild-backed search symbol",
            lines[2]);
        Assert.Contains(
            $"dnx dnaxi@{AgentGuidanceCatalog.SkillPackageVersion} --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- <command>",
            lines[2]);
        Assert.DoesNotContain("display_name:", skill);
        Assert.DoesNotContain("allowed-tools:", skill);
    }

    private static void AssertBounded(
        string content,
        int maximumLines,
        int maximumUtf8Bytes)
    {
        Assert.InRange(content.Split('\n').Length, 1, maximumLines);
        Assert.InRange(
            Encoding.UTF8.GetByteCount(content),
            1,
            maximumUtf8Bytes);
    }

    private static void InstallSkill(string source, string destination)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationFile)
                ?? throw new InvalidOperationException(
                    $"Installed path '{destinationFile}' has no directory."));
            File.Copy(sourceFile, destinationFile);
        }
    }

    private static void AssertInstalledSkill(
        string source,
        string installation)
    {
        var skillPath = Path.Combine(installation, "SKILL.md");
        var advancedPath = Path.Combine(
            installation,
            "references",
            "advanced-evidence.md");
        Assert.True(File.Exists(skillPath));
        Assert.True(File.Exists(advancedPath));
        AssertPortableMetadata(File.ReadAllText(skillPath));
        AssertSourceDiscoveryGuidance(File.ReadAllText(skillPath));
        AssertSymbolContextGuidance(File.ReadAllText(advancedPath));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(source, "SKILL.md")),
            File.ReadAllBytes(skillPath));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(
                source,
                "references",
                "advanced-evidence.md")),
            File.ReadAllBytes(advancedPath));
        Assert.Equal(
            ["SKILL.md", "references/advanced-evidence.md"],
            Directory.EnumerateFiles(
                    installation,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => Path
                    .GetRelativePath(installation, path)
                    .Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal));
    }

    private static void AssertSourceDiscoveryGuidance(string content)
    {
        foreach (var required in new[]
                 {
                     "search file '<path-fragment>'",
                     "search text '<literal>'",
                     "search text '<dotnet-regex>' --regex",
                     "search syntax invocation --name <method>",
                     "search syntax class --attribute <attribute>",
                     "search syntax catch --type <type>",
                     "search syntax object-creation --type <type>",
                     "keep only `type_match: exact`",
                     "do not report `type_match: unresolved` target-typed `new()`",
                     "--path <scope> --limit 20",
                     "Fall back to ordinary tools",
                     "retrieval_command",
                     "syntax candidates, not compiler-proven",
                 })
        {
            Assert.Contains(required, content);
        }
    }

    private static void AssertSymbolContextGuidance(string content)
    {
        foreach (var required in new[]
                {
                    "search symbol",
                    "search implementations",
                    "show symbol",
                    "show document",
                    "--start-line <line> --end-line <line>",
                    "outline '<path-or-symbol>'",
                     "context symbol",
                     "--fields id,kind,signature,owning_projects,variant_count,variants",
                     "--solution <solution>",
                     "--project",
                     "--include-tests",
                     "passive declaration candidates",
                     "framework/configuration variants",
                     "--verify",
                     "`verified`, `rejected`, or `unresolved`",
                     "structured correction and bounded replacement candidates",
                     "never silently bind",
                     "--max-chars 2000",
                     "--max-chars 12000",
                     "declaration,owner,document,outline",
                     "Increase the budget or use `--full`",
                     "unavailable capability corrections",
                 })
        {
            Assert.Contains(required, content);
        }

                foreach (var futureGraphCommand in new[]
                 {
                     "search references",
                     "show references",
                     "context references",
                     "-- references",
                 })
        {
            Assert.DoesNotContain(futureGraphCommand, content);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-agent-skill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                ".."));
}
