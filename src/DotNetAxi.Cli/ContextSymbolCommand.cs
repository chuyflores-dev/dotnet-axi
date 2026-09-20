using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
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
        Array.AsReadOnly([
            "declaration",
            "owner",
            "document",
            "outline",
            "references",
            "implementations",
            "overrides",
            "derived",
            "callers",
            "callees",
        ]);

    private static IReadOnlyList<string> DefaultSections { get; } =
        Array.AsReadOnly(["declaration", "owner", "document", "outline"]);

    private static IReadOnlySet<string> RelationshipSections { get; } =
        new HashSet<string>(
            ["tests"],
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
                "Use --include declaration,owner,document,outline,references,implementations,overrides,derived,callers,callees.");
        }

        if (requested.Length == 0)
        {
            requested = DefaultSections.ToArray();
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
                "Affected-test context is unavailable until an affected-test capability ships.");
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
                "Use --include declaration,owner,document,outline,references,implementations,overrides,derived,callers,callees.");
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

internal readonly record struct ContextSymbolSectionRequirements(
    bool RequiresDetail,
    bool RequiresOutline,
    IReadOnlyList<SemanticRelationshipKind> Relationships)
{
    internal static bool IsRelationship(string name) => name is
        "references" or "implementations" or "overrides" or "derived" or "callers" or "callees";

    internal static ContextSymbolSectionRequirements From(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var relationships = names
            .Select(static name => name switch
            {
                "references" => SemanticRelationshipKind.References,
                "implementations" => SemanticRelationshipKind.Implementations,
                "overrides" => SemanticRelationshipKind.Overrides,
                "derived" => SemanticRelationshipKind.Derived,
                "callers" => SemanticRelationshipKind.Callers,
                "callees" => SemanticRelationshipKind.Callees,
                _ => (SemanticRelationshipKind?)null,
            })
            .OfType<SemanticRelationshipKind>()
            .ToArray();
        return new(
            names.Contains("declaration", StringComparer.Ordinal)
                || names.Contains("document", StringComparer.Ordinal),
            names.Contains("outline", StringComparer.Ordinal),
            Array.AsReadOnly(relationships));
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
        var documentId = FileEntityIdentity.Create(
            match.Range.Start.Path,
            match.Range.Start.IsExternal);
        var relationshipEvidence = new RelationshipEvidenceRegistry();
        var relationshipDeclarations = new RelationshipDeclarationRegistry();
        var sectionValues = await CreateSectionValuesAsync(
                match,
                documentId,
                request.Sections,
                resolved.Scope,
                relationshipEvidence,
                relationshipDeclarations,
                cancellationToken)
            .ConfigureAwait(false);
        var budget = ContextBudget.Resolve(
            DefaultMaximumCharacters,
            explicitMaximumCharacters: request.MaxCharactersSpecified
                ? request.MaxCharacters
                : null,
            full: request.Full);
        var sectionSet = CreateBudgetedSections(
            request.Sections,
            sectionValues,
            budget,
            relationshipEvidence,
            relationshipDeclarations);
        var recoveryCommand = sectionSet.Truncated
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
                sectionSet.Sections,
                sectionSet.FullTotalCharacters,
                sectionSet.IncludedCharacters,
                sectionSet.OmittedSections,
                sectionSet.Truncated,
                sectionSet.RelationshipEnvelope,
                recoveryCommand,
                budget),
            resolved.Evidence);
    }

    private static BudgetedContextSections
        CreateBudgetedSections(
            IReadOnlyList<string> names,
            IReadOnlyDictionary<string, SymbolContextSectionPayload> values,
            ContextBudget budget,
            RelationshipEvidenceRegistry evidence,
            RelationshipDeclarationRegistry declarations)
    {
        var selectedHasPreviousSection = false;
        var selected = new List<ContextSection<SymbolContextSectionPayload>>(
            names.Count);
        var omitted = new List<string>(names.Count);
        var sourceSpanRefs = new HashSet<string>(StringComparer.Ordinal);
        var candidateRefs = new HashSet<string>(StringComparer.Ordinal);
        long includedSectionCharacters = 0;
        long fullSectionCharacters = 0;
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            var fullSection = ToonResultSerializer.CreateContextSectionForBudget(
                name,
                SectionOrder(name),
                values[name],
                hasPreviousSection: index > 0);
            fullSectionCharacters = checked(fullSectionCharacters + fullSection.IncludedCharacters);
            var section = ToonResultSerializer.CreateContextSectionForBudget(
                name,
                SectionOrder(name),
                values[name],
                selectedHasPreviousSection);
            var prospectiveSourceSpanRefs = new HashSet<string>(sourceSpanRefs, StringComparer.Ordinal);
            var prospectiveCandidateRefs = new HashSet<string>(candidateRefs, StringComparer.Ordinal);
            AddRelationshipReferences(section.Value, prospectiveSourceSpanRefs, prospectiveCandidateRefs);
            var prospectiveEnvelope = RelationshipEnvelope(
                prospectiveSourceSpanRefs,
                prospectiveCandidateRefs,
                evidence,
                declarations);
            var prospectiveEnvelopeCharacters = CountEnvelopeCharacters(prospectiveEnvelope);
            var prospectiveCharacters = checked(
                includedSectionCharacters + section.IncludedCharacters + prospectiveEnvelopeCharacters);
            if (budget.MaximumCharacters is null
                || prospectiveCharacters <= budget.MaximumCharacters.Value)
            {
                selectedHasPreviousSection = true;
                selected.Add(section);
                includedSectionCharacters = checked(includedSectionCharacters + section.IncludedCharacters);
                sourceSpanRefs = prospectiveSourceSpanRefs;
                candidateRefs = prospectiveCandidateRefs;
            }
            else
            {
                omitted.Add(name);
            }
        }

        var includedEnvelope = RelationshipEnvelope(
            sourceSpanRefs,
            candidateRefs,
            evidence,
            declarations);
        var fullEnvelope = new SymbolContextRelationshipEnvelopePayload(
            evidence.Items,
            declarations.Items);
        return new(
            Array.AsReadOnly(selected.ToArray()),
            checked(fullSectionCharacters + CountEnvelopeCharacters(fullEnvelope)),
            checked(includedSectionCharacters + CountEnvelopeCharacters(includedEnvelope)),
            Array.AsReadOnly(omitted.ToArray()),
            includedEnvelope);
    }

    private sealed record BudgetedContextSections(
        IReadOnlyList<ContextSection<SymbolContextSectionPayload>> Sections,
        long FullTotalCharacters,
        long IncludedCharacters,
        IReadOnlyList<string> OmittedSections,
        SymbolContextRelationshipEnvelopePayload RelationshipEnvelope)
    {
        internal bool Truncated => OmittedSections.Count > 0;
    }

    private static long CountCharacters(string value) => value.EnumerateRunes().LongCount();

    private static long CountEnvelopeCharacters(
        SymbolContextRelationshipEnvelopePayload envelope) =>
        envelope.RelationshipEvidence.Count == 0 && envelope.RelationshipDeclarations.Count == 0
            ? 0
            : CountCharacters(ToonResultSerializer.SerializePayloadValue(envelope));

    private static SymbolContextRelationshipEnvelopePayload RelationshipEnvelope(
        IReadOnlySet<string> sourceSpanRefs,
        IReadOnlySet<string> candidateRefs,
        RelationshipEvidenceRegistry evidence,
        RelationshipDeclarationRegistry declarations)
    {
        return new(
            evidence.Items.Where(item => sourceSpanRefs.Contains(item.Id)).ToArray(),
            declarations.Items.Where(item => candidateRefs.Contains(item.Id)).ToArray());
    }

    private static void AddRelationshipReferences(
        SymbolContextSectionPayload section,
        ISet<string> sourceSpanRefs,
        ISet<string> candidateRefs)
    {
        if (section.Data is not SymbolContextRelationshipPayload relationship)
        {
            return;
        }

        candidateRefs.UnionWith(relationship.CandidateRefs);
        foreach (var match in relationship.Matches)
        {
            if (match.GetType().GetProperty("SourceSpanRef")?.GetValue(match) is string sourceSpanRef)
            {
                sourceSpanRefs.Add(sourceSpanRef);
            }
        }
    }

    private static async ValueTask<
        IReadOnlyDictionary<string, SymbolContextSectionPayload>>
        CreateSectionValuesAsync(
            SymbolDeclarationMatch match,
            string documentId,
            IReadOnlyList<string> names,
            ResolvedSymbolWorkspaceScope scope,
            RelationshipEvidenceRegistry relationshipEvidence,
            RelationshipDeclarationRegistry relationshipDeclarations,
            CancellationToken cancellationToken)
    {
        var requirements = ContextSymbolSectionRequirements.From(names);
        var location = SymbolEvidencePipeline.Location(match);
        var values = new Dictionary<string, SymbolContextSectionPayload>(
            names.Count,
            StringComparer.Ordinal);
        SymbolDeclarationDetail? detail = null;
        if (requirements.RequiresDetail)
        {
            detail = await new SymbolDeclarationDetailReader()
                .ReadAsync(match, cancellationToken)
                .ConfigureAwait(false);
        }

        if (names.Contains("declaration", StringComparer.Ordinal))
        {
            values.Add("declaration", new(
                "symbol-entity-resolution",
                EvidenceResolution.Syntax,
                EvidenceConfidence.Candidate,
                new SymbolContextDeclarationPayload(
                    match.Id,
                    match.Kind,
                    match.Name,
                    match.FullyQualifiedName,
                    match.Signature,
                    match.Accessibility,
                    detail!.ContainingType,
                    documentId,
                    match.Id,
                    location,
                    detail.Relationships)));
        }

        if (names.Contains("owner", StringComparer.Ordinal))
        {
            values.Add("owner", new(
                "workspace-project-ownership",
                EvidenceResolution.Syntax,
                EvidenceConfidence.Candidate,
                SymbolEvidencePipeline.Owner(match)));
        }

        if (names.Contains("document", StringComparer.Ordinal))
        {
            values.Add("document", new(
                "resolved-declaration-source",
                EvidenceResolution.Text,
                EvidenceConfidence.Verified,
                new SymbolContextDocumentPayload(
                    documentId,
                    match.Range.Start.Path,
                    match.Range.Start.IsExternal,
                    match.IsGenerated,
                    "utf-8",
                    detail!.SourceByteCount,
                    [match.Id],
                    detail.SourceText)));
        }

        if (requirements.RequiresOutline)
        {
            var outline = new RoslynSourceOutliner()
                .OutlineSymbol(match, maxItems: null, cancellationToken);
            var outlineItems = outline.Items.Skip(1).ToArray();
            values.Add("outline", new(
                "roslyn-syntax-outline",
                EvidenceResolution.Syntax,
                EvidenceConfidence.Candidate,
                new SymbolContextOutlinePayload(
                    documentId,
                    match.Id,
                    outline.DiagnosticCount,
                    outline.TotalCount,
                    outlineItems.Length,
                    outlineItems)));
        }

        if (requirements.Relationships.Count > 0)
        {
            if (scope.Selection is null)
            {
                foreach (var relationship in requirements.Relationships)
                {
                    values.Add(SectionName(relationship), InapplicableRelationship(
                        "No solution or C# project is selected for semantic relationship analysis."));
                }
            }
            else
            {
                var relationshipContext = await new RoslynRelationshipContextSearcher(
                        scope.Traverser,
                        scope.Ownership,
                        scope.Projects)
                    .FindAsync(
                        match.Id,
                        scope.Workspace,
                        scope.Selection,
                        scope.Traversal,
                        scope.DeclarationScope,
                        requirements.Relationships,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (relationshipContext.References is { } references)
                {
                    values.Add("references", Relationship("compiler-semantic-references", references, relationshipEvidence, relationshipDeclarations));
                }

                if (relationshipContext.Implementations is { } implementations)
                {
                    values.Add("implementations", Relationship("compiler-semantic-implementations", implementations, relationshipEvidence, relationshipDeclarations));
                }

                if (relationshipContext.Overrides is { } overrides)
                {
                    values.Add("overrides", Relationship("compiler-semantic-overrides", overrides, relationshipEvidence, relationshipDeclarations));
                }

                if (relationshipContext.Derived is { } derived)
                {
                    values.Add("derived", Relationship("compiler-semantic-derived", derived, relationshipEvidence, relationshipDeclarations));
                }

                if (relationshipContext.Callers is { } callers)
                {
                    values.Add("callers", Relationship("compiler-semantic-callers", callers, relationshipEvidence, relationshipDeclarations));
                }

                if (relationshipContext.Callees is { } callees)
                {
                    values.Add("callees", Relationship("compiler-semantic-callees", callees, relationshipEvidence, relationshipDeclarations));
                }
            }
        }

        return values;
    }

    private static SymbolContextSectionPayload InapplicableRelationship(string reason) =>
        new(
            "compiler-semantic-relationship",
            EvidenceResolution.Semantic,
            EvidenceConfidence.Candidate,
            new SymbolContextRelationshipPayload(
                "inapplicable",
                "inapplicable",
                "none",
                new EvidenceCoverage(CoverageLevel.NotApplicable),
                [reason],
                Variants: [],
                CandidateRefs: [],
                CandidateTotal: 0,
                ErrorCode: null,
                Correction: null,
                Matches: []));

    private static SymbolContextSectionPayload Relationship(
        string provenance,
        RoslynReferenceSearchResult result,
        RelationshipEvidenceRegistry evidence,
        RelationshipDeclarationRegistry declarations) => Relationship(
            provenance, result.TargetStatus, result.ScopeMode.ToString(), result.Coverage,
            result.PartialReasons, result.Variants.Cast<object>(), result.Candidates,
            result.CandidateTotal, result.ErrorCode, result.Correction,
            result.Matches.Select(match => new
            {
                match.Id,
                SourceSpanRef = evidence.Add(match.Start, match.End),
                match.TargetIdentity,
                match.Project,
                match.Configuration,
                match.Framework,
                match.IsImplicit,
                match.Alias,
                match.CandidateReason,
            }), declarations);

    private static SymbolContextSectionPayload Relationship(
        string provenance,
        RoslynImplementationSearchResult result,
        RelationshipEvidenceRegistry evidence,
        RelationshipDeclarationRegistry declarations) => Relationship(
            provenance, result.TargetStatus, result.ScopeMode.ToString(), result.Coverage,
            result.PartialReasons, result.Variants.Cast<object>(), result.Candidates,
            result.CandidateTotal, result.ErrorCode, result.Correction,
            result.Matches.Select(match => new
            {
                match.Id,
                SourceSpanRef = evidence.Add(match.Start, match.End),
                match.TargetIdentity,
                match.ImplementationIdentity,
                match.InheritancePath,
                match.OverridePath,
                match.Owner,
                match.Project,
                match.Configuration,
                match.Framework,
            }), declarations);

    private static SymbolContextSectionPayload Relationship(
        string provenance,
        RoslynOverrideSearchResult result,
        RelationshipEvidenceRegistry evidence,
        RelationshipDeclarationRegistry declarations) => Relationship(
            provenance, result.TargetStatus, result.ScopeMode.ToString(), result.Coverage,
            result.PartialReasons, result.Variants.Cast<object>(), result.Candidates,
            result.CandidateTotal, result.ErrorCode, result.Correction,
            result.Matches.Select(match => new
            {
                match.Id,
                SourceSpanRef = evidence.Add(match.Start, match.End),
                match.TargetIdentity,
                match.OverrideIdentity,
                match.OverridePath,
                match.Owner,
                match.Project,
                match.Configuration,
                match.Framework,
            }), declarations);

    private static SymbolContextSectionPayload Relationship(
        string provenance,
        RoslynDerivedTypeSearchResult result,
        RelationshipEvidenceRegistry evidence,
        RelationshipDeclarationRegistry declarations) => Relationship(
            provenance, result.TargetStatus, result.ScopeMode.ToString(), result.Coverage,
            result.PartialReasons, result.Variants.Cast<object>(), result.Candidates,
            result.CandidateTotal, result.ErrorCode, result.Correction,
            result.Matches.Select(match => new
            {
                match.Id,
                SourceSpanRef = evidence.Add(match.Start, match.End),
                match.TargetIdentity,
                match.DerivedIdentity,
                match.InheritancePath,
                match.Owner,
                match.Project,
                match.Configuration,
                match.Framework,
            }), declarations);

    private static SymbolContextSectionPayload Relationship(
        string provenance,
        RoslynCallerSearchResult result,
        RelationshipEvidenceRegistry evidence,
        RelationshipDeclarationRegistry declarations) => Relationship(
            provenance, result.TargetStatus, result.ScopeMode.ToString(), result.Coverage,
            result.PartialReasons, result.Variants.Cast<object>(), result.Candidates,
            result.CandidateTotal, result.ErrorCode, result.Correction,
            result.Matches.Select(match => new
            {
                match.Id,
                SourceSpanRef = evidence.Add(match.Start, match.End),
                match.TargetIdentity,
                match.Project,
                match.Configuration,
                match.Framework,
                match.ContainingSymbol,
                match.Relationship,
                match.Resolution,
                match.Confidence,
            }), declarations);

    private static SymbolContextSectionPayload Relationship(
        string provenance,
        RoslynCalleeSearchResult result,
        RelationshipEvidenceRegistry evidence,
        RelationshipDeclarationRegistry declarations) => Relationship(
            provenance, result.TargetStatus, result.ScopeMode.ToString(), result.Coverage,
            result.PartialReasons, result.Variants.Cast<object>(), result.Candidates,
            result.CandidateTotal, result.ErrorCode, result.Correction,
            result.Matches.Select(match => new
            {
                match.Id,
                SourceSpanRef = evidence.Add(match.Start, match.End),
                match.TargetIdentity,
                match.Project,
                match.Configuration,
                match.Framework,
                match.ContainingSymbol,
                match.Relationship,
                match.Resolution,
                match.Confidence,
            }), declarations);

    private static SymbolContextSectionPayload Relationship(
        string provenance,
        SemanticTargetResolutionStatus targetStatus,
        string scopeMode,
        EvidenceCoverage coverage,
        IReadOnlyList<string> partialReasons,
        IEnumerable<object> variants,
        IReadOnlyList<SymbolDeclarationMatch> candidates,
        int candidateTotal,
        string? errorCode,
        string? correction,
        IEnumerable<object> matches,
        RelationshipDeclarationRegistry declarations) =>
        new(
            provenance,
            EvidenceResolution.Semantic,
            targetStatus is SemanticTargetResolutionStatus.Resolved
                && coverage.Level is CoverageLevel.Complete
                ? EvidenceConfidence.Verified
                : EvidenceConfidence.Candidate,
            new SymbolContextRelationshipPayload(
                targetStatus is not SemanticTargetResolutionStatus.Resolved
                    ? "failed"
                    : coverage.Level is CoverageLevel.Complete
                        ? "complete"
                        : "partial",
                targetStatus.ToString().ToLowerInvariant(),
                scopeMode.ToLowerInvariant(),
                coverage,
                partialReasons,
                Array.AsReadOnly(variants.ToArray()),
                Array.AsReadOnly(candidates.Select(declarations.Add).ToArray()),
                candidateTotal,
                errorCode,
                correction,
                Array.AsReadOnly(matches.ToArray())));

    private static string SectionName(SemanticRelationshipKind relationship) => relationship switch
    {
        SemanticRelationshipKind.References => "references",
        SemanticRelationshipKind.Implementations => "implementations",
        SemanticRelationshipKind.Overrides => "overrides",
        SemanticRelationshipKind.Derived => "derived",
        SemanticRelationshipKind.Callers => "callers",
        SemanticRelationshipKind.Callees => "callees",
        _ => throw new ArgumentOutOfRangeException(nameof(relationship), relationship, null),
    };

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
        "references" => 4,
        "implementations" => 5,
        "overrides" => 6,
        "derived" => 7,
        "callers" => 8,
        "callees" => 9,
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
    IReadOnlyList<SymbolContextRelationshipEvidencePayload>? RelationshipEvidence,
    IReadOnlyList<SymbolContextRelationshipCandidatePayload>? RelationshipDeclarations,
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
        IReadOnlyList<ContextSection<SymbolContextSectionPayload>> sections,
        long fullTotalCharacters,
        long includedCharacters,
        IReadOnlyList<string> omittedSections,
        bool truncated,
        SymbolContextRelationshipEnvelopePayload relationshipEnvelope,
        string? recoveryCommand,
        ContextBudget budget) =>
        new(
            target,
            relationshipEnvelope.RelationshipEvidence.Count > 0
                || relationshipEnvelope.RelationshipDeclarations.Count > 0
                    ? relationshipEnvelope.RelationshipEvidence
                    : null,
            relationshipEnvelope.RelationshipEvidence.Count > 0
                || relationshipEnvelope.RelationshipDeclarations.Count > 0
                    ? relationshipEnvelope.RelationshipDeclarations
                    : null,
            budget.Mode,
            budget.MaximumCharacters,
            sections,
            includedCharacters,
            TotalKnown: true,
            fullTotalCharacters,
            fullTotalCharacters - includedCharacters,
            omittedSections,
            EstimateTokens(includedCharacters),
            truncated,
            recoveryCommand);

    private static ApproximateTokenRange EstimateTokens(long characters) => new(
        DivideCeiling(characters, 6),
        DivideCeiling(characters, 2));

    private static long DivideCeiling(long value, long divisor) =>
        value == 0 ? 0 : ((value - 1) / divisor) + 1;
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

