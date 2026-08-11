using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record SymbolSearchCommandRequest(
    string Query,
    IReadOnlyList<string> Kinds,
    string? Namespace,
    string? Project,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Accessibilities,
    bool IncludeTests,
    bool IncludeGenerated,
    int Limit,
    bool LimitSpecified,
    bool Full,
    IReadOnlyList<string> Fields)
{
    public static readonly string[] AvailableFields =
    [
        "id",
        "kind",
        "name",
        "file",
        "line",
        "column",
        "end_line",
        "end_column",
        "namespace",
        "fully_qualified_name",
        "accessibility",
        "signature",
        "owning_project_count",
        "owning_projects",
        "variant_count",
        "variants",
        "test",
        "generated",
        "external",
        "rank",
    ];

    public static SymbolSearchCommandRequest Create(
        string query,
        IReadOnlyList<string> kinds,
        string? namespaceFilter,
        string? project,
        IReadOnlyList<string> paths,
        IReadOnlyList<string> accessibilities,
        bool includeTests,
        bool includeGenerated,
        int limit,
        bool limitSpecified,
        bool full,
        IReadOnlyList<string> fields)
    {
        fields = OutputFieldSelection.Parse(fields);

        if (string.IsNullOrWhiteSpace(query) || query.Contains('\0', StringComparison.Ordinal))
        {
            throw Usage(
                "usage.symbol_query_required",
                "The symbol query must be non-blank C# declaration text.",
                "Provide a declaration name or fully qualified name.");
        }

        ValidateFilters(
            kinds,
            SymbolDeclarationSearcher.AvailableKinds,
            "kind",
            "usage.symbol_kind");
        ValidateFilters(
            accessibilities,
            SymbolDeclarationSearcher.AvailableAccessibilities,
            "accessibility",
            "usage.symbol_accessibility");
        if (namespaceFilter is not null && string.IsNullOrWhiteSpace(namespaceFilter))
        {
            throw Usage(
                "usage.symbol_namespace",
                "The --namespace value cannot be blank.",
                "Provide a namespace or remove --namespace.");
        }

        if (project is not null && string.IsNullOrWhiteSpace(project))
        {
            throw Usage(
                "usage.symbol_project",
                "The --project value cannot be blank.",
                "Provide a project selector or remove --project.");
        }

        if (paths.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.symbol_path",
                "A --path value cannot be blank.",
                "Provide one or more non-blank paths.");
        }

        if (limit < 0 || (full && limitSpecified))
        {
            throw Usage(
                "usage.symbol_limit",
                "The --limit value is invalid for this symbol search.",
                "Use a non-negative --limit, or use --full without --limit.");
        }

        if (fields.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.symbol_field",
                "A --fields value cannot be blank.",
                UsageErrorResult.FieldCatalogCorrection(AvailableFields));
        }

        var unknownFields = fields
            .Where(field => !AvailableFields.Contains(field, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknownFields.Length > 0)
        {
            throw new UnknownOutputFieldsException(unknownFields, AvailableFields);
        }

        return new SymbolSearchCommandRequest(
            query,
            kinds,
            namespaceFilter,
            project,
            paths,
            accessibilities,
            includeTests,
            includeGenerated,
            limit,
            limitSpecified,
            full,
            fields);
    }

    private static void ValidateFilters(
        IReadOnlyList<string> values,
        IReadOnlyList<string> allowed,
        string name,
        string code)
    {
        var unknown = values
            .Where(value => string.IsNullOrWhiteSpace(value)
                || !allowed.Contains(value, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length == 0)
        {
            return;
        }

        throw Usage(
            code,
            $"One or more --{name} values are invalid.",
            $"Use one or more of: {string.Join(", ", allowed)}.");
    }

    private static CommandUsageException Usage(
        string code,
        string message,
        string correction) =>
        new(code, message, correction);
}

