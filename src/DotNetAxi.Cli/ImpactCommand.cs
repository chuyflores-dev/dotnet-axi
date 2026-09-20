using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record ImpactCommandRequest(
    CallerSearchCommandRequest RelationshipRequest,
    int MaxDepth)
{
    public static ImpactCommandRequest Create(
        string target,
        string? solution,
        string? project,
        bool includeTests,
        bool includeGenerated,
        string? configuration,
        string? framework,
        IReadOnlyList<string> properties,
        IReadOnlyList<string> paths,
        bool complete,
        int maxDepth,
        int limit,
        bool limitSpecified,
        bool full)
    {
        if (maxDepth < 0)
        {
            throw new CommandUsageException(
                "usage.impact_depth",
                "The --max-depth value must be non-negative.",
                "Use a non-negative --max-depth value.");
        }

        var relationship = CallerSearchCommandRequest.Create(
                target, solution, project, includeTests, includeGenerated,
                configuration, framework, properties, complete, limit,
                limitSpecified, full, fields: []);

        return new(
            relationship with
            {
                Scope = SymbolWorkspaceScopeRequest.Create(
                    solution, project, paths, includeTests, includeGenerated, "usage.impact_path"),
            },
            maxDepth);
    }
}

internal sealed class ImpactCommandHandler : ICommandHandler<ImpactCommandRequest>
{
    private static readonly IReadOnlySet<string> TestPackages = new HashSet<string>(
        ["microsoft.net.test.sdk", "mstest.testadapter", "nunit", "nunit3testadapter", "xunit"],
        StringComparer.OrdinalIgnoreCase);

