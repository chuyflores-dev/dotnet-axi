using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DotNetAxi.Contracts;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace DotNetAxi.Workspaces;

public enum ProjectGraphCompleteness
{
    Complete,
    Partial,
    Failed,
}

public enum EvaluatedProjectState
{
    Evaluated,
    Incomplete,
    Failed,
}

public enum ProjectEvaluationFailureReason
{
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

public enum ProjectGraphUsageErrorKind
{
    FrameworkNotDeclared,
}

public sealed class ProjectFrameworkDeclaration
{
    internal ProjectFrameworkDeclaration(
        string project,
        IEnumerable<string> frameworks)
    {
        Project = project;
        Frameworks = Array.AsReadOnly(frameworks.ToArray());
    }

    public string Project { get; }

    public IReadOnlyList<string> Frameworks { get; }
}

public sealed class ProjectGraphUsageException : InvalidOperationException
{
    internal ProjectGraphUsageException(
        ProjectGraphUsageErrorKind kind,
        string code,
        string message,
        string framework,
        IEnumerable<ProjectFrameworkDeclaration> declarations,
        string correction)
        : base(message)
    {
        Kind = kind;
        Code = code;
        Framework = framework;
        Declarations = Array.AsReadOnly(declarations.ToArray());
        Correction = correction;
    }

    public ProjectGraphUsageErrorKind Kind { get; }

    public string Code { get; }

    public string Framework { get; }

    public IReadOnlyList<ProjectFrameworkDeclaration> Declarations { get; }

    public string Correction { get; }
}

public sealed record MsBuildProperty
{
    public MsBuildProperty(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}

public sealed class ProjectGraphEvaluationOptions
{
    public ProjectGraphEvaluationOptions(
        string? configuration = null,
        string? framework = null,
        IEnumerable<MsBuildProperty>? properties = null)
    {
        Configuration = OptionalText(configuration, nameof(configuration));
        Framework = OptionalText(framework, nameof(framework));
        Properties = Array.AsReadOnly((properties ?? []).ToArray());
    }

    public string? Configuration { get; }

    public string? Framework { get; }

    public IReadOnlyList<MsBuildProperty> Properties { get; }

    private static string? OptionalText(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value cannot be empty or whitespace.",
                parameterName);
        }

        return value;
    }
}

public sealed record AppliedMsBuildProperty(string Name, string Value);

public sealed record ProjectEvaluationFailure(
    ProjectEvaluationFailureReason Reason,
    string? AuthorityCode = null);

public sealed record MsBuildRuntimeIdentity(
    string SdkVersion,
    string MsBuildVersion);

public sealed class EvaluatedProject
{
    internal EvaluatedProject(
        string path,
        bool isExternal,
        string? configuration,
        string? framework,
        EvaluatedProjectState state,
        IEnumerable<ProjectEvaluationFailure> failures)
    {
        Path = path;
        IsExternal = isExternal;
        Configuration = configuration;
        Framework = framework;
        State = state;
        Failures = Array.AsReadOnly(failures.ToArray());
    }

    public string Path { get; }

    public bool IsExternal { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    public EvaluatedProjectState State { get; }

    public IReadOnlyList<ProjectEvaluationFailure> Failures { get; }
}

public sealed record ProjectDependency(string Project, string Dependency);

public sealed class EvaluatedProjectGraph
{
    internal EvaluatedProjectGraph(
        WorkspaceSelection selection,
        ProjectGraphCompleteness completeness,
        IEnumerable<AppliedMsBuildProperty> globalProperties,
        IEnumerable<EvaluatedProject> projects,
        IEnumerable<ProjectDependency> dependencies,
        MsBuildRuntimeIdentity? runtime,
        IEnumerable<ProjectEvaluationFailure> failures,
        IEnumerable<EvaluatedProjectVariantEvidence> coverageEvidence)
    {
        Selection = selection;
        Completeness = completeness;
        GlobalProperties = Array.AsReadOnly(globalProperties.ToArray());
        Projects = Array.AsReadOnly(projects.ToArray());
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        Runtime = runtime;
        Failures = Array.AsReadOnly(failures.ToArray());
        CoverageEvidence = Array.AsReadOnly(coverageEvidence.ToArray());
    }

    public WorkspaceSelection Selection { get; }

    public ProjectGraphCompleteness Completeness { get; }

    public IReadOnlyList<AppliedMsBuildProperty> GlobalProperties { get; }

    public IReadOnlyList<EvaluatedProject> Projects { get; }

    public IReadOnlyList<ProjectDependency> Dependencies { get; }

    public MsBuildRuntimeIdentity? Runtime { get; }

    public IReadOnlyList<ProjectEvaluationFailure> Failures { get; }

    internal IReadOnlyList<EvaluatedProjectVariantEvidence> CoverageEvidence
    {
        get;
    }
}

internal sealed record ProjectInstanceGraphNode(
    string Identity,
    string ProjectPath);

internal sealed record ProjectInstanceGraphEdge(
    string ProjectIdentity,
    string DependencyIdentity);

public sealed class MsBuildProjectGraphEvaluator
{
    private readonly IMsBuildRuntimeAuthority _runtimeAuthority;
    private readonly Action<string>? _beforeGraphEvaluation;

    public MsBuildProjectGraphEvaluator(IDotNetHostResolver hostResolver)
        : this(
            new MsBuildRuntimeAuthority(new DotNetSdkSelector(hostResolver)),
            beforeGraphEvaluation: null)
    {
    }

    internal MsBuildProjectGraphEvaluator(
        IDotNetHostResolver hostResolver,
        Action<string>? beforeGraphEvaluation)
        : this(
            new MsBuildRuntimeAuthority(new DotNetSdkSelector(hostResolver)),
            beforeGraphEvaluation)
    {
    }

    internal MsBuildProjectGraphEvaluator(
        IMsBuildRuntimeAuthority runtimeAuthority)
        : this(runtimeAuthority, beforeGraphEvaluation: null)
    {
    }

