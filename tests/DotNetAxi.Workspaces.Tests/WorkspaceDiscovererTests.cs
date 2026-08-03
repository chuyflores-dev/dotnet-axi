using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class WorkspaceDiscovererTests
{
    private readonly WorkspaceDiscoverer _discoverer = new();
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Fact]
    public async Task Git_root_wins_from_a_nested_directory()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("git-worktree"),
            new RepositoryFixtureOptions(
                FixtureExecutionPermissions.Tooling));
        await fixture.PrepareGitAsync();
        var nestedDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "nested");
        Directory.CreateDirectory(nestedDirectory);

        var result = _discoverer.Discover(nestedDirectory);

        Assert.Equal(WorkspaceKind.Git, result.WorkspaceKind);
        Assert.Equal(fixture.WorkspacePath, result.RootPath);
        Assert.Equal(["Workspace.csproj"], ProjectPaths(result));
        Assert.Equal(4, result.CSharpFileCount);
    }

    [Fact]
    public async Task Configured_root_wins_over_nested_solution_and_project()
    {
        await using var fixture = await _fixtures.CreateAsync(
            DiscoveryManifestPath());
        var nestedDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App",
            "nested");
        Directory.CreateDirectory(nestedDirectory);

        var result = _discoverer.Discover(nestedDirectory);

        Assert.Equal(WorkspaceKind.Configured, result.WorkspaceKind);
        Assert.Equal(fixture.WorkspacePath, result.RootPath);
    }

    [Fact]
    public async Task Nested_directory_uses_solution_before_a_nearer_project()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("multi-project-sln"));
        var nestedDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App",
            "nested");
        Directory.CreateDirectory(nestedDirectory);

        var result = _discoverer.Discover(nestedDirectory);

        Assert.Equal(WorkspaceKind.Solution, result.WorkspaceKind);
        Assert.Equal(fixture.WorkspacePath, result.RootPath);
        Assert.Equal(["Workspace.sln"], SolutionPaths(result));
    }

    [Fact]
    public async Task Single_project_directory_is_the_workspace_root()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var projectDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Single");

        var result = _discoverer.Discover(projectDirectory);

        Assert.Equal(WorkspaceKind.Project, result.WorkspaceKind);
        Assert.Equal(projectDirectory, result.RootPath);
        Assert.Equal(["Single.csproj"], ProjectPaths(result));
        Assert.Equal(1, result.CSharpFileCount);
        Assert.Empty(result.Solutions);
    }

    [Fact]
    public async Task Marker_free_directory_remains_the_current_directory()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var markerFreeDirectory = Path.Combine(
            fixture.WorkspacePath,
            "unrelated",
            "nested");
        Directory.CreateDirectory(markerFreeDirectory);

        var result = _discoverer.Discover(markerFreeDirectory);

        Assert.Equal(WorkspaceKind.Directory, result.WorkspaceKind);
        Assert.Equal(markerFreeDirectory, result.RootPath);
        Assert.Empty(result.Solutions);
        Assert.Empty(result.Projects);
        Assert.Equal(0, result.CSharpFileCount);
        Assert.Empty(result.RootMarkers);
        Assert.Empty(result.Capabilities);
    }

    [Fact]
    public async Task Malformed_git_marker_does_not_create_a_git_workspace()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var markerFreeDirectory = Path.Combine(
            fixture.WorkspacePath,
            "unrelated");
        Directory.CreateDirectory(
            Path.Combine(markerFreeDirectory, ".git"));

        var result = _discoverer.Discover(markerFreeDirectory);

        Assert.Equal(WorkspaceKind.Directory, result.WorkspaceKind);
        Assert.Equal(markerFreeDirectory, result.RootPath);
    }

    [Fact]
    public async Task Catalog_is_deterministic_and_keeps_reported_only_inputs_out_of_candidates()
    {
        await using var fixture = await _fixtures.CreateAsync(
            DiscoveryManifestPath());

        var first = _discoverer.Discover(fixture.WorkspacePath);
        var second = _discoverer.Discover(fixture.WorkspacePath);

        Assert.Equal(
            [
                new WorkspaceSolution(
                    "Alpha.sln",
                    WorkspaceSolutionKind.Sln),
                new WorkspaceSolution(
                    "Zeta.slnx",
                    WorkspaceSolutionKind.Slnx),
            ],
            first.Solutions);
        Assert.Equal(
            ["src/App/App.csproj", "src/Library/Library.csproj"],
            ProjectPaths(first));
        Assert.Equal(1, first.CSharpFileCount);
        Assert.Equal(
            [
                new WorkspaceRootMarker(
                    "Directory.Build.props",
                    WorkspaceRootMarkerKind.BuildProperties),
                new WorkspaceRootMarker(
                    "Directory.Build.targets",
                    WorkspaceRootMarkerKind.BuildTargets),
                new WorkspaceRootMarker(
                    "Directory.Packages.props",
                    WorkspaceRootMarkerKind.CentralPackageManagement),
                new WorkspaceRootMarker(
                    "dotnet-axi.yml",
                    WorkspaceRootMarkerKind.Configuration),
                new WorkspaceRootMarker(
                    "global.json",
                    WorkspaceRootMarkerKind.SdkSelection),
            ],
            first.RootMarkers);
        Assert.Equal(
            [
                "Filtered.slnf",
                "src/Legacy/Legacy.fsproj",
                "tools/FileApp.cs",
            ],
            first.Capabilities.Select(static capability => capability.Path));
        Assert.Equal(
            [
                WorkspaceCapabilityKind.SolutionFilter,
                WorkspaceCapabilityKind.UnsupportedProject,
                WorkspaceCapabilityKind.FileBasedCSharpApplication,
            ],
            first.Capabilities.Select(static capability => capability.Kind));
        Assert.All(
            first.Capabilities,
            static capability => Assert.Equal(
                WorkspaceCapabilitySupport.ReportedOnly,
                capability.Support));
        Assert.Equal(SolutionPaths(first), SolutionPaths(second));
        Assert.Equal(ProjectPaths(first), ProjectPaths(second));
        Assert.Equal(first.CSharpFileCount, second.CSharpFileCount);
        Assert.Equal(
            first.RootMarkers,
            second.RootMarkers);
        Assert.Equal(
            first.Capabilities,
            second.Capabilities);
    }

    [Fact]
    public async Task File_symbolic_links_are_cataloged_only_within_the_workspace()
    {
        await using var fixture = await _fixtures.CreateAsync(
            DiscoveryManifestPath());
        var externalProject = Path.Combine(
            fixture.ExternalPath,
            "External.csproj");
        var linkedProject = Path.Combine(
            fixture.WorkspacePath,
            "External.csproj");
        if (!TryCreateFileSymbolicLink(linkedProject, externalProject))
        {
            return;
        }

        var internalProject = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App",
            "App.csproj");
        var aliasProject = Path.Combine(
            fixture.WorkspacePath,
            "Alias.csproj");
        if (!TryCreateFileSymbolicLink(aliasProject, internalProject))
        {
            return;
        }

        var result = _discoverer.Discover(fixture.WorkspacePath);

        Assert.Contains(
            result.Projects,
            static project => project.Path == "Alias.csproj");
        Assert.DoesNotContain(
            result.Projects,
            static project => project.Path == "External.csproj");
    }

    [Fact]
    public async Task External_file_symbolic_link_does_not_select_a_workspace_root()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var markerFreeDirectory = Path.Combine(
            fixture.WorkspacePath,
            "unrelated");
        Directory.CreateDirectory(markerFreeDirectory);
        var externalSolution = Path.Combine(
            fixture.ExternalPath,
            "External.sln");
        await File.WriteAllTextAsync(externalSolution, string.Empty);
        if (!TryCreateFileSymbolicLink(
                Path.Combine(markerFreeDirectory, "External.sln"),
                externalSolution))
        {
            return;
        }

        var result = _discoverer.Discover(markerFreeDirectory);

        Assert.Equal(WorkspaceKind.Directory, result.WorkspaceKind);
        Assert.Empty(result.Solutions);
    }

    [Fact]
    public async Task Dangling_file_symbolic_link_does_not_select_a_workspace_root()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var markerFreeDirectory = Path.Combine(
            fixture.WorkspacePath,
            "unrelated");
        Directory.CreateDirectory(markerFreeDirectory);
        if (!TryCreateFileSymbolicLink(
                Path.Combine(markerFreeDirectory, "Ghost.sln"),
                Path.Combine(markerFreeDirectory, "Missing.sln")))
        {
            return;
        }

        var result = _discoverer.Discover(markerFreeDirectory);

        Assert.Equal(WorkspaceKind.Directory, result.WorkspaceKind);
        Assert.Empty(result.Solutions);
    }

    [Fact]
    public async Task Discovery_does_not_evaluate_an_invalid_project()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("broken-project"));

        var result = _discoverer.Discover(fixture.WorkspacePath);

        Assert.Equal(WorkspaceKind.Project, result.WorkspaceKind);
        Assert.Equal(["Broken.csproj"], ProjectPaths(result));
        Assert.False(
            Directory.Exists(Path.Combine(fixture.WorkspacePath, "obj")));
    }

    private static string[] SolutionPaths(WorkspaceDiscoveryResult result) =>
        result.Solutions.Select(static solution => solution.Path).ToArray();

    private static string[] ProjectPaths(WorkspaceDiscoveryResult result) =>
        result.Projects.Select(static project => project.Path).ToArray();

    private static string DiscoveryManifestPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "WorkspaceDiscovery",
            "fixture.json");

    private static string CatalogManifestPath(string fixtureName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Catalog",
            fixtureName,
            "fixture.json");

    private static bool TryCreateFileSymbolicLink(
        string path,
        string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
