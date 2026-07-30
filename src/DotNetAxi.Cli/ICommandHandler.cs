using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

public interface ICommandHandler<in TRequest>
{
    ValueTask<ICommandResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken);
}
