using System.Collections.ObjectModel;

namespace DotNetAxi.Contracts;

public enum ProcessLifecycle
{
    NotStarted,
    Completed,
    Terminated,
    TerminationFailed,
}

public enum ProcessRunOutcome
{
    Completed,
    StartFailed,
    RuntimeFailed,
    Cancelled,
    TimedOut,
    OutputLimitExceeded,
}

public enum ProcessStartFailure
{
    None,
    ExecutableNotFound,
    WorkingDirectoryNotFound,
    WorkingDirectoryPermissionDenied,
    PermissionDenied,
    Other,
    PolicyDenied,
}

public sealed record ProcessExitEvidence
{
    public ProcessExitEvidence(int? exitCode, int? signal)
    {
        if ((exitCode is null) == (signal is null))
        {
            throw new ArgumentException(
                "Process exit evidence must contain exactly one exit code or signal.");
        }

        if (signal is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(signal),
                signal,
                "A process signal must be positive.");
        }

        ExitCode = exitCode;
        Signal = signal;
    }

    public int? ExitCode { get; }

    public int? Signal { get; }
}

public sealed record ProcessCapturedOutput
{
    public ProcessCapturedOutput(string text, bool limitExceeded)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        LimitExceeded = limitExceeded;
    }

    public string Text { get; }

    public bool LimitExceeded { get; }

    public override string ToString() =>
        $"{nameof(ProcessCapturedOutput)} {{ Length = {Text.Length}, LimitExceeded = {LimitExceeded} }}";
}

public sealed record ProcessRunResult
{
    public ProcessRunResult(
        ProcessLifecycle lifecycle,
        ProcessRunOutcome outcome,
        ProcessStartFailure startFailure,
        ProcessExitEvidence? exit,
        ProcessCapturedOutput standardOutput,
        ProcessCapturedOutput standardError,
        TimeSpan duration)
    {
        if (!Enum.IsDefined(lifecycle))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycle));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (!Enum.IsDefined(startFailure))
        {
            throw new ArgumentOutOfRangeException(nameof(startFailure));
        }

        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        ValidateState(
            lifecycle,
            outcome,
            startFailure,
            exit,
            standardOutput,
            standardError);
        Lifecycle = lifecycle;
        Outcome = outcome;
        StartFailure = startFailure;
        Exit = exit;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Duration = duration;
    }

    public ProcessLifecycle Lifecycle { get; }

    public ProcessRunOutcome Outcome { get; }

    public ProcessStartFailure StartFailure { get; }

    public ProcessExitEvidence? Exit { get; }

    public ProcessCapturedOutput StandardOutput { get; }

    public ProcessCapturedOutput StandardError { get; }

    public TimeSpan Duration { get; }

    public override string ToString() =>
        $"{nameof(ProcessRunResult)} {{ Lifecycle = {Lifecycle}, Outcome = {Outcome}, "
        + $"StartFailure = {StartFailure}, HasExitEvidence = {Exit is not null}, "
        + $"StandardOutput = {StandardOutput}, StandardError = {StandardError}, "
        + $"Duration = {Duration} }}";

    private static void ValidateState(
        ProcessLifecycle lifecycle,
        ProcessRunOutcome outcome,
        ProcessStartFailure startFailure,
        ProcessExitEvidence? exit,
        ProcessCapturedOutput standardOutput,
        ProcessCapturedOutput standardError)
    {
        var startFailed = outcome is ProcessRunOutcome.StartFailed;
        if (startFailed != (startFailure is not ProcessStartFailure.None))
        {
            throw new ArgumentException(
                "Start-failure evidence must be present only for a start-failed outcome.");
        }

        if (lifecycle is ProcessLifecycle.NotStarted)
        {
            if (outcome is not (ProcessRunOutcome.StartFailed
                or ProcessRunOutcome.Cancelled)
                || exit is not null
                || standardOutput.Text.Length != 0
                || standardError.Text.Length != 0
                || standardOutput.LimitExceeded
                || standardError.LimitExceeded)
            {
                throw new ArgumentException(
                    "A not-started process cannot contain execution or output evidence.");
            }

            return;
        }

        if (startFailed)
        {
            throw new ArgumentException(
                "A start-failed outcome requires the not-started lifecycle.");
        }

        if (outcome is ProcessRunOutcome.Completed
            && lifecycle is not ProcessLifecycle.Completed)
        {
            throw new ArgumentException(
                "A completed outcome requires the completed lifecycle.");
        }

        var outputLimitExceeded = standardOutput.LimitExceeded
            || standardError.LimitExceeded;
        if ((outcome is ProcessRunOutcome.OutputLimitExceeded
                && !outputLimitExceeded)
            || (outcome is ProcessRunOutcome.Completed
                && outputLimitExceeded))
        {
            throw new ArgumentException(
                "Output-limit evidence must agree with a completed or output-limited outcome.");
        }

        if (lifecycle is ProcessLifecycle.Completed
            or ProcessLifecycle.Terminated
            && exit is null)
        {
            throw new ArgumentException(
                "A completed or terminated lifecycle requires exit evidence.");
        }
    }
}