internal sealed record SymbolContextRelationshipPayload(
    string Status,
    string TargetStatus,
    string ScopeMode,
    EvidenceCoverage Coverage,
    IReadOnlyList<string> PartialReasons,
    IReadOnlyList<object> Variants,
    IReadOnlyList<string> CandidateRefs,
    int CandidateTotal,
    string? ErrorCode,
    string? Correction,
    IReadOnlyList<object> Matches);

internal sealed record SymbolContextRelationshipCandidatePayload(
    string Id,
    string Kind,
    string Name,
    string FullyQualifiedName,
    string Signature,
    IReadOnlyList<string> Projects,
    string File,
    int Line,
    int Column);

internal sealed record SymbolContextRelationshipEvidencePayload(
    string Id,
    SourceLocation Start,
    SourceLocation End);

internal sealed record SymbolContextRelationshipEnvelopePayload(
    IReadOnlyList<SymbolContextRelationshipEvidencePayload> RelationshipEvidence,
    IReadOnlyList<SymbolContextRelationshipCandidatePayload> RelationshipDeclarations);

internal sealed class RelationshipEvidenceRegistry
{
    private readonly Dictionary<SourceSpanKey, SymbolContextRelationshipEvidencePayload>
        _items = [];

    internal IReadOnlyList<SymbolContextRelationshipEvidencePayload> Items =>
        Array.AsReadOnly(_items.Values
            .OrderBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray());

