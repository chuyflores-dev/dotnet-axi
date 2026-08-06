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
            static () => WorktreeStateInspector.CreatePassive(),
            static () => PassiveCapabilityReporterFactory.Create());

    internal static CommandHost Create(
        TextWriter output,
        TextWriter error,
        Func<HomeInvocationContext> homeContextFactory,
        Func<WorkspaceDiscoverer> workspaceDiscovererFactory,
        Func<WorktreeStateInspector> worktreeStateInspectorFactory) =>
        Create(
            output,
            error,
            homeContextFactory,
            workspaceDiscovererFactory,
            worktreeStateInspectorFactory,
            static () => PassiveCapabilityReporterFactory.Create());

    internal static CommandHost Create(
        TextWriter output,
        TextWriter error,
        Func<HomeInvocationContext> homeContextFactory,
        Func<WorkspaceDiscoverer> workspaceDiscovererFactory,
        Func<WorktreeStateInspector> worktreeStateInspectorFactory,
        Func<ICapabilityReporter> capabilityReporterFactory)
    {
        ArgumentNullException.ThrowIfNull(homeContextFactory);
        ArgumentNullException.ThrowIfNull(workspaceDiscovererFactory);
        ArgumentNullException.ThrowIfNull(worktreeStateInspectorFactory);
        ArgumentNullException.ThrowIfNull(capabilityReporterFactory);
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
            async cancellationToken =>
            {
                var workspace = workspaceDiscovererFactory().Discover(
                    homeContextFactory().CurrentDirectory);
                return VersionResult.Create(
                    ToolVersion.Current,
                    await capabilityReporterFactory()
                        .ReportAsync(workspace.RootPath, cancellationToken)
                        .ConfigureAwait(false));
            },
            host.ResponseWriter);
        rootCommand.BindHandler(
            static _ => HomeRequest.Instance,
            () => new HomeCommandHandler(
                homeContextFactory(),
                workspaceDiscovererFactory(),
                new WorkspaceEntryPointSelector(),
                worktreeStateInspectorFactory(),
                capabilityReporterFactory()),
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
                "dnaxi search syntax class --attribute Authorize",
                "dnaxi search syntax catch --type Exception --empty",
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

        var attributedClassCommand = new Command(
            "class",
            "Find C# class declarations by syntactic attribute name; results are syntax candidates.");
        var classAttribute = new Option<string>("--attribute")
        {
            Description =
                "Match the ordinal terminal attribute identifier with optional Attribute suffix.",
            Required = true,
        };
        var classIncludeGenerated = new Option<bool>("--include-generated");
        var classLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var classFull = new Option<bool>("--full");
        var classFields = new Option<string[]>("--fields")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var classPath = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        attributedClassCommand.Options.Add(classAttribute);
        attributedClassCommand.Options.Add(classIncludeGenerated);
        attributedClassCommand.Options.Add(classLimit);
        attributedClassCommand.Options.Add(classFull);
        attributedClassCommand.Options.Add(classFields);
        attributedClassCommand.Options.Add(classPath);
        host.RegisterCommand(syntaxCommand, attributedClassCommand, OperationPolicy.Passive,
            [
                "dnaxi search syntax class --attribute Authorize",
                "dnaxi search syntax class --attribute Obsolete --path src --include-generated",
            ]);
        attributedClassCommand.BindHandler(
            result => AttributedClassSyntaxCommandRequest.Create(
                result.GetValue(classAttribute)!,
                result.GetValue(classIncludeGenerated),
                result.GetValue(classLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(classFull),
                result.GetValue(classFields) ?? [],
                result.GetValue(classPath) ?? []),
            static () => new AttributedClassSyntaxCommandHandler(),
            host.ResponseWriter);

        var objectCreationCommand = new Command(
            "object-creation",
            "Find explicit C# object or array creation syntax by terminal type name; "
                + "target-typed new() remains an unresolved syntax candidate.");
        var objectCreationType = new Option<string>("--type")
        {
            Description = "Match the exact ordinal terminal type name.",
            Required = true,
        };
        var objectCreationIncludeGenerated = new Option<bool>("--include-generated");
        var objectCreationLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var objectCreationFull = new Option<bool>("--full");
        var objectCreationFields = new Option<string[]>("--fields")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var objectCreationPath = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        objectCreationCommand.Options.Add(objectCreationType);
        objectCreationCommand.Options.Add(objectCreationIncludeGenerated);
        objectCreationCommand.Options.Add(objectCreationLimit);
        objectCreationCommand.Options.Add(objectCreationFull);
        objectCreationCommand.Options.Add(objectCreationFields);
        objectCreationCommand.Options.Add(objectCreationPath);
        host.RegisterCommand(syntaxCommand, objectCreationCommand, OperationPolicy.Passive,
            [
                "dnaxi search syntax object-creation --type HttpClient",
                "dnaxi search syntax object-creation --type Widget --path src --include-generated",
            ]);
        objectCreationCommand.BindHandler(
            result => ObjectCreationSyntaxCommandRequest.Create(
                result.GetValue(objectCreationType)!,
                result.GetValue(objectCreationIncludeGenerated),
                result.GetValue(objectCreationLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(objectCreationFull),
                result.GetValue(objectCreationFields) ?? [],
                result.GetValue(objectCreationPath) ?? []),
            static () => new ObjectCreationSyntaxCommandHandler(),
            host.ResponseWriter);

        var catchCommand = new Command(
            "catch",
            "Find C# catch-clause syntax; --type excludes untyped catches and --empty "
                + "matches bodies with no parsed statements.");
        var catchType = new Option<string?>("--type")
        {
            Description = "Match the exact ordinal terminal exception type name.",
        };
        var catchEmpty = new Option<bool>("--empty")
        {
            Description = "Return only catches whose block has no parsed statements.",
        };
        var catchIncludeGenerated = new Option<bool>("--include-generated");
        var catchLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var catchFull = new Option<bool>("--full");
        var catchFields = new Option<string[]>("--fields")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var catchPath = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        catchCommand.Options.Add(catchType);
        catchCommand.Options.Add(catchEmpty);
        catchCommand.Options.Add(catchIncludeGenerated);
        catchCommand.Options.Add(catchLimit);
        catchCommand.Options.Add(catchFull);
        catchCommand.Options.Add(catchFields);
        catchCommand.Options.Add(catchPath);
        host.RegisterCommand(syntaxCommand, catchCommand, OperationPolicy.Passive,
            [
                "dnaxi search syntax catch",
                "dnaxi search syntax catch --type Exception --empty --path src",
            ]);
        catchCommand.BindHandler(
            result => CatchSyntaxCommandRequest.Create(
                result.GetValue(catchType),
                result.GetValue(catchEmpty),
                result.GetValue(catchIncludeGenerated),
                result.GetValue(catchLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(catchFull),
                result.GetValue(catchFields) ?? [],
                result.GetValue(catchPath) ?? []),
            static () => new CatchSyntaxCommandHandler(),
            host.ResponseWriter);

        return host;
    }
}
