using System.CommandLine;
using System.CommandLine.Invocation;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class VersionOptionBinding
{
    public static void BindVersionOutput(
        this RootCommand rootCommand,
        Func<CancellationToken, ValueTask<ICommandResult>> resultFactory,
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

        versionOption.Description =
            "Show installed versions and passive compatibility capabilities.";
        versionOption.Action = new StructuredVersionAction(
            versionOption,
            resultFactory,
            responseWriter);
    }

    private sealed class StructuredVersionAction :
        AsynchronousCommandLineAction
    {
        private readonly VersionOption _versionOption;
        private readonly Func<CancellationToken, ValueTask<ICommandResult>> _resultFactory;
        private readonly ICommandResponseWriter _responseWriter;

        public StructuredVersionAction(
            VersionOption versionOption,
            Func<CancellationToken, ValueTask<ICommandResult>> resultFactory,
            ICommandResponseWriter responseWriter)
        {
            _versionOption = versionOption;
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
            var validationResult = TerminatingOptionValidation.ReparseWithout(
                parseResult,
                _versionOption);
            if (parseResult.Errors.Count > 0 ||
                !TerminatingOptionValidation.IsStandalone(
                    parseResult,
                    _versionOption) ||
                TerminatingOptionValidation.ContainsOtherTerminatingOption(
                    parseResult,
                    _versionOption) ||
                !TerminatingOptionValidation.HasOnlyMissingRequiredInputs(
                    validationResult))
            {
                return _responseWriter
                    .WriteAsync(
                        UsageErrorResult.Create(
                            validationResult,
                            CommandName.From(validationResult)),
                        CliExitCode.Usage,
                        cancellationToken)
                    .AsTask();
            }

            return WriteResultAsync(cancellationToken);
        }

        private async Task<int> WriteResultAsync(
            CancellationToken cancellationToken)
        {
            var result = await _resultFactory(cancellationToken)
                .ConfigureAwait(false);
            return await _responseWriter
                .WriteAsync(result, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
