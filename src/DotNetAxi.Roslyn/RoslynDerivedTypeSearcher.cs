using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn;

public enum DerivedTypeSearchScopeMode
{
    Default,
    Complete,
}

public sealed record RoslynDerivedTypeMatch(
    string Id,
    string TargetIdentity,
    string DerivedIdentity,
    IReadOnlyList<string> InheritancePath,
    string? Owner,
    string Project,
    string? Configuration,
    string? Framework,
    SourceLocation Start,
    SourceLocation End);

public sealed class RoslynDerivedTypeSearchResult
{
    internal RoslynDerivedTypeSearchResult(
        RoslynImplementationSearchResult source,
        DerivedTypeSearchScopeMode scopeMode)
    {
        Target = source.Target;
        TargetId = source.TargetId;
        TargetStatus = source.TargetStatus;
        Snapshot = source.Snapshot;
        ScopeMode = scopeMode;
        Matches = Array.AsReadOnly(source.Matches.Select(match =>
            new RoslynDerivedTypeMatch(
                match.Id.Replace("implementation/v1/", "derived/v1/", StringComparison.Ordinal),
                match.TargetIdentity,
                match.ImplementationIdentity,
                match.InheritancePath,
                match.Owner,
                match.Project,
                match.Configuration,
                match.Framework,
                match.Start,
                match.End)).ToArray());
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
    public DerivedTypeSearchScopeMode ScopeMode { get; }
    public IReadOnlyList<RoslynDerivedTypeMatch> Matches { get; }
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

/// <summary>Finds compiler-derived source types using the shared implementation pipeline.</summary>
public sealed class RoslynDerivedTypeSearcher
{
    private readonly RoslynImplementationSearcher _implementations;

    public RoslynDerivedTypeSearcher(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects) =>
        _implementations = new RoslynImplementationSearcher(traverser, ownership, projects);

    public async ValueTask<RoslynDerivedTypeSearchResult> FindAsync(
        string target,
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? declarationScope = null,
        DerivedTypeSearchScopeMode scopeMode = DerivedTypeSearchScopeMode.Default,
        ProjectGraphEvaluationOptions? evaluationOptions = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _implementations.FindAsync(
                target,
                discovery,
                selection,
                traversal,
                declarationScope,
                scopeMode is DerivedTypeSearchScopeMode.Complete
                    ? ImplementationSearchScopeMode.Complete
                    : ImplementationSearchScopeMode.Default,
                evaluationOptions ?? new ProjectGraphEvaluationOptions(),
                cancellationToken)
            .ConfigureAwait(false);
        return new RoslynDerivedTypeSearchResult(result, scopeMode);
    }
}
