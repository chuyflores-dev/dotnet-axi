using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetAxi.Structural;

public sealed record SymbolDeclarationSearchRequest
{
    public SymbolDeclarationSearchRequest(
        string query,
        WorkspaceTraversalRequest traversal,
        IEnumerable<string>? kinds = null,
        string? namespaceFilter = null,
        string? project = null,
        IEnumerable<string>? accessibilities = null,
        bool includeTests = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A symbol query cannot contain a null character.", nameof(query));
        }

        Query = query;
        Traversal = traversal ?? throw new ArgumentNullException(nameof(traversal));
        Kinds = CopyFilters(kinds, SymbolDeclarationSearcher.AvailableKinds, nameof(kinds));
        Namespace = OptionalText(namespaceFilter, nameof(namespaceFilter));
        Project = OptionalText(project, nameof(project));
        Accessibilities = CopyFilters(
            accessibilities,
            SymbolDeclarationSearcher.AvailableAccessibilities,
            nameof(accessibilities));
        IncludeTests = includeTests;
    }

    public string Query { get; }

    public WorkspaceTraversalRequest Traversal { get; }

    public IReadOnlyList<string> Kinds { get; }

    public string? Namespace { get; }

    public string? Project { get; }

    public IReadOnlyList<string> Accessibilities { get; }

    public bool IncludeTests { get; }

    private static IReadOnlyList<string> CopyFilters(
        IEnumerable<string>? values,
        IReadOnlyList<string> allowed,
        string parameterName)
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (copy.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Symbol filters cannot be blank.", parameterName);
        }

        var unknown = copy
            .Where(value => !allowed.Contains(value, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException(
                $"Unsupported symbol filter: {string.Join(", ", unknown)}.",
                parameterName);
        }

        return Array.AsReadOnly(copy.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string? OptionalText(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A symbol filter cannot contain a null character.", parameterName);
        }

        return value;
    }
}

public sealed record SymbolDeclarationMatch(
    string Id,
    string Kind,
    string Name,
    string FullyQualifiedName,
    string Namespace,
    string Accessibility,
    string Signature,
    StructuralSourceRange Range,
    IReadOnlyList<string> OwningProjects,
    IReadOnlyList<SymbolDeclarationVariant> Variants,
    bool IsTest,
    bool IsGenerated,
    int Rank)
{
    public int OwningProjectCount => OwningProjects.Count;

    public int VariantCount => Variants.Count;
}

public sealed record SymbolDeclarationVariant(
    string Project,
    string? Configuration,
    string? Framework,
    string Meaning);

public sealed record SymbolDeclarationSearchResult(
    IReadOnlyList<SymbolDeclarationMatch> Matches,
    IReadOnlyList<RoslynSyntaxFileObservation> Observations,
    string Snapshot);

