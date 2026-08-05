using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class CommandHostTests
{
    [Fact]
    public async Task Root_handler_receives_typed_options()
    {
        var scopeOption = new Option<string?>("--scope");
        var rootCommand = new RootCommand
        {
            Options = { scopeOption },
        };
        var host = CreateHost(rootCommand);
        var handler = new RecordingHandler<RootRequest>();
        rootCommand.BindHandler(
            parseResult => new RootRequest(parseResult.GetValue(scopeOption)),
            () => handler,
            host.ResponseWriter);

        var exitCode = await host.InvokeAsync(["--scope", "repository"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(new RootRequest("repository"), Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task Nested_handler_receives_typed_arguments()
    {
        var targetArgument = new Argument<string>("target");
        var inspectCommand = new Command("inspect")
        {
            Arguments = { targetArgument },
        };
        var rootCommand = new RootCommand();
        var host = CreateHost(rootCommand);
        host.RegisterCommand(
            rootCommand,
            inspectCommand,
            OperationPolicy.Passive,
            [
                "dnaxi inspect Widget",
                "dnaxi inspect --help",
            ]);
        var handler = new RecordingHandler<InspectRequest>();
        inspectCommand.BindHandler(
            parseResult => new InspectRequest(
                parseResult.GetRequiredValue(targetArgument)),
            () => handler,
            host.ResponseWriter);

        var exitCode = await host.InvokeAsync(["inspect", "Widget"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(new InspectRequest("Widget"), Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inspect", "--unknown")]
    public async Task Invalid_input_does_not_create_or_invoke_a_handler(
        params string[] args)
    {
        var inspectCommand = new Command("inspect");
        var rootCommand = new RootCommand();
        var host = CreateHost(rootCommand);
        host.RegisterCommand(
            rootCommand,
            inspectCommand,
            OperationPolicy.Passive,
            [
                "dnaxi inspect",
                "dnaxi inspect --help",
            ]);
        var handler = new RecordingHandler<InspectRequest>();
        var factoryCalls = 0;
        inspectCommand.BindHandler(
            static _ => new InspectRequest("unused"),
            () =>
            {
                factoryCalls++;
                return handler;
            },
            host.ResponseWriter);

        var exitCode = await host.InvokeAsync(args);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, factoryCalls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Invocation_does_not_read_standard_input()
    {
        var rootCommand = new RootCommand();
        var host = CreateHost(rootCommand);
        rootCommand.BindHandler(
            static _ => RootRequest.Empty,
            static () => new RecordingHandler<RootRequest>(),
            host.ResponseWriter);
        var originalInput = Console.In;

        try
        {
            Console.SetIn(new ThrowingTextReader());

            var exitCode = await host.InvokeAsync([]);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetIn(originalInput);
        }
    }

    [Fact]
    public void Registered_operations_are_queryable_before_execution()
    {
        var rootCommand = new RootCommand();
        var inspectCommand = new Command("inspect");
        var host = CreateHost(rootCommand);

        host.RegisterCommand(
            rootCommand,
            inspectCommand,
            OperationPolicy.Passive,
            [
                "dnaxi inspect",
                "dnaxi inspect --help",
            ]);

        Assert.Collection(
            host.Operations,
            operation =>
            {
                Assert.Equal("home", operation.Name);
                Assert.Equal(
                    OperationClassification.Passive,
                    operation.Policy.Classification);
            },
            operation =>
            {
                Assert.Equal("inspect", operation.Name);
                Assert.Same(OperationPolicy.Passive, operation.Policy);
            });
    }

    [Fact]
    public void Production_registry_classifies_every_implemented_command()
    {
        var host = CliApplication.Create(
            new StringWriter(),
            new StringWriter());

        Assert.Collection(host.Operations,
            operation => Assert.Equal("home", operation.Name),
            operation => Assert.Equal("search", operation.Name),
            operation => Assert.Equal("search file", operation.Name),
            operation => Assert.Equal("search text", operation.Name),
            operation => Assert.Equal("search syntax", operation.Name),
            operation => Assert.Equal("search syntax invocation", operation.Name),
            operation => Assert.Equal("search syntax class", operation.Name),
            operation => Assert.Equal("search syntax object-creation", operation.Name),
            operation => Assert.Equal("search syntax catch", operation.Name));
        Assert.All(host.Operations, operation =>
        {
            Assert.Same(OperationPolicy.Passive, operation.Policy);
            Assert.InRange(operation.Examples.Count, 2, 3);
        });

        host.Parse([]);
    }

    [Fact]
    public void Unclassified_command_cannot_enter_the_executable_tree()
    {
        var rootCommand = new RootCommand();
        var host = CreateHost(rootCommand);
        rootCommand.Subcommands.Add(new Command("inspect"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => host.Parse(["inspect"]));

        Assert.Contains(
            "no operation classification",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Command_registration_requires_two_or_three_examples()
    {
        var rootCommand = new RootCommand();
        var host = CreateHost(rootCommand);

        var exception = Assert.Throws<ArgumentException>(
            () => host.RegisterCommand(
                rootCommand,
                new Command("inspect"),
                OperationPolicy.Passive,
                ["dnaxi inspect"]));

        Assert.Equal("examples", exception.ParamName);
        Assert.Single(host.Operations);
        Assert.Empty(rootCommand.Subcommands);
    }

    [Fact]
    public void Command_registration_rejects_incomplete_examples()
    {
        var rootCommand = new RootCommand();
        var host = CreateHost(rootCommand);

        var exception = Assert.Throws<ArgumentException>(
            () => host.RegisterCommand(
                rootCommand,
                new Command("inspect"),
                OperationPolicy.Passive,
                [
                    "inspect Widget",
                    "dnaxi inspect --help",
                ]));

        Assert.Equal("examples", exception.ParamName);
        Assert.Single(host.Operations);
        Assert.Empty(rootCommand.Subcommands);
    }

    private static CommandHost CreateHost(RootCommand rootCommand) =>
        new(
            rootCommand,
            OperationPolicy.Passive,
            [
                "dnaxi",
                "dnaxi --help",
            ],
            new StringWriter(),
            new StringWriter());

    private sealed record RootRequest(string? Scope)
    {
        public static RootRequest Empty { get; } = new(Scope: null);
    }

    private sealed record InspectRequest(string Target);

    private sealed class RecordingHandler<TRequest> : ICommandHandler<TRequest>
    {
        public List<TRequest> Requests { get; } = [];

        public ValueTask<ICommandResult> HandleAsync(
            TRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult<ICommandResult>(
                CommandResult<RecordingPayload>.Success(
                    "test",
                    RecordingPayload.Instance));
        }
    }

    private sealed record RecordingPayload
    {
        public static RecordingPayload Instance { get; } = new();
    }

    private sealed class ThrowingTextReader : TextReader
    {
        public override int Read() =>
            throw new InvalidOperationException("Standard input was read.");

        public override int Read(char[] buffer, int index, int count) =>
            throw new InvalidOperationException("Standard input was read.");

        public override string? ReadLine() =>
            throw new InvalidOperationException("Standard input was read.");

        public override Task<string?> ReadLineAsync() =>
            throw new InvalidOperationException("Standard input was read.");
    }
}
