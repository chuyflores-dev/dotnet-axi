using System.Diagnostics;
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
    private readonly WorktreeStateInspector _inspector = new();
    private readonly RepositoryFixtureFactory _fixtures = new();

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
        var processFactory = SuccessfulProcessFactory();
        var inspector = new WorktreeStateInspector(
            "fake-git",
            processTimeout: null,
            processFactory);

        var result = await inspector.InspectAsync(workspace);

        Assert.Equal(WorktreeInspectionOutcome.Available, result.Outcome);
        Assert.Equal(3, processFactory.StartInfos.Count);
        Assert.All(
            processFactory.StartInfos,
            static startInfo => Assert.Equal(
                "1",
                startInfo.Environment["GIT_NO_LAZY_FETCH"]));
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
    public async Task Timeout_bounds_output_drain_after_process_exit()
    {
        await using var fixture = await GitFixtureAsync("git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var inheritedOutput = FakeGitProcess.WithInheritedStandardOutput();
        var processFactory = new FakeGitProcessFactory(
            FakeGitProcess.Completed(exitCode: 1),
            inheritedOutput);
        var inspector = new WorktreeStateInspector(
            "fake-git",
            TimeSpan.FromMilliseconds(100),
            processFactory);
        var stopwatch = Stopwatch.StartNew();

        var result = await inspector
            .InspectAsync(workspace)
            .WaitAsync(TimeSpan.FromSeconds(3));

        stopwatch.Stop();
        Assert.Equal(WorktreeInspectionOutcome.Failed, result.Outcome);
        Assert.Equal(
            WorktreeInspectionFailureKind.GitProcessTimedOut,
            Assert.IsType<WorktreeInspectionFailure>(result.Failure).Kind);
        Assert.True(inheritedOutput.HasExited);
        Assert.False(inheritedOutput.KillCalled);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Inspection took {stopwatch.Elapsed}.");
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
        var processFactory = SuccessfulProcessFactory(status);
        var inspector = new WorktreeStateInspector(
            "fake-git",
            processTimeout: null,
            processFactory);

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

    private static FakeGitProcessFactory SuccessfulProcessFactory(
        string? status = null) =>
        new(
            FakeGitProcess.Completed(exitCode: 1),
            FakeGitProcess.Completed(
                exitCode: 0,
                standardOutput: status ?? string.Concat(
                    "# branch.oid ",
                    "0123456789012345678901234567890123456789\0",
                    "# branch.head main\0")),
            FakeGitProcess.Completed(
                exitCode: 0,
                standardOutput: "src/File.cs\0"));

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

    private sealed class FakeGitProcessFactory : IWorktreeGitProcessFactory
    {
        private readonly Queue<IWorktreeGitProcess> _processes;

        public FakeGitProcessFactory(params IWorktreeGitProcess[] processes)
        {
            _processes = new Queue<IWorktreeGitProcess>(processes);
        }

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public IWorktreeGitProcess Create(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return _processes.Dequeue();
        }
    }

    private sealed class FakeGitProcess : IWorktreeGitProcess
    {
        private readonly int _exitCode;
        private readonly Task<string> _standardOutput;
        private readonly Task<string> _standardError;

        private FakeGitProcess(
            int exitCode,
            Task<string> standardOutput,
            Task<string> standardError)
        {
            _exitCode = exitCode;
            _standardOutput = standardOutput;
            _standardError = standardError;
        }

        public bool HasExited { get; private init; } = true;

        public int ExitCode => _exitCode;

        public bool KillCalled { get; private set; }

        public static FakeGitProcess Completed(
            int exitCode,
            string standardOutput = "",
            string standardError = "") =>
            new(
                exitCode,
                Task.FromResult(standardOutput),
                Task.FromResult(standardError));

        public static FakeGitProcess WithInheritedStandardOutput() =>
            new(
                exitCode: 0,
                new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task,
                Task.FromResult(string.Empty));

        public bool Start() => true;

        public void CloseStandardInput()
        {
        }

        public Task<string> ReadStandardOutputToEndAsync(
            CancellationToken cancellationToken) =>
            _standardOutput;

        public Task<string> ReadStandardErrorToEndAsync(
            CancellationToken cancellationToken) =>
            _standardError;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Kill(bool entireProcessTree) => KillCalled = true;

        public void Dispose()
        {
        }
    }
}
