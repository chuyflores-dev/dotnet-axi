using System.Text;
using DotNetAxi.DotNet;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli.Tests;

public sealed class SlimDefaultOutputGoldenTests
{
    [Theory]
    [MemberData(nameof(CommandCases))]
    public async Task Default_command_outputs_match_the_slim_golden(
        string name,
        string[] arguments)
    {
        var workspace = CreateWorkspace();
        try
        {
            var result = await RunAsync(workspace, arguments);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                ReadFixture($"slim-{name}.toon"),
                Normalize(result.Output));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Default_home_output_matches_the_slim_golden()
    {
        var workspace = CreateWorkspace();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var host = CliApplication.Create(
                output,
                error,
                () => new HomeInvocationContext(
                    workspace,
                    Path.Combine(workspace, "dnaxi"),
                    workspace),
                static () => new WorkspaceDiscoverer(),
                static () => WorktreeStateInspector.CreatePassive(
                    new ProcessRunner()),
                static () => MissingCapabilityReporter.Instance);

            var exitCode = await host.InvokeAsync([]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(
                ReadFixture("slim-home.toon"),
                Normalize(output.ToString()));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Theory]
    [InlineData("home")]
    [InlineData("help")]
    [InlineData("file")]
    [InlineData("text")]
    [InlineData("syntax")]
    public void Representative_defaults_are_at_least_25_percent_smaller(
        string name)
    {
        var baselineBytes = Encoding.UTF8.GetByteCount(
            ReadFixture($"slim-{name}-0.4.toon"));
        var currentBytes = Encoding.UTF8.GetByteCount(
            ReadFixture($"slim-{name}.toon"));

        Assert.True(
            currentBytes * 4 <= baselineBytes * 3,
            $"Expected {name} to be at least 25% smaller, but it changed from {baselineBytes} to {currentBytes} UTF-8 bytes.");
    }

    public static TheoryData<string, string[]> CommandCases => new()
    {
        { "help", ["--help"] },
        { "file", ["search", "file", "Sample.cs"] },
        { "text", ["search", "text", "needle", "--path", "Sample.cs", "--full"] },
        { "syntax", ["search", "syntax", "invocation", "--name", "Hit", "--path", "Sample.cs", "--full"] },
    };

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "dnaxi-slim-golden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(
            Path.Combine(workspace, "Root.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        File.WriteAllText(
            Path.Combine(workspace, "Sample.cs"),
            "class Sample\n{\n    void Run()\n    {\n        Hit(); // needle\n    }\n}\n");
        return workspace;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = workingDirectory,
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

    private static string Normalize(string output)
    {
        var lines = output
            .Replace(
                $"dnaxi@{ToolVersion.Current}",
                "dnaxi@<tool-version>",
                StringComparison.Ordinal)
            .Replace(
                $"tool_version: {ToolVersion.Current}",
                "tool_version: <tool-version>",
                StringComparison.Ordinal)
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("  root: ", StringComparison.Ordinal))
            {
                lines[index] = "  root: <workspace>";
            }
        }

        return string.Join('\n', lines).TrimEnd('\r', '\n');
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            .TrimEnd('\r', '\n');

    private sealed class MissingCapabilityReporter : ICapabilityReporter
    {
        public static MissingCapabilityReporter Instance { get; } = new();

        public ValueTask<CapabilityReport> ReportAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
}
