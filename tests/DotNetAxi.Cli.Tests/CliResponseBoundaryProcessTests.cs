using System.Diagnostics;

namespace DotNetAxi.Cli.Tests;

public sealed class CliResponseBoundaryProcessTests
{
    [Theory]
    [InlineData("success", "success", 0)]
    [InlineData("empty", "success", 0)]
    [InlineData("partial", "partial", 0)]
    [InlineData("failed", "failed", 1)]
    [InlineData("cancelled", "cancelled", 1)]
    public async Task Result_status_maps_to_structured_stdout_and_public_exit_code(
        string command,
        string status,
        int expectedExitCode)
    {
        var result = await RunAsync(command);

        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.StartsWith("schema: dotnet-axi/v1\n", result.StandardOutput);
        Assert.Contains($"command: {command}\n", result.StandardOutput);
        Assert.Contains($"status: {status}\n", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task Empty_result_retains_numeric_count_and_empty_collection()
    {
        var result = await RunAsync("empty");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("count: 0\n", result.StandardOutput);
        Assert.Contains("items: []", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task Usage_error_is_structured_and_does_not_invoke_the_handler()
    {
        var result = await RunAsync("success", "--unknown");

        Assert.Equal(2, result.ExitCode);
        Assert.StartsWith("schema: dotnet-axi/v1\n", result.StandardOutput);
        Assert.Contains("command: success\n", result.StandardOutput);
        Assert.Contains("status: failed\n", result.StandardOutput);
        Assert.Contains("usage.unknown_flag", result.StandardOutput);
        Assert.Contains("--unknown", result.StandardOutput);
        Assert.Contains("--known", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task Diagnostics_are_written_only_to_standard_error()
    {
        var result = await RunAsync("diagnostic");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("progress: fixture", result.StandardOutput);
        Assert.Equal("progress: fixture\n", result.StandardError);
    }

    [Fact]
    public async Task Unhandled_exception_is_replaced_by_a_stable_error()
    {
        var result = await RunAsync("throw");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("command: throw\n", result.StandardOutput);
        Assert.Contains("status: failed\n", result.StandardOutput);
        Assert.Contains("internal.unhandled", result.StandardOutput);
        Assert.DoesNotContain("sensitive-stack-marker", result.StandardOutput);
        Assert.DoesNotContain("sensitive-stack-marker", result.StandardError);
        Assert.DoesNotContain("System.InvalidOperationException", result.StandardError);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("--version")]
    public async Task Version_aliases_return_the_substituted_package_version(
        string option)
    {
        var result = await RunAsync(option);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("schema: dotnet-axi/v1\n", result.StandardOutput);
        Assert.Contains("command: version\n", result.StandardOutput);
        Assert.Contains("status: success\n", result.StandardOutput);
        Assert.Contains("tool: dotnet-axi\n", result.StandardOutput);
        Assert.Contains("tool_version: 9.8.7-test\n", result.StandardOutput);
        Assert.Contains("output_schema: dotnet-axi/v1", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task Built_cli_reports_its_embedded_package_version()
    {
        var result = await RunApplicationAsync(
            ProductionApplicationPath(),
            "--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: version\n", result.StandardOutput);
        Assert.Contains("tool_version: 0.1.0-alpha.1\n", result.StandardOutput);
        Assert.Contains("output_schema: dotnet-axi/v1", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task Short_version_alias_does_not_replace_subcommand_verbosity()
    {
        var verbosity = await RunAsync(
            "success",
            "--verbosity",
            "detailed");
        var shortAlias = await RunAsync("success", "-v");
        var combinedVersion = await RunAsync("-v", "success");

        Assert.Equal(0, verbosity.ExitCode);
        Assert.Contains("command: success\n", verbosity.StandardOutput);
        Assert.DoesNotContain("command: version\n", verbosity.StandardOutput);

        Assert.Equal(2, shortAlias.ExitCode);
        Assert.Contains("command: success\n", shortAlias.StandardOutput);
        Assert.Contains("status: failed\n", shortAlias.StandardOutput);

        Assert.Equal(2, combinedVersion.ExitCode);
        Assert.Contains("status: failed\n", combinedVersion.StandardOutput);
    }

    private static async Task<ProcessResult> RunAsync(params string[] args)
    {
        return await RunApplicationAsync(TestApplicationPath(), args);
    }

    private static async Task<ProcessResult> RunApplicationAsync(
        string application,
        params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(application);
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start());

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string TestApplicationPath()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var application = Path.Combine(
            RepositoryRoot(),
            "tests",
            "DotNetAxi.Cli.TestApp",
            "bin",
            configuration,
            "net10.0",
            "DotNetAxi.Cli.TestApp.dll");

        Assert.True(
            File.Exists(application),
            $"The response-boundary test application was not found at '{application}'.");
        return application;
    }

    private static string ProductionApplicationPath()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var application = Path.Combine(
            RepositoryRoot(),
            "src",
            "DotNetAxi.Cli",
            "bin",
            configuration,
            "net10.0",
            "dnaxi.dll");

        Assert.True(
            File.Exists(application),
            $"The CLI application was not found at '{application}'.");
        return application;
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                ".."));

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
