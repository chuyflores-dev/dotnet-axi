using DotNetAxi.Analysis;
using DotNetAxi.Contracts;

namespace DotNetAxi.Analysis.Tests;

public sealed class QueryPlannerTests
{
    [Fact]
    public void Selects_the_least_expensive_capable_engine()
    {
        var planner = new QueryPlanner(
        [
            Provider(
                "semantic",
                QueryEngineClass.Semantic,
                Candidate(
                    QueryAnalysisLevel.CandidateScopedSemantics,
                    EvidenceResolution.Semantic,
                    expectedProjectLoads:
                    [
                        "src/App/App.csproj",
                    ])),
            Provider(
                "text",
                QueryEngineClass.Text,
                Candidate(
                    QueryAnalysisLevel.Discovery,
                    EvidenceResolution.Text)),
            Provider(
                "syntax",
                QueryEngineClass.Syntax,
                Candidate(
                    QueryAnalysisLevel.Discovery,
                    EvidenceResolution.Syntax)),
        ]);

        var plan = planner.CreatePlan(
            Request(EvidenceResolution.Syntax));

        Assert.Equal("syntax", plan.Engine);
        Assert.Equal(QueryEngineClass.Syntax, plan.EngineClass);
        Assert.Equal(QueryAnalysisLevel.Discovery, plan.AnalysisLevel);
        Assert.Equal(EvidenceResolution.Syntax, plan.PlannedResolution);
        Assert.Equal(CoverageLevel.Partial, plan.PlannedCoverage);
        Assert.False(plan.CompleteAnalysisRequired);
    }

    [Fact]
    public void Complete_scope_explicitly_escalates_past_a_partial_engine()
    {
        var planner = new QueryPlanner(
        [
            Provider(
                "candidate-semantics",
                QueryEngineClass.Semantic,
                Candidate(
                    QueryAnalysisLevel.CandidateScopedSemantics,
                    EvidenceResolution.Semantic,
                    coverage: CoverageLevel.Partial,
                    expectedProjectLoads:
                    [
                        "src/App/App.csproj",
                    ])),
            Provider(
                "complete-semantics",
                QueryEngineClass.Semantic,
                Candidate(
                    QueryAnalysisLevel.Complete,
                    EvidenceResolution.Semantic,
                    coverage: CoverageLevel.Complete,
                    expectedProjectLoads:
                    [
                        "src/App/App.csproj",
                        "src/Core/Core.csproj",
                    ])),
        ]);

        var plan = planner.CreatePlan(
            Request(
                EvidenceResolution.Semantic,
                requireCompleteScope: true));

        Assert.Equal("complete-semantics", plan.Engine);
        Assert.Equal(CoverageLevel.Complete, plan.PlannedCoverage);
        Assert.True(plan.CompleteAnalysisRequired);
        Assert.Equal(
            [
                "src/App/App.csproj",
                "src/Core/Core.csproj",
            ],
            plan.ExpectedProjectLoads);
    }

    [Fact]
    public void Preserves_fixed_workspace_selectors_in_the_plan()
    {
        var selectors = new WorkspaceSelectors(
            solution: "Repository.slnx",
            project: "src/App/App.csproj",
            configuration: "Release",
            framework: "net10.0");
        var planner = new QueryPlanner(
        [
            Provider(
                "syntax",
                QueryEngineClass.Syntax,
                Candidate(
                    QueryAnalysisLevel.Discovery,
                    EvidenceResolution.Syntax)),
        ]);

        var plan = planner.CreatePlan(
            new QueryPlanningRequest(
                EvidenceResolution.Syntax,
                requireCompleteScope: false,
                selectors));

        Assert.Same(selectors, plan.Selectors);
        Assert.Equal("Repository.slnx", plan.Selectors.Solution);
        Assert.Equal("src/App/App.csproj", plan.Selectors.Project);
        Assert.Equal("Release", plan.Selectors.Configuration);
        Assert.Equal("net10.0", plan.Selectors.Framework);
    }

    [Fact]
    public void Fails_instead_of_downgrading_an_unavailable_complete_scope()
    {
        var planner = new QueryPlanner(
        [
            Provider(
                "candidate-semantics",
                QueryEngineClass.Semantic,
                Candidate(
                    QueryAnalysisLevel.CandidateScopedSemantics,
                    EvidenceResolution.Semantic)),
        ]);

        var exception = Assert.Throws<QueryPlanUnavailableException>(
            () => planner.CreatePlan(
                Request(
                    EvidenceResolution.Semantic,
                    requireCompleteScope: true)));

        Assert.Equal(
            EvidenceResolution.Semantic,
            exception.RequiredResolution);
        Assert.True(exception.RequireCompleteScope);
    }

    private static QueryPlanningRequest Request(
        EvidenceResolution resolution,
        bool requireCompleteScope = false) =>
        new(
            resolution,
            requireCompleteScope,
            WorkspaceSelectors.Empty);

    private static QueryPlanCandidate Candidate(
        QueryAnalysisLevel analysisLevel,
        EvidenceResolution resolution,
        CoverageLevel coverage = CoverageLevel.Partial,
        IEnumerable<string>? expectedProjectLoads = null) =>
        new(
            analysisLevel,
            resolution,
            coverage,
            "Selected candidate files",
            expectedProjectLoads);

    private static FakeQueryPlanProvider Provider(
        string engine,
        QueryEngineClass engineClass,
        QueryPlanCandidate candidate) =>
        new(engine, engineClass, _ => candidate);

    private sealed class FakeQueryPlanProvider(
        string engine,
        QueryEngineClass engineClass,
        Func<QueryPlanningRequest, QueryPlanCandidate?> createPlan)
        : IQueryPlanProvider
    {
        public string Engine { get; } = engine;

        public QueryEngineClass EngineClass { get; } = engineClass;

        public QueryPlanCandidate? TryCreatePlan(
            QueryPlanningRequest request) =>
            createPlan(request);
    }
}
