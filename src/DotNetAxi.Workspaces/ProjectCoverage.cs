using DotNetAxi.Contracts;

namespace DotNetAxi.Workspaces;

public enum ProjectFrameworkCoverageMode
{
    Default,
    Complete,
}

public enum ProjectVariantCoverageState
{
    Supported,
    Unsupported,
    Broken,
    Unrestored,
}

public enum ProjectCoverageIssueReason
{
    FrameworkNotSelected,
    UnsupportedLanguage,
    UnsupportedProjectShape,
    MissingAssets,
    CircularDependency,
    ProjectNotFound,
    ImportNotFound,
    SdkNotFound,
    InvalidProjectFile,
    EvaluationAborted,
    EvaluationFailed,
    MsBuildUnavailable,
    MsBuildIncompatible,
    WorkspacePathEscape,
    InvalidAssetsFile,
}

public sealed record ProjectCoverageIssue
{
    internal ProjectCoverageIssue(
        ProjectCoverageIssueReason reason,
        string correction,
        string? authorityCode = null)
    {
        Reason = reason;
        Correction = correction;
        AuthorityCode = authorityCode;
    }

    public ProjectCoverageIssueReason Reason { get; }

    public string Correction { get; }

    public string? AuthorityCode { get; }
}

public sealed class ProjectVariantCoverage
{
    internal ProjectVariantCoverage(
        string project,
        string? configuration,
        string? framework,
        bool? isMultiTargeted,
        bool isSelected,
        ProjectVariantCoverageState state,
        IEnumerable<ProjectCoverageIssue> issues)
    {
        Project = project;
        Configuration = configuration;
        Framework = framework;
        IsMultiTargeted = isMultiTargeted;
        IsSelected = isSelected;
        State = state;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public string Project { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    public bool? IsMultiTargeted { get; }

    public bool IsSelected { get; }

    public ProjectVariantCoverageState State { get; }

    public IReadOnlyList<ProjectCoverageIssue> Issues { get; }
}

public sealed class ProjectCoverageReport
{
    internal ProjectCoverageReport(
        ProjectFrameworkCoverageMode frameworkMode,
        EvidenceCoverage coverage,
        IEnumerable<ProjectVariantCoverage> variants)
    {
        FrameworkMode = frameworkMode;
        Coverage = coverage;
        Variants = Array.AsReadOnly(variants.ToArray());
    }

    public ProjectFrameworkCoverageMode FrameworkMode { get; }

    public EvidenceCoverage Coverage { get; }

    public IReadOnlyList<ProjectVariantCoverage> Variants { get; }
}

internal sealed class EvaluatedProjectVariantEvidence
{
    internal EvaluatedProjectVariantEvidence(
        string project,
        string? configuration,
        string? framework,
        IEnumerable<string> declaredFrameworks,
        string? language,
        bool? isSdkStyle,
        bool isOuterBuild,
        EvaluatedProjectState state,
        IEnumerable<ProjectEvaluationFailure> failures)
    {
        Project = project;
        Configuration = configuration;
        Framework = framework;
        DeclaredFrameworks = Array.AsReadOnly(
            declaredFrameworks.ToArray());
        Language = language;
        IsSdkStyle = isSdkStyle;
        IsOuterBuild = isOuterBuild;
        State = state;
        Failures = Array.AsReadOnly(failures.ToArray());
    }

    internal string Project { get; }

    internal string? Configuration { get; }

    internal string? Framework { get; }

    internal IReadOnlyList<string> DeclaredFrameworks { get; }

    internal string? Language { get; }

    internal bool? IsSdkStyle { get; }

    internal bool IsOuterBuild { get; }

    internal EvaluatedProjectState State { get; }

    internal IReadOnlyList<ProjectEvaluationFailure> Failures { get; }
}

public sealed class ProjectCoverageReporter
{
    public ProjectCoverageReport Report(
        EvaluatedProjectGraph graph,
        ProjectFrameworkCoverageMode frameworkMode =
            ProjectFrameworkCoverageMode.Default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!Enum.IsDefined(frameworkMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameworkMode),
                frameworkMode,
                "The project framework coverage mode is not defined.");
        }

        var frameworkConstrained = graph.GlobalProperties.Any(
            static property => property.Name.Equals(
                "TargetFramework",
                StringComparison.OrdinalIgnoreCase));
        var evidenceByProject = graph.CoverageEvidence
            .GroupBy(static evidence => evidence.Project, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.Ordinal);
        var variants = new List<ProjectVariantCoverage>();
        foreach (var project in graph.Projects.OrderBy(
                     static project => project.Path,
                     StringComparer.Ordinal))
        {
            if (!evidenceByProject.TryGetValue(
                    project.Path,
                    out var projectEvidence))
            {
                projectEvidence = [FallbackEvidence(project)];
            }

            variants.AddRange(ProjectVariants(
                projectEvidence,
                graph.Failures,
                frameworkMode,
                frameworkConstrained));
        }

        var analyzed = variants.Count(
            static variant => variant.IsSelected
                              && variant.State
                              is ProjectVariantCoverageState.Supported);
        var remaining = variants.Count(
            static variant => !variant.IsSelected
                              && variant.State
                              is ProjectVariantCoverageState.Supported);
        var excluded = variants.Count(
            static variant => variant.State
                              is ProjectVariantCoverageState.Unsupported);
        var failed = variants.Count(
            static variant => variant.State
                              is ProjectVariantCoverageState.Broken
                                  or ProjectVariantCoverageState.Unrestored);
        var incomplete = graph.Completeness
                         is not ProjectGraphCompleteness.Complete
                         || remaining + excluded + failed > 0;
        var coverage = new EvidenceCoverage(
            incomplete ? CoverageLevel.Partial : CoverageLevel.Complete,
            considered: variants.Count,
            analyzed: analyzed,
            remaining: remaining,
            excluded: excluded,
            failed: failed,
            partialReason: incomplete
                ? "Project or framework coverage is incomplete; inspect variant issues and graph failures."
                : null);

        return new ProjectCoverageReport(frameworkMode, coverage, variants);
    }

