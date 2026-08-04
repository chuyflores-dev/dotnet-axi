using System.CommandLine;
using DotNetAxi.Contracts;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal static class CliApplication
{
    public static CommandHost Create(TextWriter output, TextWriter error)
        => Create(
            output,
            error,
            static () => HomeInvocationContext.Capture(),
            static () => new WorkspaceDiscoverer(),
            static () => new WorktreeStateInspector());

    internal static CommandHost Create(
        TextWriter output,
        TextWriter error,
        Func<HomeInvocationContext> homeContextFactory,
        Func<WorkspaceDiscoverer> workspaceDiscovererFactory,
        Func<WorktreeStateInspector> worktreeStateInspectorFactory)
    {
        ArgumentNullException.ThrowIfNull(homeContextFactory);
        ArgumentNullException.ThrowIfNull(workspaceDiscovererFactory);
        ArgumentNullException.ThrowIfNull(worktreeStateInspectorFactory);
        var rootCommand = new RootCommand(
            "Deterministic .NET discovery, analysis, validation, and safe modification.");
        var host = new CommandHost(
            rootCommand,
            OperationPolicy.Passive,
            [
                "dnaxi",
                "dnaxi --help",
                "dnaxi --version",
            ],
            output,
            error);
        rootCommand.BindVersionOutput(
            static () => VersionResult.Create(ToolVersion.Current),
            host.ResponseWriter);
        rootCommand.BindHandler(
            static _ => HomeRequest.Instance,
            () => new HomeCommandHandler(
                homeContextFactory(),
                workspaceDiscovererFactory(),
                new WorkspaceEntryPointSelector(),
                worktreeStateInspectorFactory()),
            host.ResponseWriter);

        var searchCommand = new Command("search", "Search the current workspace.");
        var textCommand = new Command("text", "Find literal text in eligible workspace files.");
        var query = new Argument<string>("query");
        var caseSensitive = new Option<bool>("--case-sensitive");
        var includeGenerated = new Option<bool>("--include-generated");
        var regex = new Option<bool>("--regex");
        var limit = new Option<int>("--limit") { DefaultValueFactory = static _ => 100 };
        var full = new Option<bool>("--full");
        var fields = new Option<string[]>("--fields") { AllowMultipleArgumentsPerToken = true };
        var path = new Option<string[]>("--path") { AllowMultipleArgumentsPerToken = false };
        var project = new Option<string?>("--project");
        var changed = new Option<bool>("--changed");
        var baseReference = new Option<string?>("--base");
        var head = new Option<string?>("--head");
        textCommand.Arguments.Add(query);
        textCommand.Options.Add(caseSensitive);
        textCommand.Options.Add(includeGenerated);
        textCommand.Options.Add(regex);
        textCommand.Options.Add(limit);
        textCommand.Options.Add(full);
        textCommand.Options.Add(fields);
        textCommand.Options.Add(path);
        textCommand.Options.Add(project);
        textCommand.Options.Add(changed);
        textCommand.Options.Add(baseReference);
        textCommand.Options.Add(head);
        host.RegisterCommand(rootCommand, searchCommand, OperationPolicy.Passive,
            ["dnaxi search text TODO", "dnaxi search text TODO --path src"]);
        host.RegisterCommand(searchCommand, textCommand, OperationPolicy.Passive,
            ["dnaxi search text TODO --path src", "dnaxi search text TODO --case-sensitive"]);
        textCommand.BindHandler(
            result => TextSearchCommandRequest.Create(
                result.GetValue(query)!,
                result.GetValue(caseSensitive),
                result.GetValue(includeGenerated),
                result.GetValue(regex),
                result.GetValue(limit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(full),
                result.GetValue(fields) ?? [],
                result.GetValue(path) ?? [],
                result.GetValue(project),
                result.GetValue(changed),
                result.GetValue(baseReference),
                result.GetValue(head)),
            static () => new TextSearchCommandHandler(),
            host.ResponseWriter);

        return host;
    }
}
