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
