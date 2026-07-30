using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class CliApplication
{
    public static CommandHost Create(TextWriter output, TextWriter error)
    {
        var rootCommand = new RootCommand(
            "Deterministic .NET discovery, analysis, validation, and safe modification.");
        var host = new CommandHost(rootCommand, output, error);
        rootCommand.BindVersionOutput(
            static () => VersionResult.Create(ToolVersion.Current),
            host.ResponseWriter);
        rootCommand.BindHandler(
            static _ => RootInvocation.Instance,
            static () => RootHandler.Instance,
            host.ResponseWriter);

        return host;
    }

    private sealed record RootInvocation
    {
        public static RootInvocation Instance { get; } = new();
    }

    private sealed class RootHandler : ICommandHandler<RootInvocation>
    {
        public static RootHandler Instance { get; } = new();

        public ValueTask<ICommandResult> HandleAsync(
            RootInvocation request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICommandResult>(
                CommandResult<RootPayload>.Success(
                    "home",
                    RootPayload.Instance));
        }
    }

    private sealed record RootPayload
    {
        public static RootPayload Instance { get; } = new();
    }
}
