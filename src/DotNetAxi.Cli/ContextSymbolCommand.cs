using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;
using DotNetAxi.Search;
using DotNetAxi.Structural;

namespace DotNetAxi.Cli;

internal sealed record ContextSymbolCommandRequest(
    string Id,
    SymbolWorkspaceScopeRequest Scope,
    IReadOnlyList<string> Sections,
    int MaxCharacters,
    bool MaxCharactersSpecified,
    bool Full)
{
    internal static IReadOnlyList<string> AvailableSections { get; } =
        Array.AsReadOnly(["declaration", "owner", "document", "outline"]);

    private static IReadOnlySet<string> RelationshipSections { get; } =
        new HashSet<string>(
            ["references", "callers", "callees", "tests"],
            StringComparer.Ordinal);

    public static ContextSymbolCommandRequest Create(
        string id,
        string? solution,
        string? project,
        IReadOnlyList<string> paths,
        bool includeTests,
        bool includeGenerated,
        IReadOnlyList<string> sections,
        int maxCharacters,
        bool maxCharactersSpecified,
        bool full)
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

        if (full && maxCharactersSpecified)
        {
            throw new CommandUsageException(
                "usage.context_budget",
                "The --full and --max-chars options are mutually exclusive.",
                "Remove --full or --max-chars.");
        }

        var requested = sections
            .SelectMany(static value => value.Split(',', StringSplitOptions.None))
            .Select(static value => value.Trim())
            .ToArray();
        if (requested.Any(static value => value.Length == 0))
        {
            throw new CommandUsageException(
                "usage.context_section",
                "Context section names cannot be blank.",
                "Use --include declaration,owner,document,outline.");
        }

        if (requested.Length == 0)
        {
            requested = AvailableSections.ToArray();
        }

        var unavailable = requested
            .Where(RelationshipSections.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unavailable.Length > 0)
        {
            throw new CommandUsageException(
                "capability.context_section_unavailable",
                "Relationship context sections are not available in this release: "
                    + string.Join(", ", unavailable) + ".",
                "Use --include declaration,owner,document,outline; relationship "
                    + "sections become available with MVP-E05.");
        }

        var unknown = requested
            .Except(AvailableSections, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new CommandUsageException(
                "usage.context_section",
                "Unknown context section: " + string.Join(", ", unknown) + ".",
                "Use --include declaration,owner,document,outline.");
        }

        var selected = AvailableSections
            .Where(name => requested.Contains(name, StringComparer.Ordinal))
            .ToArray();
        return new ContextSymbolCommandRequest(
            id,
            SymbolWorkspaceScopeRequest.Create(
                solution,
                project,
                paths,
                includeTests,
                includeGenerated,
                "usage.context_symbol_path"),
            Array.AsReadOnly(selected),
            maxCharacters,
            maxCharactersSpecified,
            full);
    }
}

