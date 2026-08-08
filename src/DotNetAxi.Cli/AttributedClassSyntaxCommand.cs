using DotNetAxi.Contracts;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record AttributedClassSyntaxCommandRequest(
    string Attribute,
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

    public static AttributedClassSyntaxCommandRequest Create(
        string attribute,
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

        return new AttributedClassSyntaxCommandRequest(
            attribute,
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

internal sealed class AttributedClassSyntaxCommandHandler :
    ICommandHandler<AttributedClassSyntaxCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        AttributedClassSyntaxCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var fields = ClassFields.Select(request.Fields);

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
                new AttributedClassSyntaxQuery(request.Attribute),
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
        var payload = new AttributedClassSyntaxPayload(
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

        return CommandResult<AttributedClassSyntaxPayload>.Success(
            "search syntax class",
            payload,
            evidence);
    }

    private static void ValidateRequest(AttributedClassSyntaxCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Attribute))
        {
            throw Usage(
                "usage.syntax_attribute_name_required",
                "The attribute name cannot be blank.",
                "Provide a non-blank terminal attribute name with `--attribute`.");
        }

        if (request.Attribute.Contains('\0', StringComparison.Ordinal))
        {
            throw Usage(
                "usage.syntax_attribute_name",
                "The attribute name contains an invalid null character.",
                "Provide a C# attribute identifier without null characters.");
        }

        if (request.Limit < 0 || (request.Full && request.LimitSpecified))
        {
            throw Usage(
                "usage.syntax_limit",
                "The --limit value is invalid for this class search.",
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

    private static string RetrievalCommand(AttributedClassSyntaxCommandRequest request)
    {
        var arguments = new List<string>
        {
            "dnaxi",
            "search",
            "syntax",
            "class",
            "--attribute",
            Quote(request.Attribute),
        };
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

    private static readonly OutputFieldSet<StructuralCandidate> ClassFields =
        new(
        [
            new("id", static candidate => candidate.Id, includedByDefault: true),
            new("file", static candidate => candidate.Range.Start.Path, includedByDefault: true),
            new("line", static candidate => candidate.Range.Start.Line, includedByDefault: true),
            new("construct", static _ => "class", includedByDefault: true),
            new("column", static candidate => candidate.Range.Start.Column),
            new("end_line", static candidate => candidate.Range.End.Line),
            new("end_column", static candidate => candidate.Range.End.Column),
            new("external", static candidate => candidate.Range.Start.IsExternal),
        ]);

    private sealed record AttributedClassSyntaxPayload(
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
