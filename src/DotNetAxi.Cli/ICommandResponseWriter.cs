using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

public interface ICommandResponseWriter
{
    ValueTask<int> WriteAsync(
        ICommandResult result,
        CancellationToken cancellationToken = default);

    ValueTask<int> WriteAsync(
        ICommandResult result,
        CliExitCode exitCode,
        CancellationToken cancellationToken = default);
}
