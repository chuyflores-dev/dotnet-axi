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
using Microsoft.CodeAnalysis.Text;

namespace DotNetAxi.Roslyn;

public enum SemanticTargetResolutionStatus
{
    Resolved,
    NotFound,
    Ambiguous,
    Stale,
    Unsupported,
    Unresolved,
}

public enum SemanticTargetVariantStatus
{
    Resolved,
    Unresolved,
}

/// <summary>
/// One compiler meaning of a resolved declaration in an evaluated project
/// and framework. Successful variants retain the Roslyn objects required by
/// the relationship traversal that consumes this resolution.
/// </summary>
public sealed record SemanticTargetVariant
{
    internal SemanticTargetVariant(
        FileCompilerVariant variant,
        SemanticTargetVariantStatus status,
        string? identity,
        string? display,
        string? reason,
        Project? project,
        CSharpCompilation? compilation,
        ISymbol? symbol)
    {
        Variant = variant;
        Status = status;
        Identity = identity;
        Display = display;
        Reason = reason;
        Project = project;
        Compilation = compilation;
        Symbol = symbol;
    }

    internal FileCompilerVariant Variant { get; }

    public string ProjectPath => Variant.Project;

    public string? Configuration => Variant.Configuration;

    public string? Framework => Variant.Framework;

    public SemanticTargetVariantStatus Status { get; }

    public string? Identity { get; }

    public string? Display { get; }

    public string? Reason { get; }

    public Project? Project { get; }

    public CSharpCompilation? Compilation { get; }

    public ISymbol? Symbol { get; }
}

/// <summary>
/// Resolves one logical declaration before a compiler relationship is
/// traversed. Dispose successful results after their Roslyn project and symbol
/// handles have been consumed.
/// </summary>
public sealed class SemanticTargetResolution : IDisposable
{
    private IReadOnlyList<MSBuildWorkspace>? _workspaces;

