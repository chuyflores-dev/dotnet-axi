using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNetAxi.Roslyn;

public enum SemanticCandidateStatus
{
    Verified,
    Rejected,
    Unresolved,
}

public sealed record SemanticVariantVerification
{
    public SemanticVariantVerification(
        string? project,
        string? configuration,
        string? framework,
        SemanticCandidateStatus status,
        string? symbol,
        string? reason)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status is SemanticCandidateStatus.Verified && reason is not null)
        {
            throw new ArgumentException(
                "A verified variant cannot carry a failure reason.",
                nameof(reason));
        }

        if (status is not SemanticCandidateStatus.Verified
            && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A rejected or unresolved variant requires a reason.",
                nameof(reason));
        }

        Project = project;
        Configuration = configuration;
        Framework = framework;
        Status = status;
        Symbol = symbol;
        Reason = reason;
    }

    public string? Project { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    public SemanticCandidateStatus Status { get; }

    public string? Symbol { get; }

    public string? Reason { get; }
}

public sealed record SemanticCandidateVerification
{
    public SemanticCandidateVerification(
        StructuralCandidate candidate,
        IEnumerable<SemanticVariantVerification> variants)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        ArgumentNullException.ThrowIfNull(variants);
        Variants = Array.AsReadOnly(variants.ToArray());
        Status = Variants.Any(static variant =>
            variant.Status is SemanticCandidateStatus.Verified)
            ? SemanticCandidateStatus.Verified
            : Variants.Any(static variant =>
                variant.Status is SemanticCandidateStatus.Unresolved)
                ? SemanticCandidateStatus.Unresolved
                : SemanticCandidateStatus.Rejected;
    }

    public StructuralCandidate Candidate { get; }

    public IReadOnlyList<SemanticVariantVerification> Variants { get; }

    public SemanticCandidateStatus Status { get; }
}

