using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn;

public enum SemanticRelationshipKind
{
    References,
    Implementations,
    Overrides,
    Derived,
    Callers,
    Callees,
}

/// <summary>
/// Executes selected compiler relationship queries from one resolved target.
/// The session owns evaluated graph, variant, and compiler-context reuse for
/// the whole composition; standalone searchers keep their existing lifetime.
/// </summary>
public sealed class RoslynRelationshipContextSearcher
{
    private readonly IReadOnlyList<string> _projects;
    private readonly RoslynSemanticTargetResolver _targetResolver;
    private readonly MsBuildProjectGraphEvaluator _graphEvaluator;
    private readonly MsBuildCompilerVariantResolver _variantResolver;
    private readonly RoslynReferenceSearcher _references;
    private readonly RoslynImplementationSearcher _implementations;
    private readonly RoslynCallerSearcher _callers;
    private readonly RoslynCalleeSearcher _callees;

    public RoslynRelationshipContextSearcher(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects)
        : this(traverser, ownership, projects, new DotNetHostResolver())
    {
    }

    internal RoslynRelationshipContextSearcher(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects,
        IDotNetHostResolver hostResolver)
    {
        ArgumentNullException.ThrowIfNull(traverser);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(hostResolver);
        _projects = Array.AsReadOnly(projects
            .Distinct(PathComparer())
            .Order(StringComparer.Ordinal)
            .ToArray());
        _targetResolver = new RoslynSemanticTargetResolver(
            traverser,
            ownership,
            _projects);
        _graphEvaluator = new MsBuildProjectGraphEvaluator(hostResolver);
        _variantResolver = new MsBuildCompilerVariantResolver(hostResolver);
        _references = new RoslynReferenceSearcher(
            traverser, ownership, _projects, hostResolver);
        _implementations = new RoslynImplementationSearcher(
            traverser, ownership, _projects, hostResolver);
        _callers = new RoslynCallerSearcher(
            traverser, ownership, _projects, hostResolver);
        _callees = new RoslynCalleeSearcher(
            traverser, ownership, _projects, hostResolver);
    }

    public async ValueTask<RoslynRelationshipContextResult> FindAsync(
        string target,
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? declarationScope,
        IEnumerable<SemanticRelationshipKind> relationships,
        ProjectGraphEvaluationOptions? evaluationOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(relationships);
        declarationScope ??= new SymbolDeclarationScope(
            selection.Kind is WorkspaceEntryPointKind.Solution ? selection.Path : null,
            _projects,
            traversal.ExplicitPaths,
            includeTests: false,
            includeGenerated: traversal.IncludeGenerated == true);
        var requested = relationships.ToArray();
        var selected = requested
            .Where(static relationship => Enum.IsDefined(relationship))
            .Distinct()
            .Order()
            .ToHashSet();
        if (selected.Count != requested.Distinct().Count())
        {
            throw new ArgumentOutOfRangeException(
                nameof(relationships),
                "Every relationship must be a supported semantic relationship kind.");
        }
        var options = evaluationOptions ?? new ProjectGraphEvaluationOptions();
        using var session = new SemanticQuerySession(
            options,
            _graphEvaluator,
            _variantResolver);
        using var resolution = await _targetResolver.ResolveAsync(
                target,
                traversal,
                declarationScope,
                options,
                session,
                cancellationToken)
            .ConfigureAwait(false);

        RoslynReferenceSearchResult? references = null;
        RoslynImplementationSearchResult? implementations = null;
        RoslynCallerSearchResult? callers = null;
        RoslynCalleeSearchResult? callees = null;
        if (selected.Contains(SemanticRelationshipKind.References))
        {
            references = await _references.FindResolvedAsync(
                    target, discovery, selection, traversal, declarationScope,
                    ReferenceSearchScopeMode.Default, session, resolution, cancellationToken)
                .ConfigureAwait(false);
        }

        if (selected.Overlaps([
                SemanticRelationshipKind.Implementations,
                SemanticRelationshipKind.Overrides,
                SemanticRelationshipKind.Derived]))
        {
            implementations = await _implementations.FindResolvedAsync(
                    target, discovery, selection, traversal, declarationScope,
                    ImplementationSearchScopeMode.Default, session, resolution, cancellationToken)
                .ConfigureAwait(false);
        }

        if (selected.Contains(SemanticRelationshipKind.Callers))
        {
            callers = await _callers.FindResolvedAsync(
                    target, discovery, selection, traversal, declarationScope,
                    CallerSearchScopeMode.Default, session, resolution, cancellationToken)
                .ConfigureAwait(false);
        }

        if (selected.Contains(SemanticRelationshipKind.Callees))
        {
            callees = await _callees.FindResolvedAsync(
                    target, discovery, selection, traversal, declarationScope,
                    CalleeSearchScopeMode.Default, session, resolution, cancellationToken)
                .ConfigureAwait(false);
        }

        return new RoslynRelationshipContextResult(
            references,
            selected.Contains(SemanticRelationshipKind.Implementations)
                ? implementations
                : null,
            selected.Contains(SemanticRelationshipKind.Overrides)
                ? new RoslynOverrideSearchResult(
                    implementations ?? throw new InvalidOperationException(),
                    OverrideSearchScopeMode.Default)
                : null,
            selected.Contains(SemanticRelationshipKind.Derived)
                ? new RoslynDerivedTypeSearchResult(
                    implementations ?? throw new InvalidOperationException(),
                    DerivedTypeSearchScopeMode.Default)
                : null,
            callers,
            callees);
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}

public sealed record RoslynRelationshipContextResult(
    RoslynReferenceSearchResult? References,
    RoslynImplementationSearchResult? Implementations,
    RoslynOverrideSearchResult? Overrides,
    RoslynDerivedTypeSearchResult? Derived,
    RoslynCallerSearchResult? Callers,
    RoslynCalleeSearchResult? Callees);
