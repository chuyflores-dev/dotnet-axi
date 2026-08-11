using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Search;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record FileSearchCommandRequest(
    string Query,
    bool CaseSensitive,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> Globs,
    IReadOnlyList<string> Paths,
    string? Project,
    bool Changed,
    bool IncludeGenerated,
    int Limit,
    IReadOnlyList<string> Fields)
{
    public static readonly string[] AvailableFields =
    [
        "id",
        "path",
        "kind",
        "owning_project_count",
        "owning_projects",
        "external",
    ];

    public static FileSearchCommandRequest Create(
        string query,
        bool caseSensitive,
        IReadOnlyList<string> extensions,
        IReadOnlyList<string> globs,
        IReadOnlyList<string> paths,
        string? project,
        bool changed,
        bool includeGenerated,
        int limit,
        IReadOnlyList<string> fields)
    {
        fields = OutputFieldSelection.Parse(fields);

        if (fields.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.file_field",
                "A --fields value cannot be blank.",
                UsageErrorResult.FieldCatalogCorrection(AvailableFields));
        }

        var unknown = fields
            .Where(field => !AvailableFields.Contains(
                field,
                StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new UnknownOutputFieldsException(
                unknown,
                AvailableFields);
        }

        return new FileSearchCommandRequest(
            query,
            caseSensitive,
            extensions,
            globs,
            paths,
            project,
            changed,
            includeGenerated,
            limit,
            fields);
    }

    private static CommandUsageException Usage(
        string code,
        string message,
        string correction) =>
        new(code, message, correction);
}

