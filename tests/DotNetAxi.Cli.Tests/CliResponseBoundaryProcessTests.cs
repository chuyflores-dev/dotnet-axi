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

    [Theory]
    [InlineData("--bogus", "--help", "--bogus")]
    [InlineData("--bogus", "success", "--help", "--bogus")]
    [InlineData("--bogus", "--version", "--bogus")]
    [InlineData("does-not-exist", "does-not-exist", "--help")]
    [InlineData("does-not-exist", "does-not-exist", "--version")]
    public async Task Terminating_options_do_not_suppress_invalid_input(
        string invalidInput,
        params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.StartsWith("schema: dotnet-axi/v1\n", result.StandardOutput);
        Assert.Contains("status: failed\n", result.StandardOutput);
        Assert.Contains("usage.", result.StandardOutput);
        Assert.Contains(invalidInput, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Theory]
    [InlineData("--help", "--version")]
    [InlineData("--version", "--help")]
    public async Task Terminating_options_cannot_be_combined(
        params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("status: failed\n", result.StandardOutput);
        Assert.Contains("usage.", result.StandardOutput);
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

    [Fact]
    public async Task Process_timeout_terminates_a_hanging_process()
    {
        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => RunApplicationAsync(
                TestApplicationPath(),
                TimeSpan.FromMilliseconds(250),
                "hang"));

        Assert.Contains("hang", exception.Message);
        Assert.Contains("250", exception.Message);
        Assert.Contains("process tree", exception.Message);
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

    [Theory]
    [InlineData("--version", "success")]
    [InlineData("success", "--version")]
    [InlineData("-v", "success")]
    [InlineData("success", "-v")]
    public async Task Version_option_must_be_a_standalone_invocation(
        params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("status: failed\n", result.StandardOutput);
        Assert.Contains("usage.", result.StandardOutput);
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
    public async Task Built_cli_reports_structured_passive_help()
    {
        var result = await RunApplicationAsync(
            ProductionApplicationPath(),
            "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith(
            "schema: dotnet-axi/v1\n",
            result.StandardOutput);
        Assert.Contains("command: help\n", result.StandardOutput);
        Assert.Contains("topic: home\n", result.StandardOutput);
        Assert.Contains(
            "classification: passive\n",
            result.StandardOutput);
        Assert.Contains("arguments: []\n", result.StandardOutput);
        Assert.Contains("subcommands: []\n", result.StandardOutput);
        Assert.Contains(
            "dnaxi --version",
            result.StandardOutput);
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

        Assert.Equal(0, verbosity.ExitCode);
        Assert.Contains("command: success\n", verbosity.StandardOutput);
        Assert.DoesNotContain("command: version\n", verbosity.StandardOutput);

        Assert.Equal(2, shortAlias.ExitCode);
        Assert.Contains("command: success\n", shortAlias.StandardOutput);
        Assert.Contains("status: failed\n", shortAlias.StandardOutput);
    }

    private static async Task<ProcessResult> RunAsync(params string[] args)
    {
        return await RunApplicationAsync(TestApplicationPath(), args);
    }

    private static async Task<ProcessResult> RunApplicationAsync(
        string application,
        params string[] args)
    {
        return await RunApplicationAsync(
            application,
            TimeSpan.FromSeconds(15),
            args);
    }

    private static async Task<ProcessResult> RunApplicationAsync(
        string application,
        TimeSpan timeoutDuration,
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
        using var timeout = new CancellationTokenSource(timeoutDuration);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException exception)
            when (timeout.IsCancellationRequested)
        {
            var termination = TryTerminateProcessTree(process);
            using var terminationTimeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(terminationTimeout.Token);
                termination = $"{termination}; process exited";
            }
            catch (OperationCanceledException)
                when (terminationTimeout.IsCancellationRequested)
            {
                termination = $"{termination}; process did not exit within 5 seconds";
            }

            var invocation = string.Join(
                " ",
                startInfo.ArgumentList.Select(static argument =>
                    argument.Contains(' ', StringComparison.Ordinal)
                        ? $"\"{argument}\""
                        : argument));
            throw new TimeoutException(
                $"Process `{startInfo.FileName} {invocation}` (PID {process.Id}) " +
                $"did not exit within {timeoutDuration.TotalMilliseconds:0} ms; " +
                $"process tree termination: {termination}.",
                exception);
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string TryTerminateProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            return "kill requested";
        }
        catch (InvalidOperationException exception)
        {
            return $"kill failed ({exception.GetType().Name}: {exception.Message})";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return $"kill failed ({exception.GetType().Name}: {exception.Message})";
        }
        catch (NotSupportedException exception)
        {
            return $"kill failed ({exception.GetType().Name}: {exception.Message})";
        }
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
