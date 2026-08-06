using DotNetAxi.Contracts;
using DotNetAxi.DotNet;

namespace DotNetAxi.Cli;

internal sealed class PassiveProcessRunner : IProcessRunner
{
    private static readonly ProcessCapturedOutput EmptyOutput =
        new(string.Empty, limitExceeded: false);

    public static PassiveProcessRunner Instance { get; } = new();

    private PassiveProcessRunner()
    {
    }

    public ValueTask<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new ProcessRunResult(
                ProcessLifecycle.NotStarted,
                ProcessRunOutcome.StartFailed,
                ProcessStartFailure.PolicyDenied,
                exit: null,
                EmptyOutput,
                EmptyOutput,
                TimeSpan.Zero));
    }
}

internal static class PassiveCapabilityReporterFactory
{
    public static ICapabilityReporter Create() => new CapabilityReporter(
        new DotNetHostResolver(PassiveProcessRunner.Instance),
        new ExternalVersionProbe(PassiveProcessRunner.Instance),
        new AssemblyVersionProbe());
}
