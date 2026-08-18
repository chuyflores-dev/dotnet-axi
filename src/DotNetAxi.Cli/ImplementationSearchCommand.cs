using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record ImplementationSearchCommandRequest(
    string Target,
    SymbolWorkspaceScopeRequest Scope,
    string? Configuration,
    string? Framework,
    IReadOnlyList<MsBuildProperty> Properties,
    bool Complete,
    int Limit,
    bool LimitSpecified,
    bool Full,
    IReadOnlyList<string> Fields)
{
    public static readonly string[] AvailableFields =
    [
        "id",
        "file",
        "line",
        "column",
        "end_line",
        "end_column",
        "project",
        "configuration",
        "framework",
        "owner",
        "target_identity",
        "external",
    ];

    public static ImplementationSearchCommandRequest Create(
        string target,
        string? solution,
        string? project,
        bool includeTests,
        bool includeGenerated,
        string? configuration,
        string? framework,
        IReadOnlyList<string> properties,
        bool complete,
        int limit,
        bool limitSpecified,
        bool full,
        IReadOnlyList<string> fields)
    {
        if (string.IsNullOrWhiteSpace(target)
            || target.Contains('\0', StringComparison.Ordinal))
        {
            throw Usage(
                "usage.implementation_target_required",
                "The implementation target must be a non-blank declaration query or symbol/v2 ID.",
                "Provide a canonical symbol/v2 ID, fully qualified name, or declaration query.");
        }

        if (limit < 0 || full && limitSpecified)
        {
            throw Usage(
                "usage.implementation_limit",
                "The --limit value is invalid for this implementation search.",
                "Use a non-negative --limit, or use --full without --limit.");
        }

        configuration = Optional(configuration, "--configuration");
        framework = Optional(framework, "--framework");
        var parsedProperties = ParseProperties(properties);

        fields = OutputFieldSelection.Parse(fields);
        if (fields.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.implementation_field",
                "A --fields value cannot be blank.",
                UsageErrorResult.FieldCatalogCorrection(AvailableFields));
        }

        var unknown = fields
            .Where(field => !AvailableFields.Contains(
                field,
                StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new UnknownOutputFieldsException(unknown, AvailableFields);
        }

        return new ImplementationSearchCommandRequest(
            target,
            SymbolWorkspaceScopeRequest.Create(
                solution,
                project,
                paths: [],
                includeTests,
                includeGenerated,
                "usage.implementation_path"),
            configuration,
            framework,
            parsedProperties,
            complete,
            limit,
            limitSpecified,
            full,
            fields);
    }

    public ProjectGraphEvaluationOptions EvaluationOptions() =>
        new(Configuration, Framework, Properties);

    private static string? Optional(string? value, string option)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw Usage(
                "usage.implementation_build_selector",
                $"The {option} value cannot be blank.",
                $"Provide a non-blank value for {option}.");
        }

        return value.Trim();
    }

    private static IReadOnlyList<MsBuildProperty> ParseProperties(
        IEnumerable<string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var parsed = new List<MsBuildProperty>();
        foreach (var property in properties)
        {
            var separator = property?.IndexOf('=') ?? -1;
            if (separator <= 0)
            {
                throw Usage(
                    "usage.implementation_property",
                    "Each --property value must use name=value syntax.",
                    "Pass --property Name=Value; repeat the option for additional properties.");
            }

            var name = property![..separator].Trim();
            if (name.Length == 0)
            {
                throw Usage(
                    "usage.implementation_property",
                    "An MSBuild property name cannot be blank.",
                    "Pass --property Name=Value.");
            }

            parsed.Add(new MsBuildProperty(name, property[(separator + 1)..]));
        }

        return Array.AsReadOnly(parsed.ToArray());
    }

    private static CommandUsageException Usage(
        string code,
        string message,
        string correction) =>
        new(code, message, correction);
}

