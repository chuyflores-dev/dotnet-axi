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
