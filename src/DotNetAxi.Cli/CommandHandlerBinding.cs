using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

public static class CommandHandlerBinding
{
    public static void BindHandler<TRequest>(
        this Command command,
        Func<ParseResult, TRequest> bindRequest,
        Func<ICommandHandler<TRequest>> handlerFactory,
        ICommandResponseWriter responseWriter)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(bindRequest);
        ArgumentNullException.ThrowIfNull(handlerFactory);
        ArgumentNullException.ThrowIfNull(responseWriter);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var request = bindRequest(parseResult);
                var handler = handlerFactory()
                    ?? throw new InvalidOperationException("The command handler factory returned null.");
                var result = await handler
                    .HandleAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                return await responseWriter
                    .WriteAsync(result, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CommandUsageException exception)
            {
                var result = CommandResult<UsagePayload>.Failed(
                    CommandName.From(parseResult),
                    [new ResultError(exception.Code, exception.Message, exception.Correction)]);
                return await responseWriter
                    .WriteAsync(result, CliExitCode.Usage, cancellationToken)
                    .ConfigureAwait(false);
            }
        });
    }

    private sealed record UsagePayload;
}