    internal MsBuildProjectGraphEvaluator(
        IMsBuildRuntimeAuthority runtimeAuthority,
        Action<string>? beforeGraphEvaluation)
    {
        ArgumentNullException.ThrowIfNull(runtimeAuthority);
        _runtimeAuthority = runtimeAuthority;
        _beforeGraphEvaluation = beforeGraphEvaluation;
    }

    public EvaluatedProjectGraph Evaluate(
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        ProjectGraphEvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(selection);
        options ??= new ProjectGraphEvaluationOptions();

        var properties = BuildGlobalProperties(options);
        var authority = _runtimeAuthority.ResolveAndRegister(
            discovery.RootPath,
            cancellationToken);
        if (!authority.IsAvailable)
        {
            return UnavailableGraph(
                discovery,
                selection,
                properties,
                authority.Failure!);
        }

        return EvaluateCore(
            discovery,
            selection,
            properties,
            authority.Runtime!,
            cancellationToken);
    }

    private EvaluatedProjectGraph EvaluateCore(
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        IReadOnlyDictionary<string, string> properties,
        MsBuildRuntimeIdentity runtime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entryPath = Path.GetFullPath(
            selection.Path.Replace('/', Path.DirectorySeparatorChar),
            discovery.RootPath);
        ValidateDeclaredFramework(
            discovery.RootPath,
            selection,
            entryPath,
            properties,
            cancellationToken);
        var capturedProjects = new List<CapturedProjectInstance>();
        var attemptedProjectPaths = new List<string>();

        using var projectCollection = new ProjectCollection();
        ProjectGraph.ProjectInstanceFactoryFunc projectFactory =
            (projectPath, globalProperties, collection) =>
            {
                attemptedProjectPaths.Add(projectPath);
                AuthorizeProjectPath(discovery.RootPath, projectPath);
                var project = new ProjectInstance(
                    projectPath,
                    globalProperties,
                    toolsVersion: null,
                    collection);
                if (globalProperties is not null)
                {
                    capturedProjects.Add(new CapturedProjectInstance(
                        project,
                        ProjectInstanceIdentity(projectPath, globalProperties),
                        new ReadOnlyDictionary<string, string>(
                            new Dictionary<string, string>(
                                globalProperties,
                                StringComparer.OrdinalIgnoreCase))));
                }

                return project;
            };

        try
        {
            _beforeGraphEvaluation?.Invoke(entryPath);
            var graph = new ProjectGraph(
                [
                    new ProjectGraphEntryPoint(
                        entryPath,
                        new Dictionary<string, string>(
                            properties,
                            StringComparer.OrdinalIgnoreCase)),
                ],
                projectCollection,
                projectFactory,
                degreeOfParallelism: 1,
                cancellationToken);
            return SuccessfulGraph(
                discovery,
                selection,
                properties,
                runtime,
                graph);
        }
        catch (Exception exception)
            when (exception is InvalidProjectFileException
                or CircularDependencyException
                or AggregateException
                or IOException
                or UnauthorizedAccessException)
        {
            return FailedGraph(
                discovery,
                selection,
                properties,
                entryPath,
                capturedProjects,
                attemptedProjectPaths,
                runtime,
                Unwrap(exception));
        }
    }

    private static EvaluatedProjectGraph SuccessfulGraph(
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        IReadOnlyDictionary<string, string> properties,
        MsBuildRuntimeIdentity runtime,
        ProjectGraph graph)
    {
        var evaluatedInstances = graph.ProjectNodes
            .Where(node => !IsSelectedSolutionNode(
                selection,
                node.ProjectInstance.FullPath,
                discovery.RootPath))
            .Select(node =>
            {
                var project = CreateProject(
                    discovery.RootPath,
                    node.ProjectInstance,
                    []);
                return new EvaluatedProjectInstance(
                    project,
                    CreateCoverageEvidence(node.ProjectInstance, project));
            })
            .ToArray();
        var projects = evaluatedInstances
            .Select(static instance => instance.Project)
            .GroupBy(static project => project.Path, StringComparer.Ordinal)
            .Select(static projects => MergeProjects(projects))
            .OrderBy(static project => project.Path, StringComparer.Ordinal)
            .ToArray();
        var dependencies = graph.ProjectNodes
            .Where(node => !IsSelectedSolutionNode(
                selection,
                node.ProjectInstance.FullPath,
                discovery.RootPath))
            .SelectMany(node => node.ProjectReferences.Select(reference =>
                new ProjectDependency(
                    NormalizePath(discovery.RootPath, node.ProjectInstance.FullPath).Path,
                    NormalizePath(discovery.RootPath, reference.ProjectInstance.FullPath).Path)))
            .Distinct()
            .OrderBy(static dependency => dependency.Project, StringComparer.Ordinal)
            .ThenBy(static dependency => dependency.Dependency, StringComparer.Ordinal)
            .ToArray();
        var completeness = projects.Any(
            static project => project.State is not EvaluatedProjectState.Evaluated)
            ? ProjectGraphCompleteness.Partial
            : ProjectGraphCompleteness.Complete;

        return new EvaluatedProjectGraph(
            selection,
            completeness,
            AppliedProperties(properties),
            projects,
            dependencies,
            runtime,
            [],
            evaluatedInstances.Select(
                static instance => instance.CoverageEvidence));
    }

