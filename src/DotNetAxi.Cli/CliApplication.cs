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
        host.RegisterCommand(rootCommand, searchCommand, OperationPolicy.Passive,
            [
                "dnaxi search file Program",
                "dnaxi search text TODO",
                "dnaxi search syntax invocation --name SaveChangesAsync",
            ]);

        var fileCommand = new Command("file", "Find files by normalized workspace-relative path.");
        var fileQuery = new Argument<string>("query");
        var fileCaseSensitive = new Option<bool>("--case-sensitive");
        var extension = new Option<string[]>("--extension") { AllowMultipleArgumentsPerToken = false };
        var glob = new Option<string[]>("--glob") { AllowMultipleArgumentsPerToken = false };
        var filePath = new Option<string[]>("--path") { AllowMultipleArgumentsPerToken = false };
        var fileProject = new Option<string?>("--project");
        var fileChanged = new Option<bool>("--changed");
        var fileIncludeGenerated = new Option<bool>("--include-generated");
        var fileLimit = new Option<int>("--limit") { DefaultValueFactory = static _ => 100 };
        var fileFields = new Option<string[]>("--fields") { AllowMultipleArgumentsPerToken = true };
        fileCommand.Arguments.Add(fileQuery);
        fileCommand.Options.Add(fileCaseSensitive);
        fileCommand.Options.Add(extension);
        fileCommand.Options.Add(glob);
        fileCommand.Options.Add(filePath);
        fileCommand.Options.Add(fileProject);
        fileCommand.Options.Add(fileChanged);
        fileCommand.Options.Add(fileIncludeGenerated);
        fileCommand.Options.Add(fileLimit);
        fileCommand.Options.Add(fileFields);
        host.RegisterCommand(searchCommand, fileCommand, OperationPolicy.Passive,
            ["dnaxi search file Program", "dnaxi search file .cs --extension cs --path src"]);
        fileCommand.BindHandler(
            result => FileSearchCommandRequest.Create(
                result.GetValue(fileQuery)!,
                result.GetValue(fileCaseSensitive),
                result.GetValue(extension) ?? [],
                result.GetValue(glob) ?? [],
                result.GetValue(filePath) ?? [],
                result.GetValue(fileProject),
                result.GetValue(fileChanged),
                result.GetValue(fileIncludeGenerated),
                result.GetValue(fileLimit),
                result.GetValue(fileFields) ?? []),
            static () => new FileSearchCommandHandler(),
            host.ResponseWriter);

        var textCommand = new Command(
            "text",
            "Find literal or regular-expression text in eligible workspace files.");
        var query = new Argument<string>("query");
        var caseSensitive = new Option<bool>("--case-sensitive");
        var includeGenerated = new Option<bool>("--include-generated");
        var regex = new Option<bool>("--regex")
        {
            Description = "Interpret the query using the .NET regular-expression language.",
        };
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
        host.RegisterCommand(searchCommand, textCommand, OperationPolicy.Passive,
            [
                "dnaxi search text TODO --path src",
                "dnaxi search text 'TODO|FIXME' --regex",
            ]);
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

        var syntaxCommand = new Command(
            "syntax",
            "Search stable tool-owned C# syntax shapes without loading a compilation.");
        host.RegisterCommand(searchCommand, syntaxCommand, OperationPolicy.Passive,
            [
                "dnaxi search syntax invocation --name SaveChangesAsync",
                "dnaxi search syntax invocation --name Map --path src",
            ]);

        var invocationCommand = new Command(
            "invocation",
            "Find C# invocation syntax by exact terminal name; results are syntax candidates.");
        var invocationName = new Option<string>("--name")
        {
            Description = "Match the exact ordinal terminal invocation identifier.",
            Required = true,
        };
        var invocationIncludeGenerated = new Option<bool>("--include-generated");
        var invocationLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var invocationFull = new Option<bool>("--full");
        var invocationFields = new Option<string[]>("--fields")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var invocationPath = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        invocationCommand.Options.Add(invocationName);
        invocationCommand.Options.Add(invocationIncludeGenerated);
        invocationCommand.Options.Add(invocationLimit);
        invocationCommand.Options.Add(invocationFull);
        invocationCommand.Options.Add(invocationFields);
        invocationCommand.Options.Add(invocationPath);
        host.RegisterCommand(syntaxCommand, invocationCommand, OperationPolicy.Passive,
            [
                "dnaxi search syntax invocation --name SaveChangesAsync",
                "dnaxi search syntax invocation --name Map --path src --include-generated",
            ]);
        invocationCommand.BindHandler(
            result => InvocationSyntaxCommandRequest.Create(
                result.GetValue(invocationName)!,
                result.GetValue(invocationIncludeGenerated),
                result.GetValue(invocationLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(invocationFull),
                result.GetValue(invocationFields) ?? [],
                result.GetValue(invocationPath) ?? []),
            static () => new InvocationSyntaxCommandHandler(),
            host.ResponseWriter);

        return host;
    }
}
