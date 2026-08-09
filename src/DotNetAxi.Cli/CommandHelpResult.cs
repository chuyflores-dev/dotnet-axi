using System.CommandLine;
using System.Globalization;
using DotNetAxi.Axi;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class CommandHelpResult
{
    public static ICommandResult Create(
        CommandOperation operation,
        Func<Command, CommandOperation> resolveOperation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(resolveOperation);

        var command = operation.Command;
        var arguments = command.Arguments
            .Where(static argument => !argument.Hidden)
            .Select(CreateArgument)
            .ToArray();
        var flags = AvailableOptions(command)
            .Where(static entry => !entry.Option.Hidden)
            .OrderBy(
                static entry => entry.Option.Name,
                StringComparer.Ordinal)
            .Select(static entry => CreateFlag(
                entry.Option,
                entry.Inherited))
            .ToArray();
        var subcommands = command.Subcommands
            .Where(static subcommand => !subcommand.Hidden)
            .Select(resolveOperation)
            .OrderBy(
                static subcommand => subcommand.Name,
                StringComparer.Ordinal)
            .Select(static subcommand => new HelpSubcommand(
                subcommand.Name,
                OptionalText(subcommand.Command.Description),
                subcommand.Policy.Classification))
            .ToArray();
        var examples = operation.Examples
            .Select(static example => new HelpExample(
                CanonicalInvocation.OneShot(example)))
            .ToArray();

        return CommandResult<HelpPayload>.Success(
            "help",
            new HelpPayload(
                operation.Name,
                CreateUsage(operation, arguments, flags, subcommands),
                OptionalText(command.Description),
                operation.Policy.Classification,
                arguments,
                flags,
                subcommands,
                examples));
    }

    private static HelpArgument CreateArgument(Argument argument)
    {
        var arity = argument.Arity;
        return new HelpArgument(
            argument.Name,
            arity.MinimumNumberOfValues > 0,
            TypeName(argument.ValueType),
            arity.MinimumNumberOfValues,
            MaximumValues(arity),
            DefaultValue(argument),
            OptionalText(argument.Description));
    }

    private static HelpFlag CreateFlag(
        Option option,
        bool inherited)
    {
        var arity = option.Arity;
        var aliases = option.Aliases
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new HelpFlag(
            option.Name,
            aliases,
            option.Required,
            arity.MaximumNumberOfValues == 0
                ? null
                : TypeName(option.ValueType),
            arity.MinimumNumberOfValues,
            MaximumValues(arity),
            DefaultValue(option),
            inherited,
            OptionalText(option.Description));
    }

    private static IEnumerable<AvailableOption> AvailableOptions(
        Command command)
    {
        var seen = new HashSet<Option>(ReferenceEqualityComparer.Instance);
        foreach (var option in command.Options)
        {
            if (seen.Add(option))
            {
                yield return new AvailableOption(
                    option,
                    Inherited: false);
            }
        }

        for (var ancestor = Parent(command);
             ancestor is not null;
             ancestor = Parent(ancestor))
        {
            foreach (var option in ancestor.Options.Where(
                         static option => option.Recursive))
            {
                if (seen.Add(option))
                {
                    yield return new AvailableOption(
                        option,
                        Inherited: true);
                }
            }
        }
    }

    private static Command? Parent(Command command) =>
        command.Parents
            .OfType<Command>()
            .FirstOrDefault();

    private static string CreateUsage(
        CommandOperation operation,
        IReadOnlyList<HelpArgument> arguments,
        IReadOnlyList<HelpFlag> flags,
        IReadOnlyList<HelpSubcommand> subcommands)
    {
        var segments = new List<string> { "dnaxi" };
        if (operation.Name is not "home")
        {
            segments.Add(operation.Name);
        }

        segments.AddRange(arguments.Select(ArgumentUsage));
        if (flags.Count > 0)
        {
            segments.Add("[options]");
        }

        if (subcommands.Count > 0)
        {
            segments.Add("[command]");
        }

        return string.Join(' ', segments);
    }

    private static string ArgumentUsage(HelpArgument argument)
    {
        var suffix = argument.MaximumValues is null or > 1
            ? "..."
            : string.Empty;
        var value = $"<{argument.Name}>{suffix}";
        return argument.Required
            ? value
            : $"[{value}]";
    }

    private static int? MaximumValues(ArgumentArity arity) =>
        arity.MaximumNumberOfValues == int.MaxValue
            ? null
            : arity.MaximumNumberOfValues;

    private static string? DefaultValue(Argument argument) =>
        argument.HasDefaultValue
            ? FormatDefault(argument.GetDefaultValue())
            : null;

    private static string? DefaultValue(Option option) =>
        option.HasDefaultValue
            ? FormatDefault(option.GetDefaultValue())
            : null;

    private static string FormatDefault(object? value) =>
        value switch
        {
            null => "null",
            bool flag => flag ? "true" : "false",
            string text => text,
            char character => character.ToString(),
            Enum enumValue => enumValue.ToString(),
            IFormattable formattable => formattable.ToString(
                format: null,
                CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };

    private static string TypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return $"{TypeName(underlying)}?";
        }

        if (type.IsArray)
        {
            return $"{TypeName(type.GetElementType()!)}[]";
        }

        return type == typeof(bool)
            ? "bool"
            : type == typeof(int)
                ? "int"
                : type == typeof(string)
                    ? "string"
                    : type.Name;
    }

    private static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value;

    private sealed record HelpPayload(
        string Topic,
        string Usage,
        string? Description,
        OperationClassification Classification,
        IReadOnlyList<HelpArgument> Arguments,
        IReadOnlyList<HelpFlag> Flags,
        IReadOnlyList<HelpSubcommand> Subcommands,
        IReadOnlyList<HelpExample> Examples);

    private sealed record HelpArgument(
        string Name,
        bool Required,
        string ValueType,
        int MinimumValues,
        int? MaximumValues,
        string? DefaultValue,
        string? Description);

    private sealed record HelpFlag(
        string Name,
        IReadOnlyList<string> Aliases,
        bool Required,
        string? ValueType,
        int MinimumValues,
        int? MaximumValues,
        string? DefaultValue,
        bool Inherited,
        string? Description);

    private sealed record HelpSubcommand(
        string Name,
        string? Description,
        OperationClassification Classification);

    private sealed record HelpExample(string Command);

    private readonly record struct AvailableOption(
        Option Option,
        bool Inherited);
}