internal sealed class ContextSymbolCommandHandler :
    ICommandHandler<ContextSymbolCommandRequest>
{
    private const int DefaultMaximumCharacters = 12000;

    public async ValueTask<ICommandResult> HandleAsync(
        ContextSymbolCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = await SymbolEvidencePipeline.ResolveAsync(
                request.Id,
                request.Scope,
                cancellationToken)
            .ConfigureAwait(false);
        var resolution = resolved.Resolution;
        if (resolution.Stale)
        {
            return Failure(
                resolution.ErrorCode!,
                "The symbol ID no longer identifies a current declaration.",
                resolution.ReplacementCandidates,
                request,
                resolved);
        }

        if (resolution.Ambiguous)
        {
            return Failure(
                "evidence.ambiguous_id",
                "The symbol ID resolves to multiple current declarations.",
                resolution.Matches,
                request,
                resolved);
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
        var outline = new RoslynSourceOutliner()
            .OutlineSymbol(match, maxItems: null, cancellationToken);
        var documentId = FileEntityIdentity.Create(
            match.Range.Start.Path,
            match.Range.Start.IsExternal);
        var sectionValues = CreateSectionValues(
            match,
            detail,
            outline,
            documentId);
        var budget = ContextBudget.Resolve(
            DefaultMaximumCharacters,
            explicitMaximumCharacters: request.MaxCharactersSpecified
                ? request.MaxCharacters
                : null,
            full: request.Full);
        var sectionSet = CreateBudgetedSections(
            request.Sections,
            sectionValues,
            budget);
        var context = ContextBudgeter.Apply(
            sectionSet.Sections,
            budget,
            maximum => RetrievalCommand(request, resolved.Scope, maximum, full: false),
            RetrievalCommand(request, resolved.Scope, maximum: null, full: true));
        var recoveryCommand = context.Truncated
            ? sectionSet.FullTotalCharacters <= int.MaxValue
                ? RetrievalCommand(
                    request,
                    resolved.Scope,
                    checked((int)sectionSet.FullTotalCharacters),
                    full: false)
                : RetrievalCommand(
                    request,
                    resolved.Scope,
                    maximum: null,
                    full: true)
            : null;
        var target = new SymbolContextTargetPayload(
            match.Id,
            documentId,
            SymbolEvidencePipeline.Location(match));
        return CommandResult<SymbolContextCommandPayload>.Success(
            "context symbol",
            SymbolContextCommandPayload.Create(
                target,
                context,
                sectionSet.FullTotalCharacters,
                recoveryCommand),
            resolved.Evidence);
    }

    private static BudgetedContextSections
        CreateBudgetedSections(
            IReadOnlyList<string> names,
            IReadOnlyDictionary<string, SymbolContextSectionPayload> values,
            ContextBudget budget)
    {
        long remaining = budget.MaximumCharacters ?? long.MaxValue;
        var selectedHasPreviousSection = false;
        var sections = new List<ContextSection<SymbolContextSectionPayload>>(
            names.Count);
        long fullTotalCharacters = 0;
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            var fullSection = ToonResultSerializer.CreateContextSectionForBudget(
                name,
                SectionOrder(name),
                values[name],
                hasPreviousSection: index > 0);
            fullTotalCharacters = checked(
                fullTotalCharacters + fullSection.IncludedCharacters);
            var section = selectedHasPreviousSection == (index > 0)
                ? fullSection
                : ToonResultSerializer.CreateContextSectionForBudget(
                    name,
                    SectionOrder(name),
                    values[name],
                    selectedHasPreviousSection);
            sections.Add(section);
            if (budget.Mode is ContextBudgetMode.Full
                || section.IncludedCharacters <= remaining)
            {
                selectedHasPreviousSection = true;
                remaining -= section.IncludedCharacters;
            }
        }

        return new BudgetedContextSections(sections, fullTotalCharacters);
    }

    private sealed record BudgetedContextSections(
        IReadOnlyList<ContextSection<SymbolContextSectionPayload>> Sections,
        long FullTotalCharacters);

    private static IReadOnlyDictionary<string, SymbolContextSectionPayload>
        CreateSectionValues(
            SymbolDeclarationMatch match,
            SymbolDeclarationDetail detail,
            SourceOutline outline,
            string documentId)
    {
        var location = SymbolEvidencePipeline.Location(match);
        var declaration = new SymbolContextDeclarationPayload(
            match.Id,
            match.Kind,
            match.Name,
            match.FullyQualifiedName,
            match.Signature,
            match.Accessibility,
            detail.ContainingType,
            documentId,
            match.Id,
            location,
            detail.Relationships);
        var document = new SymbolContextDocumentPayload(
            documentId,
            match.Range.Start.Path,
            match.Range.Start.IsExternal,
            match.IsGenerated,
            "utf-8",
            detail.SourceByteCount,
            [match.Id],
            detail.SourceText);
        var outlineItems = outline.Items
            .Skip(1)
            .ToArray();
        return new Dictionary<string, SymbolContextSectionPayload>(
            StringComparer.Ordinal)
        {
            ["declaration"] = new(
                "symbol-entity-resolution",
                EvidenceResolution.Syntax,
                EvidenceConfidence.Candidate,
                declaration),
            ["owner"] = new(
                "workspace-project-ownership",
                EvidenceResolution.Syntax,
                EvidenceConfidence.Candidate,
                SymbolEvidencePipeline.Owner(match)),
            ["document"] = new(
                "resolved-declaration-source",
                EvidenceResolution.Text,
                EvidenceConfidence.Verified,
                document),
            ["outline"] = new(
                "roslyn-syntax-outline",
                EvidenceResolution.Syntax,
                EvidenceConfidence.Candidate,
                new SymbolContextOutlinePayload(
                    documentId,
                    match.Id,
                    outline.DiagnosticCount,
                    outline.TotalCount,
                    outlineItems.Length,
                    outlineItems)),
        };
    }

    private static CommandResult<ContextSymbolResolutionPayload> Failure(
        string code,
        string message,
        IReadOnlyList<SymbolDeclarationMatch> candidates,
        ContextSymbolCommandRequest request,
        ResolvedSymbolEvidence resolved)
    {
        var query = SymbolEvidencePipeline.SearchQuery(
            resolved.Resolution.LookupName,
            resolved.Scope);
        var bounded = BoundedCollection<ContextSymbolCandidatePayload>.Create(
            candidates.Select(candidate =>
            {
                var shaped = SymbolEvidencePipeline.Candidate(candidate);
                return new ContextSymbolCandidatePayload(
                    shaped.Id,
                    shaped.Kind,
                    shaped.Name,
                    shaped.Signature,
                    shaped.File,
                    shaped.Line,
                    ContinuationCommand(request, resolved.Scope, shaped.Id));
            }),
            limit: 10,
            knownTotal: candidates.Count,
            retrievalCommand: CanonicalInvocation.OneShot(query));
        return CommandResult<ContextSymbolResolutionPayload>.Failed(
            "context symbol",
            [new ResultError(code, message, query)],
            new ContextSymbolResolutionPayload(
                query,
                bounded.Count,
                bounded.TotalKnown,
                bounded.Total,
                bounded.Omitted,
                bounded.Truncated,
                bounded.RetrievalCommand,
                bounded.Items),
            resolved.Evidence);
    }

    private static int SectionOrder(string name) => name switch
    {
        "declaration" => 0,
        "owner" => 1,
        "document" => 2,
        "outline" => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static string RetrievalCommand(
        ContextSymbolCommandRequest request,
        ResolvedSymbolWorkspaceScope scope,
        int? maximum,
        bool full) =>
        CanonicalInvocation.OneShot(
            "dnaxi context symbol "
            + SymbolEvidencePipeline.Quote(request.Id)
            + scope.CanonicalArguments()
            + " --include "
            + SymbolEvidencePipeline.Quote(string.Join(',', request.Sections))
            + (full
                ? " --full"
                : " --max-chars "
                    + maximum!.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)));

    private static string ContinuationCommand(
        ContextSymbolCommandRequest request,
        ResolvedSymbolWorkspaceScope scope,
        string id) =>
        CanonicalInvocation.OneShot(
            "dnaxi context symbol "
            + SymbolEvidencePipeline.Quote(id)
            + scope.CanonicalArguments()
            + " --include "
            + SymbolEvidencePipeline.Quote(string.Join(',', request.Sections))
            + (request.Full
                ? " --full"
                : request.MaxCharactersSpecified
                    ? " --max-chars "
                        + request.MaxCharacters.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty));
}

