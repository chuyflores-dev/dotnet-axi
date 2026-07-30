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

    public static ICommandResult Create(
        UnknownOutputFieldsException exception,
        string command)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var unknown = string.Join(
            ", ",
            exception.UnknownFields.Select(static field => $"`{field}`"));
        var message = exception.UnknownFields.Count == 1
            ? $"Unknown field {unknown} for `{command}`."
            : $"Unknown fields {unknown} for `{command}`.";
        var available = string.Join(
            ", ",
            exception.AvailableFields.Select(static field => $"`{field}`"));
        var correction = $"Use `--fields` with one or more of: {available}.";
        var validFields = exception.AvailableFields
            .Select(static name => new UsageField(name))
            .ToArray();

        return CommandResult<UsageFieldErrorPayload>.Failed(
            command,
            [
                new ResultError(
                    "usage.unknown_field",
                    message,
                    correction),
            ],
            new UsageFieldErrorPayload(validFields));
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

    private sealed record UsageFieldErrorPayload(
        IReadOnlyList<UsageField> ValidFields);

    private sealed record UsageField(string Name);
}
