using System.CommandLine;

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
        var handler = new RecordingHandler<RootRequest>();
        rootCommand.BindHandler(
            parseResult => new RootRequest(parseResult.GetValue(scopeOption)),
            () => handler);
        var host = CreateHost(rootCommand);

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
        var rootCommand = new RootCommand
        {
            Subcommands = { inspectCommand },
        };
        var handler = new RecordingHandler<InspectRequest>();
        inspectCommand.BindHandler(
            parseResult => new InspectRequest(
                parseResult.GetRequiredValue(targetArgument)),
            () => handler);
        var host = CreateHost(rootCommand);

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
        var rootCommand = new RootCommand
        {
            Subcommands = { inspectCommand },
        };
        var handler = new RecordingHandler<InspectRequest>();
        var factoryCalls = 0;
        inspectCommand.BindHandler(
            static _ => new InspectRequest("unused"),
            () =>
            {
                factoryCalls++;
                return handler;
            });
        var host = CreateHost(rootCommand);

        var exitCode = await host.InvokeAsync(args);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, factoryCalls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Invocation_does_not_read_standard_input()
    {
        var rootCommand = new RootCommand();
        rootCommand.BindHandler(
            static _ => RootRequest.Empty,
            static () => new RecordingHandler<RootRequest>());
        var host = CreateHost(rootCommand);
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

    private static CommandHost CreateHost(RootCommand rootCommand) =>
        new(rootCommand, new StringWriter(), new StringWriter());

    private sealed record RootRequest(string? Scope)
    {
        public static RootRequest Empty { get; } = new(Scope: null);
    }

    private sealed record InspectRequest(string Target);

    private sealed class RecordingHandler<TRequest> : ICommandHandler<TRequest>
    {
        public List<TRequest> Requests { get; } = [];

        public ValueTask<int> HandleAsync(
            TRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(0);
        }
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
