using System.CommandLine;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
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
            static () => WorktreeStateInspector.CreatePassive(
                new ProcessRunner()),
            static () => CapabilityReporter.CreateDefault());

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
            static () => CapabilityReporter.CreateDefault());

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
                "dnaxi search symbol Widget",
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

        var symbolCommand = new Command(
            "symbol",
            "Find and rank C# declaration candidates without loading a compilation.");
        var symbolQuery = new Argument<string>("query");
        var symbolKinds = new Option<string[]>("--kind")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Limit results to one or more declaration kinds.",
        };
        var symbolNamespace = new Option<string?>("--namespace")
        {
            Description = "Limit results to a namespace and its descendants.",
        };
        var symbolProject = new Option<string?>("--project")
        {
            Description = "Limit results to one passively discovered project owner.",
        };
        var symbolPaths = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        var symbolAccessibilities = new Option<string[]>("--accessibility")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Limit results to one or more syntactic accessibility values.",
        };
        var symbolIncludeTests = new Option<bool>("--include-tests");
        var symbolIncludeGenerated = new Option<bool>("--include-generated");
        var symbolLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var symbolFull = new Option<bool>("--full");
        var symbolFields = new Option<string[]>("--fields")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        symbolCommand.Arguments.Add(symbolQuery);
        symbolCommand.Options.Add(symbolKinds);
        symbolCommand.Options.Add(symbolNamespace);
        symbolCommand.Options.Add(symbolProject);
        symbolCommand.Options.Add(symbolPaths);
        symbolCommand.Options.Add(symbolAccessibilities);
        symbolCommand.Options.Add(symbolIncludeTests);
        symbolCommand.Options.Add(symbolIncludeGenerated);
        symbolCommand.Options.Add(symbolLimit);
        symbolCommand.Options.Add(symbolFull);
        symbolCommand.Options.Add(symbolFields);
        host.RegisterCommand(searchCommand, symbolCommand, OperationPolicy.Passive,
            [
                "dnaxi search symbol Widget",
                "dnaxi search symbol Save --kind method --project src/App/App.csproj",
            ]);
        symbolCommand.BindHandler(
            result => SymbolSearchCommandRequest.Create(
                result.GetValue(symbolQuery)!,
                result.GetValue(symbolKinds) ?? [],
                result.GetValue(symbolNamespace),
                result.GetValue(symbolProject),
                result.GetValue(symbolPaths) ?? [],
                result.GetValue(symbolAccessibilities) ?? [],
                result.GetValue(symbolIncludeTests),
                result.GetValue(symbolIncludeGenerated),
                result.GetValue(symbolLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(symbolFull),
                result.GetValue(symbolFields) ?? []),
            static () => new SymbolSearchCommandHandler(),
            host.ResponseWriter);

        var showCommand = new Command(
            "show",
            "Show bounded detail for one stable evidence identity.");
        host.RegisterCommand(rootCommand, showCommand, OperationPolicy.Passive,
            [
                "dnaxi show symbol <symbol/v2/...>",
                "dnaxi show symbol <symbol/v2/...> --max-chars 2000",
            ]);

        var showSymbolCommand = new Command(
            "symbol",
            "Show one resolved C# declaration without loading a compilation.");
        var showSymbolId = new Argument<string>("symbol");
        var showSymbolPaths = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Reuse an explicit search scope, including external paths.",
        };
        var showSymbolMaxCharacters = new Option<int>("--max-chars")
        {
            DefaultValueFactory = static _ => 1000,
        };
        showSymbolCommand.Arguments.Add(showSymbolId);
        showSymbolCommand.Options.Add(showSymbolPaths);
        showSymbolCommand.Options.Add(showSymbolMaxCharacters);
        host.RegisterCommand(showCommand, showSymbolCommand, OperationPolicy.Passive,
            [
                "dnaxi show symbol <symbol/v2/...>",
                "dnaxi show symbol <symbol/v2/...> --max-chars 2000",
            ]);
        showSymbolCommand.BindHandler(
            result => SymbolShowCommandRequest.Create(
                result.GetValue(showSymbolId)!,
                result.GetValue(showSymbolPaths) ?? [],
                result.GetValue(showSymbolMaxCharacters)),
            static () => new SymbolShowCommandHandler(),
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
        var invocationVerify = new Option<bool>("--verify")
        {
            Description = "Verify candidates with compiler semantics in each owner/framework scope; executes repository design-time build targets.",
        };
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
        invocationCommand.Options.Add(invocationVerify);
        invocationCommand.Options.Add(invocationFields);
        invocationCommand.Options.Add(invocationPath);
        var invocationOperation = host.RegisterCommand(
            syntaxCommand,
            invocationCommand,
            OperationPolicy.Passive,
            [
                "dnaxi search syntax invocation --name SaveChangesAsync",
                "dnaxi search syntax invocation --name Map --path src --include-generated",
            ]);
        host.RegisterOptionPolicy(
            invocationOperation,
            invocationVerify,
            OperationPolicy.ExecutingInspection);
        invocationCommand.BindHandler(
            result => InvocationSyntaxCommandRequest.Create(
                result.GetValue(invocationName)!,
                result.GetValue(invocationIncludeGenerated),
                result.GetValue(invocationLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(invocationFull),
                result.GetValue(invocationVerify),
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
        var classVerify = new Option<bool>("--verify")
        {
            Description = "Verify candidates with compiler semantics in each owner/framework scope; executes repository design-time build targets.",
        };
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
        attributedClassCommand.Options.Add(classVerify);
        attributedClassCommand.Options.Add(classFields);
        attributedClassCommand.Options.Add(classPath);
        var classOperation = host.RegisterCommand(
            syntaxCommand,
            attributedClassCommand,
            OperationPolicy.Passive,
            [
                "dnaxi search syntax class --attribute Authorize",
                "dnaxi search syntax class --attribute Obsolete --path src --include-generated",
            ]);
        host.RegisterOptionPolicy(
            classOperation,
            classVerify,
            OperationPolicy.ExecutingInspection);
        attributedClassCommand.BindHandler(
            result => AttributedClassSyntaxCommandRequest.Create(
                result.GetValue(classAttribute)!,
                result.GetValue(classIncludeGenerated),
                result.GetValue(classLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(classFull),
                result.GetValue(classVerify),
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
        var objectCreationVerify = new Option<bool>("--verify")
        {
            Description = "Verify candidates with compiler semantics in each owner/framework scope; executes repository design-time build targets.",
        };
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
        objectCreationCommand.Options.Add(objectCreationVerify);
        objectCreationCommand.Options.Add(objectCreationFields);
        objectCreationCommand.Options.Add(objectCreationPath);
        var objectCreationOperation = host.RegisterCommand(
            syntaxCommand,
            objectCreationCommand,
            OperationPolicy.Passive,
            [
                "dnaxi search syntax object-creation --type HttpClient",
                "dnaxi search syntax object-creation --type Widget --path src --include-generated",
            ]);
        host.RegisterOptionPolicy(
            objectCreationOperation,
            objectCreationVerify,
            OperationPolicy.ExecutingInspection);
        objectCreationCommand.BindHandler(
            result => ObjectCreationSyntaxCommandRequest.Create(
                result.GetValue(objectCreationType)!,
                result.GetValue(objectCreationIncludeGenerated),
                result.GetValue(objectCreationLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(objectCreationFull),
                result.GetValue(objectCreationVerify),
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
        var catchVerify = new Option<bool>("--verify")
        {
            Description = "Verify candidates with compiler semantics in each owner/framework scope; executes repository design-time build targets.",
        };
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
        catchCommand.Options.Add(catchVerify);
        catchCommand.Options.Add(catchFields);
        catchCommand.Options.Add(catchPath);
        var catchOperation = host.RegisterCommand(
            syntaxCommand,
            catchCommand,
            OperationPolicy.Passive,
            [
                "dnaxi search syntax catch",
                "dnaxi search syntax catch --type Exception --empty --path src",
            ]);
        host.RegisterOptionPolicy(
            catchOperation,
            catchVerify,
            OperationPolicy.ExecutingInspection);
        catchCommand.BindHandler(
            result => CatchSyntaxCommandRequest.Create(
                result.GetValue(catchType),
                result.GetValue(catchEmpty),
                result.GetValue(catchIncludeGenerated),
                result.GetValue(catchLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(catchFull),
                result.GetValue(catchVerify),
                result.GetValue(catchFields) ?? [],
                result.GetValue(catchPath) ?? []),
            static () => new CatchSyntaxCommandHandler(),
            host.ResponseWriter);

        return host;
    }
}