internal sealed class ImplementationSearchCommandHandler :
    ICommandHandler<ImplementationSearchCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        ImplementationSearchCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fields = ImplementationFields.Select(request.Fields);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = new WorkspaceDiscoverer()
            .Discover(Directory.GetCurrentDirectory());
        var scope = SymbolWorkspaceScopeResolver.Resolve(
            workspace,
            request.Scope);
        if (scope.Selection is null)
        {
            throw new CommandUsageException(
                "usage.implementation_workspace_selection_required",
                "Implementation search requires one selected solution or C# project.",
                "Add or select a .sln, .slnx, or .csproj with --solution or --project.");
        }

        RoslynImplementationSearchResult result;
        try
        {
            result = await new RoslynImplementationSearcher(
                    scope.Traverser,
                    scope.Ownership,
                    scope.Projects)
                .FindAsync(
                    request.Target,
                    workspace,
                    scope.Selection,
                    scope.Traversal,
                    scope.DeclarationScope,
                    request.Complete
                        ? ImplementationSearchScopeMode.Complete
                        : ImplementationSearchScopeMode.Default,
                    request.EvaluationOptions(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProjectGraphUsageException exception)
        {
            throw new CommandUsageException(
                exception.Code,
                exception.Message,
                exception.Correction,
                exception.Declarations.Select(static declaration =>
                    declaration.Project));
        }

        var evidence = Evidence(result, scope);
        if (!result.TargetResolved)
        {
                var failurePayload = new ImplementationSearchFailurePayload(
                OperationClassification.Executing,
                result.Target,
                result.TargetStatus.ToString().ToLowerInvariant(),
                result.Candidates.Count,
                result.CandidateTotal,
                result.CandidateOmitted,
                result.CandidateTruncated,
                result.Variants.Select(Variant).ToArray(),
                result.Candidates.Select(Candidate).ToArray());
            return CommandResult<ImplementationSearchFailurePayload>.Failed(
                "search implementations",
                [new ResultError(
                    result.ErrorCode ?? "semantic.target_unresolved",
                    $"The semantic target `{request.Target}` could not be resolved ({result.TargetStatus.ToString().ToLowerInvariant()}).",
                    result.Correction
                        ?? "Correct the semantic target and retry.")],
                failurePayload,
                evidence);
        }

        var included = request.Full
            ? result.Matches
            : result.Matches.Take(request.Limit);
        var bounded = BoundedCollection<IReadOnlyDictionary<string, object?>>
            .FromObserved(
                Project(included, fields, cancellationToken),
                result.Matches.Count,
                totalKnown: true,
                RetrievalCommand(request, scope));
        var payload = new ImplementationSearchPayload(
            OperationClassification.Executing,
            result.Target,
            result.TargetId!,
            request.Complete ? "complete" : "default",
            bounded.Count,
            bounded.TotalKnown,
            bounded.Total,
            bounded.Omitted,
            bounded.Truncated,
            bounded.RetrievalCommand,
            result.PartialReasons,
            result.Variants.Select(Variant).ToArray(),
            bounded.Items);
        return result.Coverage.Level is CoverageLevel.Complete
            ? CommandResult<ImplementationSearchPayload>.Success(
                "search implementations",
                payload,
                evidence)
            : CommandResult<ImplementationSearchPayload>.Partial(
                "search implementations",
                payload,
                evidence);
    }

    private static Evidence? Evidence(
        RoslynImplementationSearchResult result,
        ResolvedSymbolWorkspaceScope scope)
    {
        if (result.Snapshot is null)
        {
            return null;
        }

        var projects = result.Variants.Count == 0
            ? scope.Projects
            : result.Variants
                .Select(static variant => variant.Project)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        var frameworks = result.Variants
            .Select(static variant => variant.Framework)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new Evidence(
            result.Snapshot,
            EvidenceResolution.Semantic,
            result.Coverage,
            EvidenceConfidence.Verified,
            new EvidenceScope(
                scope.Workspace.RootPath,
                result.TargetResolved
                    ? "evaluated reverse project-graph implementation candidates"
                    : "semantic target resolution before implementation traversal",
                solution: scope.Selection?.Kind
                    is WorkspaceEntryPointKind.Solution
                    ? scope.Selection.Path
                    : null,
                projects,
                frameworks,
                eligibility: new EvidenceEligibility(
                    scope.Request.IncludeTests,
                    scope.Request.IncludeGenerated)));
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> Project(
        IEnumerable<RoslynImplementationMatch> matches,
        OutputFieldSelection<RoslynImplementationMatch> fields,
        CancellationToken cancellationToken)
    {
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return fields.Project(match);
        }
    }

    private static IReadOnlyDictionary<string, object?> Candidate(
        SymbolDeclarationMatch match) =>
        new Dictionary<string, object?>
        {
            ["id"] = match.Id,
            ["signature"] = match.Signature,
            ["fully_qualified_name"] = match.FullyQualifiedName,
            ["owning_projects"] = match.OwningProjects,
        };

    private static IReadOnlyDictionary<string, object?> Variant(
        ImplementationSearchVariant variant) =>
        new Dictionary<string, object?>
        {
            ["project"] = variant.Project,
            ["configuration"] = variant.Configuration,
            ["framework"] = variant.Framework,
            ["status"] = variant.Status.ToString().ToLowerInvariant(),
            ["reason"] = variant.Reason,
            ["correction"] = variant.Correction,
        };

    private static string RetrievalCommand(
        ImplementationSearchCommandRequest request,
        ResolvedSymbolWorkspaceScope scope)
    {
        var command = "dnaxi search implementations "
            + Quote(request.Target)
            + scope.CanonicalArguments();
        if (request.Complete)
        {
            command += " --complete";
        }

        if (request.Configuration is not null)
        {
            command += " --configuration " + Quote(request.Configuration);
        }

        if (request.Framework is not null)
        {
            command += " --framework " + Quote(request.Framework);
        }

        foreach (var property in request.Properties)
        {
            command += " --property "
                + Quote(property.Name + "=" + property.Value);
        }

        if (request.Fields.Count > 0)
        {
            command += " --fields "
                + Quote(OutputFieldSelection.CanonicalValue(request.Fields));
        }

        return CanonicalInvocation.OneShot(command + " --full");
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static readonly OutputFieldSet<RoslynImplementationMatch>
        ImplementationFields = new(
        [
            new("id", static match => match.Id),
            new("file", static match => match.Start.Path, includedByDefault: true),
            new("line", static match => match.Start.Line, includedByDefault: true),
            new("column", static match => match.Start.Column),
            new("end_line", static match => match.End.Line),
            new("end_column", static match => match.End.Column),
            new("project", static match => match.Project, includedByDefault: true),
            new("configuration", static match => match.Configuration),
            new("framework", static match => match.Framework, includedByDefault: true),
            new("owner", static match => match.Owner),
            new("target_identity", static match => match.TargetIdentity),
            new("external", static match => match.Start.IsExternal),
        ]);

    private sealed record ImplementationSearchPayload(
        OperationClassification Classification,
        string Target,
        string TargetId,
        string ScopeMode,
        int Count,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<string> PartialReasons,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Variants,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Matches);

    private sealed record ImplementationSearchFailurePayload(
        OperationClassification Classification,
        string Target,
        string TargetStatus,
        int CandidateCount,
        int CandidateTotal,
        int CandidateOmitted,
        bool CandidatesTruncated,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Variants,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Candidates);
}
