using DotNetAxi.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural;

/// <summary>
/// A stable, tool-owned query evaluated against one selected C# syntax tree.
/// Query implementations discover nodes; the engine owns file traversal,
/// source locations, ordering, and candidate identity.
/// </summary>
public interface IRoslynSyntaxQuery
{
    string Kind { get; }

    /// <summary>
    /// A versioned, canonical identity containing <see cref="Kind"/> and every
    /// normalized query parameter that can affect the candidate set.
    /// </summary>
    string Identity { get; }

    IEnumerable<SyntaxNode> FindCandidates(
        CompilationUnitSyntax root,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Marks a stable tool-owned syntax query as having exactly one declared
/// compiler interpretation. Queries without this contract cannot request
/// semantic verification.
/// </summary>
public interface ISemanticallyVerifiableSyntaxQuery : IRoslynSyntaxQuery
{
    SemanticSyntaxVerifier Verifier { get; }
}

public enum SemanticSyntaxVerifierKind
{
    Invocation,
    AttributedClass,
    ObjectCreation,
    CatchClause,
}

public sealed record SemanticSyntaxVerifier
{
    public SemanticSyntaxVerifier(
        SemanticSyntaxVerifierKind kind,
        string? requestedName)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind is not SemanticSyntaxVerifierKind.CatchClause
            && string.IsNullOrWhiteSpace(requestedName))
        {
            throw new ArgumentException(
                "This semantic verifier requires a requested name.",
                nameof(requestedName));
        }

        if (requestedName?.Contains('\0', StringComparison.Ordinal) == true)
        {
            throw new ArgumentException(
                "A semantic verifier name cannot contain a null character.",
                nameof(requestedName));
        }

        Kind = kind;
        RequestedName = requestedName;
    }

    public SemanticSyntaxVerifierKind Kind { get; }

    public string? RequestedName { get; }
}

public sealed record RoslynSyntaxQueryRequest
{
    public RoslynSyntaxQueryRequest(WorkspaceTraversalRequest traversal)
    {
        Traversal = traversal ?? throw new ArgumentNullException(nameof(traversal));
    }

    public WorkspaceTraversalRequest Traversal { get; }
}

public sealed record StructuralSourceRange
{
    public StructuralSourceRange(SourceLocation start, SourceLocation end)
    {
        Start = start ?? throw new ArgumentNullException(nameof(start));
        End = end ?? throw new ArgumentNullException(nameof(end));
        if (!Start.Path.Equals(End.Path, StringComparison.Ordinal)
            || Start.IsExternal != End.IsExternal
            || End.Line < Start.Line
            || (End.Line == Start.Line && End.Column < Start.Column))
        {
            throw new ArgumentException(
                "A structural source range must be ordered within one source path.",
                nameof(end));
        }
    }

    public SourceLocation Start { get; }

    /// <summary>The exclusive end of the candidate in one-based UTF-16 coordinates.</summary>
    public SourceLocation End { get; }
}

public sealed record StructuralCandidate
{
    public StructuralCandidate(
        string id,
        string queryKind,
        string queryIdentity,
        StructuralSourceRange range,
        string text)
    {
        Id = RequiredText(id, nameof(id));
        QueryKind = RequiredText(queryKind, nameof(queryKind));
        QueryIdentity = RequiredText(queryIdentity, nameof(queryIdentity));
        Range = range ?? throw new ArgumentNullException(nameof(range));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string Id { get; }

    public string QueryKind { get; }

    public string QueryIdentity { get; }

    public StructuralSourceRange Range { get; }

    public string Text { get; }

    public bool MatchesIdentity(
        string contentHash,
        int spanStart,
        int spanLength,
        string text) =>
        Id.Equals(
            StructuralCandidateIdentity.Create(
                QueryKind,
                QueryIdentity,
                RequiredText(contentHash, nameof(contentHash)),
                Range.Start.Path,
                Range.Start.IsExternal,
                spanStart,
                spanLength,
                text ?? throw new ArgumentNullException(nameof(text))),
            StringComparison.Ordinal);

    private static string RequiredText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public sealed record RoslynSyntaxFileObservation
{
    public RoslynSyntaxFileObservation(
        string path,
        bool isExternal,
        int diagnosticCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(diagnosticCount);
        Path = path;
        IsExternal = isExternal;
        DiagnosticCount = diagnosticCount;
    }

    public string Path { get; }

    public bool IsExternal { get; }

    /// <summary>
    /// The number of Roslyn parse diagnostics. Diagnostics do not prevent
    /// syntax-only candidates from being returned.
    /// </summary>
    public int DiagnosticCount { get; }
}

public sealed record RoslynSyntaxQueryResult
{
    public RoslynSyntaxQueryResult(
        IEnumerable<StructuralCandidate> candidates,
        IEnumerable<RoslynSyntaxFileObservation> observations,
        string queryIdentity,
        string snapshot)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(observations);
        Candidates = Array.AsReadOnly(candidates.ToArray());
        Observations = Array.AsReadOnly(observations.ToArray());
        QueryIdentity = RequiredText(queryIdentity, nameof(queryIdentity));
        Snapshot = RequiredText(snapshot, nameof(snapshot));
    }

    public IReadOnlyList<StructuralCandidate> Candidates { get; }

    public IReadOnlyList<RoslynSyntaxFileObservation> Observations { get; }

    public string QueryIdentity { get; }

    public string Snapshot { get; }

    private static string RequiredText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
