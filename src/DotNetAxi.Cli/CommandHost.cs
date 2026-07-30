using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

public sealed class CommandHost
{
    private readonly RootCommand _rootCommand;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ICommandResponseWriter _responseWriter;
    private readonly ParserConfiguration _parserConfiguration = new()
    {
        EnablePosixBundling = false,
        ResponseFileTokenReplacer = null,
    };

    public CommandHost(
        RootCommand rootCommand,
        TextWriter output,
        TextWriter error)
    {
        _rootCommand = rootCommand
            ?? throw new ArgumentNullException(nameof(rootCommand));
        _output = output
            ?? throw new ArgumentNullException(nameof(output));
        _error = error
            ?? throw new ArgumentNullException(nameof(error));
        _responseWriter = new CommandResponseWriter(_output);
        Diagnostics = new CommandDiagnostics(_error);
    }

    public ICommandResponseWriter ResponseWriter => _responseWriter;

    public ICommandDiagnostics Diagnostics { get; }

    public ParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return _rootCommand.Parse(args, _parserConfiguration);
    }

    public async Task<int> InvokeAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var parseResult = Parse(args);
        var command = CommandName.From(parseResult);

        if (parseResult.Errors.Count > 0)
        {
            var result = UsageErrorResult.Create(parseResult, command);
            return await _responseWriter
                .WriteAsync(result, CliExitCode.Usage, cancellationToken)
                .ConfigureAwait(false);
        }

        var invocationConfiguration = new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = _output,
            Error = _error,
        };

        try
        {
            return await parseResult
                .InvokeAsync(invocationConfiguration, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var result = CommandResult<NoPayload>.Cancelled(
                command,
                errors:
                [
                    new ResultError(
                        "operation.cancelled",
                        $"The `{command}` operation was cancelled.",
                        "Run the command again when the operation can complete."),
                ]);

            return await _responseWriter
                .WriteAsync(result, CliExitCode.Failure, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (UnknownOutputFieldsException exception)
        {
            var result = UsageErrorResult.Create(exception, command);
            return await _responseWriter
                .WriteAsync(result, CliExitCode.Usage, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            var result = CommandResult<NoPayload>.Failed(
                command,
                [
                    new ResultError(
                        "internal.unhandled",
                        $"The `{command}` operation failed unexpectedly.",
                        "Retry the command; report the command and inputs if the failure persists."),
                ]);

            return await _responseWriter
                .WriteAsync(result, CliExitCode.Failure, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private sealed record NoPayload;
}
