using System.Text;
using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record SymbolShowCommandRequest(
    string Id,
    SymbolWorkspaceScopeRequest Scope,
    int MaxCharacters)
{
    public static SymbolShowCommandRequest Create(
        string id,
        string? solution,
        string? project,
        IReadOnlyList<string> paths,
        bool includeTests,
        bool includeGenerated,
        int maxCharacters)
    {
        if (!SymbolEntityResolver.IsSupportedId(id))
        {
            throw new CommandUsageException(
                "usage.symbol_id",
                "The symbol ID must be a canonical symbol/v2 identity.",
                "Run `dnaxi search symbol <name> --fields 'id,signature' --full` first.");
        }

        if (maxCharacters < 0)
        {
            throw new CommandUsageException(
                "usage.max_chars",
                "The --max-chars value cannot be negative.",
                "Use a non-negative --max-chars value.");
        }

        return new SymbolShowCommandRequest(
            id,
            SymbolWorkspaceScopeRequest.Create(
                solution,
                project,
                paths,
                includeTests,
                includeGenerated,
                "usage.symbol_path"),
            maxCharacters);
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

        var resolved = await SymbolEvidencePipeline.ResolveAsync(
                request.Id,
                request.Scope,
                cancellationToken)
            .ConfigureAwait(false);
        var scope = resolved.Scope;
        var resolution = resolved.Resolution;
        var evidence = resolved.Evidence;

        if (resolution.Stale)
        {
            var query = SearchQuery(resolution.LookupName, scope);
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
            var query = SearchQuery(resolution.LookupName, scope);
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
            + scope.CanonicalArguments()
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
            SymbolEvidencePipeline.Owner(match),
            SymbolEvidencePipeline.Location(match),
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
            candidates.Select(SymbolEvidencePipeline.Candidate),
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

    private static int ScalarCount(string text) =>
        text.EnumerateRunes().Count();

    private static string SearchQuery(
        string name,
        ResolvedSymbolWorkspaceScope scope) =>
        SymbolEvidencePipeline.SearchQuery(name, scope);

    private static string Quote(string value) =>
        SymbolEvidencePipeline.Quote(value);

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

}
