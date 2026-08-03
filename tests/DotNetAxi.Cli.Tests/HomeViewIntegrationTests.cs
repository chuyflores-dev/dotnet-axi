using DotNetAxi.Testing;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli.Tests;

public sealed class HomeViewIntegrationTests
{
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Fact]
    public async Task Git_workspace_renders_live_catalog_and_worktree_state()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("git-worktree"),
            new RepositoryFixtureOptions(
                FixtureExecutionPermissions.Tooling));
        await fixture.PrepareGitAsync();

        var result = await InvokeHomeAsync(
            fixture,
            fixture.WorkspacePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "bin: ~/.dotnet/tools/dnaxi\n",
            result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/workspace\n  project: Workspace.csproj\n  projects: 1\n  csharp_files: 4\n",
            result.StandardOutput);
        Assert.Contains(
            "git:\n  branch: main\n  changed_files: 5\n",
            result.StandardOutput);
        Assert.Contains(
            "arguments[2]: analyze,changed",
            result.StandardOutput);
        Assert.Contains("suggestions[3]:", result.StandardOutput);
    }

    [Fact]
    public async Task Non_git_workspace_keeps_git_state_unknown()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var projectDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Single");

        var result = await InvokeHomeAsync(fixture, projectDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/workspace/src/Single\n  project: Single.csproj\n  projects: 1\n  csharp_files: 1\n",
            result.StandardOutput);
        Assert.Contains(
            "git:\n  branch: unknown\n  changed_files: unknown\n",
            result.StandardOutput);
        Assert.DoesNotContain("analyze,changed", result.StandardOutput);
        Assert.Contains("suggestions[2]:", result.StandardOutput);
    }

    [Fact]
    public async Task Ambiguous_workspace_is_not_silently_selected()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("ambiguous-solution"));

        var result = await InvokeHomeAsync(
            fixture,
            fixture.WorkspacePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/workspace\n  solution: unknown\n  projects: 1\n  csharp_files: 0\n",
            result.StandardOutput);
        Assert.Contains(
            "search,symbol,<name>,\"--solution\",First.slnx",
            result.StandardOutput);
        Assert.DoesNotContain("Second.slnx", result.StandardOutput);
        Assert.DoesNotContain("analyze,changed", result.StandardOutput);
        Assert.Contains("suggestions[2]:", result.StandardOutput);
    }

    [Fact]
    public async Task Ambiguous_workspace_selector_is_valid_from_a_nested_directory()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("ambiguous-solution"));
        var nestedDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App");
        const string emittedSelector = "../../First.slnx";

        var result = await InvokeHomeAsync(fixture, nestedDirectory);
        var selected = new WorkspaceEntryPointSelector().Select(
            new WorkspaceDiscoverer().Discover(nestedDirectory),
            new WorkspaceSelectionRequest(solution: emittedSelector));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/workspace\n  solution: unknown\n",
            result.StandardOutput);
        Assert.Contains(
            $"search,symbol,<name>,\"--solution\",{emittedSelector}",
            result.StandardOutput);
        Assert.Equal("First.slnx", selected.Path);
    }

    [Fact]
    public async Task Mixed_layout_ambiguity_uses_the_candidate_entry_point_kind()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var workspace = Path.Combine(fixture.ExternalPath, "mixed");
        Directory.CreateDirectory(Path.Combine(workspace, "nested"));
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "dotnet-axi.yml"),
            string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "nested", "Nested.slnx"),
            "<Solution />");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "First.csproj"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "Second.csproj"),
            "<Project />");

        var result = await InvokeHomeAsync(fixture, workspace);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/external/mixed\n  project: unknown\n",
            result.StandardOutput);
        Assert.DoesNotContain("solution: unknown", result.StandardOutput);
        Assert.Contains(
            "search,symbol,<name>,\"--project\",First.csproj",
            result.StandardOutput);
        Assert.DoesNotContain("--solution", result.StandardOutput);
    }

    [Fact]
    public async Task Empty_directory_renders_zero_counts_and_unknown_state()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var emptyDirectory = Path.Combine(fixture.ExternalPath, "empty");
        Directory.CreateDirectory(emptyDirectory);

        var result = await InvokeHomeAsync(fixture, emptyDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/external/empty\n  solution: unknown\n  projects: 0\n  csharp_files: 0\n",
            result.StandardOutput);
        Assert.Contains(
            "git:\n  branch: unknown\n  changed_files: unknown\n",
            result.StandardOutput);
        Assert.Contains("suggestions[1]:", result.StandardOutput);
        Assert.Contains("arguments[1]: \"--help\"", result.StandardOutput);
    }

    [Fact]
    public async Task Home_does_not_evaluate_or_execute_the_selected_project()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("single-project"));
        var projectDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Single");
        var projectPath = Path.Combine(projectDirectory, "Single.csproj");
        var executionMarker = Path.Combine(
            projectDirectory,
            "executing-dependency.started");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <HomeMustNotEvaluate>$([System.Int32]::Parse('dnaxi-home-must-not-evaluate'))</HomeMustNotEvaluate>
              </PropertyGroup>
              <Target Name="HomeMustNotExecute" BeforeTargets="Restore;Build;Compile">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/executing-dependency.started" Lines="started" />
              </Target>
            </Project>
            """);

        var result = await InvokeHomeAsync(fixture, projectDirectory);

        Assert.Equal(0, result.ExitCode);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains("project: Single.csproj", result.StandardOutput);
        Assert.False(File.Exists(executionMarker));
        Assert.False(Directory.Exists(Path.Combine(projectDirectory, "obj")));
        Assert.False(Directory.Exists(Path.Combine(projectDirectory, "bin")));
    }

    [Theory]
    [InlineData("--help", "help", 0)]
    [InlineData("--version", "version", 0)]
    [InlineData("--unknown", "home", 2)]
    public async Task Parser_owned_output_does_not_create_home_dependencies(
        string option,
        string expectedCommand,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var contextFactoryCalls = 0;
        var workspaceFactoryCalls = 0;
        var gitFactoryCalls = 0;
        var host = CliApplication.Create(
            output,
            error,
            () =>
            {
                contextFactoryCalls++;
                throw new InvalidOperationException(
                    "Parser-owned output captured the invocation context.");
            },
            () =>
            {
                workspaceFactoryCalls++;
                throw new InvalidOperationException(
                    "Parser-owned output created the workspace dependency.");
            },
            () =>
            {
                gitFactoryCalls++;
                throw new InvalidOperationException(
                    "Parser-owned output created the Git dependency.");
            });

        var exitCode = await host.InvokeAsync([option]);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(0, contextFactoryCalls);
        Assert.Equal(0, workspaceFactoryCalls);
        Assert.Equal(0, gitFactoryCalls);
        Assert.Contains($"command: {expectedCommand}\n", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    private static async Task<HomeInvocationResult> InvokeHomeAsync(
        RepositoryFixture fixture,
        string currentDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CliApplication.Create(
            output,
            error,
            () => new HomeInvocationContext(
                currentDirectory,
                Path.Combine(
                    fixture.RootPath,
                    ".dotnet",
                    "tools",
                    "dnaxi"),
                fixture.RootPath),
            static () => new WorkspaceDiscoverer(),
            static () => new WorktreeStateInspector());

        var exitCode = await host.InvokeAsync([]);
        return new HomeInvocationResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private static void AssertEnvelope(string output)
    {
        Assert.StartsWith("schema: dotnet-axi/v1\n", output);
        Assert.Contains("command: home\n", output);
        Assert.Contains("status: success\n", output);
        Assert.Contains(
            "description: \"Search, analyze, validate, and safely change the current .NET workspace\"\n",
            output);
        Assert.Contains(
            "analysis:\n  status: not_loaded\n  compiler_errors: unknown\n",
            output);
    }

    private static string CatalogManifestPath(string fixtureName) =>
        Path.Combine(
            RepositoryRoot(),
            "tests",
            "Fixtures",
            "Catalog",
            fixtureName,
            "fixture.json");

    private static string RepositoryRoot() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                ".."));

    private sealed record HomeInvocationResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