internal sealed class SymbolSearchCommandHandler :
    ICommandHandler<SymbolSearchCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        SymbolSearchCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fields = SymbolFields.Select(request.Fields);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = new WorkspaceDiscoverer().Discover(Directory.GetCurrentDirectory());
        string? selectedProject = null;
        string? projectDirectory = null;
        if (request.Project is not null)
        {
            try
            {
                selectedProject = new WorkspaceEntryPointSelector()
                    .Select(workspace, new WorkspaceSelectionRequest(project: request.Project))
                    .Path;
                projectDirectory = DirectoryPath(selectedProject);
            }
            catch (WorkspaceSelectionUsageException exception)
            {
                throw new CommandUsageException(
                    exception.Code,
                    exception.Message,
                    exception.Correction);
            }
        }

        var traversal = new WorkspaceTraversalRequest(
            workspace.RootPath,
            explicitPaths: request.Paths,
            includeGenerated: request.IncludeGenerated,
            currentDirectory: workspace.CurrentDirectory);
        var traverser = new ProjectScopedTraverser(
            new WorkspacePathTraverser(),
            projectDirectory);
        var ownership = new WorkspaceProjectOwnershipResolver(
            workspace.RootPath,
            workspace.Projects.Select(static project => project.Path));
        var result = await new SymbolDeclarationSearcher(traverser, ownership)
            .SearchAsync(
                new SymbolDeclarationSearchRequest(
                    request.Query,
                    traversal,
                    request.Kinds,
                    request.Namespace,
                    selectedProject,
                    request.Accessibilities,
                    request.IncludeTests),
                cancellationToken)
            .ConfigureAwait(false);

        var included = request.Full
            ? result.Matches
            : result.Matches.Take(request.Limit);
        var bounded = BoundedCollection<IReadOnlyDictionary<string, object?>>
            .FromObserved(
                Project(included, fields, cancellationToken),
                result.Matches.Count,
                totalKnown: true,
                RetrievalCommand(request, selectedProject));
        var payload = new SymbolSearchPayload(
            bounded.Count,
            bounded.TotalKnown,
            bounded.Total,
            bounded.Omitted,
            bounded.Truncated,
            bounded.RetrievalCommand,
            bounded.Items);
        var evidence = new Evidence(
            result.Snapshot,
            EvidenceResolution.Syntax,
            new EvidenceCoverage(
                CoverageLevel.Complete,
                considered: result.Observations.Count,
                analyzed: result.Observations.Count,
                remaining: 0,
                excluded: 0,
                failed: 0),
            EvidenceConfidence.Candidate,
            new EvidenceScope(
                workspace.RootPath,
                request.Paths.Count == 0
                    ? "eligible C# declaration paths"
                    : "eligible explicitly selected C# declaration paths",
                projects: selectedProject is null ? null : [selectedProject]));

        return CommandResult<SymbolSearchPayload>.Success(
            "search symbol",
            payload,
            evidence);
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> Project(
        IEnumerable<SymbolDeclarationMatch> matches,
        OutputFieldSelection<SymbolDeclarationMatch> fields,
        CancellationToken cancellationToken)
    {
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return fields.Project(match);
        }
    }

    private static string RetrievalCommand(
        SymbolSearchCommandRequest request,
        string? selectedProject)
    {
        var arguments = new List<string>
        {
            "dnaxi",
            "search",
            "symbol",
            Quote(request.Query),
        };
        foreach (var kind in request.Kinds)
        {
            arguments.Add("--kind");
            arguments.Add(Quote(kind));
        }

        if (request.Namespace is not null)
        {
            arguments.Add("--namespace");
            arguments.Add(Quote(request.Namespace));
        }

        if (selectedProject is not null)
        {
            arguments.Add("--project");
            arguments.Add(Quote(selectedProject));
        }

        foreach (var path in request.Paths)
        {
            arguments.Add("--path");
            arguments.Add(Quote(path));
        }

        foreach (var accessibility in request.Accessibilities)
        {
            arguments.Add("--accessibility");
            arguments.Add(Quote(accessibility));
        }

        if (request.IncludeTests) arguments.Add("--include-tests");
        if (request.IncludeGenerated) arguments.Add("--include-generated");
        if (request.Fields.Count > 0)
        {
            arguments.Add("--fields");
            arguments.Add(Quote(OutputFieldSelection.CanonicalValue(request.Fields)));
        }

        arguments.Add("--full");
        return CanonicalInvocation.OneShot(string.Join(' ', arguments));
    }

    private static string DirectoryPath(string projectPath)
    {
        var separator = projectPath.LastIndexOf('/');
        return separator < 0 ? string.Empty : projectPath[..separator];
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static bool MatchesProject(WorkspaceTraversalPath path, string? directory)
    {
        if (directory is null || path.IsExternal)
        {
            return directory is null;
        }

        return directory.Length == 0
            || path.RelativePath.StartsWith(directory + "/", StringComparison.Ordinal);
    }

    private sealed class ProjectScopedTraverser(
        IWorkspacePathTraverser inner,
        string? projectDirectory) : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default) =>
            Array.AsReadOnly(
                inner.Traverse(request, cancellationToken)
                    .Where(path => MatchesProject(path, projectDirectory))
                    .ToArray());
    }

    private static readonly OutputFieldSet<SymbolDeclarationMatch> SymbolFields = new(
    [
        new("id", static match => match.Id),
        new("kind", static match => match.Kind, includedByDefault: true),
        new("name", static match => match.Name, includedByDefault: true),
        new("file", static match => match.Range.Start.Path, includedByDefault: true),
        new("line", static match => match.Range.Start.Line, includedByDefault: true),
        new("column", static match => match.Range.Start.Column),
        new("end_line", static match => match.Range.End.Line),
        new("end_column", static match => match.Range.End.Column),
        new("namespace", static match => match.Namespace),
        new("fully_qualified_name", static match => match.FullyQualifiedName),
        new("accessibility", static match => match.Accessibility),
        new("signature", static match => match.Signature),
        new("owning_project_count", static match => match.OwningProjectCount),
        new("owning_projects", static match => match.OwningProjects),
        new("variant_count", static match => match.VariantCount),
        new("variants", static match => match.Variants.Select(Variant).ToArray()),
        new("test", static match => match.IsTest),
        new("generated", static match => match.IsGenerated),
        new("external", static match => match.Range.Start.IsExternal),
        new("rank", static match => match.Rank),
    ]);

    private static IReadOnlyDictionary<string, object?> Variant(
        SymbolDeclarationVariant variant) =>
        new Dictionary<string, object?>
        {
            ["project"] = variant.Project,
            ["configuration"] = variant.Configuration,
            ["framework"] = variant.Framework,
            ["meaning"] = variant.Meaning,
        };

    private sealed record SymbolSearchPayload(
        int Count,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Matches);
}
