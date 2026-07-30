namespace DotNetAxi.Cli;

public interface ICommandDiagnostics
{
    ValueTask WriteLineAsync(
        string message,
        CancellationToken cancellationToken = default);
}
