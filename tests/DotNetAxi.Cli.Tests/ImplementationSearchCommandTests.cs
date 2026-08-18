using System.Diagnostics;

namespace DotNetAxi.Cli.Tests;

public sealed class ImplementationSearchCommandTests
{
    [Fact]
    public async Task Implementation_search_returns_verified_semantic_locations()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "search",
            "implementations",
            "Demo.IService",
            "--full");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("command: search implementations", result.Output);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("classification: executing", result.Output);
        Assert.Contains("resolution: semantic", result.Output);
        Assert.Contains("coverage: complete", result.Output);
        Assert.Contains("target_id: symbol/v2/", result.Output);
        Assert.Contains("count: 2", result.Output);
        Assert.Contains("matches[2]{file,line,project,framework}:", result.Output);
        Assert.Contains("Service.cs", result.Output);
    }

    [Fact]
    public async Task Implementation_search_bounds_output_and_preserves_complete_retrieval()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "search",
            "implementations",
            "Demo.IService",
            "--complete",
            "--configuration",
            "Release",
            "--framework",
            "net10.0",
            "--property",
            "Flavor=cli",
            "--limit",
            "1",
            "--fields",
            "id",
            "target_identity");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("scope_mode: complete", result.Output);
        Assert.Contains("count: 1", result.Output);
        Assert.True(
            result.Output.Contains("truncated: true", StringComparison.Ordinal),
            result.Output);
        Assert.Contains("retrieval_command:", result.Output);
        Assert.Contains("search implementations 'Demo.IService'", result.Output);
        Assert.Contains("--complete", result.Output);
        Assert.Contains("--configuration 'Release'", result.Output);
        Assert.Contains("--framework 'net10.0'", result.Output);
        Assert.Contains("--property 'Flavor=cli'", result.Output);
        Assert.Contains("--fields 'id,target_identity' --full", result.Output);
        Assert.DoesNotContain("--limit 1", result.Output);
    }

    [Fact]
    public async Task Missing_target_is_a_structured_failure_before_traversal()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var result = await workspace.RunAsync(
            "search",
            "implementations",
            "Demo.Missing");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("status: failed", result.Output);
        Assert.Contains("target_status: notfound", result.Output);
        Assert.Contains("semantic.target_not_found", result.Output);
        Assert.Contains("dnaxi search symbol", result.Output);
        Assert.Contains("variants: []", result.Output);
    }

    [Fact]
    public void Implementation_search_is_registered_as_executing_inspection()
    {
        var host = CliApplication.Create(
            new StringWriter(),
            new StringWriter());

        var parsed = host.Parse(
            ["search", "implementations", "Demo.IService"]);

        Assert.Equal(
            DotNetAxi.Contracts.OperationClassification.Executing,
            host.ResolvePolicy(parsed).Classification);
        Assert.True(host.ResolvePolicy(parsed).MayExecuteRepositoryCode);
        Assert.False(host.ResolvePolicy(parsed).MayAccessNetwork);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-implementation-command-tests",
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
                    $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
                      </PropertyGroup>
                      <ItemGroup>
                        <Reference Include="System.Private.CoreLib">
                          <HintPath>{System.Security.SecurityElement.Escape(typeof(object).Assembly.Location)}</HintPath>
                        </Reference>
                      </ItemGroup>
                    </Project>
                    """);
                await workspace.WriteAsync(
                    "Service.cs",
                    """
                    namespace Demo;

                    public interface IService
                    {
                        void Run();
                    }

                    public sealed class ServiceA : IService
                    {
                        public void Run() { }
                    }

                    public sealed class ServiceB : IService
                    {
                        public void Run() { }
                    }
                    """);
                await workspace.WriteAsync(
                    "Workspace.slnx",
                    """
                    <Solution>
                      <Project Path="App.csproj" />
                    </Solution>
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

        public async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        public async Task<(int ExitCode, string Output)> RunAsync(
            params string[] arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                    ?? "dotnet",
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(typeof(Cli.Program).Assembly.Location);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                string.IsNullOrEmpty(error),
                $"Expected empty stderr, got: {error}");
            return (process.ExitCode, output);
        }

        private async Task RestoreAsync()
        {
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                    ?? "dotnet",
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("restore");
            start.ArgumentList.Add("App.csproj");
            start.ArgumentList.Add("--ignore-failed-sources");
            start.ArgumentList.Add("--nologo");
            start.ArgumentList.Add("--verbosity");
            start.ArgumentList.Add("quiet");
            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Fixture restore failed. stdout: {output} stderr: {error}");
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
