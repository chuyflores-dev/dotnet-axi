namespace DotNetAxi.Workspaces;

public enum WorkspaceEntryPointKind
{
    Solution,
    Project,
}

public enum WorkspaceSelectionSource
{
    ExplicitSolution,
    ExplicitProject,
    RepositoryConfiguration,
    WorkspaceRootSolution,
    WorkspaceRootProject,
}

public enum WorkspaceSelectionErrorKind
{
    ConflictingExplicitSelectors,
    InvalidSelector,
    AmbiguousSelector,
    SelectionRequired,
    NoSupportedEntryPoint,
}

public sealed record ConfiguredWorkspaceSelector
{
    public ConfiguredWorkspaceSelector(
        WorkspaceEntryPointKind kind,
        string value)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The workspace entry-point kind is not defined.");
        }

        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public WorkspaceEntryPointKind Kind { get; }

    public string Value { get; }
}

public sealed class WorkspaceSelectionRequest
{
    public WorkspaceSelectionRequest(
        string? solution = null,
        string? project = null,
        ConfiguredWorkspaceSelector? configuredSelector = null)
    {
        Solution = solution;
        Project = project;
        ConfiguredSelector = configuredSelector;
    }

    public string? Solution { get; }

    public string? Project { get; }

    public ConfiguredWorkspaceSelector? ConfiguredSelector { get; }
}

public sealed record WorkspaceSelection(
    WorkspaceEntryPointKind Kind,
    string Path,
    WorkspaceSelectionSource Source);

public sealed class WorkspaceSelectionUsageException : InvalidOperationException
{
    internal WorkspaceSelectionUsageException(
        WorkspaceSelectionErrorKind kind,
        string code,
        string message,
        IEnumerable<string> candidatePaths,
        string correction)
        : base(message)
    {
        Kind = kind;
        Code = code;
        CandidatePaths = Array.AsReadOnly(candidatePaths.ToArray());
        Correction = correction;
    }

    public WorkspaceSelectionErrorKind Kind { get; }

    public string Code { get; }

    public IReadOnlyList<string> CandidatePaths { get; }

    public string Correction { get; }
}

public sealed class WorkspaceEntryPointSelector
{
    public WorkspaceSelection Select(
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelectionRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        request ??= new WorkspaceSelectionRequest();
        var candidates = SupportedCandidates(discovery);

        if (request.Solution is not null && request.Project is not null)
        {
            throw Error(
                WorkspaceSelectionErrorKind.ConflictingExplicitSelectors,
                "usage.workspace_selector_conflict",
                "The --solution and --project selectors cannot be used together.",
                candidates,
                ConflictingSelectorCorrection(discovery, candidates));
        }

        if (request.Solution is not null)
        {
            return SelectCandidate(
                discovery,
                candidates,
                WorkspaceEntryPointKind.Solution,
                request.Solution,
                WorkspaceSelectionSource.ExplicitSolution,
                discovery.CurrentDirectory);
        }

        if (request.Project is not null)
        {
            return SelectCandidate(
                discovery,
                candidates,
                WorkspaceEntryPointKind.Project,
                request.Project,
                WorkspaceSelectionSource.ExplicitProject,
                discovery.CurrentDirectory);
        }

        if (request.ConfiguredSelector is not null)
        {
            return SelectCandidate(
                discovery,
                candidates,
                request.ConfiguredSelector.Kind,
                request.ConfiguredSelector.Value,
                WorkspaceSelectionSource.RepositoryConfiguration,
                discovery.RootPath);
        }

        var rootSolutions = RootCandidates(
            candidates,
            WorkspaceEntryPointKind.Solution);
        if (rootSolutions.Count == 1)
        {
            return Selection(
                rootSolutions[0],
                WorkspaceSelectionSource.WorkspaceRootSolution);
        }

        if (rootSolutions.Count > 1)
        {
            throw AmbiguousFallback(discovery, rootSolutions);
        }

        var rootProjects = RootCandidates(
            candidates,
            WorkspaceEntryPointKind.Project);
        if (rootProjects.Count == 1)
        {
            return Selection(
                rootProjects[0],
                WorkspaceSelectionSource.WorkspaceRootProject);
        }

        if (rootProjects.Count > 1)
        {
            throw AmbiguousFallback(discovery, rootProjects);
        }

        if (candidates.Count > 0)
        {
            throw Error(
                WorkspaceSelectionErrorKind.SelectionRequired,
                "usage.workspace_selection_required",
                "No supported solution or C# project is uniquely selectable at the workspace root.",
                candidates,
                CandidateCorrection(discovery, candidates[0]));
        }

        throw Error(
            WorkspaceSelectionErrorKind.NoSupportedEntryPoint,
            "usage.workspace_entry_point_missing",
            "The workspace does not contain a supported solution or C# project entry point.",
            candidates,
            "Add a supported .sln, .slnx, or .csproj file, then select it with --solution or --project.");
    }

    private static WorkspaceSelection SelectCandidate(
        WorkspaceDiscoveryResult discovery,
        IReadOnlyList<Candidate> candidates,
        WorkspaceEntryPointKind kind,
        string selector,
        WorkspaceSelectionSource source,
        string selectorBasePath)
    {
        var matches = Matches(
            candidates,
            kind,
            selector,
            selectorBasePath,
            discovery.RootPath);
        if (matches.Count == 1)
        {
            return Selection(matches[0], source);
        }

        if (matches.Count > 1)
        {
            throw Error(
                WorkspaceSelectionErrorKind.AmbiguousSelector,
                "usage.workspace_selector_ambiguous",
                $"The {Flag(kind)} selector '{selector}' matches more than one supported entry point.",
                matches,
                CandidateCorrection(discovery, matches[0]));
        }

        throw Error(
            WorkspaceSelectionErrorKind.InvalidSelector,
            "usage.workspace_selector_invalid",
            $"The {Flag(kind)} selector '{selector}' does not match a supported entry point.",
            candidates,
            candidates.Count == 0
                ? "Add a supported .sln, .slnx, or .csproj file, then select it with --solution or --project."
                : CandidateCorrection(discovery, candidates[0]));
    }

