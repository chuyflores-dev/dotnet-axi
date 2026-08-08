using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DotNetAxi.Testing;

public enum AgentBenchmarkCondition
{
    Baseline,
    Candidate,
}

public enum AgentBenchmarkDispatch
{
    Manual,
    ContinuousIntegration,
}

public sealed record AgentBenchmarkConditionConfiguration(
    AgentBenchmarkCondition Condition,
    string InstructionsHash,
    string ToolConfigurationHash);

public sealed record AgentBenchmarkExecutionSettings(
    string AgentVersion,
    string ModelId,
    string ReasoningSetting,
    string SettingsHash,
    string Sandbox,
    string PermissionProfile,
    string NetworkPolicy);

public sealed record AgentBenchmarkProvenance(
    string HarnessVersion,
    string FixtureCommit,
    string ProductCommit,
    string ProductSchema);

public sealed record AgentBenchmarkConfiguration(
    string SeriesId,
    string CorpusDirectory,
    AgentBenchmarkDispatch Dispatch,
    int RunsPerTask,
    ulong RandomizationSeed,
    int MaximumStartAttempts,
    TimeSpan CleanupTimeout,
    AgentBenchmarkExecutionSettings Execution,
    AgentBenchmarkProvenance Provenance,
    AgentBenchmarkConditionConfiguration Baseline,
    AgentBenchmarkConditionConfiguration Candidate);

public sealed record AgentBenchmarkAdapterDescriptor(
    string Id,
    string Version);

internal sealed record AgentBenchmarkScheduledRun(
    string RunId,
    string TaskId,
    AgentBenchmarkCondition Condition,
    int Repetition,
    int ExecutionOrder);

internal sealed class AgentBenchmarkPreparedSeries
{
    internal AgentBenchmarkPreparedSeries(
        AgentBenchmarkSeriesManifest manifest,
        IReadOnlyList<AgentBenchmarkScheduledRun> schedule)
    {
        Manifest = manifest with
        {
            Execution = manifest.Execution with { },
            Provenance = manifest.Provenance with { },
            Baseline = manifest.Baseline with { },
            Candidate = manifest.Candidate with { },
            Adapter = manifest.Adapter with { },
        };
        Schedule = AgentBenchmarkSnapshots.List(
            schedule.Select(static run => run with { }));
    }

    public AgentBenchmarkSeriesManifest Manifest { get; }

    public IReadOnlyList<AgentBenchmarkScheduledRun> Schedule { get; }
}

internal interface IAgentBenchmarkRunSink
{
    ValueTask RetainAsync(
        AgentBenchmarkSeriesManifest manifest,
        AgentBenchmarkRunResult run,
        CancellationToken cancellationToken = default);
}

public interface IAgentBenchmarkAdapter
{
    AgentBenchmarkAdapterDescriptor Descriptor { get; }

    ValueTask PrepareWorkspaceAsync(
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    ValueTask<IAgentBenchmarkExecution> StartAsync(
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken = default);
}

public interface IAgentBenchmarkExecution : IAsyncDisposable
{
    Task<AgentBenchmarkAdapterResult> Completion { get; }

