using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

public sealed record CommandOperation
{
    internal CommandOperation(
        Command command,
        string name,
        OperationPolicy policy)
    {
        Command = command
            ?? throw new ArgumentNullException(nameof(command));
        Name = RequiredText(name, nameof(name));
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
    }

    internal Command Command { get; }

    public string Name { get; }

    public OperationPolicy Policy { get; }

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
        OperationPolicy rootPolicy)
    {
        Register(rootCommand, "home", rootPolicy);
    }

    public IReadOnlyList<CommandOperation> Operations => _operations.AsReadOnly();

    public CommandOperation Add(
        Command parent,
        Command command,
        OperationPolicy policy)
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
        var operation = Register(command, name, policy);
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
        OperationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(policy);

        var operation = new CommandOperation(command, name, policy);
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