    internal string Add(SourceLocation start, SourceLocation end)
    {
        var key = new SourceSpanKey(start, end);
        if (!_items.TryGetValue(key, out var item))
        {
            var id = "source-span/v1/"
                + start.Path
                + ":"
                + start.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":"
                + start.Column.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "-"
                + end.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":"
                + end.Column.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (start.IsExternal ? ":external" : string.Empty);
            item = new SymbolContextRelationshipEvidencePayload(id, start, end);
            _items.Add(key, item);
        }

        return item.Id;
    }

    private readonly record struct SourceSpanKey(SourceLocation Start, SourceLocation End);
}

internal sealed class RelationshipDeclarationRegistry
{
    private readonly Dictionary<string, SymbolContextRelationshipCandidatePayload> _items =
        new(StringComparer.Ordinal);

    internal IReadOnlyList<SymbolContextRelationshipCandidatePayload> Items =>
        Array.AsReadOnly(_items.Values
            .OrderBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray());

    internal string Add(SymbolDeclarationMatch candidate)
    {
        if (!_items.ContainsKey(candidate.Id))
        {
            _items.Add(candidate.Id, new SymbolContextRelationshipCandidatePayload(
                candidate.Id,
                candidate.Kind,
                candidate.Name,
                candidate.FullyQualifiedName,
                candidate.Signature,
                candidate.OwningProjects,
                candidate.Range.Start.Path,
                candidate.Range.Start.Line,
                candidate.Range.Start.Column));
        }

        return candidate.Id;
    }
}

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