internal sealed record SymbolContextSectionPayload(
    string Provenance,
    EvidenceResolution Resolution,
    EvidenceConfidence Confidence,
    object Data);

internal sealed record SymbolContextTargetPayload(
    string Id,
    string DocumentRef,
    SymbolLocationPayload Location);

internal sealed record SymbolContextCommandPayload(
    SymbolContextTargetPayload Target,
    ContextBudgetMode BudgetMode,
    int? MaximumCharacters,
    IReadOnlyList<ContextSection<SymbolContextSectionPayload>> Sections,
    long IncludedCharacters,
    bool TotalKnown,
    long? TotalCharacters,
    long? OmittedCharacters,
    IReadOnlyList<string> OmittedSections,
    ApproximateTokenRange ApproximateTokens,
    bool Truncated,
    string? RetrievalCommand)
{
    public static SymbolContextCommandPayload Create(
        SymbolContextTargetPayload target,
        BoundedContext<SymbolContextSectionPayload> context,
        long fullTotalCharacters,
        string? recoveryCommand) =>
        new(
            target,
            context.BudgetMode,
            context.MaximumCharacters,
            context.Sections,
            context.IncludedCharacters,
            TotalKnown: true,
            fullTotalCharacters,
            fullTotalCharacters - context.IncludedCharacters,
            context.OmittedSections,
            context.ApproximateTokens,
            context.Truncated,
            recoveryCommand);
}

internal sealed record SymbolContextDeclarationPayload(
    string Id,
    string Kind,
    string Name,
    string FullyQualifiedName,
    string Signature,
    string Accessibility,
    string? ContainingType,
    string DocumentRef,
    string SourceSpanRef,
    SymbolLocationPayload Location,
    SymbolRelationshipSummary Relationships);

internal sealed record SymbolContextDocumentPayload(
    string Id,
    string Path,
    bool External,
    bool Generated,
    string Encoding,
    int ByteCount,
    IReadOnlyList<string> DeclarationSpanIds,
    string Text);

internal sealed record SymbolContextOutlinePayload(
    string DocumentRef,
    string RootDeclarationRef,
    int DiagnosticCount,
    int TotalCount,
    int IncludedChildCount,
    IReadOnlyList<SourceOutlineItem> Items);

internal sealed record ContextSymbolResolutionPayload(
    string Query,
    int CandidateCount,
    bool TotalKnown,
    int? Total,
    int? Omitted,
    bool Truncated,
    string? RetrievalCommand,
    IReadOnlyList<ContextSymbolCandidatePayload> Candidates);

internal sealed record ContextSymbolCandidatePayload(
    string Id,
    string Kind,
    string Name,
    string Signature,
    string File,
    int Line,
    string ContextCommand);
