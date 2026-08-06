using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.DotNet;

internal interface IContainedProcess : IDisposable
{
    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    ProcessExitEvidence ExitEvidence { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void TerminateTree();
}

internal interface IContainedProcessFactory
{
    IContainedProcess? Start(ProcessStartInfo startInfo);
}

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultCleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly IContainedProcessFactory _processFactory;
    private readonly TimeSpan _cleanupTimeout;

    public ProcessRunner()
        : this(new SystemContainedProcessFactory(), DefaultCleanupTimeout)
    {
    }

    internal ProcessRunner(IContainedProcessFactory processFactory)
        : this(processFactory, DefaultCleanupTimeout)
    {
    }

    internal ProcessRunner(
        IContainedProcessFactory processFactory,
        TimeSpan cleanupTimeout)
    {
        _processFactory = processFactory
            ?? throw new ArgumentNullException(nameof(processFactory));
        if (cleanupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cleanupTimeout));
        }

        _cleanupTimeout = cleanupTimeout;
    }

    public async ValueTask<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        var cancellationSignal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.UnsafeRegister(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationSignal);
        if (cancellationSignal.Task.IsCompleted)
        {
            return NotStarted(
                ProcessRunOutcome.Cancelled,
                ProcessStartFailure.None,
                stopwatch.Elapsed);
        }

        var workingDirectoryFailure = ClassifyWorkingDirectory(
            request.WorkingDirectory);
        if (cancellationSignal.Task.IsCompleted)
        {
            return NotStarted(
                ProcessRunOutcome.Cancelled,
                ProcessStartFailure.None,
                stopwatch.Elapsed);
        }

        if (workingDirectoryFailure is not null)
        {
            return NotStarted(
                ProcessRunOutcome.StartFailed,
                workingDirectoryFailure.Value,
                stopwatch.Elapsed);
        }

        IContainedProcess? process;
        try
        {
            process = _processFactory.Start(CreateStartInfo(request));
        }
        catch (Exception exception)
            when (exception is Win32Exception
                  or FileNotFoundException
                  or DirectoryNotFoundException
                  or UnauthorizedAccessException
                  or IOException
                  or InvalidOperationException)
        {
            if (cancellationSignal.Task.IsCompleted)
            {
                return NotStarted(
                    ProcessRunOutcome.Cancelled,
                    ProcessStartFailure.None,
                    stopwatch.Elapsed);
            }

            return NotStarted(
                ProcessRunOutcome.StartFailed,
                ClassifyStartFailure(exception, request),
                stopwatch.Elapsed);
        }

        if (process is null)
        {
            if (cancellationSignal.Task.IsCompleted)
            {
                return NotStarted(
                    ProcessRunOutcome.Cancelled,
                    ProcessStartFailure.None,
                    stopwatch.Elapsed);
            }

            return NotStarted(
                ProcessRunOutcome.StartFailed,
                ProcessStartFailure.Other,
                stopwatch.Elapsed);
        }

        try
        {
            return await RunStartedProcessAsync(
                    process,
                    request,
                    stopwatch,
                    cancellationSignal.Task)
                .ConfigureAwait(false);
        }
        finally
        {
            await DisposeWithinBoundAsync(process).ConfigureAwait(false);
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (request.EnvironmentPolicy is ProcessEnvironmentPolicy.Isolated)
        {
            startInfo.Environment.Clear();
        }

        foreach (var variable in request.Environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private async Task<ProcessRunResult> RunStartedProcessAsync(
        IContainedProcess process,
        ProcessRunRequest request,
        Stopwatch stopwatch,
        Task cancellationSignal)
    {
        using var stopCapture = new CancellationTokenSource();
        var overflow = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var standardOutput = new BoundedCapture(
            request.OutputLimits.StandardOutputCharacters);
        var standardError = new BoundedCapture(
            request.OutputLimits.StandardErrorCharacters);
        var standardOutputTask = StartCapture(
            standardOutput,
            () => process.StandardOutput,
            overflow,
            stopCapture.Token);
        var standardErrorTask = StartCapture(
            standardError,
            () => process.StandardError,
            overflow,
            stopCapture.Token);
        var processExit = StartExitWait(process);
        ObserveEventually(processExit);
        var completion = Task.WhenAll(
            processExit,
            standardOutputTask,
            standardErrorTask);
        var runtimeFailure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ObserveExpectedRuntimeFailure(standardOutputTask, runtimeFailure);
        ObserveExpectedRuntimeFailure(standardErrorTask, runtimeFailure);
        ObserveExpectedRuntimeFailure(processExit, runtimeFailure);
        var timeoutSignal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutTimer = new Timer(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            timeoutSignal,
            request.Timeout,
            Timeout.InfiniteTimeSpan);
        var terminal = cancellationSignal.IsCompleted
            ? cancellationSignal
            : await Task.WhenAny(
                    completion,
                    runtimeFailure.Task,
                    overflow.Task,
                    timeoutSignal.Task,
                    cancellationSignal)
                .ConfigureAwait(false);

        if (ReferenceEquals(terminal, runtimeFailure.Task))
        {
            return await CompleteTerminalAsync(
                    process,
                    ProcessRunOutcome.RuntimeFailed,
                    stopwatch,
                    standardOutput,
                    standardError,
                    stopCapture,
                    completion,
                    processExit)
                .ConfigureAwait(false);
        }

        if (ReferenceEquals(terminal, completion))
        {
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpectedRuntimeFailure(exception))
            {
                return await CompleteTerminalAsync(
                        process,
                        ProcessRunOutcome.RuntimeFailed,
                        stopwatch,
                        standardOutput,
                        standardError,
                        stopCapture,
                        completion,
                        processExit)
                    .ConfigureAwait(false);
            }

            var exit = TryGetExitEvidence(process, out var exitEvidenceFailed);
            if (exitEvidenceFailed || exit is null)
            {
                return new ProcessRunResult(
                    ProcessLifecycle.TerminationFailed,
                    ProcessRunOutcome.RuntimeFailed,
                    ProcessStartFailure.None,
                    exit: null,
                    standardOutput.Snapshot(),
                    standardError.Snapshot(),
                    stopwatch.Elapsed);
            }

            return new ProcessRunResult(
                ProcessLifecycle.Completed,
                overflow.Task.IsCompleted
                    ? ProcessRunOutcome.OutputLimitExceeded
                    : ProcessRunOutcome.Completed,
                ProcessStartFailure.None,
                exit,
                standardOutput.Snapshot(),
                standardError.Snapshot(),
                stopwatch.Elapsed);
        }

        var outcome = ReferenceEquals(terminal, overflow.Task)
            ? ProcessRunOutcome.OutputLimitExceeded
            : ReferenceEquals(terminal, timeoutSignal.Task)
                ? ProcessRunOutcome.TimedOut
                : ProcessRunOutcome.Cancelled;
        return await CompleteTerminalAsync(
                process,
                outcome,
                stopwatch,
                standardOutput,
                standardError,
                stopCapture,
                completion,
                processExit)
            .ConfigureAwait(false);
    }

    private async Task<ProcessRunResult> CompleteTerminalAsync(
        IContainedProcess process,
        ProcessRunOutcome outcome,
        Stopwatch stopwatch,
        BoundedCapture standardOutput,
        BoundedCapture standardError,
        CancellationTokenSource stopCapture,
        Task completion,
        Task processExit)
    {
        var completedBeforeCleanup = completion.IsCompletedSuccessfully;
        var exitedBeforeCleanup = processExit.IsCompletedSuccessfully;
        var terminationRequested = false;
        if (!completedBeforeCleanup && !exitedBeforeCleanup)
        {
            terminationRequested = TryTerminateTree(process);
        }

        var containmentConfirmed = completedBeforeCleanup
            || await ObserveWithinBoundAsync(completion).ConfigureAwait(false);
        stopCapture.Cancel();
        var processExitCompleted = processExit.IsCompleted;
        var processExitConfirmed = processExit.IsCompletedSuccessfully;
        var exitEvidenceFailed = false;
        var exit = processExitCompleted
            ? TryGetExitEvidence(process, out exitEvidenceFailed)
            : null;
        var lifecycle = !containmentConfirmed
            || !processExitConfirmed
            || exitEvidenceFailed
            || exit is null
            ? ProcessLifecycle.TerminationFailed
            : completedBeforeCleanup
                || exitedBeforeCleanup
                || !terminationRequested
                ? ProcessLifecycle.Completed
                : ProcessLifecycle.Terminated;
        return new ProcessRunResult(
            lifecycle,
            exitEvidenceFailed
                ? ProcessRunOutcome.RuntimeFailed
                : outcome,
            ProcessStartFailure.None,
            exit,
            standardOutput.Snapshot(),
            standardError.Snapshot(),
            stopwatch.Elapsed);
    }

    private static bool TryTerminateTree(IContainedProcess process)
    {
        try
        {
            process.TerminateTree();
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or Win32Exception
                  or IOException
                  or NotSupportedException)
        {
            // The bounded cleanup wait below determines whether containment
            // actually completed; a termination request alone is not evidence.
            return false;
        }
    }

    private static Task StartCapture(
        BoundedCapture capture,
        Func<TextReader> reader,
        TaskCompletionSource overflow,
        CancellationToken cancellationToken)
    {
        try
        {
            return capture.ReadAsync(reader(), overflow, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeFailure(exception))
        {
            return Task.FromException(exception);
        }
    }

    private static Task StartExitWait(IContainedProcess process)
    {
        try
        {
            return process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedRuntimeFailure(exception))
        {
            return Task.FromException(exception);
        }
    }

    private static ProcessExitEvidence? TryGetExitEvidence(
        IContainedProcess process,
        out bool failed)
    {
        try
        {
            failed = false;
            return process.ExitEvidence;
        }
        catch (Exception exception) when (IsExpectedRuntimeFailure(exception))
        {
            failed = true;
            return null;
        }
    }

    private static ProcessStartFailure ClassifyStartFailure(
        Exception exception,
        ProcessRunRequest request)
    {
        var workingDirectoryFailure = ClassifyWorkingDirectory(
            request.WorkingDirectory);
        if (workingDirectoryFailure is not null)
        {
            return workingDirectoryFailure.Value;
        }

        if (exception is FileNotFoundException
            or DirectoryNotFoundException
            || exception is Win32Exception { NativeErrorCode: 2 or 3 })
        {
            return File.Exists(request.ExecutablePath)
                ? ProcessStartFailure.Other
                : ProcessStartFailure.ExecutableNotFound;
        }

        if (exception is UnauthorizedAccessException)
        {
            return ProcessStartFailure.PermissionDenied;
        }

        if (exception is Win32Exception native)
        {
            if (OperatingSystem.IsWindows())
            {
                return native.NativeErrorCode == 5
                    ? ProcessStartFailure.PermissionDenied
                    : ProcessStartFailure.Other;
            }

            return native.NativeErrorCode is 1 or 13
                ? ProcessStartFailure.PermissionDenied
                : ProcessStartFailure.Other;
        }

        return ProcessStartFailure.Other;
    }

    private static ProcessStartFailure? ClassifyWorkingDirectory(
        string workingDirectory)
    {
        try
        {
            var attributes = File.GetAttributes(workingDirectory);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                return ProcessStartFailure.WorkingDirectoryNotFound;
            }

            if (!OperatingSystem.IsWindows())
            {
                return PosixProcessAuthority.GetEffectiveAccessError(
                    workingDirectory) switch
                {
                    0 => null,
                    1 or 13 =>
                        ProcessStartFailure.WorkingDirectoryPermissionDenied,
                    2 or 3 => ProcessStartFailure.WorkingDirectoryNotFound,
                    _ => null,
                };
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return ProcessStartFailure.WorkingDirectoryPermissionDenied;
        }
        catch (DirectoryNotFoundException)
        {
            return ProcessStartFailure.WorkingDirectoryNotFound;
        }
        catch (FileNotFoundException)
        {
            return ProcessStartFailure.WorkingDirectoryNotFound;
        }
        catch (IOException)
        {
            // Let the native launch classify a transient or host-specific
            // failure; this preflight must not convert it into an assertion.
            return null;
        }
    }

    private static ProcessRunResult NotStarted(
        ProcessRunOutcome outcome,
        ProcessStartFailure failure,
        TimeSpan duration) =>
        new(
            ProcessLifecycle.NotStarted,
            outcome,
            failure,
            exit: null,
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            duration);

    private async Task<bool> ObserveWithinBoundAsync(Task operation)
    {
        ObserveEventually(operation);
        try
        {
            await operation.WaitAsync(_cleanupTimeout).ConfigureAwait(false);
            return operation.IsCompletedSuccessfully;
        }
        catch (Exception exception)
            when (exception is TimeoutException
                  or OperationCanceledException
                  or Win32Exception
                  or IOException
                  or InvalidOperationException)
        {
            // The terminal lifecycle evidence is already fixed. The underlying
            // operation remains fault-observed without extending the bound.
            return false;
        }
    }

    private async Task DisposeWithinBoundAsync(IContainedProcess process)
    {
        var disposal = Task.Run(process.Dispose);
        ObserveEventually(disposal);
        try
        {
            await disposal.WaitAsync(_cleanupTimeout).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is TimeoutException
                  or InvalidOperationException
                  or Win32Exception
                  or IOException)
        {
            // Native containment owns the process tree. Disposal remains
            // fault-observed if a host API does not honor the terminal bound.
        }
    }

    private static void ObserveEventually(Task operation)
    {
        _ = operation.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
            | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static void ObserveExpectedRuntimeFailure(
        Task operation,
        TaskCompletionSource runtimeFailure)
    {
        _ = operation.ContinueWith(
            static (completed, state) =>
            {
                if (completed.IsFaulted
                    && IsExpectedRuntimeFailure(completed.Exception))
                {
                    ((TaskCompletionSource)state!).TrySetResult();
                }
            },
            runtimeFailure,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
            | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static bool IsExpectedRuntimeFailure(AggregateException? exception) =>
        exception is not null
        && exception.Flatten().InnerExceptions.All(static inner =>
            IsExpectedRuntimeFailure(inner));

    private static bool IsExpectedRuntimeFailure(Exception exception) =>
        exception is Win32Exception
        or IOException
        or InvalidOperationException
        or OperationCanceledException
        or UnauthorizedAccessException;

    private sealed class BoundedCapture(int maximumCharacters)
    {
        private readonly object _gate = new();
        private readonly StringBuilder _text = new(
            Math.Min(maximumCharacters, 4096));
        private bool _limitExceeded;

        public async Task ReadAsync(
            TextReader reader,
            TaskCompletionSource overflow,
            CancellationToken cancellationToken)
        {
            var buffer = new char[4096];
            try
            {
                while (true)
                {
                    var remaining = maximumCharacters - Length;
                    var requested = remaining <= 0
                        ? buffer.Length
                        : remaining >= buffer.Length
                            ? buffer.Length
                            : remaining + 1;
                    var count = await reader.ReadAsync(
                            buffer.AsMemory(0, requested),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (count == 0)
                    {
                        return;
                    }

                    lock (_gate)
                    {
                        var accepted = Math.Min(count, remaining);
                        if (accepted > 0)
                        {
                            _text.Append(buffer, 0, accepted);
                        }

                        if (count > remaining)
                        {
                            _limitExceeded = true;
                            overflow.TrySetResult();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        public ProcessCapturedOutput Snapshot()
        {
            lock (_gate)
            {
                return new ProcessCapturedOutput(
                    _text.ToString(),
                    limitExceeded: _limitExceeded);
            }
        }

        private int Length
        {
            get
            {
                lock (_gate)
                {
                    return _text.Length;
                }
            }
        }
    }
}
