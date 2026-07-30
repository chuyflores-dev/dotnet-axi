using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class UsageErrorResult
{
    public static ICommandResult Create(
        ParseResult parseResult,
        string command)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var invalidInputs = parseResult.UnmatchedTokens;
        var firstInvalid = invalidInputs.FirstOrDefault();
        var isUnknownFlag = firstInvalid?.StartsWith('-') == true;
        var code = isUnknownFlag
            ? "usage.unknown_flag"
            : "usage.invalid_input";
        var message = CreateMessage(command, invalidInputs, isUnknownFlag);
        var correction = $"Run `{HelpCommand(command)}` to view valid inputs.";
        var validFlags = parseResult.CommandResult.Command.Options
            .SelectMany(option => option.Aliases.Prepend(option.Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(static name => new UsageFlag(name))
            .ToArray();

        return CommandResult<UsageErrorPayload>.Failed(
            command,
            [
                new ResultError(code, message, correction),
            ],
            new UsageErrorPayload(validFlags));
    }

    private static string CreateMessage(
        string command,
        IReadOnlyList<string> invalidInputs,
        bool isUnknownFlag)
    {
        if (invalidInputs.Count == 1 && isUnknownFlag)
        {
            return $"Unknown flag `{invalidInputs[0]}` for `{command}`.";
        }

        if (invalidInputs.Count > 0)
        {
            var inputs = string.Join(
                ", ",
                invalidInputs.Select(static input => $"`{input}`"));
            return $"Invalid input for `{command}`: {inputs}.";
        }

        return $"Invalid input for `{command}`.";
    }

    private static string HelpCommand(string command) =>
        command == "home"
            ? "dnaxi --help"
            : $"dnaxi {command} --help";

    private sealed record UsageErrorPayload(
        IReadOnlyList<UsageFlag> ValidFlags);

    private sealed record UsageFlag(string Name);
}
