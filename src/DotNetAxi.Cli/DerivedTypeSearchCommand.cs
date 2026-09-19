using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record DerivedTypeSearchCommandRequest(
    ImplementationSearchCommandRequest Shared,
    IReadOnlyList<string> Fields)
{
    internal static readonly string[] AvailableFields =
    ["id", "file", "line", "column", "end_line", "end_column", "project", "configuration", "framework", "owner", "target_identity", "derived_identity", "inheritance_path", "external"];

    public static DerivedTypeSearchCommandRequest Create(
        string target, string? solution, string? project, bool includeTests,
        bool includeGenerated, string? configuration, string? framework,
        IReadOnlyList<string> properties, bool complete, int limit,
        bool limitSpecified, bool full, IReadOnlyList<string> fields)
    {
        var parsed = OutputFieldSelection.Parse(fields);
        var unknown = parsed.Where(field => !AvailableFields.Contains(field, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new UnknownOutputFieldsException(unknown, AvailableFields);
        }

        return new DerivedTypeSearchCommandRequest(
            ImplementationSearchCommandRequest.Create(target, solution, project,
                includeTests, includeGenerated, configuration, framework, properties,
                complete, limit, limitSpecified, full, []), parsed);
    }
}

internal sealed class DerivedTypeSearchCommandHandler : ICommandHandler<DerivedTypeSearchCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        DerivedTypeSearchCommandRequest request,
        CancellationToken cancellationToken)
    {
        var shared = request.Shared;
        var workspace = new WorkspaceDiscoverer().Discover(Directory.GetCurrentDirectory());
        var scope = SymbolWorkspaceScopeResolver.Resolve(workspace, shared.Scope);
        if (scope.Selection is null)
        {
            throw new CommandUsageException("usage.derived_workspace_selection_required",
                "Derived-type search requires one selected solution or C# project.",
                "Add or select a .sln, .slnx, or .csproj with --solution or --project.");
        }

        RoslynDerivedTypeSearchResult result;
        try
        {
            result = await new RoslynDerivedTypeSearcher(scope.Traverser, scope.Ownership, scope.Projects)
                .FindAsync(shared.Target, workspace, scope.Selection, scope.Traversal,
                    scope.DeclarationScope,
                    shared.Complete ? DerivedTypeSearchScopeMode.Complete : DerivedTypeSearchScopeMode.Default,
                    shared.EvaluationOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (ProjectGraphUsageException exception)
        {
            throw new CommandUsageException(exception.Code, exception.Message, exception.Correction,
                exception.Declarations.Select(static declaration => declaration.Project));
        }

        var evidence = Evidence(result, scope);
        if (!result.TargetResolved)
        {
            return CommandResult<FailurePayload>.Failed("search derived",
                [new ResultError(result.ErrorCode ?? "semantic.target_unresolved",
                    $"The semantic target `{shared.Target}` could not be resolved ({result.TargetStatus.ToString().ToLowerInvariant()}).",
                    result.Correction ?? "Correct the semantic target and retry.")],
                new FailurePayload(OperationClassification.Executing, result.Target,
                    result.TargetStatus.ToString().ToLowerInvariant(), result.Candidates.Count,
                    result.CandidateTotal, result.CandidateOmitted, result.CandidateTruncated,
                    result.Variants.Select(Variant).ToArray(), result.Candidates.Select(Candidate).ToArray()), evidence);
        }

        var included = shared.Full ? result.Matches : result.Matches.Take(shared.Limit);
        var bounded = BoundedCollection<IReadOnlyDictionary<string, object?>>.FromObserved(
            included.Select(match => Project(match, request.Fields)), result.Matches.Count, true,
            RetrievalCommand(shared, request.Fields, scope));
        var payload = new Payload(OperationClassification.Executing, result.Target, result.TargetId!,
            shared.Complete ? "complete" : "default", bounded.Count, bounded.TotalKnown, bounded.Total,
            bounded.Omitted, bounded.Truncated, bounded.RetrievalCommand, result.PartialReasons,
            result.Variants.Select(Variant).ToArray(), bounded.Items);
        return result.Coverage.Level is CoverageLevel.Complete
            ? CommandResult<Payload>.Success("search derived", payload, evidence)
            : CommandResult<Payload>.Partial("search derived", payload, evidence);
    }

    private static Evidence? Evidence(RoslynDerivedTypeSearchResult result, ResolvedSymbolWorkspaceScope scope) =>
        result.Snapshot is null ? null : new Evidence(result.Snapshot, EvidenceResolution.Semantic,
            result.Coverage, EvidenceConfidence.Verified, new EvidenceScope(scope.Workspace.RootPath,
                result.TargetResolved ? "evaluated reverse project-graph derived-type candidates" : "semantic target resolution before derived-type traversal",
                scope.Selection?.Kind is WorkspaceEntryPointKind.Solution ? scope.Selection.Path : null,
                result.Variants.Select(static variant => variant.Project).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                result.Variants.Select(static variant => variant.Framework).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                eligibility: new EvidenceEligibility(scope.Request.IncludeTests, scope.Request.IncludeGenerated)));

    private static IReadOnlyDictionary<string, object?> Project(RoslynDerivedTypeMatch match, IReadOnlyList<string> fields)
    {
        var all = new Dictionary<string, object?> { ["id"] = match.Id, ["file"] = match.Start.Path, ["line"] = match.Start.Line, ["column"] = match.Start.Column, ["end_line"] = match.End.Line, ["end_column"] = match.End.Column, ["project"] = match.Project, ["configuration"] = match.Configuration, ["framework"] = match.Framework, ["owner"] = match.Owner, ["target_identity"] = match.TargetIdentity, ["derived_identity"] = match.DerivedIdentity, ["inheritance_path"] = match.InheritancePath, ["external"] = match.Start.IsExternal };
        var selected = fields.Count == 0 ? new[] { "file", "line", "project", "framework", "derived_identity", "inheritance_path" } : fields;
        return selected.ToDictionary(field => field, field => all[field], StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> Variant(ImplementationSearchVariant variant) => new Dictionary<string, object?> { ["project"] = variant.Project, ["configuration"] = variant.Configuration, ["framework"] = variant.Framework, ["status"] = variant.Status.ToString().ToLowerInvariant(), ["reason"] = variant.Reason, ["correction"] = variant.Correction };
    private static IReadOnlyDictionary<string, object?> Candidate(SymbolDeclarationMatch match) => new Dictionary<string, object?> { ["id"] = match.Id, ["signature"] = match.Signature, ["fully_qualified_name"] = match.FullyQualifiedName, ["owning_projects"] = match.OwningProjects };
    private static string RetrievalCommand(ImplementationSearchCommandRequest request, IReadOnlyList<string> fields, ResolvedSymbolWorkspaceScope scope) => CanonicalInvocation.OneShot("dnaxi search derived '" + request.Target.Replace("'", "'\\''", StringComparison.Ordinal) + "'" + scope.CanonicalArguments() + (request.Complete ? " --complete" : "") + (request.Configuration is null ? string.Empty : " --configuration '" + request.Configuration + "'") + (request.Framework is null ? string.Empty : " --framework '" + request.Framework + "'") + string.Concat(request.Properties.Select(property => " --property '" + property.Name + "=" + property.Value + "'")) + (fields.Count == 0 ? string.Empty : " --fields '" + OutputFieldSelection.CanonicalValue(fields) + "'") + " --full");

    private sealed record Payload(OperationClassification Classification, string Target, string TargetId, string ScopeMode, int Count, bool TotalKnown, int? Total, int? Omitted, bool Truncated, string? RetrievalCommand, IReadOnlyList<string> PartialReasons, IReadOnlyList<IReadOnlyDictionary<string, object?>> Variants, IReadOnlyList<IReadOnlyDictionary<string, object?>> Matches);
    private sealed record FailurePayload(OperationClassification Classification, string Target, string TargetStatus, int CandidateCount, int CandidateTotal, int CandidateOmitted, bool CandidatesTruncated, IReadOnlyList<IReadOnlyDictionary<string, object?>> Variants, IReadOnlyList<IReadOnlyDictionary<string, object?>> Candidates);
}
