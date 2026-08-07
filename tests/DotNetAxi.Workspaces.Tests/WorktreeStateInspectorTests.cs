using System.Diagnostics;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

[CollectionDefinition(
    EnvironmentSensitiveWorktreeCollection.Name,
    DisableParallelization = true)]
public sealed class EnvironmentSensitiveWorktreeCollection
{
    public const string Name = "Environment-sensitive worktree tests";
}

[Collection(EnvironmentSensitiveWorktreeCollection.Name)]
public sealed class WorktreeStateInspectorTests
{
    private readonly WorkspaceDiscoverer _discoverer = new();
    private readonly WorktreeStateInspector _inspector =
        WorktreeStateInspector.CreatePassive(new ProcessRunner());
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Fact]
    public void Passive_git_resolution_ignores_relative_entries_and_rejects_workspace_shadowing()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-git-trust-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "workspace");
        var workspaceTools = Path.Combine(workspace, "tools");
        var externalTools = Path.Combine(root, "external-tools");
        var executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var workspaceGit = Path.Combine(workspaceTools, executableName);
        var externalGit = Path.Combine(externalTools, executableName);
        Directory.CreateDirectory(workspaceTools);
        Directory.CreateDirectory(externalTools);
        File.WriteAllText(workspaceGit, "workspace-controlled");
        File.WriteAllText(externalGit, "external");
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode executableMode = UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute;
            File.SetUnixFileMode(workspaceGit, executableMode);
            File.SetUnixFileMode(externalGit, executableMode);
        }

        var pathValue = string.Join(
            Path.PathSeparator,
            "relative-tools",
            workspaceTools,
            externalTools);

        try
        {
            var shadowed =
                SafePassiveGitBoundary.ResolveExecutable(
                    "git",
                    workspace,
                    pathValue);
            var trusted =
                SafePassiveGitBoundary.ResolveExecutable(
                    "git",
                    workspace,
                    externalTools);

            Assert.Equal(
                PassiveGitExecutableTrust.WorkspaceControlled,
                shadowed.Trust);
            Assert.Null(shadowed.Path);
            Assert.Equal(PassiveGitExecutableTrust.Trusted, trusted.Trust);
            Assert.Equal(externalGit, trusted.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Passive_git_resolution_rejects_an_external_symlink_to_workspace_git()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-git-link-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "workspace");
        var tools = Path.Combine(root, "tools");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(tools);
        var executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var workspaceGit = Path.Combine(workspace, executableName);
        var linkedGit = Path.Combine(tools, executableName);
        File.WriteAllText(workspaceGit, "workspace-controlled");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                workspaceGit,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute);
        }

        try
        {
            try
            {
                File.CreateSymbolicLink(linkedGit, workspaceGit);
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException
                    or PlatformNotSupportedException)
            {
                return;
            }

            var resolution =
                SafePassiveGitBoundary.ResolveExecutable(
                    "git",
                    workspace,
                    tools);

            Assert.Equal(
                PassiveGitExecutableTrust.WorkspaceControlled,
                resolution.Trust);
            Assert.Null(resolution.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Passive_git_boundary_rejects_unapproved_commands_without_delegating()
    {
        var runner = new StubProcessRunner();
        var inspector = new WorktreeStateInspector(
            "git",
            processTimeout: null,
            runner,
            resolveExecutable: true,
            enforcePassiveBoundary: true);

        var result = await inspector.RunGitAsync(
            Path.GetFullPath(Path.GetTempPath()),
            ["fetch", "origin"],
            CancellationToken.None);

        Assert.Equal(
            WorktreeInspectionFailureKind.ProcessPolicyDenied,
            result.Failure?.Kind);
        Assert.Empty(runner.Requests);
        Assert.False(SafePassiveGitBoundary.IsAllowedArguments(
            ["commit", "--all"]));
        Assert.False(SafePassiveGitBoundary.IsAllowedArguments(
            ["-c", "alias.status=!payload", "status"]));
    }

    [Fact]
    public async Task Mixed_worktree_state_is_typed_and_deterministically_ordered()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        await RunGitAsync(fixture, "branch", "fixture-upstream");
        await RunGitAsync(
            fixture,
            "branch",
            "--set-upstream-to=fixture-upstream",
            "main");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);

        var result = await _inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Available, result.Outcome);
        Assert.Null(result.Failure);
        var state = Assert.IsType<GitWorktreeState>(result.State);
        Assert.Equal(
            new GitHeadState(
                GitHeadKind.Branch,
                "main",
                state.Head.CommitId),
            state.Head);
        Assert.NotNull(state.Head.CommitId);
        Assert.Matches("^[0-9a-f]{40}$", state.Head.CommitId);
        Assert.Equal(
            [
                "Workspace.csproj",
                "global.json",
                "src/Deleted.cs",
                "src/RenamedAfter.cs",
                "src/Staged.cs",
                "src/Unstaged.cs",
            ],
            state.TrackedPaths);
        Assert.Equal(
            [
                new GitWorktreeEntry(
                    "src/Deleted.cs",
                    OriginalPath: null,
                    GitPathTracking.Tracked,
                    GitPathStatus.None,
                    GitPathStatus.Deleted,
                    GitConflictKind.None),
                new GitWorktreeEntry(
                    "src/RenamedAfter.cs",
                    "src/RenamedBefore.cs",
                    GitPathTracking.Tracked,
                    GitPathStatus.Renamed,
                    GitPathStatus.None,
                    GitConflictKind.None),
                new GitWorktreeEntry(
                    "src/Staged.cs",
                    OriginalPath: null,
                    GitPathTracking.Tracked,
                    GitPathStatus.Modified,
                    GitPathStatus.None,
                    GitConflictKind.None),
                new GitWorktreeEntry(
                    "src/Unstaged.cs",
                    OriginalPath: null,
                    GitPathTracking.Tracked,
                    GitPathStatus.None,
                    GitPathStatus.Modified,
                    GitConflictKind.None),
                new GitWorktreeEntry(
                    "src/Untracked.cs",
                    OriginalPath: null,
                    GitPathTracking.Untracked,
                    GitPathStatus.None,
                    GitPathStatus.None,
                    GitConflictKind.None),
            ],
            state.Entries);
        Assert.Equal(
            ["src/RenamedAfter.cs", "src/Staged.cs"],
            Paths(state.StagedEntries));
        Assert.Equal(
            ["src/Deleted.cs", "src/Unstaged.cs"],
            Paths(state.UnstagedEntries));
        Assert.Equal(
            ["src/Untracked.cs"],
            Paths(state.UntrackedEntries));
        var rename = Assert.Single(state.RenamedEntries);
        Assert.Equal("src/RenamedBefore.cs", rename.OriginalPath);
        Assert.Equal("src/RenamedAfter.cs", rename.Path);
        Assert.Equal(
            ["src/Deleted.cs"],
            Paths(state.DeletedEntries));
        Assert.Empty(state.ConflictedEntries);

        var repeated = await _inspector.InspectAsync(workspace);

        var repeatedState = Assert.IsType<GitWorktreeState>(repeated.State);
        Assert.Equal(state.Head, repeatedState.Head);
        Assert.Equal(state.TrackedPaths, repeatedState.TrackedPaths);
        Assert.Equal(state.Entries, repeatedState.Entries);
    }

    [Fact]
    public async Task Unresolved_conflict_is_preserved_as_an_explicit_entry()
    {
        await using var fixture = await GitFixtureAsync("git-conflict");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);

        var result = await _inspector.InspectAsync(workspace);

        var state = Assert.IsType<GitWorktreeState>(result.State);
        Assert.Equal(WorktreeInspectionOutcome.Available, result.Outcome);
        var conflict = Assert.Single(state.ConflictedEntries);
        Assert.Equal("src/Conflict.cs", conflict.Path);
        Assert.Equal(GitPathTracking.Tracked, conflict.Tracking);
        Assert.Equal(GitConflictKind.BothModified, conflict.Conflict);
        Assert.True(conflict.IsConflicted);
        Assert.False(conflict.IsStaged);
        Assert.False(conflict.IsUnstaged);
        Assert.Empty(state.StagedEntries);
        Assert.Empty(state.UnstagedEntries);
        Assert.Contains("src/Conflict.cs", state.TrackedPaths);
    }

    [Fact]
    public async Task Detached_head_is_distinct_from_a_branch()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        await RunGitAsync(fixture, "checkout", "--quiet", "--detach");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);

        var result = await _inspector.InspectAsync(workspace);

        var state = Assert.IsType<GitWorktreeState>(result.State);
        Assert.Equal(GitHeadKind.Detached, state.Head.Kind);
        Assert.Null(state.Head.BranchName);
        Assert.NotNull(state.Head.CommitId);
        Assert.Matches("^[0-9a-f]{40}$", state.Head.CommitId);
    }

    [Fact]
    public async Task Non_git_workspace_is_valid_without_invoking_git()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var inspector = new WorktreeStateInspector(
            new ProcessRunner(),
            Path.Combine(fixture.WorkspacePath, "git-does-not-exist"));

        var result = await inspector.InspectAsync(workspace);

        Assert.NotEqual(WorkspaceKind.Git, workspace.WorkspaceKind);
        Assert.Equal(WorktreeInspectionOutcome.NotGit, result.Outcome);
        Assert.Null(result.State);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task Missing_git_is_a_typed_unavailable_outcome()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var inspector = new WorktreeStateInspector(
            new ProcessRunner(),
            Path.Combine(fixture.WorkspacePath, "git-does-not-exist"));

        var result = await inspector.InspectAsync(workspace);

        Assert.Equal(
            WorktreeInspectionOutcome.GitUnavailable,
            result.Outcome);
        Assert.Null(result.State);
        Assert.Equal(
            WorktreeInspectionFailureKind.GitExecutableNotFound,
            Assert.IsType<WorktreeInspectionFailure>(result.Failure).Kind);
    }

    [Fact]
    public async Task Git_failure_is_a_typed_outcome_without_process_output()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".git", "HEAD"),
            "invalid-head\n");

        var result = await _inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Failed, result.Outcome);
        Assert.Null(result.State);
        var failure = Assert.IsType<WorktreeInspectionFailure>(
            result.Failure);
        Assert.Equal(
            WorktreeInspectionFailureKind.GitProcessFailed,
            failure.Kind);
        Assert.NotNull(failure.ExitCode);
        Assert.NotEqual(0, failure.ExitCode);
    }

    [Fact]
    public async Task Hostile_git_environment_cannot_redirect_inspection()
    {
        await using var target = await GitFixtureAsync("git-worktree");
        await using var hostile = await GitFixtureAsync("git-worktree");
        await RunGitAsync(hostile, "checkout", "--quiet", "--detach");
        var workspace = _discoverer.Discover(target.WorkspacePath);
        var hostileGitDirectory = Path.Combine(
            hostile.WorkspacePath,
            ".git");
        var hostileVariables = new Dictionary<string, string?>
        {
            ["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = hostileGitDirectory,
            ["GIT_CEILING_DIRECTORIES"] = target.WorkspacePath,
            ["GIT_COMMON_DIR"] = hostileGitDirectory,
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_GLOBAL"] = hostileGitDirectory,
            ["GIT_CONFIG_KEY_0"] = "core.worktree",
            ["GIT_CONFIG_PARAMETERS"] = "hostile-config-injection",
            ["GIT_CONFIG_SYSTEM"] = hostileGitDirectory,
            ["GIT_CONFIG_VALUE_0"] = hostile.WorkspacePath,
            ["GIT_DIR"] = hostileGitDirectory,
            ["GIT_INDEX_FILE"] = Path.Combine(
                hostileGitDirectory,
                "index"),
            ["GIT_OBJECT_DIRECTORY"] = Path.Combine(
                hostileGitDirectory,
                "objects"),
            ["GIT_WORK_TREE"] = hostile.WorkspacePath,
        };
        var previousValues = hostileVariables.Keys.ToDictionary(
            static name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var variable in hostileVariables)
            {
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }

            var result = await _inspector.InspectAsync(workspace);

            Assert.Equal(WorktreeInspectionOutcome.Available, result.Outcome);
            Assert.Null(result.Failure);
            var state = Assert.IsType<GitWorktreeState>(result.State);
            Assert.Equal(GitHeadKind.Branch, state.Head.Kind);
            Assert.Equal("main", state.Head.BranchName);
            Assert.Contains("src/Staged.cs", state.TrackedPaths);
        }
        finally
        {
            foreach (var variable in previousValues)
            {
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }
        }
    }

    [Fact]
    public async Task Every_git_process_disables_lazy_fetching()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var processRunner = SuccessfulProcessRunner();
        var inspector = new WorktreeStateInspector(
            "fake-git",
            processTimeout: null,
            processRunner);

        var result = await inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Available, result.Outcome);
        Assert.Equal(3, processRunner.Requests.Count);
        Assert.All(
            processRunner.Requests,
            static request => Assert.Equal(
                "1",
                request.Environment["GIT_NO_LAZY_FETCH"]));
        Assert.All(
            processRunner.Requests,
            static request => Assert.Equal(
                ProcessEnvironmentPolicy.Isolated,
                request.EnvironmentPolicy));
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("process")]
    public async Task Repository_filter_commands_are_not_executed(
        string filterKind)
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var markerPath = Path.Combine(
            fixture.WorkspacePath,
            $"malicious-{filterKind}.marker");
        var shellMarkerPath = markerPath.Replace('\\', '/');
        await RunGitAsync(
            fixture,
            "config",
            $"filter.dnaxi-malicious.{filterKind}",
            $"git config --file \"{shellMarkerPath}\" marker.executed true");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".gitattributes"),
            "*.cs filter=dnaxi-malicious\n");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);

        var result = await _inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Failed, result.Outcome);
        Assert.Null(result.State);
        Assert.Equal(
            WorktreeInspectionFailureKind.GitFilterCommandsConfigured,
            Assert.IsType<WorktreeInspectionFailure>(result.Failure).Kind);
        Assert.False(File.Exists(markerPath));
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("process")]
    public async Task Nested_repository_filters_are_not_executed(
        string filterKind)
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var nestedPath = Path.Combine(
            fixture.WorkspacePath,
            "nested-repository");
        Directory.CreateDirectory(nestedPath);
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "init",
            "--quiet",
            "--initial-branch=main");
        await File.WriteAllTextAsync(
            Path.Combine(nestedPath, ".gitattributes"),
            "*.cs filter=dnaxi-malicious\n");
        var attributedPath = Path.Combine(nestedPath, "Attributed.cs");
        const string initialContent = "internal class Initial;\n";
        const string nextContent = "internal class Next;\n";
        await File.WriteAllTextAsync(attributedPath, initialContent);
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "add",
            "--all");
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "commit",
            "--quiet",
            "--message",
            "nested baseline");
        await File.WriteAllTextAsync(attributedPath, nextContent);
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "add",
            "--",
            "Attributed.cs");
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "commit",
            "--quiet",
            "--message",
            "nested next commit");
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "checkout",
            "--quiet",
            "HEAD^");
        await RunGitAsync(
            fixture,
            "add",
            "--",
            "nested-repository");
        await RunGitAsync(
            fixture,
            "commit",
            "--quiet",
            "--message",
            "record nested gitlink",
            "--",
            "nested-repository");
        var markerPath = Path.Combine(
            fixture.WorkspacePath,
            $"nested-malicious-{filterKind}.marker");
        var shellMarkerPath = markerPath.Replace('\\', '/');
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "config",
            $"filter.dnaxi-malicious.{filterKind}",
            $"git config --file \"{shellMarkerPath}\" marker.executed true");
        await File.WriteAllTextAsync(
            attributedPath,
            "internal class Modified;\n");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);

        var result = await _inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Available, result.Outcome);
        Assert.Null(result.Failure);
        var state = Assert.IsType<GitWorktreeState>(result.State);
        Assert.Contains("nested-repository", state.TrackedPaths);
        Assert.DoesNotContain(
            state.Entries,
            static entry => entry.Path == "nested-repository");
        Assert.False(File.Exists(markerPath));

        await File.WriteAllTextAsync(attributedPath, initialContent);
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "config",
            "--unset-all",
            $"filter.dnaxi-malicious.{filterKind}");
        await RunGitAsync(
            fixture,
            "-C",
            nestedPath,
            "checkout",
            "--quiet",
            "main");

        var pointerResult = await _inspector.InspectAsync(workspace);

        Assert.Equal(
            WorktreeInspectionOutcome.Available,
            pointerResult.Outcome);
        Assert.Null(pointerResult.Failure);
        var pointerState = Assert.IsType<GitWorktreeState>(
            pointerResult.State);
        Assert.Equal(
            new GitWorktreeEntry(
                "nested-repository",
                OriginalPath: null,
                GitPathTracking.Tracked,
                GitPathStatus.None,
                GitPathStatus.Modified,
                GitConflictKind.None),
            Assert.Single(
                pointerState.Entries,
                static entry => entry.Path == "nested-repository"));
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public async Task Timeout_is_typed_and_uses_a_bounded_process_request()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var processRunner = new StubProcessRunner(
            Completed(exitCode: 1),
            TimedOut());
        var inspector = new WorktreeStateInspector(
            "fake-git",
            TimeSpan.FromMilliseconds(100),
            processRunner);

        var result = await inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Failed, result.Outcome);
        Assert.Equal(
            WorktreeInspectionFailureKind.GitProcessTimedOut,
            Assert.IsType<WorktreeInspectionFailure>(result.Failure).Kind);
        var request = Assert.Single(processRunner.Requests.Skip(1));
        Assert.Equal(TimeSpan.FromMilliseconds(100), request.Timeout);
        Assert.Equal(
            16 * 1024 * 1024,
            request.OutputLimits.StandardOutputCharacters);
        Assert.Equal(
            16 * 1024 * 1024,
            request.OutputLimits.StandardErrorCharacters);
    }

    [Fact]
    public async Task Unknown_porcelain_v2_headers_are_ignored()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var status = string.Concat(
            "# branch.oid 0123456789012345678901234567890123456789\0",
            "# branch.head main\0",
            "# future.extension supported\0");
        var processRunner = SuccessfulProcessRunner(status);
        var inspector = new WorktreeStateInspector(
            "fake-git",
            processTimeout: null,
            processRunner);

        var result = await inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Available, result.Outcome);
        Assert.Null(result.Failure);
        var state = Assert.IsType<GitWorktreeState>(result.State);
        Assert.Equal(GitHeadKind.Branch, state.Head.Kind);
        Assert.Equal("main", state.Head.BranchName);
        Assert.Equal(["src/File.cs"], state.TrackedPaths);
    }

    private async ValueTask<RepositoryFixture> GitFixtureAsync(string name)
    {
        var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath(name),
            new RepositoryFixtureOptions(
                FixtureExecutionPermissions.Tooling));
        try
        {
            await fixture.PrepareGitAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static string[] Paths(
        IEnumerable<GitWorktreeEntry> entries) =>
        entries.Select(static entry => entry.Path).ToArray();

    private static string CatalogManifestPath(string fixtureName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Catalog",
            fixtureName,
            "fixture.json");

    private static StubProcessRunner SuccessfulProcessRunner(
        string? status = null) =>
        new(
            Completed(exitCode: 1),
            Completed(
                exitCode: 0,
                standardOutput: status ?? string.Concat(
                    "# branch.oid ",
                    "0123456789012345678901234567890123456789\0",
                    "# branch.head main\0")),
            Completed(
                exitCode: 0,
                standardOutput: "src/File.cs\0"));

    private static ProcessRunResult Completed(
        int exitCode,
        string standardOutput = "") =>
        new(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.Completed,
            ProcessStartFailure.None,
            new ProcessExitEvidence(exitCode, signal: null),
            new ProcessCapturedOutput(
                standardOutput,
                limitExceeded: false),
            new ProcessCapturedOutput(
                string.Empty,
                limitExceeded: false),
            TimeSpan.Zero);

    private static ProcessRunResult TimedOut() =>
        new(
            ProcessLifecycle.Terminated,
            ProcessRunOutcome.TimedOut,
            ProcessStartFailure.None,
            new ProcessExitEvidence(exitCode: 143, signal: null),
            new ProcessCapturedOutput(
                string.Empty,
                limitExceeded: false),
            new ProcessCapturedOutput(
                string.Empty,
                limitExceeded: false),
            TimeSpan.FromMilliseconds(100));

    private static async Task RunGitAsync(
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
        Assert.True(process.Start());
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(
            process.ExitCode == 0,
            $"Git failed.\n{await standardOutput}\n{await standardError}");
    }

    private sealed class StubProcessRunner(
        params ProcessRunResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _results = new(results);

        public List<ProcessRunRequest> Requests { get; } = [];

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(_results.Dequeue());
        }
    }
}
