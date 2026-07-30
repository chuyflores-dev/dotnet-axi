using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
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
            helpOption,
            resultFactory,
            responseWriter);
    }

    private sealed class StructuredHelpAction :
        AsynchronousCommandLineAction
    {
        private readonly HelpOption _helpOption;
        private readonly Func<ParseResult, ICommandResult> _resultFactory;
        private readonly ICommandResponseWriter _responseWriter;

        public StructuredHelpAction(
            HelpOption helpOption,
            Func<ParseResult, ICommandResult> resultFactory,
            ICommandResponseWriter responseWriter)
        {
            _helpOption = helpOption;
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
                _helpOption);
            if (parseResult.Errors.Count > 0 ||
                TerminatingOptionValidation.ContainsOtherTerminatingOption(
                    parseResult,
                    _helpOption) ||
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
                .WriteAsync(
                    _resultFactory(parseResult),
                    cancellationToken)
                .AsTask();
        }
    }
}

internal static class TerminatingOptionValidation
{
    public static bool IsStandalone(
        ParseResult parseResult,
        Option terminatingOption)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(terminatingOption);

        return parseResult.Tokens.Count == 1 &&
               IsOptionToken(
                   parseResult.Tokens[0].Value,
                   terminatingOption);
    }

    public static bool ContainsOtherTerminatingOption(
        ParseResult parseResult,
        Option terminatingOption)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(terminatingOption);

        var tokenValues = parseResult.Tokens
            .Select(static token => token.Value)
            .ToHashSet(StringComparer.Ordinal);
        return parseResult.RootCommandResult.Command.Options
            .Where(option =>
                !ReferenceEquals(option, terminatingOption) &&
                option.Action?.Terminating == true)
            .Any(option => option.Aliases
                .Append(option.Name)
                .Any(tokenValues.Contains));
    }

    public static bool HasOnlyMissingRequiredInputs(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        if (parseResult.UnmatchedTokens.Count > 0)
        {
            return false;
        }

        foreach (var errors in parseResult.Errors.GroupBy(
                     static error => error.SymbolResult,
                     ReferenceEqualityComparer.Instance))
        {
            var missingInputCount = errors.Key switch
            {
                ArgumentResult argumentResult
                    when argumentResult.Tokens.Count <
                         argumentResult.Argument.Arity.MinimumNumberOfValues =>
                    1,
                OptionResult optionResult
                    when optionResult.Option.Required &&
                         optionResult.Implicit &&
                         optionResult.IdentifierTokenCount == 0 =>
                    1,
                System.CommandLine.Parsing.CommandResult commandResult =>
                    MissingRequiredInputCount(parseResult, commandResult),
                _ => 0,
            };

            if (missingInputCount == 0 ||
                errors.Count() != missingInputCount)
            {
                return false;
            }
        }

        return true;
    }

    public static ParseResult ReparseWithout(
        ParseResult parseResult,
        Option terminatingOption)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(terminatingOption);

        var optionNames = terminatingOption.Aliases
            .Append(terminatingOption.Name)
            .ToHashSet(StringComparer.Ordinal);
        var arguments = parseResult.Tokens
            .Where(token => !optionNames.Contains(token.Value))
            .Select(static token => token.Value)
            .ToArray();

        return parseResult.RootCommandResult.Command.Parse(
            arguments,
            parseResult.Configuration);
    }

    private static int MissingRequiredInputCount(
        ParseResult parseResult,
        System.CommandLine.Parsing.CommandResult commandResult)
    {
        var count = commandResult.Command.Options.Count(option =>
            option.Required &&
            parseResult.GetResult(option) is null);
        count += commandResult.Command.Arguments.Count(argument =>
            argument.Arity.MinimumNumberOfValues > 0 &&
            parseResult.GetResult(argument) is null);
        var requiresSubcommand =
            commandResult.Command.Action is null &&
            commandResult.Command.Subcommands.Count > 0 &&
            !commandResult.Children
                .OfType<System.CommandLine.Parsing.CommandResult>()
                .Any();

        return requiresSubcommand
            ? count + 1
            : count;
    }

    private static bool IsOptionToken(string token, Option option) =>
        string.Equals(token, option.Name, StringComparison.Ordinal) ||
        option.Aliases.Contains(token, StringComparer.Ordinal);
}
