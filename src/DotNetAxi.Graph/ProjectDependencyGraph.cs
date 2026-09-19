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

/// <summary>
/// One normalized directed cycle in the evaluated project-reference graph.
/// Nodes and relationships are ordered along the cycle; the final
/// relationship returns to the first node.
/// </summary>
public sealed class ProjectDependencyCycle
{
    internal ProjectDependencyCycle(
        IReadOnlyList<ProjectGraphNode> nodes,
        IReadOnlyList<ProjectGraphRelationship> relationships)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(relationships);
        if (nodes.Count == 0 || nodes.Count != relationships.Count)
        {
            throw new ArgumentException(
                "A project dependency cycle requires one relationship for each node.");
        }

        Nodes = Array.AsReadOnly(nodes.ToArray());
        Relationships = Array.AsReadOnly(relationships.ToArray());
        Id = CreateIdentity(Relationships);
    }

    public string Id { get; }

    public IReadOnlyList<ProjectGraphNode> Nodes { get; }

    public IReadOnlyList<ProjectGraphRelationship> Relationships { get; }

    private static string CreateIdentity(
        IReadOnlyList<ProjectGraphRelationship> relationships)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var relationship in relationships)
        {
            var bytes = Encoding.UTF8.GetBytes(relationship.Id);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return "project-cycle/v1/" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>
/// Detects directed cycles in project-reference relationships. This is a
/// project-only operation: package references and incomplete graph rows stay
/// in the enclosing graph evidence but never become cycle edges.
/// </summary>
public static class ProjectDependencyCycleDetector
{
    public const int MaximumDetectedCycles = 10_000;
    public const int MaximumTraversalSteps = 1_000_000;

    public static ProjectDependencyCycleDetection FindCycles(
        ProjectDependencyGraph graph,
        int maximumCycles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (maximumCycles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCycles));
        }

        var projects = graph.Nodes
            .Where(static node => node.Kind is ProjectGraphNodeKind.Project)
            .ToDictionary(static node => node.Id, StringComparer.Ordinal);
        var outgoing = projects.Keys.ToDictionary(
            static id => id,
            static _ => new List<CycleEdge>(),
            StringComparer.Ordinal);
        foreach (var relationship in graph.Relationships.Where(static relationship =>
                     relationship.Kind is ProjectGraphRelationshipKind.ProjectReference))
        {
            var (from, to) = relationship.Direction
                is ProjectGraphRelationshipDirection.SourceDependsOnTarget
                ? (relationship.SourceId, relationship.TargetId)
                : (relationship.TargetId, relationship.SourceId);
            if (projects.ContainsKey(from) && projects.ContainsKey(to))
            {
                outgoing[from].Add(new CycleEdge(to, relationship));
            }
        }

        foreach (var edges in outgoing.Values)
        {
            edges.Sort(static (left, right) => StringComparer.Ordinal.Compare(
                left.Relationship.Id,
                right.Relationship.Id));
        }

        var cycles = new List<ProjectDependencyCycle>();
        var cyclicNodes = CyclicNodes(outgoing, cancellationToken);
        var limitReached = false;
        var traversalSteps = 0;
        foreach (var start in projects.Keys.Order(StringComparer.Ordinal))
        {
            if (limitReached)
            {
                break;
            }

            if (!cyclicNodes.Contains(start))
            {
                continue;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal) { start };
            var nodes = new List<ProjectGraphNode> { projects[start] };
            var relationships = new List<ProjectGraphRelationship>();
            Visit(start);

            void Visit(string current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var edge in outgoing[current])
                {
                    if (limitReached)
                    {
                        return;
                    }

                    if (!cyclicNodes.Contains(edge.TargetId))
                    {
                        continue;
                    }

                    if (++traversalSteps > MaximumTraversalSteps)
                    {
                        limitReached = true;
                        return;
                    }

                    if (StringComparer.Ordinal.Compare(edge.TargetId, start) < 0)
                    {
                        continue;
                    }

                    if (edge.TargetId.Equals(start, StringComparison.Ordinal))
                    {
                        if (cycles.Count == maximumCycles)
                        {
                            limitReached = true;
                        }
                        else
                        {
                            cycles.Add(new ProjectDependencyCycle(
                                nodes,
                                relationships.Append(edge.Relationship).ToArray()));
                        }
                        continue;
                    }

                    if (!visited.Add(edge.TargetId))
                    {
                        continue;
                    }

                    nodes.Add(projects[edge.TargetId]);
                    relationships.Add(edge.Relationship);
                    Visit(edge.TargetId);
                    relationships.RemoveAt(relationships.Count - 1);
                    nodes.RemoveAt(nodes.Count - 1);
                    visited.Remove(edge.TargetId);
                }
            }
        }

        return new ProjectDependencyCycleDetection(
            cycles.OrderBy(static cycle => cycle.Id, StringComparer.Ordinal).ToArray(),
            TotalKnown: !limitReached);
    }

    private sealed record CycleEdge(
        string TargetId,
        ProjectGraphRelationship Relationship);

    private static HashSet<string> CyclicNodes(
        IReadOnlyDictionary<string, List<CycleEdge>> outgoing,
        CancellationToken cancellationToken)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        var nextIndex = 0;

        foreach (var node in outgoing.Keys.Order(StringComparer.Ordinal))
        {
            if (!indexes.ContainsKey(node))
            {
                Visit(node);
            }
        }

        return cyclic;

        void Visit(string node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            indexes[node] = nextIndex;
            lowLinks[node] = nextIndex;
            nextIndex++;
            stack.Push(node);
            active.Add(node);

            foreach (var edge in outgoing[node])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!indexes.ContainsKey(edge.TargetId))
                {
                    Visit(edge.TargetId);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[edge.TargetId]);
                }
                else if (active.Contains(edge.TargetId))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indexes[edge.TargetId]);
                }
            }

            if (lowLinks[node] != indexes[node])
            {
                return;
            }

            var component = new List<string>();
            string member;
            do
            {
                member = stack.Pop();
                active.Remove(member);
                component.Add(member);
            }
            while (!member.Equals(node, StringComparison.Ordinal));

            if (component.Count > 1
                || outgoing[node].Any(edge => edge.TargetId.Equals(node, StringComparison.Ordinal)))
            {
                cyclic.UnionWith(component);
            }
        }
    }
}