public sealed record SemanticSyntaxVerificationResult
{
    public SemanticSyntaxVerificationResult(
        string snapshot,
        IEnumerable<SemanticCandidateVerification> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        ArgumentNullException.ThrowIfNull(candidates);
        Snapshot = snapshot;
        Candidates = Array.AsReadOnly(candidates.ToArray());
        Discovered = Candidates.Count;
        Verified = Candidates.Count(static candidate =>
            candidate.Status is SemanticCandidateStatus.Verified);
        Rejected = Candidates.Count(static candidate =>
            candidate.Status is SemanticCandidateStatus.Rejected);
        Unresolved = Candidates.Count(static candidate =>
            candidate.Status is SemanticCandidateStatus.Unresolved);
        PartialReasons = Array.AsReadOnly(Candidates
            .SelectMany(static candidate => candidate.Variants)
            .Where(static variant =>
                variant.Status is SemanticCandidateStatus.Unresolved)
            .Select(static variant => variant.Reason!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    public int Discovered { get; }

    public string Snapshot { get; }

    public int Verified { get; }

    public int Rejected { get; }

    public int Unresolved { get; }

    public IReadOnlyList<SemanticCandidateVerification> Candidates { get; }

    public IReadOnlyList<string> PartialReasons { get; }

    public bool HasPartialCoverage => PartialReasons.Count > 0;
}

/// <summary>
/// Verifies stable syntax candidates with Roslyn's design-time MSBuild loader.
/// Evaluated compile items discover owning variants; failed passive owners
/// remain visible as unresolved coverage. Missing inputs are never restored
/// and compiler contexts are never silently substituted.
/// </summary>
public sealed class RoslynSemanticCandidateVerifier
{
    private readonly IFileOwnershipResolver _ownership;
    private readonly IReadOnlyList<string> _projects;
    private readonly MsBuildCompilerVariantResolver _variantResolver;

    public RoslynSemanticCandidateVerifier(
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        ArgumentNullException.ThrowIfNull(projects);
        _projects = Array.AsReadOnly(projects
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
        _variantResolver = new MsBuildCompilerVariantResolver(
            new DotNetHostResolver());
    }

    public async ValueTask<SemanticSyntaxVerificationResult> VerifyAsync(
        string workspaceRoot,
        RoslynSyntaxQueryResult syntax,
        ISemanticallyVerifiableSyntaxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(query);
        if (!syntax.QueryIdentity.Equals(query.Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The syntax result and semantic verifier must describe the same query.",
                nameof(query));
        }

        var root = Path.GetFullPath(workspaceRoot);
        var ownersByCandidate = syntax.Candidates.ToDictionary(
            static candidate => candidate.Id,
            candidate =>
            {
                var candidatePath = CandidatePath(root, candidate);
                var traversalPath = new WorkspaceTraversalPath(
                    candidatePath,
                    candidate.Range.Start.Path,
                    candidate.Range.Start.IsExternal);
                return _ownership.GetOwningProjects(traversalPath)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            },
            StringComparer.Ordinal);
        var resolution = _variantResolver.Resolve(
            root,
            _projects,
            cancellationToken);
        var contexts = new Dictionary<CompilerContextKey, ProjectCompilationContext>();
        var results = new List<SemanticCandidateVerification>(
            syntax.Candidates.Count);

        foreach (var candidate in syntax.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passiveOwners = ownersByCandidate[candidate.Id];
            var variants = resolution.Variants
                .Where(evaluated =>
                    evaluated.Sources.Contains(candidate.Range.Start.Path)
                    || (evaluated.FailureReason is not null
                        && passiveOwners.Contains(
                            evaluated.Variant.Project,
                            StringComparer.Ordinal)))
                .ToArray();
            var owners = passiveOwners
                .Concat(variants.Select(static variant => variant.Variant.Project))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (owners.Length == 0)
            {
                results.Add(new SemanticCandidateVerification(
                    candidate,
                    [Unresolved(
                        project: null,
                        configuration: null,
                        framework: null,
                        "ownership.not_found")]));
                continue;
            }

            if (!resolution.IsAvailable)
            {
                results.Add(new SemanticCandidateVerification(
                    candidate,
                    owners.Select(owner => Unresolved(
                        owner,
                        configuration: null,
                        framework: null,
                        resolution.FailureReason ?? "msbuild.unavailable"))));
                continue;
            }

            if (variants.Length == 0)
            {
                results.Add(new SemanticCandidateVerification(
                    candidate,
                    [Unresolved(
                        project: null,
                        configuration: null,
                        framework: null,
                        "ownership.not_found")]));
                continue;
            }

            var variantResults = new List<SemanticVariantVerification>(
                variants.Length);
            foreach (var evaluated in variants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var variant = evaluated.Variant;
                var key = new CompilerContextKey(
                    variant.Project,
                    variant.Configuration,
                    variant.Framework,
                    variant.ContextFingerprint);
                if (!contexts.TryGetValue(key, out var context))
                {
                    context = evaluated.FailureReason is null
                        ? await CreateContextAsync(
                                root,
                                variant,
                                cancellationToken)
                            .ConfigureAwait(false)
                        : ProjectCompilationContext.Failed(
                            evaluated.FailureReason);
                    contexts.Add(key, context);
                }

                variantResults.Add(VerifyCandidate(
                    candidate,
                    query.Verifier,
                    variant,
                    context,
                    cancellationToken));
            }

            results.Add(new SemanticCandidateVerification(
                candidate,
                variantResults));
        }

        return new SemanticSyntaxVerificationResult(
            CreateSnapshot(syntax.Snapshot, resolution, contexts, results),
            results);
    }

    private static string CandidatePath(
        string workspaceRoot,
        StructuralCandidate candidate)
    {
        if (candidate.Range.Start.IsExternal)
        {
            return candidate.Range.Start.Path;
        }

        var path = Path.GetFullPath(
            candidate.Range.Start.Path.Replace('/', Path.DirectorySeparatorChar),
            workspaceRoot);
        if (!IsWithin(workspaceRoot, path))
        {
            throw new InvalidOperationException(
                "A semantic candidate path escaped the workspace root.");
        }

        return path;
    }

    private static async ValueTask<ProjectCompilationContext> CreateContextAsync(
        string workspaceRoot,
        FileCompilerVariant variant,
        CancellationToken cancellationToken)
    {
        var projectPath = Path.GetFullPath(
            variant.Project.Replace('/', Path.DirectorySeparatorChar),
            workspaceRoot);
        if (!IsWithin(workspaceRoot, projectPath))
        {
            return ProjectCompilationContext.Failed("project.path_escape");
        }

        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = variant.Configuration ?? "Debug",
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["BuildProjectReferences"] = "false",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
        };
        if (variant.Framework is not null)
        {
            properties["TargetFramework"] = variant.Framework;
        }

        var treeByPath = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        var contentHashByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
        try
        {
            using var workspace = MSBuildWorkspace.Create(properties);
            workspace.RegisterWorkspaceFailedHandler(args =>
                workspaceDiagnostics.Add(args.Diagnostic));
            workspace.LoadMetadataForReferencedProjects = true;
            var loaded = await workspace.OpenProjectAsync(
                    projectPath,
                    progress: null,
                    cancellationToken)
                .ConfigureAwait(false);
            var compilation = await loaded.GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false) as CSharpCompilation;
            if (compilation is null)
            {
                return ProjectCompilationContext.Failed(
                    "project.compilation_unavailable");
            }

            var pathResolver = new WorkspacePathResolver(
                workspaceRoot,
                workspaceRoot);
            foreach (var document in loaded.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (document.FilePath is null)
                {
                    continue;
                }

                var tree = await document.GetSyntaxTreeAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (tree is null)
                {
                    continue;
                }

                var relativePath = pathResolver
                    .NormalizeOutput(document.FilePath)
                    .Path;
                if (IsBuildOutputPath(relativePath))
                {
                    continue;
                }

                treeByPath[relativePath] = tree;
                contentHashByPath[relativePath] = Convert.ToHexStringLower(
                    SHA256.HashData(await File.ReadAllBytesAsync(
                            document.FilePath,
                            cancellationToken)
                        .ConfigureAwait(false)));
            }

            var compilationErrors = compilation.GetDiagnostics(cancellationToken)
                .Where(static diagnostic =>
                    diagnostic.Severity is DiagnosticSeverity.Error)
                .ToArray();
            var diagnosticReason = DiagnosticReason(
                workspaceDiagnostics,
                compilationErrors);

            return ProjectCompilationContext.Succeeded(
                compilation,
                new ReadOnlyDictionary<string, SyntaxTree>(treeByPath),
                new ReadOnlyDictionary<string, string>(contentHashByPath),
                variant.Configuration ?? properties["Configuration"],
                variant.Framework ?? EvaluatedFramework(loaded.ParseOptions),
                diagnosticReason,
                compilationErrors,
                CompilationFingerprint(
                    workspaceRoot,
                    compilation,
                    variant.ContextFingerprint,
                    cancellationToken));
        }
        catch (InvalidOperationException)
        {
            return ProjectCompilationContext.Failed("project.load_failed");
        }
        catch (IOException)
        {
            return ProjectCompilationContext.Failed("project.load_failed");
        }
    }

    private static SemanticVariantVerification VerifyCandidate(
        StructuralCandidate candidate,
        SemanticSyntaxVerifier verifier,
        FileCompilerVariant variant,
        ProjectCompilationContext context,
        CancellationToken cancellationToken)
    {
        if (context.FailureReason is not null)
        {
            return Unresolved(variant, context.FailureReason);
        }

        var selectedVariant = new FileCompilerVariant(
            variant.Project,
            context.Configuration,
            context.Framework,
            variant.ContextFingerprint);

        if (!context.Trees.TryGetValue(candidate.Range.Start.Path, out var tree))
        {
            return Unresolved(selectedVariant, "project.source_not_in_scope");
        }

        var nodes = FindCandidateNodes(
            tree,
            verifier.Kind,
            candidate,
            cancellationToken);
        if (nodes.Count == 0)
        {
            return new SemanticVariantVerification(
                variant.Project,
                variant.Configuration,
                variant.Framework,
                SemanticCandidateStatus.Rejected,
                symbol: null,
                "semantic.not_in_variant");
        }

        if (!context.ContentHashes.TryGetValue(
                candidate.Range.Start.Path,
                out var contentHash))
        {
            return Unresolved(selectedVariant, "candidate.stale");
        }

        var node = nodes.FirstOrDefault(node => candidate.MatchesIdentity(
                contentHash,
                node.SpanStart,
                node.Span.Length,
                node.ToString()));
        if (node is null)
        {
            return Unresolved(selectedVariant, "candidate.stale");
        }

        var model = context.Compilation!.GetSemanticModel(
            tree,
            ignoreAccessibility: true);
        var result = verifier.Kind switch
        {
            SemanticSyntaxVerifierKind.Invocation => VerifyInvocation(
                model,
                (InvocationExpressionSyntax)node,
                verifier.RequestedName!,
                selectedVariant,
                cancellationToken),
            SemanticSyntaxVerifierKind.AttributedClass => VerifyAttributedClass(
                model,
                (ClassDeclarationSyntax)node,
                verifier.RequestedName!,
                selectedVariant,
                cancellationToken),
            SemanticSyntaxVerifierKind.ObjectCreation => VerifyObjectCreation(
                model,
                (ExpressionSyntax)node,
                verifier.RequestedName!,
                selectedVariant,
                cancellationToken),
            SemanticSyntaxVerifierKind.CatchClause => VerifyCatchClause(
                model,
                (CatchClauseSyntax)node,
                verifier.RequestedName,
                selectedVariant,
                cancellationToken),
            _ => throw new InvalidOperationException(
                "The semantic verifier kind is unsupported."),
        };
        if (context.DiagnosticReason is null)
        {
            return result;
        }

        var onlyCandidateErrors = result.Status is SemanticCandidateStatus.Unresolved
            && context.DiagnosticReason.Equals(
                "project.compilation_errors",
                StringComparison.Ordinal)
            && context.CompilationErrors.All(diagnostic =>
                diagnostic.Location.SourceTree == tree
                && node.FullSpan.Contains(diagnostic.Location.SourceSpan));
        return onlyCandidateErrors
            ? result
            : Unresolved(selectedVariant, context.DiagnosticReason);
    }

    private static IReadOnlyList<SyntaxNode> FindCandidateNodes(
        SyntaxTree tree,
        SemanticSyntaxVerifierKind kind,
        StructuralCandidate candidate,
        CancellationToken cancellationToken)
    {
        var root = tree.GetRoot(cancellationToken);
        var line = candidate.Range.Start.Line - 1;
        var column = candidate.Range.Start.Column - 1;
        var text = tree.GetText(cancellationToken);
        if (line < 0 || line >= text.Lines.Count
            || column < 0 || column > text.Lines[line].Span.Length)
        {
            return [];
        }

        var position = text.Lines[line].Start + column;
        return Array.AsReadOnly((kind switch
        {
            SemanticSyntaxVerifierKind.Invocation => root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node => node.SpanStart == position)
                .Cast<SyntaxNode>(),
            SemanticSyntaxVerifierKind.AttributedClass => root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(node => node.Keyword.SpanStart == position)
                .Cast<SyntaxNode>(),
            SemanticSyntaxVerifierKind.ObjectCreation => root.DescendantNodes()
                .OfType<ExpressionSyntax>()
                .Where(static node => node is ObjectCreationExpressionSyntax
                    or ImplicitObjectCreationExpressionSyntax
                    or ArrayCreationExpressionSyntax)
                .Where(node => node.SpanStart == position)
                .Cast<SyntaxNode>(),
            SemanticSyntaxVerifierKind.CatchClause => root.DescendantNodes()
                .OfType<CatchClauseSyntax>()
                .Where(node => node.SpanStart == position)
                .Cast<SyntaxNode>(),
            _ => [],
        }).ToArray());
    }

    private static SemanticVariantVerification VerifyInvocation(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        string requestedName,
        FileCompilerVariant variant,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        if (info.Symbol is IMethodSymbol method)
        {
            return Resolved(
                variant,
                method.Name.Equals(requestedName, StringComparison.Ordinal),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }

        return UnresolvedSymbol(variant, info);
    }

    private static SemanticVariantVerification VerifyAttributedClass(
        SemanticModel model,
        ClassDeclarationSyntax declaration,
        string requestedName,
        FileCompilerVariant variant,
        CancellationToken cancellationToken)
    {
        var normalizedRequest = WithoutAttributeSuffix(requestedName);
        var sawResolvedMismatch = false;
        var sawAmbiguity = false;
        foreach (var attribute in declaration.AttributeLists
                     .SelectMany(static list => list.Attributes)
                     .Where(attribute => AttributeTerminalName(attribute.Name) is { } name
                         && WithoutAttributeSuffix(name).Equals(
                             normalizedRequest,
                             StringComparison.Ordinal)))
        {
            var info = model.GetSymbolInfo(attribute, cancellationToken);
            if (info.Symbol is IMethodSymbol constructor)
            {
                var type = constructor.ContainingType;
                if (WithoutAttributeSuffix(type.Name).Equals(
                    normalizedRequest,
                    StringComparison.Ordinal))
                {
                    return Resolved(
                        variant,
                        matches: true,
                        type.ToDisplayString(
                            SymbolDisplayFormat.CSharpErrorMessageFormat));
                }

                sawResolvedMismatch = true;
            }
            else if (info.CandidateSymbols.Length > 0)
            {
                sawAmbiguity = true;
            }
        }

        if (sawResolvedMismatch)
        {
            return Resolved(variant, matches: false, symbol: null);
        }

        return Unresolved(
            variant,
            sawAmbiguity ? "semantic.ambiguous" : "semantic.unresolved");
    }

    private static SemanticVariantVerification VerifyObjectCreation(
        SemanticModel model,
        ExpressionSyntax creation,
        string requestedName,
        FileCompilerVariant variant,
        CancellationToken cancellationToken)
    {
        var type = model.GetTypeInfo(creation, cancellationToken).Type;
        while (type is IArrayTypeSymbol array)
        {
            type = array.ElementType;
        }

        if (type is not null && type.TypeKind is not TypeKind.Error)
        {
            var displayName = type.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat);
            return Resolved(
                variant,
                type.Name.Equals(requestedName, StringComparison.Ordinal)
                || displayName.Equals(requestedName, StringComparison.Ordinal),
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }

        var info = model.GetSymbolInfo(creation, cancellationToken);
        return UnresolvedSymbol(variant, info);
    }

    private static SemanticVariantVerification VerifyCatchClause(
        SemanticModel model,
        CatchClauseSyntax clause,
        string? requestedName,
        FileCompilerVariant variant,
        CancellationToken cancellationToken)
    {
        if (clause.Declaration?.Type is { } typeSyntax)
        {
            var type = model.GetTypeInfo(typeSyntax, cancellationToken).Type;
            if (type is null || type.TypeKind is TypeKind.Error)
            {
                return Unresolved(variant, "semantic.unresolved");
            }

            return Resolved(
                variant,
                requestedName is null
                || type.Name.Equals(requestedName, StringComparison.Ordinal),
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }

        if (requestedName is not null)
        {
            return Resolved(variant, matches: false, symbol: null);
        }

        var hasError = model.GetDiagnostics(
                clause.Span,
                cancellationToken)
            .Any(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Error);
        return hasError
            ? Unresolved(variant, "semantic.unresolved")
            : Resolved(variant, matches: true, "catch");
    }

    private static SemanticVariantVerification UnresolvedSymbol(
        FileCompilerVariant variant,
        SymbolInfo info) =>
        Unresolved(
            variant,
            info.CandidateSymbols.Length > 0
                ? "semantic.ambiguous"
                : "semantic.unresolved");

    private static SemanticVariantVerification Resolved(
        FileCompilerVariant variant,
        bool matches,
        string? symbol) =>
        matches
            ? new SemanticVariantVerification(
                variant.Project,
                variant.Configuration,
                variant.Framework,
                SemanticCandidateStatus.Verified,
                symbol,
                reason: null)
            : new SemanticVariantVerification(
                variant.Project,
                variant.Configuration,
                variant.Framework,
                SemanticCandidateStatus.Rejected,
                symbol,
                "semantic.mismatch");

    private static SemanticVariantVerification Unresolved(
        FileCompilerVariant variant,
        string reason) =>
        Unresolved(
            variant.Project,
            variant.Configuration,
            variant.Framework,
            reason);

    private static SemanticVariantVerification Unresolved(
        string? project,
        string? configuration,
        string? framework,
        string reason) =>
        new(
            project,
            configuration,
            framework,
            SemanticCandidateStatus.Unresolved,
            symbol: null,
            reason);

    private static string? AttributeTerminalName(NameSyntax name) =>
        name switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => null,
        };

    private static string? EvaluatedFramework(ParseOptions? options)
    {
        if (options is not CSharpParseOptions csharp)
        {
            return null;
        }

        return csharp.PreprocessorSymbolNames
            .Where(static symbol => !symbol.EndsWith(
                "_OR_GREATER",
                StringComparison.Ordinal))
            .Select(FrameworkFromSymbol)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string? FrameworkFromSymbol(string symbol)
    {
        var prefix = symbol.StartsWith("NETSTANDARD", StringComparison.Ordinal)
            ? "NETSTANDARD"
            : symbol.StartsWith("NETCOREAPP", StringComparison.Ordinal)
                ? "NETCOREAPP"
                : symbol.StartsWith("NET", StringComparison.Ordinal)
                    ? "NET"
                    : null;
        if (prefix is null)
        {
            return null;
        }

        var version = symbol[prefix.Length..];
        if (version.Length == 0
            || version.Any(static character =>
                !char.IsAsciiDigit(character) && character != '_'))
        {
            return null;
        }

        var name = prefix switch
        {
            "NETSTANDARD" => "netstandard",
            "NETCOREAPP" => "netcoreapp",
            _ => "net",
        };
        return name + version.Replace('_', '.');
    }

    private static string WithoutAttributeSuffix(string name) =>
        name.Length > "Attribute".Length
        && name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name[..^"Attribute".Length]
            : name;

    private static string? DiagnosticReason(
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        IReadOnlyCollection<Diagnostic> compilationErrors)
    {
        var workspace = workspaceDiagnostics.ToArray();
        if (workspace.Any(static diagnostic => IsMissingMetadata(
                diagnostic.Message)))
        {
            return "metadata.missing";
        }

        if (workspace.Any(static diagnostic =>
                diagnostic.Kind is WorkspaceDiagnosticKind.Failure))
        {
            return "project.load_failed";
        }

        if (compilationErrors.Any(static diagnostic => diagnostic.Id is
                "CS0006" or "CS0012" or "CS0518"))
        {
            return "metadata.missing";
        }

        return compilationErrors.Count > 0
            ? "project.compilation_errors"
            : null;
    }

    private static bool IsMissingMetadata(string message) =>
        message.Contains("metadata", StringComparison.OrdinalIgnoreCase)
        || message.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("missing", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("could not", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unable", StringComparison.OrdinalIgnoreCase))
        || message.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase);

    private static string CompilationFingerprint(
        string workspaceRoot,
        CSharpCompilation compilation,
        string evaluatedProjectFingerprint,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/semantic-context/v1");
        Append(hash, evaluatedProjectFingerprint);
        Append(hash, compilation.Options.OutputKind.ToString());
        Append(hash, compilation.Options.NullableContextOptions.ToString());
        foreach (var symbol in compilation.SyntaxTrees
                     .Select(static tree => tree.Options)
                     .OfType<CSharpParseOptions>()
                     .SelectMany(static options => options.PreprocessorSymbolNames)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            Append(hash, symbol);
        }

        foreach (var tree in compilation.SyntaxTrees
                     .OrderBy(static tree => tree.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, FingerprintPath(workspaceRoot, tree.FilePath));
            Append(hash, Convert.ToHexStringLower(
                tree.GetText(cancellationToken).GetChecksum().AsSpan()));
        }

        foreach (var reference in compilation.References
                     .OrderBy(static reference => reference.Display, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var display = reference.Display ?? string.Empty;
            Append(hash, FingerprintPath(workspaceRoot, display));
            if (Path.IsPathFullyQualified(display) && File.Exists(display))
            {
                try
                {
                    Append(hash, Convert.ToHexStringLower(
                        SHA256.HashData(File.ReadAllBytes(display))));
                }
                catch (IOException)
                {
                    Append(hash, "unreadable");
                }
                catch (UnauthorizedAccessException)
                {
                    Append(hash, "unreadable");
                }
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string CreateSnapshot(
        string syntaxSnapshot,
        CompilerVariantResolution resolution,
        IReadOnlyDictionary<CompilerContextKey, ProjectCompilationContext> contexts,
        IEnumerable<SemanticCandidateVerification> candidates)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/semantic-snapshot/v1");
        Append(hash, syntaxSnapshot);
        Append(hash, resolution.Runtime?.SdkVersion ?? string.Empty);
        Append(hash, resolution.Runtime?.MsBuildVersion ?? string.Empty);
        Append(hash, resolution.FailureReason ?? string.Empty);
        foreach (var context in contexts
                     .OrderBy(static pair => pair.Key.Project, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Key.Configuration, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Key.Framework, StringComparer.Ordinal)
                     .ThenBy(static pair => pair.Key.ContextFingerprint, StringComparer.Ordinal))
        {
            Append(hash, context.Key.Project);
            Append(hash, context.Key.Configuration ?? string.Empty);
            Append(hash, context.Key.Framework ?? string.Empty);
            Append(hash, context.Key.ContextFingerprint);
            Append(hash, context.Value.SemanticFingerprint);
            Append(hash, context.Value.FailureReason ?? string.Empty);
            Append(hash, context.Value.DiagnosticReason ?? string.Empty);
        }

        foreach (var candidate in candidates.OrderBy(
                     static candidate => candidate.Candidate.Id,
                     StringComparer.Ordinal))
        {
            Append(hash, candidate.Candidate.Id);
            Append(hash, candidate.Status.ToString());
            foreach (var variant in candidate.Variants)
            {
                Append(hash, variant.Project ?? string.Empty);
                Append(hash, variant.Configuration ?? string.Empty);
                Append(hash, variant.Framework ?? string.Empty);
                Append(hash, variant.Status.ToString());
                Append(hash, variant.Symbol ?? string.Empty);
                Append(hash, variant.Reason ?? string.Empty);
            }
        }

        return "ws_" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string FingerprintPath(string workspaceRoot, string path) =>
        Path.IsPathFullyQualified(path) && IsWithin(workspaceRoot, path)
            ? Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/')
            : Path.GetFileName(path);

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathFullyQualified(relative)
            && relative != ".."
            && !relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            && !relative.StartsWith(
                ".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    private static bool IsBuildOutputPath(string relativePath) =>
        relativePath.Split('/').Any(static segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private sealed record CompilerContextKey(
        string Project,
        string? Configuration,
        string? Framework,
        string ContextFingerprint);

    private sealed record ProjectCompilationContext(
        CSharpCompilation? Compilation,
        IReadOnlyDictionary<string, SyntaxTree> Trees,
        IReadOnlyDictionary<string, string> ContentHashes,
        string? Configuration,
        string? Framework,
        string? FailureReason,
        string? DiagnosticReason,
        IReadOnlyList<Diagnostic> CompilationErrors,
        string SemanticFingerprint)
    {
        public static ProjectCompilationContext Failed(string reason) =>
            new(null, new ReadOnlyDictionary<string, SyntaxTree>(
                new Dictionary<string, SyntaxTree>(StringComparer.Ordinal)),
                new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.Ordinal)),
                Configuration: null,
                Framework: null,
                FailureReason: reason,
                DiagnosticReason: null,
                CompilationErrors: [],
                SemanticFingerprint: "failed:" + reason);

        public static ProjectCompilationContext Succeeded(
            CSharpCompilation compilation,
            IReadOnlyDictionary<string, SyntaxTree> trees,
            IReadOnlyDictionary<string, string> contentHashes,
            string? configuration,
            string? framework,
            string? diagnosticReason,
            IReadOnlyList<Diagnostic> compilationErrors,
            string semanticFingerprint) =>
            new(
                compilation,
                trees,
                contentHashes,
                configuration,
                framework,
                FailureReason: null,
                diagnosticReason,
                compilationErrors,
                semanticFingerprint);
    }
}
