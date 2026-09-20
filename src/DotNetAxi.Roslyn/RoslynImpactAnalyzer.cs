using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn;

/// <summary>
/// Composes bounded static relationship evidence for one exact semantic target.
/// The operation resolves its target and loads project/compiler state once.
/// </summary>
public sealed class RoslynImpactAnalyzer
{
    private readonly IReadOnlyList<string> _projects;
    private readonly RoslynSemanticTargetResolver _targetResolver;
    private readonly MsBuildProjectGraphEvaluator _graphEvaluator;
    private readonly MsBuildCompilerVariantResolver _variantResolver;
    private readonly RoslynReferenceSearcher _references;
    private readonly RoslynImplementationSearcher _implementations;
    private readonly RoslynCallerSearcher _callers;
    private readonly RoslynCalleeSearcher _callees;

    public RoslynImpactAnalyzer(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects)
        : this(traverser, ownership, projects, new DotNetHostResolver())
    {
    }

    internal RoslynImpactAnalyzer(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects,
        IDotNetHostResolver hostResolver)
    {
        ArgumentNullException.ThrowIfNull(traverser);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(hostResolver);
        _projects = Array.AsReadOnly(projects
            .Distinct(PathComparer())
            .Order(StringComparer.Ordinal)
            .ToArray());
        _targetResolver = new RoslynSemanticTargetResolver(
            traverser,
            ownership,
            _projects);
        _graphEvaluator = new MsBuildProjectGraphEvaluator(hostResolver);
        _variantResolver = new MsBuildCompilerVariantResolver(hostResolver);
        _references = new RoslynReferenceSearcher(traverser, ownership, _projects, hostResolver);
        _implementations = new RoslynImplementationSearcher(traverser, ownership, _projects, hostResolver);
        _callers = new RoslynCallerSearcher(traverser, ownership, _projects, hostResolver);
        _callees = new RoslynCalleeSearcher(traverser, ownership, _projects, hostResolver);
    }

