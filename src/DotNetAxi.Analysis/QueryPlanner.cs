using DotNetAxi.Contracts;

namespace DotNetAxi.Analysis;

public sealed class QueryPlanner
{
    private readonly IReadOnlyList<ProviderRegistration> _providers;

    public QueryPlanner(IEnumerable<IQueryPlanProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var registrations = providers
            .Select(CreateRegistration)
            .ToArray();
        if (registrations.Length == 0)
        {
            throw new ArgumentException(
                "At least one query-plan provider is required.",
                nameof(providers));
        }

        var duplicate = registrations
            .GroupBy(
                static registration => registration.Engine,
                StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Query engine '{duplicate.Key}' is registered more than once.",
                nameof(providers));
        }

        _providers = Array.AsReadOnly(registrations);
    }

    public QueryPlan CreatePlan(QueryPlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selected = _providers
            .Select(registration => new PlanOffer(
                registration,
                registration.Provider.TryCreatePlan(request)))
            .Where(static offer => offer.Candidate is not null)
            .Select(static offer => new PlanOffer(
                offer.Registration,
                offer.Candidate!))
            .Where(offer => Satisfies(offer.Candidate!, request))
            .OrderBy(static offer => offer.Candidate!.AnalysisLevel)
            .ThenBy(static offer => offer.Candidate!.ExpectedProjectLoads.Count)
            .ThenBy(static offer => offer.Candidate!.Resolution)
            .ThenBy(static offer => offer.Registration.EngineClass)
            .ThenBy(
                static offer => offer.Registration.Engine,
                StringComparer.Ordinal)
            .FirstOrDefault();

        if (selected is null)
        {
            throw new QueryPlanUnavailableException(
                request.RequiredResolution,
                request.RequireCompleteScope);
        }

        return new QueryPlan(
            selected.Registration.Engine,
            selected.Registration.EngineClass,
            selected.Candidate!,
            request.Selectors);
    }

    private static ProviderRegistration CreateRegistration(
        IQueryPlanProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.Engine))
        {
            throw new ArgumentException(
                "Query-plan providers require a non-empty engine identifier.",
                nameof(provider));
        }

        if (!Enum.IsDefined(provider.EngineClass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider.EngineClass,
                "The query engine class is not defined.");
        }

        return new ProviderRegistration(
            provider,
            provider.Engine,
            provider.EngineClass);
    }

    private static bool Satisfies(
        QueryPlanCandidate candidate,
        QueryPlanningRequest request) =>
        candidate.Resolution >= request.RequiredResolution &&
        (!request.RequireCompleteScope ||
         candidate.Coverage is CoverageLevel.Complete);

    private sealed record ProviderRegistration(
        IQueryPlanProvider Provider,
        string Engine,
        QueryEngineClass EngineClass);

    private sealed record PlanOffer(
        ProviderRegistration Registration,
        QueryPlanCandidate? Candidate);
}

public sealed class QueryPlanUnavailableException : InvalidOperationException
{
    public QueryPlanUnavailableException(
        EvidenceResolution requiredResolution,
        bool requireCompleteScope)
        : base(CreateMessage(requiredResolution, requireCompleteScope))
    {
        RequiredResolution = requiredResolution;
        RequireCompleteScope = requireCompleteScope;
    }

    public EvidenceResolution RequiredResolution { get; }

    public bool RequireCompleteScope { get; }

    private static string CreateMessage(
        EvidenceResolution requiredResolution,
        bool requireCompleteScope)
    {
        var coverage = requireCompleteScope
            ? "complete"
            : "partial or complete";
        return $"No query engine can provide {requiredResolution.ToString().ToLowerInvariant()} resolution with {coverage} coverage.";
    }
}
