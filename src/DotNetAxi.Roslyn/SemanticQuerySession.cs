using DotNetAxi.Contracts;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn;

internal sealed class SemanticQuerySession
{
    private static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly ProjectGraphEvaluationOptions _options;
    private readonly Func<
        WorkspaceDiscoveryResult,
        WorkspaceSelection,
        ProjectGraphEvaluationOptions,
        CancellationToken,
        EvaluatedProjectGraph> _evaluateGraph;
    private readonly Func<
        string,
        IEnumerable<string>,
        ProjectGraphEvaluationOptions,
        CancellationToken,
        CompilerVariantResolution> _resolveVariants;
    private readonly Dictionary<string, VariantResolutionState>
        _variantStatesByRoot = new(PathComparer);

    private EvaluatedProjectGraph? _graph;
    private bool _graphEvaluated;

    internal SemanticQuerySession(
        ProjectGraphEvaluationOptions options,
        MsBuildProjectGraphEvaluator graphEvaluator,
        MsBuildCompilerVariantResolver variantResolver)
        : this(
            options,
            graphEvaluator.Evaluate,
            variantResolver.Resolve)
    {
    }

    internal SemanticQuerySession(
        ProjectGraphEvaluationOptions options,
        Func<
            WorkspaceDiscoveryResult,
            WorkspaceSelection,
            ProjectGraphEvaluationOptions,
            CancellationToken,
            EvaluatedProjectGraph> evaluateGraph,
        Func<
            string,
            IEnumerable<string>,
            ProjectGraphEvaluationOptions,
            CancellationToken,
            CompilerVariantResolution> resolveVariants)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(evaluateGraph);
        ArgumentNullException.ThrowIfNull(resolveVariants);

        _options = options;
        _evaluateGraph = evaluateGraph;
        _resolveVariants = resolveVariants;
    }

    internal EvaluatedProjectGraph GetProjectGraph(
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        CancellationToken cancellationToken)
    {
        if (_graphEvaluated)
        {
            return _graph!;
        }

        _graph = _evaluateGraph(
            discovery,
            selection,
            _options,
            cancellationToken);
        _graphEvaluated = true;
        return _graph;
    }

    internal CompilerVariantResolution ResolveCompilerVariants(
        string workspaceRoot,
        IEnumerable<string> projects,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(projects);

        var root = Path.GetFullPath(workspaceRoot);
        if (!_variantStatesByRoot.TryGetValue(root, out var state))
        {
            state = new VariantResolutionState();
            _variantStatesByRoot.Add(root, state);
        }

        var requested = projects
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (state.ResolutionFailed)
        {
            return new CompilerVariantResolution(
                Runtime: null,
                state.FailureReason,
                Array.Empty<EvaluatedCompilerVariant>());
        }

        var missing = requested
            .Where(project => !state.VariantsByProject.ContainsKey(project))
            .ToArray();

        if (missing.Length > 0)
        {
            var resolved = _resolveVariants(
                root,
                missing,
                _options,
                cancellationToken);
            if (!resolved.IsAvailable)
            {
                state.ResolutionFailed = true;
                state.FailureReason = resolved.FailureReason;
                return resolved;
            }

            state.Runtime ??= resolved.Runtime;
            foreach (var project in missing)
            {
                state.VariantsByProject.Add(
                    project,
                    Array.AsReadOnly(resolved.Variants
                        .Where(variant => string.Equals(
                            variant.Variant.Project,
                            project,
                            StringComparison.Ordinal))
                        .ToArray()));
            }
        }

        return new CompilerVariantResolution(
            state.Runtime,
            FailureReason: null,
            Array.AsReadOnly(requested
                .SelectMany(project => state.VariantsByProject[project])
                .OrderBy(
                    static variant => variant.Variant.Project,
                    StringComparer.Ordinal)
                .ThenBy(
                    static variant => variant.Variant.Configuration,
                    StringComparer.Ordinal)
                .ThenBy(
                    static variant => variant.Variant.Framework,
                    StringComparer.Ordinal)
                .ToArray()));
    }

    private sealed class VariantResolutionState
    {
        internal Dictionary<string, IReadOnlyList<EvaluatedCompilerVariant>>
            VariantsByProject { get; } = new(StringComparer.Ordinal);

        internal MsBuildRuntimeIdentity? Runtime { get; set; }

        internal string? FailureReason { get; set; }

        internal bool ResolutionFailed { get; set; }
    }
}
