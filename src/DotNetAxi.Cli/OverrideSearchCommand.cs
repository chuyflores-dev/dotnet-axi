using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record OverrideSearchCommandRequest(ImplementationSearchCommandRequest Shared)
{
    public static OverrideSearchCommandRequest Create(string target, string? solution, string? project,
        bool includeTests, bool includeGenerated, string? configuration, string? framework,
        IReadOnlyList<string> properties, bool complete, int limit, bool limitSpecified, bool full) => new(
        ImplementationSearchCommandRequest.Create(target, solution, project, includeTests, includeGenerated,
            configuration, framework, properties, complete, limit, limitSpecified, full, []));
}

internal sealed class OverrideSearchCommandHandler : ICommandHandler<OverrideSearchCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(OverrideSearchCommandRequest request, CancellationToken cancellationToken)
    {
        var shared = request.Shared;
        var workspace = new WorkspaceDiscoverer().Discover(Directory.GetCurrentDirectory());
        var scope = SymbolWorkspaceScopeResolver.Resolve(workspace, shared.Scope);
        if (scope.Selection is null)
            throw new CommandUsageException("usage.override_workspace_selection_required", "Override search requires one selected solution or C# project.", "Add or select a .sln, .slnx, or .csproj with --solution or --project.");
        var result = await new RoslynOverrideSearcher(scope.Traverser, scope.Ownership, scope.Projects).FindAsync(
            shared.Target, workspace, scope.Selection, scope.Traversal, scope.DeclarationScope,
            shared.Complete ? OverrideSearchScopeMode.Complete : OverrideSearchScopeMode.Default,
            shared.EvaluationOptions(), cancellationToken).ConfigureAwait(false);
        var evidence = result.Snapshot is null ? null : new Evidence(result.Snapshot, EvidenceResolution.Semantic, result.Coverage, EvidenceConfidence.Verified,
            new EvidenceScope(scope.Workspace.RootPath, "evaluated reverse project-graph override candidates", null,
                result.Variants.Select(static x => x.Project).Distinct().Order().ToArray(), result.Variants.Select(static x => x.Framework).OfType<string>().Distinct().Order().ToArray(), eligibility: new EvidenceEligibility(scope.Request.IncludeTests, scope.Request.IncludeGenerated)));
        if (!result.TargetResolved)
            return CommandResult<Failure>.Failed("search overrides", [new ResultError(result.ErrorCode ?? "semantic.target_unresolved", "The semantic target could not be resolved.", result.Correction ?? "Correct the target and retry.")], new Failure(result.TargetStatus.ToString().ToLowerInvariant()), evidence);
        var matches = (shared.Full ? result.Matches : result.Matches.Take(shared.Limit)).Select(static match => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["file"] = match.Start.Path, ["line"] = match.Start.Line, ["project"] = match.Project, ["framework"] = match.Framework, ["override_identity"] = match.OverrideIdentity, ["override_path"] = match.OverridePath });
        var bounded = BoundedCollection<IReadOnlyDictionary<string, object?>>.FromObserved(matches, result.Matches.Count, true, CanonicalInvocation.OneShot("dnaxi search overrides '" + shared.Target + "' --full"));
        var payload = new Payload(OperationClassification.Executing, result.Target, result.TargetId!, shared.Complete ? "complete" : "default", bounded.Count, bounded.TotalKnown, bounded.Total, bounded.Omitted, bounded.Truncated, bounded.RetrievalCommand, result.PartialReasons, bounded.Items);
        return result.Coverage.Level is CoverageLevel.Complete ? CommandResult<Payload>.Success("search overrides", payload, evidence) : CommandResult<Payload>.Partial("search overrides", payload, evidence);
    }
    private sealed record Payload(OperationClassification Classification, string Target, string TargetId, string ScopeMode, int Count, bool TotalKnown, int? Total, int? Omitted, bool Truncated, string? RetrievalCommand, IReadOnlyList<string> PartialReasons, IReadOnlyList<IReadOnlyDictionary<string, object?>> Matches);
    private sealed record Failure(string TargetStatus);
}
