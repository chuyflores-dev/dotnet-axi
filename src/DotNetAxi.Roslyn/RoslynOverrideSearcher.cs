using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn;

public enum OverrideSearchScopeMode { Default, Complete }

public sealed record RoslynOverrideMatch(
    string Id,
    string TargetIdentity,
    string OverrideIdentity,
    IReadOnlyList<string> OverridePath,
    string? Owner,
    string Project,
    string? Configuration,
    string? Framework,
    SourceLocation Start,
    SourceLocation End);

public sealed class RoslynOverrideSearchResult
{
    internal RoslynOverrideSearchResult(RoslynImplementationSearchResult source, OverrideSearchScopeMode scopeMode)
    {
        Target = source.Target;
        TargetId = source.TargetId;
        TargetStatus = source.TargetStatus;
        Snapshot = source.Snapshot;
        ScopeMode = scopeMode;
        Matches = Array.AsReadOnly(source.Matches
            .Where(static match => match.OverridePath.Count > 1)
            .Select(match => new RoslynOverrideMatch(
                match.Id.Replace("implementation/v1/", "override/v1/", StringComparison.Ordinal),
                match.TargetIdentity, match.ImplementationIdentity, match.OverridePath,
                match.Owner, match.Project, match.Configuration, match.Framework, match.Start, match.End))
            .ToArray());
        Coverage = source.Coverage;
        Variants = source.Variants;
        Candidates = source.Candidates;
        CandidateTotal = source.CandidateTotal;
        ErrorCode = source.ErrorCode;
        Correction = source.Correction;
        PartialReasons = source.PartialReasons;
    }

    public string Target { get; }
    public string? TargetId { get; }
    public SemanticTargetResolutionStatus TargetStatus { get; }
    public string? Snapshot { get; }
    public OverrideSearchScopeMode ScopeMode { get; }
    public IReadOnlyList<RoslynOverrideMatch> Matches { get; }
    public EvidenceCoverage Coverage { get; }
    public IReadOnlyList<ImplementationSearchVariant> Variants { get; }
    public IReadOnlyList<SymbolDeclarationMatch> Candidates { get; }
    public int CandidateTotal { get; }
    public int CandidateOmitted => CandidateTotal - Candidates.Count;
    public bool CandidateTruncated => CandidateOmitted > 0;
    public string? ErrorCode { get; }
    public string? Correction { get; }
    public IReadOnlyList<string> PartialReasons { get; }
    public bool TargetResolved => TargetStatus is SemanticTargetResolutionStatus.Resolved;
}

public sealed class RoslynOverrideSearcher
{
    private readonly RoslynImplementationSearcher _implementations;

    public RoslynOverrideSearcher(IWorkspacePathTraverser traverser, IFileOwnershipResolver ownership, IEnumerable<string> projects) =>
        _implementations = new RoslynImplementationSearcher(traverser, ownership, projects);

    public async ValueTask<RoslynOverrideSearchResult> FindAsync(
        string target, WorkspaceDiscoveryResult discovery, WorkspaceSelection selection,
        WorkspaceTraversalRequest traversal, SymbolDeclarationScope? declarationScope = null,
        OverrideSearchScopeMode scopeMode = OverrideSearchScopeMode.Default,
        ProjectGraphEvaluationOptions? evaluationOptions = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _implementations.FindAsync(target, discovery, selection, traversal,
            declarationScope, scopeMode is OverrideSearchScopeMode.Complete
                ? ImplementationSearchScopeMode.Complete : ImplementationSearchScopeMode.Default,
            evaluationOptions ?? new ProjectGraphEvaluationOptions(), cancellationToken).ConfigureAwait(false);
        return new RoslynOverrideSearchResult(result, scopeMode);
    }
}
