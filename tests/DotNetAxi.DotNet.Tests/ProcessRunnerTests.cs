using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.DotNet.Tests;

public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Arguments_environment_and_working_directory_are_exact_inputs()
    {
        var workingDirectory = CreateWorkingDirectory();
        var arguments = new[]
        {
            "; echo shell-was-used",
            "$(touch should-not-exist)",
            "%PATH% & exit 99",
            "space and \"quote\" and trailing\\",
            string.Empty,
        };
        var environmentValue = "literal;$HOME%PATH%&|<>";
        try
        {
            var result = await RunAsync(
                workingDirectory,
                ["echo", .. arguments],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["PROCESS_RUNNER_VALUE"] = environmentValue,
                });

            AssertCompleted(result, 0);
            var lines = result.StandardOutput.Text.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(
                NormalizeMacOsPrivatePath(workingDirectory),
                NormalizeMacOsPrivatePath(
                    Decode(Assert.Single(lines, line => line.StartsWith(
                        "cwd:",
                        StringComparison.Ordinal))[4..])));
            Assert.Equal(
                environmentValue,
                Decode(Assert.Single(lines, line => line.StartsWith(
                    "env:",
                    StringComparison.Ordinal))[4..]));
            Assert.Equal(
                "<null>",
                Decode(Assert.Single(lines, line => line.StartsWith(
                    "path:",
                    StringComparison.Ordinal))[5..]));
            Assert.Equal(
                arguments,
                lines.Where(static line => line.StartsWith(
                        "arg:",
                        StringComparison.Ordinal))
                    .Select(static line => Decode(line[4..])));
            Assert.False(File.Exists(
                Path.Combine(workingDirectory, "should-not-exist")));
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Inherited_environment_is_preserved_and_controlled_values_are_applied()
    {
        var parentPath = Environment.GetEnvironmentVariable("PATH");
        Assert.False(string.IsNullOrEmpty(parentPath));
        const string controlled = "literal;$HOME%PATH%&|<>";

        var result = await RunAsync(
            RepositoryRoot(),
            ["echo"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PROCESS_RUNNER_VALUE"] = controlled,
            },
            environmentPolicy: ProcessEnvironmentPolicy.InheritParent);

        AssertCompleted(result, 0);
        var lines = result.StandardOutput.Text.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            parentPath,
            Decode(Assert.Single(lines, line => line.StartsWith(
                "path:",
                StringComparison.Ordinal))[5..]));
        Assert.Equal(
            controlled,
            Decode(Assert.Single(lines, line => line.StartsWith(
                "env:",
                StringComparison.Ordinal))[4..]));
    }

    [Fact]
    public async Task Concurrent_stdout_and_stderr_pressure_is_drained()
    {
        const int characterCount = 256 * 1024;

        var result = await RunAsync(
            RepositoryRoot(),
            ["pressure", characterCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture)],
            outputLimits: new ProcessOutputLimits(
                characterCount,
                characterCount));

        AssertCompleted(result, 0);
        Assert.Equal(characterCount, result.StandardOutput.Text.Length);
        Assert.Equal(characterCount, result.StandardError.Text.Length);
        Assert.All(result.StandardOutput.Text, static value => Assert.Equal('o', value));
        Assert.All(result.StandardError.Text, static value => Assert.Equal('e', value));
        Assert.False(result.StandardOutput.LimitExceeded);
        Assert.False(result.StandardError.LimitExceeded);
    }

    [Fact]
    public async Task Output_overflow_is_bounded_and_terminates_the_process_tree()
    {
        var result = await RunAsync(
            RepositoryRoot(),
            ["pressure", "1000000"],
            outputLimits: new ProcessOutputLimits(257, 193));

        Assert.Equal(ProcessLifecycle.Terminated, result.Lifecycle);
        Assert.Equal(
            ProcessRunOutcome.OutputLimitExceeded,
            result.Outcome);
        Assert.InRange(result.StandardOutput.Text.Length, 0, 257);
        Assert.InRange(result.StandardError.Text.Length, 0, 193);
        Assert.True(
            result.StandardOutput.LimitExceeded
            || result.StandardError.LimitExceeded);
    }

    [Fact]
    public async Task Output_overflow_preserves_exit_evidence_when_process_already_completed()
    {
        var process = new CompletedContainedProcess("too-long", string.Empty, 0);
        var runner = new ProcessRunner(new StubProcessFactory(process));
        var request = new ProcessRunRequest(
            ProcessApplicationPath(),
            RepositoryRoot(),
            [],
            new Dictionary<string, string>(),
            new ProcessOutputLimits(1, 1),
            TestTimeout);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(ProcessLifecycle.Completed, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.OutputLimitExceeded, result.Outcome);
        Assert.Equal(ProcessStartFailure.None, result.StartFailure);
        Assert.Equal(0, result.Exit!.ExitCode);
        Assert.True(result.StandardOutput.LimitExceeded);
        Assert.Equal(1, result.StandardOutput.Text.Length);
    }

    [Fact]
    public async Task Maximum_output_limit_does_not_overflow_capture_arithmetic()
    {
        var process = new CompletedContainedProcess("ok", string.Empty, 0);
        var runner = new ProcessRunner(new StubProcessFactory(process));
        var request = new ProcessRunRequest(
            ProcessApplicationPath(),
            RepositoryRoot(),
            [],
            new Dictionary<string, string>(),
            new ProcessOutputLimits(int.MaxValue, int.MaxValue),
            TestTimeout);

        var result = await runner.RunAsync(request, CancellationToken.None);

        AssertCompleted(result, 0);
        Assert.Equal("ok", result.StandardOutput.Text);
    }

    [Fact]
    public async Task Timeout_terminates_a_hanging_process()
    {
        var result = await RunAsync(
            RepositoryRoot(),
            ["hang"],
            timeout: TimeSpan.FromMilliseconds(250));

        Assert.Equal(ProcessLifecycle.Terminated, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.TimedOut, result.Outcome);
        Assert.Equal(ProcessStartFailure.None, result.StartFailure);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancellation_is_distinct_from_timeout()
    {
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(250));

        var result = await RunAsync(
            RepositoryRoot(),
            ["hang"],
            timeout: TestTimeout,
            cancellationToken: cancellation.Token);

        Assert.Equal(ProcessLifecycle.Terminated, result.Lifecycle);
        Assert.Equal(
            ProcessRunOutcome.Cancelled,
            result.Outcome);
        Assert.Equal(ProcessStartFailure.None, result.StartFailure);
    }

    [Fact]
    public async Task Pre_cancelled_execution_never_starts_the_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var missingExecutable = Path.Combine(
            RepositoryRoot(),
            "does-not-exist",
            "process-app");
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessRunRequest(
                missingExecutable,
                RepositoryRoot(),
                [],
                new Dictionary<string, string>(),
                new ProcessOutputLimits(1, 1),
                TestTimeout),
            cancellation.Token);

        Assert.Equal(ProcessLifecycle.NotStarted, result.Lifecycle);
        Assert.Equal(
            ProcessRunOutcome.Cancelled,
            result.Outcome);
        Assert.Equal(ProcessStartFailure.None, result.StartFailure);
    }

    [Fact]
    public async Task Descendant_is_contained_when_the_root_exits()
    {
        var result = await RunAsync(
            RepositoryRoot(),
            ["spawn-descendant", "exit-root"],
            timeout: TimeSpan.FromSeconds(3));

        AssertCompleted(result, 0);
        var descendant = ParseDescendant(result.StandardOutput.Text);
        AssertProcessStopped(descendant);
    }

    [Fact]
    public async Task Timeout_terminates_descendants_and_releases_output_handles()
    {
        var result = await RunAsync(
            RepositoryRoot(),
            ["spawn-descendant", "hang-root"],
            timeout: TimeSpan.FromMilliseconds(500));

        Assert.Equal(ProcessLifecycle.Terminated, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.TimedOut, result.Outcome);
        var descendant = ParseDescendant(result.StandardOutput.Text);
        AssertProcessStopped(descendant);
    }

    [Fact]
    public async Task Dependency_exit_code_is_preserved_without_termination_evidence()
    {
        var result = await RunAsync(RepositoryRoot(), ["exit", "23"]);

        AssertCompleted(result, 23);
        Assert.Null(result.Exit!.Signal);
    }

    [Fact]
    public async Task Posix_signal_is_distinct_from_a_dependency_exit_code()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await RunAsync(RepositoryRoot(), ["signal", "15"]);

        Assert.Equal(ProcessLifecycle.Completed, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.Completed, result.Outcome);
        Assert.NotNull(result.Exit);
        Assert.Null(result.Exit.ExitCode);
        Assert.Equal(15, result.Exit.Signal);
    }

    [Fact]
    public void Posix_failed_reap_releases_termination_authority()
    {
        var terminationRequests = 0;
        var group = new PosixOwnedProcessGroup(
            123,
            waitForLeaderExitWithoutReaping: static _ => { },
            reapLeader: static _ => throw new Win32Exception(10),
            terminateGroup: _ => terminationRequests++,
            waitForGroupExit: static _ => { });

        Assert.Throws<Win32Exception>(group.WaitForExitAndContainDescendants);
        group.Terminate();

        Assert.Equal(1, terminationRequests);
    }

    [Fact]
    public void Posix_containment_fault_preserves_captured_exit_evidence()
    {
        var group = new PosixOwnedProcessGroup(
            123,
            waitForLeaderExitWithoutReaping: static _ => { },
            reapLeader: static _ => new ProcessExitEvidence(17, signal: null),
            terminateGroup: static _ => { },
            waitForGroupExit: static _ => throw new IOException("Expected failure."));

        Assert.Throws<IOException>(group.WaitForExitAndContainDescendants);

        Assert.Equal(17, group.ExitEvidence.ExitCode);
    }

    [Fact]
    public async Task Start_failure_is_distinct_from_dependency_failure()
    {
        var request = new ProcessRunRequest(
            Path.Combine(RepositoryRoot(), "missing-process-runner-app"),
            RepositoryRoot(),
            [],
            new Dictionary<string, string>(),
            new ProcessOutputLimits(100, 100),
            TestTimeout);

        var result = await new ProcessRunner().RunAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ProcessLifecycle.NotStarted, result.Lifecycle);
        Assert.Equal(
            ProcessStartFailure.ExecutableNotFound,
            result.StartFailure);
        Assert.Null(result.Exit);
    }

    [Fact]
    public async Task Missing_working_directory_is_distinct_from_a_missing_executable()
    {
        var request = new ProcessRunRequest(
            ProcessApplicationPath(),
            Path.Combine(RepositoryRoot(), "missing-working-directory"),
            [],
            new Dictionary<string, string>(),
            new ProcessOutputLimits(100, 100),
            TestTimeout);

        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

        Assert.Equal(ProcessLifecycle.NotStarted, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.StartFailed, result.Outcome);
        Assert.Equal(
            ProcessStartFailure.WorkingDirectoryNotFound,
            result.StartFailure);
    }

    [Fact]
    public async Task Inaccessible_posix_working_directory_is_identified_before_launch()
    {
        if (OperatingSystem.IsWindows()
            || PosixProcessAuthority.UsesSuperUserExecutionSemantics)
        {
            return;
        }

        var workingDirectory = CreateWorkingDirectory();
        var originalMode = File.GetUnixFileMode(workingDirectory);
        try
        {
            File.SetUnixFileMode(workingDirectory, UnixFileMode.None);
            var result = await new ProcessRunner().RunAsync(
                CreateRequest(workingDirectory: workingDirectory),
                CancellationToken.None);

            Assert.Equal(ProcessLifecycle.NotStarted, result.Lifecycle);
            Assert.Equal(ProcessRunOutcome.StartFailed, result.Outcome);
            Assert.Equal(
                ProcessStartFailure.WorkingDirectoryPermissionDenied,
                result.StartFailure);
        }
        finally
        {
            File.SetUnixFileMode(workingDirectory, originalMode);
            Directory.Delete(workingDirectory);
        }
    }

    [Fact]
    public async Task Posix_operation_not_permitted_is_a_permission_start_failure()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new ProcessRunner(
            new ThrowingProcessFactory(new Win32Exception(1)),
            TimeSpan.FromMilliseconds(50));
        var request = CreateRequest();

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(ProcessLifecycle.NotStarted, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.StartFailed, result.Outcome);
        Assert.Equal(ProcessStartFailure.PermissionDenied, result.StartFailure);
    }

    [Fact]
    public async Task Faulting_reader_returns_a_typed_failure_and_terminates_live_containment()
    {
        var process = new FaultingReaderContainedProcess();
        var runner = new ProcessRunner(
            new StubProcessFactory(process),
            TimeSpan.FromMilliseconds(50));

        var result = await runner.RunAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(ProcessLifecycle.TerminationFailed, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.RuntimeFailed, result.Outcome);
        Assert.Equal(17, result.Exit!.ExitCode);
        Assert.True(process.TerminationRequested);
    }

    [Fact]
    public async Task Faulting_exit_returns_a_typed_failure_without_claiming_termination()
    {
        var process = new FaultingExitContainedProcess();
        var runner = new ProcessRunner(
            new StubProcessFactory(process),
            TimeSpan.FromMilliseconds(50));

        var result = await runner.RunAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(ProcessLifecycle.TerminationFailed, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.RuntimeFailed, result.Outcome);
        Assert.Null(result.Exit);
        Assert.True(process.TerminationRequested);
    }

    [Fact]
    public async Task Containment_fault_preserves_available_exit_evidence()
    {
        var process = new FaultingContainmentWithExitEvidence();
        var runner = new ProcessRunner(
            new StubProcessFactory(process),
            TimeSpan.FromMilliseconds(50));

        var result = await runner.RunAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(ProcessLifecycle.TerminationFailed, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.RuntimeFailed, result.Outcome);
        Assert.Equal(19, result.Exit!.ExitCode);
        Assert.True(process.TerminationRequested);
    }

    [Fact]
    public async Task Terminate_request_without_exit_confirmation_is_termination_failed()
    {
        var process = new UncooperativeContainedProcess();
        var runner = new ProcessRunner(
            new StubProcessFactory(process),
            TimeSpan.FromMilliseconds(50));
        var request = CreateRequest(timeout: TimeSpan.FromMilliseconds(10));

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(ProcessLifecycle.TerminationFailed, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.TimedOut, result.Outcome);
        Assert.True(process.TerminationRequested);
    }

    [Fact]
    public void Request_copies_inputs_and_does_not_render_secret_bearing_values()
    {
        var arguments = new[] { "secret-argument" };
        var environment = new Dictionary<string, string>
        {
            ["TOKEN"] = "secret-value",
        };
        var request = new ProcessRunRequest(
            ProcessApplicationPath(),
            RepositoryRoot(),
            arguments,
            environment,
            new ProcessOutputLimits(1, 1),
            TestTimeout);

        arguments[0] = "changed";
        environment["TOKEN"] = "changed";

        Assert.Equal("secret-argument", Assert.Single(request.Arguments));
        Assert.Equal("secret-value", request.Environment["TOKEN"]);
        Assert.Equal(
            ProcessEnvironmentPolicy.Isolated,
            request.EnvironmentPolicy);
        Assert.Equal(nameof(ProcessRunRequest), request.ToString());
    }

    [Fact]
    public void Captured_output_and_result_do_not_render_captured_secrets()
    {
        const string secret = "secret-captured-output";
        var output = new ProcessCapturedOutput(secret, limitExceeded: false);
        var result = new ProcessRunResult(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.Completed,
            ProcessStartFailure.None,
            new ProcessExitEvidence(0, null),
            output,
            output,
            TimeSpan.Zero);

        Assert.DoesNotContain(secret, $"{output}", StringComparison.Ordinal);
        Assert.DoesNotContain(secret, $"{result}", StringComparison.Ordinal);
        Assert.Contains("Length = 22", $"{output}", StringComparison.Ordinal);
    }

    [Fact]
    public async Task First_cancellation_signal_is_not_replaced_by_later_completion()
    {
        var process = new ControllableContainedProcess();
        var runner = new ProcessRunner(
            new StubProcessFactory(process),
            TimeSpan.FromMilliseconds(50));
        using var cancellation = new CancellationTokenSource();
        var run = runner.RunAsync(CreateRequest(), cancellation.Token).AsTask();
        await process.WaitUntilObservedAsync();

        cancellation.Cancel();
        process.Complete();
        var result = await run;

        Assert.Equal(ProcessRunOutcome.Cancelled, result.Outcome);
        Assert.True(result.Lifecycle is ProcessLifecycle.Completed
            or ProcessLifecycle.Terminated);
        Assert.Equal(31, result.Exit!.ExitCode);
    }

    [Fact]
    public async Task Cancellation_during_start_is_latched_before_child_completion()
    {
        var process = new CompletedContainedProcess(string.Empty, string.Empty, 31);
        var factory = new BlockingProcessFactory(process);
        var runner = new ProcessRunner(factory, TimeSpan.FromMilliseconds(50));
        using var cancellation = new CancellationTokenSource();
        var run = Task.Run(async () => await runner.RunAsync(
            CreateRequest(),
            cancellation.Token));
        await factory.WaitUntilStartEnteredAsync();

        cancellation.Cancel();
        factory.ReleaseStart();
        var result = await run;

        Assert.Equal(ProcessRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(ProcessLifecycle.Completed, result.Lifecycle);
        Assert.Equal(31, result.Exit!.ExitCode);
    }

    [Fact]
    public void Public_process_evidence_rejects_contradictory_states()
    {
        var emptyOutput = new ProcessCapturedOutput(
            string.Empty,
            limitExceeded: false);
        var limitedOutput = new ProcessCapturedOutput(
            "bounded",
            limitExceeded: true);

        Assert.Throws<ArgumentException>(
            () => new ProcessExitEvidence(exitCode: null, signal: null));
        Assert.Throws<ArgumentException>(
            () => new ProcessExitEvidence(exitCode: 0, signal: 9));
        Assert.Throws<ArgumentException>(() => new ProcessRunResult(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.Completed,
            ProcessStartFailure.None,
            exit: null,
            emptyOutput,
            emptyOutput,
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessRunRequest(
            ProcessApplicationPath(),
            RepositoryRoot(),
            [],
            new Dictionary<string, string>(),
            new ProcessOutputLimits(1, 1),
            TestTimeout,
            (ProcessEnvironmentPolicy)int.MaxValue));
        Assert.Throws<ArgumentException>(() => new ProcessRunResult(
            ProcessLifecycle.NotStarted,
            ProcessRunOutcome.StartFailed,
            ProcessStartFailure.None,
            exit: null,
            emptyOutput,
            emptyOutput,
            TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new ProcessRunResult(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.Completed,
            ProcessStartFailure.None,
            new ProcessExitEvidence(0, signal: null),
            limitedOutput,
            emptyOutput,
            TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new ProcessRunResult(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.OutputLimitExceeded,
            ProcessStartFailure.None,
            new ProcessExitEvidence(0, signal: null),
            emptyOutput,
            emptyOutput,
            TimeSpan.Zero));
    }

    private static async ValueTask<ProcessRunResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        ProcessOutputLimits? outputLimits = null,
        TimeSpan? timeout = null,
        ProcessEnvironmentPolicy environmentPolicy =
            ProcessEnvironmentPolicy.Isolated,
        CancellationToken cancellationToken = default)
    {
        var request = new ProcessRunRequest(
            ProcessApplicationPath(),
            workingDirectory,
            arguments,
            environment ?? new Dictionary<string, string>(),
            outputLimits ?? new ProcessOutputLimits(1024 * 1024, 1024 * 1024),
            timeout ?? TestTimeout,
            environmentPolicy);
        return await new ProcessRunner().RunAsync(request, cancellationToken);
    }

    private static ProcessRunRequest CreateRequest(
        TimeSpan? timeout = null,
        string? workingDirectory = null) =>
        new(
            ProcessApplicationPath(),
            workingDirectory ?? RepositoryRoot(),
            [],
            new Dictionary<string, string>(),
            new ProcessOutputLimits(100, 100),
            timeout ?? TestTimeout);

    private static void AssertCompleted(ProcessRunResult result, int exitCode)
    {
        Assert.Equal(ProcessLifecycle.Completed, result.Lifecycle);
        Assert.Equal(ProcessRunOutcome.Completed, result.Outcome);
        Assert.Equal(ProcessStartFailure.None, result.StartFailure);
        Assert.NotNull(result.Exit);
        Assert.Equal(exitCode, result.Exit.ExitCode);
    }

    private static (int ProcessId, long StartTimeTicks) ParseDescendant(
        string output)
    {
        var line = Assert.Single(
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var fields = line.Split(':');
        Assert.Equal(3, fields.Length);
        Assert.Equal("descendant", fields[0]);
        return (
            int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture),
            long.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AssertProcessStopped(
        (int ProcessId, long StartTimeTicks) expected)
    {
        try
        {
            using var process = Process.GetProcessById(expected.ProcessId);
            if (process.StartTime.ToUniversalTime().Ticks
                == expected.StartTimeTicks)
            {
                Assert.True(
                    process.HasExited,
                    $"Descendant PID {expected.ProcessId} was still alive when the runner returned.");
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private static string Decode(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static string NormalizeMacOsPrivatePath(string path) =>
        OperatingSystem.IsMacOS()
        && path.StartsWith("/private/", StringComparison.Ordinal)
            ? path[8..]
            : path;

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-process-runner-tests",
            $"hostile ;$()&-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ProcessApplicationPath()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var executable = OperatingSystem.IsWindows()
            ? "DotNetAxi.DotNet.ProcessTestApp.exe"
            : "DotNetAxi.DotNet.ProcessTestApp";
        var path = Path.Combine(
            RepositoryRoot(),
            "tests",
            "DotNetAxi.DotNet.ProcessTestApp",
            "bin",
            configuration,
            "net10.0",
            executable);
        Assert.True(File.Exists(path), $"Process test app not found at '{path}'.");
        return path;
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    private sealed class StubProcessFactory(IContainedProcess process)
        : IContainedProcessFactory
    {
        public IContainedProcess Start(ProcessStartInfo startInfo) => process;
    }

    private sealed class ThrowingProcessFactory(Exception exception)
        : IContainedProcessFactory
    {
        public IContainedProcess Start(ProcessStartInfo startInfo) => throw exception;
    }

    private sealed class BlockingProcessFactory(IContainedProcess process)
        : IContainedProcessFactory
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public IContainedProcess Start(ProcessStartInfo startInfo)
        {
            _entered.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
            return process;
        }

        public Task WaitUntilStartEnteredAsync() => _entered.Task;

        public void ReleaseStart() => _release.TrySetResult();
    }

    private sealed class CompletedContainedProcess(
        string standardOutput,
        string standardError,
        int exitCode) : IContainedProcess
    {
        public TextReader StandardOutput { get; } = new StringReader(standardOutput);

        public TextReader StandardError { get; } = new StringReader(standardError);

        public ProcessExitEvidence ExitEvidence { get; } = new(exitCode, signal: null);

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void TerminateTree() =>
            Assert.Fail("A completed process must not be terminated.");

        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private sealed class FaultingReaderContainedProcess : IContainedProcess
    {
        private readonly TaskCompletionSource _exit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TextReader StandardOutput { get; } = new FaultingTextReader();

        public TextReader StandardError { get; } = TextReader.Null;

        public ProcessExitEvidence ExitEvidence { get; } = new(17, null);

        public bool TerminationRequested { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task;

        public void TerminateTree()
        {
            TerminationRequested = true;
            _exit.TrySetResult();
        }

        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private sealed class FaultingExitContainedProcess : IContainedProcess
    {
        public TextReader StandardOutput { get; } = TextReader.Null;

        public TextReader StandardError { get; } = TextReader.Null;

        public ProcessExitEvidence ExitEvidence => throw new Win32Exception(5);

        public bool TerminationRequested { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.FromException(new Win32Exception(5));

        public void TerminateTree() => TerminationRequested = true;

        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private sealed class FaultingContainmentWithExitEvidence : IContainedProcess
    {
        public TextReader StandardOutput { get; } = TextReader.Null;

        public TextReader StandardError { get; } = TextReader.Null;

        public ProcessExitEvidence ExitEvidence { get; } = new(19, signal: null);

        public bool TerminationRequested { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Expected containment failure."));

        public void TerminateTree() => TerminationRequested = true;

        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private sealed class UncooperativeContainedProcess : IContainedProcess
    {
        private readonly TaskCompletionSource _exit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TextReader StandardOutput { get; } = TextReader.Null;

        public TextReader StandardError { get; } = TextReader.Null;

        public ProcessExitEvidence ExitEvidence => throw new InvalidOperationException();

        public bool TerminationRequested { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task;

        public void TerminateTree() => TerminationRequested = true;

        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private sealed class ControllableContainedProcess : IContainedProcess
    {
        private readonly TaskCompletionSource _observed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TextReader StandardOutput { get; } = TextReader.Null;

        public TextReader StandardError { get; } = TextReader.Null;

        public ProcessExitEvidence ExitEvidence { get; } = new(31, null);

        public bool TerminationRequested { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            _observed.TrySetResult();
            return _exit.Task;
        }

        public Task WaitUntilObservedAsync() => _observed.Task;

        public void Complete() => _exit.TrySetResult();

        public void TerminateTree()
        {
            TerminationRequested = true;
            _exit.TrySetResult();
        }

        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private sealed class FaultingTextReader : TextReader
    {
        public override ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Expected test failure."));
    }
}
