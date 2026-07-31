using System.Diagnostics;

namespace DotNetAxi.Testing.Tests;

public sealed class FixtureCatalogTests
{
    private static readonly string[] RequiredCapabilities =
    [
        "analyzer:diagnostic",
        "framework:multi-targeting",
        "framework:net10.0",
        "generator:source",
        "git:conflicted",
        "git:deleted",
        "git:renamed",
        "git:staged",
        "git:unstaged",
        "git:untracked",
        "language:csharp",
        "project-reference:cycle",
        "project:broken",
        "project:external-import",
        "project:project-reference",
        "project:sdk-style",
        "restore:assets-missing",
        "solution:sln",
        "solution:slnx",
        "source:conditional-compilation",
        "source:generated",
        "source:linked-file",
        "test-runner:mtp",
        "test-runner:vstest",
        "workspace:ambiguous",
        "workspace:multi-project",
        "workspace:single-project",
        "workspace:unsupported-input",
    ];

    private static readonly IReadOnlyDictionary<string, ScenarioExpectation>
        ExpectedScenarios =
            new Dictionary<string, ScenarioExpectation>(StringComparer.Ordinal)
            {
                ["ambiguous-solution"] = new(
                    IntentionalFailure: true,
                    FixtureCoverageExpectation.Partial,
                    ["filesystem"],
                    "multiple-solution-candidates"),
                ["broken-project"] = new(
                    IntentionalFailure: true,
                    FixtureCoverageExpectation.Partial,
                    ["filesystem", "workspace"],
                    "project-xml-invalid"),
                ["external-import"] = new(
                    IntentionalFailure: false,
                    FixtureCoverageExpectation.Complete,
                    ["external-import", "project", "workspace"],
                    "import-outside-workspace"),
                ["git-conflict"] = new(
                    IntentionalFailure: true,
                    FixtureCoverageExpectation.Partial,
                    ["filesystem", "project", "workspace"],
                    "unmerged-index-entry"),
                ["git-worktree"] = new(
                    IntentionalFailure: false,
                    FixtureCoverageExpectation.Complete,
                    ["changed-files", "project", "workspace"],
                    "mixed-worktree-state"),
                ["missing-assets"] = new(
                    IntentionalFailure: true,
                    FixtureCoverageExpectation.Partial,
                    ["filesystem", "project-spec", "workspace"],
                    "restore-assets-absent"),
                ["unsupported-input"] = new(
                    IntentionalFailure: true,
                    FixtureCoverageExpectation.Partial,
                    ["filesystem"],
                    "solution-filter-unsupported"),
            };

