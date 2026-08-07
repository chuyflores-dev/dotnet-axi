using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DotNetAxi.Cli.Tests;

public sealed class PassiveOperationBoundaryTests
{
    [Fact]
    public async Task Representative_passive_commands_reject_workspace_tools_and_have_no_forbidden_effects()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-passive-boundary-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "workspace");
        var tools = Path.Combine(workspace, "tools");
        var processMarker = Path.Combine(root, "dependency.started");
        await using var networkMonitor = new NetworkAttemptMonitor();
        Directory.CreateDirectory(workspace);
        PrepareGitMarker(workspace);
        PrepareSentinelTools(tools);
        var workspaceSdkMarker = PrepareWorkspaceSdk(workspace);
        await AssertWorkspaceSdkSentinelAsync(workspace, workspaceSdkMarker);
        File.Delete(workspaceSdkMarker);
        RemoveWorkspaceSdkRuntimeLink(workspace);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "App.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PassiveMustNotEvaluate>$([System.IO.File]::WriteAllText('evaluation.started', 'started'))</PassiveMustNotEvaluate>
                <RestoreSources>{networkMonitor.Url}/passive-must-not-restore</RestoreSources>
              </PropertyGroup>
              <ItemGroup>
                <Analyzer Include="analyzer-must-not-load.dll" />
              </ItemGroup>
              <Target Name="PassiveMustNotExecute" BeforeTargets="Restore;CoreCompile">
                <WriteLinesToFile File="restore-analyzer-generator.started" Lines="started" />
              </Target>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "Program.cs"),
            "class Program { void Run() { Console.WriteLine(\"needle\"); } }\n");
        var before = Snapshot(workspace);

        try
        {
            var commands = new[]
            {
                Array.Empty<string>(),
                new[] { "--version" },
                new[] { "search", "file", "Program" },
                new[] { "search", "text", "needle" },
                new[] { "search", "text", "needle", "--case-sensitive" },
                new[] { "search", "syntax", "invocation", "--name", "WriteLine" },
            };
            foreach (var command in commands)
            {
                var result = await RunAsync(
                    workspace,
                    tools,
                    processMarker,
                    networkMonitor.Url,
                    command);

                Assert.Equal(0, result.ExitCode);
                Assert.Equal(string.Empty, result.StandardError);
                Assert.Contains("status: success\n", result.StandardOutput);
            }

            Assert.False(File.Exists(processMarker));
            Assert.False(File.Exists(workspaceSdkMarker));
            Assert.False(networkMonitor.ConnectionAttempted);
            var after = Snapshot(workspace);
            Assert.True(before.Keys.ToHashSet(PathComparer()).SetEquals(after.Keys));
            foreach (var file in before)
            {
                Assert.Equal(file.Value, after[file.Key]);
            }

            Assert.False(Directory.Exists(Path.Combine(workspace, "obj")));
            Assert.False(Directory.Exists(Path.Combine(workspace, "bin")));
            Assert.False(File.Exists(Path.Combine(workspace, "evaluation.started")));
            Assert.False(File.Exists(
                Path.Combine(workspace, "restore-analyzer-generator.started")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunAsync(
        string workspace,
        string tools,
        string processMarker,
        string proxyUrl,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ProductionExecutablePath(),
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PATH"] = string.Join(
            Path.PathSeparator,
            tools,
            Environment.GetEnvironmentVariable("PATH"));
        startInfo.Environment["DNAXI_PASSIVE_BOUNDARY_PROCESS_MARKER"] =
            processMarker;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["HTTP_PROXY"] = proxyUrl;
        startInfo.Environment["HTTPS_PROXY"] = proxyUrl;
        startInfo.Environment["ALL_PROXY"] = proxyUrl;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The passive CLI did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static void PrepareGitMarker(string workspace)
    {
        var git = Path.Combine(workspace, ".git");
        Directory.CreateDirectory(Path.Combine(git, "objects"));
        File.WriteAllText(
            Path.Combine(git, "HEAD"),
            "ref: refs/heads/main\n");
    }

    private static void PrepareSentinelTools(string tools)
    {
        var source = TestApplicationDirectory();
        Directory.CreateDirectory(tools);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
        }

        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var appHost = Path.Combine(
            tools,
            $"DotNetAxi.Cli.TestApp{extension}");
        foreach (var command in new[] { "dotnet", "git", "rg" })
        {
            var path = Path.Combine(tools, $"{command}{extension}");
            File.Copy(appHost, path);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute);
            }
        }
    }

    private static string PrepareWorkspaceSdk(string workspace)
    {
        const string version = "10.0.302";
        var sdkDirectory = Path.Combine(
            workspace,
            ".dotnet",
            "sdk",
            version);
        Directory.CreateDirectory(sdkDirectory);
        var sourceDirectory = ProcessTestApplicationDirectory();
        const string sourceName = "DotNetAxi.DotNet.ProcessTestApp";
        foreach (var extension in new[]
                 {
                     ".dll",
                     ".deps.json",
                     ".runtimeconfig.json",
                 })
        {
            File.Copy(
                Path.Combine(sourceDirectory, sourceName + extension),
                Path.Combine(sdkDirectory, "dotnet" + extension));
        }

        File.WriteAllText(
            Path.Combine(workspace, "global.json"),
            $$"""
            {
              "sdk": {
                "version": "{{version}}",
                "paths": [".dotnet"]
              }
            }
            """);
        var sharedRoot = Path.Combine(workspace, ".dotnet", "shared");
        Directory.CreateDirectory(sharedRoot);
        var currentRuntime = Path.GetDirectoryName(
            typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException(
                "The current .NET runtime directory is unavailable.");
        var frameworkRoot = Directory.GetParent(currentRuntime)?.FullName
            ?? throw new InvalidOperationException(
                "The current .NET framework root is unavailable.");
        Directory.CreateSymbolicLink(
            Path.Combine(sharedRoot, "Microsoft.NETCore.App"),
            frameworkRoot);
        return Path.Combine(sdkDirectory, "workspace-sdk.executed");
    }

    private static void RemoveWorkspaceSdkRuntimeLink(string workspace)
    {
        var sharedRoot = Path.Combine(workspace, ".dotnet", "shared");
        Directory.Delete(Path.Combine(sharedRoot, "Microsoft.NETCore.App"));
        Directory.Delete(sharedRoot);
    }

    private static async Task AssertWorkspaceSdkSentinelAsync(
        string workspace,
        string marker)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--info");
        startInfo.Environment.Remove("DOTNET_MULTILEVEL_LOOKUP");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The workspace SDK sentinel did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 97 && File.Exists(marker),
            $"Workspace SDK sentinel did not execute (exit {process.ExitCode}).\n{await standardOutput}\n{await standardError}");
    }

    private static Dictionary<string, byte[]> Snapshot(string workspace) =>
        Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
            .ToDictionary(
                Path.GetFullPath,
                File.ReadAllBytes,
                PathComparer());

    private static string ProductionExecutablePath() => Path.Combine(
        RepositoryRoot(),
        "src",
        "DotNetAxi.Cli",
        "bin",
        Configuration(),
        "net10.0",
        OperatingSystem.IsWindows() ? "dnaxi.exe" : "dnaxi");

    private static string ProcessTestApplicationDirectory() => Path.Combine(
        RepositoryRoot(),
        "tests",
        "DotNetAxi.DotNet.ProcessTestApp",
        "bin",
        Configuration(),
        "net10.0");

    private static string TestApplicationDirectory() => Path.Combine(
        RepositoryRoot(),
        "tests",
        "DotNetAxi.Cli.TestApp",
        "bin",
        Configuration(),
        "net10.0");

    private static string RepositoryRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Configuration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class NetworkAttemptMonitor : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task<TcpClient> _connection;

        public NetworkAttemptMonitor()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}";
            _connection = _listener
                .AcceptTcpClientAsync(_cancellation.Token)
                .AsTask();
        }

        public string Url { get; }

        public bool ConnectionAttempted => _connection.IsCompletedSuccessfully;

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                using var connection = await _connection.ConfigureAwait(false);
            }
            catch (Exception exception)
                when (exception is OperationCanceledException
                    or ObjectDisposedException
                    or SocketException)
            {
            }

            _cancellation.Dispose();
        }
    }
}
