using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class WorkspaceEntryPointSelectorTests
{
    private readonly WorkspaceDiscoverer _discoverer = new();
    private readonly WorkspaceEntryPointSelector _selector = new();
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Fact]
    public async Task Explicit_solution_wins_over_configuration_and_fallbacks()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(
                solution: "Zeta.slnx",
                configuredSelector: ConfiguredSolution("Alpha.sln")));

        Assert.Equal(WorkspaceEntryPointKind.Solution, selection.Kind);
        Assert.Equal("Zeta.slnx", selection.Path);
        Assert.Equal(
            WorkspaceSelectionSource.ExplicitSolution,
            selection.Source);
    }

    [Fact]
    public async Task Explicit_project_wins_over_configuration_and_solution_fallback()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(
                project: "Library",
                configuredSelector: ConfiguredSolution("Alpha.sln")));

        Assert.Equal(WorkspaceEntryPointKind.Project, selection.Kind);
        Assert.Equal("src/Library/Library.csproj", selection.Path);
        Assert.Equal(
            WorkspaceSelectionSource.ExplicitProject,
            selection.Source);
    }

    [Fact]
    public async Task Explicit_solution_and_project_are_a_typed_conflict()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(
                discovery,
                new WorkspaceSelectionRequest(
                    solution: "Alpha.sln",
                    project: "App")));

        Assert.Equal(
            WorkspaceSelectionErrorKind.ConflictingExplicitSelectors,
            error.Kind);
        Assert.Equal("usage.workspace_selector_conflict", error.Code);
        Assert.Equal(
            [
                "Alpha.sln",
                "Zeta.slnx",
                "src/App/App.csproj",
                "src/Library/Library.csproj",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Specify exactly one selector; for example, use `--solution` with candidate path `Alpha.sln`.",
            error.Correction);
    }

    [Fact]
    public async Task Invalid_selector_lists_supported_candidates_and_a_concrete_correction()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(
                discovery,
                new WorkspaceSelectionRequest(
                    solution: "Missing.sln")));

        Assert.Equal(WorkspaceSelectionErrorKind.InvalidSelector, error.Kind);
        Assert.Equal("usage.workspace_selector_invalid", error.Code);
        Assert.Equal(
            [
                "Alpha.sln",
                "Zeta.slnx",
                "src/App/App.csproj",
                "src/Library/Library.csproj",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Use `--solution` with candidate path `Alpha.sln`.",
            error.Correction);
    }

    [Fact]
    public async Task Configured_selector_wins_over_root_fallbacks()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(
            Path.Combine(fixture.WorkspacePath, "src", "App"));

        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(
                configuredSelector: new ConfiguredWorkspaceSelector(
                    WorkspaceEntryPointKind.Project,
                    "src/App/App.csproj")));

        Assert.Equal(WorkspaceEntryPointKind.Project, selection.Kind);
        Assert.Equal("src/App/App.csproj", selection.Path);
        Assert.Equal(
            WorkspaceSelectionSource.RepositoryConfiguration,
            selection.Source);
    }

    [Theory]
    [InlineData("Library", "src/Library/Library.csproj")]
    [InlineData("Library.csproj", "src/Library/Library.csproj")]
    [InlineData("./src/Library/Library.csproj", "src/Library/Library.csproj")]
    [InlineData("src\\Library\\Library.csproj", "src/Library/Library.csproj")]
    public async Task Project_selector_matches_names_and_paths(
        string selector,
        string expectedPath)
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: selector));

        Assert.Equal(expectedPath, selection.Path);
    }

    [Fact]
    public async Task Solution_selector_matches_a_normalized_path()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "./Zeta.slnx"));

        Assert.Equal("Zeta.slnx", selection.Path);
    }

    [Theory]
    [InlineData("../../Alpha.sln", true, "Alpha.sln")]
    [InlineData(
        "../Library/Library.csproj",
        false,
        "src/Library/Library.csproj")]
    public async Task Explicit_paths_resolve_from_the_current_directory(
        string selector,
        bool asSolution,
        string expectedPath)
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var currentDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App");
        var discovery = _discoverer.Discover(currentDirectory);
        var request = asSolution
            ? new WorkspaceSelectionRequest(solution: selector)
            : new WorkspaceSelectionRequest(project: selector);

        var selection = _selector.Select(discovery, request);

        Assert.Equal(currentDirectory, discovery.CurrentDirectory);
        Assert.Equal(expectedPath, selection.Path);
    }

    [Fact]
    public async Task Project_name_matching_is_independent_of_current_directory()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(
            Path.Combine(fixture.WorkspacePath, "src", "App"));

        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "Library"));

        Assert.Equal("src/Library/Library.csproj", selection.Path);
    }

    [Fact]
    public async Task Corrections_use_paths_relative_to_the_current_directory()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(
            Path.Combine(fixture.WorkspacePath, "src", "App"));

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(
                discovery,
                new WorkspaceSelectionRequest(solution: "Missing.sln")));

        Assert.Equal(
            [
                "Alpha.sln",
                "Zeta.slnx",
                "src/App/App.csproj",
                "src/Library/Library.csproj",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Use `--solution` with candidate path `../../Alpha.sln`.",
            error.Correction);
    }

    [Fact]
    public async Task Path_matching_uses_the_host_path_comparison()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(
                project: "SRC\\LIBRARY\\LIBRARY.CSPROJ"));

        Assert.Equal("src/Library/Library.csproj", selection.Path);
    }

    [Fact]
    public async Task Ambiguous_project_name_lists_only_deterministic_matches()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var duplicateDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Other");
        Directory.CreateDirectory(duplicateDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(duplicateDirectory, "App.csproj"),
            string.Empty);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(
                discovery,
                new WorkspaceSelectionRequest(project: "App")));

        Assert.Equal(
            WorkspaceSelectionErrorKind.AmbiguousSelector,
            error.Kind);
        Assert.Equal("usage.workspace_selector_ambiguous", error.Code);
        Assert.Equal(
            [
                "src/App/App.csproj",
                "src/Other/App.csproj",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Use `--project` with candidate path `src/App/App.csproj`.",
            error.Correction);
    }

    [Fact]
    public async Task Single_workspace_root_solution_wins_over_root_project()
    {
        await using var fixture = await CatalogFixtureAsync(
            "multi-project-sln");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "Root.csproj"),
            string.Empty);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var selection = _selector.Select(discovery);

        Assert.Equal(WorkspaceEntryPointKind.Solution, selection.Kind);
        Assert.Equal("Workspace.sln", selection.Path);
        Assert.Equal(
            WorkspaceSelectionSource.WorkspaceRootSolution,
            selection.Source);
    }

    [Fact]
    public async Task Single_workspace_root_project_is_the_last_fallback()
    {
        await using var fixture = await CatalogFixtureAsync("git-worktree");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "src", "Nested.slnx"),
            string.Empty);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var selection = _selector.Select(discovery);

        Assert.Equal(WorkspaceEntryPointKind.Project, selection.Kind);
        Assert.Equal("Workspace.csproj", selection.Path);
        Assert.Equal(
            WorkspaceSelectionSource.WorkspaceRootProject,
            selection.Source);
    }

    [Fact]
    public async Task Nested_candidates_never_become_implicit_fallbacks()
    {
        await using var fixture = await DiscoveryFixtureAsync();
        File.Delete(Path.Combine(fixture.WorkspacePath, "Alpha.sln"));
        File.Delete(Path.Combine(fixture.WorkspacePath, "Zeta.slnx"));
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(discovery));

        Assert.Equal(
            WorkspaceSelectionErrorKind.SelectionRequired,
            error.Kind);
        Assert.Equal("usage.workspace_selection_required", error.Code);
        Assert.Equal(
            [
                "src/App/App.csproj",
                "src/Library/Library.csproj",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Use `--project` with candidate path `src/App/App.csproj`.",
            error.Correction);
    }

    [Fact]
    public async Task Root_solution_ambiguity_never_selects_arbitrarily()
    {
        await using var fixture = await CatalogFixtureAsync(
            "ambiguous-solution");
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(discovery));

        Assert.Equal(
            [
                "First.slnx",
                "Second.slnx",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Use `--solution` with candidate path `First.slnx`.",
            error.Correction);
    }

    [Fact]
    public async Task Correction_preserves_a_candidate_path_with_spaces()
    {
        await using var fixture = await CatalogFixtureAsync("git-worktree");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "Credit Platform.slnx"),
            string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "Other Platform.slnx"),
            string.Empty);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(discovery));

        Assert.Equal(
            [
                "Credit Platform.slnx",
                "Other Platform.slnx",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Use `--solution` with candidate path `Credit Platform.slnx`.",
            error.Correction);
    }

    [Fact]
    public async Task Root_project_ambiguity_never_selects_arbitrarily()
    {
        await using var fixture = await CatalogFixtureAsync("git-worktree");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "Another.csproj"),
            string.Empty);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(discovery));

        Assert.Equal(
            [
                "Another.csproj",
                "Workspace.csproj",
            ],
            error.CandidatePaths);
        Assert.Equal(
            "Use `--project` with candidate path `Another.csproj`.",
            error.Correction);
    }

    [Theory]
    [InlineData("Filtered.slnf", true)]
    [InlineData("src/Legacy/Legacy.fsproj", false)]
    [InlineData("tools/FileApp.cs", false)]
    public async Task Reported_only_capabilities_are_not_selectable(
        string selector,
        bool asSolution)
    {
        await using var fixture = await DiscoveryFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var request = asSolution
            ? new WorkspaceSelectionRequest(solution: selector)
            : new WorkspaceSelectionRequest(project: selector);

        var error = Assert.Throws<WorkspaceSelectionUsageException>(
            () => _selector.Select(discovery, request));

        Assert.Equal(WorkspaceSelectionErrorKind.InvalidSelector, error.Kind);
        Assert.DoesNotContain(selector, error.CandidatePaths);
        Assert.All(
            error.CandidatePaths,
            static path => Assert.True(
                path.EndsWith(".sln", StringComparison.Ordinal)
                || path.EndsWith(".slnx", StringComparison.Ordinal)
                || path.EndsWith(".csproj", StringComparison.Ordinal)));
    }

    private ValueTask<RepositoryFixture> DiscoveryFixtureAsync() =>
        _fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "WorkspaceDiscovery",
                "fixture.json"));

    private ValueTask<RepositoryFixture> CatalogFixtureAsync(
        string fixtureName) =>
        _fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Catalog",
                fixtureName,
                "fixture.json"));

    private static ConfiguredWorkspaceSelector ConfiguredSolution(
        string path) =>
        new(WorkspaceEntryPointKind.Solution, path);
}