    public static TheoryData<string> CatalogManifests()
    {
        var manifests = new TheoryData<string>();
        foreach (var path in Directory
                     .EnumerateFiles(
                         CatalogRoot(),
                         "fixture.json",
                         SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            manifests.Add(path);
        }

        return manifests;
    }

    [Fact]
    public async Task Catalog_declares_every_required_capability()
    {
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var scenarioStates = new HashSet<string>(StringComparer.Ordinal);
        var factory = new RepositoryFixtureFactory();

        foreach (var manifestPath in CatalogManifestPaths())
        {
            await using var fixture = await factory.CreateAsync(manifestPath);

            Assert.NotEmpty(fixture.Capabilities);
            Assert.NotNull(fixture.BuildVerification);
            Assert.Equal("10.0.302", fixture.Identity.SelectedSdk.Version);
            capabilities.UnionWith(fixture.Capabilities);
            if (fixture.Scenario is not null)
            {
                var expected = Assert.Contains(
                    fixture.Scenario.State,
                    ExpectedScenarios);
                Assert.Equal(
                    expected.IntentionalFailure,
                    fixture.Scenario.IntentionalFailure);
                Assert.Equal(
                    expected.ExpectedCoverage,
                    fixture.Scenario.ExpectedCoverage);
                Assert.Equal(
                    expected.RemainingCoverage,
                    fixture.Scenario.RemainingCoverage);
                Assert.Equal(expected.Reason, fixture.Scenario.Reason);

                scenarioStates.Add(fixture.Scenario.State);
            }
        }

        Assert.Empty(
            RequiredCapabilities.Except(
                capabilities,
                StringComparer.Ordinal));
        Assert.Empty(
            ExpectedScenarios.Keys.Except(
                scenarioStates,
                StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CatalogManifests))]
    public async Task Edge_fixture_state_matches_manifest(
        string manifestPath)
    {
        var factory = new RepositoryFixtureFactory();
        await using var fixture = await factory.CreateAsync(
            manifestPath,
            new RepositoryFixtureOptions(
                FixtureExecutionPermissions.Tooling));
        if (fixture.Scenario is null)
        {
            return;
        }

        switch (fixture.Scenario.State)
        {
            case "git-worktree":
                await AssertGitWorktreeAsync(fixture);
                break;
            case "git-conflict":
                await AssertGitConflictAsync(fixture);
                break;
            case "ambiguous-solution":
                Assert.Equal(
                    2,
                    Directory.EnumerateFiles(
                            fixture.WorkspacePath,
                            "*.slnx",
                            SearchOption.TopDirectoryOnly)
                        .Count());
                break;
            case "broken-project":
                Assert.Equal(
                    FixtureBuildOutcome.Failure,
                    fixture.BuildVerification?.ExpectedOutcome);
                break;
            case "missing-assets":
                Assert.Empty(
                    Directory.EnumerateFiles(
                        fixture.WorkspacePath,
                        "project.assets.json",
                        SearchOption.AllDirectories));
                break;
            case "external-import":
                Assert.NotEmpty(fixture.ExternalContentFiles);
                Assert.NotNull(fixture.ExternalContentHash);
                Assert.All(
                    fixture.ExternalContentFiles,
                    relativePath => Assert.True(
                        File.Exists(Path.Combine(
                            fixture.ExternalPath,
                            relativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar)))));
                Assert.StartsWith(
                    "..",
                    Path.GetRelativePath(
                        fixture.WorkspacePath,
                        fixture.ExternalPath),
                    StringComparison.Ordinal);
                break;
            case "unsupported-input":
                Assert.Single(
                    Directory.EnumerateFiles(
                        fixture.WorkspacePath,
                        "*.slnf",
                        SearchOption.TopDirectoryOnly));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown catalog scenario '{fixture.Scenario.State}'.");
        }
    }