    private static IReadOnlyList<Candidate> Matches(
        IReadOnlyList<Candidate> candidates,
        WorkspaceEntryPointKind kind,
        string selector,
        string selectorBasePath,
        string workspaceRootPath)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return Array.Empty<Candidate>();
        }

        var normalizedSelector = NormalizeSelector(
            selectorBasePath,
            workspaceRootPath,
            selector);
        var isProjectName = kind is WorkspaceEntryPointKind.Project
            && !selector.Contains('/')
            && !selector.Contains('\\');
        var kindCandidates = candidates
            .Where(candidate => candidate.Kind == kind)
            .ToArray();
        var pathMatches = kindCandidates
            .Where(candidate => candidate.Path.Equals(
                normalizedSelector,
                PathComparison()))
            .ToArray();
        if (pathMatches.Length > 0
            || kind is WorkspaceEntryPointKind.Solution
            || !isProjectName)
        {
            return Array.AsReadOnly(pathMatches);
        }

        var projectName = selector.EndsWith(
            ".csproj",
            StringComparison.OrdinalIgnoreCase)
            ? selector[..^".csproj".Length]
            : selector;
        var nameMatches = kindCandidates
            .Where(candidate => Path.GetFileNameWithoutExtension(candidate.Path)
                .Equals(projectName, PathComparison()))
            .ToArray();
        return Array.AsReadOnly(nameMatches);
    }

    private static string NormalizeSelector(
        string selectorBasePath,
        string workspaceRootPath,
        string selector)
    {
        var nativeSelector = selector
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        try
        {
            var fullPath = Path.IsPathRooted(nativeSelector)
                ? Path.GetFullPath(nativeSelector)
                : Path.GetFullPath(nativeSelector, selectorBasePath);
            return Path.GetRelativePath(workspaceRootPath, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return selector.Replace('\\', '/');
        }
    }

    private static IReadOnlyList<Candidate> SupportedCandidates(
        WorkspaceDiscoveryResult discovery)
    {
        var candidates = discovery.Solutions
            .Select(static solution => new Candidate(
                WorkspaceEntryPointKind.Solution,
                solution.Path))
            .Concat(discovery.Projects.Select(static project => new Candidate(
                WorkspaceEntryPointKind.Project,
                project.Path)))
            .OrderBy(static candidate => candidate.Path, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Kind)
            .ToArray();
        return Array.AsReadOnly(candidates);
    }

    private static IReadOnlyList<Candidate> RootCandidates(
        IEnumerable<Candidate> candidates,
        WorkspaceEntryPointKind kind)
    {
        var rootCandidates = candidates
            .Where(candidate => candidate.Kind == kind)
            .Where(static candidate => !candidate.Path.Contains('/'))
            .ToArray();
        return Array.AsReadOnly(rootCandidates);
    }

    private static WorkspaceSelectionUsageException AmbiguousFallback(
        WorkspaceDiscoveryResult discovery,
        IReadOnlyList<Candidate> candidates) =>
        Error(
            WorkspaceSelectionErrorKind.AmbiguousSelector,
            "usage.workspace_selector_ambiguous",
            "More than one supported workspace-root entry point has the same selection precedence.",
            candidates,
            CandidateCorrection(discovery, candidates[0]));

    private static WorkspaceSelection Selection(
        Candidate candidate,
        WorkspaceSelectionSource source) =>
        new(candidate.Kind, candidate.Path, source);

    private static WorkspaceSelectionUsageException Error(
        WorkspaceSelectionErrorKind kind,
        string code,
        string message,
        IEnumerable<Candidate> candidates,
        string correction) =>
        new(
            kind,
            code,
            message,
            candidates.Select(static candidate => candidate.Path),
            correction);

    private static string CandidateCorrection(
        WorkspaceDiscoveryResult discovery,
        Candidate candidate) =>
        $"Use `{Flag(candidate.Kind)}` with candidate path `{CorrectionPath(discovery, candidate)}`.";

    private static string ConflictingSelectorCorrection(
        WorkspaceDiscoveryResult discovery,
        IReadOnlyList<Candidate> candidates) =>
        candidates.Count == 0
            ? "Remove either --solution or --project and provide one supported entry-point path."
            : $"Specify exactly one selector; for example, use `{Flag(candidates[0].Kind)}` with candidate path `{CorrectionPath(discovery, candidates[0])}`.";

    private static string CorrectionPath(
        WorkspaceDiscoveryResult discovery,
        Candidate candidate)
    {
        var nativeCandidatePath = candidate.Path.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var absoluteCandidatePath = Path.GetFullPath(
            nativeCandidatePath,
            discovery.RootPath);
        return Path.GetRelativePath(
                discovery.CurrentDirectory,
                absoluteCandidatePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Flag(WorkspaceEntryPointKind kind) =>
        kind switch
        {
            WorkspaceEntryPointKind.Solution => "--solution",
            WorkspaceEntryPointKind.Project => "--project",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The workspace entry-point kind is not defined."),
        };

    private sealed record Candidate(
        WorkspaceEntryPointKind Kind,
        string Path);
}
