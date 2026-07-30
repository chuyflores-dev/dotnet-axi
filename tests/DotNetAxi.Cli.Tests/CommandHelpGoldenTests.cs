using System.CommandLine;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class CommandHelpGoldenTests
{
    [Fact]
    public async Task Root_help_describes_only_the_registered_cli()
    {
        var fixture = CreateFixture();

        var exitCode = await fixture.Host.InvokeAsync(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fixture.HandlerFactoryCalls);
        Assert.Equal(
            ReadFixture("help-root.toon"),
            fixture.Output.ToString());
    }

    [Fact]
    public async Task Subcommand_help_includes_parser_contract_and_is_passive()
    {
        var fixture = CreateFixture();

        var exitCode = await fixture.Host.InvokeAsync(
            ["inspect", "--help"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fixture.HandlerFactoryCalls);
        Assert.Equal(
            ReadFixture("help-subcommand.toon"),
            fixture.Output.ToString());
    }

    private static HelpFixture CreateFixture()
    {
        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Select a solution path.",
            Recursive = true,
        };
        var rootCommand = new RootCommand(
            "Inspect a deterministic fixture.")
        {
            Options = { solutionOption },
        };
        var output = new StringWriter();
        var host = new CommandHost(
            rootCommand,
            OperationPolicy.Passive,
            [
                "dnaxi",
                "dnaxi inspect --help",
            ],
            output,
            new StringWriter());
        rootCommand.BindVersionOutput(
            static () => VersionResult.Create("1.2.3-test"),
            host.ResponseWriter);

        var targetArgument = new Argument<string>("target")
        {
            Description = "Stable ID or exact target name.",
        };
        var formatOption = new Option<string>(
            "--format",
            "-f")
        {
            Description = "Select the detail format.",
            DefaultValueFactory = static _ => "summary",
        };
        var projectOption = new Option<string>("--project")
        {
            Description = "Limit inspection to one project.",
            Required = true,
        };
        var inspectCommand = new Command(
            "inspect",
            "Inspect one registered target.")
        {
            Arguments = { targetArgument },
            Options = { formatOption, projectOption },
        };
        var executingPolicy = new OperationPolicy(
            OperationClassification.Executing,
            mayAccessNetwork: false,
            mayExecuteRepositoryCode: true,
            mayWriteArtifacts: false,
            mayWriteMetadata: false,
            mayWriteUserState: false,
            mayWriteSource: false);
        host.RegisterCommand(
            rootCommand,
            inspectCommand,
            executingPolicy,
            [
                "dnaxi inspect sym_123 --project src/App/App.csproj",
                "dnaxi inspect sym_123 --project src/App/App.csproj --format detailed",
            ]);

        var fixture = new HelpFixture(host, output);
        inspectCommand.BindHandler(
            static _ => HelpRequest.Instance,
            () =>
            {
                fixture.HandlerFactoryCalls++;
                return NeverInvokedHandler.Instance;
            },
            host.ResponseWriter);
        return fixture;
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            .TrimEnd('\r', '\n');

    private sealed class HelpFixture
    {
        public HelpFixture(CommandHost host, StringWriter output)
        {
            Host = host;
            Output = output;
        }

        public CommandHost Host { get; }

        public StringWriter Output { get; }

        public int HandlerFactoryCalls { get; set; }
    }

    private sealed record HelpRequest
    {
        public static HelpRequest Instance { get; } = new();
    }

    private sealed class NeverInvokedHandler :
        ICommandHandler<HelpRequest>
    {
        public static NeverInvokedHandler Instance { get; } = new();

        public ValueTask<ICommandResult> HandleAsync(
            HelpRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Help must not create or invoke a command handler.");
    }
}
