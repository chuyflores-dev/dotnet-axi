namespace DotNetAxi.Testing;

public sealed record AgentTaskCorpus(
    string Id,
    string Version,
    IReadOnlyList<AgentTaskDefinition> Tasks)
{
    public IReadOnlyList<AgentTaskDefinition> SelectApplicableTasks(
        string milestone,
        IEnumerable<string> availableCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestone);
        ArgumentNullException.ThrowIfNull(availableCapabilities);
        if (!System.Version.TryParse(milestone, out var selectedMilestone)
            || selectedMilestone.Build < 0
            || selectedMilestone.Revision >= 0)
        {
            throw new ArgumentException(
                "Milestone must be an explicit major.minor.patch version.",
                nameof(milestone));
        }

        var capabilities = new HashSet<string>(
            availableCapabilities,
            StringComparer.Ordinal);
        return Array.AsReadOnly(
            Tasks
                .Where(task =>
                    System.Version.Parse(task.Milestone)
                        <= selectedMilestone
                    && task.RequiredCapabilities.All(capabilities.Contains))
                .ToArray());
    }
}

public sealed record AgentTaskDefinition(
    string Id,
    string Milestone,
    IReadOnlyList<string> RequiredCapabilities,
    string Prompt,
    AgentTaskRepositoryState Repository,
    AgentTaskApplicability Applicability,
    AgentTaskExecutionPolicy Execution,
    AgentTaskSuccessOracle SuccessOracle,
    AgentTaskSafetyOracle SafetyOracle,
    IReadOnlyList<string> RequiredValidation);

public sealed record AgentTaskRepositoryState(
    string FixtureManifest,
    string FixtureName,
    int FixtureSeed,
    string ContentHash,
    string State);

public sealed record AgentTaskApplicability(
    bool Baseline,
    bool Candidate);

public sealed record AgentTaskExecutionPolicy(
    IReadOnlyList<string> PermittedTools,
    int TimeoutSeconds,
    string Network,
    string Locale,
    string TimeZone);

public sealed record AgentTaskSuccessOracle(
    string Kind,
    string? Normalizer,
    IReadOnlyList<string> ExpectedFacts,
    AgentTaskModelJudge? ModelJudge);

public sealed record AgentTaskModelJudge(
    string Version,
    bool ConditionBlinded,
    string Rubric);

public sealed record AgentTaskSafetyOracle(
    string Kind,
    IReadOnlyList<string> Checks);
