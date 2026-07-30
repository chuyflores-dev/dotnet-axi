using System.CommandLine;
using System.CommandLine.Invocation;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class VersionOptionBinding
{
    public static void BindVersionOutput(
        this RootCommand rootCommand,
        Func<ICommandResult> resultFactory,
        ICommandResponseWriter responseWriter)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        ArgumentNullException.ThrowIfNull(resultFactory);
        ArgumentNullException.ThrowIfNull(responseWriter);

        var versionOption = rootCommand.Options
            .OfType<VersionOption>()
            .Single();
        if (!versionOption.Aliases.Contains("-v", StringComparer.Ordinal))
        {
            versionOption.Aliases.Add("-v");
        }

        versionOption.Action = new StructuredVersionAction(
            resultFactory,
            responseWriter);
    }

    private sealed class StructuredVersionAction :
        AsynchronousCommandLineAction
    {
        private readonly Func<ICommandResult> _resultFactory;
        private readonly ICommandResponseWriter _responseWriter;

        public StructuredVersionAction(
            Func<ICommandResult> resultFactory,
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
                .WriteAsync(_resultFactory(), cancellationToken)
                .AsTask();
        }
    }
}
