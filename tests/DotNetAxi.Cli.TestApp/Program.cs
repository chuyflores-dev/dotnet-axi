using System.CommandLine;
using DotNetAxi.Cli;
using DotNetAxi.Contracts;

var passiveBoundaryMarker = Environment.GetEnvironmentVariable(
    "DNAXI_PASSIVE_BOUNDARY_PROCESS_MARKER");
if (!string.IsNullOrWhiteSpace(passiveBoundaryMarker))
{
    await File.AppendAllTextAsync(
        passiveBoundaryMarker,
        $"{Environment.ProcessPath}{Environment.NewLine}");
    return 97;
}

var rootCommand = new RootCommand();
var host = new CommandHost(
    rootCommand,
    OperationPolicy.Passive,
    [
        "dnaxi",
        "dnaxi --help",
    ],
    Console.Out,
    Console.Error);
rootCommand.BindVersionOutput(
    static _ => ValueTask.FromResult(
        VersionResult.Create(
            ToolVersion.FromAssembly(typeof(ScenarioHandler).Assembly))),
    host.ResponseWriter);

AddScenario("success", Scenario.Success, includeKnownOption: true);
AddScenario("empty", Scenario.Empty);
AddScenario("partial", Scenario.Partial);
AddScenario("failed", Scenario.Failed);
AddScenario("cancelled", Scenario.Cancelled);
AddScenario("diagnostic", Scenario.Diagnostic);
AddScenario("throw", Scenario.Throw);
AddScenario("hang", Scenario.Hang);

return await host.InvokeAsync(args);

void AddScenario(
    string name,
    Scenario scenario,
    bool includeKnownOption = false)
{
    var command = new Command(name);
    if (includeKnownOption)
    {
        command.Options.Add(new Option<bool>("--known"));
        command.Options.Add(new Option<string?>("--verbosity"));
    }

    command.BindHandler(
        _ => new ScenarioRequest(scenario),
        () => new ScenarioHandler(host.Diagnostics),
        host.ResponseWriter);
    host.RegisterCommand(
        rootCommand,
        command,
        OperationPolicy.Passive,
        [
            $"dnaxi {name}",
            $"dnaxi {name} --help",
        ]);
}

internal enum Scenario
{
    Success,
    Empty,
    Partial,
    Failed,
    Cancelled,
    Diagnostic,
    Throw,
    Hang,
}

internal sealed record ScenarioRequest(Scenario Scenario);

internal sealed class ScenarioHandler : ICommandHandler<ScenarioRequest>
{
    private readonly ICommandDiagnostics _diagnostics;

    public ScenarioHandler(ICommandDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public async ValueTask<ICommandResult> HandleAsync(
        ScenarioRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return request.Scenario switch
        {
            Scenario.Success => Success("success"),
            Scenario.Empty => CommandResult<EmptyPayload>.Success(
                "empty",
                new EmptyPayload(0, [])),
            Scenario.Partial => CommandResult<KindPayload>.Partial(
                "partial",
                new KindPayload("partial")),
            Scenario.Failed => CommandResult<NoPayload>.Failed(
                "failed",
                [
                    new ResultError(
                        "fixture.failed",
                        "The fixture operation failed.",
                        "Use a successful fixture scenario."),
                ]),
            Scenario.Cancelled => CommandResult<NoPayload>.Cancelled(
                "cancelled",
                errors:
                [
                    new ResultError(
                        "fixture.cancelled",
                        "The fixture operation was cancelled.",
                        "Run the fixture scenario again."),
                ]),
            Scenario.Diagnostic => await DiagnosticAsync(cancellationToken),
            Scenario.Throw => throw new InvalidOperationException(
                "sensitive-stack-marker"),
            Scenario.Hang => await HangAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Scenario,
                "The fixture scenario is not defined."),
        };
    }

    private static ICommandResult Success(string command) =>
        CommandResult<KindPayload>.Success(
            command,
            new KindPayload("success"));

    private async ValueTask<ICommandResult> DiagnosticAsync(
        CancellationToken cancellationToken)
    {
        await _diagnostics.WriteLineAsync(
            "progress: fixture",
            cancellationToken);
        return Success("diagnostic");
    }

    private static async ValueTask<ICommandResult> HangAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Success("hang");
    }

    private sealed record KindPayload(string Kind);

    private sealed record EmptyPayload(
        int Count,
        IReadOnlyList<string> Items);

    private sealed record NoPayload;
}
