using System.Text;
using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record SymbolShowCommandRequest(
    string Id,
    IReadOnlyList<string> Paths,
    int MaxCharacters)
{
    public static SymbolShowCommandRequest Create(
        string id,
        IReadOnlyList<string> paths,
        int maxCharacters)
    {
        if (!SymbolEntityResolver.IsSupportedId(id))
        {
            throw new CommandUsageException(
                "usage.symbol_id",
                "The symbol ID must be a canonical symbol/v2 identity.",
                "Run `dnaxi search symbol <name> --fields id signature --full` first.");
        }

        if (paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new CommandUsageException(
                "usage.symbol_path",
                "A --path value cannot be blank.",
                "Provide one or more non-blank paths.");
        }

        if (maxCharacters < 0)
        {
            throw new CommandUsageException(
                "usage.max_chars",
                "The --max-chars value cannot be negative.",
                "Use a non-negative --max-chars value.");
        }

        return new SymbolShowCommandRequest(id, paths, maxCharacters);
    }
}

internal sealed class SymbolShowCommandHandler :
    ICommandHandler<SymbolShowCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        SymbolShowCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = new WorkspaceDiscoverer().Discover(Directory.GetCurrentDirectory());
        var traversal = new WorkspaceTraversalRequest(
            workspace.RootPath,
            explicitPaths: request.Paths,
            currentDirectory: workspace.CurrentDirectory);
        var resolver = new SymbolEntityResolver(
            new WorkspacePathTraverser(),
            new WorkspaceProjectOwnershipResolver(
                workspace.RootPath,
                workspace.Projects.Select(static project => project.Path)));
        var resolution = await resolver
            .ResolveAsync(request.Id, traversal, cancellationToken)
            .ConfigureAwait(false);
        var evidence = EvidenceFor(workspace.RootPath, request.Paths, resolution);

        if (resolution.Stale)
        {
            var query = SearchQuery(resolution.LookupName, request.Paths);
            return Failure(
                resolution.ErrorCode!,
                "The symbol ID no longer identifies a current declaration.",
                query,
                query,
                resolution.ReplacementCandidates,
                evidence);
        }

        if (resolution.Ambiguous)
        {
            var query = SearchQuery(resolution.LookupName, request.Paths);
            return Failure(
                "evidence.ambiguous_id",
                "The symbol ID resolves to multiple current declarations.",
                query,
                query,
                resolution.Matches,
                evidence);
        }

        if (!resolution.Resolved)
        {
            throw new InvalidOperationException(
                "Symbol resolution produced neither a result nor a structured correction.");
        }

        var match = resolution.Matches[0];
        var detail = await new SymbolDeclarationDetailReader()
            .ReadAsync(match, cancellationToken)
            .ConfigureAwait(false);
        var completeBudget = Math.Max(
            ScalarCount(detail.Documentation),
            ScalarCount(detail.Body));
        var retrievalCommand = CanonicalInvocation.OneShot(
            "dnaxi show symbol "
            + Quote(request.Id)
            + PathArguments(request.Paths)
            + " --max-chars "
            + completeBudget.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        var payload = new SymbolShowPayload(
            match.Id,
            match.Kind,
            match.Name,
            match.FullyQualifiedName,
            match.Signature,
            match.Accessibility,
            detail.ContainingType,
            new SymbolOwnerPayload(
                match.OwningProjectCount,
                match.OwningProjects,
                match.VariantCount,
                match.Variants.Select(Variant).ToArray()),
            new SymbolLocationPayload(
                match.Range.Start.Path,
                match.Range.Start.Line,
                match.Range.Start.Column,
                match.Range.End.Line,
                match.Range.End.Column,
                match.Range.Start.IsExternal),
            BoundedText.Create(
                detail.Documentation,
                request.MaxCharacters,
                retrievalCommand),
            BoundedText.Create(
                detail.Body,
                request.MaxCharacters,
                retrievalCommand),
            detail.Relationships);
        return CommandResult<SymbolShowPayload>.Success(
            "show symbol",
            payload,
            evidence);
    }

    private static CommandResult<SymbolResolutionPayload> Failure(
        string code,
        string message,
        string correction,
        string query,
        IReadOnlyList<SymbolDeclarationMatch> candidates,
        Evidence evidence)
    {
        var bounded = BoundedCollection<SymbolCandidatePayload>.Create(
            candidates.Select(Candidate),
            limit: 10,
            knownTotal: candidates.Count,
            retrievalCommand: CanonicalInvocation.OneShot(query));
        return CommandResult<SymbolResolutionPayload>.Failed(
            "show symbol",
            [new ResultError(code, message, correction)],
            new SymbolResolutionPayload(
                query,
                bounded.Count,
                bounded.TotalKnown,
                bounded.Total,
                bounded.Omitted,
                bounded.Truncated,
                bounded.RetrievalCommand,
                bounded.Items),
            evidence);
    }

    private static Evidence EvidenceFor(
        string workspaceRoot,
        IReadOnlyList<string> paths,
        SymbolEntityResolution resolution) =>
        new(
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
            new EvidenceScope(
                workspaceRoot,
                paths.Count == 0
                    ? "eligible C# declaration paths"
                    : "eligible explicitly selected C# declaration paths"));

    private static SymbolCandidatePayload Candidate(SymbolDeclarationMatch match) =>
        new(
            match.Id,
            match.Kind,
            match.Name,
            match.Signature,
            match.Range.Start.Path,
            match.Range.Start.Line);

    private static SymbolVariantPayload Variant(SymbolDeclarationVariant variant) =>
        new(
            variant.Project,
            variant.Configuration,
            variant.Framework,
            variant.Meaning);

    private static int ScalarCount(string text) =>
        text.EnumerateRunes().Count();

    private static string SearchQuery(
        string name,
        IReadOnlyList<string> paths) =>
        "dnaxi search symbol "
        + Quote(name)
        + PathArguments(paths)
        + " --fields id signature owning_projects variant_count variants --full";

    private static string PathArguments(IReadOnlyList<string> paths) =>
        string.Concat(paths.Select(path => " --path " + Quote(path)));

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record SymbolShowPayload(
        string Id,
        string Kind,
        string Name,
        string FullyQualifiedName,
        string Signature,
        string Accessibility,
        string? ContainingType,
        SymbolOwnerPayload Owner,
        SymbolLocationPayload Location,
        BoundedText Documentation,
        BoundedText Body,
        SymbolRelationshipSummary Relationships);

    private sealed record SymbolOwnerPayload(
        int ProjectCount,
        IReadOnlyList<string> Projects,
        int VariantCount,
        IReadOnlyList<SymbolVariantPayload> Variants);

    private sealed record SymbolVariantPayload(
        string Project,
        string? Configuration,
        string? Framework,
        string Meaning);

    private sealed record SymbolLocationPayload(
        string File,
        int Line,
        int Column,
        int EndLine,
        int EndColumn,
        bool External);

    private sealed record SymbolResolutionPayload(
        string Query,
        int CandidateCount,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<SymbolCandidatePayload> Candidates);

    private sealed record SymbolCandidatePayload(
        string Id,
        string Kind,
        string Name,
        string Signature,
        string File,
        int Line);
}
