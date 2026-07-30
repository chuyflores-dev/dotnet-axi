using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class HelpOptionBinding
{
    public static void BindHelpOutput(
        this RootCommand rootCommand,
        Func<ParseResult, ICommandResult> resultFactory,
        ICommandResponseWriter responseWriter)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        ArgumentNullException.ThrowIfNull(resultFactory);
        ArgumentNullException.ThrowIfNull(responseWriter);

        var helpOption = rootCommand.Options
            .OfType<HelpOption>()
            .Single();
        helpOption.Description =
            "Show structured help for the selected command.";
        helpOption.Action = new StructuredHelpAction(
            resultFactory,
            responseWriter);
    }

    private sealed class StructuredHelpAction :
        AsynchronousCommandLineAction
    {
        private readonly Func<ParseResult, ICommandResult> _resultFactory;
        private readonly ICommandResponseWriter _responseWriter;

        public StructuredHelpAction(
            Func<ParseResult, ICommandResult> resultFactory,
            ICommandResponseWriter responseWriter)
        {
            _resultFactory = resultFactory;
            _responseWriter = responseWriter;
        }

        public override bool Terminating => true;

        public override bool ClearsParseErrors => true;

        public override Task<int> InvokeAsync(
            ParseResult parseResult,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(parseResult);
            return _responseWriter
                .WriteAsync(
                    _resultFactory(parseResult),
                    cancellationToken)
                .AsTask();
        }
    }
}
