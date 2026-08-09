using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record CatchSyntaxCommandRequest(
    string? Type,
    bool Empty,
    bool IncludeGenerated,
    int Limit,
    bool LimitSpecified,
    bool Full,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> Paths)
{
    public static readonly string[] AvailableFields =
    [
        "id",
        "file",
        "line",
        "construct",
        "column",
        "end_line",
        "end_column",
        "external",
    ];

    public static CatchSyntaxCommandRequest Create(
        string? type,
        bool empty,
        bool includeGenerated,
        int limit,
        bool limitSpecified,
        bool full,
        IReadOnlyList<string> fields,
        IReadOnlyList<string> paths)
    {
        if (fields.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.syntax_field",
                "A --fields value cannot be blank.",
                $"Use `--fields` with one or more of: {string.Join(", ", AvailableFields)}.");
        }

        var unknown = fields
            .Where(field => !AvailableFields.Contains(field, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new UnknownOutputFieldsException(unknown, AvailableFields);
        }

        return new CatchSyntaxCommandRequest(
            type,
            empty,
            includeGenerated,
            limit,
            limitSpecified,
            full,
            fields,
            paths);
    }

    private static CommandUsageException Usage(
        string code,
        string message,
        string correction) =>
        new(code, message, correction);
}

internal sealed class CatchSyntaxCommandHandler :
    ICommandHandler<CatchSyntaxCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        CatchSyntaxCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var fields = CatchFields.Select(request.Fields);

        cancellationToken.ThrowIfCancellationRequested();
        var workspace = new WorkspaceDiscoverer()
            .Discover(Directory.GetCurrentDirectory());
        var traversal = new WorkspaceTraversalRequest(
            workspace.RootPath,
            explicitPaths: request.Paths,
            includeGenerated: request.IncludeGenerated,
            currentDirectory: workspace.CurrentDirectory);
        var result = await new RoslynSyntaxEngine(new WorkspacePathTraverser())
            .QueryAsync(
                new RoslynSyntaxQueryRequest(traversal),
                new CatchClauseSyntaxQuery(request.Type, request.Empty),
                cancellationToken)
            .ConfigureAwait(false);

        var retrievalCommand = RetrievalCommand(request);
        var includedCandidates = request.Full
            ? result.Candidates
            : result.Candidates.Take(request.Limit);
        var bounded = BoundedCollection<IReadOnlyDictionary<string, object?>>.FromObserved(
            ProjectCandidates(includedCandidates, fields, cancellationToken),
            total: result.Candidates.Count,
            totalKnown: true,
            retrievalCommand);
        var payload = new CatchSyntaxPayload(
            bounded.Count,
            bounded.TotalKnown,
            bounded.Total,
            bounded.Omitted,
            bounded.Truncated,
            bounded.RetrievalCommand,
            bounded.Items);
        var evidence = new Evidence(
            result.Snapshot,
            EvidenceResolution.Syntax,
            new EvidenceCoverage(
                CoverageLevel.Complete,
                considered: result.Observations.Count,
                analyzed: result.Observations.Count,
                remaining: 0,
                excluded: 0,
                failed: 0),
            EvidenceConfidence.Candidate,
            new EvidenceScope(
                workspace.RootPath,
                request.Paths.Count == 0
                    ? "eligible C# workspace paths"
                    : "eligible explicitly selected C# paths"));

        return CommandResult<CatchSyntaxPayload>.Success(
            "search syntax catch",
            payload,
            evidence);
    }

    private static void ValidateRequest(CatchSyntaxCommandRequest request)
    {
        if (request.Type is not null && string.IsNullOrWhiteSpace(request.Type))
        {
            throw Usage(
                "usage.syntax_catch_type",
                "The catch type cannot be blank.",
                "Provide a non-blank terminal exception type name with `--type`.");
        }

        if (request.Type?.Contains('\0', StringComparison.Ordinal) == true)
        {
            throw Usage(
                "usage.syntax_catch_type",
                "The catch type contains an invalid null character.",
                "Provide a C# exception type name without null characters.");
        }

        if (request.Limit < 0 || (request.Full && request.LimitSpecified))
        {
            throw Usage(
                "usage.syntax_limit",
                "The --limit value is invalid for this catch search.",
                "Use a non-negative --limit, or use --full without --limit.");
        }

        if (request.Paths.Any(string.IsNullOrWhiteSpace))
        {
            throw Usage(
                "usage.syntax_path",
                "A --path value cannot be blank.",
                "Provide one or more non-blank paths.");
        }
    }

    private static string RetrievalCommand(CatchSyntaxCommandRequest request)
    {
        var arguments = new List<string>
        {
            "dnaxi",
            "search",
            "syntax",
            "catch",
        };
        if (request.Type is not null)
        {
            arguments.Add("--type");
            arguments.Add(Quote(request.Type));
        }

        if (request.Empty)
        {
            arguments.Add("--empty");
        }

        if (request.IncludeGenerated)
        {
            arguments.Add("--include-generated");
        }

        foreach (var path in request.Paths)
        {
            arguments.Add("--path");
            arguments.Add(Quote(path));
        }

        if (request.Fields.Count > 0)
        {
            arguments.Add("--fields");
            arguments.AddRange(request.Fields.Select(Quote));
        }

        arguments.Add("--full");
        return CanonicalInvocation.OneShot(string.Join(' ', arguments));
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ProjectCandidates(
        IEnumerable<StructuralCandidate> candidates,
        OutputFieldSelection<StructuralCandidate> fields,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return fields.Project(candidate);
        }
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static readonly OutputFieldSet<StructuralCandidate> CatchFields =
        new(
        [
            new("id", static candidate => candidate.Id),
            new("file", static candidate => candidate.Range.Start.Path, includedByDefault: true),
            new("line", static candidate => candidate.Range.Start.Line, includedByDefault: true),
            new("construct", static _ => "catch"),
            new("column", static candidate => candidate.Range.Start.Column),
            new("end_line", static candidate => candidate.Range.End.Line),
            new("end_column", static candidate => candidate.Range.End.Column),
            new("external", static candidate => candidate.Range.Start.IsExternal),
        ]);

    private sealed record CatchSyntaxPayload(
        int Count,
        bool TotalKnown,
        int? Total,
        int? Omitted,
        bool Truncated,
        string? RetrievalCommand,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Matches);

    private static CommandUsageException Usage(
        string code,
        string message,
        string correction) =>
        new(code, message, correction);
}