    public async ValueTask<ICommandResult> HandleAsync(
        ImpactCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var relationship = request.RelationshipRequest;
        if (relationship.Target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleProjectAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var workspace = new WorkspaceDiscoverer().Discover(Directory.GetCurrentDirectory());
        var scope = SymbolWorkspaceScopeResolver.Resolve(workspace, relationship.Scope);
        if (scope.Selection is null)
        {
            throw new CommandUsageException(
                "usage.impact_workspace_selection_required",
                "Impact analysis requires one selected solution or C# project.",
                "Add or select a .sln, .slnx, or .csproj with --solution or --project.");
        }

        RoslynImpactAnalysis analysis;
        try
        {
            analysis = await new RoslynImpactAnalyzer(
                    scope.Traverser,
                    scope.Ownership,
                    scope.Projects)
                .AnalyzeAsync(
                    relationship.Target,
                    workspace,
                    scope.Selection,
                    scope.Traversal,
                    scope.DeclarationScope,
                    relationship.Complete,
                    relationship.EvaluationOptions(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProjectGraphUsageException exception)
        {
            throw new CommandUsageException(
                exception.Code,
                exception.Message,
                exception.Correction,
                exception.Declarations.Select(static declaration => declaration.Project));
        }

        var evidence = Evidence(analysis, scope);
        if (!analysis.TargetResolved)
        {
            var failurePayload = new ImpactFailurePayload(
                analysis.Target,
                analysis.TargetStatus.ToString().ToLowerInvariant(),
                analysis.Candidates,
                analysis.CandidateTotal,
                analysis.ErrorCode,
                analysis.Correction);
            return CommandResult<ImpactFailurePayload>.Failed(
                "graph impact",
                [new ResultError(
                    analysis.ErrorCode ?? "semantic.target_unresolved",
                    $"The impact target `{analysis.Target}` could not be resolved ({analysis.TargetStatus.ToString().ToLowerInvariant()}).",
                    analysis.Correction ?? "Correct the semantic target and retry.")],
                failurePayload,
                evidence);
        }

        var affected = AffectedProjects(analysis.ProjectGraph!, analysis.TargetProjects, request.MaxDepth, cancellationToken);
        var documents = AffectedDocuments(analysis);
        var tests = CandidateTests(analysis.ProjectGraph!, affected.Projects);
        var limit = relationship.Limit;
        var full = relationship.Full;
        var retrieval = RetrievalCommand(request);
        var payload = new ImpactPayload(
            analysis.Target,
            analysis.TargetId!,
            new ImpactPublicSurfacePayload(
                "applicable",
                analysis.Declarations.Any(static declaration => declaration.Accessibility is "public" or "protected" or "protected-internal"),
                analysis.Declarations),
            Bounded(affected.Projects, limit, full, retrieval),
            Bounded(documents, limit, full, retrieval),
            Bounded(tests, limit, full, retrieval),
            Bounded(affected.Paths, limit, full, retrieval),
            new ImpactRelationshipPayload(
                "applicable",
                Bounded(analysis.References!.Matches, limit, full, retrieval),
                Bounded(analysis.Implementations!.Matches, limit, full, retrieval),
                Bounded(analysis.Callers!.Matches, limit, full, retrieval),
                Bounded(analysis.Callees!.Matches, limit, full, retrieval)),
            new ImpactExpansionPayload(request.MaxDepth, affected.DepthLimited, relationship.Complete),
            analysis.PartialReasons);
        return analysis.Coverage.Level is CoverageLevel.Complete
            ? CommandResult<ImpactPayload>.Success("graph impact", payload, evidence)
            : CommandResult<ImpactPayload>.Partial("graph impact", payload, evidence);
    }

    private static BoundedCollection<T> Bounded<T>(
        IReadOnlyList<T> items,
        int limit,
        bool full,
        string retrievalCommand) => BoundedCollection<T>.FromObserved(
            full ? items : items.Take(limit),
            items.Count,
            totalKnown: true,
            retrievalCommand + " --full");

    private static (IReadOnlyList<ImpactProjectPayload> Projects, IReadOnlyList<ImpactPathPayload> Paths, bool DepthLimited) AffectedProjects(
        EvaluatedProjectGraph graph,
        IReadOnlyList<string> targetProjects,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var byPath = graph.Projects.ToDictionary(static project => project.Path, StringComparer.Ordinal);
        var reverse = graph.Dependencies
            .GroupBy(static dependency => dependency.Dependency, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static dependency => dependency.Project)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var queue = new Queue<(string Project, int Depth, IReadOnlyList<string> Path)>();
        var seen = new Dictionary<string, (int Depth, IReadOnlyList<string> Path)>(StringComparer.Ordinal);
        foreach (var project in targetProjects)
        {
            if (byPath.ContainsKey(project) && seen.TryAdd(project, (0, [project])))
            {
                queue.Enqueue((project, 0, [project]));
            }
        }

        var depthLimited = false;
        while (queue.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Depth == maxDepth)
            {
                depthLimited |= reverse.ContainsKey(current.Project);
                continue;
            }

            if (!reverse.TryGetValue(current.Project, out var dependents))
            {
                continue;
            }

            foreach (var dependent in dependents)
            {
                if (seen.ContainsKey(dependent))
                {
                    continue;
                }

                var path = current.Path.Append(dependent).ToArray();
                seen.Add(dependent, (current.Depth + 1, path));
                queue.Enqueue((dependent, current.Depth + 1, path));
            }
        }

        var projects = seen
            .OrderBy(static pair => pair.Value.Depth)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ImpactProjectPayload(pair.Key, pair.Value.Depth, byPath[pair.Key].State.ToString().ToLowerInvariant()))
            .ToArray();
        var paths = seen.Values
            .Where(static value => value.Depth > 0)
            .OrderBy(static value => value.Depth)
            .ThenBy(static value => string.Join("\u001F", value.Path), StringComparer.Ordinal)
            .Select(static value => new ImpactPathPayload(value.Path, value.Depth))
            .ToArray();
        return (projects, paths, depthLimited);
    }

    private static async ValueTask<ICommandResult> HandleProjectAsync(
        ImpactCommandRequest request,
        CancellationToken cancellationToken)
    {
        var relationship = request.RelationshipRequest;
        var query = await ProjectGraphCommandHandler.QueryAsync(
                ProjectGraphCommandRequest.Create(
                    dependencyProject: null,
                    relationship.Scope.Solution,
                    relationship.Scope.Project,
                    relationship.Configuration,
                    relationship.Framework,
                    relationship.Properties.Select(static property => property.Name + "=" + property.Value).ToArray(),
                    relationship.Limit,
                    relationship.LimitSpecified,
                    relationship.Full),
                cancellationToken)
            .ConfigureAwait(false);
        string path;
        try
        {
            path = new WorkspacePathResolver(query.Workspace.RootPath, query.Workspace.CurrentDirectory)
                .ResolveInput(relationship.Target).Path;
        }
        catch (WorkspacePathScopeException)
        {
            throw new CommandUsageException(
                "usage.impact_project_target",
                "The project impact target must be within the workspace.",
                "Provide an evaluated workspace-relative .csproj path.");
        }

        var node = query.Graph.Nodes.SingleOrDefault(node =>
            node.Kind is DotNetAxi.Graph.ProjectGraphNodeKind.Project
            && node.Project!.ProjectPath.Equals(path, StringComparison.Ordinal));
        if (node is null)
        {
            throw new CommandUsageException(
                "usage.impact_project_target",
                $"The project `{relationship.Target}` was not evaluated in the selected graph.",
                "Select a graph scope containing the project impact target.");
        }

        var affected = AffectedProjects(query.Evaluated, [path], request.MaxDepth, cancellationToken);
        var retrieval = RetrievalCommand(request);
        var payload = new ImpactPayload(
            relationship.Target,
            node.Id,
            new ImpactPublicSurfacePayload("not_applicable", null, []),
            Bounded(affected.Projects, relationship.Limit, relationship.Full, retrieval),
            Bounded(Array.Empty<ImpactDocumentPayload>(), relationship.Limit, relationship.Full, retrieval),
            Bounded(CandidateTests(query.Evaluated, affected.Projects), relationship.Limit, relationship.Full, retrieval),
            Bounded(affected.Paths, relationship.Limit, relationship.Full, retrieval),
            new ImpactRelationshipPayload(
                "not_applicable",
                Bounded(Array.Empty<RoslynReferenceMatch>(), relationship.Limit, relationship.Full, retrieval),
                Bounded(Array.Empty<RoslynImplementationMatch>(), relationship.Limit, relationship.Full, retrieval),
                Bounded(Array.Empty<RoslynCallerMatch>(), relationship.Limit, relationship.Full, retrieval),
                Bounded(Array.Empty<RoslynCalleeMatch>(), relationship.Limit, relationship.Full, retrieval)),
            new ImpactExpansionPayload(request.MaxDepth, affected.DepthLimited, relationship.Complete),
            ["Code relationship and public-surface sections are not applicable to a project target."]);
        return query.Coverage.Coverage.Level is CoverageLevel.Complete
            ? CommandResult<ImpactPayload>.Success("graph impact", payload, query.Evidence)
            : CommandResult<ImpactPayload>.Partial("graph impact", payload, query.Evidence);
    }

    private static IReadOnlyList<ImpactDocumentPayload> AffectedDocuments(RoslynImpactAnalysis analysis) =>
        analysis.Declarations.Select(static declaration => (declaration.Range.Start.Path, "target_declaration"))
            .Concat(analysis.References!.Matches.Select(static match => (match.Start.Path, "reference")))
            .Concat(analysis.Implementations!.Matches.Select(static match => (match.Start.Path, "implementation")))
            .Concat(analysis.Callers!.Matches.Select(static match => (match.Start.Path, "caller")))
            .Concat(analysis.Callees!.Matches.Select(static match => (match.Start.Path, "callee")))
            .GroupBy(static item => item.Path, StringComparer.Ordinal)
            .Select(static group => new ImpactDocumentPayload(group.Key, group.Select(static item => item.Item2).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(static document => document.Path, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ImpactTestPayload> CandidateTests(
        EvaluatedProjectGraph graph,
        IReadOnlyList<ImpactProjectPayload> affectedProjects) =>
        graph.PackageDependencies
            .Where(dependency => TestPackages.Contains(dependency.PackageId)
                && affectedProjects.Any(project => project.Project.Equals(dependency.Project, StringComparison.Ordinal)))
            .GroupBy(static dependency => dependency.Project, StringComparer.Ordinal)
            .Select(group => new ImpactTestPayload(
                group.Key,
                group.Select(static dependency => dependency.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                "test_framework_package_reference"))
            .OrderBy(static candidate => candidate.Project, StringComparer.Ordinal)
            .ToArray();

    private static Evidence? Evidence(RoslynImpactAnalysis analysis, ResolvedSymbolWorkspaceScope scope) =>
        analysis.Snapshot is null ? null : new Evidence(
            analysis.Snapshot,
            EvidenceResolution.Semantic,
            analysis.Coverage,
            EvidenceConfidence.Verified,
            new EvidenceScope(
                scope.Workspace.RootPath,
                "resolved target with shared semantic impact traversal",
                solution: scope.Selection?.Kind is WorkspaceEntryPointKind.Solution ? scope.Selection.Path : null,
                projects: analysis.TargetProjects,
                eligibility: new EvidenceEligibility(
                    scope.DeclarationScope.IncludeTests,
                    scope.Traversal.IncludeGenerated == true)));

    private static string RetrievalCommand(ImpactCommandRequest request)
    {
        var relationship = request.RelationshipRequest;
        var command = "dnaxi graph impact " + Quote(relationship.Target);
        if (relationship.Scope.Solution is not null)
        {
            command += " --solution " + Quote(relationship.Scope.Solution);
        }

        if (relationship.Scope.Project is not null)
        {
            command += " --project " + Quote(relationship.Scope.Project);
        }

        foreach (var path in relationship.Scope.Paths)
        {
            command += " --path " + Quote(path);
        }

        if (relationship.Scope.IncludeTests)
        {
            command += " --include-tests";
        }

        if (relationship.Scope.IncludeGenerated)
        {
            command += " --include-generated";
        }

        if (relationship.Configuration is not null)
        {
            command += " --configuration " + Quote(relationship.Configuration);
        }

        if (relationship.Framework is not null)
        {
            command += " --framework " + Quote(relationship.Framework);
        }

        foreach (var property in relationship.Properties)
        {
            command += " --property " + Quote(property.Name + "=" + property.Value);
        }

        if (relationship.Complete)
        {
            command += " --complete";
        }

        command += " --max-depth " + request.MaxDepth;
        return command;
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

internal sealed record ImpactPayload(
    string Target,
    string TargetId,
    ImpactPublicSurfacePayload PublicSurface,
    BoundedCollection<ImpactProjectPayload> AffectedProjects,
    BoundedCollection<ImpactDocumentPayload> AffectedDocuments,
    BoundedCollection<ImpactTestPayload> CandidateTests,
    BoundedCollection<ImpactPathPayload> ImportantPaths,
    ImpactRelationshipPayload Relationships,
    ImpactExpansionPayload Expansion,
    IReadOnlyList<string> Limitations);
internal sealed record ImpactFailurePayload(string Target, string TargetStatus, IReadOnlyList<SymbolDeclarationMatch> Candidates, int CandidateTotal, string? ErrorCode, string? Correction);
internal sealed record ImpactPublicSurfacePayload(string Applicability, bool? IsPublicOrProtected, IReadOnlyList<SymbolDeclarationMatch> Declarations);
internal sealed record ImpactProjectPayload(string Project, int Depth, string EvaluationState);
internal sealed record ImpactDocumentPayload(string Path, IReadOnlyList<string> Reasons);
internal sealed record ImpactTestPayload(string Project, IReadOnlyList<string> Packages, string SelectionReason);
internal sealed record ImpactPathPayload(IReadOnlyList<string> Projects, int Depth);
internal sealed record ImpactRelationshipPayload(string Applicability, BoundedCollection<RoslynReferenceMatch> References, BoundedCollection<RoslynImplementationMatch> Implementations, BoundedCollection<RoslynCallerMatch> Callers, BoundedCollection<RoslynCalleeMatch> Callees);
internal sealed record ImpactExpansionPayload(int MaxDepth, bool DepthLimited, bool CompleteScope);