public sealed record ProcessOutputLimits
{
    public ProcessOutputLimits(
        int standardOutputCharacters,
        int standardErrorCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            standardOutputCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(
            standardErrorCharacters);
        StandardOutputCharacters = standardOutputCharacters;
        StandardErrorCharacters = standardErrorCharacters;
    }

    public int StandardOutputCharacters { get; }

    public int StandardErrorCharacters { get; }
}

public enum ProcessEnvironmentPolicy
{
    Isolated,
    InheritParent,
}

public sealed class ProcessRunRequest
{
    public ProcessRunRequest(
        string executablePath,
        string workingDirectory,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        ProcessOutputLimits outputLimits,
        TimeSpan timeout,
        ProcessEnvironmentPolicy environmentPolicy =
            ProcessEnvironmentPolicy.Isolated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(outputLimits);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "The executable path must be fully qualified.",
                nameof(executablePath));
        }

        if (!Path.IsPathFullyQualified(workingDirectory))
        {
            throw new ArgumentException(
                "The working directory must be fully qualified.",
                nameof(workingDirectory));
        }

        if (timeout <= TimeSpan.Zero
            || timeout.TotalMilliseconds > uint.MaxValue - 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The process timeout must be positive and finite.");
        }

        if (!Enum.IsDefined(environmentPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(environmentPolicy));
        }

        ExecutablePath = Path.GetFullPath(executablePath);
        WorkingDirectory = Path.GetFullPath(workingDirectory);
        Arguments = CopyArguments(arguments);
        Environment = CopyEnvironment(environment);
        OutputLimits = outputLimits;
        Timeout = timeout;
        EnvironmentPolicy = environmentPolicy;
    }

    public string ExecutablePath { get; }

    public string WorkingDirectory { get; }

    public IReadOnlyList<string> Arguments { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public ProcessOutputLimits OutputLimits { get; }

    public TimeSpan Timeout { get; }

    public ProcessEnvironmentPolicy EnvironmentPolicy { get; }

    public override string ToString() => nameof(ProcessRunRequest);

    private static IReadOnlyList<string> CopyArguments(
        IEnumerable<string> arguments)
    {
        var copy = arguments.Select((argument, index) =>
        {
            if (argument is null)
            {
                throw new ArgumentException(
                    $"Process argument {index} is null.",
                    nameof(arguments));
            }

            if (argument.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Process argument {index} contains a null character.",
                    nameof(arguments));
            }

            return argument;
        }).ToArray();
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyDictionary<string, string> CopyEnvironment(
        IReadOnlyDictionary<string, string> environment)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var copy = new Dictionary<string, string>(comparer);
        foreach (var variable in environment)
        {
            if (string.IsNullOrEmpty(variable.Key)
                || variable.Key.Contains('=', StringComparison.Ordinal)
                || variable.Key.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Environment variable names must be non-empty and cannot contain '=' or null characters.",
                    nameof(environment));
            }

            if (variable.Value is null
                || variable.Value.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Environment variable values cannot be null or contain null characters.",
                    nameof(environment));
            }

            if (!copy.TryAdd(variable.Key, variable.Value))
            {
                throw new ArgumentException(
                    $"Environment variable '{variable.Key}' is duplicated.",
                    nameof(environment));
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}

public interface IProcessRunner
{
    ValueTask<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken);
}
