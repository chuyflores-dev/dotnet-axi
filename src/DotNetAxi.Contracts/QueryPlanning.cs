namespace DotNetAxi.Contracts;

public enum QueryEngineClass
{
    Catalog,
    Text,
    Structural,
    Syntax,
    Semantic,
    ProjectGraph,
    Sdk,
}

public enum QueryAnalysisLevel
{
    RepositoryCatalog,
    Discovery,
    CandidateScopedSemantics,
    DependencyAwareExpansion,
    Complete,
}

public sealed record WorkspaceSelectors
{
    public WorkspaceSelectors(
        string? solution = null,
        string? project = null,
        string? configuration = null,
        string? framework = null)
    {
        Solution = ContractGuards.OptionalText(solution, nameof(solution));
        Project = ContractGuards.OptionalText(project, nameof(project));
        Configuration = ContractGuards.OptionalText(
            configuration,
            nameof(configuration));
        Framework = ContractGuards.OptionalText(framework, nameof(framework));
    }

    public static WorkspaceSelectors Empty { get; } = new();

    public string? Solution { get; }

    public string? Project { get; }

    public string? Configuration { get; }

    public string? Framework { get; }
}

public sealed record QueryPlanningRequest
{
    public QueryPlanningRequest(
        EvidenceResolution requiredResolution,
        bool requireCompleteScope,
        WorkspaceSelectors selectors)
    {
        if (!Enum.IsDefined(requiredResolution))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredResolution),
                requiredResolution,
                "The required evidence resolution is not defined.");
        }

        RequiredResolution = requiredResolution;
        RequireCompleteScope = requireCompleteScope;
        Selectors = selectors
            ?? throw new ArgumentNullException(nameof(selectors));
    }

    public EvidenceResolution RequiredResolution { get; }

    public bool RequireCompleteScope { get; }

    public WorkspaceSelectors Selectors { get; }
}

public sealed record QueryPlanCandidate
{
    public QueryPlanCandidate(
        QueryAnalysisLevel analysisLevel,
        EvidenceResolution resolution,
        CoverageLevel coverage,
        string candidateScope,
        IEnumerable<string>? expectedProjectLoads = null)
    {
        if (!Enum.IsDefined(analysisLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(analysisLevel),
                analysisLevel,
                "The query analysis level is not defined.");
        }

        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution),
                resolution,
                "The evidence resolution is not defined.");
        }

        if (!Enum.IsDefined(coverage) ||
            coverage is CoverageLevel.NotApplicable)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coverage),
                coverage,
                "A query plan must provide partial or complete coverage.");
        }

        AnalysisLevel = analysisLevel;
        Resolution = resolution;
        Coverage = coverage;
        CandidateScope = ContractGuards.RequiredText(
            candidateScope,
            nameof(candidateScope));
        ExpectedProjectLoads = ContractGuards.CopyText(
            expectedProjectLoads,
            nameof(expectedProjectLoads));
    }

    public QueryAnalysisLevel AnalysisLevel { get; }

    public EvidenceResolution Resolution { get; }

    public CoverageLevel Coverage { get; }

    public string CandidateScope { get; }

    public IReadOnlyList<string> ExpectedProjectLoads { get; }

    public bool CompleteAnalysisRequired =>
        AnalysisLevel is QueryAnalysisLevel.Complete;
}

public interface IQueryPlanProvider
{
    string Engine { get; }

    QueryEngineClass EngineClass { get; }

    QueryPlanCandidate? TryCreatePlan(QueryPlanningRequest request);
}

public sealed record QueryPlan
{
    public QueryPlan(
        string engine,
        QueryEngineClass engineClass,
        QueryPlanCandidate candidate,
        WorkspaceSelectors selectors)
    {
        Engine = ContractGuards.RequiredText(engine, nameof(engine));

        if (!Enum.IsDefined(engineClass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(engineClass),
                engineClass,
                "The query engine class is not defined.");
        }

        EngineClass = engineClass;
        Candidate = candidate
            ?? throw new ArgumentNullException(nameof(candidate));
        Selectors = selectors
            ?? throw new ArgumentNullException(nameof(selectors));
    }

    public string Engine { get; }

    public QueryEngineClass EngineClass { get; }

    public QueryAnalysisLevel AnalysisLevel => Candidate.AnalysisLevel;

    public EvidenceResolution PlannedResolution => Candidate.Resolution;

    public CoverageLevel PlannedCoverage => Candidate.Coverage;

    public string CandidateScope => Candidate.CandidateScope;

    public IReadOnlyList<string> ExpectedProjectLoads =>
        Candidate.ExpectedProjectLoads;

    public bool CompleteAnalysisRequired =>
        Candidate.CompleteAnalysisRequired;

    public WorkspaceSelectors Selectors { get; }

    private QueryPlanCandidate Candidate { get; }
}
