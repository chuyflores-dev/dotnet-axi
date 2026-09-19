using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.Graph;

/// <summary>
/// The project-level entities materialized by the 0.6 dependency graph.
/// Solution selection belongs to evidence scope; it is not a graph node.
/// </summary>
public enum ProjectGraphNodeKind
{
    Project,
    Package,
}

public enum ProjectGraphRelationshipKind
{
    ProjectReference,
    PackageReference,
}

/// <summary>
/// States that can be reported without exposing MSBuild implementation types.
/// </summary>
public enum ProjectGraphEvaluationState
{
    Evaluated,
    Incomplete,
    Failed,
    Unsupported,
}

/// <summary>
/// The direction of a relationship relative to its source and target IDs.
/// </summary>
public enum ProjectGraphRelationshipDirection
{
    SourceDependsOnTarget,
    TargetDependsOnSource,
}

/// <summary>
/// The authority from which a project-graph row was observed.
/// </summary>
public enum ProjectGraphProvenance
{
    EvaluatedProjectGraph,
    EvaluatedProjectReference,
    EvaluatedPackageReference,
}

/// <summary>
/// A selected evaluated project meaning. A project path without a framework is
/// valid when evaluation could not select or complete an inner build.
/// </summary>
public sealed record ProjectGraphVariant
{
    public ProjectGraphVariant(
        string projectPath,
        string? configuration = null,
        string? framework = null)
    {
        ProjectPath = RequiredText(projectPath, nameof(projectPath));
        Configuration = OptionalText(configuration, nameof(configuration));
        Framework = OptionalText(framework, nameof(framework));
    }

    public string ProjectPath { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    private static string RequiredText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string? OptionalText(string? value, string parameterName)
    {
        if (value is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        }

        return value;
    }
}

/// <summary>
/// Per-row graph evidence. It remains independent from the response envelope
/// because a graph can contain verified and incomplete observations together.
/// </summary>
public sealed record ProjectGraphEvidence
{
    public ProjectGraphEvidence(
        EvidenceScope scope,
        EvidenceCoverage coverage,
        EvidenceConfidence confidence,
        ProjectGraphProvenance provenance)
    {
        if (!Enum.IsDefined(confidence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "The evidence confidence is not defined.");
        }

        if (!Enum.IsDefined(provenance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provenance),
                provenance,
                "The graph provenance is not defined.");
        }

        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Coverage = coverage ?? throw new ArgumentNullException(nameof(coverage));
        Confidence = confidence;
        Provenance = provenance;
    }

    public EvidenceScope Scope { get; }

    public EvidenceCoverage Coverage { get; }

    public EvidenceConfidence Confidence { get; }

    public ProjectGraphProvenance Provenance { get; }
}

/// <summary>
/// A project variant or a package observed from an evaluated project variant.
/// The factory methods prevent a malformed hybrid node.
/// </summary>
public sealed class ProjectGraphNode
{
    private ProjectGraphNode(
        string id,
        ProjectGraphNodeKind kind,
        ProjectGraphVariant? project,
        string? packageId,
        string? packageVersion,
        ProjectGraphEvaluationState evaluationState,
        ProjectGraphEvidence evidence)
    {
        Id = id;
        Kind = kind;
        Project = project;
        PackageId = packageId;
        PackageVersion = packageVersion;
        EvaluationState = evaluationState;
        Evidence = evidence;
    }

    public string Id { get; }

    public ProjectGraphNodeKind Kind { get; }

    public ProjectGraphVariant? Project { get; }

    public string? PackageId { get; }

    public string? PackageVersion { get; }

    public ProjectGraphEvaluationState EvaluationState { get; }

    public ProjectGraphEvidence Evidence { get; }

    public static ProjectGraphNode CreateProject(
        ProjectGraphVariant project,
        ProjectGraphEvaluationState evaluationState,
        ProjectGraphEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(evidence);
        ValidateEvaluationState(evaluationState);

        return new ProjectGraphNode(
            ProjectGraphIdentity.CreateProject(project),
            ProjectGraphNodeKind.Project,
            project,
            packageId: null,
            packageVersion: null,
            evaluationState,
            evidence);
    }

    public static ProjectGraphNode CreatePackage(
        string packageId,
        string? packageVersion,
        ProjectGraphEvaluationState evaluationState,
        ProjectGraphEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (packageVersion is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        }

        ArgumentNullException.ThrowIfNull(evidence);
        ValidateEvaluationState(evaluationState);

        return new ProjectGraphNode(
            ProjectGraphIdentity.CreatePackage(packageId, packageVersion),
            ProjectGraphNodeKind.Package,
            project: null,
            packageId,
            packageVersion,
            evaluationState,
            evidence);
    }

    private static void ValidateEvaluationState(ProjectGraphEvaluationState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "The project graph evaluation state is not defined.");
        }
    }
}

/// <summary>
/// A directed project- or package-reference relation. Configuration and
/// framework describe the evaluated source meaning that supplied the row.
/// </summary>
public sealed class ProjectGraphRelationship
{
    public ProjectGraphRelationship(
        ProjectGraphRelationshipKind kind,
        string sourceId,
        string targetId,
        ProjectGraphRelationshipDirection direction,
        string? configuration,
        string? framework,
        ProjectGraphEvidence evidence)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The relationship kind is not defined.");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The relationship direction is not defined.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (configuration is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        }

