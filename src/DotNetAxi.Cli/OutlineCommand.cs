using System.Security.Cryptography;
using DotNetAxi.Contracts;
using DotNetAxi.Search;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record OutlineCommandRequest(
    string Target,
    bool SymbolTarget,
    SymbolWorkspaceScopeRequest Scope,
    int Limit,
    bool LimitSpecified,
    bool Full)
{
    public static OutlineCommandRequest Create(
        string target,
        string? solution,
        string? project,
        IReadOnlyList<string> paths,
        bool includeTests,
        bool includeGenerated,
        int limit,
        bool limitSpecified,
        bool full)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new CommandUsageException(
                "usage.outline_target",
                "The outline target cannot be blank.",
                "Provide one explicit C# document path or canonical symbol/v2 identity.");
        }

        var symbolTarget = SymbolEntityResolver.IsSupportedId(target);
        if (!symbolTarget && target.StartsWith("symbol/", StringComparison.Ordinal))
        {
            throw new CommandUsageException(
                "usage.symbol_id",
                "The symbol ID must be a canonical symbol/v2 identity.",
                "Run `dnaxi search symbol <name> --fields 'id,signature' --full` first.");
        }

        if (!symbolTarget
            && (paths.Count > 0
                || solution is not null
                || project is not null
                || includeTests))
        {
            throw new CommandUsageException(
                "usage.outline_scope",
                "Workspace selectors and test eligibility apply only to a symbol identity target.",
                "Remove --solution, --project, --path, and --include-tests when outlining one explicit document.");
        }

        if (limit < 0)
        {
            throw new CommandUsageException(
                "usage.limit",
                "The --limit value cannot be negative.",
                "Use a non-negative --limit value.");
        }

        if (full && limitSpecified)
        {
            throw new CommandUsageException(
                "usage.outline_limit",
                "The --full and --limit options cannot be combined.",
                "Use either --full or one explicit --limit value.");
        }

        return new OutlineCommandRequest(
            target,
            symbolTarget,
            SymbolWorkspaceScopeRequest.Create(
                solution,
                project,
                paths,
                includeTests,
                includeGenerated,
                "usage.outline_path"),
            limit,
            limitSpecified,
            full);
    }
}

