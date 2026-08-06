using DotNetAxi.Axi;
using DotNetAxi.Contracts;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record HomeRequest
{
    public static HomeRequest Instance { get; } = new();
}

internal sealed record HomeInvocationContext
{
    public HomeInvocationContext(
        string currentDirectory,
        string executablePath,
        string? homeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        CurrentDirectory = currentDirectory;
        ExecutablePath = executablePath;
        HomeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
            ? null
            : homeDirectory;
    }

    public string CurrentDirectory { get; }

    public string ExecutablePath { get; }

    public string? HomeDirectory { get; }

    public static HomeInvocationContext Capture()
    {
        var commandLinePath = Environment.GetCommandLineArgs()
            .FirstOrDefault(static argument =>
                !string.IsNullOrWhiteSpace(argument));
        var processPath = Environment.ProcessPath;
        var executablePath = processPath is not null
            && !Path.GetFileNameWithoutExtension(processPath).Equals(
                "dotnet",
                StringComparison.OrdinalIgnoreCase)
                ? processPath
                : commandLinePath ?? processPath ?? "dnaxi";
        return new HomeInvocationContext(
            Directory.GetCurrentDirectory(),
            executablePath,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
}

internal sealed class HomeCommandHandler : ICommandHandler<HomeRequest>
{
    private const string Unknown = "unknown";

    private static readonly SuggestionTemplate HelpSuggestion = new(
        priority: 10,
        [SuggestionToken.Literal("--help")]);

    private readonly HomeInvocationContext _context;
    private readonly WorkspaceDiscoverer _workspaceDiscoverer;
    private readonly WorkspaceEntryPointSelector _entryPointSelector;
    private readonly WorktreeStateInspector _worktreeStateInspector;
    private readonly ICapabilityReporter _capabilityReporter;

    public HomeCommandHandler(
        HomeInvocationContext context,
        WorkspaceDiscoverer workspaceDiscoverer,
        WorkspaceEntryPointSelector entryPointSelector,
        WorktreeStateInspector worktreeStateInspector,
        ICapabilityReporter capabilityReporter)
    {
        _context = context
            ?? throw new ArgumentNullException(nameof(context));
        _workspaceDiscoverer = workspaceDiscoverer
            ?? throw new ArgumentNullException(nameof(workspaceDiscoverer));
        _entryPointSelector = entryPointSelector
            ?? throw new ArgumentNullException(nameof(entryPointSelector));
        _worktreeStateInspector = worktreeStateInspector
            ?? throw new ArgumentNullException(nameof(worktreeStateInspector));
        _capabilityReporter = capabilityReporter
            ?? throw new ArgumentNullException(nameof(capabilityReporter));
    }

    public async ValueTask<ICommandResult> HandleAsync(
        HomeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = _workspaceDiscoverer.Discover(
            _context.CurrentDirectory);
        var selection = SelectEntryPoint(workspace);
        var worktreeTask = _worktreeStateInspector
            .InspectAsync(workspace, cancellationToken);
        var capabilitiesTask = _capabilityReporter
            .ReportAsync(workspace.RootPath, cancellationToken)
            .AsTask();
        await Task.WhenAll(worktreeTask, capabilitiesTask)
            .ConfigureAwait(false);
        var worktree = await worktreeTask.ConfigureAwait(false);
        var capabilities = await capabilitiesTask.ConfigureAwait(false);
        var git = CreateGitPayload(worktree);
        var suggestions = CreateSuggestions();

        return CommandResult<HomePayload>.Success(
            "home",
            new HomePayload(
                DisplayPath(
                    _context.ExecutablePath,
                    _context.CurrentDirectory,
                    _context.HomeDirectory),
                "Search, analyze, validate, and safely change the current .NET workspace",
                "dotnet-axi",
                ToolVersion.Current,
                OutputSchema.Current,
                AgentGuidanceCatalog.Command,
                new HomeWorkspacePayload(
                    DisplayPath(
                        workspace.RootPath,
                        _context.CurrentDirectory,
                        _context.HomeDirectory),
                    selection.Solution,
                    selection.Project,
                    workspace.Projects.Count,
                    workspace.CSharpFileCount),
                git,
                capabilities,
                new HomeAnalysisPayload(
                    HomeAnalysisStatus.NotLoaded,
                    HomeCompilerErrorState.Unknown)),
            suggestions: suggestions);
    }

    private HomeSelection SelectEntryPoint(
        WorkspaceDiscoveryResult workspace)
    {
        try
        {
            var selection = _entryPointSelector.Select(workspace);
            return selection.Kind switch
            {
                WorkspaceEntryPointKind.Solution => new HomeSelection(
                    selection.Path,
                    Project: null),
                WorkspaceEntryPointKind.Project => new HomeSelection(
                    Solution: null,
                    selection.Path),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(workspace),
                    selection.Kind,
                    "The workspace entry-point kind is not defined."),
            };
        }
        catch (WorkspaceSelectionUsageException exception)
        {
            var candidate = exception.CandidatePaths.FirstOrDefault();
            var candidateKind = CandidateKind(workspace, candidate);
            if (candidateKind is WorkspaceEntryPointKind.Solution)
            {
                return new HomeSelection(
                    Unknown,
                    Project: null);
            }

            if (candidateKind is WorkspaceEntryPointKind.Project)
            {
                return new HomeSelection(
                    Solution: null,
                    Unknown);
            }

            return new HomeSelection(
                Unknown,
                Project: null);
        }
    }

    private static WorkspaceEntryPointKind? CandidateKind(
        WorkspaceDiscoveryResult workspace,
        string? candidate)
    {
        if (candidate is null)
        {
            return null;
        }

        if (workspace.Solutions.Any(solution => solution.Path.Equals(
                candidate,
                PathComparison())))
        {
            return WorkspaceEntryPointKind.Solution;
        }

        return workspace.Projects.Any(project => project.Path.Equals(
            candidate,
            PathComparison()))
                ? WorkspaceEntryPointKind.Project
                : null;
    }

    private static HomeGitPayload CreateGitPayload(
        WorktreeStateResult result)
    {
        if (result.Outcome is not WorktreeInspectionOutcome.Available
            || result.State is null)
        {
            return new HomeGitPayload(Unknown, Unknown);
        }

        var branch = result.State.Head.Kind switch
        {
            GitHeadKind.Branch or GitHeadKind.Unborn =>
                result.State.Head.BranchName ?? Unknown,
            GitHeadKind.Detached => "detached",
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.State.Head.Kind,
                "The Git head kind is not defined."),
        };
        return new HomeGitPayload(branch, result.State.Entries.Count);
    }

