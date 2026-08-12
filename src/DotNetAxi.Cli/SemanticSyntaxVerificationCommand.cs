using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal static class SemanticSyntaxVerificationCommand
{
    public static ValueTask<ICommandResult> ExecuteAsync(
        string command,
        WorkspaceDiscoveryResult workspace,
        RoslynSyntaxQueryResult syntax,
        ISemanticallyVerifiableSyntaxQuery query,
        OutputFieldSelection<StructuralCandidate> fields,
        bool full,
        int limit,
        string retrievalCommand,
        IReadOnlyList<string> paths,
        bool includeGenerated,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            command,
            workspace,
            syntax,
            query,
            fields,
            full,
            limit,
            retrievalCommand,
            paths,
            includeGenerated,
            verifier: null,
            cancellationToken);

    internal static ValueTask<ICommandResult> ExecuteAsync(
        string command,
        WorkspaceDiscoveryResult workspace,
        RoslynSyntaxQueryResult syntax,
        ISemanticallyVerifiableSyntaxQuery query,
        OutputFieldSelection<StructuralCandidate> fields,
        bool full,
        int limit,
        string retrievalCommand,
        IReadOnlyList<string> paths,
        bool includeGenerated,
        RoslynSemanticCandidateVerifier verifier,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            command,
            workspace,
            syntax,
            query,
            fields,
            full,
            limit,
            retrievalCommand,
            paths,
            includeGenerated,
            verifier,
            cancellationToken);

    private static async ValueTask<ICommandResult> ExecuteCoreAsync(
        string command,
        WorkspaceDiscoveryResult workspace,
        RoslynSyntaxQueryResult syntax,
        ISemanticallyVerifiableSyntaxQuery query,
        OutputFieldSelection<StructuralCandidate> fields,
        bool full,
        int limit,
        string retrievalCommand,
        IReadOnlyList<string> paths,
        bool includeGenerated,
        RoslynSemanticCandidateVerifier? verifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(paths);

        var projects = workspace.Projects
            .Select(static project => project.Path)
            .ToArray();
        verifier ??= new RoslynSemanticCandidateVerifier(
            new WorkspaceProjectOwnershipResolver(
                workspace.RootPath,
                projects),
            projects);
        var verification = await verifier
            .VerifyAsync(
                workspace.RootPath,
                syntax,
                query,
                cancellationToken)
            .ConfigureAwait(false);
        var included = full
            ? verification.Candidates
            : verification.Candidates.Take(limit);
        var bounded = BoundedCollection<IReadOnlyDictionary<string, object?>>
            .FromObserved(
                ProjectCandidates(included, fields, cancellationToken),
                verification.Candidates.Count,
                totalKnown: true,
                retrievalCommand);
        var payload = new SemanticSyntaxPayload(
            OperationClassification.Executing,
            verification.Discovered,
            verification.Verified,
            verification.Rejected,
            verification.Unresolved,
            bounded.Count,
            bounded.TotalKnown,
            bounded.Total,
            bounded.Omitted,
            bounded.Truncated,
            bounded.RetrievalCommand,
            bounded.Items);
        var variants = verification.Candidates
            .SelectMany(static candidate => candidate.Variants)
            .ToArray();
        var failed = variants.Count(static variant =>
            variant.Status is SemanticCandidateStatus.Unresolved);
        var coverage = verification.HasPartialCoverage
            ? new EvidenceCoverage(
                CoverageLevel.Partial,
                considered: variants.Length,
                analyzed: variants.Length - failed,
                remaining: 0,
                excluded: 0,
                failed,
                partialReason: string.Join(
                    ", ",
                    verification.PartialReasons))
            : new EvidenceCoverage(
                CoverageLevel.Complete,
                considered: variants.Length,
                analyzed: variants.Length,
                remaining: 0,
                excluded: 0,
                failed: 0);
        var evidence = new Evidence(
            verification.Snapshot,
            EvidenceResolution.Semantic,
            coverage,
            EvidenceConfidence.Verified,
            SyntaxCommandScope.Create(
                workspace,
                paths,
                includeGenerated,
                "owning project and framework variants for discovered syntax candidates",
                projects: variants
                    .Select(static variant => variant.Project)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
                frameworks: variants
                    .Select(static variant => variant.Framework)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)));

        return verification.HasPartialCoverage
            ? CommandResult<SemanticSyntaxPayload>.Partial(
                command,
                payload,
                evidence)
            : CommandResult<SemanticSyntaxPayload>.Success(
                command,
                payload,
                evidence);
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>>
        ProjectCandidates(
            IEnumerable<SemanticCandidateVerification> candidates,
            OutputFieldSelection<StructuralCandidate> fields,
            CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projection = new Dictionary<string, object?>(
                StringComparer.Ordinal);
            foreach (var field in fields.Project(candidate.Candidate))
            {
                projection.Add(field.Key, field.Value);
            }

            projection.Add("status", Status(candidate.Status));
            projection.Add(
                "variants",
                candidate.Variants.Select(ProjectVariant).ToArray());
            yield return projection;
        }
    }

    private static IReadOnlyDictionary<string, object?> ProjectVariant(
        SemanticVariantVerification variant) =>
        new Dictionary<string, object?>
        {
            ["project"] = variant.Project,
            ["configuration"] = variant.Configuration,
            ["framework"] = variant.Framework,
            ["status"] = Status(variant.Status),
            ["symbol"] = variant.Symbol,
            ["reason"] = variant.Reason,
        };

    private static string Status(SemanticCandidateStatus status) =>
        status.ToString().ToLowerInvariant();

    private sealed record SemanticSyntaxPayload(
        OperationClassification Classification,
        int Discovered,
        int Verified,
        int Rejected,
        int Unresolved,
        int Count,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Candidates);
}
