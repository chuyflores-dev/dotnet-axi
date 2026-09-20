using System.Text.Json;
using DotNetAxi.Contracts;
using DotNetAxi.Graph;

namespace DotNetAxi.Graph.Tests;

public sealed class ProjectDependencyGraphTests
{
    [Fact]
    public void Project_and_package_nodes_have_deterministic_domain_identities()
    {
        var first = new ProjectGraphVariant("src/App/App.csproj", "Release", "net10.0");
        var second = new ProjectGraphVariant("src/App/App.csproj", "Release", "net10.0");

        Assert.Equal(
            ProjectGraphIdentity.CreateProject(first),
            ProjectGraphIdentity.CreateProject(second));
        Assert.NotEqual(
            ProjectGraphIdentity.CreateProject(first),
            ProjectGraphIdentity.CreateProject(
                new ProjectGraphVariant("src/App/App.csproj", "Release", "net9.0")));
        Assert.Equal(
            ProjectGraphIdentity.CreatePackage("Example.Package", "2.0.0"),
            ProjectGraphIdentity.CreatePackage("example.package", "2.0.0"));
    }

    [Fact]
    public void Composition_preserves_row_evidence_and_orders_domain_rows()
    {
        var complete = RowEvidence(
            CoverageLevel.Complete,
            EvidenceConfidence.Verified,
            ProjectGraphProvenance.EvaluatedProjectGraph);
        var partial = RowEvidence(
            CoverageLevel.Partial,
            EvidenceConfidence.Unknown,
            ProjectGraphProvenance.EvaluatedPackageReference);
        var app = ProjectGraphNode.CreateProject(
            new ProjectGraphVariant("src/App/App.csproj", "Release", "net10.0"),
            ProjectGraphEvaluationState.Evaluated,
            complete);
        var library = ProjectGraphNode.CreateProject(
            new ProjectGraphVariant("src/Library/Library.csproj", "Release", "net10.0"),
            ProjectGraphEvaluationState.Evaluated,
            complete);
        var package = ProjectGraphNode.CreatePackage(
            "Example.Package",
            packageVersion: null,
            ProjectGraphEvaluationState.Incomplete,
            partial);
        var projectReference = new ProjectGraphRelationship(
            ProjectGraphRelationshipKind.ProjectReference,
            app.Id,
            library.Id,
            ProjectGraphRelationshipDirection.SourceDependsOnTarget,
            "Release",
            "net10.0",
            complete);
        var packageReference = new ProjectGraphRelationship(
            ProjectGraphRelationshipKind.PackageReference,
            app.Id,
            package.Id,
            ProjectGraphRelationshipDirection.SourceDependsOnTarget,
            "Release",
            "net10.0",
            partial);

        var graph = new ProjectDependencyGraph(
            ResponseEvidence(),
            [package, library, app],
            [packageReference, projectReference]);

        Assert.Equal(
            graph.Nodes.OrderBy(static node => node.Id, StringComparer.Ordinal).Select(static node => node.Id),
            graph.Nodes.Select(static node => node.Id));
        Assert.Equal(
            graph.Relationships.OrderBy(static relationship => relationship.Id, StringComparer.Ordinal).Select(static relationship => relationship.Id),
            graph.Relationships.Select(static relationship => relationship.Id));
        Assert.Equal(ProjectGraphNodeKind.Package, package.Kind);
        Assert.Equal(ProjectGraphEvaluationState.Incomplete, package.EvaluationState);
        Assert.Equal(EvidenceConfidence.Unknown, packageReference.Evidence.Confidence);
        Assert.Equal(CoverageLevel.Partial, packageReference.Evidence.Coverage.Level);
        Assert.Equal(ProjectGraphProvenance.EvaluatedPackageReference, packageReference.Evidence.Provenance);
        Assert.Equal("Release", packageReference.Configuration);
        Assert.Equal("net10.0", packageReference.Framework);
    }