/// <summary>
/// Discovers C# declarations from syntax trees without creating a compilation
/// or evaluating projects. Project paths are passive ownership candidates.
/// </summary>
public sealed class SymbolDeclarationSearcher
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<string> AvailableKinds { get; } = Array.AsReadOnly(
    [
        "namespace",
        "class",
        "struct",
        "interface",
        "record",
        "enum",
        "delegate",
        "method",
        "constructor",
        "destructor",
        "property",
        "indexer",
        "event",
        "field",
        "enum-member",
        "operator",
        "conversion-operator",
    ]);

    public static IReadOnlyList<string> AvailableAccessibilities { get; } = Array.AsReadOnly(
    [
        "public",
        "internal",
        "protected",
        "private",
        "protected-internal",
        "private-protected",
        "file",
        "not-applicable",
    ]);

    private readonly IWorkspacePathTraverser _traverser;
    private readonly IFileOwnershipResolver _ownership;

    public SymbolDeclarationSearcher(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership)
    {
        _traverser = traverser ?? throw new ArgumentNullException(nameof(traverser));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
    }

    public async ValueTask<SymbolDeclarationSearchResult> SearchAsync(
        SymbolDeclarationSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var paths = _traverser
            .Traverse(request.Traversal, cancellationToken)
            .Where(static path => Path.GetExtension(path.RelativePath).Equals(
                ".cs",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path.RelativePath, StringComparer.Ordinal)
            .ThenBy(static path => path.IsExternal)
            .ThenBy(static path => path.FullPath, StringComparer.Ordinal)
            .DistinctBy(static path => (path.RelativePath, path.IsExternal))
            .ToArray();

        using var snapshot = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StructuralCandidateIdentity.Append(snapshot, "dotnet-axi/symbol-search-snapshot/v1");
        StructuralCandidateIdentity.Append(snapshot, request.Query);
        AppendMany(snapshot, request.Kinds);
        StructuralCandidateIdentity.Append(snapshot, request.Namespace ?? string.Empty);
        StructuralCandidateIdentity.Append(snapshot, request.Project ?? string.Empty);
        AppendMany(snapshot, request.Accessibilities);
        StructuralCandidateIdentity.Append(snapshot, request.IncludeTests ? "tests" : "no-tests");

        var observations = new List<RoslynSyntaxFileObservation>(paths.Length);
        var matches = new List<SymbolDeclarationMatch>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(path.FullPath, cancellationToken)
                .ConfigureAwait(false);
            var contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var owners = Array.AsReadOnly(_ownership
                .GetOwningProjects(path)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
            var compilerVariants = _ownership
                .GetCompilerVariants(path)
                .DistinctBy(static variant => (
                    variant.Project,
                    variant.Configuration,
                    variant.Framework,
                    variant.ContextFingerprint))
                .OrderBy(static variant => variant.Project, StringComparer.Ordinal)
                .ThenBy(static variant => variant.Configuration, StringComparer.Ordinal)
                .ThenBy(static variant => variant.Framework, StringComparer.Ordinal)
                .ThenBy(
                    static variant => variant.ContextFingerprint,
                    StringComparer.Ordinal)
                .ToArray();
            if (compilerVariants.Any(variant =>
                    !owners.Contains(variant.Project, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Compiler variants must belong to an owning project.");
            }

            var isTest = IsTestPath(path.RelativePath, owners);
            var isGenerated = path.IsGenerated;

            StructuralCandidateIdentity.Append(snapshot, path.RelativePath);
            StructuralCandidateIdentity.Append(snapshot, path.IsExternal ? "external" : "workspace");
            StructuralCandidateIdentity.Append(snapshot, isGenerated ? "generated" : "source");
            StructuralCandidateIdentity.Append(snapshot, contentHash);
            AppendMany(snapshot, owners);
            StructuralCandidateIdentity.Append(
                snapshot,
                compilerVariants.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            foreach (var variant in compilerVariants)
            {
                StructuralCandidateIdentity.Append(snapshot, variant.Project);
                StructuralCandidateIdentity.Append(
                    snapshot,
                    variant.Configuration ?? string.Empty);
                StructuralCandidateIdentity.Append(
                    snapshot,
                    variant.Framework ?? string.Empty);
                StructuralCandidateIdentity.Append(
                    snapshot,
                    variant.ContextFingerprint);
            }

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
            observations.Add(new RoslynSyntaxFileObservation(
                path.RelativePath,
                path.IsExternal,
                tree.GetDiagnostics(cancellationToken).Count()));

            foreach (var declaration in Declarations(root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rank = Rank(request.Query, declaration.Name, declaration.FullyQualifiedName);
                if (rank is null
                    || (request.Kinds.Count > 0
                        && !request.Kinds.Contains(declaration.Kind, StringComparer.Ordinal))
                    || (request.Namespace is not null
                        && !InNamespace(declaration.Namespace, request.Namespace))
                    || (request.Project is not null
                        && !owners.Contains(request.Project, StringComparer.Ordinal))
                    || (request.Accessibilities.Count > 0
                        && !request.Accessibilities.Contains(
                            declaration.Accessibility,
                            StringComparer.Ordinal))
                    || (!request.IncludeTests && isTest))
                {
                    continue;
                }

                var lineSpan = tree.GetLineSpan(declaration.Node.Span, cancellationToken).Span;
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
                var id = SymbolEntityIdentity.Create(
                        declaration.Name,
                        declaration.Kind,
                        declaration.FullyQualifiedName,
                        declaration.Signature,
                        contentHash,
                        declaration.Node.SpanStart,
                        declaration.Node.Span.Length,
                        path.RelativePath,
                        path.IsExternal,
                        compilerVariants);
                var variants = Array.AsReadOnly(compilerVariants
                    .Select(variant => new SymbolDeclarationVariant(
                        variant.Project,
                        variant.Configuration,
                        variant.Framework,
                        "unresolved"))
                    .ToArray());
                matches.Add(new SymbolDeclarationMatch(
                    id,
                    declaration.Kind,
                    declaration.Name,
                    declaration.FullyQualifiedName,
                    declaration.Namespace,
                    declaration.Accessibility,
                    declaration.Signature,
                    range,
                    owners,
                    variants,
                    isTest,
                    isGenerated,
                    rank.Value));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = matches
            .OrderBy(static match => match.Rank)
            .ThenBy(static match => match.Range.Start.Path, StringComparer.Ordinal)
            .ThenBy(static match => match.Range.Start.IsExternal)
            .ThenBy(static match => match.Range.Start.Line)
            .ThenBy(static match => match.Range.Start.Column)
            .ThenBy(static match => match.Kind, StringComparer.Ordinal)
            .ThenBy(static match => match.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static match => match.Signature, StringComparer.Ordinal)
            .ThenBy(static match => match.Id, StringComparer.Ordinal)
            .ToArray();

        return new SymbolDeclarationSearchResult(
            Array.AsReadOnly(ordered),
            Array.AsReadOnly(observations.ToArray()),
            "ws_" + Convert.ToHexStringLower(snapshot.GetHashAndReset()));
    }

    private static IEnumerable<Declaration> Declarations(
        CompilationUnitSyntax root,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case BaseNamespaceDeclarationSyntax value:
                    var namespaceName = NamespaceOf(value);
                    yield return new Declaration(
                        value,
                        "namespace",
                        value.Name.ToString(),
                        namespaceName,
                        namespaceName,
                        "not-applicable",
                        value.Name.ToString());
                    break;
                case ClassDeclarationSyntax value:
                    yield return Create(value, "class", value.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    break;
                case StructDeclarationSyntax value:
                    yield return Create(value, "struct", value.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    break;
                case InterfaceDeclarationSyntax value:
                    yield return Create(value, "interface", value.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    break;
                case RecordDeclarationSyntax value:
                    yield return Create(value, "record", value.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    break;
                case EnumDeclarationSyntax value:
                    yield return Create(value, "enum", value.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    break;
                case DelegateDeclarationSyntax value:
                    yield return Create(value, "delegate", value.Identifier.ValueText, Accessibility(value.Modifiers, value), Parameters(value.ParameterList));
                    break;
                case MethodDeclarationSyntax value:
                    yield return Create(value, "method", value.Identifier.ValueText, Accessibility(value.Modifiers, value), Parameters(value.ParameterList));
                    break;
                case ConstructorDeclarationSyntax value:
                    yield return Create(value, "constructor", value.Identifier.ValueText, Accessibility(value.Modifiers, value), Parameters(value.ParameterList));
                    break;
                case DestructorDeclarationSyntax value:
                    yield return Create(value, "destructor", "~" + value.Identifier.ValueText, "private", Parameters(value.ParameterList));
                    break;
                case PropertyDeclarationSyntax value:
                    yield return Create(value, "property", value.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    break;
                case IndexerDeclarationSyntax value:
                    yield return Create(value, "indexer", "this", Accessibility(value.Modifiers, value), BracketParameters(value.ParameterList));
                    break;
                case EventDeclarationSyntax value:
                    yield return Create(value, "event", value.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    break;
                case EventFieldDeclarationSyntax value:
                    foreach (var variable in value.Declaration.Variables)
                    {
                        yield return Create(variable, "event", variable.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    }
                    break;
                case FieldDeclarationSyntax value:
                    foreach (var variable in value.Declaration.Variables)
                    {
                        yield return Create(variable, "field", variable.Identifier.ValueText, Accessibility(value.Modifiers, value));
                    }
                    break;
                case EnumMemberDeclarationSyntax value:
                    yield return Create(value, "enum-member", value.Identifier.ValueText, "public");
                    break;
                case OperatorDeclarationSyntax value:
                    yield return Create(value, "operator", "operator" + value.OperatorToken.ValueText, Accessibility(value.Modifiers, value), Parameters(value.ParameterList));
                    break;
                case ConversionOperatorDeclarationSyntax value:
                    var conversionName = value.ImplicitOrExplicitKeyword.ValueText
                        + " operator " + value.Type.WithoutTrivia();
                    yield return Create(value, "conversion-operator", conversionName, Accessibility(value.Modifiers, value), Parameters(value.ParameterList));
                    break;
            }
        }
    }

    private static Declaration Create(
        SyntaxNode node,
        string kind,
        string name,
        string accessibility,
        string? parameters = null)
    {
        var namespaceName = NamespaceOf(node);
        var containers = node.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(static type => type.Identifier.ValueText)
            .ToArray();
        var components = new List<string>();
        if (namespaceName.Length > 0)
        {
            components.Add(namespaceName);
        }

        components.AddRange(containers);
        components.Add(name);
        return new Declaration(
            node,
            kind,
            name,
            string.Join('.', components),
            namespaceName,
            accessibility,
            name + (parameters ?? string.Empty));
    }

    private static string NamespaceOf(SyntaxNode node) =>
        string.Join(
            '.',
            node.AncestorsAndSelf()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(static declaration => declaration.Name.ToString()));

    private static string Accessibility(SyntaxTokenList modifiers, SyntaxNode declaration)
    {
        var hasProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        var hasInternal = modifiers.Any(SyntaxKind.InternalKeyword);
        var hasPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);
        if (hasProtected && hasInternal)
        {
            return "protected-internal";
        }

        if (hasPrivate && hasProtected)
        {
            return "private-protected";
        }

        if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
        if (hasInternal) return "internal";
        if (hasProtected) return "protected";
        if (hasPrivate) return "private";
        if (modifiers.Any(SyntaxKind.FileKeyword)) return "file";
        if (declaration.Parent is InterfaceDeclarationSyntax
            or EnumDeclarationSyntax) return "public";
        if (declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
            && declaration.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
        {
            return "internal";
        }

        return "private";
    }

    private static string Parameters(ParameterListSyntax parameters) =>
        "(" + string.Join(',', parameters.Parameters.Select(Parameter)) + ")";

    private static string BracketParameters(BracketedParameterListSyntax parameters) =>
        "[" + string.Join(',', parameters.Parameters.Select(Parameter)) + "]";

    private static string Parameter(ParameterSyntax parameter)
    {
        var modifiers = string.Join(' ', parameter.Modifiers.Select(static token => token.ValueText));
        var type = parameter.Type?.WithoutTrivia().ToString() ?? "?";
        return modifiers.Length == 0 ? type : modifiers + " " + type;
    }

    private static int? Rank(string query, string name, string fullyQualifiedName)
    {
        if (fullyQualifiedName.Equals(query, StringComparison.Ordinal)) return 0;
        if (name.Equals(query, StringComparison.Ordinal)) return 1;
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)
            || fullyQualifiedName.Equals(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
        if (TokenMatch(name, query)) return 4;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 5;
        return null;
    }

    private static bool TokenMatch(string candidate, string query)
    {
        var candidateTokens = Tokens(candidate);
        var queryTokens = Tokens(query);
        if (queryTokens.Length > 1)
        {
            return queryTokens.All(queryToken => candidateTokens.Any(candidateToken =>
                candidateToken.StartsWith(queryToken, StringComparison.OrdinalIgnoreCase)));
        }

        if (queryTokens.Length == 1
            && candidateTokens.Any(candidateToken => candidateToken.StartsWith(
                queryTokens[0],
                StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var acronym = string.Concat(candidateTokens.Select(static token => token[0]));
        return acronym.StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Tokens(string value)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (!char.IsLetterOrDigit(current))
            {
                AddToken(tokens, value, start, index);
                start = -1;
                continue;
            }

            if (start < 0)
            {
                start = index;
            }
            else if (char.IsUpper(current)
                && (char.IsLower(value[index - 1])
                    || index + 1 < value.Length
                    && char.IsUpper(value[index - 1])
                    && char.IsLower(value[index + 1])))
            {
                AddToken(tokens, value, start, index);
                start = index;
            }
        }

        AddToken(tokens, value, start, value.Length);
        return tokens.ToArray();
    }

    private static void AddToken(List<string> tokens, string value, int start, int end)
    {
        if (start >= 0 && end > start)
        {
            tokens.Add(value[start..end]);
        }
    }

    private static bool InNamespace(string candidate, string filter) =>
        candidate.Equals(filter, StringComparison.Ordinal)
        || candidate.StartsWith(filter + ".", StringComparison.Ordinal);

    private static bool IsTestPath(string path, IReadOnlyList<string> owners)
    {
        if (owners.Count == 0)
        {
            return TestTokens(path).Any(IsTestToken);
        }

        var deepest = owners.Max(ProjectDirectoryDepth);
        var nearestOwners = owners.Where(owner => ProjectDirectoryDepth(owner) == deepest);
        return nearestOwners.All(owner => TestTokens(owner).Any(IsTestToken));
    }

    private static int ProjectDirectoryDepth(string project) =>
        project.Count(static character => character == '/');

    private static IEnumerable<string> TestTokens(string path) =>
        path.Split(['/', '\\', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);

    private static bool IsTestToken(string token) =>
        token.Equals("test", StringComparison.OrdinalIgnoreCase)
        || token.Equals("tests", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);

    private static void AppendMany(IncrementalHash hash, IEnumerable<string> values)
    {
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            StructuralCandidateIdentity.Append(hash, value);
        }
    }

    private sealed record Declaration(
        SyntaxNode Node,
        string Kind,
        string Name,
        string FullyQualifiedName,
        string Namespace,
        string Accessibility,
        string Signature);
}
