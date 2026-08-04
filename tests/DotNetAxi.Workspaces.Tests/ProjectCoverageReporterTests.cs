using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class ProjectCoverageReporterTests
{
    private const string UnrestoredProject =
        "src/Unrestored/Unrestored.csproj";
    private const string VisualBasicProject =
        "src/VisualBasic/VisualBasic.vbproj";

    private readonly WorkspaceDiscoverer _discoverer = new();
    private readonly WorkspaceEntryPointSelector _selector = new();
    private readonly MsBuildProjectGraphEvaluator _evaluator =
        new(new DotNetHostResolver());
    private readonly ProjectCoverageReporter _reporter = new();
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Fact]
    public async Task Default_and_complete_modes_partition_exact_declared_variant_coverage()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await AddAssetsExceptAsync(
            fixture.WorkspacePath,
            UnrestoredProject);
        var unrestoredObjectDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Unrestored",
            "obj");
        Assert.False(Directory.Exists(unrestoredObjectDirectory));
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Coverage.slnx"));

        var graph = _evaluator.Evaluate(discovery, selection);
        var defaultCoverage = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Default);
        var completeCoverage = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Complete);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        AssertCoverage(
            defaultCoverage.Coverage,
            considered: 6,
            analyzed: 2,
            remaining: 1,
            excluded: 2,
            failed: 1);
        Assert.Equal(
            [
                ("src/Legacy/Legacy.csproj", "net10.0", false,
                    ProjectVariantCoverageState.Unsupported),
                ("src/Multi/Multi.csproj", "net9.0", true,
                    ProjectVariantCoverageState.Supported),
                ("src/Multi/Multi.csproj", "net10.0", false,
                    ProjectVariantCoverageState.Supported),
                ("src/Supported/Supported.csproj", "net10.0", true,
                    ProjectVariantCoverageState.Supported),
                (UnrestoredProject, "net10.0", false,
                    ProjectVariantCoverageState.Unrestored),
                ("src/VisualBasic/VisualBasic.vbproj", "net10.0", false,
                    ProjectVariantCoverageState.Unsupported),
            ],
            defaultCoverage.Variants.Select(static variant =>
                (variant.Project,
                    variant.Framework,
                    variant.IsSelected,
                    variant.State)));
        Assert.All(
            defaultCoverage.Variants.Where(
                static variant => variant.Project
                    == "src/Multi/Multi.csproj"),
            static variant => Assert.True(variant.IsMultiTargeted));
        AssertIssue(
            defaultCoverage,
            "src/Multi/Multi.csproj",
            "net10.0",
            ProjectCoverageIssueReason.FrameworkNotSelected);
        AssertIssue(
            defaultCoverage,
            "src/Legacy/Legacy.csproj",
            "net10.0",
            ProjectCoverageIssueReason.UnsupportedProjectShape);
        AssertIssue(
            defaultCoverage,
            "src/VisualBasic/VisualBasic.vbproj",
            "net10.0",
            ProjectCoverageIssueReason.UnsupportedLanguage);
        var unrestoredIssue = AssertIssue(
            defaultCoverage,
            UnrestoredProject,
            "net10.0",
            ProjectCoverageIssueReason.MissingAssets);
        Assert.Contains(
            "dnaxi restore",
            unrestoredIssue.Correction,
            StringComparison.Ordinal);

        Assert.Equal(
            ProjectFrameworkCoverageMode.Complete,
            completeCoverage.FrameworkMode);
        AssertCoverage(
            completeCoverage.Coverage,
            considered: 6,
            analyzed: 3,
            remaining: 0,
            excluded: 2,
            failed: 1);
        Assert.All(
            completeCoverage.Variants.Where(
                static variant => variant.Project
                    == "src/Multi/Multi.csproj"),
            static variant => Assert.True(variant.IsSelected));
        Assert.False(Directory.Exists(unrestoredObjectDirectory));
    }

    [Fact]
    public async Task Complete_mode_covers_every_supported_framework_variant()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await AddAssetsExceptAsync(
            fixture.WorkspacePath,
            excludedProject: null);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "Multi"));

        var graph = _evaluator.Evaluate(discovery, selection);
        var defaultCoverage = _reporter.Report(graph);
        var completeCoverage = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Complete);

        Assert.Equal(ProjectGraphCompleteness.Complete, graph.Completeness);
        AssertCoverage(
            defaultCoverage.Coverage,
            considered: 2,
            analyzed: 1,
            remaining: 1,
            excluded: 0,
            failed: 0);
        var unselected = AssertIssue(
            defaultCoverage,
            "src/Multi/Multi.csproj",
            "net10.0",
            ProjectCoverageIssueReason.FrameworkNotSelected);
        Assert.Contains(
            "--complete",
            unselected.Correction,
            StringComparison.Ordinal);
        AssertCompleteCoverage(
            completeCoverage.Coverage,
            considered: 2,
            analyzed: 2);
        Assert.Equal(
            ["net9.0", "net10.0"],
            completeCoverage.Variants.Select(
                static variant => variant.Framework));
        Assert.All(
            completeCoverage.Variants,
            static variant =>
            {
                Assert.True(variant.IsSelected);
                Assert.Empty(variant.Issues);
            });
    }

    [Fact]
    public async Task Unsupported_language_precedes_missing_assets_and_retains_both_issues()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await AddAssetsExceptAsync(
            fixture.WorkspacePath,
            VisualBasicProject);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Coverage.slnx"));

        var graph = _evaluator.Evaluate(discovery, selection);
        var report = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Complete);

        AssertCoverage(
            report.Coverage,
            considered: 6,
            analyzed: 4,
            remaining: 0,
            excluded: 2,
            failed: 0);
        var visualBasic = Assert.Single(
            report.Variants,
            static variant => variant.Project == VisualBasicProject);
        Assert.Equal(
            ProjectVariantCoverageState.Unsupported,
            visualBasic.State);
        Assert.False(visualBasic.IsSelected);
        Assert.Equal(
            [
                ProjectCoverageIssueReason.UnsupportedLanguage,
                ProjectCoverageIssueReason.MissingAssets,
            ],
            visualBasic.Issues.Select(static issue => issue.Reason));
        Assert.Contains(
            visualBasic.Issues,
            static issue => issue.Reason
                            is ProjectCoverageIssueReason.MissingAssets
                            && issue.Correction.Contains(
                                "dnaxi restore",
                                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unsupported_project_shape_precedes_missing_assets_and_retains_both_issues()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        var projectDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Custom");
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Custom.proj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Language>C#</Language>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = new WorkspaceSelection(
            WorkspaceEntryPointKind.Project,
            "src/Custom/Custom.proj",
            WorkspaceSelectionSource.ExplicitProject);

        var graph = _evaluator.Evaluate(discovery, selection);
        var report = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Complete);

        AssertCoverage(
            report.Coverage,
            considered: 1,
            analyzed: 0,
            remaining: 0,
            excluded: 1,
            failed: 0);
        var custom = Assert.Single(report.Variants);
        Assert.Equal(ProjectVariantCoverageState.Unsupported, custom.State);
        Assert.False(custom.IsSelected);
        Assert.Equal(
            [
                ProjectCoverageIssueReason.UnsupportedProjectShape,
                ProjectCoverageIssueReason.MissingAssets,
            ],
            custom.Issues.Select(static issue => issue.Reason));
        Assert.False(Directory.Exists(Path.Combine(projectDirectory, "obj")));
    }

    [Fact]
    public async Task Malformed_assets_are_a_stable_broken_coverage_reason()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await AddAssetsExceptAsync(
            fixture.WorkspacePath,
            excludedProject: null);
        var assetsPath = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Supported",
            "obj",
            "project.assets.json");
        const string malformedAssets = "{ invalid";
        await File.WriteAllTextAsync(assetsPath, malformedAssets);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Coverage.slnx"));

        var graph = _evaluator.Evaluate(discovery, selection);
        var report = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Complete);

        AssertCoverage(
            report.Coverage,
            considered: 6,
            analyzed: 3,
            remaining: 0,
            excluded: 2,
            failed: 1);
        var supported = Assert.Single(
            report.Variants,
            static variant => variant.Project
                == "src/Supported/Supported.csproj");
        Assert.Equal(ProjectVariantCoverageState.Broken, supported.State);
        var issue = Assert.Single(supported.Issues);
        Assert.Equal(
            ProjectCoverageIssueReason.InvalidAssetsFile,
            issue.Reason);
        Assert.Equal("assets.invalid", issue.AuthorityCode);
        Assert.Equal(malformedAssets, await File.ReadAllTextAsync(assetsPath));
    }

    [Fact]
    public async Task Broken_project_remains_in_the_denominator_with_authority_reason()
    {
        await using var fixture = await ProjectGraphFixtureAsync(
            "evaluation-failure");
        await AddAssetsExceptAsync(
            fixture.WorkspacePath,
            excludedProject: null);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(discovery, selection);
        var report = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Complete);

        AssertCoverage(
            report.Coverage,
            considered: 2,
            analyzed: 1,
            remaining: 0,
            excluded: 0,
            failed: 1);
        var broken = Assert.Single(
            report.Variants,
            static variant => variant.Project
                == "src/Broken/Broken.csproj");
        Assert.Equal(ProjectVariantCoverageState.Broken, broken.State);
        Assert.False(broken.IsSelected);
        var issue = Assert.Single(broken.Issues);
        Assert.Equal(
            ProjectCoverageIssueReason.InvalidProjectFile,
            issue.Reason);
        Assert.Equal("MSB4025", issue.AuthorityCode);
        Assert.Contains(
            "Correct the project",
            issue.Correction,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_sdk_authority_keeps_known_projects_failed_without_inspection_writes()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Coverage.slnx"));
        var failure = new ProjectEvaluationFailure(
            ProjectEvaluationFailureReason.MsBuildUnavailable,
            "sdk.selection_failed");
        var evaluator = new MsBuildProjectGraphEvaluator(
            new StubRuntimeAuthority(failure));

        var graph = evaluator.Evaluate(discovery, selection);
        var report = _reporter.Report(
            graph,
            ProjectFrameworkCoverageMode.Complete);

        AssertCoverage(
            report.Coverage,
            considered: 5,
            analyzed: 0,
            remaining: 0,
            excluded: 0,
            failed: 5);
        Assert.All(
            report.Variants,
            variant =>
            {
                Assert.Equal(ProjectVariantCoverageState.Broken, variant.State);
                Assert.False(variant.IsSelected);
                Assert.Null(variant.IsMultiTargeted);
                var issue = Assert.Single(variant.Issues);
                Assert.Equal(
                    ProjectCoverageIssueReason.MsBuildUnavailable,
                    issue.Reason);
                Assert.Equal("sdk.selection_failed", issue.AuthorityCode);
                Assert.Contains(
                    "required .NET SDK",
                    issue.Correction,
                    StringComparison.Ordinal);
            });
        Assert.Empty(Directory.EnumerateDirectories(
            fixture.WorkspacePath,
            "obj",
            SearchOption.AllDirectories));
    }

    private async ValueTask<RepositoryFixture> ProjectGraphFixtureAsync(
        string fixtureName) =>
        await _fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ProjectGraph",
                fixtureName,
                "fixture.json"));

    private static async Task AddAssetsExceptAsync(
        string workspacePath,
        string? excludedProject)
    {
        var excludedPath = excludedProject is null
            ? null
            : Path.GetFullPath(
                excludedProject.Replace('/', Path.DirectorySeparatorChar),
                workspacePath);
        foreach (var projectPath in Directory.EnumerateFiles(
                     workspacePath,
                     "*.*proj",
                     SearchOption.AllDirectories))
        {
            if (string.Equals(
                    Path.GetFullPath(projectPath),
                    excludedPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                continue;
            }

            var assetsDirectory = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                "obj");
            Directory.CreateDirectory(assetsDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(assetsDirectory, "project.assets.json"),
                Assets("net9.0", "net10.0", "net10.0-windows"));
        }
    }

    private static string Assets(params string[] targets) =>
        $"{{\"version\":3,\"targets\":{{{string.Join(',', targets.Select(
            static target => $"\"{target}\":{{}}"))}}}}}";

    private static void AssertCoverage(
        EvidenceCoverage coverage,
        int considered,
        int analyzed,
        int remaining,
        int excluded,
        int failed)
    {
        Assert.Equal(CoverageLevel.Partial, coverage.Level);
        Assert.Equal(considered, coverage.Considered);
        Assert.Equal(analyzed, coverage.Analyzed);
        Assert.Equal(remaining, coverage.Remaining);
        Assert.Equal(excluded, coverage.Excluded);
        Assert.Equal(failed, coverage.Failed);
        Assert.Equal(
            coverage.Considered,
            coverage.Analyzed
            + coverage.Remaining
            + coverage.Excluded
            + coverage.Failed);
        Assert.NotNull(coverage.PartialReason);
    }

    private static ProjectCoverageIssue AssertIssue(
        ProjectCoverageReport report,
        string project,
        string? framework,
        ProjectCoverageIssueReason reason)
    {
        var variant = Assert.Single(
            report.Variants,
            variant => variant.Project == project
                       && variant.Framework == framework);
        var issue = Assert.Single(variant.Issues);
        Assert.Equal(reason, issue.Reason);
        Assert.False(string.IsNullOrWhiteSpace(issue.Correction));
        return issue;
    }

    private static void AssertCompleteCoverage(
        EvidenceCoverage coverage,
        int considered,
        int analyzed)
    {
        Assert.Equal(CoverageLevel.Complete, coverage.Level);
        Assert.Equal(considered, coverage.Considered);
        Assert.Equal(analyzed, coverage.Analyzed);
        Assert.Equal(0, coverage.Remaining);
        Assert.Equal(0, coverage.Excluded);
        Assert.Equal(0, coverage.Failed);
        Assert.Equal(
            coverage.Considered,
            coverage.Analyzed
            + coverage.Remaining
            + coverage.Excluded
            + coverage.Failed);
        Assert.Null(coverage.PartialReason);
    }

    private sealed class StubRuntimeAuthority(ProjectEvaluationFailure failure)
        : IMsBuildRuntimeAuthority
    {
        public MsBuildAuthorityResult ResolveAndRegister(
            string workspaceRoot,
            CancellationToken cancellationToken) =>
            new(null, failure);
    }
}