internal sealed class OutlineCommandHandler :
    ICommandHandler<OutlineCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        OutlineCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = new WorkspaceDiscoverer()
            .Discover(Directory.GetCurrentDirectory());
        return request.SymbolTarget
            ? await OutlineSymbolAsync(workspace, request, cancellationToken)
                .ConfigureAwait(false)
            : await OutlineDocumentAsync(workspace, request, cancellationToken)
                .ConfigureAwait(false);
    }

    private static async ValueTask<ICommandResult> OutlineSymbolAsync(
        WorkspaceDiscoveryResult workspace,
        OutlineCommandRequest request,
        CancellationToken cancellationToken)
    {
        var scope = SymbolWorkspaceScopeResolver.Resolve(workspace, request.Scope);
        var resolver = new SymbolEntityResolver(
            scope.Traverser,
            scope.Ownership);
        var resolution = await resolver
            .ResolveAsync(
                request.Target,
                scope.Traversal,
                scope.DeclarationScope,
                cancellationToken)
            .ConfigureAwait(false);
        var evidence = SymbolEvidence(scope, resolution);

        if (resolution.Stale)
        {
            var query = SearchQuery(resolution.LookupName, scope);
            return SymbolFailure(
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
            return SymbolFailure(
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
        var outline = new RoslynSourceOutliner()
            .OutlineSymbol(
                match,
                request.Full ? null : request.Limit,
                cancellationToken);
        return Success(
            request,
            outline,
            targetKind: "symbol",
            match.Id,
            match.IsGenerated,
            match.OwningProjects,
            evidence,
            scope.CanonicalArguments());
    }

    private static async ValueTask<ICommandResult> OutlineDocumentAsync(
        WorkspaceDiscoveryResult workspace,
        OutlineCommandRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = ResolvePath(workspace, request.Target);
        if (Directory.Exists(resolution.FullPath))
        {
            throw new CommandUsageException(
                "usage.outline_target",
                $"Path `{resolution.Path}` identifies a directory, not a document.",
                "Provide one explicit C# document path or canonical symbol/v2 identity.");
        }

        if (!File.Exists(resolution.FullPath))
        {
            return DocumentFailure(
                "document.not_found",
                $"Document `{resolution.Path}` does not exist.",
                "Correct the path and run the command again.");
        }

        if (!Path.GetExtension(resolution.Path).Equals(
                ".cs",
                StringComparison.OrdinalIgnoreCase))
        {
            return DocumentFailure(
                "outline.language_unsupported",
                $"Document `{resolution.Path}` is not a C# source file.",
                "Select one .cs document or a canonical C# symbol identity.");
        }

        var traversal = new WorkspaceTraversalRequest(
            workspace.RootPath,
            explicitPaths: [request.Target],
            includeGenerated: true,
            currentDirectory: workspace.CurrentDirectory);
        var paths = new WorkspacePathTraverser()
            .Traverse(traversal, cancellationToken);
        var document = paths.Count == 1 ? paths[0] : null;
        if (document is null)
        {
            return DocumentFailure(
                "document.excluded",
                $"Document `{resolution.Path}` is excluded by workspace traversal policy.",
                "Remove the applicable exclusion or select an eligible document.");
        }

        var read = await new TextDocumentReader()
            .ReadAsync(document.FullPath, cancellationToken)
            .ConfigureAwait(false);
        if (read.Status is not TextDocumentReadStatus.Success)
        {
            return ReadFailure(document.RelativePath, read.Status);
        }

        var isGenerated = document.IsGenerated
            || WorkspaceGeneratedCodeClassifier.HasGeneratedHeader(read.Text!);
        if (isGenerated && !request.Scope.IncludeGenerated)
        {
            return DocumentFailure(
                "document.generated_excluded",
                $"Document `{document.RelativePath}` is generated and excluded by default.",
                GeneratedCorrection(request));
        }

        var currentPaths = new WorkspacePathTraverser()
            .Traverse(traversal, cancellationToken);
        var currentDocument = currentPaths.Count == 1 ? currentPaths[0] : null;
        if (currentDocument is null || !SameDocument(document, currentDocument))
        {
            return ChangedDuringRead(document.RelativePath);
        }

        document = currentDocument;
        var ownership = new WorkspaceProjectOwnershipResolver(
            workspace.RootPath,
            workspace.Projects.Select(static project => project.Path));
        var owners = ownership
            .GetOwningProjects(document)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var contentHash = Convert.ToHexStringLower(
            SHA256.HashData(read.Content.Span));
        var snapshot = await DocumentShowCommandHandler.CaptureSnapshotAsync(
                document,
                isGenerated,
                owners,
                contentHash,
                read.Content.Length,
                cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return ChangedDuringRead(document.RelativePath);
        }

        var finalPaths = new WorkspacePathTraverser()
            .Traverse(traversal, cancellationToken);
        var finalDocument = finalPaths.Count == 1 ? finalPaths[0] : null;
        if (finalDocument is null || !SameDocument(document, finalDocument))
        {
            return ChangedDuringRead(document.RelativePath);
        }

        document = finalDocument;
        var outline = new RoslynSourceOutliner().OutlineDocument(
            document.RelativePath,
            document.IsExternal,
            read.Text!,
            contentHash,
            request.Full ? null : request.Limit,
            cancellationToken);
        var evidence = new Evidence(
            snapshot,
            EvidenceResolution.Syntax,
            new EvidenceCoverage(
                CoverageLevel.Complete,
                considered: 1,
                analyzed: 1,
                remaining: 0,
                excluded: 0,
                failed: 0),
            EvidenceConfidence.Candidate,
            new EvidenceScope(
                workspace.RootPath,
                document.IsExternal
                    ? "one explicitly selected external C# document"
                    : "one explicitly selected workspace C# document",
                projects: owners));
        return Success(
            request,
            outline,
            targetKind: "document",
            FileEntityIdentity.Create(document),
            isGenerated,
            owners,
            evidence,
            scopeArguments: string.Empty);
    }

    private static CommandResult<OutlinePayload> Success(
        OutlineCommandRequest request,
        SourceOutline outline,
        string targetKind,
        string id,
        bool generated,
        IReadOnlyList<string> owners,
        Evidence evidence,
        string scopeArguments)
    {
        var bounded = BoundedCollection<SourceOutlineItem>.Create(
            outline.Items,
            request.Full ? outline.Items.Count : request.Limit,
            knownTotal: outline.TotalCount,
            retrievalCommand: RetrievalCommand(request, scopeArguments));
        return CommandResult<OutlinePayload>.Success(
            "outline",
            new OutlinePayload(
                targetKind,
                id,
                outline.Path,
                outline.External,
                generated,
                owners.Count,
                owners,
                outline.DiagnosticCount,
                bounded.Count,
                bounded.TotalKnown,
                bounded.Total,
                bounded.Omitted,
                bounded.Truncated,
                bounded.RetrievalCommand,
                bounded.Items),
            evidence);
    }

    private static WorkspacePathResolution ResolvePath(
        WorkspaceDiscoveryResult workspace,
        string path)
    {
        try
        {
            return new WorkspacePathResolver(
                workspace.RootPath,
                workspace.CurrentDirectory)
                .ResolveInput(path, WorkspacePathScope.Explicit);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException)
        {
            throw new CommandUsageException(
                "usage.outline_target",
                $"Outline target `{path}` is invalid.",
                "Provide one valid C# document path or canonical symbol/v2 identity.");
        }
    }

    private static bool SameDocument(
        WorkspaceTraversalPath before,
        WorkspaceTraversalPath after) =>
        string.Equals(
            Path.GetFullPath(before.FullPath),
            Path.GetFullPath(after.FullPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)
        && string.Equals(
            before.RelativePath,
            after.RelativePath,
            StringComparison.Ordinal)
        && before.IsExternal == after.IsExternal
        && before.IsGenerated == after.IsGenerated;

    private static Evidence SymbolEvidence(
        ResolvedSymbolWorkspaceScope scope,
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
            scope.EvidenceScope);

    private static CommandResult<OutlineResolutionPayload> SymbolFailure(
        string code,
        string message,
        string correction,
        string query,
        IReadOnlyList<SymbolDeclarationMatch> candidates,
        Evidence evidence)
    {
        var bounded = BoundedCollection<OutlineCandidatePayload>.Create(
            candidates.Select(Candidate),
            limit: 10,
            knownTotal: candidates.Count,
            retrievalCommand: CanonicalInvocation.OneShot(query));
        return CommandResult<OutlineResolutionPayload>.Failed(
            "outline",
            [new ResultError(code, message, correction)],
            new OutlineResolutionPayload(
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

    private static OutlineCandidatePayload Candidate(
        SymbolDeclarationMatch match) =>
        new(
            match.Id,
            match.Kind,
            match.Name,
            match.Signature,
            match.Range.Start.Path,
            match.Range.Start.Line);

    private static CommandResult<OutlineFailurePayload> ReadFailure(
        string path,
        TextDocumentReadStatus status) =>
        status switch
        {
            TextDocumentReadStatus.Binary => DocumentFailure(
                "document.binary",
                $"Document `{path}` contains binary data.",
                "Select a supported UTF-8 or UTF-16 C# document."),
            TextDocumentReadStatus.Undecodable => DocumentFailure(
                "document.undecodable",
                $"Document `{path}` cannot be decoded without data loss.",
                "Correct the document encoding or convert it to valid UTF-8 or UTF-16."),
            TextDocumentReadStatus.UnsupportedEncoding => DocumentFailure(
                "document.encoding_unsupported",
                $"Document `{path}` uses an unsupported encoding.",
                "Convert the document to UTF-8 or UTF-16 and run the command again."),
            TextDocumentReadStatus.Unreadable => DocumentFailure(
                "document.unreadable",
                $"Document `{path}` could not be read.",
                "Check file permissions and availability, then run the command again."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The document read status does not describe a failure."),
        };

    private static CommandResult<OutlineFailurePayload> ChangedDuringRead(
        string path) =>
        DocumentFailure(
            "document.changed_during_read",
            $"Document `{path}` changed while it was being read.",
            "Run the command again against a stable document path.");

    private static CommandResult<OutlineFailurePayload> DocumentFailure(
        string code,
        string message,
        string correction) =>
        CommandResult<OutlineFailurePayload>.Failed(
            "outline",
            [new ResultError(code, message, correction)],
            new OutlineFailurePayload(TargetKind: "document"));

    private static string SearchQuery(
        string name,
        ResolvedSymbolWorkspaceScope scope) =>
        "dnaxi search symbol "
        + Quote(name)
        + scope.CanonicalArguments()
        + " --fields 'id,signature,owning_projects,variant_count,variants' --full";

    private static string RetrievalCommand(
        OutlineCommandRequest request,
        string scopeArguments) =>
        CanonicalInvocation.OneShot(
            "dnaxi outline "
            + Quote(request.Target)
            + scopeArguments
            + " --full");

    private static string GeneratedCorrection(OutlineCommandRequest request) =>
        "Run `"
        + CanonicalInvocation.OneShot(
            "dnaxi outline "
            + Quote(request.Target)
            + " --include-generated"
            + (request.Full
                ? " --full"
                : request.LimitSpecified
                    ? " --limit "
                        + request.Limit.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty))
        + "` to include it explicitly.";

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record OutlinePayload(
        string TargetKind,
        string Id,
        string Path,
        bool External,
        bool Generated,
        int OwningProjectCount,
        IReadOnlyList<string> OwningProjects,
        int DiagnosticCount,
        int Count,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<SourceOutlineItem> Items);

    private sealed record OutlineResolutionPayload(
        string Query,
        int CandidateCount,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<OutlineCandidatePayload> Candidates);

    private sealed record OutlineCandidatePayload(
        string Id,
        string Kind,
        string Name,
        string Signature,
        string File,
        int Line);

    private sealed record OutlineFailurePayload(string TargetKind);
}
