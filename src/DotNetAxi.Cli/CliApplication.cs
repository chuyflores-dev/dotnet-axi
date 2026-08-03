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

        return host;
    }
}