    internal SemanticTargetResolution(
        string target,
        SemanticTargetResolutionStatus status,
        string? snapshot,
        IEnumerable<SymbolDeclarationMatch>? declarations,
        IEnumerable<SymbolDeclarationMatch>? candidates,
        IEnumerable<SemanticTargetVariant>? variants,
        string? errorCode,
        string? correction,
        int? candidateTotal = null,
        IEnumerable<MSBuildWorkspace>? workspaces = null)
    {
        Target = target;
        Status = status;
        Snapshot = snapshot;
        Declarations = Array.AsReadOnly(declarations?.ToArray() ?? []);
        Candidates = Array.AsReadOnly(candidates?.ToArray() ?? []);
        CandidateTotal = candidateTotal ?? Candidates.Count;
        CandidateOmitted = CandidateTotal - Candidates.Count;
        Variants = Array.AsReadOnly(variants?.ToArray() ?? []);
        ErrorCode = errorCode;
        Correction = correction;
        PartialReasons = Array.AsReadOnly(Variants
            .Where(static variant =>
                variant.Status is SemanticTargetVariantStatus.Unresolved)
            .Select(static variant => variant.Reason!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
        _workspaces = Array.AsReadOnly(workspaces?.ToArray() ?? []);
    }

    public string Target { get; }

    public SemanticTargetResolutionStatus Status { get; }

    public string? Snapshot { get; }

    public IReadOnlyList<SymbolDeclarationMatch> Declarations { get; }

    public IReadOnlyList<SymbolDeclarationMatch> Candidates { get; }

    public int CandidateTotal { get; }

    public int CandidateOmitted { get; }

    public bool CandidatesTruncated => CandidateOmitted > 0;

    public IReadOnlyList<SemanticTargetVariant> Variants { get; }

    public string? ErrorCode { get; }

    public string? Correction { get; }

    public IReadOnlyList<string> PartialReasons { get; }

    public bool Resolved => Status is SemanticTargetResolutionStatus.Resolved;

    public string? CanonicalId => Resolved
        ? Declarations
            .Select(static declaration => declaration.Id)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault()
        : null;

    public bool HasPartialCoverage => PartialReasons.Count > 0;

    public void Dispose()
    {
        var workspaces = Interlocked.Exchange(ref _workspaces, null);
        if (workspaces is null)
        {
            return;
        }

        foreach (var workspace in workspaces)
        {
            workspace.Dispose();
        }
    }
}

/// <summary>
/// Selects passive declaration candidates, evaluates their exact compiler
/// variants, and proves that they represent one Roslyn symbol. It never
/// guesses between overloads or unrelated declarations.
/// </summary>
public sealed class RoslynSemanticTargetResolver
{
    public const int DefaultCandidateLimit = 20;

    private readonly IReadOnlyList<string> _projects;
    private readonly SymbolDeclarationSearcher _searcher;
    private readonly SymbolEntityResolver _entityResolver;
    private readonly MsBuildCompilerVariantResolver _variantResolver;
    private readonly RoslynCompilerContextLoader _compilerContextLoader;

    public RoslynSemanticTargetResolver(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects)
        : this(
            traverser,
            ownership,
            projects,
            LoadProjectAsync)
    {
    }

    internal RoslynSemanticTargetResolver(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects,
        Func<MSBuildWorkspace, string, CancellationToken, Task<Project>>
            projectLoader)
    {
        ArgumentNullException.ThrowIfNull(traverser);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(projects);
        _projects = Array.AsReadOnly(projects
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
        _searcher = new SymbolDeclarationSearcher(traverser, ownership);
        _entityResolver = new SymbolEntityResolver(traverser, ownership);
        _variantResolver = new MsBuildCompilerVariantResolver(
            new DotNetHostResolver());
        _compilerContextLoader = new RoslynCompilerContextLoader(
            projectLoader
                ?? throw new ArgumentNullException(nameof(projectLoader)));
    }

    internal static Task<Project> LoadProjectAsync(
        MSBuildWorkspace workspace,
        string projectPath,
        CancellationToken cancellationToken) =>
        RoslynCompilerContextLoader.LoadProjectAsync(
            workspace,
            projectPath,
            cancellationToken);

    public async ValueTask<SemanticTargetResolution> ResolveAsync(
        string target,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? scope = null,
        CancellationToken cancellationToken = default)
        => await ResolveAsync(
                target,
                traversal,
                scope,
                new ProjectGraphEvaluationOptions(),
                cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<SemanticTargetResolution> ResolveAsync(
        string target,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? scope,
        ProjectGraphEvaluationOptions evaluationOptions,
        CancellationToken cancellationToken = default) =>
        await ResolveCoreAsync(
                target,
                traversal,
                scope,
                evaluationOptions,
                session: null,
                cancellationToken)
            .ConfigureAwait(false);

    internal async ValueTask<SemanticTargetResolution> ResolveAsync(
        string target,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? scope,
        ProjectGraphEvaluationOptions evaluationOptions,
        SemanticQuerySession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return await ResolveCoreAsync(
                target,
                traversal,
                scope,
                evaluationOptions,
                session,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<SemanticTargetResolution> ResolveCoreAsync(
        string target,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? scope,
        ProjectGraphEvaluationOptions evaluationOptions,
        SemanticQuerySession? session,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(evaluationOptions);
        if (target.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A semantic target cannot contain a null character.",
                nameof(target));
        }

        scope ??= new SymbolDeclarationScope(
            solution: null,
            projects: null,
            traversal.ExplicitPaths,
            includeTests: false,
            includeGenerated: traversal.IncludeGenerated == true);

        var root = Path.GetFullPath(traversal.WorkspaceRoot);
        var selection = await SelectCandidatesAsync(
                target,
                traversal,
                scope,
                cancellationToken)
            .ConfigureAwait(false);
        if (selection.Result is not null)
        {
            return selection.Result;
        }

        var candidates = selection.Candidates!;
        var effectiveProjects = EffectiveCandidateProjects(scope, candidates);
        var compilerVariants = session is null
            ? _variantResolver.Resolve(
                root,
                effectiveProjects,
                evaluationOptions,
                cancellationToken)
            : session.ResolveCompilerVariants(
                root,
                effectiveProjects,
                cancellationToken);
        Dictionary<RoslynCompilerContextKey, RoslynCompilerContext>?
            standaloneContexts = session is null ? [] : null;
        try
        {
            var states = new List<CandidateState>(candidates.Count);
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                states.Add(await ResolveCandidateAsync(
                        root,
                        candidate,
                        compilerVariants,
                        session,
                        standaloneContexts,
                        evaluationOptions,
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            var groups = GroupCandidates(states);
            if (groups.Count > 1)
            {
                return Failure(
                    target,
                    SemanticTargetResolutionStatus.Ambiguous,
                    selection.Snapshot,
                    "semantic.target_ambiguous",
                    "Retry with one returned candidate ID; use a fully qualified name only when it selects one declaration. Inspect all candidates with "
                    + SearchQuery(target)
                    + ".",
                    candidates: groups.Select(static group => group[0].Candidate));
            }

            var group = groups[0];
            var variants = MergeVariants(group);
            var resolved = variants.Any(static variant =>
                variant.Status is SemanticTargetVariantStatus.Resolved);
            if (!resolved)
            {
                return Failure(
                    target,
                    SemanticTargetResolutionStatus.Unresolved,
                    CreateSnapshot(selection.Snapshot!, compilerVariants, variants),
                    "semantic.target_unresolved",
                    "Fix the reported project or framework failures, then retry the same target.",
                    declarations: group.Select(static state => state.Candidate),
                    variants: variants);
            }

            IEnumerable<MSBuildWorkspace>? workspaces = null;
            if (standaloneContexts is not null)
            {
                workspaces = standaloneContexts.Values
                    .Select(static context => context.Workspace)
                    .OfType<MSBuildWorkspace>()
                    .ToArray();
                foreach (var context in standaloneContexts.Values)
                {
                    context.TransferOwnership();
                }
            }

            return new SemanticTargetResolution(
                target,
                SemanticTargetResolutionStatus.Resolved,
                CreateSnapshot(selection.Snapshot!, compilerVariants, variants),
                group.Select(static state => state.Candidate),
                null,
                variants,
                null,
                null,
                candidateTotal: null,
                workspaces);
        }
        finally
        {
            if (standaloneContexts is not null)
            {
                foreach (var context in standaloneContexts.Values)
                {
                    context.Dispose();
                }
            }
        }
    }

    private async ValueTask<CandidateSelection> SelectCandidatesAsync(
        string target,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope scope,
        CancellationToken cancellationToken)
    {
        if (SymbolEntityResolver.IsSupportedId(target))
        {
            var entity = await _entityResolver.ResolveAsync(
                    target,
                    traversal,
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);
            if (entity.Stale)
            {
                return CandidateSelection.Completed(Failure(
                    target,
                    SemanticTargetResolutionStatus.Stale,
                    entity.Snapshot,
                    entity.ErrorCode!,
                    entity.Query!,
                    candidates: entity.ReplacementCandidates));
            }

            var matches = entity.Matches.Where(match => InScope(match, scope)).ToArray();
            if (matches.Length == 0)
            {
                return CandidateSelection.Completed(Failure(
                    target,
                    SemanticTargetResolutionStatus.Stale,
                    entity.Snapshot,
                    "evidence.stale_id",
                    SearchQuery(entity.LookupName),
                    candidates: entity.ReplacementCandidates));
            }

            return CandidateSelection.Selected(entity.Snapshot, matches);
        }

        if (target.StartsWith("symbol/", StringComparison.Ordinal)
            || LooksLikeAnotherEntityId(target))
        {
            return CandidateSelection.Completed(Failure(
                target,
                SemanticTargetResolutionStatus.Unsupported,
                snapshot: null,
                "semantic.target_unsupported",
                "Use a canonical symbol/v2 ID, a fully qualified declaration name, or a declaration query."));
        }

        var search = await _searcher.SearchAsync(
                new SymbolDeclarationSearchRequest(
                    target,
                    traversal,
                    includeTests: scope.IncludeTests,
                    scope: scope),
                cancellationToken)
            .ConfigureAwait(false);
        var scoped = search.Matches.Where(match => InScope(match, scope)).ToArray();
        if (scoped.Length == 0)
        {
            return CandidateSelection.Completed(Failure(
                target,
                SemanticTargetResolutionStatus.NotFound,
                search.Snapshot,
                "semantic.target_not_found",
                SearchQuery(target)));
        }

        var bestRank = scoped.Min(static match => match.Rank);
        return CandidateSelection.Selected(
            search.Snapshot,
            scoped.Where(match => match.Rank == bestRank).ToArray());
    }

    private async ValueTask<CandidateState> ResolveCandidateAsync(
        string workspaceRoot,
        SymbolDeclarationMatch candidate,
        CompilerVariantResolution resolution,
        SemanticQuerySession? session,
        IDictionary<RoslynCompilerContextKey, RoslynCompilerContext>?
            standaloneContexts,
        ProjectGraphEvaluationOptions evaluationOptions,
        CancellationToken cancellationToken)
    {
        if (!resolution.IsAvailable)
        {
            return new CandidateState(
                candidate,
                candidate.OwningProjects.Select(project => Unresolved(
                    new FileCompilerVariant(project, null, null, project),
                    resolution.FailureReason ?? "msbuild.unavailable")));
        }

        var evaluatedVariants = resolution.Variants
            .Where(evaluated =>
                evaluated.Sources.Contains(candidate.Range.Start.Path)
                || evaluated.FailureReason is not null
                && candidate.OwningProjects.Contains(
                    evaluated.Variant.Project,
                    StringComparer.Ordinal))
            .ToArray();
        if (evaluatedVariants.Length == 0)
        {
            return new CandidateState(
                candidate,
                [Unresolved(
                    new FileCompilerVariant(
                        candidate.OwningProjects.FirstOrDefault() ?? "unknown",
                        null,
                        null,
                        "ownership.not_found"),
                    "ownership.not_found")]);
        }

        var variants = new List<SemanticTargetVariant>(evaluatedVariants.Length);
        foreach (var evaluated in evaluatedVariants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variant = evaluated.Variant;
            if (evaluated.FailureReason is not null)
            {
                variants.Add(Unresolved(variant, evaluated.FailureReason));
                continue;
            }

            RoslynCompilerContext context;
            if (session is not null)
            {
                context = await session.GetCompilerContextAsync(
                        workspaceRoot,
                        variant,
                        RoslynCompilerContextPurpose.Target,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var key = RoslynCompilerContextKey.From(variant);
                if (!standaloneContexts!.TryGetValue(key, out context!))
                {
                    context = await _compilerContextLoader.LoadAsync(
                            workspaceRoot,
                            variant,
                            evaluationOptions,
                            RoslynCompilerContextPurpose.Target,
                            cancellationToken)
                        .ConfigureAwait(false);
                    standaloneContexts.Add(key, context);
                }
            }

            variants.Add(ResolveInContext(
                candidate,
                variant,
                context,
                cancellationToken));
        }

        return new CandidateState(candidate, variants);
    }

    private static SemanticTargetVariant ResolveInContext(
        SymbolDeclarationMatch candidate,
        FileCompilerVariant variant,
        RoslynCompilerContext context,
        CancellationToken cancellationToken)
    {
        if (context.FailureReason is not null)
        {
            return Unresolved(variant, context.FailureReason);
        }

        if (!context.Trees.TryGetValue(candidate.Range.Start.Path, out var tree))
        {
            return Unresolved(variant, "project.source_not_in_scope");
        }

        if (!context.ContentHashes.TryGetValue(
                candidate.Range.Start.Path,
                out var contentHash)
            || !contentHash.Equals(
                Convert.ToHexStringLower(SHA256.HashData(candidate.SourceBytes)),
                StringComparison.Ordinal))
        {
            return Unresolved(variant, "candidate.stale");
        }

        var node = FindDeclaration(tree, candidate, cancellationToken);
        if (node is null)
        {
            return Unresolved(variant, "semantic.declaration_not_found");
        }

        var diagnosticReason = context.DiagnosticReason(
            RoslynCompilerContextPurpose.Target);
        if (diagnosticReason is not null)
        {
            return Unresolved(variant, diagnosticReason);
        }

        var model = context.Compilation!.GetSemanticModel(
            tree,
            ignoreAccessibility: true);
        var symbol = node is BaseNamespaceDeclarationSyntax namespaceDeclaration
            ? NamespaceSymbol(
                context.Compilation,
                namespaceDeclaration)
            : model.GetDeclaredSymbol(node, cancellationToken);
        if (symbol is null)
        {
            return Unresolved(variant, "semantic.target_unsupported");
        }

        if (symbol.Kind is SymbolKind.ErrorType)
        {
            return Unresolved(variant, "semantic.target_unsupported");
        }

        var identity = DocumentationCommentId.CreateDeclarationId(symbol);
        return new SemanticTargetVariant(
            variant,
            SemanticTargetVariantStatus.Resolved,
            identity,
            symbol.ToDisplayString(
                SymbolDisplayFormat.CSharpErrorMessageFormat),
            reason: null,
            context.Project,
            context.Compilation,
            symbol);
    }

    private static INamespaceSymbol? NamespaceSymbol(
        CSharpCompilation compilation,
        BaseNamespaceDeclarationSyntax declaration)
    {
        INamespaceSymbol current = compilation.GlobalNamespace;
        var segments = declaration.AncestorsAndSelf()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .SelectMany(static item => item.Name
                .DescendantTokens()
                .Where(static token => token.IsKind(SyntaxKind.IdentifierToken))
                .Select(static token => token.ValueText));
        foreach (var segment in segments)
        {
            var next = current.GetNamespaceMembers().FirstOrDefault(candidate =>
                candidate.Name.Equals(segment, StringComparison.Ordinal));
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private IReadOnlyList<string> EffectiveProjects(SymbolDeclarationScope scope) =>
        scope.Projects.Count == 0
            ? _projects
            : Array.AsReadOnly(_projects
                .Where(project => scope.Projects.Contains(
                    project,
                    StringComparer.Ordinal))
                .ToArray());

    private IReadOnlyList<string> EffectiveCandidateProjects(
        SymbolDeclarationScope scope,
        IEnumerable<SymbolDeclarationMatch> candidates)
    {
        return SelectCandidateProjects(
            EffectiveProjects(scope),
            candidates.SelectMany(static candidate => candidate.OwningProjects));
    }

    internal static IReadOnlyList<string> SelectCandidateProjects(
        IEnumerable<string> effectiveProjects,
        IEnumerable<string> candidateProjects)
    {
        ArgumentNullException.ThrowIfNull(effectiveProjects);
        ArgumentNullException.ThrowIfNull(candidateProjects);

        var effective = effectiveProjects.ToHashSet(StringComparer.Ordinal);
        return Array.AsReadOnly(candidateProjects
            .Where(effective.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    private static bool InScope(
        SymbolDeclarationMatch match,
        SymbolDeclarationScope scope) =>
        scope.Projects.Count == 0
        || match.OwningProjects.Any(project =>
            scope.Projects.Contains(project, StringComparer.Ordinal));

    private static IReadOnlyList<IReadOnlyList<CandidateState>> GroupCandidates(
        IReadOnlyList<CandidateState> states)
    {
        var groups = new List<List<CandidateState>>();
        foreach (var state in states)
        {
            var matching = groups
                .Where(group => group.Any(existing => SameTarget(existing, state)))
                .ToArray();
            if (matching.Length == 0)
            {
                groups.Add([state]);
                continue;
            }

            matching[0].Add(state);
            foreach (var additional in matching.Skip(1))
            {
                matching[0].AddRange(additional);
                groups.Remove(additional);
            }
        }

        return Array.AsReadOnly(groups
            .Select(static group =>
                (IReadOnlyList<CandidateState>)Array.AsReadOnly(group.ToArray()))
            .ToArray());
    }

    private static bool SameTarget(CandidateState left, CandidateState right) =>
        left.Variants.Any(leftVariant =>
            leftVariant.Status is SemanticTargetVariantStatus.Resolved
            && right.Variants.Any(rightVariant =>
                rightVariant.Status is SemanticTargetVariantStatus.Resolved
                && RoslynCompilerContextKey.From(leftVariant.Variant)
                    .Equals(RoslynCompilerContextKey.From(rightVariant.Variant))
                && SymbolEqualityComparer.Default.Equals(
                    leftVariant.Symbol,
                    rightVariant.Symbol)));

    private static IReadOnlyList<SemanticTargetVariant> MergeVariants(
        IEnumerable<CandidateState> states) =>
        Array.AsReadOnly(states
            .SelectMany(static state => state.Variants)
            .GroupBy(static variant =>
                RoslynCompilerContextKey.From(variant.Variant))
            .Select(static group => group.FirstOrDefault(variant =>
                    variant.Status is SemanticTargetVariantStatus.Resolved)
                ?? group.First())
            .OrderBy(static variant => variant.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static variant => variant.Configuration, StringComparer.Ordinal)
            .ThenBy(static variant => variant.Framework, StringComparer.Ordinal)
            .ToArray());

    private static SyntaxNode? FindDeclaration(
        SyntaxTree tree,
        SymbolDeclarationMatch candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.SourceSpanStart < 0 || candidate.SourceSpanLength < 0
            || candidate.SourceSpanStart + candidate.SourceSpanLength
            > tree.Length)
        {
            return null;
        }

        var span = new TextSpan(
            candidate.SourceSpanStart,
            candidate.SourceSpanLength);
        return tree.GetRoot(cancellationToken)
            .DescendantNodes()
            .FirstOrDefault(node => node.Span == span);
    }

    private static SemanticTargetVariant Unresolved(
        FileCompilerVariant variant,
        string reason) =>
        new(
            variant,
            SemanticTargetVariantStatus.Unresolved,
            identity: null,
            display: null,
            reason,
            project: null,
            compilation: null,
            symbol: null);

    private static SemanticTargetResolution Failure(
        string target,
        SemanticTargetResolutionStatus status,
        string? snapshot,
        string errorCode,
        string correction,
        IEnumerable<SymbolDeclarationMatch>? declarations = null,
        IEnumerable<SymbolDeclarationMatch>? candidates = null,
        IEnumerable<SemanticTargetVariant>? variants = null)
    {
        var allCandidates = candidates?.ToArray() ?? [];
        return new SemanticTargetResolution(
            target,
            status,
            snapshot,
            declarations,
            allCandidates.Take(DefaultCandidateLimit),
            variants,
            errorCode,
            correction,
            allCandidates.Length);
    }

    private static bool LooksLikeAnotherEntityId(string target)
    {
        var kindSeparator = target.IndexOf('/', StringComparison.Ordinal);
        var versionSeparator = kindSeparator < 0
            ? -1
            : target.IndexOf('/', kindSeparator + 1);
        if (kindSeparator <= 0 || versionSeparator <= kindSeparator + 2
            || target[kindSeparator + 1] != 'v')
        {
            return false;
        }

        return target.AsSpan(kindSeparator + 2, versionSeparator - kindSeparator - 2)
            .IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static string SearchQuery(string target) =>
        "dnaxi search symbol "
        + Quote(target)
        + " --fields 'id,signature,owning_projects,variant_count,variants' --full";

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string CreateSnapshot(
        string syntaxSnapshot,
        CompilerVariantResolution resolution,
        IEnumerable<SemanticTargetVariant> variants)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/semantic-target-snapshot/v1");
        Append(hash, syntaxSnapshot);
        Append(hash, resolution.Runtime?.SdkVersion ?? string.Empty);
        Append(hash, resolution.Runtime?.MsBuildVersion ?? string.Empty);
        Append(hash, resolution.FailureReason ?? string.Empty);
        foreach (var variant in variants)
        {
            Append(hash, variant.ProjectPath);
            Append(hash, variant.Configuration ?? string.Empty);
            Append(hash, variant.Framework ?? string.Empty);
            Append(hash, variant.Variant.ContextFingerprint);
            Append(hash, variant.Status.ToString());
            Append(hash, variant.Identity ?? string.Empty);
            Append(hash, variant.Reason ?? string.Empty);
        }

        return "ws_" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
            length,
            bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

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

    private sealed record CandidateSelection(
        string? Snapshot,
        IReadOnlyList<SymbolDeclarationMatch>? Candidates,
        SemanticTargetResolution? Result)
    {
        public static CandidateSelection Selected(
            string snapshot,
            IEnumerable<SymbolDeclarationMatch> candidates) =>
            new(snapshot, Array.AsReadOnly(candidates.ToArray()), Result: null);

        public static CandidateSelection Completed(
            SemanticTargetResolution result) =>
            new(Snapshot: null, Candidates: null, result);
    }

    private sealed record CandidateState(
        SymbolDeclarationMatch Candidate,
        IReadOnlyList<SemanticTargetVariant> Variants)
    {
        public CandidateState(
            SymbolDeclarationMatch candidate,
            IEnumerable<SemanticTargetVariant> variants)
            : this(candidate, Array.AsReadOnly(variants.ToArray()))
        {
        }
    }

}