    [Fact]
    public void Serialization_exposes_typed_project_contract_without_backend_objects()
    {
        var evidence = RowEvidence(
            CoverageLevel.Complete,
            EvidenceConfidence.Verified,
            ProjectGraphProvenance.EvaluatedProjectGraph);
        var node = ProjectGraphNode.CreateProject(
            new ProjectGraphVariant("src/App/App.csproj", "Debug", "net10.0"),
            ProjectGraphEvaluationState.Evaluated,
            evidence);
        var graph = new ProjectDependencyGraph(ResponseEvidence(), [node], []);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(graph));
        var serializedNode = document.RootElement.GetProperty("Nodes")[0];

        Assert.Equal((int)ProjectGraphNodeKind.Project, serializedNode.GetProperty("Kind").GetInt32());
        Assert.Equal("src/App/App.csproj", serializedNode.GetProperty("Project").GetProperty("ProjectPath").GetString());
        Assert.Equal("Debug", serializedNode.GetProperty("Project").GetProperty("Configuration").GetString());
        Assert.Equal("net10.0", serializedNode.GetProperty("Project").GetProperty("Framework").GetString());
        Assert.Equal((int)ProjectGraphEvaluationState.Evaluated, serializedNode.GetProperty("EvaluationState").GetInt32());
        Assert.Equal(
            (int)ProjectGraphProvenance.EvaluatedProjectGraph,
            serializedNode.GetProperty("Evidence").GetProperty("Provenance").GetInt32());
    }

    [Fact]
    public void Relationships_require_existing_endpoints_but_allow_self_project_references()
    {
        var evidence = RowEvidence(
            CoverageLevel.Complete,
            EvidenceConfidence.Verified,
            ProjectGraphProvenance.EvaluatedProjectGraph);
        var app = ProjectGraphNode.CreateProject(
            new ProjectGraphVariant("src/App/App.csproj"),
            ProjectGraphEvaluationState.Evaluated,
            evidence);
        var other = ProjectGraphNode.CreateProject(
            new ProjectGraphVariant("src/Other/Other.csproj"),
            ProjectGraphEvaluationState.Evaluated,
            evidence);
        var relationship = new ProjectGraphRelationship(
            ProjectGraphRelationshipKind.ProjectReference,
            app.Id,
            other.Id,
            ProjectGraphRelationshipDirection.SourceDependsOnTarget,
            configuration: null,
            framework: null,
            evidence);

        var exception = Assert.Throws<ArgumentException>(
            () => new ProjectDependencyGraph(ResponseEvidence(), [app], [relationship]));

        Assert.Equal("relationships", exception.ParamName);

        var selfReference = new ProjectGraphRelationship(
            ProjectGraphRelationshipKind.ProjectReference,
            app.Id,
            app.Id,
            ProjectGraphRelationshipDirection.SourceDependsOnTarget,
            configuration: null,
            framework: null,
            evidence);
        var graph = new ProjectDependencyGraph(ResponseEvidence(), [app], [selfReference]);

        Assert.Same(selfReference, Assert.Single(graph.Relationships));
    }

    [Fact]
    public void Cycle_detection_normalizes_rotations_without_collapsing_overlapping_or_reverse_cycles()
    {
        var evidence = RowEvidence(
            CoverageLevel.Complete,
            EvidenceConfidence.Verified,
            ProjectGraphProvenance.EvaluatedProjectGraph);
        var a = Project("A.csproj", evidence);
        var b = Project("B.csproj", evidence);
        var c = Project("C.csproj", evidence);
        var graph = new ProjectDependencyGraph(
            ResponseEvidence(),
            [a, b, c],
            [
                Edge(a, a, evidence),
                Edge(a, b, evidence),
                Edge(b, a, evidence),
                Edge(b, c, evidence),
                Edge(c, a, evidence),
                Edge(a, c, evidence),
                Edge(c, b, evidence),
            ]);

        var detection = ProjectDependencyCycleDetector.FindCycles(graph, 100);
        var cycles = detection.Cycles;

        Assert.True(detection.TotalKnown);
        Assert.Equal(6, cycles.Count);
        Assert.Equal(
            ["A.csproj"],
            cycles.Single(cycle => cycle.Nodes.Count is 1)
                .Nodes.Select(static node => node.Project!.ProjectPath));
        Assert.Contains(cycles, cycle => MatchesCycle(cycle, "A.csproj", "B.csproj"));
        Assert.Contains(cycles, cycle => MatchesCycle(cycle, "A.csproj", "C.csproj"));
        Assert.Contains(cycles, cycle => MatchesCycle(cycle, "B.csproj", "C.csproj"));
        Assert.Contains(cycles, cycle => MatchesCycle(cycle, "A.csproj", "B.csproj", "C.csproj"));
        Assert.Contains(cycles, cycle => MatchesCycle(cycle, "A.csproj", "C.csproj", "B.csproj"));
        Assert.Equal(
            cycles.OrderBy(static cycle => cycle.Id, StringComparer.Ordinal)
                .Select(static cycle => cycle.Id),
            cycles.Select(static cycle => cycle.Id));
    }

    [Fact]
    public void Cycle_detection_returns_empty_without_reclassifying_partial_graph_evidence()
    {
        var evidence = RowEvidence(
            CoverageLevel.Partial,
            EvidenceConfidence.Unknown,
            ProjectGraphProvenance.EvaluatedProjectGraph);
        var a = Project("A.csproj", evidence);
        var b = Project("B.csproj", evidence);
        var graph = new ProjectDependencyGraph(
            new Evidence(
                "ws_partial",
                EvidenceResolution.Syntax,
                new EvidenceCoverage(
                    CoverageLevel.Partial,
                    partialReason: "Evaluation failed for one project."),
                EvidenceConfidence.Unknown,
                Scope()),
            [a, b],
            [Edge(a, b, evidence)]);

        var detection = ProjectDependencyCycleDetector.FindCycles(graph, 100);

        Assert.True(detection.TotalKnown);
        Assert.Empty(detection.Cycles);
        Assert.Equal(CoverageLevel.Partial, graph.Evidence.Coverage.Level);
        Assert.Equal(EvidenceConfidence.Unknown, graph.Evidence.Confidence);
    }

    [Fact]
    public void Cycle_detection_uses_conditional_evaluated_project_reference_variants()
    {
        var evidence = RowEvidence(
            CoverageLevel.Complete,
            EvidenceConfidence.Verified,
            ProjectGraphProvenance.EvaluatedProjectReference);
        var a = ProjectGraphNode.CreateProject(
            new ProjectGraphVariant("A.csproj", "Release", "net10.0"),
            ProjectGraphEvaluationState.Evaluated,
            evidence);
        var b = ProjectGraphNode.CreateProject(
            new ProjectGraphVariant("B.csproj", "Release", "net10.0"),
            ProjectGraphEvaluationState.Evaluated,
            evidence);
        var graph = new ProjectDependencyGraph(
            ResponseEvidence(),
            [a, b],
            [
                Edge(a, b, evidence, "Release", "net10.0"),
                Edge(b, a, evidence, "Release", "net10.0"),
            ]);

        var cycle = Assert.Single(ProjectDependencyCycleDetector.FindCycles(graph, 100).Cycles);

        Assert.True(MatchesCycle(cycle, "A.csproj", "B.csproj"));
        Assert.All(cycle.Relationships, relationship =>
        {
            Assert.Equal("Release", relationship.Configuration);
            Assert.Equal("net10.0", relationship.Framework);
        });
    }

    [Fact]
    public void Cycle_detection_stops_at_the_requested_safety_bound_with_unknown_total()
    {
        var evidence = RowEvidence(
            CoverageLevel.Complete,
            EvidenceConfidence.Verified,
            ProjectGraphProvenance.EvaluatedProjectGraph);
        var a = Project("A.csproj", evidence);
        var b = Project("B.csproj", evidence);
        var c = Project("C.csproj", evidence);
        var graph = new ProjectDependencyGraph(
            ResponseEvidence(),
            [a, b, c],
            [
                Edge(a, b, evidence), Edge(b, a, evidence),
                Edge(a, c, evidence), Edge(c, a, evidence),
            ]);

        var detection = ProjectDependencyCycleDetector.FindCycles(graph, maximumCycles: 1);

        Assert.Single(detection.Cycles);
        Assert.False(detection.TotalKnown);
    }

    [Fact]
    public void Cycle_detection_skips_dense_acyclic_regions_without_path_enumeration()
    {
        var evidence = RowEvidence(
            CoverageLevel.Complete,
            EvidenceConfidence.Verified,
            ProjectGraphProvenance.EvaluatedProjectGraph);
        var nodes = Enumerable.Range(0, 20)
            .Select(index => Project($"{index:D2}.csproj", evidence))
            .ToArray();
        var relationships = nodes.SelectMany((source, sourceIndex) => nodes
            .Skip(sourceIndex + 1)
            .Select(target => Edge(source, target, evidence)));
        var graph = new ProjectDependencyGraph(ResponseEvidence(), nodes, relationships);

        var detection = ProjectDependencyCycleDetector.FindCycles(graph, 1);

        Assert.True(detection.TotalKnown);
        Assert.Empty(detection.Cycles);
    }

    [Fact]
    public void Shortest_paths_are_deterministic_and_retain_equal_length_ties()
    {
        var evidence = RowEvidence(CoverageLevel.Complete, EvidenceConfidence.Verified, ProjectGraphProvenance.EvaluatedProjectGraph);
        var a = Project("A.csproj", evidence);
        var b = Project("B.csproj", evidence);
        var c = Project("C.csproj", evidence);
        var d = Project("D.csproj", evidence);
        var graph = new ProjectDependencyGraph(ResponseEvidence(), [a, b, c, d],
            [Edge(a, b, evidence), Edge(a, c, evidence), Edge(b, d, evidence), Edge(c, d, evidence)]);

        var search = ProjectDependencyPathFinder.FindShortestPaths(graph, a.Id, d.Id, 4, 10);

        Assert.Equal(2, search.ShortestDepth);
        Assert.True(search.TotalKnown);
        Assert.False(search.DepthLimited);
        Assert.Equal(2, search.Paths.Count);
        Assert.All(search.Paths, path => Assert.Equal(2, path.Relationships.Count));
        Assert.Equal(
            ["A.csproj", "B.csproj", "D.csproj"],
            search.Paths[0].Nodes.Select(static node => node.Project!.ProjectPath));
        Assert.Equal(
            ["A.csproj", "C.csproj", "D.csproj"],
            search.Paths[1].Nodes.Select(static node => node.Project!.ProjectPath));
    }

    [Fact]
    public void Path_search_reports_depth_limited_no_path()
    {
        var evidence = RowEvidence(CoverageLevel.Complete, EvidenceConfidence.Verified, ProjectGraphProvenance.EvaluatedProjectGraph);
        var a = Project("A.csproj", evidence);
        var b = Project("B.csproj", evidence);
        var c = Project("C.csproj", evidence);
        var graph = new ProjectDependencyGraph(ResponseEvidence(), [a, b, c], [Edge(a, b, evidence), Edge(b, c, evidence)]);

        var search = ProjectDependencyPathFinder.FindShortestPaths(graph, a.Id, c.Id, 1, 10);

        Assert.Empty(search.Paths);
        Assert.Null(search.ShortestDepth);
        Assert.True(search.DepthLimited);
    }

    [Fact]
    public void Path_search_finds_a_twelve_edge_path_at_the_selected_depth()
    {
        var evidence = RowEvidence(CoverageLevel.Complete, EvidenceConfidence.Verified, ProjectGraphProvenance.EvaluatedProjectGraph);
        var nodes = Enumerable.Range(0, 13)
            .Select(index => Project($"P{index}.csproj", evidence))
            .ToArray();
        var graph = new ProjectDependencyGraph(
            ResponseEvidence(),
            nodes,
            nodes.Zip(nodes.Skip(1), (source, target) => Edge(source, target, evidence)));

        var search = ProjectDependencyPathFinder.FindShortestPaths(graph, nodes[0].Id, nodes[12].Id, 12, 1);

        Assert.Equal(12, search.ShortestDepth);
        Assert.Single(search.Paths);
        Assert.Equal(12, search.Paths[0].Relationships.Count);
    }

    [Fact]
    public void Path_search_reports_a_complete_no_path_when_traversal_exhausts()
    {
        var evidence = RowEvidence(CoverageLevel.Complete, EvidenceConfidence.Verified, ProjectGraphProvenance.EvaluatedProjectGraph);
        var a = Project("A.csproj", evidence);
        var b = Project("B.csproj", evidence);
        var c = Project("C.csproj", evidence);
        var graph = new ProjectDependencyGraph(ResponseEvidence(), [a, b, c], [Edge(a, b, evidence)]);

        var search = ProjectDependencyPathFinder.FindShortestPaths(graph, a.Id, c.Id, 4, 10);

        Assert.Empty(search.Paths);
        Assert.Null(search.ShortestDepth);
        Assert.False(search.DepthLimited);
        Assert.True(search.TotalKnown);
    }

    private static ProjectGraphNode Project(
        string path,
        ProjectGraphEvidence evidence) =>
        ProjectGraphNode.CreateProject(
            new ProjectGraphVariant(path),
            ProjectGraphEvaluationState.Evaluated,
            evidence);

    private static ProjectGraphRelationship Edge(
        ProjectGraphNode source,
        ProjectGraphNode target,
        ProjectGraphEvidence evidence,
        string? configuration = null,
        string? framework = null) =>
        new(
            ProjectGraphRelationshipKind.ProjectReference,
            source.Id,
            target.Id,
            ProjectGraphRelationshipDirection.SourceDependsOnTarget,
            configuration,
            framework,
            evidence);

    private static string[] Paths(ProjectDependencyCycle cycle) =>
        cycle.Nodes.Select(static node => node.Project!.ProjectPath).ToArray();

    private static bool MatchesCycle(ProjectDependencyCycle cycle, params string[] expected)
    {
        var actual = Paths(cycle);
        if (actual.Length != expected.Length)
        {
            return false;
        }

        for (var start = 0; start < actual.Length; start++)
        {
            if (actual[start] != expected[0])
            {
                continue;
            }

            if (expected.Select((path, index) => actual[(start + index) % actual.Length] == path)
                .All(static match => match))
            {
                return true;
            }
        }

        return false;
    }

    private static ProjectGraphEvidence RowEvidence(
        CoverageLevel coverage,
        EvidenceConfidence confidence,
        ProjectGraphProvenance provenance)
        => new(
            Scope(),
            new EvidenceCoverage(
                coverage,
                partialReason: coverage is CoverageLevel.Partial
                    ? "A package version was not available from evaluation."
                    : null),
            confidence,
            provenance);

    private static Evidence ResponseEvidence()
        => new(
            "ws_123",
            EvidenceResolution.Syntax,
            new EvidenceCoverage(CoverageLevel.Complete),
            EvidenceConfidence.Verified,
            Scope());

    private static EvidenceScope Scope()
        => new(
            "/repo",
            "selected evaluated project variants",
            solution: "Workspace.slnx",
            projects: ["src/App/App.csproj"]);
}
