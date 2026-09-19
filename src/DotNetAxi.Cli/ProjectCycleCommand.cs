using DotNetAxi.Contracts;
using DotNetAxi.Graph;

namespace DotNetAxi.Cli;

internal sealed record ProjectCycleCommandRequest(ProjectGraphCommandRequest GraphRequest)
{
    public static ProjectCycleCommandRequest Create(
        string? solution,
        string? entryProject,
        string? configuration,
        string? framework,
        IReadOnlyList<string> properties,
        int limit,
        bool limitSpecified,
        bool full) =>
        new(ProjectGraphCommandRequest.Create(
            dependencyProject: null,
            solution,
            entryProject,
            configuration,
            framework,
            properties,
            limit,
            limitSpecified,
            full));
}

internal sealed class ProjectCycleCommandHandler :
    ICommandHandler<ProjectCycleCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        ProjectCycleCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = await ProjectGraphCommandHandler.QueryAsync(
                request.GraphRequest,
                cancellationToken)
            .ConfigureAwait(false);
        var observedGraph = ProjectGraphCommandHandler.Materialize(
            query.Evaluated,
            query.Coverage,
            query.Evidence,
            includeIncompleteProjectReferences: true);
        var maximumCycles = request.GraphRequest.Full
            ? ProjectDependencyCycleDetector.MaximumDetectedCycles
            : request.GraphRequest.Limit >= ProjectDependencyCycleDetector.MaximumDetectedCycles
                ? ProjectDependencyCycleDetector.MaximumDetectedCycles
                : request.GraphRequest.Limit + 1;
        var detection = ProjectDependencyCycleDetector.FindCycles(
            observedGraph,
            maximumCycles,
            cancellationToken);
        var retrievalCommand = RetrievalCommand(request.GraphRequest);
        var visibleCycles = request.GraphRequest.Full
            ? detection.Cycles
            : detection.Cycles.Take(request.GraphRequest.Limit).ToArray();
        var bounded = BoundedCollection<ProjectDependencyCycle>.FromObserved(
            visibleCycles,
            detection.TotalKnown ? detection.Cycles.Count : null,
            detection.TotalKnown,
            retrievalCommand + " --full");
        var payload = new ProjectCyclePayload(
            bounded,
            new ProjectCycleDetectionPayload(
                maximumCycles,
                ProjectDependencyCycleDetector.MaximumTraversalSteps,
                detection.TotalKnown),
            query.Evaluated.Failures,
            query.Coverage.Variants.Select(ProjectGraphCoveragePayloadFrom).ToArray());
        return query.Coverage.Coverage.Level is CoverageLevel.Complete
            ? CommandResult<ProjectCyclePayload>.Success("graph cycles", payload, query.Evidence)
            : CommandResult<ProjectCyclePayload>.Partial("graph cycles", payload, query.Evidence);
    }

    private static ProjectGraphCoveragePayload ProjectGraphCoveragePayloadFrom(
        DotNetAxi.Workspaces.ProjectVariantCoverage variant) =>
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
        var command = "dnaxi graph cycles";
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
}

internal sealed record ProjectCyclePayload(
    BoundedCollection<ProjectDependencyCycle> Cycles,
    ProjectCycleDetectionPayload Detection,
    IReadOnlyList<DotNetAxi.Workspaces.ProjectEvaluationFailure> Failures,
    IReadOnlyList<ProjectGraphCoveragePayload> Variants);

internal sealed record ProjectCycleDetectionPayload(
    int MaximumCycles,
    int MaximumTraversalSteps,
    bool TotalKnown);
