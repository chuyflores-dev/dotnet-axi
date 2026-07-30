using System.CommandLine;

namespace DotNetAxi.Cli;

public static class CommandHandlerBinding
{
    public static void BindHandler<TRequest>(
        this Command command,
        Func<ParseResult, TRequest> bindRequest,
        Func<ICommandHandler<TRequest>> handlerFactory)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(bindRequest);
        ArgumentNullException.ThrowIfNull(handlerFactory);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = bindRequest(parseResult);
            var handler = handlerFactory()
                ?? throw new InvalidOperationException("The command handler factory returned null.");

            return await handler
                .HandleAsync(request, cancellationToken)
                .ConfigureAwait(false);
        });
    }
}
