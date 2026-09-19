using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Graph;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record ProjectGraphCommandRequest(
    string? DependencyProject,
    string? Solution,
    string? EntryProject,
    string? Configuration,
    string? Framework,
    IReadOnlyList<MsBuildProperty> Properties,
    int Limit,
    bool LimitSpecified,
    bool Full)
{
    public static ProjectGraphCommandRequest Create(
        string? dependencyProject,
        string? solution,
        string? entryProject,
        string? configuration,
        string? framework,
        IReadOnlyList<string> properties,
        int limit,
        bool limitSpecified,
        bool full)
    {
        if (limit < 0 || full && limitSpecified)
        {
            throw Usage(
                "usage.graph_limit",
                "The --limit value is invalid for this graph query.",
                "Use a non-negative --limit, or use --full without --limit.");
        }

        if (dependencyProject is not null && string.IsNullOrWhiteSpace(dependencyProject))
        {
            throw Usage(
                "usage.graph_dependency_project",
                "The dependency project cannot be blank.",
                "Provide a project path relative to the current workspace.");
        }

        return new ProjectGraphCommandRequest(
            dependencyProject?.Trim(),
            Optional(solution, "--solution"),
            Optional(entryProject, "--project"),
            Optional(configuration, "--configuration"),
            Optional(framework, "--framework"),
            ParseProperties(properties),
            limit,
            limitSpecified,
            full);
    }

    public ProjectGraphEvaluationOptions EvaluationOptions() =>
        new(Configuration, Framework, Properties);

    private static string? Optional(string? value, string option)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw Usage(
                "usage.graph_build_selector",
                $"The {option} value cannot be blank.",
                $"Provide a non-blank value for {option}.");
        }

        return value.Trim();
    }

    private static IReadOnlyList<MsBuildProperty> ParseProperties(
        IEnumerable<string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var parsed = new List<MsBuildProperty>();
        foreach (var property in properties)
        {
            var separator = property?.IndexOf('=') ?? -1;
            if (separator <= 0)
            {
                throw Usage(
                    "usage.graph_property",
                    "Each --property value must use name=value syntax.",
                    "Pass --property Name=Value; repeat the option for additional properties.");
            }

            var name = property![..separator].Trim();
            if (name.Length == 0)
            {
                throw Usage(
                    "usage.graph_property",
                    "An MSBuild property name cannot be blank.",
                    "Pass --property Name=Value.");
            }

            parsed.Add(new MsBuildProperty(name, property[(separator + 1)..]));
        }

        return Array.AsReadOnly(parsed.ToArray());
    }

    private static CommandUsageException Usage(
        string code,
        string message,
        string correction) =>
        new(code, message, correction);
}