    private static EvaluatedProjectGraph FailedGraph(
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        IReadOnlyDictionary<string, string> properties,
        string entryPath,
        IReadOnlyList<CapturedProjectInstance> capturedProjects,
        IReadOnlyList<string> attemptedProjectPaths,
        MsBuildRuntimeIdentity runtime,
        Exception exception)
    {
        var failure = Failure(exception);
        var projectsByPath = new Dictionary<string, EvaluatedProject>(
            StringComparer.Ordinal);
        var cycleParticipants = exception is CircularDependencyException
            ? CycleParticipantIdentities(
                discovery.RootPath,
                selection,
                capturedProjects)
            : new HashSet<string>(StringComparer.Ordinal);

        var capturedEvaluations = capturedProjects
            .Where(captured => !IsSelectedSolutionNode(
                selection,
                captured.Project.FullPath,
                discovery.RootPath))
            .Select(captured =>
            {
                var project = CreateProject(
                    discovery.RootPath,
                    captured.Project,
                    cycleParticipants.Contains(captured.Identity)
                        ? [failure]
                        : []);
                return new EvaluatedProjectInstance(
                    project,
                    CreateCoverageEvidence(captured.Project, project));
            })
            .ToArray();
        var coverageEvidence = capturedEvaluations
            .Select(static instance => instance.CoverageEvidence)
            .ToList();
        var capturedProjectsByPath = capturedEvaluations
            .Select(static instance => instance.Project)
            .GroupBy(static project => project.Path, StringComparer.Ordinal)
            .Select(static projects => MergeProjects(projects));
        foreach (var evaluated in capturedProjectsByPath)
        {
            projectsByPath[evaluated.Path] = evaluated;
        }

        var failedProjectPaths = attemptedProjectPaths
            .Where(path => !IsSelectedSolutionNode(
                selection,
                path,
                discovery.RootPath))
            .Select(path => NormalizePath(discovery.RootPath, path))
            .Where(path => !projectsByPath.ContainsKey(path.Path))
            .ToArray();
        if (failedProjectPaths.Length == 0
            && projectsByPath.Count == 0)
        {
            failedProjectPaths = selection.Kind is WorkspaceEntryPointKind.Project
                ? [NormalizePath(discovery.RootPath, entryPath)]
                : [];
        }

        foreach (var failedPath in failedProjectPaths)
        {
            var failedProject = new EvaluatedProject(
                failedPath.Path,
                failedPath.IsExternal,
                properties.GetValueOrDefault("Configuration"),
                properties.GetValueOrDefault("TargetFramework"),
                EvaluatedProjectState.Failed,
                [failure]);
            projectsByPath[failedPath.Path] = failedProject;
            coverageEvidence.Add(FallbackCoverageEvidence(failedProject));
        }

        var knownSolutionProjectPaths = selection.Kind
            is WorkspaceEntryPointKind.Solution
            ? KnownSolutionProjects(entryPath)
                .Select(path => NormalizePath(discovery.RootPath, path))
            : [];
        var capturedSolutionProjectPaths = capturedProjects
            .Where(captured => IsSelectedSolutionNode(
                selection,
                captured.Project.FullPath,
                discovery.RootPath))
            .SelectMany(captured => ProjectDependencies(
                discovery.RootPath,
                captured.Project))
            .Select(dependency => NormalizeOutputPath(
                discovery.RootPath,
                dependency.Dependency));
        var solutionProjectPaths = knownSolutionProjectPaths
            .Concat(capturedSolutionProjectPaths)
            .GroupBy(static path => path.Path, StringComparer.Ordinal)
            .Select(static paths => paths.First())
            .OrderBy(static path => path.Path, StringComparer.Ordinal);
        foreach (var solutionProjectPath in solutionProjectPaths)
        {
            if (projectsByPath.ContainsKey(solutionProjectPath.Path))
            {
                continue;
            }

            var incompleteProject = new EvaluatedProject(
                solutionProjectPath.Path,
                solutionProjectPath.IsExternal,
                properties.GetValueOrDefault("Configuration"),
                properties.GetValueOrDefault("TargetFramework"),
                EvaluatedProjectState.Incomplete,
                [failure]);
            projectsByPath[solutionProjectPath.Path] = incompleteProject;
            coverageEvidence.Add(FallbackCoverageEvidence(incompleteProject));
        }

        var dependencies = capturedProjects
            .Where(captured => !IsSelectedSolutionNode(
                selection,
                captured.Project.FullPath,
                discovery.RootPath))
            .SelectMany(captured => ProjectDependencies(
                discovery.RootPath,
                captured.Project))
            .Distinct()
            .OrderBy(static dependency => dependency.Project, StringComparer.Ordinal)
            .ThenBy(static dependency => dependency.Dependency, StringComparer.Ordinal)
            .ToArray();

        return new EvaluatedProjectGraph(
            selection,
            capturedProjects.Count == 0
                ? ProjectGraphCompleteness.Failed
                : ProjectGraphCompleteness.Partial,
            AppliedProperties(properties),
            projectsByPath.Values.OrderBy(
                static project => project.Path,
                StringComparer.Ordinal),
            dependencies,
            runtime,
            [failure],
            coverageEvidence);
    }

    private static IEnumerable<ProjectDependency> ProjectDependencies(
        string workspaceRoot,
        ProjectInstance project)
    {
        var projectPath = NormalizePath(workspaceRoot, project.FullPath).Path;
        var projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        foreach (var reference in project.GetItems("ProjectReference"))
        {
            var dependencyPath = Path.GetFullPath(
                reference.EvaluatedInclude,
                projectDirectory);
            yield return new ProjectDependency(
                projectPath,
                NormalizePath(workspaceRoot, dependencyPath).Path);
        }
    }

    private static EvaluatedProject CreateProject(
        string workspaceRoot,
        ProjectInstance project,
        IEnumerable<ProjectEvaluationFailure> graphFailures)
    {
        var normalized = NormalizePath(workspaceRoot, project.FullPath);
        var failures = graphFailures.ToList();
        failures.AddRange(AssetsFailures(project));

        return new EvaluatedProject(
            normalized.Path,
            normalized.IsExternal,
            Optional(project.GetPropertyValue("Configuration")),
            Optional(project.GetPropertyValue("TargetFramework")),
            failures.Count == 0
                ? EvaluatedProjectState.Evaluated
                : EvaluatedProjectState.Incomplete,
            failures
                .Distinct()
                .OrderBy(static failure => failure.Reason)
                .ThenBy(
                    static failure => failure.AuthorityCode,
                    StringComparer.Ordinal));
    }

