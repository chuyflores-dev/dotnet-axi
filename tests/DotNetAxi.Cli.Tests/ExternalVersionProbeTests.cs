using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class ExternalVersionProbeTests
{
    [Theory]
    [InlineData("git", "git version 2.50.1.windows.1\n", "2.50.1.windows.1")]
    [InlineData("rg", "ripgrep 15.2.0\n-SIMD -AVX\n", "15.2.0")]
    public async Task Probe_runs_only_the_bounded_version_command(
        string capabilityName,
        string output,
        string expectedVersion)
    {
        var capability = capabilityName == "git"
            ? ExternalCapability.Git
            : ExternalCapability.Ripgrep;
        var tools = Path.GetFullPath("controlled-tools");
        var command = capability is ExternalCapability.Git ? "git" : "rg";
        var executable = Path.Combine(
            tools,
            OperatingSystem.IsWindows() ? $"{command}.exe" : command);
        var runner = new RecordingProcessRunner(Completed(output));
        var probe = new ExternalVersionProbe(
            runner,
            () => tools,
            path => path == executable,
            _ => true);

        var result = await probe.ProbeAsync(
            capability,
            Path.GetFullPath("controlled-workspace"));

        Assert.Equal(executable, result.ExecutablePath);
        Assert.Equal(expectedVersion, result.Version);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(["--version"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(2), request.Timeout);
        Assert.Equal(4 * 1024, request.OutputLimits.StandardOutputCharacters);
        Assert.Equal("C", request.Environment["LC_ALL"]);
        Assert.Equal(
            ProcessEnvironmentPolicy.Isolated,
            request.EnvironmentPolicy);
        if (capability is ExternalCapability.Ripgrep)
        {
            Assert.Equal("1", request.Environment["NO_COLOR"]);
            Assert.Equal(
                ChildProcessEnvironment.RipgrepDefaults.Count,
                request.Environment.Count);
            Assert.False(request.Environment.ContainsKey("PATH"));
        }

        Assert.DoesNotContain(
            request.Arguments,
            static argument => argument.Contains(
                "install",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_executable_does_not_start_a_process()
    {
        var runner = new RecordingProcessRunner();
        var probe = new ExternalVersionProbe(
            runner,
            static () => null,
            static _ => false,
            static _ => false);

        var result = await probe.ProbeAsync(
            ExternalCapability.Ripgrep,
            Path.GetFullPath("controlled-workspace"));

        Assert.Equal(CapabilityAvailability.Missing, result.Availability);
        Assert.Null(result.ExecutablePath);
        Assert.Null(result.Version);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Failed_version_command_remains_present_but_unverified()
    {
        var tools = Path.GetFullPath("controlled-tools");
        var executable = Path.Combine(
            tools,
            OperatingSystem.IsWindows() ? "rg.exe" : "rg");
        var runner = new RecordingProcessRunner(
            Completed("raw dependency output", exitCode: 2));
        var probe = new ExternalVersionProbe(
            runner,
            () => tools,
            path => path == executable,
            _ => true);

        var result = await probe.ProbeAsync(
            ExternalCapability.Ripgrep,
            Path.GetFullPath("controlled-workspace"));

        Assert.Equal(CapabilityAvailability.Present, result.Availability);
        Assert.Null(result.Version);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Workspace_local_executable_is_reported_without_execution()
    {
        var workspace = Path.GetFullPath("controlled-workspace");
        var tools = Path.Combine(workspace, "tools");
        var executable = Path.Combine(
            tools,
            OperatingSystem.IsWindows() ? "rg.exe" : "rg");
        var runner = new RecordingProcessRunner();
        var probe = new ExternalVersionProbe(
            runner,
            () => tools,
            path => path == executable,
            _ => true);

        var result = await probe.ProbeAsync(
            ExternalCapability.Ripgrep,
            workspace);

        Assert.Equal(CapabilityAvailability.Present, result.Availability);
        Assert.Null(result.Version);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Path_resolution_skips_relative_and_non_executable_entries()
    {
        var blockedDirectory = Path.GetFullPath("blocked-tools");
        var usableDirectory = Path.GetFullPath("usable-tools");
        var blocked = Path.Combine(blockedDirectory, "git");
        var usable = Path.Combine(usableDirectory, "git");

        var result = ExternalVersionProbe.ResolveExecutablePath(
            "git",
            string.Join(
                Path.PathSeparator,
                "relative-tools",
                blockedDirectory,
                usableDirectory),
            isWindows: false,
            path => path == blocked || path == usable,
            path => path == usable);

        Assert.Equal(usable, result);
    }

    private static ProcessRunResult Completed(
        string output,
        int exitCode = 0) => new(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.Completed,
            ProcessStartFailure.None,
            new ProcessExitEvidence(exitCode, null),
            new ProcessCapturedOutput(output, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            TimeSpan.Zero);

    private sealed class RecordingProcessRunner(
        params ProcessRunResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _results = new(results);

        public List<ProcessRunRequest> Requests { get; } = [];

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(_results.Dequeue());
        }
    }
}