    private static IEnumerable<ProjectVariantCoverage> ProjectVariants(
        IReadOnlyList<EvaluatedProjectVariantEvidence> evidence,
        IReadOnlyList<ProjectEvaluationFailure> graphFailures,
        ProjectFrameworkCoverageMode frameworkMode,
        bool frameworkConstrained)
    {
        var declaredFrameworks = DeclaredFrameworks(evidence);
        var actualEvidence = evidence
            .Where(static item => !item.IsOuterBuild)
            .ToArray();
        var expandOuterBuild = !frameworkConstrained
                               && evidence.Any(
                                   static item => item.IsOuterBuild);
        var frameworks = OrderedFrameworks(
            actualEvidence,
            declaredFrameworks,
            expandOuterBuild);
        bool? isMultiTargeted = evidence.All(
            static item => item.IsSdkStyle is null)
            ? null
            : declaredFrameworks.Count > 1;
        var classified = frameworks
            .Select(framework => Classify(
                EvidenceForFramework(
                    evidence,
                    actualEvidence,
                    graphFailures,
                    framework),
                framework,
                isMultiTargeted))
            .ToArray();
        var defaultSelection = frameworkMode
            is ProjectFrameworkCoverageMode.Default
            ? Array.FindIndex(
                classified,
                static variant => variant.State
                                  is ProjectVariantCoverageState.Supported)
            : -1;

        for (var index = 0; index < classified.Length; index++)
        {
            var variant = classified[index];
            var selected = variant.State
                           is ProjectVariantCoverageState.Supported
                           && (frameworkMode
                               is ProjectFrameworkCoverageMode.Complete
                               || index == defaultSelection);
            yield return new ProjectVariantCoverage(
                variant.Evidence.Project,
                variant.Evidence.Configuration,
                variant.Framework,
                variant.IsMultiTargeted,
                selected,
                variant.State,
                Issues(variant, selected));
        }
    }