    public async ValueTask<RoslynImpactAnalysis> AnalyzeAsync(
        string target,
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? declarationScope,
        bool complete,
        ProjectGraphEvaluationOptions evaluationOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(evaluationOptions);
        declarationScope ??= new SymbolDeclarationScope(
            selection.Kind is WorkspaceEntryPointKind.Solution ? selection.Path : null,
            _projects,
            traversal.ExplicitPaths,
            includeTests: false,
            includeGenerated: traversal.IncludeGenerated == true);

        using var session = new SemanticQuerySession(
            evaluationOptions,
            _graphEvaluator,
            _variantResolver);
        using var resolution = await _targetResolver.ResolveAsync(
                target,
                traversal,
                declarationScope,
                evaluationOptions,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        if (!resolution.Resolved)
        {
            return RoslynImpactAnalysis.Unresolved(resolution);
        }

        var graph = session.GetProjectGraph(discovery, selection, cancellationToken);
        var referenceScope = complete
            ? ReferenceSearchScopeMode.Complete
            : ReferenceSearchScopeMode.Default;
        var implementationScope = complete
            ? ImplementationSearchScopeMode.Complete
            : ImplementationSearchScopeMode.Default;
        var callerScope = complete
            ? CallerSearchScopeMode.Complete
            : CallerSearchScopeMode.Default;
        var calleeScope = complete
            ? CalleeSearchScopeMode.Complete
            : CalleeSearchScopeMode.Default;
        var references = await _references.FindResolvedAsync(
                target, discovery, selection, traversal, declarationScope, referenceScope,
                session, resolution, cancellationToken)
            .ConfigureAwait(false);
        var implementations = await _implementations.FindResolvedAsync(
                target, discovery, selection, traversal, declarationScope, implementationScope,
                session, resolution, cancellationToken)
            .ConfigureAwait(false);
        var callers = await _callers.FindResolvedAsync(
                target, discovery, selection, traversal, declarationScope, callerScope,
                session, resolution, cancellationToken)
            .ConfigureAwait(false);
        var callees = await _callees.FindResolvedAsync(
                target, discovery, selection, traversal, declarationScope, calleeScope,
                session, resolution, cancellationToken)
            .ConfigureAwait(false);
        return RoslynImpactAnalysis.Resolved(
            resolution,
            graph,
            references,
            implementations,
            callers,
            callees);
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed class RoslynImpactAnalysis
{
    private RoslynImpactAnalysis(
        string target,
        string? targetId,
        SemanticTargetResolutionStatus targetStatus,
        string? snapshot,
        IReadOnlyList<SymbolDeclarationMatch> declarations,
        IReadOnlyList<SymbolDeclarationMatch> candidates,
        int candidateTotal,
        string? errorCode,
        string? correction,
        IReadOnlyList<string> targetProjects,
        EvaluatedProjectGraph? projectGraph,
        RoslynReferenceSearchResult? references,
        RoslynImplementationSearchResult? implementations,
        RoslynCallerSearchResult? callers,
        RoslynCalleeSearchResult? callees,
        EvidenceCoverage coverage,
        IReadOnlyList<string> partialReasons)
    {
        Target = target;
        TargetId = targetId;
        TargetStatus = targetStatus;
        Snapshot = snapshot;
        Declarations = declarations;
        Candidates = candidates;
        CandidateTotal = candidateTotal;
        ErrorCode = errorCode;
        Correction = correction;
        TargetProjects = targetProjects;
        ProjectGraph = projectGraph;
        References = references;
        Implementations = implementations;
        Callers = callers;
        Callees = callees;
        Coverage = coverage;
        PartialReasons = partialReasons;
    }

    public string Target { get; }
    public string? TargetId { get; }
    public SemanticTargetResolutionStatus TargetStatus { get; }
    public string? Snapshot { get; }
    public IReadOnlyList<SymbolDeclarationMatch> Declarations { get; }
    public IReadOnlyList<SymbolDeclarationMatch> Candidates { get; }
    public int CandidateTotal { get; }
    public string? ErrorCode { get; }
    public string? Correction { get; }
    public IReadOnlyList<string> TargetProjects { get; }
    public EvaluatedProjectGraph? ProjectGraph { get; }
    public RoslynReferenceSearchResult? References { get; }
    public RoslynImplementationSearchResult? Implementations { get; }
    public RoslynCallerSearchResult? Callers { get; }
    public RoslynCalleeSearchResult? Callees { get; }
    public EvidenceCoverage Coverage { get; }
    public IReadOnlyList<string> PartialReasons { get; }
    public bool TargetResolved => TargetStatus is SemanticTargetResolutionStatus.Resolved;

    internal static RoslynImpactAnalysis Unresolved(SemanticTargetResolution resolution) =>
        new(
            resolution.Target,
            targetId: null,
            resolution.Status,
            resolution.Snapshot,
            resolution.Declarations,
            resolution.Candidates,
            resolution.CandidateTotal,
            resolution.ErrorCode,
            resolution.Correction,
            [],
            projectGraph: null,
            references: null,
            implementations: null,
            callers: null,
            callees: null,
            new EvidenceCoverage(CoverageLevel.NotApplicable),
            resolution.PartialReasons);

    internal static RoslynImpactAnalysis Resolved(
        SemanticTargetResolution resolution,
        EvaluatedProjectGraph graph,
        RoslynReferenceSearchResult references,
        RoslynImplementationSearchResult implementations,
        RoslynCallerSearchResult callers,
        RoslynCalleeSearchResult callees)
    {
        var partialReasons = resolution.PartialReasons
            .Concat(references.PartialReasons)
            .Concat(implementations.PartialReasons)
            .Concat(callers.PartialReasons)
            .Concat(callees.PartialReasons)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var coverage = partialReasons.Length == 0
            && references.Coverage.Level is CoverageLevel.Complete
            && implementations.Coverage.Level is CoverageLevel.Complete
            && callers.Coverage.Level is CoverageLevel.Complete
            && callees.Coverage.Level is CoverageLevel.Complete
                ? new EvidenceCoverage(CoverageLevel.Complete)
                : new EvidenceCoverage(
                    CoverageLevel.Partial,
                    partialReason: partialReasons.FirstOrDefault()
                        ?? "One or more impact relationships were not fully analyzed.");
        return new(
            resolution.Target,
            resolution.CanonicalId,
            resolution.Status,
            ImpactSnapshot(resolution.Snapshot, graph),
            resolution.Declarations,
            [],
            candidateTotal: 0,
            errorCode: null,
            correction: null,
            resolution.Variants
                .Where(static variant => variant.Status is SemanticTargetVariantStatus.Resolved)
                .Select(static variant => variant.ProjectPath)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            graph,
            references,
            implementations,
            callers,
            callees,
            coverage,
            partialReasons);
    }

    private static string? ImpactSnapshot(
        string? semanticSnapshot,
        EvaluatedProjectGraph graph)
    {
        if (semanticSnapshot is null)
        {
            return null;
        }

        return new WorkspaceSnapshotCapturer().Capture(
                new WorkspaceSnapshotCapture(
                    [],
                    [
                        new WorkspaceSnapshotValueInput(
                            WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                            "semantic-target",
                            semanticSnapshot),
                        new WorkspaceSnapshotValueInput(
                            WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                            "evaluated-project-graph",
                            EvaluatedProjectGraphFingerprint.Create(graph),
                            graph.Selection.Path),
                    ],
                    new WorkspaceSnapshotEntryPointInput(graph.Selection)))
            .Identity;
    }
}