    private static EvaluatedProjectVariantEvidence CreateCoverageEvidence(
        ProjectInstance projectInstance,
        EvaluatedProject project) =>
        new(
            project.Path,
            project.Configuration,
            project.Framework,
            DeclaredFrameworks(projectInstance),
            Optional(projectInstance.GetPropertyValue("Language")),
            projectInstance.GetPropertyValue("UsingMicrosoftNETSdk").Equals(
                "true",
                StringComparison.OrdinalIgnoreCase),
            ProjectType(projectInstance) is CapturedProjectType.OuterBuild,
            project.State,
            project.Failures);

    private static EvaluatedProjectVariantEvidence FallbackCoverageEvidence(
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

    private static IReadOnlyList<string> DeclaredFrameworks(
        ProjectInstance project)
    {
        var frameworks = project.GetPropertyValue("TargetFrameworks")
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (frameworks.Length > 0)
        {
            return frameworks;
        }

        var framework = Optional(project.GetPropertyValue("TargetFramework"));
        return framework is null ? [] : [framework];
    }

    private static EvaluatedProject MergeProjects(
        IEnumerable<EvaluatedProject> projects)
    {
        var variants = projects.ToArray();
        var first = variants[0];
        var failures = variants
            .SelectMany(static project => project.Failures)
            .Distinct()
            .OrderBy(static failure => failure.Reason)
            .ThenBy(
                static failure => failure.AuthorityCode,
                StringComparer.Ordinal)
            .ToArray();
        var state = variants.Max(static project => project.State);
        return new EvaluatedProject(
            first.Path,
            first.IsExternal,
            variants.Select(static project => project.Configuration)
                .Where(static value => value is not null)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .FirstOrDefault(),
            variants.Select(static project => project.Framework)
                .Where(static value => value is not null)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .FirstOrDefault(),
            state,
            failures);
    }

    private static ProjectEvaluationFailure Failure(Exception exception)
    {
        if (exception is ProjectPathScopeException)
        {
            return new ProjectEvaluationFailure(
                ProjectEvaluationFailureReason.WorkspacePathEscape,
                "workspace.project_link_escape");
        }

        if (exception is CircularDependencyException)
        {
            return new ProjectEvaluationFailure(
                ProjectEvaluationFailureReason.CircularDependency);
        }

        if (exception is InvalidProjectFileException invalid)
        {
            var reason = invalid.ErrorCode switch
            {
                "MSB3202" => ProjectEvaluationFailureReason.ProjectNotFound,
                "MSB4019" => ProjectEvaluationFailureReason.ImportNotFound,
                "MSB4236" => ProjectEvaluationFailureReason.SdkNotFound,
                "MSB4025" => ProjectEvaluationFailureReason.InvalidProjectFile,
                _ => ProjectEvaluationFailureReason.EvaluationFailed,
            };
            return new ProjectEvaluationFailure(reason, Optional(invalid.ErrorCode));
        }

        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ProjectEvaluationFailure(
                ProjectEvaluationFailureReason.ProjectNotFound);
        }

        return new ProjectEvaluationFailure(
            ProjectEvaluationFailureReason.EvaluationFailed);
    }

    private static Exception Unwrap(Exception exception)
    {
        if (exception is not AggregateException aggregate)
        {
            return exception;
        }

        var exceptions = aggregate.Flatten().InnerExceptions;
        return exceptions.FirstOrDefault(
                   static inner => inner is ProjectPathScopeException)
               ?? exceptions.FirstOrDefault(
                   static inner => inner is InvalidProjectFileException)
               ?? exceptions.FirstOrDefault(
                   static inner => inner is CircularDependencyException)
               ?? exceptions[0];
    }

    private static EvaluatedProjectGraph UnavailableGraph(
        WorkspaceDiscoveryResult discovery,
        WorkspaceSelection selection,
        IReadOnlyDictionary<string, string> properties,
        ProjectEvaluationFailure failure)
    {
        var entryPath = Path.GetFullPath(
            selection.Path.Replace('/', Path.DirectorySeparatorChar),
            discovery.RootPath);
        var projectPaths = selection.Kind is WorkspaceEntryPointKind.Solution
            ? KnownSolutionProjects(entryPath)
            : [entryPath];
        var projects = projectPaths
            .Select(path => NormalizePath(discovery.RootPath, path))
            .Distinct()
            .OrderBy(static path => path.Path, StringComparer.Ordinal)
            .Select(path => new EvaluatedProject(
                path.Path,
                path.IsExternal,
                properties.GetValueOrDefault("Configuration"),
                properties.GetValueOrDefault("TargetFramework"),
                EvaluatedProjectState.Failed,
                [failure]))
            .ToArray();
        return new EvaluatedProjectGraph(
            selection,
            ProjectGraphCompleteness.Failed,
            AppliedProperties(properties),
            projects,
            [],
            null,
            [failure],
            projects.Select(FallbackCoverageEvidence));
    }

    private static IReadOnlyDictionary<string, string> BuildGlobalProperties(
        ProjectGraphEvaluationOptions options)
    {
        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var property in options.Properties)
        {
            properties[property.Name] = property.Value;
        }

        if (options.Configuration is not null)
        {
            properties.Remove("Configuration");
            properties["Configuration"] = options.Configuration;
        }

        if (options.Framework is not null)
        {
            properties.Remove("TargetFramework");
            properties["TargetFramework"] = options.Framework;
        }

        return new ReadOnlyDictionary<string, string>(properties);
    }

