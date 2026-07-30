using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

public sealed class CommandResponseWriter : ICommandResponseWriter
{
    private readonly TextWriter _output;
    private readonly ToonResultSerializer _serializer;

    public CommandResponseWriter(
        TextWriter output,
        ToonResultSerializer? serializer = null)
    {
        _output = output
            ?? throw new ArgumentNullException(nameof(output));
        _serializer = serializer ?? new ToonResultSerializer();
    }

    public ValueTask<int> WriteAsync(
        ICommandResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return WriteAsync(result, ExitCodeFor(result), cancellationToken);
    }

    public async ValueTask<int> WriteAsync(
        ICommandResult result,
        CliExitCode exitCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(exitCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(exitCode),
                exitCode,
                "The CLI exit code is not defined.");
        }

        var document = _serializer.Serialize(result);
        await _output
            .WriteAsync(document.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return (int)exitCode;
    }

    private static CliExitCode ExitCodeFor(ICommandResult result) =>
        result.Status switch
        {
            ResultStatus.Success => CliExitCode.Success,
            ResultStatus.Partial => CliExitCode.Success,
            ResultStatus.Failed => CliExitCode.Failure,
            ResultStatus.Cancelled => CliExitCode.Failure,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Status,
                "The result status is not defined."),
        };
}
