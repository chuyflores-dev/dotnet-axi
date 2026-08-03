using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class WorkspacePathResolverTests
{
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Theory]
    [InlineData("../Unicode/Café😀.cs")]
    [InlineData("..\\Unicode\\Café😀.cs")]
    public async Task Relative_inputs_resolve_from_the_current_directory(
        string inputPath)
    {
        await using var fixture = await CreateFixtureAsync();
        var currentDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Nested");
        var resolver = new WorkspacePathResolver(
            fixture.WorkspacePath,
            currentDirectory);

        var resolved = resolver.ResolveInput(inputPath);

        Assert.Equal(
            Path.Combine(
                fixture.WorkspacePath,
                "src",
                "Unicode",
                "Café😀.cs"),
            resolved.FullPath);
        Assert.Equal("src/Unicode/Café😀.cs", resolved.Path);
        Assert.DoesNotContain('\\', resolved.Path);
        Assert.False(resolved.IsExternal);
        Assert.False(resolved.EscapesThroughSymbolicLink);
    }

    [Fact]
    public async Task Workspace_selection_uses_current_directory_unicode_paths()
    {
        await using var fixture = await CreateFixtureAsync();
        var currentDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Nested");
        var discovery = new WorkspaceDiscoverer().Discover(currentDirectory);

        var selection = new WorkspaceEntryPointSelector().Select(
            discovery,
            new WorkspaceSelectionRequest(
                project: "..\\Linked\\Café😀.csproj"));

        Assert.Equal("src/Linked/Café😀.csproj", selection.Path);
    }

    [Fact]
    public async Task External_inputs_require_explicit_scope_and_stay_labeled()
    {
        await using var fixture = await CreateFixtureAsync();
        var currentDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Nested");
        var externalFile = Path.Combine(
            fixture.ExternalPath,
            "Outside😀.cs");
        var inputPath = Path.GetRelativePath(currentDirectory, externalFile);
        var resolver = new WorkspacePathResolver(
            fixture.WorkspacePath,
            currentDirectory);

        var error = Assert.Throws<WorkspacePathScopeException>(
            () => resolver.ResolveInput(inputPath));
        var resolved = resolver.ResolveInput(
            inputPath,
            WorkspacePathScope.Explicit);

        Assert.Equal(WorkspacePathScopeViolation.ExternalPath, error.Violation);
        Assert.Equal("workspace.path_external", error.Code);
        Assert.Equal("../external/Outside😀.cs", error.Path);
        Assert.Equal(externalFile, resolved.FullPath);
        Assert.Equal("../external/Outside😀.cs", resolved.Path);
        Assert.True(resolved.IsExternal);
        Assert.False(resolved.EscapesThroughSymbolicLink);
    }

    [Fact]
    public async Task Symbolic_link_escapes_require_explicit_scope()
    {
        await using var fixture = await CreateFixtureAsync();
        var currentDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Nested");
        var linkPath = Path.Combine(currentDirectory, "external-link");
        if (!TryCreateDirectorySymbolicLink(linkPath, fixture.ExternalPath))
        {
            return;
        }

        var resolver = new WorkspacePathResolver(
            fixture.WorkspacePath,
            currentDirectory);
        const string inputPath = "external-link/Outside😀.cs";

        var error = Assert.Throws<WorkspacePathScopeException>(
            () => resolver.ResolveInput(inputPath));
        var resolved = resolver.ResolveInput(
            inputPath,
            WorkspacePathScope.Explicit);

        Assert.Equal(
            WorkspacePathScopeViolation.SymbolicLinkEscape,
            error.Violation);
        Assert.Equal("workspace.path_link_escape", error.Code);
        Assert.Equal("../external/Outside😀.cs", error.Path);
        Assert.Equal("../external/Outside😀.cs", resolved.Path);
        Assert.True(resolved.IsExternal);
        Assert.True(resolved.EscapesThroughSymbolicLink);
    }

    [Fact]
    public async Task Parent_segments_are_applied_after_symbolic_link_targets()
    {
        await using var fixture = await CreateFixtureAsync();
        var externalSubdirectory = Path.Combine(
            fixture.ExternalPath,
            "subdirectory");
        Directory.CreateDirectory(externalSubdirectory);
        var linkPath = Path.Combine(
            fixture.WorkspacePath,
            "external-link");
        if (!TryCreateDirectorySymbolicLink(
                linkPath,
                externalSubdirectory))
        {
            return;
        }

        var resolver = new WorkspacePathResolver(
            fixture.WorkspacePath,
            fixture.WorkspacePath);
        const string inputPath = "external-link/../Outside😀.cs";

        var error = Assert.Throws<WorkspacePathScopeException>(
            () => resolver.ResolveInput(inputPath));
        var resolved = resolver.ResolveInput(
            inputPath,
            WorkspacePathScope.Explicit);

        Assert.Equal(
            WorkspacePathScopeViolation.SymbolicLinkEscape,
            error.Violation);
        Assert.Equal("../external/Outside😀.cs", error.Path);
        Assert.Equal("../external/Outside😀.cs", resolved.Path);
        Assert.True(resolved.IsExternal);
        Assert.True(resolved.EscapesThroughSymbolicLink);
    }

    [Fact]
    public async Task A_symbolic_link_workspace_root_keeps_physical_paths_internal()
    {
        await using var fixture = await CreateFixtureAsync();
        var workspaceAlias = Path.Combine(fixture.RootPath, "workspace-alias");
        if (!TryCreateDirectorySymbolicLink(
                workspaceAlias,
                fixture.WorkspacePath))
        {
            return;
        }

        var resolver = new WorkspacePathResolver(
            workspaceAlias,
            workspaceAlias);

        var resolved = resolver.NormalizeOutput(
            Path.Combine(
                fixture.WorkspacePath,
                "src",
                "Unicode",
                "Café😀.cs"));

        Assert.Equal("src/Unicode/Café😀.cs", resolved.Path);
        Assert.False(resolved.IsExternal);
        Assert.False(resolved.EscapesThroughSymbolicLink);
    }

    [Fact]
    public async Task A_symbolic_link_workspace_ancestor_keeps_physical_paths_internal()
    {
        await using var fixture = await CreateFixtureAsync();
        var physicalParent = Path.Combine(
            fixture.RootPath,
            "physical-parent");
        var physicalWorkspace = Path.Combine(physicalParent, "workspace");
        Directory.CreateDirectory(physicalWorkspace);
        var sourcePath = Path.Combine(physicalWorkspace, "File.cs");
        await File.WriteAllTextAsync(sourcePath, string.Empty);
        var parentAlias = Path.Combine(fixture.RootPath, "parent-alias");
        if (!TryCreateDirectorySymbolicLink(parentAlias, physicalParent))
        {
            return;
        }

        var workspaceAlias = Path.Combine(parentAlias, "workspace");
        var resolver = new WorkspacePathResolver(
            workspaceAlias,
            workspaceAlias);

        var resolved = resolver.NormalizeOutput(sourcePath);

        Assert.Equal("File.cs", resolved.Path);
        Assert.False(resolved.IsExternal);
        Assert.False(resolved.EscapesThroughSymbolicLink);
    }

    [Fact]
    public async Task A_dangling_directory_link_escape_requires_explicit_scope()
    {
        await using var fixture = await CreateFixtureAsync();
        var target = Path.Combine(
            fixture.ExternalPath,
            "missing-directory");
        var linkPath = Path.Combine(
            fixture.WorkspacePath,
            "dangling-directory");
        if (!TryCreateDirectorySymbolicLink(linkPath, target))
        {
            return;
        }

        AssertDanglingEscape(
            new WorkspacePathResolver(
                fixture.WorkspacePath,
                fixture.WorkspacePath),
            "dangling-directory",
            "../external/missing-directory");
    }

    [Fact]
    public async Task A_dangling_file_link_escape_requires_explicit_scope()
    {
        await using var fixture = await CreateFixtureAsync();
        var target = Path.Combine(
            fixture.ExternalPath,
            "missing-file.cs");
        var linkPath = Path.Combine(
            fixture.WorkspacePath,
            "dangling-file.cs");
        if (!TryCreateFileSymbolicLink(linkPath, target))
        {
            return;
        }

        AssertDanglingEscape(
            new WorkspacePathResolver(
                fixture.WorkspacePath,
                fixture.WorkspacePath),
            "dangling-file.cs",
            "../external/missing-file.cs");
    }

    [Fact]
    public async Task Passive_discovery_does_not_follow_directory_links()
    {
        await using var fixture = await CreateFixtureAsync();
        var internalLink = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "InternalLink");
        if (!TryCreateDirectorySymbolicLink(
                internalLink,
                Path.Combine(fixture.WorkspacePath, "src", "Linked")))
        {
            return;
        }

        var externalLink = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "ExternalLink");
        if (!TryCreateDirectorySymbolicLink(
                externalLink,
                fixture.ExternalPath))
        {
            return;
        }

        var discovery = new WorkspaceDiscoverer().Discover(
            Path.Combine(fixture.WorkspacePath, "src", "Nested"));

        Assert.Equal(
            ["Workspace.csproj", "src/Linked/Café😀.csproj"],
            discovery.Projects.Select(static project => project.Path));
        Assert.DoesNotContain(
            discovery.Projects,
            static project => project.Path.Contains(
                "InternalLink",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            discovery.Projects,
            static project => project.Path.Contains(
                "ExternalLink",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Cross_volume_external_identities_are_relative_and_portable()
    {
        var identity = WorkspacePathResolver.CrossVolumeExternalIdentity(
            @"C:\",
            @"Unicode\Café😀.cs");

        Assert.True(identity.IsExternal);
        Assert.StartsWith("../.external-volume/", identity.Path);
        Assert.EndsWith("/Unicode/Café😀.cs", identity.Path);
        Assert.DoesNotContain('\\', identity.Path);
        Assert.False(Path.IsPathRooted(identity.Path));
    }

    private ValueTask<RepositoryFixture> CreateFixtureAsync() =>
        _fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PathsAndLocations",
                "fixture.json"));

    private static bool TryCreateDirectorySymbolicLink(
        string path,
        string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
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

    private static void AssertDanglingEscape(
        WorkspacePathResolver resolver,
        string inputPath,
        string expectedPath)
    {
        var error = Assert.Throws<WorkspacePathScopeException>(
            () => resolver.ResolveInput(inputPath));
        var resolved = resolver.ResolveInput(
            inputPath,
            WorkspacePathScope.Explicit);

        Assert.Equal(
            WorkspacePathScopeViolation.SymbolicLinkEscape,
            error.Violation);
        Assert.Equal(expectedPath, error.Path);
        Assert.Equal(expectedPath, resolved.Path);
        Assert.True(resolved.IsExternal);
        Assert.True(resolved.EscapesThroughSymbolicLink);
    }
}
