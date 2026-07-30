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

        versionOption.Description =
            "Show the installed tool and output-schema versions.";
        versionOption.Action = new StructuredVersionAction(
            versionOption,
            resultFactory,
            responseWriter);
    }

    private sealed class StructuredVersionAction :
        AsynchronousCommandLineAction
    {
        private readonly VersionOption _versionOption;
        private readonly Func<ICommandResult> _resultFactory;
        private readonly ICommandResponseWriter _responseWriter;

        public StructuredVersionAction(
            VersionOption versionOption,
            Func<ICommandResult> resultFactory,
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

            return _responseWriter
                .WriteAsync(_resultFactory(), cancellationToken)
                .AsTask();
        }
    }
}
