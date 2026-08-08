using DotNetAxi.DotNet;
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
        AssertOnlyAvailableSuggestion(result.StandardOutput);
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
        AssertOnlyAvailableSuggestion(result.StandardOutput);
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
        AssertOnlyAvailableSuggestion(result.StandardOutput);
    }

    [Fact]
    public async Task Nested_ambiguous_workspace_recommends_only_registered_help()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("ambiguous-solution"));
        var nestedDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App");

        var result = await InvokeHomeAsync(fixture, nestedDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/workspace\n  solution: unknown\n",
            result.StandardOutput);
        AssertOnlyAvailableSuggestion(result.StandardOutput);
    }

    [Fact]
    public async Task Symlinked_ambiguous_workspace_recommends_only_registered_help()
    {
        await using var fixture = await _fixtures.CreateAsync(
            CatalogManifestPath("ambiguous-solution"));
        var linkedDirectory = Path.Combine(
            fixture.WorkspacePath,
            "current");
        if (!TryCreateDirectorySymbolicLink(
                linkedDirectory,
                Path.Combine(fixture.WorkspacePath, "src", "App")))
        {
            return;
        }

        var result = await InvokeHomeAsync(fixture, linkedDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        AssertEnvelope(result.StandardOutput);
        Assert.Contains(
            "workspace:\n  root: ~/workspace\n  solution: unknown\n",
            result.StandardOutput);
        AssertOnlyAvailableSuggestion(result.StandardOutput);
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
        Assert.DoesNotContain("--solution", result.StandardOutput);
        AssertOnlyAvailableSuggestion(result.StandardOutput);
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
        AssertOnlyAvailableSuggestion(result.StandardOutput);
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

    [Fact]
    public async Task Version_capabilities_use_the_discovered_workspace_root()
    {
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-version-root-{Guid.NewGuid():N}");
        var currentDirectory = Path.Combine(workspaceRoot, "src", "nested");
        var gitDirectory = Path.Combine(workspaceRoot, ".git");
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
        File.WriteAllText(
            Path.Combine(gitDirectory, "HEAD"),
            "ref: refs/heads/main\n");
        Directory.CreateDirectory(currentDirectory);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "tools"));
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var reporter = new RecordingCapabilityReporter();
            var host = CliApplication.Create(
                output,
                error,
                () => new HomeInvocationContext(
                    currentDirectory,
                    Path.Combine(workspaceRoot, "dnaxi"),
                    workspaceRoot),
                static () => new WorkspaceDiscoverer(),
                static () => throw new InvalidOperationException(
                    "Version reporting created the Git dependency."),
                () => reporter);

            var exitCode = await host.InvokeAsync(["--version"]);

            Assert.Equal(0, exitCode);
            Assert.Equal(workspaceRoot, reporter.WorkspaceRoot);
            Assert.Equal(1, reporter.Calls);
            Assert.Contains("command: version\n", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
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
            static () => WorktreeStateInspector.CreatePassive(
                new ProcessRunner()));

        var exitCode = await host.InvokeAsync([]);
        return new HomeInvocationResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private sealed class RecordingCapabilityReporter : ICapabilityReporter
    {
        public int Calls { get; private set; }

        public string? WorkspaceRoot { get; private set; }

        public ValueTask<CapabilityReport> ReportAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            WorkspaceRoot = workspaceRoot;
            return ValueTask.FromResult(
                new CapabilityReport(
                    new SelectedHostCapability(
                        null,
                        CapabilityAvailability.Missing),
                    MissingVersionedCapability(),
                    MissingVersionedCapability(),
                    MissingVersionedCapability(),
                    MissingVersionedCapability(),
                    [],
                    []));
        }

        private static VersionedCapability MissingVersionedCapability() =>
            new(
                null,
                CapabilityAvailability.Missing,
                CapabilityCompatibility.Unverified);
    }

    private static void AssertEnvelope(string output)
    {
        Assert.StartsWith("schema: dotnet-axi/v1\n", output);
        Assert.Contains("command: home\n", output);
        Assert.Contains("status: success\n", output);
        Assert.Contains(
            "description: \"Search, analyze, validate, and safely change the current .NET workspace\"\n",
            output);
        Assert.Contains("tool: dotnet-axi\n", output);
        Assert.Contains($"tool_version: {ToolVersion.Current}\n", output);
        Assert.Contains("output_schema: dotnet-axi/v1\n", output);
        Assert.Contains(
            "capabilities:\n  selected_host:\n",
            output);
        Assert.Contains("\n  sdk:\n", output);
        Assert.Contains("\n  ms_build:\n", output);
        Assert.Contains("\n  roslyn:\n", output);
        Assert.Contains("\n  optional_engines[1]", output);
        Assert.Contains(
            "command_engines[1]{command,preferred_engine,selected_engine,degradation}",
            output);
        Assert.Contains(
            "analysis:\n  status: not_loaded\n  compiler_errors: unknown\n",
            output);
    }

    private static void AssertOnlyAvailableSuggestion(string output)
    {
        Assert.Contains(
            $"suggestions[1]:\n  - command: dnx\n    arguments[5]: dnaxi@{ToolVersion.Current},\"--verbosity\",quiet,\"--\",\"--help\"",
            output);
        Assert.DoesNotContain("search,symbol", output);
        Assert.DoesNotContain("analyze,changed", output);
        Assert.DoesNotContain("validate,\"--profile\",fast", output);
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

    private sealed record HomeInvocationResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
