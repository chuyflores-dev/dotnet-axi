using System.Diagnostics;
using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

[Collection(EnvironmentSensitiveWorktreeCollection.Name)]
public sealed class ChangedScopeResolverTests
{
    private readonly WorkspaceDiscoverer _discoverer = new();
    private readonly ChangedScopeResolver _resolver = new();
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Fact]
    public async Task Default_scope_returns_all_non_conflicted_worktree_paths()
    {
        await using var fixture = await CatalogGitFixtureAsync(
            "git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var head = await RevParseAsync(fixture, "HEAD");

        var result = await _resolver.ResolveAsync(workspace);

        Assert.Equal(ChangedScopeMode.Worktree, result.Mode);
        Assert.Equal(
            [
                "src/Deleted.cs",
                "src/RenamedAfter.cs",
                "src/RenamedBefore.cs",
                "src/Staged.cs",
                "src/Unstaged.cs",
                "src/Untracked.cs",
            ],
            result.ChangedPaths);
        Assert.Empty(result.ExcludedConflictedPaths);
        Assert.Null(result.ResolvedBaseCommit);
        Assert.Equal(head, result.ResolvedHeadCommit);
        Assert.Null(result.MergeBaseCommit);
        Assert.True(result.IncludesWorktreeChanges);
    }

    [Fact]
    public async Task Base_scope_uses_merge_base_and_includes_the_worktree()
    {
        await using var fixture = await GraphFixtureAsync();
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var resolvedBase = await RevParseAsync(
            fixture,
            "comparison-base");
        var resolvedHead = await RevParseAsync(fixture, "main");
        var mergeBase = await RevParseAsync(fixture, "main^");

        var result = await _resolver.ResolveAsync(
            workspace,
            new ChangedScopeRequest("comparison-base"));

        Assert.Equal(
            ChangedScopeMode.MergeBaseWithWorktree,
            result.Mode);
        Assert.Equal(
            [
                "src/AmbientStaged.cs",
                "src/AmbientUnstaged.cs",
                "src/AmbientUntracked.cs",
                "src/Committed.cs",
                "src/Deleted.cs",
                "src/RenamedAfter.cs",
                "src/RenamedBefore.cs",
            ],
            result.ChangedPaths);
        Assert.DoesNotContain("src/BaseOnly.cs", result.ChangedPaths);
        Assert.Empty(result.ExcludedConflictedPaths);
        Assert.Equal(resolvedBase, result.ResolvedBaseCommit);
        Assert.Equal(resolvedHead, result.ResolvedHeadCommit);
        Assert.Equal(mergeBase, result.MergeBaseCommit);
        Assert.True(result.IncludesWorktreeChanges);
    }

    [Fact]
    public async Task Base_and_head_use_committed_three_dot_scope_only()
    {
        await using var fixture = await GraphFixtureAsync();
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var resolvedBase = await RevParseAsync(
            fixture,
            "comparison-base");
        var resolvedHead = await RevParseAsync(fixture, "main");
        var mergeBase = await RevParseAsync(fixture, "main^");

        var result = await _resolver.ResolveAsync(
            workspace,
            new ChangedScopeRequest("comparison-base", "main"));

        Assert.Equal(ChangedScopeMode.CommittedThreeDot, result.Mode);
        Assert.Equal(
            [
                "src/Committed.cs",
                "src/Deleted.cs",
                "src/RenamedAfter.cs",
                "src/RenamedBefore.cs",
            ],
            result.ChangedPaths);
        Assert.DoesNotContain("src/BaseOnly.cs", result.ChangedPaths);
        Assert.DoesNotContain(
            "src/AmbientStaged.cs",
            result.ChangedPaths);
        Assert.DoesNotContain(
            "src/AmbientUnstaged.cs",
            result.ChangedPaths);
        Assert.DoesNotContain(
            "src/AmbientUntracked.cs",
            result.ChangedPaths);
        Assert.Empty(result.ExcludedConflictedPaths);
        Assert.Equal(resolvedBase, result.ResolvedBaseCommit);
        Assert.Equal(resolvedHead, result.ResolvedHeadCommit);
        Assert.Equal(mergeBase, result.MergeBaseCommit);
        Assert.False(result.IncludesWorktreeChanges);
    }

    [Theory]
    [InlineData(false, false, ChangedScopeMode.Worktree)]
    [InlineData(true, false, ChangedScopeMode.MergeBaseWithWorktree)]
    [InlineData(true, true, ChangedScopeMode.CommittedThreeDot)]
    public async Task Conflicted_paths_are_identified_and_excluded(
        bool hasBase,
        bool hasHead,
        ChangedScopeMode expectedMode)
    {
        await using var fixture = await CatalogGitFixtureAsync(
            "git-conflict");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var request = hasBase
            ? new ChangedScopeRequest(
                "HEAD^",
                hasHead ? "HEAD" : null)
            : null;

        var result = await _resolver.ResolveAsync(workspace, request);

        Assert.Equal(expectedMode, result.Mode);
        Assert.Empty(result.ChangedPaths);
        Assert.Equal(
            ["src/Conflict.cs"],
            result.ExcludedConflictedPaths);
        Assert.Equal(!hasHead, result.IncludesWorktreeChanges);
    }

    [Fact]
    public async Task Invalid_base_reference_is_a_structured_failure()
    {
        await using var fixture = await CatalogGitFixtureAsync(
            "git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        const string reference = "refs/heads/base-does-not-exist";

        var error = await Assert.ThrowsAsync<
            ChangedScopeResolutionException>(
            () => _resolver.ResolveAsync(
                workspace,
                new ChangedScopeRequest(reference)));

        Assert.Equal(
            ChangedScopeErrorKind.InvalidBaseReference,
            error.Kind);
        Assert.Equal("workspace.git_ref_invalid", error.Code);
        Assert.Equal(reference, error.Reference);
        Assert.Null(error.ProcessExitCode);
        Assert.Equal(
            "Provide --base with an existing commit reference.",
            error.Correction);
    }

    [Fact]
    public async Task Invalid_head_reference_is_a_structured_failure()
    {
        await using var fixture = await CatalogGitFixtureAsync(
            "git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        const string reference = "refs/heads/head-does-not-exist";

        var error = await Assert.ThrowsAsync<
            ChangedScopeResolutionException>(
            () => _resolver.ResolveAsync(
                workspace,
                new ChangedScopeRequest("HEAD", reference)));

        Assert.Equal(
            ChangedScopeErrorKind.InvalidHeadReference,
            error.Kind);
        Assert.Equal("workspace.git_ref_invalid", error.Code);
        Assert.Equal(reference, error.Reference);
        Assert.Null(error.ProcessExitCode);
        Assert.Equal(
            "Provide --head with an existing commit reference.",
            error.Correction);
    }

    [Fact]
    public async Task Head_without_base_is_a_structured_usage_failure()
    {
        await using var fixture = await CatalogGitFixtureAsync(
            "git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);

        var error = await Assert.ThrowsAsync<
            ChangedScopeResolutionException>(
            () => _resolver.ResolveAsync(
                workspace,
                new ChangedScopeRequest(headReference: "HEAD")));

        Assert.Equal(
            ChangedScopeErrorKind.HeadRequiresBase,
            error.Kind);
        Assert.Equal("usage.changed_head_requires_base", error.Code);
        Assert.Equal(
            "Provide --base together with --head, or remove --head.",
            error.Correction);
    }

    [Fact]
    public async Task Non_git_usage_returns_git_required_without_invoking_git()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var resolver = new ChangedScopeResolver(
            Path.Combine(fixture.WorkspacePath, "git-does-not-exist"));

        var error = await Assert.ThrowsAsync<
            ChangedScopeResolutionException>(
            () => resolver.ResolveAsync(workspace));

        Assert.NotEqual(WorkspaceKind.Git, workspace.WorkspaceKind);
        Assert.Equal(ChangedScopeErrorKind.GitRequired, error.Kind);
        Assert.Equal("workspace.git_required", error.Code);
        Assert.Equal(
            "Run the command from a Git worktree or omit --changed.",
            error.Correction);
        Assert.Null(error.Reference);
        Assert.Null(error.ProcessExitCode);
    }

    [Fact]
    public async Task Missing_git_is_a_typed_resolution_failure()
    {
        await using var fixture = await CatalogGitFixtureAsync(
            "git-worktree");
        var workspace = _discoverer.Discover(fixture.WorkspacePath);
        var resolver = new ChangedScopeResolver(
            Path.Combine(fixture.WorkspacePath, "git-does-not-exist"));

        var error = await Assert.ThrowsAsync<
            ChangedScopeResolutionException>(
            () => resolver.ResolveAsync(workspace));

        Assert.Equal(
            ChangedScopeErrorKind.GitExecutableNotFound,
            error.Kind);
        Assert.Equal("workspace.git_unavailable", error.Code);
        Assert.Null(error.Reference);
        Assert.Null(error.ProcessExitCode);
    }

    private async ValueTask<RepositoryFixture> GraphFixtureAsync()
    {
        var fixture = await _fixtures.CreateAsync(
            GraphManifestPath(),
            new RepositoryFixtureOptions(
                FixtureExecutionPermissions.Tooling));
        try
        {
            await fixture.PrepareGitAsync();
            await RunGitAsync(fixture, "branch", "comparison-base");
            await RunGitAsync(
                fixture,
                "mv",
                "--",
                "src/RenamedBefore.cs",
                "src/RenamedAfter.cs");
            await RunGitAsync(
                fixture,
                "rm",
                "--quiet",
                "--",
                "src/Deleted.cs");
            await RunGitAsync(
                fixture,
                "commit",
                "--quiet",
                "--message",
                "fixture main changes");

            await RunGitAsync(
                fixture,
                "switch",
                "--quiet",
                "comparison-base");
            await ApplyGraphTemplateAsync(
                fixture,
                "src/BaseOnly.cs",
                "BaseOnly.after.cs");
            await RunGitAsync(
                fixture,
                "add",
                "--",
                "src/BaseOnly.cs");
            await RunGitAsync(
                fixture,
                "commit",
                "--quiet",
                "--message",
                "fixture base changes");
            await RunGitAsync(
                fixture,
                "switch",
                "--quiet",
                "main");

            await ApplyGraphTemplateAsync(
                fixture,
                "src/AmbientStaged.cs",
                "AmbientStaged.after.cs");
            await RunGitAsync(
                fixture,
                "add",
                "--",
                "src/AmbientStaged.cs");
            await ApplyGraphTemplateAsync(
                fixture,
                "src/AmbientUnstaged.cs",
                "AmbientUnstaged.after.cs");
            await ApplyGraphTemplateAsync(
                fixture,
                "src/AmbientUntracked.cs",
                "AmbientUntracked.cs");
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async ValueTask<RepositoryFixture> CatalogGitFixtureAsync(
        string name)
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

    private static async Task ApplyGraphTemplateAsync(
        RepositoryFixture fixture,
        string destination,
        string template)
    {
        var content = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ChangedScope",
                "git-graph",
                "templates",
                template));
        await File.WriteAllBytesAsync(
            Path.Combine(
                fixture.WorkspacePath,
                destination.Replace(
                    '/',
                    Path.DirectorySeparatorChar)),
            content);
    }

    private static async Task<string> RevParseAsync(
        RepositoryFixture fixture,
        string reference) =>
        (await RunGitAsync(
            fixture,
            "rev-parse",
            "--verify",
            reference)).Trim();

    private static async Task<string> RunGitAsync(
        RepositoryFixture fixture,
        params string[] arguments)
    {
        var startInfo = fixture.CreateProcessStartInfo(
            FixtureProcessKind.Tooling,
            "git",
            arguments);
        startInfo.Environment["GIT_AUTHOR_DATE"] =
            "2000-01-02T00:00:00+00:00";
        startInfo.Environment["GIT_COMMITTER_DATE"] =
            "2000-01-02T00:00:00+00:00";
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
        return await standardOutput;
    }

    private static string CatalogManifestPath(string fixtureName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Catalog",
            fixtureName,
            "fixture.json");

    private static string GraphManifestPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "ChangedScope",
            "git-graph",
            "fixture.json");
}
