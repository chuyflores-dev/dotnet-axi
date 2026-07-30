namespace DotNetAxi.Cli;

public interface ICommandHandler<in TRequest>
{
    ValueTask<int> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken);
}