    private static void ValidateDeclaredFramework(
        string workspaceRoot,
        WorkspaceSelection selection,
        string entryPath,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken)
    {
        if (!properties.TryGetValue("TargetFramework", out var framework))
        {
            return;
        }

        var unconstrainedProperties = new Dictionary<string, string>(
            properties,
            StringComparer.OrdinalIgnoreCase);
        unconstrainedProperties.Remove("TargetFramework");
        var declarations = ReadFrameworkDeclarations(
                workspaceRoot,
                selection,
                entryPath,
                unconstrainedProperties,
                cancellationToken)
            .Where(declaration => !declaration.Frameworks.Contains(
                framework,
                StringComparer.Ordinal))
            .ToArray();
        if (declarations.Length == 0)
        {
            return;
        }

        var projects = string.Join(
            ", ",
            declarations.Select(
                static declaration => $"`{declaration.Project}`"));
        var available = string.Join(
            "; ",
            declarations.Select(declaration =>
                $"`{declaration.Project}`: {FrameworkList(declaration)}"));
        throw new ProjectGraphUsageException(
            ProjectGraphUsageErrorKind.FrameworkNotDeclared,
            "usage.framework_not_declared",
            $"Target framework `{framework}` is not declared by selected project(s) {projects}.",
            framework,
            declarations,
            "Use `--framework` with a framework declared by every selected "
            + $"project. Available declarations: {available}.");
    }

    private static string FrameworkList(
        ProjectFrameworkDeclaration declaration) =>
        declaration.Frameworks.Count == 0
            ? "(none)"
            : string.Join(
                ", ",
                declaration.Frameworks.Select(static value => $"`{value}`"));

