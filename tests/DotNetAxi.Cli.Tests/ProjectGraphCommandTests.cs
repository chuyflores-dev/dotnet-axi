namespace DotNetAxi.Cli.Tests;

public sealed class ProjectGraphCommandTests
{
    [Fact]
    public async Task Projects_retains_unsupported_and_unrestored_nodes_as_partial_coverage()
    {
        var fixtures = new DotNetAxi.Testing.RepositoryFixtureFactory();
        await using var fixture = await fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ProjectGraph",
                "coverage",
                "fixture.json"));
        await AddAssetsAsync(
            fixture.WorkspacePath,
            "src/Multi/Multi.csproj",
            """{ "version": 3, "targets": { "net9.0": {}, "net10.0": {} } }""");

        var result = await RunAsync(
            fixture.WorkspacePath,
            "graph",
            "projects",
            "--solution",
            "Coverage.slnx",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("status: partial", result.Output);
        Assert.Contains("evaluation_state: unsupported", result.Output);
        Assert.Contains("unrestored", result.Output);
        Assert.Contains("missingassets", result.Output);
        Assert.Contains("xunit", result.Output);
        Assert.Contains(
            "project_path: src/Multi/Multi.csproj\n        configuration: Debug\n        framework: net9.0",
            result.Output);
    }

    [Fact]
    public async Task Projects_never_emits_a_relationship_without_bounded_endpoint_nodes()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "graph",
            "projects",
            "--property",
            "Flavor=conditional",
            "--limit",
            "1");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("nodes:\n  count: 1", result.Output);
        Assert.Contains("relationships:\n  count: 0", result.Output);
        Assert.DoesNotContain("source_id:", result.Output);
    }

    [Fact]
    public async Task Projects_returns_conditional_project_and_package_references()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "graph",
            "projects",
            "--property",
            "Flavor=conditional",
            "--framework",
            "net9.0",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("command: graph projects", result.Output);
        Assert.Contains("status: partial", result.Output);
        Assert.Contains("resolution: syntax", result.Output);
        Assert.Contains("coverage: partial", result.Output);
        Assert.Contains("project_reference", result.Output);
        Assert.Contains("package_reference", result.Output);
        Assert.Contains("Library/Library.csproj", result.Output);
        Assert.Contains("xunit", result.Output);
    }

    [Fact]
    public async Task Dependencies_returns_only_the_requested_project_outgoing_edges()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "graph",
            "dependencies",
            "App.csproj",
            "--property",
            "Flavor=conditional",
            "--framework",
            "net9.0",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("command: graph dependencies", result.Output);
        Assert.Contains("App.csproj", result.Output);
        Assert.Contains("Library/Library.csproj", result.Output);
        Assert.Contains("xunit", result.Output);
        Assert.DoesNotContain("Unused/Unused.csproj", result.Output);
    }

    [Fact]
    public async Task Projects_omits_edges_from_an_unselected_multi_target_variant()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "graph",
            "projects",
            "--property",
            "Flavor=conditional",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("status: partial", result.Output);
        Assert.DoesNotContain("kind: project_reference", result.Output);
    }

    [Fact]
    public async Task Cycles_retains_partial_failed_evaluation_evidence()
    {
        var fixtures = new DotNetAxi.Testing.RepositoryFixtureFactory();
        await using var fixture = await fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ProjectGraph",
                "cycle-scope",
                "fixture.json"));
        await AddAssetsAsync(fixture.WorkspacePath, "src/A/A.csproj", """{ "version": 3, "targets": { "net10.0": {} } }""");
        await AddAssetsAsync(fixture.WorkspacePath, "src/B/B.csproj", """{ "version": 3, "targets": { "net10.0": {} } }""");
        await AddAssetsAsync(fixture.WorkspacePath, "src/Root/Root.csproj", """{ "version": 3, "targets": { "net10.0": {} } }""");
        await AddAssetsAsync(fixture.WorkspacePath, "src/Unrelated/Unrelated.csproj", """{ "version": 3, "targets": { "net10.0": {} } }""");

        var result = await RunAsync(
            fixture.WorkspacePath,
            "graph",
            "cycles",
            "--solution",
            "Cycle.slnx",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("command: graph cycles", result.Output);
        Assert.Contains("status: partial", result.Output);
        Assert.Contains("cycles:", result.Output);
        Assert.Contains("circular_dependency", result.Output);
    }

    [Fact]
    public async Task Path_returns_a_bounded_shortest_project_reference_path()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "graph", "path",
            "--from", "App.csproj",
            "--to", "Library/Library.csproj",
            "--property", "Flavor=conditional",
            "--framework", "net9.0",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("command: graph path", result.Output);
        Assert.Contains("shortest_depth: 1", result.Output);
        Assert.Contains("paths:\n  count: 1", result.Output);
        Assert.Contains("App.csproj", result.Output);
        Assert.Contains("Library/Library.csproj", result.Output);
    }

    [Fact]
    public async Task Impact_accepts_an_evaluated_project_target()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "graph", "impact", "Library/Library.csproj",
            "--property", "Flavor=conditional",
            "--framework", "net9.0",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("command: graph impact", result.Output);
        Assert.Contains("target_id: project/v1/", result.Output);
        Assert.Contains("affected_projects:", result.Output);
        Assert.Contains("App.csproj", result.Output);
        Assert.Contains("candidate_tests:", result.Output);
        Assert.Contains("xunit", result.Output);
        Assert.Contains("applicability: not_applicable", result.Output);
        Assert.Contains("not applicable to a project target", result.Output);
    }

    [Fact]
    public void Path_retrieval_command_escapes_quoted_endpoints()
    {
        var request = ProjectPathCommandRequest.Create(
            "App.csproj",
            "Direct'ly/Target.csproj",
            10,
            solution: null,
            project: null,
            configuration: null,
            framework: "net9.0",
            properties: ["Flavor=quoted"],
            limit: 1,
            limitSpecified: true,
            full: false);

        Assert.Equal(
            "dnaxi graph path --from 'App.csproj' --to 'Direct'\\''ly/Target.csproj' --framework 'net9.0' --property 'Flavor=quoted'",
            ProjectPathCommandHandler.RetrievalCommand(request));
    }

    [Fact]
    public void Graph_commands_are_registered_as_executing_inspection()
    {
        var host = CliApplication.Create(new StringWriter(), new StringWriter());

        var projects = host.Parse(["graph", "projects"]);
        var dependencies = host.Parse(["graph", "dependencies", "App.csproj"]);
        var cycles = host.Parse(["graph", "cycles"]);
        var path = host.Parse(["graph", "path", "--from", "App.csproj", "--to", "Library/Library.csproj"]);

        Assert.Equal(
            DotNetAxi.Contracts.OperationClassification.Executing,
            host.ResolvePolicy(projects).Classification);
        Assert.Equal(
            DotNetAxi.Contracts.OperationClassification.Executing,
            host.ResolvePolicy(dependencies).Classification);
        Assert.True(host.ResolvePolicy(projects).MayExecuteRepositoryCode);
        Assert.False(host.ResolvePolicy(projects).MayAccessNetwork);
        Assert.Equal(
            DotNetAxi.Contracts.OperationClassification.Executing,
            host.ResolvePolicy(cycles).Classification);
        Assert.Equal(
            DotNetAxi.Contracts.OperationClassification.Executing,
            host.ResolvePolicy(path).Classification);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-project-graph-command-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var workspace = new TestWorkspace();
            try
            {
                await workspace.WriteAsync(
                    "App.csproj",
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
                      </PropertyGroup>
                      <ItemGroup>
                        <ProjectReference Include="Library/Library.csproj" Condition="'$(Flavor)' == 'conditional' and '$(TargetFramework)' == 'net9.0'" />
                        <PackageReference Include="xunit" Version="2.9.3" />
                      </ItemGroup>
                    </Project>
                    """);
                await workspace.WriteAsync(
                    "Library/Library.csproj",
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFrameworks>net8.0;net9.0</TargetFrameworks></PropertyGroup>
                    </Project>
                    """);
                await workspace.WriteAsync(
                    "Unused/Unused.csproj",
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                    </Project>
                    """);
                await workspace.RestoreAsync();
                return workspace;
            }
            catch
            {
                workspace.Dispose();
                throw;
            }
        }

        public async Task<(int ExitCode, string Output)> RunAsync(
            params string[] arguments)
            => await ProjectGraphCommandTests.RunAsync(Root, arguments);

        private async Task RestoreAsync()
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("restore");
            start.ArgumentList.Add("App.csproj");
            start.ArgumentList.Add("-p:Flavor=conditional");
            start.ArgumentList.Add("--ignore-failed-sources");
            start.ArgumentList.Add("--nologo");
            start.ArgumentList.Add("--verbosity");
            start.ArgumentList.Add("quiet");
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Fixture restore failed. stdout: {output} stderr: {error}");
            }
        }

        private async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string root,
        params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Cli.Program).Assembly.Location);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(string.IsNullOrEmpty(error), error);
        return (process.ExitCode, output);
    }

    private static async Task AddAssetsAsync(
        string workspacePath,
        string projectPath,
        string contents)
    {
        var project = Path.Combine(
            workspacePath,
            projectPath.Replace('/', Path.DirectorySeparatorChar));
        var assets = Path.Combine(
            Path.GetDirectoryName(project)!,
            "obj",
            "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        await File.WriteAllTextAsync(assets, contents);
    }
}
