using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

public sealed record CommandOperation
{
    private readonly Dictionary<Option, OperationPolicy> _optionPolicies =
        new(ReferenceEqualityComparer.Instance);
    internal CommandOperation(
        Command command,
        string name,
        OperationPolicy policy,
        IEnumerable<string> examples)
    {
        Command = command
            ?? throw new ArgumentNullException(nameof(command));
        Name = RequiredText(name, nameof(name));
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
        Examples = CopyExamples(examples);
    }

    internal Command Command { get; }

    public string Name { get; }

    public OperationPolicy Policy { get; }

    public IReadOnlyList<string> Examples { get; }

    internal IReadOnlyDictionary<Option, OperationPolicy> OptionPolicies =>
        _optionPolicies;

    internal void AddOptionPolicy(Option option, OperationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(policy);
        if (!Command.Options.Contains(option))
        {
            throw new InvalidOperationException(
                $"Option '{option.Name}' is not registered on command '{Name}'.");
        }

        if (option.ValueType != typeof(bool))
        {
            throw new InvalidOperationException(
                "Conditional operation policies require a Boolean option.");
        }

        if (!_optionPolicies.TryAdd(option, policy))
        {
            throw new InvalidOperationException(
                $"Option '{option.Name}' already has an operation policy.");
        }
    }

    private static IReadOnlyList<string> CopyExamples(
        IEnumerable<string> examples)
    {
        ArgumentNullException.ThrowIfNull(examples);

        var copy = examples
            .Select(example => RequiredText(example, nameof(examples)))
            .ToArray();
        if (copy.Length is < 2 or > 3)
        {
            throw new ArgumentException(
                "Commands require two or three representative examples.",
                nameof(examples));
        }

        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException(
                "Command examples must be distinct.",
                nameof(examples));
        }

        if (copy.Any(static example =>
                example is not "dnaxi" &&
                !example.StartsWith("dnaxi ", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Command examples must be complete `dnaxi` invocations.",
                nameof(examples));
        }

        return Array.AsReadOnly(copy);
    }

    private static string RequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A non-empty value is required.",
                parameterName);
        }

        return value;
    }
}

internal sealed class CommandOperationRegistry
{
    private readonly Dictionary<Command, CommandOperation> _byCommand =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, CommandOperation> _byName =
        new(StringComparer.Ordinal);
    private readonly List<CommandOperation> _operations = [];

    public CommandOperationRegistry(
        RootCommand rootCommand,
        OperationPolicy rootPolicy,
        IEnumerable<string> rootExamples)
    {
        Register(rootCommand, "home", rootPolicy, rootExamples);
    }

    public IReadOnlyList<CommandOperation> Operations => _operations.AsReadOnly();

    public CommandOperation Add(
        Command parent,
        Command command,
        OperationPolicy policy,
        IEnumerable<string> examples)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(policy);

        if (!_byCommand.TryGetValue(parent, out var parentOperation))
        {
            throw new InvalidOperationException(
                $"Parent command '{parent.Name}' is not registered.");
        }

        var name = parentOperation.Name is "home"
            ? command.Name
            : $"{parentOperation.Name} {command.Name}";
        var operation = Register(command, name, policy, examples);
        parent.Subcommands.Add(command);
        return operation;
    }

    public CommandOperation Get(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _byCommand.TryGetValue(command, out var operation)
            ? operation
            : throw new InvalidOperationException(
                $"Command '{command.Name}' has no operation classification.");
    }

    public OperationPolicy Resolve(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        var operation = Get(parseResult.CommandResult.Command);
        return operation.OptionPolicies
            .Where(pair => parseResult.GetValue((Option<bool>)pair.Key))
            .Select(static pair => pair.Value)
            .OrderByDescending(static policy => policy.Classification)
            .FirstOrDefault()
            ?? operation.Policy;
    }

    public void AddOptionPolicy(
        CommandOperation operation,
        Option<bool> option,
        OperationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!_operations.Contains(operation))
        {
            throw new InvalidOperationException(
                "The command operation is not registered by this host.");
        }

        operation.AddOptionPolicy(option, policy);
    }

    public void EnsureComplete(RootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);

        var discovered = new HashSet<Command>(ReferenceEqualityComparer.Instance);
        Visit(rootCommand, discovered);

        var detached = _operations
            .Where(operation => !discovered.Contains(operation.Command))
            .Select(operation => operation.Name)
            .ToArray();
        if (detached.Length > 0)
        {
            throw new InvalidOperationException(
                $"Registered commands are detached from the command tree: {string.Join(", ", detached)}.");
        }
    }

    private CommandOperation Register(
        Command command,
        string name,
        OperationPolicy policy,
        IEnumerable<string> examples)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(policy);

        var operation = new CommandOperation(
            command,
            name,
            policy,
            examples);
        if (!_byCommand.TryAdd(command, operation))
        {
            throw new InvalidOperationException(
                $"Command '{command.Name}' is already registered.");
        }

        if (!_byName.TryAdd(operation.Name, operation))
        {
            _byCommand.Remove(command);
            throw new InvalidOperationException(
                $"Operation name '{operation.Name}' is already registered.");
        }

        _operations.Add(operation);
        return operation;
    }

    private void Visit(
        Command command,
        ISet<Command> discovered)
    {
        Get(command);
        discovered.Add(command);

        foreach (var subcommand in command.Subcommands)
        {
            Visit(subcommand, discovered);
        }
    }
}