    private static ClassifiedVariant Classify(
        EvaluatedProjectVariantEvidence evidence,
        string? framework,
        bool? isMultiTargeted)
    {
        if (evidence.IsSdkStyle is false)
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Unsupported,
                ProjectCoverageIssueReason.UnsupportedProjectShape);
        }

        if (evidence.Language is not null
            && !string.Equals(
                evidence.Language,
                "C#",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Unsupported,
                ProjectCoverageIssueReason.UnsupportedLanguage);
        }

        if (evidence.IsSdkStyle is true
            && !Path.GetExtension(evidence.Project).Equals(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Unsupported,
                ProjectCoverageIssueReason.UnsupportedProjectShape);
        }

        var onlyMissingAssets = evidence.Failures.Count > 0
                                && evidence.Failures.All(
                                    static failure => failure.Reason
                                        is ProjectEvaluationFailureReason.MissingAssets);
        if (onlyMissingAssets
            && evidence.State is not EvaluatedProjectState.Failed)
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Unrestored,
                UnsupportedReason: null);
        }

        if (evidence.State is not EvaluatedProjectState.Evaluated
            || evidence.Failures.Count > 0)
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Broken,
                UnsupportedReason: null);
        }

        if (evidence.IsSdkStyle is not true)
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Unsupported,
                ProjectCoverageIssueReason.UnsupportedProjectShape);
        }

        if (!string.Equals(
                evidence.Language,
                "C#",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Unsupported,
                ProjectCoverageIssueReason.UnsupportedLanguage);
        }

        if (!Path.GetExtension(evidence.Project).Equals(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedVariant(
                evidence,
                framework,
                isMultiTargeted,
                ProjectVariantCoverageState.Unsupported,
                ProjectCoverageIssueReason.UnsupportedProjectShape);
        }

        return new ClassifiedVariant(
            evidence,
            framework,
            isMultiTargeted,
            ProjectVariantCoverageState.Supported,
            UnsupportedReason: null);
    }

    private static IEnumerable<ProjectCoverageIssue> Issues(
        ClassifiedVariant variant,
        bool selected)
    {
        if (variant.State is ProjectVariantCoverageState.Supported)
        {
            return selected
                ? []
                : [Issue(ProjectCoverageIssueReason.FrameworkNotSelected)];
        }

        if (variant.UnsupportedReason is not null)
        {
            return new[] { Issue(variant.UnsupportedReason.Value) }
                .Concat(variant.Evidence.Failures.Select(
                    static failure => Issue(
                        FailureReason(failure.Reason),
                        failure.AuthorityCode)))
                .Distinct()
                .OrderBy(static issue => issue.Reason)
                .ThenBy(
                    static issue => issue.AuthorityCode,
                    StringComparer.Ordinal);
        }

        var failures = variant.Evidence.Failures.Count == 0
            ? [new ProjectEvaluationFailure(
                ProjectEvaluationFailureReason.EvaluationFailed)]
            : variant.Evidence.Failures;
        return failures
            .Select(static failure => Issue(
                FailureReason(failure.Reason),
                failure.AuthorityCode))
            .Distinct()
            .OrderBy(static issue => issue.Reason)
            .ThenBy(
                static issue => issue.AuthorityCode,
                StringComparer.Ordinal);
    }

    private static ProjectCoverageIssue Issue(
        ProjectCoverageIssueReason reason,
        string? authorityCode = null) =>
        new(reason, Correction(reason), authorityCode);

    private static ProjectCoverageIssueReason FailureReason(
        ProjectEvaluationFailureReason reason) =>
        reason switch
        {
            ProjectEvaluationFailureReason.MissingAssets =>
                ProjectCoverageIssueReason.MissingAssets,
            ProjectEvaluationFailureReason.InvalidAssetsFile =>
                ProjectCoverageIssueReason.InvalidAssetsFile,
            ProjectEvaluationFailureReason.CircularDependency =>
                ProjectCoverageIssueReason.CircularDependency,
            ProjectEvaluationFailureReason.ProjectNotFound =>
                ProjectCoverageIssueReason.ProjectNotFound,
            ProjectEvaluationFailureReason.ImportNotFound =>
                ProjectCoverageIssueReason.ImportNotFound,
            ProjectEvaluationFailureReason.SdkNotFound =>
                ProjectCoverageIssueReason.SdkNotFound,
            ProjectEvaluationFailureReason.InvalidProjectFile =>
                ProjectCoverageIssueReason.InvalidProjectFile,
            ProjectEvaluationFailureReason.EvaluationAborted =>
                ProjectCoverageIssueReason.EvaluationAborted,
            ProjectEvaluationFailureReason.EvaluationFailed =>
                ProjectCoverageIssueReason.EvaluationFailed,
            ProjectEvaluationFailureReason.MsBuildUnavailable =>
                ProjectCoverageIssueReason.MsBuildUnavailable,
            ProjectEvaluationFailureReason.MsBuildIncompatible =>
                ProjectCoverageIssueReason.MsBuildIncompatible,
            ProjectEvaluationFailureReason.WorkspacePathEscape =>
                ProjectCoverageIssueReason.WorkspacePathEscape,
            _ => ProjectCoverageIssueReason.EvaluationFailed,
        };

    private static string Correction(ProjectCoverageIssueReason reason) =>
        reason switch
        {
            ProjectCoverageIssueReason.FrameworkNotSelected =>
                "Use `--complete` to analyze every compatible declared framework.",
            ProjectCoverageIssueReason.UnsupportedLanguage =>
                "Narrow the scope to an SDK-style C# project.",
            ProjectCoverageIssueReason.UnsupportedProjectShape =>
                "Convert the project to SDK style or narrow the scope to a supported SDK-style C# project.",
            ProjectCoverageIssueReason.MissingAssets =>
                "Run `dnaxi restore` for this project, then retry.",
            ProjectCoverageIssueReason.InvalidAssetsFile =>
                "Run `dnaxi restore` to replace the invalid assets file, then retry.",
            ProjectCoverageIssueReason.SdkNotFound
                or ProjectCoverageIssueReason.MsBuildUnavailable =>
                "Install or select the required .NET SDK, or narrow the project scope, then retry.",
            ProjectCoverageIssueReason.MsBuildIncompatible =>
                "Retry in a fresh dnaxi process using the workspace-selected SDK.",
            ProjectCoverageIssueReason.WorkspacePathEscape =>
                "Correct the linked project path or narrow the project scope, then retry.",
            _ =>
                "Correct the project or narrow the project scope, then retry.",
        };

    private static IReadOnlyList<string> DeclaredFrameworks(
        IReadOnlyList<EvaluatedProjectVariantEvidence> evidence)
    {
        var outerDeclarations = evidence
            .Where(static item => item.IsOuterBuild)
            .Select(static item => item.DeclaredFrameworks)
            .Where(static frameworks => frameworks.Count > 0)
            .ToArray();
        var declarations = outerDeclarations.Length > 0
            ? outerDeclarations
            : evidence
                .Select(static item => item.DeclaredFrameworks)
                .Where(static frameworks => frameworks.Count > 0)
                .ToArray();
        var primary = declarations
            .OrderByDescending(static frameworks => frameworks.Count)
            .ThenBy(
                static frameworks => string.Join('\u001f', frameworks),
                StringComparer.Ordinal)
            .FirstOrDefault();
        if (primary is null)
        {
            return [];
        }

        var result = primary.ToList();
        var known = result.ToHashSet(StringComparer.Ordinal);
        result.AddRange(declarations
            .SelectMany(static frameworks => frameworks)
            .Where(known.Add)
            .Order(StringComparer.Ordinal));
        return result;
    }

    private static IReadOnlyList<string?> OrderedFrameworks(
        IReadOnlyList<EvaluatedProjectVariantEvidence> actualEvidence,
        IReadOnlyList<string> declaredFrameworks,
        bool expandOuterBuild)
    {
        var actualFrameworks = actualEvidence
            .Select(static item => item.Framework)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new List<string?>();
        if (actualFrameworks.Contains(null))
        {
            result.Add(null);
        }

        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var framework in declaredFrameworks)
        {
            if ((expandOuterBuild || actualFrameworks.Contains(
                    framework,
                    StringComparer.Ordinal))
                && known.Add(framework))
            {
                result.Add(framework);
            }
        }

        result.AddRange(actualFrameworks
            .Where(static framework => framework is not null)
            .Select(static framework => framework!)
            .Where(known.Add)
            .Order(StringComparer.Ordinal));
        if (result.Count == 0)
        {
            result.Add(null);
        }

        return result;
    }

    private static EvaluatedProjectVariantEvidence EvidenceForFramework(
        IReadOnlyList<EvaluatedProjectVariantEvidence> allEvidence,
        IReadOnlyList<EvaluatedProjectVariantEvidence> actualEvidence,
        IReadOnlyList<ProjectEvaluationFailure> graphFailures,
        string? framework)
    {
        var matching = actualEvidence
            .Where(item => string.Equals(
                item.Framework,
                framework,
                StringComparison.Ordinal))
            .ToArray();
        if (matching.Length > 0)
        {
            return MergeEvidence(matching, framework);
        }

        var outer = allEvidence
            .Where(static item => item.IsOuterBuild)
            .ToArray();
        var basis = outer.Length > 0
            ? MergeEvidence(outer, framework)
            : MergeEvidence(allEvidence, framework);
        var failures = basis.Failures.Count > 0
            ? basis.Failures
            : graphFailures.Count > 0
                ? graphFailures
                : [new ProjectEvaluationFailure(
                    ProjectEvaluationFailureReason.EvaluationFailed)];
        var onlyMissingAssets = failures.All(
            static failure => failure.Reason
                is ProjectEvaluationFailureReason.MissingAssets);
        return new EvaluatedProjectVariantEvidence(
            basis.Project,
            basis.Configuration,
            framework,
            basis.DeclaredFrameworks,
            basis.Language,
            basis.IsSdkStyle,
            isOuterBuild: false,
            onlyMissingAssets
                ? EvaluatedProjectState.Incomplete
                : EvaluatedProjectState.Failed,
            failures);
    }

    private static EvaluatedProjectVariantEvidence MergeEvidence(
        IReadOnlyList<EvaluatedProjectVariantEvidence> evidence,
        string? framework)
    {
        var first = evidence[0];
        var sdkStyles = evidence
            .Select(static item => item.IsSdkStyle)
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();
        return new EvaluatedProjectVariantEvidence(
            first.Project,
            evidence
                .Select(static item => item.Configuration)
                .Where(static value => value is not null)
                .Order(StringComparer.Ordinal)
                .FirstOrDefault(),
            framework,
            DeclaredFrameworks(evidence),
            evidence
                .Select(static item => item.Language)
                .Where(static value => value is not null)
                .Order(StringComparer.Ordinal)
                .FirstOrDefault(),
            sdkStyles.Length == 0
                ? null
                : sdkStyles.All(static value => value),
            isOuterBuild: false,
            evidence.Max(static item => item.State),
            evidence
                .SelectMany(static item => item.Failures)
                .Distinct()
                .OrderBy(static failure => failure.Reason)
                .ThenBy(
                    static failure => failure.AuthorityCode,
                    StringComparer.Ordinal));
    }

    private static EvaluatedProjectVariantEvidence FallbackEvidence(
        EvaluatedProject project) =>
        new(
            project.Path,
            project.Configuration,
            project.Framework,
            project.Framework is null ? [] : [project.Framework],
            language: null,
            isSdkStyle: null,
            isOuterBuild: false,
            project.State,
            project.Failures);

    private sealed record ClassifiedVariant(
        EvaluatedProjectVariantEvidence Evidence,
        string? Framework,
        bool? IsMultiTargeted,
        ProjectVariantCoverageState State,
        ProjectCoverageIssueReason? UnsupportedReason);
}
