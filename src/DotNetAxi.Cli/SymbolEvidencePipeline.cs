using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record ResolvedSymbolEvidence(
    ResolvedSymbolWorkspaceScope Scope,
    SymbolEntityResolution Resolution,
    Evidence Evidence);

internal static class SymbolEvidencePipeline
{
    public static async ValueTask<ResolvedSymbolEvidence> ResolveAsync(
        string id,
        SymbolWorkspaceScopeRequest request,
        CancellationToken cancellationToken)
    {
        var workspace = new WorkspaceDiscoverer()
            .Discover(Directory.GetCurrentDirectory());
        var scope = SymbolWorkspaceScopeResolver.Resolve(workspace, request);
        var resolution = await new SymbolEntityResolver(
                scope.Traverser,
                scope.Ownership)
            .ResolveAsync(
                id,
                scope.Traversal,
                scope.DeclarationScope,
                cancellationToken)
            .ConfigureAwait(false);
        return new ResolvedSymbolEvidence(
            scope,
            resolution,
            new Evidence(
                resolution.Snapshot,
                EvidenceResolution.Syntax,
                new EvidenceCoverage(
                    CoverageLevel.Complete,
                    considered: resolution.ObservedFileCount,
                    analyzed: resolution.ObservedFileCount,
                    remaining: 0,
                    excluded: 0,
                    failed: 0),
                EvidenceConfidence.Candidate,
                scope.EvidenceScope));
    }

    public static string SearchQuery(
        string name,
        ResolvedSymbolWorkspaceScope scope) =>
        "dnaxi search symbol "
        + Quote(name)
        + scope.CanonicalArguments()
        + " --fields 'id,signature,owning_projects,variant_count,variants' --full";

    public static SymbolCandidatePayload Candidate(
        SymbolDeclarationMatch match) =>
        new(
            match.Id,
            match.Kind,
            match.Name,
            match.Signature,
            match.Range.Start.Path,
            match.Range.Start.Line);

    public static SymbolOwnerPayload Owner(SymbolDeclarationMatch match) =>
        new(
            match.OwningProjectCount,
            match.OwningProjects,
            match.VariantCount,
            match.Variants.Select(Variant).ToArray());

    public static SymbolLocationPayload Location(
        SymbolDeclarationMatch match) =>
        new(
            match.Range.Start.Path,
            match.Range.Start.Line,
            match.Range.Start.Column,
            match.Range.End.Line,
            match.Range.End.Column,
            match.Range.Start.IsExternal);

    public static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static SymbolVariantPayload Variant(
        SymbolDeclarationVariant variant) =>
        new(
            variant.Project,
            variant.Configuration,
            variant.Framework,
            variant.Meaning);
}

internal sealed record SymbolOwnerPayload(
    int ProjectCount,
    IReadOnlyList<string> Projects,
    int VariantCount,
    IReadOnlyList<SymbolVariantPayload> Variants);

internal sealed record SymbolVariantPayload(
    string Project,
    string? Configuration,
    string? Framework,
    string Meaning);

internal sealed record SymbolLocationPayload(
    string File,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    bool External);

internal sealed record SymbolResolutionPayload(
    string Query,
    int CandidateCount,
    bool TotalKnown,
    int? Total,
    int? Omitted,
    bool Truncated,
    string? RetrievalCommand,
    IReadOnlyList<SymbolCandidatePayload> Candidates);

internal sealed record SymbolCandidatePayload(
    string Id,
    string Kind,
    string Name,
    string Signature,
    string File,
    int Line);
