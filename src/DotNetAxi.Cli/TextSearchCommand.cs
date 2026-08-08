using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Search;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record TextSearchCommandRequest(
    string Query, bool CaseSensitive, bool IncludeGenerated, bool Regex,
    int Limit, bool LimitSpecified, bool Full, IReadOnlyList<string> Fields,
    IReadOnlyList<string> Paths, string? Project, bool Changed, string? Base,
    string? Head)
{
    public static readonly string[] MatchFieldNames = ["id", "file", "line", "preview", "column", "external"];
    public static readonly string[] AvailableFields = [.. MatchFieldNames, "skip_details"];

    public static TextSearchCommandRequest Create(
        string query, bool caseSensitive, bool includeGenerated, bool regex,
        int limit, bool limitSpecified, bool full, IReadOnlyList<string> fields,
        IReadOnlyList<string> paths, string? project, bool changed, string? @base,
        string? head)
    {
        if (fields.Any(string.IsNullOrWhiteSpace))
        {
            throw new CommandUsageException("usage.text_field", "A --fields value cannot be blank.",
                $"Use `--fields` with one or more of: {string.Join(", ", AvailableFields)}.");
        }

        var unknown = fields.Where(field => !AvailableFields.Contains(field, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new UnknownOutputFieldsException(unknown, AvailableFields);
        }

        return new(query, caseSensitive, includeGenerated, regex, limit, limitSpecified,
            full, fields, paths, project, changed, @base, head);
    }
}

internal sealed class TextSearchCommandHandler : ICommandHandler<TextSearchCommandRequest>
{
    private const int DefaultLimit = 100;
    private static readonly TimeSpan DefaultRegexPerFileTimeout =
        TimeSpan.FromSeconds(1);

    private readonly ChangedScopeResolver _changedScopeResolver;

    public TextSearchCommandHandler()
        : this(ChangedScopeResolver.CreatePassive(new ProcessRunner()))
    {
    }

    internal TextSearchCommandHandler(
        ChangedScopeResolver changedScopeResolver)
    {
        _changedScopeResolver = changedScopeResolver
            ?? throw new ArgumentNullException(nameof(changedScopeResolver));
    }

    public async ValueTask<ICommandResult> HandleAsync(TextSearchCommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var includeSkipDetails = request.Fields.Contains("skip_details", StringComparer.Ordinal);
        var fields = TextSearchFields.Select(request.Fields.Where(field => field != "skip_details"));

        cancellationToken.ThrowIfCancellationRequested();
        var workspace = new WorkspaceDiscoverer().Discover(Directory.GetCurrentDirectory());
        var declaredScopes = ResolveDeclaredScopes(workspace, request.Paths);
        string? projectDirectory = null;
        string? selectedProject = null;
        if (request.Project is not null)
        {
            try
            {
                var selected = new WorkspaceEntryPointSelector().Select(workspace, new WorkspaceSelectionRequest(project: request.Project));
                selectedProject = selected.Path;
                projectDirectory = Path.GetDirectoryName(selected.Path)?.Replace('\\', '/') ?? string.Empty;
            }
            catch (WorkspaceSelectionUsageException exception)
            {
                throw new CommandUsageException(exception.Code, exception.Message, exception.Correction);
            }
        }

        ChangedScopeResult? changedScope = null;
        if (request.Changed)
        {
            try
            {
                changedScope = await _changedScopeResolver.ResolveAsync(
                    workspace, new ChangedScopeRequest(request.Base, request.Head), cancellationToken).ConfigureAwait(false);
            }
            catch (ChangedScopeResolutionException exception) when (exception.Kind is ChangedScopeErrorKind.GitRequired
                or ChangedScopeErrorKind.HeadRequiresBase or ChangedScopeErrorKind.InvalidBaseReference
                or ChangedScopeErrorKind.InvalidHeadReference)
            {
                throw new CommandUsageException(exception.Code, exception.Message, exception.Correction);
            }
            catch (ChangedScopeResolutionException exception)
            {
                return CommandResult<TextSearchPayload>.Failed("search text", [new ResultError(exception.Code, exception.Message, exception.Correction)]);
            }
        }

        var traversal = new WorkspaceTraversalRequest(workspace.RootPath, explicitPaths: request.Paths,
            includeGenerated: request.IncludeGenerated, currentDirectory: workspace.CurrentDirectory);
        var scoped = new ScopedTraverser(new WorkspacePathTraverser(), projectDirectory,
            changedScope?.ChangedPaths.ToHashSet(StringComparer.Ordinal));
        IReadOnlySet<string> allSelectable = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlySet<string> currentSelectable = new HashSet<string>(StringComparer.Ordinal);
        if (changedScope is not null)
        {
            var generatedTraversal = new WorkspaceTraversalRequest(workspace.RootPath, explicitPaths: request.Paths,
                includeGenerated: true, currentDirectory: workspace.CurrentDirectory);
            allSelectable = new ScopedTraverser(new WorkspacePathTraverser(), projectDirectory, null)
                .Traverse(generatedTraversal, cancellationToken)
                .Select(path => path.RelativePath)
                .ToHashSet(StringComparer.Ordinal);
            currentSelectable = new ScopedTraverser(new WorkspacePathTraverser(), projectDirectory, null)
                .Traverse(traversal, cancellationToken)
                .Select(path => path.RelativePath)
                .ToHashSet(StringComparer.Ordinal);
        }

        var limit = request.Full ? int.MaxValue : request.Limit;
        var skippedDetailLimit = request.Full && includeSkipDetails
            ? int.MaxValue
            : 50;
        var accelerator = new RgTextSearchAccelerator(new ProcessRunner());
        TextSearchResult result;
        if (request.Regex)
        {
            result = await new RegexTextSearcher(scoped, accelerator).SearchAsync(
                new RegexTextSearchRequest(
                    request.Query,
                    traversal,
                    DefaultRegexPerFileTimeout,
                    request.CaseSensitive,
                    limit,
                    skippedDetailLimit: skippedDetailLimit),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await new LiteralTextSearcher(scoped, accelerator).SearchAsync(
                new TextSearchRequest(
                    request.Query,
                    traversal,
                    request.CaseSensitive,
                    limit,
                    skippedDetailLimit: skippedDetailLimit),
                cancellationToken).ConfigureAwait(false);
        }

        var resultErrors = result.Errors.Select(ToResultError).ToArray();
        if (result.Errors.Any(error =>
                error.Kind is TextSearchErrorKind.InvalidRegularExpression))
        {
            return CommandResult<TextSearchPayload>.Failed(
                "search text",
                resultErrors);
        }

        var changedCoverage = changedScope is null ? null : BuildChangedCoverage(changedScope, result,
            declaredScopes, projectDirectory, allSelectable, currentSelectable);
        var coverage = changedCoverage?.Coverage ?? (result.TotalKnown
            ? new EvidenceCoverage(CoverageLevel.Complete)
            : new EvidenceCoverage(
                CoverageLevel.Partial,
                partialReason: result.Errors.Any(error =>
                    error.Kind is TextSearchErrorKind.RegularExpressionTimeout)
                    ? "One or more files exceeded the regular-expression timeout."
                    : "Collection stopped at the requested result limit."));
        var evidence = new Evidence(result.Snapshot, EvidenceResolution.Text, coverage, EvidenceConfidence.Verified,
            new EvidenceScope(workspace.RootPath, request.Changed ? "changed paths" : "workspace paths",
                projects: selectedProject is null ? null : [selectedProject]));
        var bounded = BoundedCollection<IReadOnlyDictionary<string, object?>>.FromObserved(
            fields.Project(result.Matches), result.Total, result.TotalKnown, RetrievalCommand(request));
        var payload = TextSearchPayload.From(result, bounded, changedScope, changedCoverage?.Observations ?? [], includeSkipDetails, RetrievalCommand(request));
        return coverage.Level is CoverageLevel.Complete
            ? CommandResult<TextSearchPayload>.Success("search text", payload, evidence)
            : CommandResult<TextSearchPayload>.Partial(
                "search text",
                payload,
                evidence,
                errors: resultErrors);
    }

    private static void ValidateRequest(TextSearchCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) throw Usage("usage.text_query_required", "The text query cannot be blank.", "Provide a non-blank text query.");
        if (request.Limit < 0 || (request.Full && request.LimitSpecified)) throw Usage("usage.text_limit", "The --limit value is invalid for this invocation.", "Use a non-negative --limit, or use --full without --limit.");
        if (!request.Changed && (request.Base is not null || request.Head is not null)) throw Usage("usage.changed_selector_requires_changed", "--base and --head require --changed.", "Add --changed or remove --base and --head.");
        if (request.Project is not null && string.IsNullOrWhiteSpace(request.Project)) throw Usage("usage.text_project", "The --project value cannot be blank.", "Provide a project selector or remove --project.");
        if (request.Base is not null && string.IsNullOrWhiteSpace(request.Base) || request.Head is not null && string.IsNullOrWhiteSpace(request.Head)) throw Usage("usage.changed_reference", "Changed-scope references cannot be blank.", "Provide non-blank --base and --head references.");
        if (request.Paths.Any(string.IsNullOrWhiteSpace)) throw Usage("usage.text_path", "A --path value cannot be blank.", "Provide one or more non-blank paths.");
    }

    private static IReadOnlyList<string> ResolveDeclaredScopes(WorkspaceDiscoveryResult workspace, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return [];
        try
        {
            var resolver = new WorkspacePathResolver(workspace.RootPath, workspace.CurrentDirectory);
            return paths.Select(path => resolver.ResolveInput(path, WorkspacePathScope.Explicit).Path).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (ArgumentException exception)
        {
            throw Usage("usage.text_path", exception.Message, "Provide valid explicit workspace paths.");
        }
    }

    private static ChangedCoverage BuildChangedCoverage(ChangedScopeResult changed, TextSearchResult result,
        IReadOnlyList<string> declaredScopes, string? projectDirectory, IReadOnlySet<string> allSelectable, IReadOnlySet<string> currentSelectable)
    {
        var seen = result.Observations.ToDictionary(item => item.Path, item => item.Status, StringComparer.Ordinal);
        var conflicts = changed.ExcludedConflictedPaths.ToHashSet(StringComparer.Ordinal);
        var items = new List<ChangedPathObservation>();
        foreach (var path in changed.ChangedPaths.Concat(changed.ExcludedConflictedPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                     .Where(path => MatchesScopes(path, declaredScopes) && MatchesProject(path, projectDirectory)))
        {
            if (conflicts.Contains(path)) items.Add(new(path, "excluded", "conflicted"));
            else if (!allSelectable.Contains(path)) items.Add(new(path, "excluded", "excluded_by_traversal_policy_or_missing"));
            else if (!currentSelectable.Contains(path)) items.Add(new(path, "excluded", "generated"));
            else if (!seen.TryGetValue(path, out var status)) items.Add(new(path, "remaining", "collection_stopped_before_path"));
            else items.Add(status switch
            {
                TextSearchFileStatus.Analyzed => new(path, "analyzed", "searched"),
                TextSearchFileStatus.LimitReached => new(path, "remaining", "collection_stopped_at_limit"),
                TextSearchFileStatus.Unreadable => new(path, "failed", "unreadable"),
                TextSearchFileStatus.RegularExpressionTimeout => new(path, "failed", "regular_expression_timeout"),
                TextSearchFileStatus.Binary => new(path, "excluded", "binary"),
                TextSearchFileStatus.Undecodable => new(path, "excluded", "undecodable"),
                TextSearchFileStatus.UnsupportedEncoding => new(path, "excluded", "unsupported_encoding"),
                _ => throw new InvalidOperationException("Unknown text-search observation."),
            });
        }

        var analyzed = items.Count(item => item.Disposition == "analyzed");
        var excluded = items.Count(item => item.Disposition == "excluded");
        var failed = items.Count(item => item.Disposition == "failed");
        var remaining = items.Count(item => item.Disposition == "remaining");
        var level = failed == 0 && remaining == 0 ? CoverageLevel.Complete : CoverageLevel.Partial;
        return new(new EvidenceCoverage(level, items.Count, analyzed, remaining, excluded, failed,
            level is CoverageLevel.Partial ? "One or more selected changed paths were not fully searched." : null), items);
    }

    private static bool MatchesScopes(string path, IReadOnlyList<string> scopes) => scopes.Count == 0 || scopes.Any(scope => scope == "." || path == scope || path.StartsWith(scope.TrimEnd('/') + "/", StringComparison.Ordinal));
    private static bool MatchesProject(string path, string? directory) => string.IsNullOrEmpty(directory) || path.StartsWith(directory.TrimEnd('/') + "/", StringComparison.Ordinal);
    private static CommandUsageException Usage(string code, string message, string correction) => new(code, message, correction);

    private static ResultError ToResultError(TextSearchError error) =>
        error.Kind switch
        {
            TextSearchErrorKind.InvalidRegularExpression => new ResultError(
                "search.regex_invalid",
                $"The regular-expression query `{error.Query}` is invalid.",
                "Correct the query so it uses valid .NET regular-expression syntax."),
            TextSearchErrorKind.RegularExpressionTimeout => new ResultError(
                "search.regex_timeout",
                $"The regular-expression query `{error.Query}` timed out while searching `{error.Path}`.",
                "Narrow the expression or file scope and run the search again."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(error),
                error.Kind,
                "The text-search error kind is not defined."),
        };

    private static string RetrievalCommand(TextSearchCommandRequest request)
    {
        var arguments = new List<string> { "dnaxi", "search", "text", Quote(request.Query) };
        if (request.CaseSensitive) arguments.Add("--case-sensitive");
        if (request.IncludeGenerated) arguments.Add("--include-generated");
        if (request.Regex) arguments.Add("--regex");
        foreach (var path in request.Paths) { arguments.Add("--path"); arguments.Add(Quote(path)); }
        if (request.Project is not null) { arguments.Add("--project"); arguments.Add(Quote(request.Project)); }
        if (request.Changed) arguments.Add("--changed");
        if (request.Base is not null) { arguments.Add("--base"); arguments.Add(Quote(request.Base)); }
        if (request.Head is not null) { arguments.Add("--head"); arguments.Add(Quote(request.Head)); }
        if (request.Fields.Count > 0) { arguments.Add("--fields"); arguments.AddRange(request.Fields.Select(Quote)); }
        arguments.Add("--full");
        return CanonicalInvocation.OneShot(string.Join(' ', arguments));
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed class ScopedTraverser(IWorkspacePathTraverser inner, string? projectDirectory, ISet<string>? changed) : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(WorkspaceTraversalRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = new List<WorkspaceTraversalPath>();
            foreach (var path in inner.Traverse(request, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesProject(path.RelativePath, projectDirectory) || changed is not null && !changed.Contains(path.RelativePath)) continue;
                paths.Add(path);
            }
            return Array.AsReadOnly(paths.ToArray());
        }
    }

    private static readonly OutputFieldSet<TextSearchMatch> TextSearchFields = new([
        new("id", static match => match.Id, true), new("file", static match => match.Location.Path, true),
        new("line", static match => match.Location.Line, true), new("preview", static match => match.Preview, true),
        new("column", static match => match.Location.Column), new("external", static match => match.Location.IsExternal)]);

    private sealed record ChangedCoverage(EvidenceCoverage Coverage, IReadOnlyList<ChangedPathObservation> Observations);
    private sealed record ChangedPathObservation(string Path, string Disposition, string Reason);
    private sealed record TextSearchPayload(BoundedCollection<IReadOnlyDictionary<string, object?>> Matches, TextSearchSkipPayload Skipped, ChangedScopeResult? Changed, IReadOnlyList<ChangedPathObservation> ChangedCoverage)
    {
        public static TextSearchPayload From(TextSearchResult result, BoundedCollection<IReadOnlyDictionary<string, object?>> matches,
            ChangedScopeResult? changed, IReadOnlyList<ChangedPathObservation> coverage, bool includeDetails, string retrievalCommand) => new(matches,
                TextSearchSkipPayload.From(result, includeDetails, retrievalCommand), changed, coverage);
    }
    private sealed record TextSearchSkipPayload(int Binary, int Undecodable, int UnsupportedEncoding, int Unreadable, bool TotalsKnown, BoundedCollection<TextSearchSkippedFile>? Details)
    {
        public static TextSearchSkipPayload From(TextSearchResult result, bool details, string command) => new(result.SkippedBinary, result.SkippedUndecodable,
            result.SkippedUnsupportedEncoding, result.SkippedUnreadable,
            result.SkipTotalsKnown, details ? BoundedCollection<TextSearchSkippedFile>.FromObserved(result.SkippedFiles, result.SkippedFileTotal, result.SkipTotalsKnown, command) : null);
    }
}
