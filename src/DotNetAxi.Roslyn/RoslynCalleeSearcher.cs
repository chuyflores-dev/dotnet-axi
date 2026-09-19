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
using Microsoft.CodeAnalysis.Operations;

namespace DotNetAxi.Roslyn;

public enum CalleeSearchScopeMode
{
    Default,
    Complete,
}

public enum CalleeSearchVariantStatus
{
    Analyzed,
    Remaining,
    Excluded,
    Failed,
}

public sealed record CalleeSearchVariant
{
    internal CalleeSearchVariant(
        string project,
        string? configuration,
        string? framework,
        CalleeSearchVariantStatus status,
        string? reason,
        string? correction)
    {
        Project = project;
        Configuration = configuration;
        Framework = framework;
        Status = status;
        Reason = reason;
        Correction = correction;
    }

    public string Project { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    public CalleeSearchVariantStatus Status { get; }

    public string? Reason { get; }

    public string? Correction { get; }
}

public sealed record RoslynCalleeMatch
{
    internal RoslynCalleeMatch(
        string id,
        string targetIdentity,
        string project,
        string? configuration,
        string? framework,
        SourceLocation start,
        SourceLocation end,
        string containingSymbol,
        string relationship,
        string resolution,
        string confidence)
    {
        Id = id;
        TargetIdentity = targetIdentity;
        Project = project;
        Configuration = configuration;
        Framework = framework;
        Start = start;
        End = end;
        ContainingSymbol = containingSymbol;
        Relationship = relationship;
        Resolution = resolution;
        Confidence = confidence;
    }

    public string Id { get; }

    public string TargetIdentity { get; }