internal sealed class FileSearchCommandHandler :
    ICommandHandler<FileSearchCommandRequest>
{
    private readonly ChangedScopeResolver _changedScopeResolver;

    public FileSearchCommandHandler()
        : this(ChangedScopeResolver.CreatePassive(new ProcessRunner()))
    {
    }

    internal FileSearchCommandHandler(
        ChangedScopeResolver changedScopeResolver)
    {
        _changedScopeResolver = changedScopeResolver
            ?? throw new ArgumentNullException(nameof(changedScopeResolver));
    }

    public async ValueTask<ICommandResult> HandleAsync(
        FileSearchCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var fields = FileSearchFields.Select(request.Fields);

        cancellationToken.ThrowIfCancellationRequested();
        var workspace = new WorkspaceDiscoverer()
            .Discover(Directory.GetCurrentDirectory());
        string? projectDirectory = null;
        string? selectedProject = null;
        if (request.Project is not null)
        {
            try
            {
                var selected = new WorkspaceEntryPointSelector().Select(
                    workspace,
                    new WorkspaceSelectionRequest(
                        project: request.Project));
                selectedProject = selected.Path;
                projectDirectory = DirectoryPath(selected.Path);
            }
            catch (WorkspaceSelectionUsageException exception)
            {
                throw new CommandUsageException(
                    exception.Code,
                    exception.Message,
                    exception.Correction);
            }
        }

        ChangedScopeResult? changedScope = null;
        if (request.Changed)
        {
            try
            {
                changedScope = await _changedScopeResolver
                    .ResolveAsync(
                        workspace,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ChangedScopeResolutionException exception)
                when (exception.Kind is ChangedScopeErrorKind.GitRequired
                    or ChangedScopeErrorKind.HeadRequiresBase
                    or ChangedScopeErrorKind.InvalidBaseReference
                    or ChangedScopeErrorKind.InvalidHeadReference)
            {
                throw new CommandUsageException(
                    exception.Code,
                    exception.Message,
                    exception.Correction);
            }
            catch (ChangedScopeResolutionException exception)
            {
                return CommandResult<FileSearchPayload>.Failed(
                    "search file",
                    [
                        new ResultError(
                            exception.Code,
                            exception.Message,
                            exception.Correction),
                    ]);
            }
        }

        var traversal = new WorkspaceTraversalRequest(
            workspace.RootPath,
            explicitPaths: request.Paths,
            includeGenerated: request.IncludeGenerated,
            currentDirectory: workspace.CurrentDirectory);
        var scopedTraversal = new ScopedTraverser(
            new WorkspacePathTraverser(),
            projectDirectory,
            changedScope?.ChangedPaths.ToHashSet(StringComparer.Ordinal));
        var ownership = new WorkspaceProjectOwnershipResolver(
            workspace.Projects.Select(static project => project.Path));
        FileSearchResult result;
        try
        {
            result = new FileSearcher(scopedTraversal, ownership).Search(
                new FileSearchRequest(
                    request.Query,
                    traversal,
                    request.CaseSensitive,
                    request.Extensions,
                    request.Globs,
                    request.Limit),
                cancellationToken);
        }
        catch (FileSearchGlobException exception)
        {
            throw Usage(
                "usage.file_glob",
                $"The --glob pattern `{exception.Pattern}` is invalid.",
                "Use a valid workspace-relative glob pattern.");
        }
        var retrievalCommand = RetrievalCommand(request, result.Total);
        var bounded = BoundedCollection<
            IReadOnlyDictionary<string, object?>>.FromObserved(
                fields.Project(result.Matches),
                result.Total,
                totalKnown: true,
                retrievalCommand);
        var payload = new FileSearchPayload(
            bounded.Count,
            bounded.TotalKnown,
            bounded.Total,
            bounded.Omitted,
            bounded.Truncated,
            bounded.RetrievalCommand,
            bounded.Items);
        var evidence = new Evidence(
            result.Snapshot,
            EvidenceResolution.Text,
            new EvidenceCoverage(CoverageLevel.Complete),
            EvidenceConfidence.Verified,
            new EvidenceScope(
                workspace.RootPath,
                request.Changed ? "eligible changed paths" : "eligible workspace paths",
                projects: selectedProject is null
                    ? null
                    : [selectedProject]));
        return CommandResult<FileSearchPayload>.Success(
            "search file",
            payload,
            evidence);
    }

    private static void ValidateRequest(FileSearchCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw Usage(
                "usage.file_query_required",
                "The file query cannot be blank.",
                "Provide a non-blank path query.");
        }

        if (request.Limit < 0)
        {
            throw Usage(
                "usage.file_limit",
                "The --limit value cannot be negative.",
                "Use a non-negative --limit value.");
        }

        if (request.Project is not null
            && string.IsNullOrWhiteSpace(request.Project))
        {
            throw Usage(
                "usage.file_project",
                "The --project value cannot be blank.",
                "Provide a project selector or remove --project.");
        }

        if (request.Paths.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.file_path",
                "A --path value cannot be blank.",
                "Provide one or more non-blank paths.");
        }

        if (request.Globs.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.file_glob",
                "A --glob value cannot be blank.",
                "Provide one or more non-blank workspace-relative glob patterns.");
        }

        if (request.Extensions.Any(InvalidExtension))
        {
            throw Usage(
                "usage.file_extension",
                "An --extension value is invalid.",
                "Use extension names such as `cs` or `.cs` without path separators.");
        }
    }

    private static bool InvalidExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return true;
        }

        var normalized = extension.TrimStart('.');
        return normalized.Length == 0
            || normalized.Contains('/')
            || normalized.Contains('\\');
    }

    private static string DirectoryPath(string projectPath)
    {
        var separator = projectPath.LastIndexOf('/');
        return separator < 0 ? string.Empty : projectPath[..separator];
    }

    private static bool MatchesProject(
        WorkspaceTraversalPath path,
        string? directory)
    {
        if (directory is null || path.IsExternal)
        {
            return directory is null;
        }

        return directory.Length == 0
            || path.RelativePath.StartsWith(
                directory + "/",
                StringComparison.Ordinal);
    }

    private static CommandUsageException Usage(
        string code,
        string message,
        string correction) =>
        new(code, message, correction);

    private static string RetrievalCommand(
        FileSearchCommandRequest request,
        int total)
    {
        var arguments = new List<string>
        {
            "dnaxi",
            "search",
            "file",
            Quote(request.Query),
        };
        if (request.CaseSensitive)
        {
            arguments.Add("--case-sensitive");
        }

        foreach (var extension in request.Extensions)
        {
            arguments.Add("--extension");
            arguments.Add(Quote(extension));
        }

        foreach (var glob in request.Globs)
        {
            arguments.Add("--glob");
            arguments.Add(Quote(glob));
        }

        foreach (var path in request.Paths)
        {
            arguments.Add("--path");
            arguments.Add(Quote(path));
        }

        if (request.Project is not null)
        {
            arguments.Add("--project");
            arguments.Add(Quote(request.Project));
        }

        if (request.Changed)
        {
            arguments.Add("--changed");
        }

        if (request.IncludeGenerated)
        {
            arguments.Add("--include-generated");
        }

        if (request.Fields.Count > 0)
        {
            arguments.Add("--fields");
            arguments.Add(Quote(OutputFieldSelection.CanonicalValue(request.Fields)));
        }

        arguments.Add("--limit");
        arguments.Add(total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return CanonicalInvocation.OneShot(string.Join(' ', arguments));
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed class ScopedTraverser(
        IWorkspacePathTraverser inner,
        string? projectDirectory,
        ISet<string>? changedPaths) : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = new List<WorkspaceTraversalPath>();
            foreach (var path in inner.Traverse(request, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesProject(path, projectDirectory)
                    || changedPaths is not null
                    && !changedPaths.Contains(path.RelativePath))
                {
                    continue;
                }

                paths.Add(path);
            }

            return Array.AsReadOnly(paths.ToArray());
        }
    }

    private static readonly OutputFieldSet<FileSearchMatch>
        FileSearchFields = new(
        [
            new OutputField<FileSearchMatch>(
                "id",
                static match => match.Id,
                includedByDefault: false),
            new OutputField<FileSearchMatch>(
                "path",
                static match => match.Path,
                includedByDefault: true),
            new OutputField<FileSearchMatch>(
                "kind",
                static match => match.Kind,
                includedByDefault: false),
            new OutputField<FileSearchMatch>(
                "owning_project_count",
                static match => match.OwningProjectCount,
                includedByDefault: false),
            new OutputField<FileSearchMatch>(
                "owning_projects",
                static match => match.OwningProjects),
            new OutputField<FileSearchMatch>(
                "external",
                static match => match.IsExternal),
        ]);

    private sealed record FileSearchPayload(
        int Count,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Files);
}