internal sealed class ProjectGraphCommandHandler :
    ICommandHandler<ProjectGraphCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        ProjectGraphCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = await QueryAsync(request, cancellationToken).ConfigureAwait(false);
        var graph = query.Graph;
        if (request.DependencyProject is not null)
        {
            graph = DependenciesFor(graph, request.DependencyProject, query.Workspace);
        }

        var retrievalCommand = RetrievalCommand(request);
        var nodes = request.Full
            ? BoundedCollection<ProjectGraphNode>.Create(graph.Nodes, graph.Nodes.Count)
            : BoundedCollection<ProjectGraphNode>.Create(
                graph.Nodes,
                request.Limit,
                graph.Nodes.Count,
                retrievalCommand + " --full");
        var includedNodes = nodes.Items
            .Select(static node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        var visibleRelationships = graph.Relationships.Where(
            relationship => includedNodes.Contains(relationship.SourceId)
                            && includedNodes.Contains(relationship.TargetId));
        var relationships = request.Full
            ? BoundedCollection<ProjectGraphRelationship>.Create(
                graph.Relationships,
                graph.Relationships.Count)
            : BoundedCollection<ProjectGraphRelationship>.Create(
                visibleRelationships,
                request.Limit,
                graph.Relationships.Count,
                retrievalCommand + " --full");
        var payload = new ProjectGraphPayload(
            nodes,
            relationships,
            query.Evaluated.Failures,
            query.Coverage.Variants.Select(Variant).ToArray());
        var command = request.DependencyProject is null
            ? "graph projects"
            : "graph dependencies";
        return query.Coverage.Coverage.Level is CoverageLevel.Complete
            ? CommandResult<ProjectGraphPayload>.Success(command, payload, query.Evidence)
            : CommandResult<ProjectGraphPayload>.Partial(command, payload, query.Evidence);
    }

    internal static async ValueTask<ProjectGraphQuery> QueryAsync(
        ProjectGraphCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = new WorkspaceDiscoverer()
            .Discover(Directory.GetCurrentDirectory());
        WorkspaceSelection selection;
        try
        {
            selection = new WorkspaceEntryPointSelector().Select(
                workspace,
                new WorkspaceSelectionRequest(request.Solution, request.EntryProject));
        }
        catch (WorkspaceSelectionUsageException exception)
        {
            throw new CommandUsageException(
                exception.Code,
                exception.Message,
                exception.Correction,
                exception.CandidatePaths);
        }

        EvaluatedProjectGraph evaluated;
        try
        {
            evaluated = new MsBuildProjectGraphEvaluator(new DotNetHostResolver()).Evaluate(
                workspace,
                selection,
                request.EvaluationOptions(),
                cancellationToken);
        }
        catch (ProjectGraphUsageException exception)
        {
            throw new CommandUsageException(
                exception.Code,
                exception.Message,
                exception.Correction,
                exception.Declarations.Select(static declaration => declaration.Project));
        }

        var coverage = new ProjectCoverageReporter().Report(evaluated);
        var evidence = await EvidenceAsync(
                workspace,
                evaluated,
                coverage,
                cancellationToken)
            .ConfigureAwait(false);
        var graph = Materialize(evaluated, coverage, evidence);
        return new ProjectGraphQuery(workspace, evaluated, coverage, evidence, graph);
    }

    private static ProjectDependencyGraph DependenciesFor(
        ProjectDependencyGraph graph,
        string dependencyProject,
        WorkspaceDiscoveryResult workspace)
    {
        string project;
        try
        {
            project = new WorkspacePathResolver(
                    workspace.RootPath,
                    workspace.CurrentDirectory)
                .ResolveInput(dependencyProject)
                .Path;
        }
        catch (WorkspacePathScopeException)
        {
            throw new CommandUsageException(
                "usage.graph_dependency_project",
                "The dependency project must be within the current workspace.",
                "Provide an evaluated workspace-relative project path.");
        }

        var sourceNodes = graph.Nodes
            .Where(node => node.Kind is ProjectGraphNodeKind.Project
                           && node.Project!.ProjectPath.Equals(
                               project,
                               StringComparison.Ordinal))
            .ToArray();
        if (sourceNodes.Length == 0)
        {
            throw new CommandUsageException(
                "usage.graph_dependency_project",
                $"The project `{dependencyProject}` was not evaluated in the selected graph.",
                "Select a solution or project that contains the requested project.",
                graph.Nodes
                    .Where(static node => node.Kind is ProjectGraphNodeKind.Project)
                    .Select(static node => node.Project!.ProjectPath));
        }

        var sourceIds = sourceNodes
            .Select(static node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        var relationships = graph.Relationships
            .Where(relationship => sourceIds.Contains(relationship.SourceId))
            .ToArray();
        var includedIds = relationships
            .Select(static relationship => relationship.SourceId)
            .Concat(relationships.Select(static relationship => relationship.TargetId))
            .Concat(sourceIds)
            .ToHashSet(StringComparer.Ordinal);
        return new ProjectDependencyGraph(
            graph.Evidence,
            graph.Nodes.Where(node => includedIds.Contains(node.Id)),
            relationships);
    }

    internal static ProjectDependencyGraph Materialize(
        EvaluatedProjectGraph evaluated,
        ProjectCoverageReport coverage,
        Evidence evidence,
        bool includeIncompleteProjectReferences = false)
    {
        var scope = evidence.Scope;
        var projects = evaluated.Projects
            .GroupBy(static project => project.Path, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(static project => project.State)
                .ThenBy(static project => project.Configuration, StringComparer.Ordinal)
                .ThenBy(static project => project.Framework, StringComparer.Ordinal)
                .First())
            .Select(project => MaterializedProject.Create(project, coverage))
            .OrderBy(static project => project.Path, StringComparer.Ordinal)
            .ToArray();
        var projectNodes = projects.ToDictionary(
            static project => project.Path,
            project => ProjectGraphNode.CreateProject(
                new ProjectGraphVariant(
                    project.Path,
                    project.Configuration,
                    project.Framework),
                project.EvaluationState,
                RowEvidence(scope, coverage.Coverage, ProjectGraphProvenance.EvaluatedProjectGraph)),
            StringComparer.Ordinal);
        var packageNodes = evaluated.PackageDependencies
            .GroupBy(static dependency => (
                PackageId: dependency.PackageId.ToUpperInvariant(),
                dependency.Version))
            .Select(group =>
            {
                var dependencies = group
                    .OrderBy(static dependency => dependency.PackageId, StringComparer.Ordinal)
                    .ThenBy(static dependency => dependency.Project, StringComparer.Ordinal)
                    .ToArray();
                var dependency = dependencies[0];
                return ProjectGraphNode.CreatePackage(
                    dependency.PackageId,
                    dependency.Version,
                    PackageState(dependencies, projectNodes),
                    RowEvidence(scope, coverage.Coverage, ProjectGraphProvenance.EvaluatedPackageReference));
            })
            .ToDictionary(static node => node.Id, StringComparer.Ordinal);
        var relationships = evaluated.Dependencies
            .Select(dependency => ProjectRelationship(
                dependency,
                projectNodes,
                scope,
                coverage.Coverage,
                includeIncompleteProjectReferences))
            .OfType<ProjectGraphRelationship>()
            .Concat(evaluated.PackageDependencies.Select(dependency => PackageRelationship(
                dependency,
                projectNodes,
                packageNodes,
                scope,
                coverage.Coverage))
                .OfType<ProjectGraphRelationship>())
            .GroupBy(static relationship => relationship.Id, StringComparer.Ordinal)
            .Select(static relationships => relationships.First())
            .OrderBy(static relationship => relationship.Id, StringComparer.Ordinal)
            .ToArray();
        return new ProjectDependencyGraph(
            evidence,
            projectNodes.Values.Concat(packageNodes.Values),
            relationships);
    }

    private static ProjectGraphRelationship? ProjectRelationship(
        ProjectDependency dependency,
        IReadOnlyDictionary<string, ProjectGraphNode> projects,
        EvidenceScope scope,
        EvidenceCoverage coverage,
        bool includeIncompleteProjectReferences)
    {
        var source = projects[dependency.Project];
        var target = projects[dependency.Dependency];
        if (!MatchesVariant(source, dependency.Configuration, dependency.Framework))
        {
            return null;
        }

        var hasTargetVariant = dependency.DependencyConfiguration is not null
            && dependency.DependencyFramework is not null;
        if (!hasTargetVariant && !includeIncompleteProjectReferences)
        {
            return null;
        }

        if (hasTargetVariant && !MatchesVariant(
                target,
                dependency.DependencyConfiguration,
                dependency.DependencyFramework))
        {
            return null;
        }

        return new ProjectGraphRelationship(
            ProjectGraphRelationshipKind.ProjectReference,
            source.Id,
            target.Id,
            ProjectGraphRelationshipDirection.SourceDependsOnTarget,
            source.Project!.Configuration,
            source.Project.Framework,
            RowEvidence(scope, coverage, ProjectGraphProvenance.EvaluatedProjectReference));
    }

    private static ProjectGraphRelationship? PackageRelationship(
        PackageDependency dependency,
        IReadOnlyDictionary<string, ProjectGraphNode> projects,
        IReadOnlyDictionary<string, ProjectGraphNode> packages,
        EvidenceScope scope,
        EvidenceCoverage coverage)
    {
        var source = projects[dependency.Project];
        if (!MatchesVariant(source, dependency.Configuration, dependency.Framework))
        {
            return null;
        }

        var targetId = ProjectGraphIdentity.CreatePackage(
            dependency.PackageId,
            dependency.Version);
        return new ProjectGraphRelationship(
            ProjectGraphRelationshipKind.PackageReference,
            source.Id,
            packages[targetId].Id,
            ProjectGraphRelationshipDirection.SourceDependsOnTarget,
            source.Project!.Configuration,
            source.Project.Framework,
            RowEvidence(scope, coverage, ProjectGraphProvenance.EvaluatedPackageReference));
    }

    private static bool MatchesVariant(
        ProjectGraphNode node,
        string? configuration,
        string? framework) =>
        (configuration is null || string.Equals(
            node.Project!.Configuration,
            configuration,
            StringComparison.Ordinal))
        && (framework is null || string.Equals(
            node.Project!.Framework,
            framework,
            StringComparison.Ordinal));

    private static ProjectGraphEvaluationState PackageState(
        IEnumerable<PackageDependency> dependencies,
        IReadOnlyDictionary<string, ProjectGraphNode> projects) =>
        dependencies
            .Select(dependency => projects.TryGetValue(
                dependency.Project,
                out var source)
                ? source.EvaluationState
                : ProjectGraphEvaluationState.Incomplete)
            .OrderByDescending(StateSeverity)
            .First();

    private static int StateSeverity(ProjectGraphEvaluationState state) =>
        state switch
        {
            ProjectGraphEvaluationState.Failed => 3,
            ProjectGraphEvaluationState.Incomplete => 2,
            ProjectGraphEvaluationState.Unsupported => 1,
            _ => 0,
        };

    private static ProjectGraphEvidence RowEvidence(
        EvidenceScope scope,
        EvidenceCoverage coverage,
        ProjectGraphProvenance provenance) =>
        new(scope, coverage, EvidenceConfidence.Verified, provenance);

    private static async ValueTask<Evidence> EvidenceAsync(
        WorkspaceDiscoveryResult workspace,
        EvaluatedProjectGraph graph,
        ProjectCoverageReport coverage,
        CancellationToken cancellationToken)
    {
        var files = new List<WorkspaceSnapshotFileInput>();
        var fileKeys = new HashSet<(WorkspaceSnapshotFileKind, string)>();
        await AddFileAsync(
                graph.Selection.Kind is WorkspaceEntryPointKind.Solution
                    ? WorkspaceSnapshotFileKind.Solution
                    : WorkspaceSnapshotFileKind.Project,
                graph.Selection.Path)
            .ConfigureAwait(false);
        foreach (var project in graph.Projects.OrderBy(
                     static project => project.Path,
                     StringComparer.Ordinal))
        {
            await AddFileAsync(WorkspaceSnapshotFileKind.Project, project.Path)
                .ConfigureAwait(false);
        }

        var values = graph.GlobalProperties
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .Select(property => new WorkspaceSnapshotValueInput(
                property.Name.Equals("Configuration", StringComparison.OrdinalIgnoreCase)
                    ? WorkspaceSnapshotValueKind.Configuration
                    : property.Name.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase)
                        ? WorkspaceSnapshotValueKind.TargetFramework
                        : WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                property.Name,
                property.Value))
            .ToList();
        if (graph.Runtime is not null)
        {
            values.Add(new WorkspaceSnapshotValueInput(
                WorkspaceSnapshotValueKind.DotNetSdkIdentity,
                "sdk",
                graph.Runtime.SdkVersion));
            values.Add(new WorkspaceSnapshotValueInput(
                WorkspaceSnapshotValueKind.MsBuildIdentity,
                "msbuild",
                graph.Runtime.MsBuildVersion));
        }

        var snapshot = new WorkspaceSnapshotCapturer().Capture(
            new WorkspaceSnapshotCapture(
                files,
                values,
                new WorkspaceSnapshotEntryPointInput(graph.Selection)));
        return new Evidence(
            snapshot.Identity,
            EvidenceResolution.Syntax,
            coverage.Coverage,
            EvidenceConfidence.Verified,
            new EvidenceScope(
                workspace.RootPath,
                "evaluated MSBuild project graph",
                solution: graph.Selection.Kind is WorkspaceEntryPointKind.Solution
                    ? graph.Selection.Path
                    : null,
                projects: graph.Projects.Select(static project => project.Path),
                frameworks: graph.Projects
                    .Select(static project => project.Framework)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
                configuration: graph.GlobalProperties
                    .FirstOrDefault(static property => property.Name.Equals(
                        "Configuration",
                        StringComparison.OrdinalIgnoreCase))?.Value));

        async ValueTask AddFileAsync(WorkspaceSnapshotFileKind kind, string path)
        {
            if (!fileKeys.Add((kind, path)))
            {
                return;
            }

            var fullPath = Path.GetFullPath(
                path.Replace('/', Path.DirectorySeparatorChar),
                workspace.RootPath);
            if (!File.Exists(fullPath))
            {
                return;
            }

            files.Add(new WorkspaceSnapshotFileInput(
                kind,
                path,
                await File.ReadAllBytesAsync(fullPath, cancellationToken)
                    .ConfigureAwait(false)));
        }
    }

    private static ProjectGraphCoveragePayload Variant(ProjectVariantCoverage variant) =>
        new(
            variant.Project,
            variant.Configuration,
            variant.Framework,
            variant.IsSelected,
            variant.State.ToString().ToLowerInvariant(),
            variant.Issues.Select(issue => new ProjectGraphCoverageIssuePayload(
                issue.Reason.ToString().ToLowerInvariant(),
                issue.AuthorityCode,
                issue.Correction)).ToArray());

    private static string RetrievalCommand(ProjectGraphCommandRequest request)
    {
        var command = request.DependencyProject is null
            ? "dnaxi graph projects"
            : "dnaxi graph dependencies " + Quote(request.DependencyProject);
        if (request.Solution is not null)
        {
            command += " --solution " + Quote(request.Solution);
        }

        if (request.EntryProject is not null)
        {
            command += " --project " + Quote(request.EntryProject);
        }

        if (request.Configuration is not null)
        {
            command += " --configuration " + Quote(request.Configuration);
        }

        if (request.Framework is not null)
        {
            command += " --framework " + Quote(request.Framework);
        }

        foreach (var property in request.Properties)
        {
            command += " --property " + Quote(property.Name + "=" + property.Value);
        }

        return command;
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record MaterializedProject(
        string Path,
        string? Configuration,
        string? Framework,
        ProjectGraphEvaluationState EvaluationState)
    {
        public static MaterializedProject Create(
            EvaluatedProject project,
            ProjectCoverageReport coverage)
        {
            var variant = coverage.Variants
                .Where(candidate => candidate.Project.Equals(
                    project.Path,
                    StringComparison.Ordinal))
                .OrderByDescending(static candidate => candidate.IsSelected)
                .ThenBy(candidate => candidate.State
                    is ProjectVariantCoverageState.Supported ? 1 : 0)
                .ThenBy(static candidate => candidate.Configuration, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.Framework, StringComparer.Ordinal)
                .FirstOrDefault();
            return new MaterializedProject(
                project.Path,
                variant?.Configuration ?? project.Configuration,
                variant?.Framework ?? project.Framework,
                variant?.State is ProjectVariantCoverageState.Unsupported
                    ? ProjectGraphEvaluationState.Unsupported
                    : project.State switch
                    {
                        EvaluatedProjectState.Evaluated => ProjectGraphEvaluationState.Evaluated,
                        EvaluatedProjectState.Incomplete => ProjectGraphEvaluationState.Incomplete,
                        _ => ProjectGraphEvaluationState.Failed,
                    });
        }
    }
}

internal sealed record ProjectGraphQuery(
    WorkspaceDiscoveryResult Workspace,
    EvaluatedProjectGraph Evaluated,
    ProjectCoverageReport Coverage,
    Evidence Evidence,
    ProjectDependencyGraph Graph);

internal sealed record ProjectGraphPayload(
    BoundedCollection<ProjectGraphNode> Nodes,
    BoundedCollection<ProjectGraphRelationship> Relationships,
    IReadOnlyList<ProjectEvaluationFailure> Failures,
    IReadOnlyList<ProjectGraphCoveragePayload> Variants);

internal sealed record ProjectGraphCoveragePayload(
    string Project,
    string? Configuration,
    string? Framework,
    bool Selected,
    string State,
    IReadOnlyList<ProjectGraphCoverageIssuePayload> Issues);

internal sealed record ProjectGraphCoverageIssuePayload(
    string Reason,
    string? AuthorityCode,
    string Correction);