    AgentBenchmarkProgressSnapshot GetProgressSnapshot();

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentBenchmarkAdapterInput
{
    internal AgentBenchmarkAdapterInput(
        string runId,
        int startAttempt,
        int executionOrder,
        int repetition,
        AgentBenchmarkCondition condition,
        AgentTaskDefinition task,
        string workspacePath,
        IReadOnlyDictionary<string, string> environmentVariables,
        AgentBenchmarkExecutionSettings execution,
        string promptHash,
        string instructionsHash,
        string toolConfigurationHash)
    {
        RunId = runId;
        StartAttempt = startAttempt;
        ExecutionOrder = executionOrder;
        Repetition = repetition;
        Condition = condition;
        Task = AgentBenchmarkSnapshots.Task(task);
        WorkspacePath = workspacePath;
        EnvironmentVariables = AgentBenchmarkSnapshots.Dictionary(
            environmentVariables);
        Execution = execution with { };
        PromptHash = promptHash;
        InstructionsHash = instructionsHash;
        ToolConfigurationHash = toolConfigurationHash;
    }

    public string RunId { get; }

    public int StartAttempt { get; }

    public int ExecutionOrder { get; }

    public int Repetition { get; }

    public AgentBenchmarkCondition Condition { get; }

    public AgentTaskDefinition Task { get; }

    public string WorkspacePath { get; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    public AgentBenchmarkExecutionSettings Execution { get; }

    public string PromptHash { get; }

    public string InstructionsHash { get; }

    public string ToolConfigurationHash { get; }
}

public sealed record AgentBenchmarkObservedConfiguration(
    string AgentVersion,
    string ModelId,
    string ReasoningSetting,
    string SettingsHash,
    string Sandbox,
    string PermissionProfile,
    string NetworkPolicy,
    string TaskContentHash,
    string PromptHash,
    string InstructionsHash,
    string ToolConfigurationHash);

public sealed record AgentBenchmarkToolCall(
    int Sequence,
    string ToolClass,
    string Name,
    string InputHash,
    bool Succeeded);

public sealed record AgentBenchmarkRawEvent(
    int Sequence,
    string Kind,
    string Payload,
    string PayloadHash);

public sealed record AgentBenchmarkInspectedScope(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Projects);

public sealed record AgentBenchmarkAdapterResult(
    string Status,
    string Answer,
    long InputTokens,
    long OutputTokens,
    int Turns,
    IReadOnlyList<AgentBenchmarkToolCall> ToolCalls,
    AgentBenchmarkInspectedScope InspectedScope,
    bool ClaimsSupported,
    bool NetworkUsed,
    AgentBenchmarkObservedConfiguration ObservedConfiguration,
    IReadOnlyList<AgentBenchmarkRawEvent> RawEvents);

public sealed record AgentBenchmarkProgressSnapshot(
    long InputTokens,
    long OutputTokens,
    int Turns,
    IReadOnlyList<AgentBenchmarkToolCall> ToolCalls,
    AgentBenchmarkInspectedScope InspectedScope,
    IReadOnlyList<AgentBenchmarkRawEvent> RawEvents);

public sealed record AgentBenchmarkValidationResult(
    string Id,
    bool Executed,
    bool Passed,
    string Detail);

public sealed record AgentBenchmarkSafetyCheckResult(
    string Id,
    bool Passed,
    string Detail);

public sealed record AgentBenchmarkRunVersions(
    string HarnessVersion,
    string AdapterId,
    string AdapterVersion,
    string AgentVersion,
    string ModelId,
    string ReasoningSetting,
    string CorpusVersion,
    string ProductSchema);

public sealed record AgentBenchmarkRunHashes(
    string Settings,
    string Prompt,
    string Instructions,
    string ToolConfiguration,
    string FixtureContent,
    string WorkspaceBefore,
    string WorkspaceAfter,
    string FixtureCommit,
    string ProductCommit,
    string RawTrajectory);

public sealed class AgentBenchmarkRunResult
{
    [JsonConstructor]
    internal AgentBenchmarkRunResult(
        string runId,
        string taskId,
        AgentBenchmarkCondition condition,
        int repetition,
        int executionOrder,
        int startAttempts,
        int timeoutSeconds,
        bool timedOut,
        string status,
        string answer,
        bool success,
        bool safe,
        long inputTokens,
        long outputTokens,
        int turns,
        IReadOnlyList<AgentBenchmarkToolCall> toolCalls,
        TimeSpan duration,
        AgentBenchmarkInspectedScope inspectedScope,
        IReadOnlyList<AgentBenchmarkSafetyCheckResult> safetyChecks,
        IReadOnlyList<AgentBenchmarkValidationResult> validations,
        AgentBenchmarkRunVersions versions,
        AgentBenchmarkRunHashes hashes,
        string sandbox,
        string permissionProfile,
        string networkPolicy,
        IReadOnlyList<AgentBenchmarkRawEvent> rawEvents)
    {
        RunId = runId;
        TaskId = taskId;
        Condition = condition;
        Repetition = repetition;
        ExecutionOrder = executionOrder;
        StartAttempts = startAttempts;
        TimeoutSeconds = timeoutSeconds;
        TimedOut = timedOut;
        Status = status;
        Answer = answer;
        Success = success;
        Safe = safe;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Turns = turns;
        ToolCalls = AgentBenchmarkSnapshots.List(
            toolCalls.Select(static call => call with { }));
        Duration = duration;
        InspectedScope = AgentBenchmarkSnapshots.Scope(inspectedScope);
        SafetyChecks = AgentBenchmarkSnapshots.List(
            safetyChecks.Select(static check => check with { }));
        Validations = AgentBenchmarkSnapshots.List(
            validations.Select(static validation => validation with { }));
        Versions = versions with { };
        Hashes = hashes with { };
        Sandbox = sandbox;
        PermissionProfile = permissionProfile;
        NetworkPolicy = networkPolicy;
        RawEvents = AgentBenchmarkSnapshots.List(
            rawEvents.Select(static rawEvent => rawEvent with { }));
    }

    public string RunId { get; }

    public string TaskId { get; }

    public AgentBenchmarkCondition Condition { get; }

    public int Repetition { get; }

    public int ExecutionOrder { get; }

    public int StartAttempts { get; }

    public int TimeoutSeconds { get; }

    public bool TimedOut { get; }

    public string Status { get; }

    public string Answer { get; }

    public bool Success { get; }

    public bool Safe { get; }

    public long InputTokens { get; }

    public long OutputTokens { get; }

    public long TotalTokens => checked(InputTokens + OutputTokens);

    public int Turns { get; }

    public IReadOnlyList<AgentBenchmarkToolCall> ToolCalls { get; }

    public int ToolCallCount => ToolCalls.Count;

    public TimeSpan Duration { get; }

    public AgentBenchmarkInspectedScope InspectedScope { get; }

    public IReadOnlyList<AgentBenchmarkSafetyCheckResult> SafetyChecks { get; }

    public IReadOnlyList<AgentBenchmarkValidationResult> Validations { get; }

    public AgentBenchmarkRunVersions Versions { get; }

    public AgentBenchmarkRunHashes Hashes { get; }

    public string PermissionProfile { get; }

    public string Sandbox { get; }

    public string NetworkPolicy { get; }

    public IReadOnlyList<AgentBenchmarkRawEvent> RawEvents { get; }
}

public sealed record AgentBenchmarkSeriesManifest(
    string Schema,
    string SeriesId,
    string CorpusId,
    string CorpusVersion,
    AgentBenchmarkDispatch Dispatch,
    int RunsPerTask,
    ulong RandomizationSeed,
    AgentBenchmarkExecutionSettings Execution,
    AgentBenchmarkProvenance Provenance,
    AgentBenchmarkConditionConfiguration Baseline,
    AgentBenchmarkConditionConfiguration Candidate,
    AgentBenchmarkAdapterDescriptor Adapter);

public sealed class AgentBenchmarkSeriesResult
{
    internal AgentBenchmarkSeriesResult(
        AgentBenchmarkSeriesManifest manifest,
        IReadOnlyList<AgentBenchmarkRunResult> runs)
    {
        Manifest = manifest with
        {
            Execution = manifest.Execution with { },
            Provenance = manifest.Provenance with { },
            Baseline = manifest.Baseline with { },
            Candidate = manifest.Candidate with { },
            Adapter = manifest.Adapter with { },
        };
        Runs = AgentBenchmarkSnapshots.List(runs);
    }

    public AgentBenchmarkSeriesManifest Manifest { get; }

    public IReadOnlyList<AgentBenchmarkRunResult> Runs { get; }
}

public class AgentBenchmarkException : Exception
{
    public AgentBenchmarkException(string message)
        : base(message)
    {
    }

    public AgentBenchmarkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class AgentBenchmarkStartException : AgentBenchmarkException
{
    public AgentBenchmarkStartException(string message, bool retryable)
        : base(message)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}

internal static class AgentBenchmarkSnapshots
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    public static IReadOnlyDictionary<string, string> Dictionary(
        IEnumerable<KeyValuePair<string, string>> values) =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.Ordinal));

    public static AgentBenchmarkInspectedScope Scope(
        AgentBenchmarkInspectedScope scope) =>
        new(
            List(scope.Files),
            List(scope.Projects));

    public static AgentTaskDefinition Task(AgentTaskDefinition task) =>
        task with
        {
            RequiredCapabilities = List(task.RequiredCapabilities),
            Repository = task.Repository with { },
            Applicability = task.Applicability with { },
            Execution = task.Execution with
            {
                PermittedTools = List(task.Execution.PermittedTools),
            },
            SuccessOracle = task.SuccessOracle with
            {
                ExpectedFacts = List(task.SuccessOracle.ExpectedFacts),
                ModelJudge = task.SuccessOracle.ModelJudge is null
                    ? null
                    : task.SuccessOracle.ModelJudge with { },
            },
            SafetyOracle = task.SafetyOracle with
            {
                Checks = List(task.SafetyOracle.Checks),
            },
            RequiredValidation = List(task.RequiredValidation),
        };
}