    private static IReadOnlyList<ProjectFrameworkDeclaration> ReadFrameworkDeclarations(
        string workspaceRoot,
        WorkspaceSelection selection,
        string entryPath,
        IDictionary<string, string> properties,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var projectCollection = new ProjectCollection();
        ProjectGraph.ProjectInstanceFactoryFunc projectFactory =
            (projectPath, globalProperties, collection) =>
            {
                AuthorizeProjectPath(workspaceRoot, projectPath);
                return new ProjectInstance(
                    projectPath,
                    globalProperties,
                    toolsVersion: null,
                    collection);
            };
        ProjectGraph graph;
        try
        {
            graph = new ProjectGraph(
                [new ProjectGraphEntryPoint(entryPath, properties)],
                projectCollection,
                projectFactory,
                degreeOfParallelism: 1,
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is InvalidProjectFileException
                  or CircularDependencyException
                  or AggregateException
                  or IOException
                  or UnauthorizedAccessException)
        {
            // The constrained graph evaluation reports project failures.
            return [];
        }

        var selectedPaths = selection.Kind is WorkspaceEntryPointKind.Solution
            ? KnownSolutionProjects(entryPath)
            : [entryPath];
        var selectedPathSet = selectedPaths
            .Select(Path.GetFullPath)
            .ToHashSet(PathComparer());
        var directNodes = graph.EntryPointNodes
            .Where(node => selectedPathSet.Contains(
                Path.GetFullPath(node.ProjectInstance.FullPath)))
            .Concat(graph.ProjectNodes
                .Where(node => IsSelectedSolutionNode(
                    selection,
                    node.ProjectInstance.FullPath,
                    workspaceRoot))
                .SelectMany(static node => node.ProjectReferences)
                .Where(node => selectedPathSet.Contains(
                    Path.GetFullPath(node.ProjectInstance.FullPath))))
            .Distinct()
            .ToArray();
        return directNodes
            .GroupBy(
                node => Path.GetFullPath(node.ProjectInstance.FullPath),
                PathComparer())
            .Select(group => new ProjectFrameworkDeclaration(
                NormalizePath(workspaceRoot, group.Key).Path,
                MergedDeclaredFrameworks(group.Select(
                    static node => node.ProjectInstance))))
            .OrderBy(
                static declaration => declaration.Project,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> MergedDeclaredFrameworks(
        IEnumerable<ProjectInstance> projects)
    {
        var declarations = projects
            .Select(DeclaredFrameworks)
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

    private static IEnumerable<ProjectEvaluationFailure> AssetsFailures(
        ProjectInstance project)
    {
        var assetsPath = project.GetPropertyValue("ProjectAssetsFile");
        if (string.IsNullOrWhiteSpace(assetsPath))
        {
            return [];
        }

        var fullAssetsPath = Path.IsPathRooted(assetsPath)
            ? Path.GetFullPath(assetsPath)
            : Path.GetFullPath(
                assetsPath,
                Path.GetDirectoryName(project.FullPath)!);
        if (!File.Exists(fullAssetsPath))
        {
            return [MissingAssetsFailure()];
        }

        try
        {
            using var stream = File.OpenRead(fullAssetsPath);
            using var assets = JsonDocument.Parse(stream);
            if (assets.RootElement.ValueKind is not JsonValueKind.Object
                || !assets.RootElement.TryGetProperty("targets", out var targets)
                || targets.ValueKind is not JsonValueKind.Object)
            {
                return [InvalidAssetsFailure()];
            }

            var framework = Optional(project.GetPropertyValue(
                "TargetFramework"));
            if (framework is null)
            {
                return [];
            }

            var runtimeIdentifier = Optional(project.GetPropertyValue(
                "RuntimeIdentifier"));
            var target = runtimeIdentifier is null
                ? framework
                : $"{framework}/{runtimeIdentifier}";
            if (!targets.TryGetProperty(target, out var targetValue))
            {
                return [MissingAssetsFailure()];
            }

            return targetValue.ValueKind is JsonValueKind.Object
                ? []
                : [InvalidAssetsFailure()];
        }
        catch (FileNotFoundException)
        {
            return [MissingAssetsFailure()];
        }
        catch (DirectoryNotFoundException)
        {
            return [MissingAssetsFailure()];
        }
        catch (Exception exception)
            when (exception is JsonException
                  or IOException
                  or UnauthorizedAccessException)
        {
            return [InvalidAssetsFailure()];
        }
    }

    private static ProjectEvaluationFailure MissingAssetsFailure() =>
        new(ProjectEvaluationFailureReason.MissingAssets);

    private static ProjectEvaluationFailure InvalidAssetsFailure() =>
        new(
            ProjectEvaluationFailureReason.InvalidAssetsFile,
            "assets.invalid");

    private static IEnumerable<AppliedMsBuildProperty> AppliedProperties(
        IReadOnlyDictionary<string, string> properties) =>
        properties
            .Select(static property => new AppliedMsBuildProperty(
                property.Key,
                property.Value))
            .OrderBy(static property => property.Name, StringComparer.Ordinal);

    private static (string Path, bool IsExternal) NormalizePath(
        string workspaceRoot,
        string path)
    {
        var normalized = new WorkspacePathResolver(
                workspaceRoot,
                workspaceRoot)
            .NormalizeOutput(path);
        return (normalized.Path, normalized.IsExternal);
    }

    private static (string Path, bool IsExternal) NormalizeOutputPath(
        string workspaceRoot,
        string path) =>
        NormalizePath(workspaceRoot, path);

    private static bool IsSelectedSolutionNode(
        WorkspaceSelection selection,
        string projectPath,
        string workspaceRoot) =>
        selection.Kind is WorkspaceEntryPointKind.Solution
        && NormalizePath(workspaceRoot, projectPath).Path.Equals(
            selection.Path,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void AuthorizeProjectPath(
        string workspaceRoot,
        string projectPath)
    {
        var resolved = new WorkspacePathResolver(
                workspaceRoot,
                workspaceRoot)
            .NormalizeOutput(projectPath);
        if (resolved.EscapesThroughSymbolicLink)
        {
            throw new ProjectPathScopeException(projectPath);
        }
    }

    private static string ProjectInstanceIdentity(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties) =>
        ProjectInstanceIdentity(
            projectPath,
            globalProperties,
            OperatingSystem.IsWindows());

    internal static string ProjectInstanceIdentity(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        bool isWindows)
    {
        var identity = new StringBuilder();
        var fullProjectPath = Path.GetFullPath(projectPath);
        AppendIdentityPart(
            identity,
            isWindows
                ? fullProjectPath.ToUpperInvariant()
                : fullProjectPath);
        foreach (var property in globalProperties
                     .OrderBy(
                         static property => property.Key,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(
                         static property => property.Key,
                         StringComparer.Ordinal))
        {
            AppendIdentityPart(identity, property.Key.ToUpperInvariant());
            AppendIdentityPart(identity, property.Value);
        }

        return identity.ToString();
    }

    private static void AppendIdentityPart(StringBuilder identity, string value)
    {
        identity.Append(value.Length).Append(':').Append(value).Append('|');
    }

    private static HashSet<string> CycleParticipantIdentities(
        string workspaceRoot,
        WorkspaceSelection selection,
        IReadOnlyList<CapturedProjectInstance> projects)
    {
        var instances = projects
            .Where(captured => !IsSelectedSolutionNode(
                selection,
                captured.Project.FullPath,
                workspaceRoot))
            .GroupBy(static captured => captured.Identity, StringComparer.Ordinal)
            .Select(static captures => captures.First())
            .ToArray();
        var instancesByPath = instances
            .GroupBy(
                captured => Path.GetFullPath(captured.Project.FullPath),
                PathComparer())
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                PathComparer());
        var nodes = instances
            .Select(captured => new ProjectInstanceGraphNode(
                captured.Identity,
                NormalizePath(
                    workspaceRoot,
                    captured.Project.FullPath).Path))
            .ToArray();
        var edges = new HashSet<ProjectInstanceGraphEdge>();
        var outerBuildChildren = new Dictionary<string, CapturedProjectInstance[]>(
            StringComparer.Ordinal);

        foreach (var outerBuild in instances.Where(
                     static captured => ProjectType(captured.Project)
                         is CapturedProjectType.OuterBuild))
        {
            var children = ProvenOuterBuildChildren(
                outerBuild,
                instancesByPath);
            outerBuildChildren[outerBuild.Identity] = children;
            foreach (var child in children)
            {
                edges.Add(new ProjectInstanceGraphEdge(
                    outerBuild.Identity,
                    child.Identity));
            }
        }

        foreach (var source in instances.Where(
                     static captured => ProjectType(captured.Project)
                         is not CapturedProjectType.OuterBuild))
        {
            foreach (var reference in source.Project.GetItems(
                         "ProjectReference"))
            {
                var target = ProvenReferenceTarget(
                    source,
                    reference,
                    instancesByPath);
                if (target is null)
                {
                    continue;
                }

                edges.Add(new ProjectInstanceGraphEdge(
                    source.Identity,
                    target.Identity));
                if (!outerBuildChildren.TryGetValue(
                        target.Identity,
                        out var innerBuilds))
                {
                    continue;
                }

                foreach (var innerBuild in innerBuilds)
                {
                    edges.Add(new ProjectInstanceGraphEdge(
                        source.Identity,
                        innerBuild.Identity));
                }
            }
        }

        return FindCycleParticipantIdentities(nodes, edges);
    }

    internal static HashSet<string> CycleParticipantPaths(
        IReadOnlyCollection<ProjectInstanceGraphNode> nodes,
        IReadOnlyCollection<ProjectInstanceGraphEdge> edges)
    {
        var participantIdentities = FindCycleParticipantIdentities(nodes, edges);
        return nodes
            .Where(node => participantIdentities.Contains(node.Identity))
            .Select(static node => node.ProjectPath)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> FindCycleParticipantIdentities(
        IReadOnlyCollection<ProjectInstanceGraphNode> nodes,
        IReadOnlyCollection<ProjectInstanceGraphEdge> edges)
    {
        var nodeIdentities = nodes
            .Select(static node => node.Identity)
            .ToHashSet(StringComparer.Ordinal);
        var adjacency = nodeIdentities.ToDictionary(
            static identity => identity,
            static _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (adjacency.TryGetValue(edge.ProjectIdentity, out var dependencies)
                && nodeIdentities.Contains(edge.DependencyIdentity))
            {
                dependencies.Add(edge.DependencyIdentity);
            }
        }

        var nextIndex = 0;
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var participants = new HashSet<string>(StringComparer.Ordinal);

        foreach (var identity in nodeIdentities.Order(
                     StringComparer.Ordinal))
        {
            if (!indexes.ContainsKey(identity))
            {
                Visit(identity);
            }
        }

        return participants;

        void Visit(string identity)
        {
            indexes[identity] = nextIndex;
            lowLinks[identity] = nextIndex;
            nextIndex++;
            stack.Push(identity);
            onStack.Add(identity);

            foreach (var dependency in adjacency[identity].Order(
                         StringComparer.Ordinal))
            {
                if (!indexes.ContainsKey(dependency))
                {
                    Visit(dependency);
                    lowLinks[identity] = Math.Min(
                        lowLinks[identity],
                        lowLinks[dependency]);
                }
                else if (onStack.Contains(dependency))
                {
                    lowLinks[identity] = Math.Min(
                        lowLinks[identity],
                        indexes[dependency]);
                }
            }

            if (lowLinks[identity] != indexes[identity])
            {
                return;
            }

            var component = new List<string>();
            string componentIdentity;
            do
            {
                componentIdentity = stack.Pop();
                onStack.Remove(componentIdentity);
                component.Add(componentIdentity);
            }
            while (!componentIdentity.Equals(identity, StringComparison.Ordinal));

            if (component.Count > 1 || adjacency[identity].Contains(identity))
            {
                participants.UnionWith(component);
            }
        }
    }

    private static CapturedProjectInstance[] ProvenOuterBuildChildren(
        CapturedProjectInstance outerBuild,
        IReadOnlyDictionary<string, CapturedProjectInstance[]> instancesByPath)
    {
        var innerBuildProperty = outerBuild.Project.GetPropertyValue(
            "InnerBuildProperty");
        var innerBuildValuesProperty = outerBuild.Project.GetPropertyValue(
            "InnerBuildPropertyValues");
        if (string.IsNullOrWhiteSpace(innerBuildProperty)
            || string.IsNullOrWhiteSpace(innerBuildValuesProperty))
        {
            return [];
        }

        var innerBuildValues = outerBuild.Project.GetPropertyValue(
            innerBuildValuesProperty);
        if (innerBuildValues.Contains('%', StringComparison.Ordinal)
            || !instancesByPath.TryGetValue(
                Path.GetFullPath(outerBuild.Project.FullPath),
                out var candidates))
        {
            return [];
        }

        var children = new List<CapturedProjectInstance>();
        foreach (var value in innerBuildValues.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var expectedProperties = new Dictionary<string, string>(
                outerBuild.GlobalProperties,
                StringComparer.OrdinalIgnoreCase)
            {
                [innerBuildProperty] = value,
            };
            var child = SingleMatchingInstance(candidates, expectedProperties);
            if (child is not null)
            {
                children.Add(child);
            }
        }

        return children
            .DistinctBy(static child => child.Identity, StringComparer.Ordinal)
            .ToArray();
    }

    private static CapturedProjectInstance? ProvenReferenceTarget(
        CapturedProjectInstance source,
        ProjectItemInstance reference,
        IReadOnlyDictionary<string, CapturedProjectInstance[]> instancesByPath)
    {
        var targetPath = reference.GetMetadataValue("FullPath");
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = Path.GetFullPath(
                reference.EvaluatedInclude,
                Path.GetDirectoryName(source.Project.FullPath)!);
        }

        if (!instancesByPath.TryGetValue(
                Path.GetFullPath(targetPath),
                out var candidates))
        {
            return null;
        }

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        return TryEffectiveReferenceProperties(
            source,
            reference,
            targetPath,
            out var effectiveProperties)
            ? SingleMatchingInstance(candidates, effectiveProperties)
            : null;
    }

    private static bool TryEffectiveReferenceProperties(
        CapturedProjectInstance source,
        ProjectItemInstance reference,
        string targetPath,
        out IReadOnlyDictionary<string, string> effectiveProperties)
    {
        effectiveProperties = new Dictionary<string, string>();
        if (!IsKnownFalse(source.Project.GetPropertyValue(
                "EnableDynamicPlatformResolution"))
            || !string.IsNullOrEmpty(reference.GetMetadataValue("ToolsVersion"))
            || ProjectType(source.Project) is CapturedProjectType.OuterBuild
            || !TryParsePropertyAssignments(
                reference.GetMetadataValue("Properties"),
                out var properties)
            || !TryParsePropertyAssignments(
                reference.GetMetadataValue("AdditionalProperties"),
                out var additionalProperties)
            || !TryParsePropertyNames(
                reference.GetMetadataValue("UndefineProperties"),
                out var propertiesToRemove)
            || !TryParsePropertyNames(
                reference.GetMetadataValue("GlobalPropertiesToRemove"),
                out var globalPropertiesToRemove))
        {
            return false;
        }

        if (properties.Count == 0)
        {
            var setProperties = string.Join(
                ';',
                new[]
                {
                    reference.GetMetadataValue("SetConfiguration"),
                    reference.GetMetadataValue("SetPlatform"),
                    reference.GetMetadataValue("SetTargetFramework"),
                }.Where(static value => value.Length > 0));
            if (!TryParsePropertyAssignments(setProperties, out properties))
            {
                return false;
            }
        }

        var result = new Dictionary<string, string>(
            source.GlobalProperties,
            StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties)
        {
            result[property.Key] = property.Value;
        }

        foreach (var property in additionalProperties)
        {
            result[property.Key] = property.Value;
        }

        foreach (var propertyName in propertiesToRemove.Concat(
                     globalPropertiesToRemove))
        {
            result.Remove(propertyName);
        }

        if (ProjectType(source.Project) is CapturedProjectType.InnerBuild)
        {
            result.Remove(source.Project.GetPropertyValue("InnerBuildProperty"));
        }

        if (!TryApplySolutionConfiguration(
                source.Project.GetPropertyValue(
                    "CurrentSolutionConfigurationContents"),
                targetPath,
                result))
        {
            return false;
        }

        effectiveProperties = result;
        return true;
    }

    private static bool TryApplySolutionConfiguration(
        string solutionConfiguration,
        string targetPath,
        IDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(solutionConfiguration))
        {
            return true;
        }

        try
        {
            using var text = new StringReader(solutionConfiguration);
            using var reader = XmlReader.Create(
                text,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                });
            var projectConfiguration = XDocument.Load(reader)
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals(
                                               "ProjectConfiguration",
                                               StringComparison.Ordinal)
                                           && element.Attributes().Any(
                                               attribute => attribute.Name.LocalName.Equals(
                                                                "AbsolutePath",
                                                                StringComparison.Ordinal)
                                                            && PathComparer().Equals(
                                                                Path.GetFullPath(attribute.Value),
                                                                Path.GetFullPath(targetPath))));
            if (projectConfiguration is null)
            {
                return false;
            }

            var configurationPlatform = projectConfiguration.Value.Split('|');
            if (configurationPlatform.Length == 0
                || string.IsNullOrWhiteSpace(configurationPlatform[0]))
            {
                return false;
            }

            properties["Configuration"] = configurationPlatform[0];
            if (configurationPlatform.Length > 1)
            {
                properties["Platform"] = configurationPlatform[1];
            }
            else
            {
                properties.Remove("Platform");
            }

            return true;
        }
        catch (Exception exception)
            when (exception is XmlException
                  or ArgumentException
                  or NotSupportedException
                  or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryParsePropertyAssignments(
        string value,
        out Dictionary<string, string> properties)
    {
        properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (value.Length == 0)
        {
            return true;
        }

        if (value.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var assignment in value.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = assignment.IndexOf('=');
            var name = separator < 0 ? string.Empty : assignment[..separator].Trim();
            if (name.Length == 0)
            {
                return false;
            }

            properties[name] = assignment[(separator + 1)..];
        }

        return true;
    }

    private static bool TryParsePropertyNames(
        string value,
        out IReadOnlyList<string> propertyNames)
    {
        propertyNames = [];
        if (value.Length == 0)
        {
            return true;
        }

        if (value.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        propertyNames = value.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        return true;
    }

    private static CapturedProjectInstance? SingleMatchingInstance(
        IEnumerable<CapturedProjectInstance> candidates,
        IReadOnlyDictionary<string, string> expectedProperties)
    {
        var matches = candidates
            .Where(candidate => GlobalPropertiesEqual(
                candidate.GlobalProperties,
                expectedProperties))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool GlobalPropertiesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(property => right.TryGetValue(
            property.Key,
            out var value)
            && value.Equals(property.Value, StringComparison.Ordinal));

    private static CapturedProjectType ProjectType(ProjectInstance project)
    {
        var innerBuildProperty = project.GetPropertyValue("InnerBuildProperty");
        var innerBuildValue = string.IsNullOrWhiteSpace(innerBuildProperty)
            ? string.Empty
            : project.GetPropertyValue(innerBuildProperty);
        var innerBuildValuesProperty = project.GetPropertyValue(
            "InnerBuildPropertyValues");
        var innerBuildValues = string.IsNullOrWhiteSpace(
            innerBuildValuesProperty)
            ? string.Empty
            : project.GetPropertyValue(innerBuildValuesProperty);
        if (string.IsNullOrWhiteSpace(innerBuildValue)
            && !string.IsNullOrWhiteSpace(innerBuildValues))
        {
            return CapturedProjectType.OuterBuild;
        }

        return string.IsNullOrWhiteSpace(innerBuildValue)
            ? CapturedProjectType.NonMultitargeting
            : CapturedProjectType.InnerBuild;
    }

    private static bool IsKnownFalse(string value) =>
        value.Length == 0
        || value.Equals("false", StringComparison.OrdinalIgnoreCase)
        || value.Equals("off", StringComparison.OrdinalIgnoreCase)
        || value.Equals("no", StringComparison.OrdinalIgnoreCase)
        || value.Equals("0", StringComparison.Ordinal);

    private static IReadOnlyList<string> KnownSolutionProjects(
        string solutionPath)
    {
        try
        {
            var extension = Path.GetExtension(solutionPath);
            var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
            if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(solutionPath);
                using var reader = XmlReader.Create(
                    stream,
                    new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                    });
                return XDocument.Load(reader)
                    .Descendants()
                    .Where(static element => element.Name.LocalName.Equals(
                        "Project",
                        StringComparison.Ordinal))
                    .Select(static element => element.Attributes()
                        .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                            "Path",
                            StringComparison.OrdinalIgnoreCase))?.Value)
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.GetFullPath(
                        path!
                            .Replace('/', Path.DirectorySeparatorChar)
                            .Replace('\\', Path.DirectorySeparatorChar),
                        solutionDirectory))
                    .Where(static path => Path.GetExtension(path).EndsWith(
                        "proj",
                        StringComparison.OrdinalIgnoreCase))
                    .Distinct(PathComparer())
                    .ToArray();
            }

            if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadLines(solutionPath)
                    .Where(static line => line.StartsWith(
                        "Project(\"",
                        StringComparison.Ordinal))
                    .Select(static line => line.Split('"'))
                    .Where(static fields => fields.Length >= 6)
                    .Select(static fields => fields[5])
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Where(static path => Path.GetExtension(path).EndsWith(
                        "proj",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.GetFullPath(
                        path.Replace('\\', Path.DirectorySeparatorChar),
                        solutionDirectory))
                    .Where(static path => !path.EndsWith(
                        ".sln",
                        StringComparison.OrdinalIgnoreCase))
                    .Distinct(PathComparer())
                    .ToArray();
            }
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or XmlException
                  or ArgumentException
                  or NotSupportedException)
        {
            // The graph-level authority failure remains the honest result.
        }

        return [];
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private enum CapturedProjectType
    {
        NonMultitargeting,
        OuterBuild,
        InnerBuild,
    }

    private sealed record CapturedProjectInstance(
        ProjectInstance Project,
        string Identity,
        IReadOnlyDictionary<string, string> GlobalProperties);

    private sealed record EvaluatedProjectInstance(
        EvaluatedProject Project,
        EvaluatedProjectVariantEvidence CoverageEvidence);

    private sealed class ProjectPathScopeException(string path)
        : IOException($"Project path '{path}' escapes the workspace through a link.");
}
