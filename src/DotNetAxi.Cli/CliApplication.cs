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
        var fileFields = CreateFieldsOption();
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
        var fields = CreateFieldsOption();
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
        var symbolSolution = new Option<string?>("--solution")
        {
            Description = "Select one passively discovered solution scope.",
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
        var symbolFields = CreateFieldsOption();
        symbolCommand.Arguments.Add(symbolQuery);
        symbolCommand.Options.Add(symbolKinds);
        symbolCommand.Options.Add(symbolNamespace);
        symbolCommand.Options.Add(symbolSolution);
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
                result.GetValue(symbolSolution),
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

        var referencesCommand = new Command(
            "references",
            "Find exact Roslyn references in dependency-aware project and framework scope.");
        var referencesTarget = new Argument<string>("symbol")
        {
            Description = "Canonical symbol/v2 ID, fully qualified name, or declaration query.",
        };
        var referencesSolution = new Option<string?>("--solution")
        {
            Description = "Select one solution scope.",
        };
        var referencesProject = new Option<string?>("--project")
        {
            Description = "Select one project and its evaluated dependency scope.",
        };
        var referencesIncludeTests = new Option<bool>("--include-tests");
        var referencesIncludeGenerated = new Option<bool>("--include-generated");
        var referencesConfiguration = new Option<string?>("--configuration")
        {
            Description = "Select one evaluated MSBuild configuration.",
        };
        var referencesFramework = new Option<string?>("--framework")
        {
            Description = "Select one declared target framework.",
        };
        var referencesProperties = new Option<string[]>("--property")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Set an MSBuild name=value property; repeat for additional properties.",
        };
        var referencesComplete = new Option<bool>("--complete")
        {
            Description = "Analyze the transitive reverse project graph and every supported framework variant.",
        };
        var referencesLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var referencesFull = new Option<bool>("--full");
        var referencesFields = CreateFieldsOption();
        referencesCommand.Arguments.Add(referencesTarget);
        referencesCommand.Options.Add(referencesSolution);
        referencesCommand.Options.Add(referencesProject);
        referencesCommand.Options.Add(referencesIncludeTests);
        referencesCommand.Options.Add(referencesIncludeGenerated);
        referencesCommand.Options.Add(referencesConfiguration);
        referencesCommand.Options.Add(referencesFramework);
        referencesCommand.Options.Add(referencesProperties);
        referencesCommand.Options.Add(referencesComplete);
        referencesCommand.Options.Add(referencesLimit);
        referencesCommand.Options.Add(referencesFull);
        referencesCommand.Options.Add(referencesFields);
        host.RegisterCommand(
            searchCommand,
            referencesCommand,
            OperationPolicy.ExecutingInspection,
            [
                "dnaxi search references <symbol/v2/...>",
                "dnaxi search references Demo.Service.Run --complete",
            ]);
        referencesCommand.BindHandler(
            result => ReferenceSearchCommandRequest.Create(
                result.GetValue(referencesTarget)!,
                result.GetValue(referencesSolution),
                result.GetValue(referencesProject),
                result.GetValue(referencesIncludeTests),
                result.GetValue(referencesIncludeGenerated),
                result.GetValue(referencesConfiguration),
                result.GetValue(referencesFramework),
                result.GetValue(referencesProperties) ?? [],
                result.GetValue(referencesComplete),
                result.GetValue(referencesLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(referencesFull),
                result.GetValue(referencesFields) ?? []),
            static () => new ReferenceSearchCommandHandler(),
            host.ResponseWriter);

        var callersCommand = new Command("callers", "Find compiler-verified call sites in dependency-aware project and framework scope.");
        var callersTarget = new Argument<string>("symbol") { Description = "Canonical symbol/v2 ID, fully qualified name, or declaration query." };
        var callersSolution = new Option<string?>("--solution") { Description = "Select one solution scope." };
        var callersProject = new Option<string?>("--project") { Description = "Select one project and its evaluated dependency scope." };
        var callersIncludeTests = new Option<bool>("--include-tests");
        var callersIncludeGenerated = new Option<bool>("--include-generated");
        var callersConfiguration = new Option<string?>("--configuration") { Description = "Select one evaluated MSBuild configuration." };
        var callersFramework = new Option<string?>("--framework") { Description = "Select one declared target framework." };
        var callersProperties = new Option<string[]>("--property") { AllowMultipleArgumentsPerToken = false, Description = "Set an MSBuild name=value property; repeat for additional properties." };
        var callersComplete = new Option<bool>("--complete") { Description = "Analyze the transitive reverse project graph and every supported framework variant." };
        var callersLimit = new Option<int>("--limit") { DefaultValueFactory = static _ => 100 };
        var callersFull = new Option<bool>("--full");
        var callersFields = CreateFieldsOption();
        callersCommand.Arguments.Add(callersTarget);
        callersCommand.Options.Add(callersSolution);
        callersCommand.Options.Add(callersProject);
        callersCommand.Options.Add(callersIncludeTests);
        callersCommand.Options.Add(callersIncludeGenerated);
        callersCommand.Options.Add(callersConfiguration);
        callersCommand.Options.Add(callersFramework);
        callersCommand.Options.Add(callersProperties);
        callersCommand.Options.Add(callersComplete);
        callersCommand.Options.Add(callersLimit);
        callersCommand.Options.Add(callersFull);
        callersCommand.Options.Add(callersFields);
        host.RegisterCommand(searchCommand, callersCommand, OperationPolicy.ExecutingInspection,
            ["dnaxi search callers <symbol/v2/...>", "dnaxi search callers Demo.Service.Run --complete"]);
        callersCommand.BindHandler(
            result => CallerSearchCommandRequest.Create(
                result.GetValue(callersTarget)!, result.GetValue(callersSolution), result.GetValue(callersProject),
                result.GetValue(callersIncludeTests), result.GetValue(callersIncludeGenerated),
                result.GetValue(callersConfiguration), result.GetValue(callersFramework), result.GetValue(callersProperties) ?? [],
                result.GetValue(callersComplete), result.GetValue(callersLimit),
                result.Tokens.Any(token => token.Value == "--limit"), result.GetValue(callersFull), result.GetValue(callersFields) ?? []),
            static () => new CallerSearchCommandHandler(), host.ResponseWriter);

        var calleesCommand = new Command("callees", "Find compiler-known targets invoked by one selected member.");
        var calleesTarget = new Argument<string>("symbol") { Description = "Canonical symbol/v2 ID, fully qualified name, or declaration query." };
        var calleesSolution = new Option<string?>("--solution") { Description = "Select one solution scope." };
        var calleesProject = new Option<string?>("--project") { Description = "Select one project and its evaluated framework variants." };
        var calleesIncludeTests = new Option<bool>("--include-tests");
        var calleesIncludeGenerated = new Option<bool>("--include-generated");
        var calleesConfiguration = new Option<string?>("--configuration") { Description = "Select one evaluated MSBuild configuration." };
        var calleesFramework = new Option<string?>("--framework") { Description = "Select one declared target framework." };
        var calleesProperties = new Option<string[]>("--property") { AllowMultipleArgumentsPerToken = false, Description = "Set an MSBuild name=value property; repeat for additional properties." };
        var calleesComplete = new Option<bool>("--complete") { Description = "Analyze every supported framework variant of the target project." };
        var calleesLimit = new Option<int>("--limit") { DefaultValueFactory = static _ => 100 };
        var calleesFull = new Option<bool>("--full");
        var calleesFields = CreateFieldsOption();
        calleesCommand.Arguments.Add(calleesTarget);
        calleesCommand.Options.Add(calleesSolution);
        calleesCommand.Options.Add(calleesProject);
        calleesCommand.Options.Add(calleesIncludeTests);
        calleesCommand.Options.Add(calleesIncludeGenerated);
        calleesCommand.Options.Add(calleesConfiguration);
        calleesCommand.Options.Add(calleesFramework);
        calleesCommand.Options.Add(calleesProperties);
        calleesCommand.Options.Add(calleesComplete);
        calleesCommand.Options.Add(calleesLimit);
        calleesCommand.Options.Add(calleesFull);
        calleesCommand.Options.Add(calleesFields);
        host.RegisterCommand(searchCommand, calleesCommand, OperationPolicy.ExecutingInspection,
            ["dnaxi search callees <symbol/v2/...>", "dnaxi search callees Demo.Service.Run --complete"]);
        calleesCommand.BindHandler(
            result => CalleeSearchCommandRequest.Create(
                result.GetValue(calleesTarget)!, result.GetValue(calleesSolution), result.GetValue(calleesProject),
                result.GetValue(calleesIncludeTests), result.GetValue(calleesIncludeGenerated),
                result.GetValue(calleesConfiguration), result.GetValue(calleesFramework), result.GetValue(calleesProperties) ?? [],
                result.GetValue(calleesComplete), result.GetValue(calleesLimit),
                result.Tokens.Any(token => token.Value == "--limit"), result.GetValue(calleesFull), result.GetValue(calleesFields) ?? []),
            static () => new CalleeSearchCommandHandler(), host.ResponseWriter);

        var implementationsCommand = new Command(
            "implementations",
            "Find exact compiler implementations in dependency-aware project and framework scope.");
        var implementationsTarget = new Argument<string>("symbol")
        {
            Description = "Canonical symbol/v2 ID, fully qualified name, or declaration query.",
        };
        var implementationsSolution = new Option<string?>("--solution")
        {
            Description = "Select one solution scope.",
        };
        var implementationsProject = new Option<string?>("--project")
        {
            Description = "Select one project and its evaluated dependency scope.",
        };
        var implementationsIncludeTests = new Option<bool>("--include-tests");
        var implementationsIncludeGenerated = new Option<bool>("--include-generated");
        var implementationsConfiguration = new Option<string?>("--configuration")
        {
            Description = "Select one evaluated MSBuild configuration.",
        };
        var implementationsFramework = new Option<string?>("--framework")
        {
            Description = "Select one declared target framework.",
        };
        var implementationsProperties = new Option<string[]>("--property")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Set an MSBuild name=value property; repeat for additional properties.",
        };
        var implementationsComplete = new Option<bool>("--complete")
        {
            Description = "Analyze the transitive reverse project graph and every supported framework variant.",
        };
        var implementationsLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var implementationsFull = new Option<bool>("--full");
        var implementationsFields = CreateFieldsOption();
        implementationsCommand.Arguments.Add(implementationsTarget);
        implementationsCommand.Options.Add(implementationsSolution);
        implementationsCommand.Options.Add(implementationsProject);
        implementationsCommand.Options.Add(implementationsIncludeTests);
        implementationsCommand.Options.Add(implementationsIncludeGenerated);
        implementationsCommand.Options.Add(implementationsConfiguration);
        implementationsCommand.Options.Add(implementationsFramework);
        implementationsCommand.Options.Add(implementationsProperties);
        implementationsCommand.Options.Add(implementationsComplete);
        implementationsCommand.Options.Add(implementationsLimit);
        implementationsCommand.Options.Add(implementationsFull);
        implementationsCommand.Options.Add(implementationsFields);
        host.RegisterCommand(
            searchCommand,
            implementationsCommand,
            OperationPolicy.ExecutingInspection,
            [
                "dnaxi search implementations <symbol/v2/...>",
                "dnaxi search implementations Demo.Service.Run --complete",
            ]);
        implementationsCommand.BindHandler(
            result => ImplementationSearchCommandRequest.Create(
                result.GetValue(implementationsTarget)!,
                result.GetValue(implementationsSolution),
                result.GetValue(implementationsProject),
                result.GetValue(implementationsIncludeTests),
                result.GetValue(implementationsIncludeGenerated),
                result.GetValue(implementationsConfiguration),
                result.GetValue(implementationsFramework),
                result.GetValue(implementationsProperties) ?? [],
                result.GetValue(implementationsComplete),
                result.GetValue(implementationsLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(implementationsFull),
                result.GetValue(implementationsFields) ?? []),
            static () => new ImplementationSearchCommandHandler(),
            host.ResponseWriter);

        var derivedCommand = new Command(
            "derived",
            "Find exact compiler-derived source types in dependency-aware project and framework scope.");
        var derivedTarget = new Argument<string>("symbol")
        {
            Description = "Canonical symbol/v2 ID, fully qualified name, or declaration query.",
        };
        var derivedSolution = new Option<string?>("--solution");
        var derivedProject = new Option<string?>("--project");
        var derivedIncludeTests = new Option<bool>("--include-tests");
        var derivedIncludeGenerated = new Option<bool>("--include-generated");
        var derivedConfiguration = new Option<string?>("--configuration");
        var derivedFramework = new Option<string?>("--framework");
        var derivedProperties = new Option<string[]>("--property") { AllowMultipleArgumentsPerToken = false };
        var derivedComplete = new Option<bool>("--complete");
        var derivedLimit = new Option<int>("--limit") { DefaultValueFactory = static _ => 100 };
        var derivedFull = new Option<bool>("--full");
        var derivedFields = CreateFieldsOption();
        derivedCommand.Arguments.Add(derivedTarget);
        derivedCommand.Options.Add(derivedSolution);
        derivedCommand.Options.Add(derivedProject);
        derivedCommand.Options.Add(derivedIncludeTests);
        derivedCommand.Options.Add(derivedIncludeGenerated);
        derivedCommand.Options.Add(derivedConfiguration);
        derivedCommand.Options.Add(derivedFramework);
        derivedCommand.Options.Add(derivedProperties);
        derivedCommand.Options.Add(derivedComplete);
        derivedCommand.Options.Add(derivedLimit);
        derivedCommand.Options.Add(derivedFull);
        derivedCommand.Options.Add(derivedFields);
        host.RegisterCommand(searchCommand, derivedCommand, OperationPolicy.ExecutingInspection,
            ["dnaxi search derived <symbol/v2/...>", "dnaxi search derived Demo.Base --complete"]);
        derivedCommand.BindHandler(
            result => DerivedTypeSearchCommandRequest.Create(
                result.GetValue(derivedTarget)!, result.GetValue(derivedSolution),
                result.GetValue(derivedProject), result.GetValue(derivedIncludeTests),
                result.GetValue(derivedIncludeGenerated), result.GetValue(derivedConfiguration),
                result.GetValue(derivedFramework), result.GetValue(derivedProperties) ?? [],
                result.GetValue(derivedComplete), result.GetValue(derivedLimit),
                result.Tokens.Any(token => token.Value == "--limit"), result.GetValue(derivedFull),
                result.GetValue(derivedFields) ?? []),
            static () => new DerivedTypeSearchCommandHandler(),
            host.ResponseWriter);

        var overridesCommand = new Command("overrides", "Find exact compiler override relationships.");
        var overridesTarget = new Argument<string>("symbol");
        var overridesSolution = new Option<string?>("--solution");
        var overridesProject = new Option<string?>("--project");
        var overridesTests = new Option<bool>("--include-tests");
        var overridesGenerated = new Option<bool>("--include-generated");
        var overridesConfiguration = new Option<string?>("--configuration");
        var overridesFramework = new Option<string?>("--framework");
        var overridesProperties = new Option<string[]>("--property") { AllowMultipleArgumentsPerToken = false };
        var overridesComplete = new Option<bool>("--complete");
        var overridesLimit = new Option<int>("--limit") { DefaultValueFactory = static _ => 100 };
        var overridesFull = new Option<bool>("--full");
        overridesCommand.Arguments.Add(overridesTarget);
        foreach (var option in new Option[] { overridesSolution, overridesProject, overridesTests, overridesGenerated, overridesConfiguration, overridesFramework, overridesProperties, overridesComplete, overridesLimit, overridesFull }) overridesCommand.Options.Add(option);
        host.RegisterCommand(searchCommand, overridesCommand, OperationPolicy.ExecutingInspection,
            ["dnaxi search overrides <symbol/v2/...>", "dnaxi search overrides Demo.Base.Run --complete"]);
        overridesCommand.BindHandler(result => OverrideSearchCommandRequest.Create(result.GetValue(overridesTarget)!, result.GetValue(overridesSolution), result.GetValue(overridesProject), result.GetValue(overridesTests), result.GetValue(overridesGenerated), result.GetValue(overridesConfiguration), result.GetValue(overridesFramework), result.GetValue(overridesProperties) ?? [], result.GetValue(overridesComplete), result.GetValue(overridesLimit), result.Tokens.Any(token => token.Value == "--limit"), result.GetValue(overridesFull)), static () => new OverrideSearchCommandHandler(), host.ResponseWriter);

        var graphCommand = new Command(
            "graph",
            "Inspect the evaluated project dependency graph.");
        host.RegisterCommand(rootCommand, graphCommand, OperationPolicy.ExecutingInspection,
            [
                "dnaxi graph projects",
                "dnaxi graph dependencies src/App/App.csproj",
            ]);

        var graphProjectsCommand = new Command(
            "projects",
            "Return the selected evaluated project graph.");
        var graphProjectsSolution = new Option<string?>("--solution");
        var graphProjectsProject = new Option<string?>("--project");
        var graphProjectsConfiguration = new Option<string?>("--configuration");
        var graphProjectsFramework = new Option<string?>("--framework");
        var graphProjectsProperties = new Option<string[]>("--property")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        var graphProjectsLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var graphProjectsFull = new Option<bool>("--full");
        graphProjectsCommand.Options.Add(graphProjectsSolution);
        graphProjectsCommand.Options.Add(graphProjectsProject);
        graphProjectsCommand.Options.Add(graphProjectsConfiguration);
        graphProjectsCommand.Options.Add(graphProjectsFramework);
        graphProjectsCommand.Options.Add(graphProjectsProperties);
        graphProjectsCommand.Options.Add(graphProjectsLimit);
        graphProjectsCommand.Options.Add(graphProjectsFull);
        host.RegisterCommand(graphCommand, graphProjectsCommand, OperationPolicy.ExecutingInspection,
            [
                "dnaxi graph projects",
                "dnaxi graph projects --solution src/App.sln --framework net10.0",
            ]);
        graphProjectsCommand.BindHandler(
            result => ProjectGraphCommandRequest.Create(
                dependencyProject: null,
                result.GetValue(graphProjectsSolution),
                result.GetValue(graphProjectsProject),
                result.GetValue(graphProjectsConfiguration),
                result.GetValue(graphProjectsFramework),
                result.GetValue(graphProjectsProperties) ?? [],
                result.GetValue(graphProjectsLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(graphProjectsFull)),
            static () => new ProjectGraphCommandHandler(),
            host.ResponseWriter);

        var graphDependenciesCommand = new Command(
            "dependencies",
            "Return outgoing project and package dependencies for one evaluated project.");
        var graphDependenciesProjectArgument = new Argument<string>("project");
        var graphDependenciesSolution = new Option<string?>("--solution");
        var graphDependenciesEntryProject = new Option<string?>("--project");
        var graphDependenciesConfiguration = new Option<string?>("--configuration");
        var graphDependenciesFramework = new Option<string?>("--framework");
        var graphDependenciesProperties = new Option<string[]>("--property")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        var graphDependenciesLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var graphDependenciesFull = new Option<bool>("--full");
        graphDependenciesCommand.Arguments.Add(graphDependenciesProjectArgument);
        graphDependenciesCommand.Options.Add(graphDependenciesSolution);
        graphDependenciesCommand.Options.Add(graphDependenciesEntryProject);
        graphDependenciesCommand.Options.Add(graphDependenciesConfiguration);
        graphDependenciesCommand.Options.Add(graphDependenciesFramework);
        graphDependenciesCommand.Options.Add(graphDependenciesProperties);
        graphDependenciesCommand.Options.Add(graphDependenciesLimit);
        graphDependenciesCommand.Options.Add(graphDependenciesFull);
        host.RegisterCommand(graphCommand, graphDependenciesCommand, OperationPolicy.ExecutingInspection,
            [
                "dnaxi graph dependencies src/App/App.csproj",
                "dnaxi graph dependencies src/App/App.csproj --framework net10.0",
            ]);
        graphDependenciesCommand.BindHandler(
            result => ProjectGraphCommandRequest.Create(
                result.GetValue(graphDependenciesProjectArgument),
                result.GetValue(graphDependenciesSolution),
                result.GetValue(graphDependenciesEntryProject),
                result.GetValue(graphDependenciesConfiguration),
                result.GetValue(graphDependenciesFramework),
                result.GetValue(graphDependenciesProperties) ?? [],
                result.GetValue(graphDependenciesLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(graphDependenciesFull)),
            static () => new ProjectGraphCommandHandler(),
            host.ResponseWriter);

        var graphCyclesCommand = new Command(
            "cycles",
            "Return normalized cycles in the evaluated project-reference graph.");
        var graphCyclesSolution = new Option<string?>("--solution");
        var graphCyclesProject = new Option<string?>("--project");
        var graphCyclesConfiguration = new Option<string?>("--configuration");
        var graphCyclesFramework = new Option<string?>("--framework");
        var graphCyclesProperties = new Option<string[]>("--property")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        var graphCyclesLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
        };
        var graphCyclesFull = new Option<bool>("--full");
        graphCyclesCommand.Options.Add(graphCyclesSolution);
        graphCyclesCommand.Options.Add(graphCyclesProject);
        graphCyclesCommand.Options.Add(graphCyclesConfiguration);
        graphCyclesCommand.Options.Add(graphCyclesFramework);
        graphCyclesCommand.Options.Add(graphCyclesProperties);
        graphCyclesCommand.Options.Add(graphCyclesLimit);
        graphCyclesCommand.Options.Add(graphCyclesFull);
        host.RegisterCommand(graphCommand, graphCyclesCommand, OperationPolicy.ExecutingInspection,
            [
                "dnaxi graph cycles",
                "dnaxi graph cycles --solution src/App.sln --framework net10.0",
            ]);
        graphCyclesCommand.BindHandler(
            result => ProjectCycleCommandRequest.Create(
                result.GetValue(graphCyclesSolution),
                result.GetValue(graphCyclesProject),
                result.GetValue(graphCyclesConfiguration),
                result.GetValue(graphCyclesFramework),
                result.GetValue(graphCyclesProperties) ?? [],
                result.GetValue(graphCyclesLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(graphCyclesFull)),
            static () => new ProjectCycleCommandHandler(),
            host.ResponseWriter);

        var graphPathCommand = new Command("path", "Find bounded shortest paths between evaluated projects.");
        var graphPathFrom = new Option<string>("--from") { Required = true };
        var graphPathTo = new Option<string>("--to") { Required = true };
        var graphPathDepth = new Option<int>("--max-depth") { DefaultValueFactory = static _ => 10 };
        var graphPathSolution = new Option<string?>("--solution");
        var graphPathProject = new Option<string?>("--project");
        var graphPathConfiguration = new Option<string?>("--configuration");
        var graphPathFramework = new Option<string?>("--framework");
        var graphPathProperties = new Option<string[]>("--property") { AllowMultipleArgumentsPerToken = false };
        var graphPathLimit = new Option<int>("--limit") { DefaultValueFactory = static _ => 100 };
        var graphPathFull = new Option<bool>("--full");
        foreach (var option in new Option[] { graphPathFrom, graphPathTo, graphPathDepth, graphPathSolution, graphPathProject, graphPathConfiguration, graphPathFramework, graphPathProperties, graphPathLimit, graphPathFull }) graphPathCommand.Options.Add(option);
        host.RegisterCommand(graphCommand, graphPathCommand, OperationPolicy.ExecutingInspection,
            ["dnaxi graph path --from src/App/App.csproj --to src/Core/Core.csproj", "dnaxi graph path --from src/App/App.csproj --to src/Core/Core.csproj --max-depth 4"]);
        graphPathCommand.BindHandler(result => ProjectPathCommandRequest.Create(
            result.GetValue(graphPathFrom)!, result.GetValue(graphPathTo)!, result.GetValue(graphPathDepth),
            result.GetValue(graphPathSolution), result.GetValue(graphPathProject), result.GetValue(graphPathConfiguration), result.GetValue(graphPathFramework),
            result.GetValue(graphPathProperties) ?? [], result.GetValue(graphPathLimit), result.Tokens.Any(token => token.Value == "--limit"), result.GetValue(graphPathFull)),
            static () => new ProjectPathCommandHandler(), host.ResponseWriter);

        var showCommand = new Command(
            "show",
            "Show bounded detail for one stable evidence identity or document.");
        host.RegisterCommand(rootCommand, showCommand, OperationPolicy.Passive,
            [
                "dnaxi show symbol <symbol/v2/...>",
                "dnaxi show symbol <symbol/v2/...> --max-chars 2000",
                "dnaxi show document src/App/Service.cs",
            ]);

        var showSymbolCommand = new Command(
            "symbol",
            "Show one resolved C# declaration without loading a compilation.");
        var showSymbolId = new Argument<string>("symbol");
        var showSymbolSolution = new Option<string?>("--solution")
        {
            Description = "Reuse the selected solution scope.",
        };
        var showSymbolProject = new Option<string?>("--project")
        {
            Description = "Reuse the selected project scope.",
        };
        var showSymbolPaths = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Reuse an explicit search scope, including external paths.",
        };
        var showSymbolIncludeTests = new Option<bool>("--include-tests")
        {
            Description = "Resolve declarations classified as test-only.",
        };
        var showSymbolIncludeGenerated = new Option<bool>("--include-generated")
        {
            Description = "Resolve declarations classified as generated source.",
        };
        var showSymbolMaxCharacters = new Option<int>("--max-chars")
        {
            DefaultValueFactory = static _ => 1000,
        };
        showSymbolCommand.Arguments.Add(showSymbolId);
        showSymbolCommand.Options.Add(showSymbolSolution);
        showSymbolCommand.Options.Add(showSymbolProject);
        showSymbolCommand.Options.Add(showSymbolPaths);
        showSymbolCommand.Options.Add(showSymbolIncludeTests);
        showSymbolCommand.Options.Add(showSymbolIncludeGenerated);
        showSymbolCommand.Options.Add(showSymbolMaxCharacters);
        host.RegisterCommand(showCommand, showSymbolCommand, OperationPolicy.Passive,
            [
                "dnaxi show symbol <symbol/v2/...>",
                "dnaxi show symbol <symbol/v2/...> --max-chars 2000",
            ]);
        showSymbolCommand.BindHandler(
            result => SymbolShowCommandRequest.Create(
                result.GetValue(showSymbolId)!,
                result.GetValue(showSymbolSolution),
                result.GetValue(showSymbolProject),
                result.GetValue(showSymbolPaths) ?? [],
                result.GetValue(showSymbolIncludeTests),
                result.GetValue(showSymbolIncludeGenerated),
                result.GetValue(showSymbolMaxCharacters)),
            static () => new SymbolShowCommandHandler(),
            host.ResponseWriter);

        var showDocumentCommand = new Command(
            "document",
            "Show one bounded text document with identity and ownership evidence.");
        var showDocumentPath = new Argument<string>("path")
        {
            Description = "One explicit workspace or external document path.",
        };
        var showDocumentIncludeGenerated = new Option<bool>(
            "--include-generated")
        {
            Description = "Include a document classified as generated source.",
        };
        var showDocumentMaxCharacters = new Option<int>("--max-chars")
        {
            Description = "Limit the preview by Unicode scalar values.",
            DefaultValueFactory = static _ => 1000,
        };
        var showDocumentStartLine = new Option<int?>("--start-line")
        {
            Description = "Start at this one-based inclusive line.",
        };
        var showDocumentEndLine = new Option<int?>("--end-line")
        {
            Description = "End at this one-based inclusive line.",
        };
        var showDocumentFull = new Option<bool>("--full")
        {
            Description = "Return the complete document without a character limit.",
        };
        showDocumentCommand.Arguments.Add(showDocumentPath);
        showDocumentCommand.Options.Add(showDocumentIncludeGenerated);
        showDocumentCommand.Options.Add(showDocumentMaxCharacters);
        showDocumentCommand.Options.Add(showDocumentStartLine);
        showDocumentCommand.Options.Add(showDocumentEndLine);
        showDocumentCommand.Options.Add(showDocumentFull);
        host.RegisterCommand(
            showCommand,
            showDocumentCommand,
            OperationPolicy.Passive,
            [
                "dnaxi show document src/App/Service.cs",
                "dnaxi show document src/App/Service.cs --start-line 40 --end-line 80",
                "dnaxi show document Generated.g.cs --include-generated --full",
            ]);
        showDocumentCommand.BindHandler(
            result => DocumentShowCommandRequest.Create(
                result.GetValue(showDocumentPath)!,
                result.GetValue(showDocumentIncludeGenerated),
                result.GetValue(showDocumentMaxCharacters),
                result.Tokens.Any(token => token.Value == "--max-chars"),
                result.GetValue(showDocumentStartLine),
                result.GetValue(showDocumentEndLine),
                result.GetValue(showDocumentFull)),
            static () => new DocumentShowCommandHandler(),
            host.ResponseWriter);

        var contextCommand = new Command(
            "context",
            "Compose bounded evidence for one coding-agent decision.");
        host.RegisterCommand(
            rootCommand,
            contextCommand,
            OperationPolicy.Passive,
            [
                "dnaxi context symbol <symbol/v2/...>",
                "dnaxi context symbol <symbol/v2/...> --include declaration,owner",
            ]);
        var contextSymbolCommand = new Command(
            "symbol",
            "Compose declaration, owner, document, and outline evidence once.");
        var contextSymbolId = new Argument<string>("symbol");
        var contextSymbolSolution = new Option<string?>("--solution")
        {
            Description = "Reuse the selected solution scope.",
        };
        var contextSymbolProject = new Option<string?>("--project")
        {
            Description = "Reuse the selected project scope.",
        };
        var contextSymbolPaths = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Reuse an explicit search scope, including external paths.",
        };
        var contextSymbolIncludeTests = new Option<bool>("--include-tests")
        {
            Description = "Resolve declarations classified as test-only.",
        };
        var contextSymbolIncludeGenerated = new Option<bool>("--include-generated")
        {
            Description = "Resolve declarations classified as generated source.",
        };
        var contextSymbolSections = new Option<string[]>("--include")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Sections: declaration, owner, document, outline. Defaults to all four.",
        };
        var contextSymbolMaxCharacters = new Option<int>("--max-chars")
        {
            DefaultValueFactory = static _ => 12000,
            Description = "Limit included whole sections by emitted Unicode scalar values.",
        };
        var contextSymbolFull = new Option<bool>("--full")
        {
            Description = "Return every requested section without a character limit.",
        };
        contextSymbolCommand.Arguments.Add(contextSymbolId);
        contextSymbolCommand.Options.Add(contextSymbolSolution);
        contextSymbolCommand.Options.Add(contextSymbolProject);
        contextSymbolCommand.Options.Add(contextSymbolPaths);
        contextSymbolCommand.Options.Add(contextSymbolIncludeTests);
        contextSymbolCommand.Options.Add(contextSymbolIncludeGenerated);
        contextSymbolCommand.Options.Add(contextSymbolSections);
        contextSymbolCommand.Options.Add(contextSymbolMaxCharacters);
        contextSymbolCommand.Options.Add(contextSymbolFull);
        host.RegisterCommand(
            contextCommand,
            contextSymbolCommand,
            OperationPolicy.Passive,
            [
                "dnaxi context symbol <symbol/v2/...>",
                "dnaxi context symbol <symbol/v2/...> --include declaration,owner --max-chars 4000",
                "dnaxi context symbol <symbol/v2/...> --full",
            ]);
        contextSymbolCommand.BindHandler(
            result => ContextSymbolCommandRequest.Create(
                result.GetValue(contextSymbolId)!,
                result.GetValue(contextSymbolSolution),
                result.GetValue(contextSymbolProject),
                result.GetValue(contextSymbolPaths) ?? [],
                result.GetValue(contextSymbolIncludeTests),
                result.GetValue(contextSymbolIncludeGenerated),
                result.GetValue(contextSymbolSections) ?? [],
                result.GetValue(contextSymbolMaxCharacters),
                result.Tokens.Any(token => token.Value == "--max-chars"),
                result.GetValue(contextSymbolFull)),
            static () => new ContextSymbolCommandHandler(),
            host.ResponseWriter);

        var outlineCommand = new Command(
            "outline",
            "Show the stable Roslyn syntax structure of one C# document or symbol.");
        var outlineTarget = new Argument<string>("path-or-symbol")
        {
            Description = "One explicit C# document path or canonical symbol/v2 identity.",
        };
        var outlineSolution = new Option<string?>("--solution")
        {
            Description = "Reuse the selected solution scope for a symbol target.",
        };
        var outlineProject = new Option<string?>("--project")
        {
            Description = "Reuse the selected project scope for a symbol target.",
        };
        var outlinePaths = new Option<string[]>("--path")
        {
            AllowMultipleArgumentsPerToken = false,
            Description = "Reuse an explicit symbol-search scope, including external paths.",
        };
        var outlineIncludeTests = new Option<bool>("--include-tests")
        {
            Description = "Resolve test-only declarations for a symbol target.",
        };
        var outlineIncludeGenerated = new Option<bool>("--include-generated")
        {
            Description = "Include generated C# source explicitly.",
        };
        var outlineLimit = new Option<int>("--limit")
        {
            DefaultValueFactory = static _ => 100,
            Description = "Limit the number of source-ordered outline items.",
        };
        var outlineFull = new Option<bool>("--full")
        {
            Description = "Return every outline item without a count limit.",
        };
        outlineCommand.Arguments.Add(outlineTarget);
        outlineCommand.Options.Add(outlineSolution);
        outlineCommand.Options.Add(outlineProject);
        outlineCommand.Options.Add(outlinePaths);
        outlineCommand.Options.Add(outlineIncludeTests);
        outlineCommand.Options.Add(outlineIncludeGenerated);
        outlineCommand.Options.Add(outlineLimit);
        outlineCommand.Options.Add(outlineFull);
        host.RegisterCommand(
            rootCommand,
            outlineCommand,
            OperationPolicy.Passive,
            [
                "dnaxi outline src/App/Service.cs",
                "dnaxi outline <symbol/v2/...> --full",
                "dnaxi outline Generated.g.cs --include-generated --limit 200",
            ]);
        outlineCommand.BindHandler(
            result => OutlineCommandRequest.Create(
                result.GetValue(outlineTarget)!,
                result.GetValue(outlineSolution),
                result.GetValue(outlineProject),
                result.GetValue(outlinePaths) ?? [],
                result.GetValue(outlineIncludeTests),
                result.GetValue(outlineIncludeGenerated),
                result.GetValue(outlineLimit),
                result.Tokens.Any(token => token.Value == "--limit"),
                result.GetValue(outlineFull)),
            static () => new OutlineCommandHandler(),
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
        var invocationFields = CreateFieldsOption();
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
        var classFields = CreateFieldsOption();
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
        var objectCreationFields = CreateFieldsOption();
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
        var catchFields = CreateFieldsOption();
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

    private static Option<string[]> CreateFieldsOption() =>
        new("--fields")
        {
            AllowMultipleArgumentsPerToken = true,
            Description =
                "Select output fields. Compact: --fields id,external. "
                + "Repeated: --fields id --fields external. "
                + "Multi-value: --fields id external.",
        };
}