    private static IReadOnlyList<ResultSuggestion> CreateSuggestions() =>
        ContextualSuggestions.Compose(
            [HelpSuggestion],
            WorkspaceSelectors.Empty);

    private static string DisplayPath(
        string path,
        string basePath,
        string? homeDirectory)
    {
        var fullPath = Path.GetFullPath(path, basePath);
        if (homeDirectory is not null)
        {
            var homePath = Path.GetFullPath(homeDirectory);
            var relativeToHome = Path.GetRelativePath(homePath, fullPath);
            if (relativeToHome.Equals(".", StringComparison.Ordinal))
            {
                return "~";
            }

            if (!Path.IsPathFullyQualified(relativeToHome)
                && !relativeToHome.Equals("..", StringComparison.Ordinal)
                && !relativeToHome.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                return $"~/{NormalizePath(relativeToHome)}";
            }
        }

        return NormalizePath(fullPath);
    }

    private static string NormalizePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed record HomeSelection(
        string? Solution,
        string? Project);

    private sealed record HomePayload(
        string Bin,
        string Description,
        string Tool,
        string ToolVersion,
        string OutputSchema,
        AgentCommandGuidance Guidance,
        HomeWorkspacePayload Workspace,
        HomeGitPayload Git,
        CapabilityReport Capabilities,
        HomeAnalysisPayload Analysis);

    private sealed record HomeWorkspacePayload(
        string Root,
        string? Solution,
        string? Project,
        int Projects,
        int CsharpFiles);

    private sealed record HomeGitPayload(
        string Branch,
        object ChangedFiles);

    private sealed record HomeAnalysisPayload(
        HomeAnalysisStatus Status,
        HomeCompilerErrorState CompilerErrors);

    private enum HomeAnalysisStatus
    {
        NotLoaded,
    }

    private enum HomeCompilerErrorState
    {
        Unknown,
    }
}