/// <summary>
/// Bounded cycle detection evidence. A false <see cref="TotalKnown"/> means
/// enumeration stopped at a deterministic cycle or traversal safety bound.
/// </summary>
public sealed record ProjectDependencyCycleDetection(
    IReadOnlyList<ProjectDependencyCycle> Cycles,
    bool TotalKnown);

/// <summary>
/// One directed shortest path through materialized project-reference evidence.
/// </summary>
public sealed class ProjectDependencyPath
{
    internal ProjectDependencyPath(
        IReadOnlyList<ProjectGraphNode> nodes,
        IReadOnlyList<ProjectGraphRelationship> relationships)
    {
        if (nodes.Count == 0 || relationships.Count + 1 != nodes.Count)
        {
            throw new ArgumentException("A path must contain one more node than relationships.");
        }

        Nodes = Array.AsReadOnly(nodes.ToArray());
        Relationships = Array.AsReadOnly(relationships.ToArray());
    }

    public IReadOnlyList<ProjectGraphNode> Nodes { get; }

    public IReadOnlyList<ProjectGraphRelationship> Relationships { get; }
}

/// <summary>
/// Bounded shortest-path evidence. A false total-known result means a caller
/// requested fewer paths than were reconstructed at the shortest depth.
/// </summary>
public sealed record ProjectDependencyPathSearch(
    IReadOnlyList<ProjectDependencyPath> Paths,
    int? ShortestDepth,
    bool DepthLimited,
    bool TotalKnown);

/// <summary>
/// Finds deterministic shortest paths through evaluated project references.
/// Package and unsupported semantic relationships are deliberately outside the
/// first project-level path operation.
/// </summary>
public static class ProjectDependencyPathFinder
{
    public const int MaximumDetectedPaths = 10_000;

    public static ProjectDependencyPathSearch FindShortestPaths(
        ProjectDependencyGraph graph,
        string fromId,
        string toId,
        int maxDepth,
        int maximumPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toId);
        if (maxDepth < 0 || maximumPaths < 1)
        {
            throw new ArgumentOutOfRangeException(maxDepth < 0 ? nameof(maxDepth) : nameof(maximumPaths));
        }

