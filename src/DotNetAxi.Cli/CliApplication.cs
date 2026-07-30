using System.CommandLine;

namespace DotNetAxi.Cli;

internal static class CliApplication
{
    public static CommandHost Create(TextWriter output, TextWriter error)
    {
        var rootCommand = new RootCommand(
            "Deterministic .NET discovery, analysis, validation, and safe modification.");
        rootCommand.BindHandler(
            static _ => RootInvocation.Instance,
            static () => RootHandler.Instance);

        return new CommandHost(rootCommand, output, error);
    }

    private sealed record RootInvocation
    {
        public static RootInvocation Instance { get; } = new();
    }

    private sealed class RootHandler : ICommandHandler<RootInvocation>
    {
        public static RootHandler Instance { get; } = new();

        public ValueTask<int> HandleAsync(
            RootInvocation request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
    }
}
