namespace DotNetAxi.Cli;

public sealed class CommandDiagnostics : ICommandDiagnostics
{
    private readonly TextWriter _error;

    public CommandDiagnostics(TextWriter error)
    {
        _error = error
            ?? throw new ArgumentNullException(nameof(error));
    }

    public async ValueTask WriteLineAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _error
            .WriteAsync($"{message}\n".AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await _error.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
