using System.Xml;
using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record SymbolWorkspaceScopeRequest(
    string? Solution,
    string? Project,
    IReadOnlyList<string> Paths,
    bool IncludeTests,
    bool IncludeGenerated)
{
    public static SymbolWorkspaceScopeRequest Create(
        string? solution,
        string? project,
        IReadOnlyList<string> paths,
        bool includeTests,
        bool includeGenerated,
        string pathErrorCode)
    {
        if (paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new CommandUsageException(
                pathErrorCode,
                "A --path value cannot be blank.",
                "Provide one or more non-blank paths.");
        }

        return new SymbolWorkspaceScopeRequest(
            solution,
            project,
            Array.AsReadOnly(paths.ToArray()),
            includeTests,
            includeGenerated);
    }
}

internal sealed class ResolvedSymbolWorkspaceScope
{
    public ResolvedSymbolWorkspaceScope(
        WorkspaceDiscoveryResult workspace,
        WorkspaceSelection? selection,
        IReadOnlyList<string> projects,
        SymbolWorkspaceScopeRequest request)
    {
        Workspace = workspace;
        Selection = selection;
        Projects = projects;
        Request = request;
        Traversal = new WorkspaceTraversalRequest(
            workspace.RootPath,
            explicitPaths: request.Paths,
            includeGenerated: request.IncludeGenerated,
            currentDirectory: workspace.CurrentDirectory);
        Ownership = new WorkspaceProjectOwnershipResolver(
            workspace.RootPath,
            projects);
        Traverser = selection is null
            ? new WorkspacePathTraverser()
            : new OwnershipScopedTraverser(
                new WorkspacePathTraverser(),
                Ownership);
    }

    public WorkspaceDiscoveryResult Workspace { get; }

    public WorkspaceSelection? Selection { get; }

    public IReadOnlyList<string> Projects { get; }

    public SymbolWorkspaceScopeRequest Request { get; }

    public WorkspaceTraversalRequest Traversal { get; }

    public IWorkspacePathTraverser Traverser { get; }

    public IFileOwnershipResolver Ownership { get; }

    public SymbolDeclarationScope DeclarationScope => new(
        Selection?.Kind is WorkspaceEntryPointKind.Solution
            ? Selection.Path
            : null,
        Projects,
        Request.Paths,
        Request.IncludeTests,
        Request.IncludeGenerated);

    public EvidenceScope EvidenceScope => new(
        Workspace.RootPath,
        EligibilityDescription(),
        solution: Selection?.Kind is WorkspaceEntryPointKind.Solution
            ? Selection.Path
            : null,
        projects: Projects,
        eligibility: new EvidenceEligibility(
            Request.IncludeTests,
            Request.IncludeGenerated),
        paths: Request.Paths);

    private string EligibilityDescription()
    {
        var source = Request.IncludeTests
            ? "production and test C# declarations"
            : "production C# declarations";
        var generated = Request.IncludeGenerated
            ? ", including generated source"
            : ", excluding generated source";
        var paths = Request.Paths.Count == 0
            ? "selected workspace "
            : "selected workspace explicitly constrained ";
        return paths + source + generated;
    }

    public string CanonicalArguments()
    {
        var arguments = new List<string>();
        if (Selection is not null)
        {
            arguments.Add(Selection.Kind is WorkspaceEntryPointKind.Solution
                ? "--solution"
                : "--project");
            arguments.Add(Quote(Selection.Path));
        }

        foreach (var path in Request.Paths)
        {
            arguments.Add("--path");
            arguments.Add(Quote(path));
        }

        if (Request.IncludeTests)
        {
            arguments.Add("--include-tests");
        }

        if (Request.IncludeGenerated)
        {
            arguments.Add("--include-generated");
        }

        return arguments.Count == 0
            ? string.Empty
            : " " + string.Join(' ', arguments);
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed class OwnershipScopedTraverser(
        IWorkspacePathTraverser inner,
        IFileOwnershipResolver ownership) : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default) =>
            Array.AsReadOnly(
                inner.Traverse(request, cancellationToken)
                    .Where(path => path.IsExternal
                        || ownership.GetOwningProjects(path).Count > 0)
                    .ToArray());
    }
}

internal static class SymbolWorkspaceScopeResolver
{
    public static ResolvedSymbolWorkspaceScope Resolve(
        WorkspaceDiscoveryResult workspace,
        SymbolWorkspaceScopeRequest request,
        ConfiguredWorkspaceSelector? configuredSelector = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        WorkspaceSelection? selection = null;
        if (request.Solution is not null
            || request.Project is not null
            || configuredSelector is not null
            || workspace.Solutions.Count > 0
            || workspace.Projects.Count > 0)
        {
            try
            {
                selection = new WorkspaceEntryPointSelector().Select(
                    workspace,
                    new WorkspaceSelectionRequest(
                        request.Solution,
                        request.Project,
                        configuredSelector));
            }
            catch (WorkspaceSelectionUsageException exception)
            {
                throw new CommandUsageException(
                    exception.Code,
                    exception.Message,
                    exception.Correction,
                    exception.CandidatePaths);
            }
        }

        var projects = selection switch
        {
            { Kind: WorkspaceEntryPointKind.Project } => [selection.Path],
            { Kind: WorkspaceEntryPointKind.Solution } =>
                SolutionProjects(workspace, selection.Path),
            _ => workspace.Projects
                .Select(static project => project.Path)
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
        return new ResolvedSymbolWorkspaceScope(
            workspace,
            selection,
            Array.AsReadOnly(projects.ToArray()),
            request);
    }

    private static IReadOnlyList<string> SolutionProjects(
        WorkspaceDiscoveryResult workspace,
        string solution)
    {
        var solutionPath = Path.GetFullPath(
            solution.Replace('/', Path.DirectorySeparatorChar),
            workspace.RootPath);
        IEnumerable<string> projectPaths;
        try
        {
            projectPaths = PassiveSolutionProjectReader.ReadProjectPaths(
                solutionPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or XmlException
            or ArgumentException
            or NotSupportedException)
        {
            throw new CommandUsageException(
                "usage.workspace_solution_invalid",
                $"The selected solution `{solution}` could not be read passively.",
                $"Repair `{solution}` or select one project with --project.");
        }

        var discovered = workspace.Projects
            .Select(static project => project.Path)
            .Order(StringComparer.Ordinal)
            .GroupBy(
                static path => path,
                WorkspacePathIdentity.Comparer)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                WorkspacePathIdentity.Comparer);
        return Array.AsReadOnly(
            projectPaths
                .Where(static path => Path.GetExtension(path).Equals(
                    ".csproj",
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => WorkspaceRelative(workspace.RootPath, path))
                .Where(static path => path.Length > 0)
                .Select(path => discovered.TryGetValue(path, out var canonical)
                    ? canonical
                    : path)
                .Distinct(WorkspacePathIdentity.Comparer)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static string WorkspaceRelative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathFullyQualified(relative)
            || relative == ".."
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            || relative.StartsWith(
                ".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return relative.Replace('\\', '/');
    }
}
