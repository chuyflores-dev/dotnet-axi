using DotNetAxi.Contracts;
using DotNetAxi.Graph;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record ProjectPathCommandRequest(
    string From,
    string To,
    int MaxDepth,
    ProjectGraphCommandRequest GraphRequest)
{
    public static ProjectPathCommandRequest Create(
        string from, string to, int maxDepth, string? solution, string? project,
        string? configuration, string? framework, IReadOnlyList<string> properties,
        int limit, bool limitSpecified, bool full)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || maxDepth < 0)
        {
            throw new CommandUsageException("usage.graph_path", "Path endpoints and --max-depth must be valid.", "Provide non-blank --from/--to project paths and a non-negative --max-depth.");
        }

        return new(from.Trim(), to.Trim(), maxDepth, ProjectGraphCommandRequest.Create(
            null, solution, project, configuration, framework, properties, limit, limitSpecified, full));
    }
}

internal sealed class ProjectPathCommandHandler : ICommandHandler<ProjectPathCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(ProjectPathCommandRequest request, CancellationToken cancellationToken)
    {
        var query = await ProjectGraphCommandHandler.QueryAsync(request.GraphRequest, cancellationToken).ConfigureAwait(false);
        var from = ResolveNode(query.Graph, request.From, query.Workspace);
        var to = ResolveNode(query.Graph, request.To, query.Workspace);
        var maximumPaths = request.GraphRequest.Full
            ? ProjectDependencyPathFinder.MaximumDetectedPaths
            : request.GraphRequest.Limit >= ProjectDependencyPathFinder.MaximumDetectedPaths
                ? ProjectDependencyPathFinder.MaximumDetectedPaths
                : request.GraphRequest.Limit + 1;
        var search = ProjectDependencyPathFinder.FindShortestPaths(
            query.Graph, from.Id, to.Id, request.MaxDepth, maximumPaths, cancellationToken);
        var visible = request.GraphRequest.Full ? search.Paths : search.Paths.Take(request.GraphRequest.Limit).ToArray();
        var paths = BoundedCollection<ProjectDependencyPath>.FromObserved(
            visible, search.TotalKnown ? search.Paths.Count : null, search.TotalKnown,
            RetrievalCommand(request) + " --full");
        var payload = new ProjectPathPayload(paths, search.ShortestDepth, search.DepthLimited,
            new ProjectPathDetectionPayload(maximumPaths, search.TotalKnown),
            query.Evaluated.Failures, query.Coverage.Variants.Select(ProjectGraphCoverage).ToArray());
        return query.Coverage.Coverage.Level is CoverageLevel.Complete
            ? CommandResult<ProjectPathPayload>.Success("graph path", payload, query.Evidence)
            : CommandResult<ProjectPathPayload>.Partial("graph path", payload, query.Evidence);
    }

    private static ProjectGraphNode ResolveNode(ProjectDependencyGraph graph, string input, WorkspaceDiscoveryResult workspace)
    {
        string path;
        try { path = new WorkspacePathResolver(workspace.RootPath, workspace.CurrentDirectory).ResolveInput(input).Path; }
        catch (WorkspacePathScopeException) { throw new CommandUsageException("usage.graph_path_endpoint", "Path endpoints must be within the workspace.", "Provide evaluated workspace-relative project paths."); }
        var node = graph.Nodes.SingleOrDefault(node => node.Kind is ProjectGraphNodeKind.Project && node.Project!.ProjectPath.Equals(path, StringComparison.Ordinal));
        return node ?? throw new CommandUsageException("usage.graph_path_endpoint", $"The project `{input}` was not evaluated.", "Select a graph scope containing both endpoints.");
    }

    private static ProjectGraphCoveragePayload ProjectGraphCoverage(ProjectVariantCoverage variant) => new(variant.Project, variant.Configuration, variant.Framework, variant.IsSelected, variant.State.ToString().ToLowerInvariant(), variant.Issues.Select(issue => new ProjectGraphCoverageIssuePayload(issue.Reason.ToString().ToLowerInvariant(), issue.AuthorityCode, issue.Correction)).ToArray());

    internal static string RetrievalCommand(ProjectPathCommandRequest request)
    {
        var command = "dnaxi graph path --from " + Quote(request.From) + " --to " + Quote(request.To)
            + " --max-depth " + request.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var graph = request.GraphRequest;
        if (graph.Solution is not null)
        {
            command += " --solution " + Quote(graph.Solution);
        }

        if (graph.EntryProject is not null)
        {
            command += " --project " + Quote(graph.EntryProject);
        }

        if (graph.Configuration is not null)
        {
            command += " --configuration " + Quote(graph.Configuration);
        }

        if (graph.Framework is not null)
        {
            command += " --framework " + Quote(graph.Framework);
        }

        foreach (var property in graph.Properties)
        {
            command += " --property " + Quote(property.Name + "=" + property.Value);
        }

        return command;
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

internal sealed record ProjectPathPayload(
    BoundedCollection<ProjectDependencyPath> Paths,
    int? ShortestDepth,
    bool DepthLimited,
    ProjectPathDetectionPayload Detection,
    IReadOnlyList<ProjectEvaluationFailure> Failures,
    IReadOnlyList<ProjectGraphCoveragePayload> Variants);

internal sealed record ProjectPathDetectionPayload(int MaximumPaths, bool TotalKnown);
