using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetAxi.Structural;

/// <summary>
/// Parses only the C# files selected by the shared workspace traverser. It
/// never creates a compilation, loads a project, or executes repository code.
/// </summary>
public sealed class RoslynSyntaxEngine
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IWorkspacePathTraverser _traverser;

    public RoslynSyntaxEngine(IWorkspacePathTraverser traverser)
    {
        _traverser = traverser ?? throw new ArgumentNullException(nameof(traverser));
    }

    public async ValueTask<RoslynSyntaxQueryResult> QueryAsync(
        RoslynSyntaxQueryRequest request,
        IRoslynSyntaxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var queryKind = RequiredQueryKind(query.Kind);
        var queryIdentity = RequiredQueryIdentity(query.Identity);
        var selectedPaths = _traverser
            .Traverse(request.Traversal, cancellationToken)
            .Where(static path => Path.GetExtension(path.RelativePath).Equals(
                ".cs",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path.RelativePath, StringComparer.Ordinal)
            .ThenBy(static path => path.IsExternal)
            .ThenBy(static path => path.FullPath, StringComparer.Ordinal)
            .GroupBy(
                static path => (path.RelativePath, path.IsExternal),
                SyntaxPathIdentityComparer.Instance)
            .Select(static group => group.First())
            .ToArray();

        using var snapshot = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StructuralCandidateIdentity.Append(
            snapshot,
            "dotnet-axi/roslyn-syntax-snapshot/v1");
        StructuralCandidateIdentity.Append(snapshot, queryKind);
        StructuralCandidateIdentity.Append(snapshot, queryIdentity);

        var candidates = new List<StructuralCandidate>();
        var observations = new List<RoslynSyntaxFileObservation>(selectedPaths.Length);

        foreach (var path in selectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(path.FullPath))
            {
                throw new InvalidOperationException(
                    "Workspace traversal must return fully qualified source paths.");
            }

            var bytes = await File.ReadAllBytesAsync(path.FullPath, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            cancellationToken.ThrowIfCancellationRequested();

            StructuralCandidateIdentity.Append(snapshot, path.RelativePath);
            StructuralCandidateIdentity.Append(
                snapshot,
                path.IsExternal ? "external" : "workspace");
            StructuralCandidateIdentity.Append(snapshot, contentHash);

            var source = SourceText.From(
                bytes,
                bytes.Length,
                DefaultEncoding,
                SourceHashAlgorithm.Sha256,
                throwIfBinaryDetected: true,
                canBeEmbedded: false);
            var tree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default,
                path.RelativePath,
                cancellationToken);
            var root = (CompilationUnitSyntax)await tree
                .GetRootAsync(cancellationToken)
                .ConfigureAwait(false);
            var diagnosticCount = tree.GetDiagnostics(cancellationToken).Count();
            observations.Add(new RoslynSyntaxFileObservation(
                path.RelativePath,
                path.IsExternal,
                diagnosticCount));

            var discovered = query.FindCandidates(root, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Syntax query '{queryKind}' returned a null candidate sequence.");
            foreach (var node in discovered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is null || !ReferenceEquals(node.SyntaxTree, tree))
                {
                    throw new InvalidOperationException(
                        $"Syntax query '{queryKind}' returned a node outside the selected syntax tree.");
                }

                var lineSpan = tree.GetLineSpan(node.Span, cancellationToken).Span;
                var range = new StructuralSourceRange(
                    SourceLocation.FromZeroBasedUtf16(
                        path.RelativePath,
                        lineSpan.Start.Line,
                        lineSpan.Start.Character,
                        path.IsExternal),
                    SourceLocation.FromZeroBasedUtf16(
                        path.RelativePath,
                        lineSpan.End.Line,
                        lineSpan.End.Character,
                        path.IsExternal));
                var text = node.ToString();
                candidates.Add(new StructuralCandidate(
                    StructuralCandidateIdentity.Create(
                        queryKind,
                        queryIdentity,
                        contentHash,
                        path.RelativePath,
                        path.IsExternal,
                        node.SpanStart,
                        node.Span.Length,
                        text),
                    queryKind,
                    queryIdentity,
                    range,
                    text));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalized = candidates
            .OrderBy(static candidate => candidate.Range.Start.Path, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Range.Start.IsExternal)
            .ThenBy(static candidate => candidate.Range.Start.Line)
            .ThenBy(static candidate => candidate.Range.Start.Column)
            .ThenBy(static candidate => candidate.Range.End.Line)
            .ThenBy(static candidate => candidate.Range.End.Column)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .DistinctBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();

        return new RoslynSyntaxQueryResult(
            normalized,
            observations,
            queryIdentity,
            "ws_" + Convert.ToHexStringLower(snapshot.GetHashAndReset()));
    }

    private static string RequiredQueryKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A syntax query kind cannot contain a null character.",
                nameof(value));
        }

        return value;
    }

    private static string RequiredQueryIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A syntax query identity cannot contain a null character.",
                nameof(value));
        }

        return value;
    }

    private sealed class SyntaxPathIdentityComparer
        : IEqualityComparer<(string RelativePath, bool IsExternal)>
    {
        public static SyntaxPathIdentityComparer Instance { get; } = new();

        public bool Equals(
            (string RelativePath, bool IsExternal) x,
            (string RelativePath, bool IsExternal) y) =>
            x.IsExternal == y.IsExternal
            && StringComparer.Ordinal.Equals(x.RelativePath, y.RelativePath);

        public int GetHashCode((string RelativePath, bool IsExternal) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.RelativePath),
                value.IsExternal);
    }
}