        var nodes = graph.Nodes
            .Where(static node => node.Kind is ProjectGraphNodeKind.Project)
            .ToDictionary(static node => node.Id, StringComparer.Ordinal);
        if (!nodes.ContainsKey(fromId) || !nodes.ContainsKey(toId))
        {
            throw new ArgumentException("Path endpoints must be project graph nodes.");
        }

        if (fromId.Equals(toId, StringComparison.Ordinal))
        {
            return new ProjectDependencyPathSearch(
                [new ProjectDependencyPath([nodes[fromId]], [])], 0, false, true);
        }

        var outgoing = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<PathEdge>(),
            StringComparer.Ordinal);
        foreach (var relationship in graph.Relationships.Where(static relationship =>
                     relationship.Kind is ProjectGraphRelationshipKind.ProjectReference))
        {
            var (from, to) = relationship.Direction
                is ProjectGraphRelationshipDirection.SourceDependsOnTarget
                ? (relationship.SourceId, relationship.TargetId)
                : (relationship.TargetId, relationship.SourceId);
            if (nodes.ContainsKey(from) && nodes.ContainsKey(to))
            {
                outgoing[from].Add(new PathEdge(to, relationship));
            }
        }

        foreach (var edges in outgoing.Values)
        {
            edges.Sort(static (left, right) => StringComparer.Ordinal.Compare(
                left.Relationship.Id,
                right.Relationship.Id));
        }

        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [fromId] = 0 };
        var parents = new Dictionary<string, List<PathParent>>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(fromId);
        int? shortestDepth = null;
        var depthLimited = false;
        while (queue.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var depth = distances[current];
            if (shortestDepth is not null && depth >= shortestDepth)
            {
                continue;
            }

            if (depth == maxDepth)
            {
                depthLimited = true;
                continue;
            }

            foreach (var edge in outgoing[current])
            {
                var nextDepth = depth + 1;
                if (!distances.TryGetValue(edge.TargetId, out var knownDepth))
                {
                    distances.Add(edge.TargetId, nextDepth);
                    parents.Add(edge.TargetId, [new PathParent(current, edge.Relationship)]);
                    queue.Enqueue(edge.TargetId);
                }
                else if (knownDepth == nextDepth)
                {
                    parents[edge.TargetId].Add(new PathParent(current, edge.Relationship));
                }
                else
                {
                    continue;
                }

                if (edge.TargetId.Equals(toId, StringComparison.Ordinal))
                {
                    shortestDepth = nextDepth;
                }
            }
        }

        if (shortestDepth is null)
        {
            return new ProjectDependencyPathSearch([], null, depthLimited, true);
        }

        var paths = new List<ProjectDependencyPath>();
        var reversedNodes = new List<ProjectGraphNode> { nodes[toId] };
        var reversedRelationships = new List<ProjectGraphRelationship>();
        var totalKnown = true;
        Reconstruct(toId);
        return new ProjectDependencyPathSearch(
            paths.OrderBy(PathKey, StringComparer.Ordinal).ToArray(),
            shortestDepth,
            depthLimited,
            totalKnown);

        void Reconstruct(string current)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!totalKnown)
            {
                return;
            }

            if (current.Equals(fromId, StringComparison.Ordinal))
            {
                if (paths.Count == maximumPaths)
                {
                    totalKnown = false;
                    return;
                }

                paths.Add(new ProjectDependencyPath(
                    reversedNodes.AsEnumerable().Reverse().ToArray(),
                    reversedRelationships.AsEnumerable().Reverse().ToArray()));
                return;
            }

            foreach (var parent in parents[current].OrderBy(
                         static parent => parent.Relationship.Id,
                         StringComparer.Ordinal))
            {
                reversedNodes.Add(nodes[parent.NodeId]);
                reversedRelationships.Add(parent.Relationship);
                Reconstruct(parent.NodeId);
                reversedRelationships.RemoveAt(reversedRelationships.Count - 1);
                reversedNodes.RemoveAt(reversedNodes.Count - 1);
            }
        }
    }

    private static string PathKey(ProjectDependencyPath path) => string.Join(
        "\u001F",
        path.Relationships.Select(static relationship => relationship.Id));

    private sealed record PathEdge(string TargetId, ProjectGraphRelationship Relationship);

    private sealed record PathParent(string NodeId, ProjectGraphRelationship Relationship);
}