    public string Project { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    public SourceLocation Start { get; }

    public SourceLocation End { get; }

    public string ContainingSymbol { get; }
    public string Relationship { get; }
    public string Resolution { get; }
    public string Confidence { get; }
}

public sealed class RoslynCalleeSearchResult
{
    internal RoslynCalleeSearchResult(
        string target,
        string? targetId,
        SemanticTargetResolutionStatus targetStatus,
        string? snapshot,
        CalleeSearchScopeMode scopeMode,
        IEnumerable<RoslynCalleeMatch>? matches,
        EvidenceCoverage coverage,
        IEnumerable<CalleeSearchVariant>? variants,
        IEnumerable<SymbolDeclarationMatch>? candidates,
        int candidateTotal,
        string? errorCode,
        string? correction,
        IEnumerable<string>? partialReasons)
    {
        Target = target;
        TargetId = targetId;
        TargetStatus = targetStatus;
        Snapshot = snapshot;
        ScopeMode = scopeMode;
        Matches = Array.AsReadOnly(matches?.ToArray() ?? []);
        Coverage = coverage;
        Variants = Array.AsReadOnly(variants?.ToArray() ?? []);
        Candidates = Array.AsReadOnly(candidates?.ToArray() ?? []);
        CandidateTotal = candidateTotal;
        ErrorCode = errorCode;
        Correction = correction;
        PartialReasons = Array.AsReadOnly(
            (partialReasons ?? [])
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    public string Target { get; }

    public string? TargetId { get; }

    public SemanticTargetResolutionStatus TargetStatus { get; }

    public string? Snapshot { get; }

    public CalleeSearchScopeMode ScopeMode { get; }

    public IReadOnlyList<RoslynCalleeMatch> Matches { get; }

    public EvidenceCoverage Coverage { get; }

    public IReadOnlyList<CalleeSearchVariant> Variants { get; }

    public IReadOnlyList<SymbolDeclarationMatch> Candidates { get; }

    public int CandidateTotal { get; }

    public int CandidateOmitted => CandidateTotal - Candidates.Count;

    public bool CandidatesTruncated => CandidateOmitted > 0;

    public string? ErrorCode { get; }

    public string? Correction { get; }

    public IReadOnlyList<string> PartialReasons { get; }

    public bool TargetResolved =>
        TargetStatus is SemanticTargetResolutionStatus.Resolved;
}

/// <summary>
/// Finds compiler-known callees in the body of one resolved member. Default
/// mode analyzes the selected project variant; complete mode includes every
/// supported framework variant for that project.
/// </summary>
public sealed class RoslynCalleeSearcher
{
    private readonly IReadOnlyList<string> _projects;
    private readonly IWorkspacePathTraverser _traverser;
    private readonly RoslynSemanticTargetResolver _targetResolver;
    private readonly MsBuildProjectGraphEvaluator _graphEvaluator;
    private readonly MsBuildCompilerVariantResolver _variantResolver;
    private readonly ProjectCoverageReporter _coverageReporter = new();

    public RoslynCalleeSearcher(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects)
        : this(
            traverser,
            ownership,
            projects,
            new DotNetHostResolver())
    {
    }

    internal RoslynCalleeSearcher(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership,
        IEnumerable<string> projects,
        IDotNetHostResolver hostResolver)
    {
        ArgumentNullException.ThrowIfNull(traverser);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(hostResolver);
        _traverser = traverser;
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
    }

    public ValueTask<RoslynCalleeSearchResult> FindAsync(
        string target,
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? declarationScope = null,
        CalleeSearchScopeMode scopeMode = CalleeSearchScopeMode.Default,
        CancellationToken cancellationToken = default)
        => FindAsync(
            target,
            discovery,
            selection,
            traversal,
            declarationScope,
            scopeMode,
            new ProjectGraphEvaluationOptions(),
            cancellationToken);

    public async ValueTask<RoslynCalleeSearchResult> FindAsync(
        string target,
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope? declarationScope,
        CalleeSearchScopeMode scopeMode,
        ProjectGraphEvaluationOptions evaluationOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(evaluationOptions);
        if (!Enum.IsDefined(scopeMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scopeMode),
                scopeMode,
                "The callee-search scope mode is not defined.");
        }

        declarationScope ??= new SymbolDeclarationScope(
            selection.Kind is WorkspaceEntryPointKind.Solution
                ? selection.Path
                : null,
            _projects,
            traversal.ExplicitPaths,
            includeTests: false,
            includeGenerated: traversal.IncludeGenerated == true);

        using var session = new SemanticQuerySession(
            evaluationOptions,
            _graphEvaluator,
            _variantResolver);
        using var resolution = await _targetResolver.ResolveAsync(
                target,
                traversal,
                declarationScope,
                evaluationOptions,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        if (!resolution.Resolved)
        {
            return TargetFailure(target, scopeMode, resolution);
        }

        if (resolution.Variants.Any(variant =>
                variant.Status is SemanticTargetVariantStatus.Resolved
                && variant.Symbol is not IMethodSymbol))
        {
            return UnsupportedMemberTarget(target, scopeMode, resolution);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var eligiblePaths = _traverser
            .Traverse(traversal, cancellationToken)
            .Select(static path => path.RelativePath)
            .ToHashSet(PathComparer());
        var graph = session.GetProjectGraph(
            discovery,
            selection,
            cancellationToken);
        var targetProjects = resolution.Variants
            .Where(static variant =>
                variant.Status is SemanticTargetVariantStatus.Resolved)
            .Select(static variant => variant.ProjectPath)
            .Distinct(PathComparer())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var report = _coverageReporter.Report(
            graph,
            scopeMode is CalleeSearchScopeMode.Complete
                ? ProjectFrameworkCoverageMode.Complete
                : ProjectFrameworkCoverageMode.Default);
        var plans = BuildPlans(
            report,
            targetProjects,
            targetProjects,
            graph);
        var compilerVariants = session.ResolveCompilerVariants(
            discovery.RootPath,
            targetProjects,
            cancellationToken);

        var matches = new List<RoslynCalleeMatch>();
        var variants = new List<CalleeSearchVariant>(plans.Count);
        var fingerprints = new List<VariantFingerprint>(plans.Count);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!plan.Analyze)
            {
                variants.Add(plan.Variant);
                fingerprints.Add(new VariantFingerprint(
                    plan.Variant.Project,
                    plan.Variant.Configuration,
                    plan.Variant.Framework,
                    "not-analyzed:" + (plan.Variant.Reason ?? "unknown")));
                continue;
            }

            var analyzed = await AnalyzeVariantAsync(
                discovery.RootPath,
                session,
                plan.Coverage!,
                resolution.Variants,
                CompilerVariant(compilerVariants, plan.Coverage!),
                eligiblePaths,
                cancellationToken)
                .ConfigureAwait(false);
            variants.Add(analyzed.Variant);
            matches.AddRange(analyzed.Matches);
            fingerprints.Add(new VariantFingerprint(
                analyzed.Variant.Project,
                analyzed.Variant.Configuration,
                analyzed.Variant.Framework,
                analyzed.SemanticFingerprint));
        }

        var orderedMatches = matches
            .DistinctBy(static match => match.Id, StringComparer.Ordinal)
            .OrderBy(static match => match.Start.Path, StringComparer.Ordinal)
            .ThenBy(static match => match.Start.Line)
            .ThenBy(static match => match.Start.Column)
            .ThenBy(static match => match.Project, StringComparer.Ordinal)
            .ThenBy(static match => match.Framework, StringComparer.Ordinal)
            .ToArray();
        var orderedVariants = variants
            .OrderBy(static variant => variant.Project, StringComparer.Ordinal)
            .ThenBy(static variant => variant.Configuration, StringComparer.Ordinal)
            .ThenBy(static variant => variant.Framework, StringComparer.Ordinal)
            .ToArray();
        var coverage = Coverage(graph, resolution, orderedVariants);
        var partialReasons = PartialReasons(
            graph,
            resolution,
            orderedVariants);
        return new RoslynCalleeSearchResult(
            target,
            resolution.CanonicalId,
            SemanticTargetResolutionStatus.Resolved,
            Snapshot(
                resolution.Snapshot!,
                graph,
                orderedVariants,
                fingerprints,
                orderedMatches),
            scopeMode,
            orderedMatches,
            coverage,
            orderedVariants,
            candidates: null,
            candidateTotal: 0,
            errorCode: null,
            correction: null,
            partialReasons);
    }

    private static RoslynCalleeSearchResult TargetFailure(
        string target,
        CalleeSearchScopeMode scopeMode,
        SemanticTargetResolution resolution) =>
        new(
            target,
            targetId: null,
            resolution.Status,
            resolution.Snapshot,
            scopeMode,
            matches: null,
            new EvidenceCoverage(CoverageLevel.NotApplicable),
            resolution.Variants.Select(static variant =>
                new CalleeSearchVariant(
                    variant.ProjectPath,
                    variant.Configuration,
                    variant.Framework,
                    CalleeSearchVariantStatus.Failed,
                    variant.Reason ?? "semantic.target_unresolved",
                    "Fix the reported project or framework failure, then retry.")),
            resolution.Candidates,
            resolution.CandidateTotal,
            resolution.ErrorCode,
            resolution.Correction,
            resolution.PartialReasons);

    private static RoslynCalleeSearchResult UnsupportedMemberTarget(
        string target,
        CalleeSearchScopeMode scopeMode,
        SemanticTargetResolution resolution) =>
        new(
            target,
            targetId: null,
            SemanticTargetResolutionStatus.Unsupported,
            resolution.Snapshot,
            scopeMode,
            matches: null,
            new EvidenceCoverage(CoverageLevel.NotApplicable),
            resolution.Variants.Select(static variant =>
                new CalleeSearchVariant(
                    variant.ProjectPath,
                    variant.Configuration,
                    variant.Framework,
                    CalleeSearchVariantStatus.Failed,
                    "semantic.target_not_member",
                    "Select a source method, constructor, operator, or accessor and retry.")),
            candidates: null,
            candidateTotal: 0,
            errorCode: "semantic.target_unsupported",
            correction: "Select a source method, constructor, operator, or accessor and retry.",
            partialReasons: ["semantic.target_not_member"]);

    private static IReadOnlyList<VariantPlan> BuildPlans(
        ProjectCoverageReport report,
        IReadOnlyCollection<string> completeProjects,
        IReadOnlyCollection<string> selectedProjects,
        EvaluatedProjectGraph graph)
    {
        var comparer = PathComparer();
        var complete = completeProjects.ToHashSet(comparer);
        var selected = selectedProjects.ToHashSet(comparer);
        var plans = report.Variants
            .Where(variant => complete.Contains(variant.Project))
            .GroupBy(static variant => variant.Project, PathComparer())
            .SelectMany(group => group.Select((variant, index) => Plan(
                variant,
                selected.Contains(variant.Project),
                fallbackSelected: index == 0,
                report.FrameworkMode)))
            .ToList();
        var coveredProjects = plans
            .Select(static plan => plan.Variant.Project)
            .ToHashSet(comparer);
        foreach (var project in complete.Where(project =>
                     !coveredProjects.Contains(project)))
        {
            var failure = graph.Projects
                .FirstOrDefault(item => comparer.Equals(item.Path, project))
                ?.Failures.FirstOrDefault();
            plans.Add(new VariantPlan(
                Coverage: null,
                Analyze: false,
                new CalleeSearchVariant(
                    project,
                    configuration: null,
                    framework: null,
                    CalleeSearchVariantStatus.Failed,
                    failure is null
                        ? "project.coverage_unavailable"
                        : FailureReason(failure.Reason),
                    "Repair project evaluation, then retry `dnaxi search callees`.")));
        }

        return Array.AsReadOnly(plans.ToArray());
    }

    private static EvaluatedCompilerVariant? CompilerVariant(
        CompilerVariantResolution resolution,
        ProjectVariantCoverage coverage) =>
        resolution.Variants.FirstOrDefault(variant =>
            PathComparer().Equals(
                variant.Variant.Project,
                coverage.Project)
            && string.Equals(
                variant.Variant.Configuration,
                coverage.Configuration,
                StringComparison.Ordinal)
            && string.Equals(
                variant.Variant.Framework,
                coverage.Framework,
                StringComparison.Ordinal));

    private static VariantPlan Plan(
        ProjectVariantCoverage coverage,
        bool projectSelected,
        bool fallbackSelected,
        ProjectFrameworkCoverageMode frameworkMode)
    {
        var missingAssetsOnly =
            coverage.State is ProjectVariantCoverageState.Unrestored
            && coverage.Issues.Any(static issue =>
                issue.Reason is ProjectCoverageIssueReason.MissingAssets)
            && coverage.Issues.All(static issue =>
                issue.Reason is ProjectCoverageIssueReason.MissingAssets
                    or ProjectCoverageIssueReason.FrameworkNotSelected);
        if (coverage.State is ProjectVariantCoverageState.Supported
            || missingAssetsOnly)
        {
            var frameworkSelected = coverage.IsSelected
                || missingAssetsOnly
                && (frameworkMode is ProjectFrameworkCoverageMode.Complete
                    || fallbackSelected);
            if (projectSelected && frameworkSelected)
            {
                return new VariantPlan(
                    coverage,
                    Analyze: true,
                    new CalleeSearchVariant(
                        coverage.Project,
                        coverage.Configuration,
                        coverage.Framework,
                        CalleeSearchVariantStatus.Analyzed,
                        reason: null,
                        correction: null));
            }

            return new VariantPlan(
                coverage,
                Analyze: false,
                new CalleeSearchVariant(
                    coverage.Project,
                    coverage.Configuration,
                    coverage.Framework,
                    CalleeSearchVariantStatus.Remaining,
                    projectSelected
                        ? "framework.not_selected"
                        : "project.not_selected",
                    "Retry with `dnaxi search callees <symbol> --complete`."));
        }

        var issue = coverage.Issues.FirstOrDefault();
        var status = coverage.State is ProjectVariantCoverageState.Unsupported
            ? CalleeSearchVariantStatus.Excluded
            : CalleeSearchVariantStatus.Failed;
        return new VariantPlan(
            coverage,
            Analyze: false,
            new CalleeSearchVariant(
                coverage.Project,
                coverage.Configuration,
                coverage.Framework,
                status,
                issue is null
                    ? "project.unsupported"
                    : CoverageReason(issue.Reason),
                issue?.Correction
                    ?? "Repair the project or framework, then retry the callee search."));
    }

    private static async ValueTask<AnalyzedVariant> AnalyzeVariantAsync(
        string workspaceRoot,
        SemanticQuerySession session,
        ProjectVariantCoverage coverage,
        IReadOnlyList<SemanticTargetVariant> targetVariants,
        EvaluatedCompilerVariant? compilerVariant,
        IReadOnlySet<string> eligiblePaths,
        CancellationToken cancellationToken)
    {
        var targets = TargetDescriptors(targetVariants, coverage.Framework);
        if (targets.Count == 0)
        {
            return Failed(coverage, "semantic.target_not_in_framework");
        }

        var retained = targetVariants.FirstOrDefault(variant =>
            variant.Status is SemanticTargetVariantStatus.Resolved
            && variant.Project is not null
            && variant.Symbol is not null
            && PathComparer().Equals(variant.ProjectPath, coverage.Project)
            && string.Equals(
                variant.Configuration,
                coverage.Configuration,
                StringComparison.Ordinal)
            && string.Equals(
                variant.Framework,
                coverage.Framework,
                StringComparison.Ordinal));
        if (retained is not null)
        {
            var retainedSymbols = ExactTargetSymbols(
                retained.Symbol!,
                retained.Compilation!.Assembly.Identity.ToString());
            var retainedMatches = await FindMatchesAsync(
                    workspaceRoot,
                    coverage,
                    retained.Project!,
                    retainedSymbols.Select(symbol =>
                        (retained.Identity!, symbol)),
                    eligiblePaths,
                    cancellationToken)
                .ConfigureAwait(false);
            return Analyzed(
                coverage,
                retainedMatches,
                await SemanticFingerprintAsync(
                        workspaceRoot,
                        retained.Project!,
                        retained.Compilation!,
                        retained.Variant.ContextFingerprint,
                        cancellationToken)
                    .ConfigureAwait(false));
        }

        try
        {
            var contextVariant = compilerVariant?.Variant
                ?? new FileCompilerVariant(
                    coverage.Project,
                    coverage.Configuration,
                    coverage.Framework,
                    contextFingerprint: coverage.Project);
            var context = await session.GetCompilerContextAsync(
                    workspaceRoot,
                    contextVariant,
                    RoslynCompilerContextPurpose.Relationship,
                    cancellationToken)
                .ConfigureAwait(false);
            if (context.FailureReason is not null)
            {
                return Failed(coverage, context.FailureReason);
            }

            var diagnosticReason = context.DiagnosticReason(
                RoslynCompilerContextPurpose.Relationship);
            if (diagnosticReason is not null)
            {
                return Failed(coverage, diagnosticReason);
            }

            var project = context.Project!;
            var compilation = context.Compilation!;
            var symbols = targets
                .SelectMany(target => DocumentationCommentId
                    .GetSymbolsForDeclarationId(target.Identity, compilation)
                    .SelectMany(symbol => ExactTargetSymbols(
                        symbol,
                        target.AssemblyIdentity))
                    .Select(symbol => (
                        target.Identity,
                        Symbol: symbol)))
                .GroupBy(
                    static item => item.Symbol!,
                    SymbolEqualityComparer.Default)
                .Select(static group => group.First())
                .ToArray();
            var matches = await FindMatchesAsync(
                    workspaceRoot,
                    coverage,
                    project,
                    symbols.Select(static item =>
                        (item.Identity, item.Symbol!)),
                    eligiblePaths,
                    cancellationToken)
                .ConfigureAwait(false);
            return Analyzed(
                coverage,
                matches,
                await SemanticFingerprintAsync(
                        workspaceRoot,
                        project,
                        compilation,
                        compilerVariant?.Variant.ContextFingerprint
                            ?? coverage.Project,
                        cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            return Failed(coverage, "project.load_failed");
        }
    }

    private static async ValueTask<IReadOnlyList<RoslynCalleeMatch>>
        FindMatchesAsync(
            string workspaceRoot,
            ProjectVariantCoverage coverage,
            Project project,
            IEnumerable<(string Identity, ISymbol Symbol)> symbols,
            IReadOnlySet<string> eligiblePaths,
            CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false);
        if (compilation is null)
        {
            return Array.Empty<RoslynCalleeMatch>();
        }

        var matches = new List<RoslynCalleeMatch>();
        foreach (var (sourceIdentity, symbol) in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var declaration in symbol.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var syntax = declaration.GetSyntax(cancellationToken);
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                var operation = model.GetOperation(syntax, cancellationToken);
                if (operation is null)
                {
                    continue;
                }

                var collector = new CalleeOperationCollector();
                collector.Visit(operation);
                foreach (var candidate in collector.Candidates)
                {
                    var match = Match(
                        workspaceRoot,
                        coverage,
                        candidate.Identity,
                        candidate.Location,
                        sourceIdentity,
                        candidate.Relationship,
                        candidate.Confidence);
                    if (eligiblePaths.Contains(match.Start.Path))
                    {
                        matches.Add(match);
                    }
                }
            }
        }

        return Array.AsReadOnly(matches.ToArray());
    }

    private static AnalyzedVariant Analyzed(
        ProjectVariantCoverage coverage,
        IReadOnlyList<RoslynCalleeMatch> matches,
        string semanticFingerprint) =>
        new(
            new CalleeSearchVariant(
                coverage.Project,
                coverage.Configuration,
                coverage.Framework,
                CalleeSearchVariantStatus.Analyzed,
                reason: null,
                correction: null),
            matches,
            semanticFingerprint);

    private static IReadOnlyList<TargetDescriptor> TargetDescriptors(
        IReadOnlyList<SemanticTargetVariant> variants,
        string? framework)
    {
        return Array.AsReadOnly(variants
            .Where(variant =>
                variant.Status is SemanticTargetVariantStatus.Resolved
                && string.Equals(
                    variant.Framework,
                    framework,
                    StringComparison.Ordinal)
                && variant.Identity is not null
                && variant.Symbol is not null
                && variant.Compilation is not null)
            .Select(static variant => new TargetDescriptor(
                variant.Identity!,
                variant.Compilation!.Assembly.Identity.ToString()))
            .Distinct()
            .OrderBy(static target => target.Identity, StringComparer.Ordinal)
            .ThenBy(
                static target => target.AssemblyIdentity,
                StringComparer.Ordinal)
            .ToArray());
    }

    private static IReadOnlyList<ISymbol> ExactTargetSymbols(
        ISymbol symbol,
        string assemblyIdentity)
    {
        if (symbol is INamespaceSymbol namespaceSymbol
            && namespaceSymbol.ContainingAssembly is null
            && namespaceSymbol.ConstituentNamespaces.Any(candidate =>
                string.Equals(
                    candidate.ContainingAssembly?.Identity.ToString(),
                    assemblyIdentity,
                    StringComparison.Ordinal)))
        {
            return [symbol];
        }

        IEnumerable<ISymbol> candidates = symbol is INamespaceSymbol value
            ? value.ConstituentNamespaces
            : [symbol];
        return Array.AsReadOnly(candidates
            .Where(candidate => string.Equals(
                candidate.ContainingAssembly?.Identity.ToString(),
                assemblyIdentity,
                StringComparison.Ordinal))
            .Distinct(SymbolEqualityComparer.Default)
            .ToArray());
    }

    private static RoslynCalleeMatch Match(
        string workspaceRoot,
        ProjectVariantCoverage coverage,
        string identity,
        Location location,
        string containingSymbol,
        string relationship,
        string confidence)
    {
        var filePath = location.SourceTree?.FilePath
            ?? throw new InvalidOperationException(
                "A source callee must have a document path.");
        var normalized = new WorkspacePathResolver(
                workspaceRoot,
                workspaceRoot)
            .NormalizeOutput(filePath);
        var span = location.GetLineSpan().Span;
        var start = SourceLocation.FromZeroBasedUtf16(
            normalized.Path,
            span.Start.Line,
            span.Start.Character,
            normalized.IsExternal);
        var end = SourceLocation.FromZeroBasedUtf16(
            normalized.Path,
            span.End.Line,
            span.End.Character,
            normalized.IsExternal);
        var id = CalleeId(
            identity,
            coverage,
            normalized.Path,
            location.SourceSpan.Start,
            location.SourceSpan.Length);
        return new RoslynCalleeMatch(
            id,
            identity,
            coverage.Project,
            coverage.Configuration,
            coverage.Framework,
            start,
            end,
            containingSymbol,
            relationship,
            "semantic",
            confidence);
    }

    private sealed class CalleeOperationCollector : OperationWalker
    {
        private readonly List<CalleeCandidate> _candidates = [];

        public IReadOnlyList<CalleeCandidate> Candidates => _candidates;

        public override void VisitInvocation(IInvocationOperation operation)
        {
            Add(operation.TargetMethod.ReducedFrom ?? operation.TargetMethod,
                operation.Syntax.GetLocation(),
                IsDispatchTarget(operation.TargetMethod)
                    ? "possible_dispatch"
                    : "direct_call");
            base.VisitInvocation(operation);
        }

        public override void VisitObjectCreation(IObjectCreationOperation operation)
        {
            if (operation.Constructor is not null)
            {
                Add(operation.Constructor, operation.Syntax.GetLocation(), "direct_call");
            }

            base.VisitObjectCreation(operation);
        }

        public override void VisitMethodReference(IMethodReferenceOperation operation)
        {
            Add(operation.Method.ReducedFrom ?? operation.Method,
                operation.Syntax.GetLocation(),
                "delegate");
            base.VisitMethodReference(operation);
        }

        public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
        {
            // Nested bodies have their own attribution and are not callees of
            // the enclosing member.
        }

        public override void VisitLocalFunction(ILocalFunctionOperation operation)
        {
            // Nested bodies have their own attribution and are not callees of
            // the enclosing member.
        }

        private void Add(IMethodSymbol method, Location location, string relationship)
        {
            if (!location.IsInSource)
            {
                return;
            }

            var identity = method.GetDocumentationCommentId();
            if (identity is null)
            {
                return;
            }

            _candidates.Add(new CalleeCandidate(
                identity,
                location,
                relationship,
                relationship is "direct_call" ? "verified" : "possible"));
        }
    }

    private static bool IsDispatchTarget(IMethodSymbol target) =>
        !target.IsStatic
        && (target.ContainingType?.TypeKind is TypeKind.Interface
            || target.IsAbstract
            || target.IsVirtual
            || target.IsOverride);

    private static string CalleeId(
        string identity,
        ProjectVariantCoverage coverage,
        string path,
        int start,
        int length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/callee/v1");
        Append(hash, identity);
        Append(hash, coverage.Project);
        Append(hash, coverage.Configuration ?? string.Empty);
        Append(hash, coverage.Framework ?? string.Empty);
        Append(hash, path);
        Append(hash, start.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return "callee/v1/" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static AnalyzedVariant Failed(
        ProjectVariantCoverage coverage,
        string reason) =>
        new(
            new CalleeSearchVariant(
                coverage.Project,
                coverage.Configuration,
                coverage.Framework,
                CalleeSearchVariantStatus.Failed,
                reason,
                Correction(reason)),
            Array.Empty<RoslynCalleeMatch>(),
            "failed:" + reason);

    private static EvidenceCoverage Coverage(
        EvaluatedProjectGraph graph,
        SemanticTargetResolution target,
        IReadOnlyCollection<CalleeSearchVariant> variants)
    {
        var analyzed = variants.Count(static variant =>
            variant.Status is CalleeSearchVariantStatus.Analyzed);
        var remaining = variants.Count(static variant =>
            variant.Status is CalleeSearchVariantStatus.Remaining);
        var excluded = variants.Count(static variant =>
            variant.Status is CalleeSearchVariantStatus.Excluded);
        var failed = variants.Count(static variant =>
            variant.Status is CalleeSearchVariantStatus.Failed);
        var partial = graph.Completeness is not ProjectGraphCompleteness.Complete
            || target.HasPartialCoverage
            || remaining + excluded + failed > 0;
        return new EvidenceCoverage(
            partial ? CoverageLevel.Partial : CoverageLevel.Complete,
            considered: variants.Count,
            analyzed,
            remaining,
            excluded,
            failed,
            partialReason: partial
                ? string.Join("; ", PartialReasons(graph, target, variants))
                : null);
    }

    private static IReadOnlyList<string> PartialReasons(
        EvaluatedProjectGraph graph,
        SemanticTargetResolution target,
        IEnumerable<CalleeSearchVariant> variants)
    {
        var reasons = new List<string>();
        if (graph.Completeness is not ProjectGraphCompleteness.Complete)
        {
            reasons.Add("project.graph_incomplete");
        }

        reasons.AddRange(target.PartialReasons);
        reasons.AddRange(variants
            .Where(static variant =>
                variant.Status is not CalleeSearchVariantStatus.Analyzed)
            .Select(static variant => variant.Reason!));
        return Array.AsReadOnly(reasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    private static string Snapshot(
        string targetSnapshot,
        EvaluatedProjectGraph graph,
        IEnumerable<CalleeSearchVariant> variants,
        IEnumerable<VariantFingerprint> fingerprints,
        IEnumerable<RoslynCalleeMatch> matches)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/callee-search-snapshot/v1");
        Append(hash, targetSnapshot);
        Append(hash, graph.Runtime?.SdkVersion ?? string.Empty);
        Append(hash, graph.Runtime?.MsBuildVersion ?? string.Empty);
        Append(hash, graph.Completeness.ToString());
        foreach (var edge in graph.Dependencies
                     .OrderBy(static edge => edge.Project, StringComparer.Ordinal)
                     .ThenBy(static edge => edge.Dependency, StringComparer.Ordinal))
        {
            Append(hash, edge.Project);
            Append(hash, edge.Dependency);
        }

        foreach (var variant in variants)
        {
            Append(hash, variant.Project);
            Append(hash, variant.Configuration ?? string.Empty);
            Append(hash, variant.Framework ?? string.Empty);
            Append(hash, variant.Status.ToString());
            Append(hash, variant.Reason ?? string.Empty);
        }

        foreach (var fingerprint in fingerprints
                     .OrderBy(
                         static item => item.Project,
                         StringComparer.Ordinal)
                     .ThenBy(
                         static item => item.Configuration,
                         StringComparer.Ordinal)
                     .ThenBy(
                         static item => item.Framework,
                         StringComparer.Ordinal))
        {
            Append(hash, fingerprint.Project);
            Append(hash, fingerprint.Configuration ?? string.Empty);
            Append(hash, fingerprint.Framework ?? string.Empty);
            Append(hash, fingerprint.Value);
        }

        foreach (var match in matches)
        {
            Append(hash, match.Id);
        }

        return "ws_" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async ValueTask<string> SemanticFingerprintAsync(
        string workspaceRoot,
        Project project,
        CSharpCompilation compilation,
        string evaluatedProjectFingerprint,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/callee-semantic-context/v1");
        Append(hash, evaluatedProjectFingerprint);
        Append(hash, compilation.Assembly.Identity.ToString());
        Append(hash, compilation.Options.OutputKind.ToString());
        Append(hash, compilation.Options.NullableContextOptions.ToString());
        Append(hash, compilation.Options.OptimizationLevel.ToString());
        Append(hash, compilation.Options.AllowUnsafe ? "unsafe" : "safe");
        Append(hash, compilation.Options.CheckOverflow
            ? "checked"
            : "unchecked");

        foreach (var tree in compilation.SyntaxTrees
                     .OrderBy(
                         static tree => tree.FilePath,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, FingerprintPath(workspaceRoot, tree.FilePath));
            Append(hash, Convert.ToHexStringLower(
                tree.GetText(cancellationToken).GetChecksum().AsSpan()));
            if (tree.Options is CSharpParseOptions options)
            {
                Append(hash, options.LanguageVersion.ToString());
                foreach (var symbol in options.PreprocessorSymbolNames
                             .Order(StringComparer.Ordinal))
                {
                    Append(hash, symbol);
                }

                foreach (var feature in options.Features
                             .OrderBy(
                                 static feature => feature.Key,
                                 StringComparer.Ordinal))
                {
                    Append(hash, feature.Key);
                    Append(hash, feature.Value);
                }
            }
        }

        foreach (var document in project.AdditionalDocuments
                     .OrderBy(
                         static document => document.FilePath ?? document.Name,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, "additional");
            Append(hash, DocumentPath(workspaceRoot, document));
            Append(hash, Convert.ToHexStringLower(
                (await document.GetTextAsync(cancellationToken)
                    .ConfigureAwait(false)).GetChecksum().AsSpan()));
        }

        foreach (var document in project.AnalyzerConfigDocuments
                     .OrderBy(
                         static document => document.FilePath ?? document.Name,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, "analyzer-config");
            Append(hash, DocumentPath(workspaceRoot, document));
            Append(hash, Convert.ToHexStringLower(
                (await document.GetTextAsync(cancellationToken)
                    .ConfigureAwait(false)).GetChecksum().AsSpan()));
        }

        foreach (var reference in compilation.References
                     .OrderBy(
                         static reference => reference.Display,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, reference.Properties.Kind.ToString());
            Append(hash, reference.Properties.EmbedInteropTypes
                ? "embedded"
                : "ordinary");
            foreach (var alias in reference.Properties.Aliases
                         .Order(StringComparer.Ordinal))
            {
                Append(hash, alias);
            }

            var display = reference.Display ?? string.Empty;
            Append(hash, FingerprintPath(workspaceRoot, display));
            if (reference is CompilationReference compilationReference)
            {
                Append(
                    hash,
                    compilationReference.Compilation.Assembly.Identity.ToString());
                foreach (var tree in compilationReference.Compilation.SyntaxTrees
                             .OrderBy(
                                 static tree => tree.FilePath,
                                 StringComparer.Ordinal))
                {
                    Append(hash, FingerprintPath(workspaceRoot, tree.FilePath));
                    Append(hash, Convert.ToHexStringLower(
                        tree.GetText(cancellationToken).GetChecksum().AsSpan()));
                }
            }
            else if (Path.IsPathFullyQualified(display))
            {
                Append(hash, await FileFingerprintAsync(
                        display,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        foreach (var reference in project.AnalyzerReferences
                     .OrderBy(
                         static reference => reference.FullPath
                             ?? reference.Display,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, reference.Id.ToString() ?? string.Empty);
            Append(hash, reference.Display ?? string.Empty);
            Append(hash, FingerprintPath(
                workspaceRoot,
                reference.FullPath ?? string.Empty));
            if (reference.FullPath is not null)
            {
                Append(hash, await FileFingerprintAsync(
                        reference.FullPath,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        foreach (var item in project.ProjectReferences
                     .Select(reference => (
                         Reference: reference,
                         Project: project.Solution.GetProject(
                             reference.ProjectId)))
                     .OrderBy(
                         static item => item.Project?.FilePath
                             ?? item.Project?.Name,
                         StringComparer.Ordinal))
        {
            var reference = item.Reference;
            Append(hash, item.Project?.FilePath is null
                ? item.Project?.AssemblyName
                    ?? item.Project?.Name
                    ?? "unresolved-project-reference"
                : FingerprintPath(workspaceRoot, item.Project.FilePath));
            Append(hash, reference.EmbedInteropTypes
                ? "embedded"
                : "ordinary");
            foreach (var alias in reference.Aliases.Order(StringComparer.Ordinal))
            {
                Append(hash, alias);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string DocumentPath(
        string workspaceRoot,
        TextDocument document) =>
        document.FilePath is null
            ? document.Name
            : FingerprintPath(workspaceRoot, document.FilePath);

    private static ValueTask<string> FileFingerprintAsync(
        string path,
        CancellationToken cancellationToken) =>
        SemanticRelationshipFileFingerprint.CreateAsync(path, cancellationToken);

    private static string FingerprintPath(string workspaceRoot, string path) =>
        Path.IsPathFullyQualified(path) && IsWithin(workspaceRoot, path)
            ? Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/')
            : Path.GetFileName(path);

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
                diagnostic.Message))
            || compilationErrors.Any(static diagnostic => diagnostic.Id is
                "CS0006" or "CS0012" or "CS0518"))
        {
            return "metadata.missing";
        }

        if (workspace.Any(static diagnostic =>
                diagnostic.Kind is WorkspaceDiagnosticKind.Failure))
        {
            return "project.load_failed";
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

    private static string CoverageReason(ProjectCoverageIssueReason reason) =>
        reason switch
        {
            ProjectCoverageIssueReason.FrameworkNotSelected =>
                "framework.not_selected",
            ProjectCoverageIssueReason.UnsupportedLanguage =>
                "project.language_unsupported",
            ProjectCoverageIssueReason.UnsupportedProjectShape =>
                "project.shape_unsupported",
            ProjectCoverageIssueReason.MissingAssets => "metadata.missing",
            ProjectCoverageIssueReason.CircularDependency =>
                "project.circular_dependency",
            ProjectCoverageIssueReason.ProjectNotFound => "project.not_found",
            ProjectCoverageIssueReason.ImportNotFound =>
                "project.import_not_found",
            ProjectCoverageIssueReason.SdkNotFound => "sdk.not_found",
            ProjectCoverageIssueReason.InvalidProjectFile => "project.invalid",
            ProjectCoverageIssueReason.EvaluationAborted =>
                "project.evaluation_aborted",
            ProjectCoverageIssueReason.EvaluationFailed =>
                "project.evaluation_failed",
            ProjectCoverageIssueReason.MsBuildUnavailable =>
                "msbuild.unavailable",
            ProjectCoverageIssueReason.MsBuildIncompatible =>
                "msbuild.incompatible",
            ProjectCoverageIssueReason.WorkspacePathEscape =>
                "project.path_escape",
            ProjectCoverageIssueReason.InvalidAssetsFile =>
                "metadata.invalid_assets",
            _ => "project.evaluation_failed",
        };

    private static string FailureReason(ProjectEvaluationFailureReason reason) =>
        reason switch
        {
            ProjectEvaluationFailureReason.MissingAssets => "metadata.missing",
            ProjectEvaluationFailureReason.CircularDependency =>
                "project.circular_dependency",
            ProjectEvaluationFailureReason.ProjectNotFound => "project.not_found",
            ProjectEvaluationFailureReason.ImportNotFound => "project.import_not_found",
            ProjectEvaluationFailureReason.SdkNotFound => "sdk.not_found",
            ProjectEvaluationFailureReason.InvalidProjectFile => "project.invalid",
            ProjectEvaluationFailureReason.EvaluationAborted =>
                "project.evaluation_aborted",
            ProjectEvaluationFailureReason.EvaluationFailed =>
                "project.evaluation_failed",
            ProjectEvaluationFailureReason.MsBuildUnavailable => "msbuild.unavailable",
            ProjectEvaluationFailureReason.MsBuildIncompatible =>
                "msbuild.incompatible",
            ProjectEvaluationFailureReason.WorkspacePathEscape =>
                "project.path_escape",
            ProjectEvaluationFailureReason.InvalidAssetsFile =>
                "metadata.invalid_assets",
            _ => "project.evaluation_failed",
        };

    private static string Correction(string reason) =>
        reason switch
        {
            "metadata.missing" =>
                "Run `dnaxi restore`, repair missing callees, then retry the callee search.",
            "project.compilation_errors" =>
                "Fix compiler errors in the reported project and framework, then retry.",
            "project.path_escape" =>
                "Select a project inside the workspace root.",
            _ =>
                "Repair the reported project or framework, then retry the callee search.",
        };

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

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record VariantPlan(
        ProjectVariantCoverage? Coverage,
        bool Analyze,
        CalleeSearchVariant Variant);

    private sealed record AnalyzedVariant(
        CalleeSearchVariant Variant,
        IReadOnlyList<RoslynCalleeMatch> Matches,
        string SemanticFingerprint);

    private sealed record TargetDescriptor(
        string Identity,
        string AssemblyIdentity);

    private sealed record VariantFingerprint(
        string Project,
        string? Configuration,
        string? Framework,
        string Value);

    private sealed record CalleeCandidate(
        string Identity,
        Location Location,
        string Relationship,
        string Confidence);
}
