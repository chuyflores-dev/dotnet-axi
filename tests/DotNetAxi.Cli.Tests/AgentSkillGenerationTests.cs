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
                AgentSkillDocuments.CodexReferenceRelativePath,
            ],
            first.Select(static document => document.RelativePath));

        var skill = first.Single(document =>
            document.RelativePath == AgentSkillDocuments.SkillRelativePath).Content;
        var codex = first.Single(document =>
            document.RelativePath == AgentSkillDocuments.CodexReferenceRelativePath).Content;

        AssertPortableMetadata(skill);
        AssertBounded(skill, maximumLines: 120, maximumUtf8Bytes: 9_000);
        AssertBounded(codex, maximumLines: 100, maximumUtf8Bytes: 8_000);
        Assert.DoesNotContain('\r', skill);
        Assert.DoesNotContain('\r', codex);
        Assert.EndsWith("\n", skill);
        Assert.EndsWith("\n", codex);

        var guidance = AgentGuidanceCatalog.Command;
        Assert.Equal(
            "dnx dnaxi@0.4.0 --verbosity quiet -- <command>",
            guidance.Invocation);
        Assert.DoesNotContain("<exact-version>", skill);
        Assert.Contains(guidance.Invocation, skill);
        Assert.Contains(guidance.HomeInvocation, skill);
        Assert.Contains(guidance.HelpInvocation, skill);
        Assert.Contains(guidance.VersionInvocation, skill);
        Assert.Contains(guidance.Authority, skill);
        Assert.Contains(guidance.CapabilityCondition, skill);
        Assert.Contains(guidance.Completion, skill);
        Assert.Contains("references/codex.md", skill);
        Assert.Contains("## Start with dnx", skill);
        Assert.Contains("Default to one-shot", skill);
        Assert.Contains("do not add a help probe before a known route", skill);
        Assert.Contains("search file --help", skill);
        Assert.Contains("search text --help", skill);
        Assert.Contains("search syntax invocation --help", skill);
        Assert.DoesNotContain("route or options are unknown", skill);
        Assert.DoesNotContain(
            "Before source discovery, inspect the invoked version's structured help",
            skill);
        foreach (var item in guidance.ActivationFlow)
        {
            Assert.Contains(item, skill);
        }
        foreach (var item in guidance.UseWhen
                     .Concat(guidance.SkipWhen)
                     .Concat(guidance.CapabilityFlow)
                     .Concat(guidance.SourceDiscoveryFlow)
                     .Concat(guidance.SafetyFlow))
        {
            Assert.Contains(item, skill);
        }

        AssertSourceDiscoveryGuidance(skill);

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
            Assert.DoesNotContain(codexWorkerToken, skill);
        }
        const string shortOutputFlagPattern =
            @"(?<![\p{L}\p{N}])-o(?=$|[\s`])";
        Assert.Matches(shortOutputFlagPattern, "Use `-o result.txt`.");
        Assert.DoesNotMatch(shortOutputFlagPattern, skill);

        Assert.Contains("writable workspace root", codex);
        Assert.Contains("protected Git metadata", codex);
        Assert.Contains("networked operation as explicit", codex);
        Assert.Contains("narrow scope", codex);
        Assert.Contains("workspace-write", codex);
        Assert.Contains("read-only", codex);
        Assert.Contains("native Codex subagents", codex);
        Assert.Contains("owning host or automation boundary", codex);
        Assert.Contains("denial before `thread.started`", codex);
        Assert.Contains("not proof that the launcher process exited", codex);
        Assert.Contains("stop polling an event stream that never began", codex);
        Assert.Contains("terminate, wait for, and reap that child", codex);
        Assert.Contains("event absence or silence never authorizes a duplicate", codex);
        Assert.Contains("Retry at most once", codex);
        Assert.Contains("retry loop", codex);
        Assert.Contains(AgentGuidanceCatalog.Codex.SkillsLink, codex);
        Assert.Contains(AgentGuidanceCatalog.Codex.SandboxingLink, codex);
        Assert.Contains(AgentGuidanceCatalog.Codex.SubagentsLink, codex);
        Assert.Contains(AgentGuidanceCatalog.Codex.NonInteractiveLink, codex);
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
            File.Delete(Path.Combine(
                root,
                AgentSkillDocuments.CodexReferenceRelativePath));

            var stale = AgentSkillDocuments.FindStale(root);

            Assert.Equal(
                [
                    new StaleGeneratedDocument(
                        AgentSkillDocuments.SkillRelativePath,
                        GeneratedDocumentState.Different),
                    new StaleGeneratedDocument(
                        AgentSkillDocuments.CodexReferenceRelativePath,
                        GeneratedDocumentState.Missing),
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
    public async Task Home_and_structured_help_emit_the_canonical_guidance()
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
            var runtimeGuidance =
                AgentGuidanceCatalog.ForVersion(ToolVersion.Current);
            foreach (var item in SummaryText(runtimeGuidance.Summary))
            {
                Assert.Contains(item, homeOutput.ToString());
                Assert.Contains(item, helpOutput.ToString());
            }

            AssertCompactRuntimeGuidance(homeOutput.ToString());
            AssertCompactRuntimeGuidance(helpOutput.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<string> SummaryText(
        AgentCommandGuidanceSummary guidance) =>
    [
        guidance.Invocation,
        guidance.Authority,
        .. guidance.NextSteps,
    ];

    private static void AssertPortableMetadata(string skill)
    {
        var lines = skill.Split('\n');
        Assert.Equal("---", lines[0]);
        Assert.Equal($"name: {AgentGuidanceCatalog.SkillName}", lines[1]);
        Assert.Equal(
            $"description: {AgentGuidanceCatalog.SkillDescription}",
            lines[2]);
        Assert.Equal("---", lines[3]);
        Assert.Contains("Trigger for finding .NET files", lines[2]);
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
        var referencePath = Path.Combine(
            installation,
            "references",
            "codex.md");
        Assert.True(File.Exists(skillPath));
        Assert.True(File.Exists(referencePath));
        AssertPortableMetadata(File.ReadAllText(skillPath));
        AssertSourceDiscoveryGuidance(File.ReadAllText(skillPath));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(source, "SKILL.md")),
            File.ReadAllBytes(skillPath));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(source, "references", "codex.md")),
            File.ReadAllBytes(referencePath));
    }

    private static void AssertSourceDiscoveryGuidance(string content)
    {
        foreach (var required in new[]
                 {
                     "search file '<path-fragment>'",
                     "search text '<literal>'",
                     "search text '<dotnet-regex>' --regex",
                     "search syntax --help",
                     "search file --help",
                     "search text --help",
                     "search syntax invocation --help",
                     "search syntax invocation --name SaveChangesAsync",
                     "--path <scope> --limit 20",
                     "built-in engine",
                     "use an available direct tool",
                     "retrieval_command",
                     "syntax candidates, never as compiler-verified",
                 })
        {
            Assert.Contains(required, content);
        }

        foreach (var futureSemanticCommand in new[]
                 {
                     "search symbol",
                     "show symbol",
                     "-- references",
                     "-- implementations",
                 })
        {
            Assert.DoesNotContain(futureSemanticCommand, content);
        }
    }

    private static void AssertCompactRuntimeGuidance(string content)
    {
        Assert.Contains("guidance:\n  invocation: dnx dnaxi@", content);
        Assert.Contains("\n  authority:", content);
        Assert.Contains("\n  next_steps[3]:", content);
        foreach (var omitted in new[]
                 {
                     "boundaries:",
                     "use_when:",
                     "skip_when:",
                     "activation_flow:",
                     "invocation_flow:",
                     "capability_flow:",
                     "source_discovery_flow:",
                     "safety_flow:",
                     "completion:",
                 })
        {
            Assert.DoesNotContain(omitted, content);
        }

        Assert.DoesNotContain("dotnet tool run dnaxi", content);
        Assert.DoesNotContain("source_discovery_flow", content);
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