        if (framework is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        }

        ArgumentNullException.ThrowIfNull(evidence);
        Kind = kind;
        SourceId = sourceId;
        TargetId = targetId;
        Direction = direction;
        Configuration = configuration;
        Framework = framework;
        Evidence = evidence;
        Id = ProjectGraphIdentity.CreateRelationship(
            kind,
            sourceId,
            targetId,
            direction,
            configuration,
            framework);
    }

    public string Id { get; }

    public ProjectGraphRelationshipKind Kind { get; }

    public string SourceId { get; }

    public string TargetId { get; }

    public ProjectGraphRelationshipDirection Direction { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    public ProjectGraphEvidence Evidence { get; }
}

/// <summary>
/// The bounded, in-memory project dependency graph used by graph commands.
/// It deliberately does not model source, Roslyn, runtime, or persistent graph
/// backend objects.
/// </summary>
public sealed class ProjectDependencyGraph
{
    public ProjectDependencyGraph(
        Evidence evidence,
        IEnumerable<ProjectGraphNode> nodes,
        IEnumerable<ProjectGraphRelationship> relationships)
    {
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(relationships);

        var materializedNodes = nodes.ToArray();
        if (materializedNodes.Any(static node => node is null))
        {
            throw new ArgumentException("Graph nodes cannot contain null values.", nameof(nodes));
        }

        var orderedNodes = materializedNodes
            .OrderBy(static node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var nodesById = new Dictionary<string, ProjectGraphNode>(StringComparer.Ordinal);
        foreach (var node in orderedNodes)
        {
            if (!nodeIds.Add(node.Id))
            {
                throw new ArgumentException("Graph node IDs must be unique.", nameof(nodes));
            }

            nodesById.Add(node.Id, node);
        }

        var materializedRelationships = relationships.ToArray();
        if (materializedRelationships.Any(static relationship => relationship is null))
        {
            throw new ArgumentException("Graph relationships cannot contain null values.", nameof(relationships));
        }

        var orderedRelationships = materializedRelationships
            .OrderBy(static relationship => relationship.Id, StringComparer.Ordinal)
            .ToArray();

        var relationshipIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in orderedRelationships)
        {
            if (!relationshipIds.Add(relationship.Id))
            {
                throw new ArgumentException("Graph relationship IDs must be unique.", nameof(relationships));
            }

            if (!nodesById.TryGetValue(relationship.SourceId, out var source)
                || !nodesById.TryGetValue(relationship.TargetId, out var target))
            {
                throw new ArgumentException(
                    "Every graph relationship endpoint must be a graph node.",
                    nameof(relationships));
            }

            ValidateRelationshipEndpoints(relationship, source, target);
        }

        Nodes = Array.AsReadOnly(orderedNodes);
        Relationships = Array.AsReadOnly(orderedRelationships);
    }

    public Evidence Evidence { get; }

    public IReadOnlyList<ProjectGraphNode> Nodes { get; }

    public IReadOnlyList<ProjectGraphRelationship> Relationships { get; }

    private static void ValidateRelationshipEndpoints(
        ProjectGraphRelationship relationship,
        ProjectGraphNode source,
        ProjectGraphNode target)
    {
        var valid = relationship.Kind switch
        {
            ProjectGraphRelationshipKind.ProjectReference => source.Kind
                is ProjectGraphNodeKind.Project
                && target.Kind is ProjectGraphNodeKind.Project,
            ProjectGraphRelationshipKind.PackageReference => relationship.Direction
                is ProjectGraphRelationshipDirection.SourceDependsOnTarget
                    ? source.Kind is ProjectGraphNodeKind.Project
                      && target.Kind is ProjectGraphNodeKind.Package
                    : source.Kind is ProjectGraphNodeKind.Package
                      && target.Kind is ProjectGraphNodeKind.Project,
            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException(
                "Relationship endpoints do not match the relationship kind and direction.",
                nameof(relationship));
        }
    }
}

public static class ProjectGraphIdentity
{
    public static string CreateProject(ProjectGraphVariant project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Create(
            "project/v1/",
            "dotnet-axi/project-graph-project/v1",
            project.ProjectPath,
            project.Configuration ?? string.Empty,
            project.Framework ?? string.Empty);
    }

    public static string CreatePackage(string packageId, string? packageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (packageVersion is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        }

        return Create(
            "package/v1/",
            "dotnet-axi/project-graph-package/v1",
            packageId.ToUpperInvariant(),
            packageVersion ?? string.Empty);
    }

    internal static string CreateRelationship(
        ProjectGraphRelationshipKind kind,
        string sourceId,
        string targetId,
        ProjectGraphRelationshipDirection direction,
        string? configuration,
        string? framework)
        => Create(
            "project-relationship/v1/",
            "dotnet-axi/project-graph-relationship/v1",
            kind.ToString(),
            sourceId,
            targetId,
            direction.ToString(),
            configuration ?? string.Empty,
            framework ?? string.Empty);

    private static string Create(string prefix, params string[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var part in parts)
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return prefix + Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