    [Fact]
    public async Task Git_preparation_requires_permission_and_runs_once()
    {
        var manifestPath = CatalogManifestPaths().Single(
            path => string.Equals(
                Path.GetFileName(
                    Path.GetDirectoryName(path)),
                "git-worktree",
                StringComparison.Ordinal));
        var factory = new RepositoryFixtureFactory();

        await using (var passive = await factory.CreateAsync(manifestPath))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => passive.PrepareGitAsync().AsTask());
            Assert.False(
                Directory.Exists(
                    Path.Combine(passive.WorkspacePath, ".git")));
        }

        await using var tooling = await factory.CreateAsync(
            manifestPath,
            new RepositoryFixtureOptions(
                FixtureExecutionPermissions.Tooling));
        await tooling.PrepareGitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tooling.PrepareGitAsync().AsTask());
    }

    [Theory]
    [MemberData(nameof(CatalogManifests))]
    public async Task Fixture_build_matches_manifest(string manifestPath)
    {
        var options = new RepositoryFixtureOptions(
            FixtureExecutionPermissions.Restore
            | FixtureExecutionPermissions.RepositoryCode);
        var factory = new RepositoryFixtureFactory();
        await using var fixture = await factory.CreateAsync(
            manifestPath,
            options);
        var verification = fixture.BuildVerification
            ?? throw new InvalidOperationException(
                "Catalog fixture does not declare build verification.");
        var startInfo = fixture.CreateProcessStartInfo(
            FixtureProcessKind.Restore
            | FixtureProcessKind.RepositoryCode,
            fixture.DotNetHostPath,
            "build",
            verification.Target,
            "--configuration",
            "Release",
            "--verbosity",
            "minimal",
            "--nologo",
            "--disable-build-servers",
            "--artifacts-path",
            Path.Combine(fixture.ArtifactsPath, "build"));
        using var process = new Process
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start(), "The fixture build process did not start.");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new TimeoutException(
                $"Fixture '{fixture.Identity.Name}' build exceeded three minutes.");
        }

        var output = string.Join(
            Environment.NewLine,
            await standardOutput,
            await standardError);
        var actualOutcome = process.ExitCode == 0
            ? FixtureBuildOutcome.Success
            : FixtureBuildOutcome.Failure;

        Assert.True(
            actualOutcome == verification.ExpectedOutcome,
            $"""
             Fixture '{fixture.Identity.Name}' expected build outcome
             '{verification.ExpectedOutcome}' but exited {process.ExitCode}.

             {output}
             """);
        foreach (var requiredOutput in verification.RequiredOutput)
        {
            Assert.Contains(
                requiredOutput,
                output,
                StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string> CatalogManifestPaths() =>
        Directory
            .EnumerateFiles(
                CatalogRoot(),
                "fixture.json",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static async Task AssertGitWorktreeAsync(
        RepositoryFixture fixture)
    {
        Assert.True(fixture.RequiresGitPreparation);
        await fixture.PrepareGitAsync();

        var status = await RunGitAsync(
            fixture,
            "status",
            "--porcelain=v1",
            "--untracked-files=all");
        var lines = status
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var plan = fixture.GitPlan
            ?? throw new InvalidOperationException(
                "Git fixture does not retain its preparation plan.");
        foreach (var change in plan.Changes)
        {
            var expected = change.Kind switch
            {
                FixtureGitChangeKind.Staged =>
                    $"M  {change.Path}",
                FixtureGitChangeKind.Unstaged =>
                    $" M {change.Path}",
                FixtureGitChangeKind.Untracked =>
                    $"?? {change.Path}",
                FixtureGitChangeKind.Renamed =>
                    $"R  {change.Path} -> {change.NewPath}",
                FixtureGitChangeKind.Deleted =>
                    $" D {change.Path}",
                _ => throw new ArgumentOutOfRangeException(),
            };
            Assert.Contains(expected, lines);
        }

        Assert.Equal(plan.Changes.Count, lines.Count);
    }

    private static async Task AssertGitConflictAsync(
        RepositoryFixture fixture)
    {
        Assert.True(fixture.RequiresGitPreparation);
        await fixture.PrepareGitAsync();

        var conflict = fixture.GitPlan?.Conflict
            ?? throw new InvalidOperationException(
                "Git conflict fixture does not retain its preparation plan.");
        var status = await RunGitAsync(
            fixture,
            "status",
            "--porcelain=v1");
        Assert.Equal(
            $"UU {conflict.Path}",
            status.TrimEnd('\r', '\n'));
        var unmerged = await RunGitAsync(
            fixture,
            "ls-files",
            "--unmerged",
            "--",
            conflict.Path);
        Assert.Equal(
            3,
            unmerged.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Length);
    }

    private static async Task<string> RunGitAsync(
        RepositoryFixture fixture,
        params string[] arguments)
    {
        var startInfo = fixture.CreateProcessStartInfo(
            FixtureProcessKind.Tooling,
            "git",
            arguments);
        using var process = new Process
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start(), "The fixture Git process did not start.");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new TimeoutException(
                "Fixture Git inspection exceeded 30 seconds.");
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"""
             Fixture Git inspection exited {process.ExitCode}.

             {output}
             {error}
             """);
        return output;
    }

    private static string CatalogRoot() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Catalog");

    private sealed record ScenarioExpectation(
        bool IntentionalFailure,
        FixtureCoverageExpectation ExpectedCoverage,
        IReadOnlyList<string> RemainingCoverage,
        string Reason);
}
