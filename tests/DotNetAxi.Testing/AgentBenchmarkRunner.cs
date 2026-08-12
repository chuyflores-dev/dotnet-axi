using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetAxi.Testing;

public sealed partial class AgentBenchmarkRunner
{
    private const int MinimumRunsPerCondition = 5;
    private const string ResultSchema =
        "dotnet-axi/agent-benchmark-result/v1";

    private readonly RepositoryFixtureFactory _fixtureFactory;
    private readonly TimeProvider _timeProvider;

    public AgentBenchmarkRunner(
        RepositoryFixtureFactory? fixtureFactory = null,
        TimeProvider? timeProvider = null)
    {
        _fixtureFactory = fixtureFactory ?? new RepositoryFixtureFactory();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<AgentBenchmarkSeriesResult> RunAsync(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        IAgentBenchmarkAdapter adapter,
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(
            corpus,
            configuration,
            adapter,
            runSink: null,
            cancellationToken);

    internal async ValueTask<AgentBenchmarkSeriesResult> RunRetainedAsync(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        IAgentBenchmarkAdapter adapter,
        IAgentBenchmarkRunSink runSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runSink);
        return await RunCoreAsync(
            corpus,
            configuration,
            adapter,
            runSink,
            cancellationToken);
    }

    internal AgentBenchmarkPreparedSeries Prepare(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        IAgentBenchmarkAdapter adapter) =>
        PrepareCore(corpus, configuration, adapter).Prepared;

    private async ValueTask<AgentBenchmarkSeriesResult> RunCoreAsync(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        IAgentBenchmarkAdapter adapter,
        IAgentBenchmarkRunSink? runSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(adapter);
        var prepared = PrepareCore(corpus, configuration, adapter);
        var runs = new List<AgentBenchmarkRunResult>(
            prepared.Schedule.Count);
        foreach (var scheduledRun in prepared.Schedule)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await RunOnceAsync(
                    prepared.Corpus,
                    prepared.Configuration,
                    adapter,
                    prepared.Descriptor,
                    scheduledRun,
                    cancellationToken);
            runs.Add(run);
            if (runSink is not null)
            {
                await runSink.RetainAsync(
                    prepared.Prepared.Manifest,
                    run,
                    CancellationToken.None);
            }
        }

        return new AgentBenchmarkSeriesResult(
            prepared.Prepared.Manifest,
            runs);
    }

    private static PreparedState PrepareCore(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        IAgentBenchmarkAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(adapter);
        var descriptor = SnapshotDescriptor(adapter);
        configuration = SnapshotConfiguration(configuration);
        corpus = SnapshotAndValidateCorpus(corpus);
        Validate(corpus, configuration, descriptor, adapter);
        var schedule = CreateSchedule(corpus.Tasks, configuration);
        var manifest = new AgentBenchmarkSeriesManifest(
            ResultSchema,
            configuration.SeriesId,
            corpus.Id,
            corpus.Version,
            configuration.Dispatch,
            configuration.RunsPerTask,
            configuration.RandomizationSeed,
            configuration.Execution,
            configuration.Provenance,
            configuration.Baseline,
            configuration.Candidate,
            descriptor);
        var publicSchedule = AgentBenchmarkSnapshots.List(
            schedule.Select(run => new AgentBenchmarkScheduledRun(
                $"{configuration.SeriesId}/{run.ExecutionOrder:D6}",
                run.Task.Id,
                run.Condition,
                run.Repetition,
                run.ExecutionOrder)));
        return new PreparedState(
            corpus,
            configuration,
            descriptor,
            schedule,
            new AgentBenchmarkPreparedSeries(manifest, publicSchedule));
    }

    private async ValueTask<AgentBenchmarkRunResult> RunOnceAsync(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        IAgentBenchmarkAdapter adapter,
        AgentBenchmarkAdapterDescriptor descriptor,
        ScheduledRun scheduledRun,
        CancellationToken cancellationToken)
    {
        var task = scheduledRun.Task;
        var condition = GetCondition(configuration, scheduledRun.Condition);
        var manifestPath = ResolveManifestPath(
            configuration.CorpusDirectory,
            task.Repository.FixtureManifest);
        await using var fixture = await _fixtureFactory.CreateAsync(
            manifestPath,
            cancellationToken: cancellationToken);
        if (!string.Equals(
                fixture.ContentHash,
                task.Repository.ContentHash,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                $"Task '{task.Id}' materialized fixture hash does not match the corpus.");
        }

        var runId = $"{configuration.SeriesId}/{scheduledRun.ExecutionOrder:D6}";
        var promptHash = AgentBenchmarkHash.Compute(task.Prompt);
        await adapter.PrepareWorkspaceAsync(
            new AgentBenchmarkAdapterInput(
                runId,
                startAttempt: 0,
                scheduledRun.ExecutionOrder,
                scheduledRun.Repetition,
                scheduledRun.Condition,
                task,
                fixture.WorkspacePath,
                fixture.EnvironmentVariables,
                configuration.Execution,
                promptHash,
                condition.InstructionsHash,
                condition.ToolConfigurationHash),
            cancellationToken);

        var workspaceBaseline =
            await AgentBenchmarkWorkspaceHasher.CaptureBaselineAsync(
                fixture.WorkspacePath,
                fixture.ContentFiles,
                configuration.CleanupTimeout,
                cancellationToken);

        IAgentBenchmarkExecution? execution = null;
        var startAttempts = 0;
        while (execution is null)
        {
            startAttempts++;
            var input = new AgentBenchmarkAdapterInput(
                runId,
                startAttempts,
                scheduledRun.ExecutionOrder,
                scheduledRun.Repetition,
                scheduledRun.Condition,
                task,
                fixture.WorkspacePath,
                fixture.EnvironmentVariables,
                configuration.Execution,
                promptHash,
                condition.InstructionsHash,
                condition.ToolConfigurationHash);
            try
            {
                execution = await adapter.StartAsync(input, cancellationToken);
                if (execution is null)
                {
                    throw new AgentBenchmarkException(
                        $"Adapter '{descriptor.Id}' returned no execution for run '{runId}'.");
                }
            }
            catch (AgentBenchmarkStartException exception)
                when (exception.Retryable
                      && startAttempts < configuration.MaximumStartAttempts)
            {
                // A start exception contractually means no live execution
                // exists, so retrying cannot duplicate an agent run.
                var retryInspection =
                    await AgentBenchmarkWorkspaceHasher.InspectAsync(
                        fixture.WorkspacePath,
                        workspaceBaseline,
                        configuration.CleanupTimeout,
                        CancellationToken.None);
                if (!retryInspection.Complete
                    || !retryInspection.MatchesBaseline)
                {
                    throw new AgentBenchmarkException(
                        $"Retryable start failure contaminated task '{task.Id}' before a live retry.");
                }
            }
            catch (AgentBenchmarkStartException exception)
            {
                throw new AgentBenchmarkException(
                    $"Adapter '{descriptor.Id}' could not start run '{runId}' after {startAttempts} attempt(s).",
                    exception);
            }
        }

        AgentBenchmarkAdapterResult? adapterResult = null;
        AgentBenchmarkProgressSnapshot? progress = null;
        AgentBenchmarkProgressSnapshot? progressAtTimeout = null;
        var timedOut = false;
        TimeSpan duration;
        try
        {
            var startedAt = _timeProvider.GetTimestamp();
            Task completedTask;
            using var timeoutCancellation = new CancellationTokenSource();
            var timeoutTask = Task.Delay(
                TimeSpan.FromSeconds(task.Execution.TimeoutSeconds),
                _timeProvider,
                timeoutCancellation.Token);
            try
            {
                completedTask = await Task.WhenAny(
                        execution.Completion,
                        timeoutTask)
                    .WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await timeoutCancellation.CancelAsync();
                throw;
            }

            if (ReferenceEquals(completedTask, timeoutTask))
            {
                timedOut = true;
                progressAtTimeout = SnapshotAndValidateProgress(
                    execution.GetProgressSnapshot(),
                    runId);
                progress = progressAtTimeout;
                duration = _timeProvider.GetElapsedTime(startedAt);
            }
            else
            {
                await timeoutCancellation.CancelAsync();
                try
                {
                    adapterResult = await execution.Completion;
                }
                catch (Exception exception)
                {
                    throw new AgentBenchmarkException(
                        $"Adapter execution for run '{runId}' failed without a normalized result.",
                        exception);
                }

                adapterResult = SnapshotAdapterResult(adapterResult, runId);
                ValidateAdapterResult(
                    adapterResult,
                    configuration,
                    task,
                    condition,
                    promptHash,
                    runId);
                adapterResult = ApplyToolPolicy(
                    adapterResult,
                    task.Execution.PermittedTools);
                duration = _timeProvider.GetElapsedTime(startedAt);
            }
        }
        finally
        {
            await StopAndDisposeAsync(
                execution,
                configuration.CleanupTimeout,
                runId);
        }

        if (timedOut)
        {
            var progressAfterCleanup = SnapshotAndValidateProgress(
                execution.GetProgressSnapshot(),
                runId);
            ValidateProgressExtension(
                progressAtTimeout!,
                progressAfterCleanup,
                runId);
            progress = progressAfterCleanup;
        }

        var workspaceInspection =
            await AgentBenchmarkWorkspaceHasher.InspectAsync(
                fixture.WorkspacePath,
                workspaceBaseline,
                configuration.CleanupTimeout,
                CancellationToken.None);
        return timedOut
            ? CreateTimedOutResult(
                corpus,
                configuration,
                descriptor,
                scheduledRun,
                runId,
                startAttempts,
                promptHash,
                condition,
                workspaceBaseline,
                workspaceInspection,
                duration,
                progress!)
            : CreateCompletedResult(
                corpus,
                configuration,
                descriptor,
                scheduledRun,
                runId,
                startAttempts,
                promptHash,
                condition,
                workspaceBaseline,
                workspaceInspection,
                duration,
                adapterResult!);
    }

    private static AgentBenchmarkRunResult CreateCompletedResult(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        AgentBenchmarkAdapterDescriptor adapter,
        ScheduledRun scheduledRun,
        string runId,
        int startAttempts,
        string promptHash,
        AgentBenchmarkConditionConfiguration condition,
        AgentBenchmarkWorkspaceBaseline workspaceBaseline,
        AgentBenchmarkWorkspaceInspection workspaceInspection,
        TimeSpan duration,
        AgentBenchmarkAdapterResult adapterResult)
    {
        var task = scheduledRun.Task;
        var rawEvents = SnapshotAndValidateRawEvents(
            adapterResult.RawEvents,
            runId);
        var success = string.Equals(
                adapterResult.Status,
                "completed",
                StringComparison.Ordinal)
            && EvaluateSuccess(task.SuccessOracle, adapterResult.Answer);
        var workspaceUnchanged = workspaceInspection.Complete
            && workspaceInspection.MatchesBaseline;
        var claimsSupported = adapterResult.ClaimsSupported
            && EvaluateClaimsSupported(
                task.SuccessOracle,
                adapterResult.Answer);
        var safetyChecks = CreateSafetyChecks(
            claimsSupported,
            !adapterResult.NetworkUsed,
            workspaceUnchanged,
            workspaceInspection.Detail);
        var safe = safetyChecks.All(static check => check.Passed);
        var validations = CreateValidations(
            task,
            success,
            safe,
            workspaceUnchanged);

        return new AgentBenchmarkRunResult(
            runId,
            task.Id,
            scheduledRun.Condition,
            scheduledRun.Repetition,
            scheduledRun.ExecutionOrder,
            startAttempts,
            task.Execution.TimeoutSeconds,
            timedOut: false,
            adapterResult.Status,
            adapterResult.Answer,
            success,
            safe,
            adapterResult.InputTokens,
            adapterResult.OutputTokens,
            adapterResult.Turns,
            adapterResult.ToolCalls,
            duration,
            NormalizeScope(adapterResult.InspectedScope),
            safetyChecks,
            validations,
            Versions(corpus, configuration, adapter),
            Hashes(
                configuration,
                task,
                promptHash,
                condition,
                workspaceBaseline.InventoryHash,
                workspaceInspection.Hash,
                rawEvents),
            configuration.Execution.Sandbox,
            configuration.Execution.PermissionProfile,
            configuration.Execution.NetworkPolicy,
            rawEvents);
    }

    private static AgentBenchmarkRunResult CreateTimedOutResult(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        AgentBenchmarkAdapterDescriptor adapter,
        ScheduledRun scheduledRun,
        string runId,
        int startAttempts,
        string promptHash,
        AgentBenchmarkConditionConfiguration condition,
        AgentBenchmarkWorkspaceBaseline workspaceBaseline,
        AgentBenchmarkWorkspaceInspection workspaceInspection,
        TimeSpan duration,
        AgentBenchmarkProgressSnapshot progress)
    {
        var task = scheduledRun.Task;
        var rawEvents = progress.RawEvents;
        var workspaceUnchanged = workspaceInspection.Complete
            && workspaceInspection.MatchesBaseline;
        var safetyChecks = CreateSafetyChecks(
            claimsSupported: false,
            networkUnused: false,
            workspaceUnchanged,
            workspaceInspection.Detail);
        return new AgentBenchmarkRunResult(
            runId,
            task.Id,
            scheduledRun.Condition,
            scheduledRun.Repetition,
            scheduledRun.ExecutionOrder,
            startAttempts,
            task.Execution.TimeoutSeconds,
            timedOut: true,
            "timed-out",
            string.Empty,
            success: false,
            safe: false,
            progress.InputTokens,
            progress.OutputTokens,
            progress.Turns,
            progress.ToolCalls,
            duration,
            progress.InspectedScope,
            safetyChecks,
            CreateValidations(
                task,
                success: false,
                safe: false,
                workspaceUnchanged),
            Versions(corpus, configuration, adapter),
            Hashes(
                configuration,
                task,
                promptHash,
                condition,
                workspaceBaseline.InventoryHash,
                workspaceInspection.Hash,
                rawEvents),
            configuration.Execution.Sandbox,
            configuration.Execution.PermissionProfile,
            configuration.Execution.NetworkPolicy,
            rawEvents);
    }

    private static AgentBenchmarkRunVersions Versions(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        AgentBenchmarkAdapterDescriptor adapter) =>
        new(
            configuration.Provenance.HarnessVersion,
            adapter.Id,
            adapter.Version,
            configuration.Execution.AgentVersion,
            configuration.Execution.ModelId,
            configuration.Execution.ReasoningSetting,
            corpus.Version,
            configuration.Provenance.ProductSchema);

    private static AgentBenchmarkRunHashes Hashes(
        AgentBenchmarkConfiguration configuration,
        AgentTaskDefinition task,
        string promptHash,
        AgentBenchmarkConditionConfiguration condition,
        string initialWorkspaceHash,
        string actualWorkspaceHash,
        IReadOnlyList<AgentBenchmarkRawEvent> rawEvents) =>
        new(
            configuration.Execution.SettingsHash,
            promptHash,
            condition.InstructionsHash,
            condition.ToolConfigurationHash,
            task.Repository.ContentHash,
            initialWorkspaceHash,
            actualWorkspaceHash,
            configuration.Provenance.FixtureCommit,
            configuration.Provenance.ProductCommit,
            AgentBenchmarkHash.Trajectory(rawEvents));

    private static IReadOnlyList<AgentBenchmarkSafetyCheckResult>
        CreateSafetyChecks(
            bool claimsSupported,
            bool networkUnused,
            bool workspaceUnchanged,
            string workspaceDetail) =>
        AgentBenchmarkSnapshots.List(
            new[]
            {
                new AgentBenchmarkSafetyCheckResult(
                    "claims-supported",
                    claimsSupported,
                    claimsSupported
                        ? "Every returned claim is supported by the deterministic oracle."
                        : "One or more returned claims are unsupported or unavailable."),
                new AgentBenchmarkSafetyCheckResult(
                    "network-unused",
                    networkUnused,
                    networkUnused
                        ? "The adapter reported no network use."
                        : "Network non-use was not established."),
                new AgentBenchmarkSafetyCheckResult(
                    "workspace-unchanged",
                    workspaceUnchanged,
                    workspaceDetail),
            });

    private static IReadOnlyList<AgentBenchmarkValidationResult>
        CreateValidations(
            AgentTaskDefinition task,
            bool success,
            bool safe,
            bool workspaceUnchanged)
    {
        var validations = new List<AgentBenchmarkValidationResult>(
            task.RequiredValidation.Count);
        foreach (var validation in task.RequiredValidation)
        {
            validations.Add(
                validation switch
                {
                    "fixture-content-hash" => new(
                        validation,
                        true,
                        workspaceUnchanged,
                        workspaceUnchanged
                            ? "Materialized task content remained unchanged."
                            : "Materialized task content changed during execution."),
                    "safety-oracle" => new(
                        validation,
                        true,
                        safe,
                        safe
                            ? "Every declared safety check passed."
                            : "One or more declared safety checks failed."),
                    "success-oracle" => new(
                        validation,
                        true,
                        success,
                        success
                            ? "The normalized result satisfied the success oracle."
                            : "The normalized result did not satisfy the success oracle."),
                    "model-judge" => throw new AgentBenchmarkException(
                        $"Task '{task.Id}' requires a model judge, but no judge adapter is configured."),
                    _ => throw new AgentBenchmarkException(
                        $"Task '{task.Id}' requires unsupported validation '{validation}'."),
                });
        }

        return AgentBenchmarkSnapshots.List(validations);
    }

    private static bool EvaluateSuccess(
        AgentTaskSuccessOracle oracle,
        string answer)
    {
        if (!string.Equals(
                oracle.Kind,
                "exact-fact-set",
                StringComparison.Ordinal)
            || oracle.Normalizer is not (
                "ordinal-lines/v1" or "ordinal-sequence/v1"))
        {
            throw new AgentBenchmarkException(
                $"Unsupported success oracle '{oracle.Kind}'.");
        }

        return AgentBenchmarkFactSet.EqualsExpected(
            answer,
            oracle.ExpectedFacts,
            oracle.Normalizer!);
    }

    private static bool EvaluateClaimsSupported(
        AgentTaskSuccessOracle oracle,
        string answer)
    {
        if (!string.Equals(
                oracle.Kind,
                "exact-fact-set",
                StringComparison.Ordinal))
        {
            return false;
        }

        return AgentBenchmarkFactSet.ContainsOnlyExpected(
            answer,
            oracle.ExpectedFacts,
            oracle.Normalizer ?? string.Empty);
    }

    private static AgentBenchmarkInspectedScope NormalizeScope(
        AgentBenchmarkInspectedScope scope) =>
        new(
            AgentBenchmarkSnapshots.List(
                scope.Files.Order(StringComparer.Ordinal)),
            AgentBenchmarkSnapshots.List(
                scope.Projects.Order(StringComparer.Ordinal)));

    private static IReadOnlyList<AgentBenchmarkRawEvent>
        SnapshotAndValidateRawEvents(
            IReadOnlyList<AgentBenchmarkRawEvent>? rawEvents,
            string runId)
    {
        if (rawEvents is null || rawEvents.Count == 0)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' did not retain raw trajectory evidence.");
        }

        var snapshot = AgentBenchmarkSnapshots.List(
            rawEvents.Select(static rawEvent => rawEvent with { }));
        for (var index = 0; index < snapshot.Count; index++)
        {
            var rawEvent = snapshot[index];
            if (rawEvent.Sequence != index
                || string.IsNullOrWhiteSpace(rawEvent.Kind)
                || rawEvent.Payload is null
                || !AgentBenchmarkHash.IsHash(rawEvent.PayloadHash)
                || !string.Equals(
                    AgentBenchmarkHash.Compute(rawEvent.Payload),
                    rawEvent.PayloadHash,
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    $"Run '{runId}' contains malformed raw trajectory evidence at sequence {index}.");
            }
        }

        return snapshot;
    }

    private static AgentBenchmarkProgressSnapshot SnapshotAndValidateProgress(
        AgentBenchmarkProgressSnapshot? progress,
        string runId)
    {
        if (progress is null
            || progress.InputTokens < 0
            || progress.OutputTokens < 0
            || progress.Turns < 0
            || progress.ToolCalls is null
            || progress.InspectedScope is null)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' returned malformed timeout progress.");
        }

        ValidateToolCallShape(progress.ToolCalls, runId);
        ValidateScope(progress.InspectedScope, runId);
        ValidateTokenTotal(
            progress.InputTokens,
            progress.OutputTokens,
            runId);
        return new AgentBenchmarkProgressSnapshot(
            progress.InputTokens,
            progress.OutputTokens,
            progress.Turns,
            AgentBenchmarkSnapshots.List(
                progress.ToolCalls.Select(static call => call with { })),
            NormalizeScope(progress.InspectedScope),
            SnapshotAndValidateRawEvents(progress.RawEvents, runId));
    }

    private static void ValidateProgressExtension(
        AgentBenchmarkProgressSnapshot beforeCleanup,
        AgentBenchmarkProgressSnapshot afterCleanup,
        string runId)
    {
        var toolCallsMatch = beforeCleanup.ToolCalls.Count
                             <= afterCleanup.ToolCalls.Count
                             && beforeCleanup.ToolCalls.SequenceEqual(
                                 afterCleanup.ToolCalls.Take(
                                     beforeCleanup.ToolCalls.Count));
        var rawEventsMatch = beforeCleanup.RawEvents.Count
                             <= afterCleanup.RawEvents.Count
                             && beforeCleanup.RawEvents.SequenceEqual(
                                 afterCleanup.RawEvents.Take(
                                     beforeCleanup.RawEvents.Count));
        var filesMatch = beforeCleanup.InspectedScope.Files.All(
            afterCleanup.InspectedScope.Files.Contains);
        var projectsMatch = beforeCleanup.InspectedScope.Projects.All(
            afterCleanup.InspectedScope.Projects.Contains);
        if (afterCleanup.InputTokens < beforeCleanup.InputTokens
            || afterCleanup.OutputTokens < beforeCleanup.OutputTokens
            || afterCleanup.Turns < beforeCleanup.Turns
            || !toolCallsMatch
            || !rawEventsMatch
            || !filesMatch
            || !projectsMatch)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' replaced timeout evidence during bounded cleanup.");
        }
    }

    private static AgentBenchmarkAdapterResult SnapshotAdapterResult(
        AgentBenchmarkAdapterResult? result,
        string runId)
    {
        if (result is null)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' returned no normalized result.");
        }

        return result with
        {
            ToolCalls = result.ToolCalls is null
                ? null!
                : AgentBenchmarkSnapshots.List(
                    result.ToolCalls.Select(static call => call with { })),
            InspectedScope = result.InspectedScope is null
                ? null!
                : AgentBenchmarkSnapshots.Scope(result.InspectedScope),
            ObservedConfiguration = result.ObservedConfiguration is null
                ? null!
                : result.ObservedConfiguration with { },
            RawEvents = result.RawEvents is null
                ? null!
                : SnapshotAndValidateRawEvents(result.RawEvents, runId),
        };
    }

    private static void ValidateAdapterResult(
        AgentBenchmarkAdapterResult result,
        AgentBenchmarkConfiguration configuration,
        AgentTaskDefinition task,
        AgentBenchmarkConditionConfiguration condition,
        string promptHash,
        string runId)
    {
        if (result is null)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' returned no normalized result.");
        }

        if (result.Status is not ("completed" or "failed" or "permission-denied")
            || result.Answer is null
            || result.InputTokens < 0
            || result.OutputTokens < 0
            || result.Turns < 0
            || result.ToolCalls is null
            || result.InspectedScope is null
            || result.ObservedConfiguration is null)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' returned malformed normalized metrics.");
        }

        ValidateToolCallShape(result.ToolCalls, runId);
        ValidateTokenTotal(result.InputTokens, result.OutputTokens, runId);

        ValidateScope(result.InspectedScope, runId);
        var observed = result.ObservedConfiguration;
        if (observed != new AgentBenchmarkObservedConfiguration(
                configuration.Execution.AgentVersion,
                configuration.Execution.ModelId,
                configuration.Execution.ReasoningSetting,
                configuration.Execution.SettingsHash,
                configuration.Execution.Sandbox,
                configuration.Execution.PermissionProfile,
                configuration.Execution.NetworkPolicy,
                task.Repository.ContentHash,
                promptHash,
                condition.InstructionsHash,
                condition.ToolConfigurationHash))
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' observed settings do not match the controlled benchmark condition.");
        }

        _ = SnapshotAndValidateRawEvents(result.RawEvents, runId);
    }

    private static void ValidateToolCallShape(
        IReadOnlyList<AgentBenchmarkToolCall> toolCalls,
        string runId)
    {
        for (var index = 0; index < toolCalls.Count; index++)
        {
            var call = toolCalls[index];
            if (call.Sequence != index
                || string.IsNullOrWhiteSpace(call.ToolClass)
                || string.IsNullOrWhiteSpace(call.Name)
                || !AgentBenchmarkHash.IsHash(call.InputHash))
            {
                throw new AgentBenchmarkException(
                    $"Run '{runId}' contains a malformed tool call at sequence {index}.");
            }
        }
    }

    private static AgentBenchmarkAdapterResult ApplyToolPolicy(
        AgentBenchmarkAdapterResult result,
        IReadOnlyList<string> permittedTools) =>
        result.ToolCalls.All(call => permittedTools.Contains(
            call.ToolClass,
            StringComparer.Ordinal))
            ? result
            : result with
            {
                Status = "failed",
                ClaimsSupported = false,
            };

    private static void ValidateScope(
        AgentBenchmarkInspectedScope scope,
        string runId)
    {
        ValidatePaths(scope.Files, "file", runId);
        ValidatePaths(scope.Projects, "project", runId);
    }

    private static void ValidatePaths(
        IReadOnlyList<string>? values,
        string kind,
        string runId)
    {
        if (values is null)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' omitted inspected {kind} scope.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!PortableRelativePath.TryNormalize(
                    value,
                    normalizeBackslashes: false,
                    out var normalized)
                || !string.Equals(value, normalized, StringComparison.Ordinal)
                || !seen.Add(value))
            {
                throw new AgentBenchmarkException(
                    $"Run '{runId}' contains malformed inspected {kind} scope '{value}'.");
            }
        }
    }

    private static void ValidateTokenTotal(
        long inputTokens,
        long outputTokens,
        string runId)
    {
        try
        {
            _ = checked(inputTokens + outputTokens);
        }
        catch (OverflowException exception)
        {
            throw new AgentBenchmarkException(
                $"Run '{runId}' token totals overflow Int64.",
                exception);
        }
    }

    private static async ValueTask StopAndDisposeAsync(
        IAgentBenchmarkExecution execution,
        TimeSpan cleanupTimeout,
        string runId)
    {
        Exception? stopFailure = null;
        try
        {
            await StopAsync(execution, cleanupTimeout, runId);
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        Exception? disposeFailure = null;
        try
        {
            await DisposeAsync(execution, cleanupTimeout, runId);
        }
        catch (Exception exception)
        {
            disposeFailure = exception;
        }

        if (stopFailure is not null || disposeFailure is not null)
        {
            var failures = new[] { stopFailure, disposeFailure }
                .Where(static failure => failure is not null)
                .Cast<Exception>();
            throw new AgentBenchmarkException(
                $"Adapter execution for run '{runId}' did not finalize cleanly.",
                new AggregateException(failures));
        }
    }

    private static async ValueTask StopAsync(
        IAgentBenchmarkExecution execution,
        TimeSpan cleanupTimeout,
        string runId)
    {
        using var cleanup = new CancellationTokenSource(cleanupTimeout);
        try
        {
            await execution.StopAsync(cleanup.Token)
                .AsTask()
                .WaitAsync(cleanup.Token);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or TimeoutException)
        {
            throw new AgentBenchmarkException(
                $"Adapter execution for run '{runId}' did not stop within the cleanup timeout.",
                exception);
        }
    }

    private static async ValueTask DisposeAsync(
        IAgentBenchmarkExecution execution,
        TimeSpan cleanupTimeout,
        string runId)
    {
        using var cleanup = new CancellationTokenSource(cleanupTimeout);
        try
        {
            await execution.DisposeAsync()
                .AsTask()
                .WaitAsync(cleanup.Token);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or TimeoutException)
        {
            throw new AgentBenchmarkException(
                $"Adapter execution for run '{runId}' did not dispose within the cleanup timeout.",
                exception);
        }
    }

    private static AgentBenchmarkConditionConfiguration GetCondition(
        AgentBenchmarkConfiguration configuration,
        AgentBenchmarkCondition condition) =>
        condition == AgentBenchmarkCondition.Baseline
            ? configuration.Baseline
            : configuration.Candidate;

    private static IReadOnlyList<ScheduledRun> CreateSchedule(
        IReadOnlyList<AgentTaskDefinition> tasks,
        AgentBenchmarkConfiguration configuration)
    {
        var random = new StableRandom(configuration.RandomizationSeed);
        var candidates = new List<ScheduledRun>();
        for (var repetition = 1;
             repetition <= configuration.RunsPerTask;
             repetition++)
        {
            foreach (var task in tasks)
            {
                foreach (var condition in ApplicableConditions(task))
                {
                    candidates.Add(
                        new ScheduledRun(
                            task,
                            condition,
                            repetition,
                            ExecutionOrder: -1));
                }
            }
        }

        var shuffled = candidates.ToArray();
        random.Shuffle(shuffled);
        return AgentBenchmarkSnapshots.List(
            shuffled.Select(
                static (scheduledRun, executionOrder) =>
                    scheduledRun with
                    {
                        ExecutionOrder = executionOrder,
                    }));
    }

    private static IEnumerable<AgentBenchmarkCondition> ApplicableConditions(
        AgentTaskDefinition task)
    {
        if (task.Applicability.Baseline)
        {
            yield return AgentBenchmarkCondition.Baseline;
        }

        if (task.Applicability.Candidate)
        {
            yield return AgentBenchmarkCondition.Candidate;
        }
    }

    private static string ResolveManifestPath(
        string corpusDirectory,
        string fixtureManifest)
    {
        var directory = Path.GetFullPath(corpusDirectory);
        var path = Path.GetFullPath(
            Path.Combine(
                directory,
                fixtureManifest.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(directory, path);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The fixture manifest must remain inside the corpus directory.");
        }

        return path;
    }

    private static AgentBenchmarkAdapterDescriptor SnapshotDescriptor(
        IAgentBenchmarkAdapter adapter)
    {
        AgentBenchmarkAdapterDescriptor? descriptor;
        try
        {
            descriptor = adapter.Descriptor;
        }
        catch (Exception exception)
        {
            throw new AgentBenchmarkException(
                "The agent adapter descriptor could not be read.",
                exception);
        }

        if (descriptor is null)
        {
            throw new AgentBenchmarkException(
                "The agent adapter descriptor is required.");
        }

        return descriptor with { };
    }

    private static AgentBenchmarkConfiguration SnapshotConfiguration(
        AgentBenchmarkConfiguration configuration)
    {
        if (configuration.Execution is null
            || configuration.Provenance is null
            || configuration.Baseline is null
            || configuration.Candidate is null)
        {
            throw new AgentBenchmarkException(
                "Benchmark execution, provenance, and condition configuration are required.");
        }

        return configuration with
        {
            Execution = configuration.Execution with { },
            Provenance = configuration.Provenance with { },
            Baseline = configuration.Baseline with { },
            Candidate = configuration.Candidate with { },
        };
    }

    private static AgentTaskCorpus SnapshotAndValidateCorpus(
        AgentTaskCorpus corpus)
    {
        if (!IdentifierRegex().IsMatch(corpus.Id ?? string.Empty)
            || !SemanticTripletRegex().IsMatch(corpus.Version ?? string.Empty)
            || corpus.Tasks is null)
        {
            throw new AgentBenchmarkException(
                "The benchmark corpus identity, version, or task collection is invalid.");
        }

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        var tasks = new List<AgentTaskDefinition>();
        foreach (var sourceTask in corpus.Tasks)
        {
            if (sourceTask is null
                || sourceTask.RequiredCapabilities is null
                || sourceTask.Repository is null
                || sourceTask.Applicability is null
                || sourceTask.Execution is null
                || sourceTask.Execution.PermittedTools is null
                || sourceTask.SuccessOracle is null
                || sourceTask.SuccessOracle.ExpectedFacts is null
                || sourceTask.SafetyOracle is null
                || sourceTask.SafetyOracle.Checks is null
                || sourceTask.RequiredValidation is null)
            {
                throw new AgentBenchmarkException(
                    "The benchmark corpus contains a malformed task definition.");
            }

            var task = AgentBenchmarkSnapshots.Task(sourceTask);
            if (!IdentifierRegex().IsMatch(task.Id ?? string.Empty)
                || !taskIds.Add(task.Id ?? string.Empty)
                || !SemanticTripletRegex().IsMatch(task.Milestone ?? string.Empty)
                || !IsIdentifierSet(task.RequiredCapabilities, requireNonEmpty: true)
                || string.IsNullOrWhiteSpace(task.Prompt)
                || ContainsConditionGuidance(task.Prompt))
            {
                throw new AgentBenchmarkException(
                    "The benchmark corpus contains a malformed task definition.");
            }

            if (!PortableRelativePath.TryNormalize(
                    task.Repository.FixtureManifest,
                    normalizeBackslashes: false,
                    out var fixtureManifest)
                || !string.Equals(
                    fixtureManifest,
                    task.Repository.FixtureManifest,
                    StringComparison.Ordinal)
                || !IdentifierRegex().IsMatch(task.Repository.FixtureName ?? string.Empty)
                || !AgentBenchmarkHash.IsHash(task.Repository.ContentHash)
                || !string.Equals(
                    task.Repository.State,
                    "materialized-clean",
                    StringComparison.Ordinal)
                || (!task.Applicability.Baseline
                    && !task.Applicability.Candidate))
            {
                throw new AgentBenchmarkException(
                    $"Task '{task.Id}' repository state or applicability is invalid.");
            }

            if (!IsIdentifierSet(task.Execution.PermittedTools, requireNonEmpty: true)
                || task.Execution.PermittedTools.Any(ContainsConditionGuidance)
                || task.Execution.TimeoutSeconds is < 1 or > 1800
                || !string.Equals(
                    task.Execution.Network,
                    "disabled",
                    StringComparison.Ordinal)
                || !string.Equals(
                    task.Execution.Locale,
                    "invariant",
                    StringComparison.Ordinal)
                || !string.Equals(
                    task.Execution.TimeZone,
                    "UTC",
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    $"Task '{task.Id}' execution policy is invalid.");
            }

            if (!string.Equals(
                    task.SuccessOracle.Kind,
                    "exact-fact-set",
                    StringComparison.Ordinal)
                || task.SuccessOracle.Normalizer is not (
                    "ordinal-lines/v1" or "ordinal-sequence/v1")
                || task.SuccessOracle.ModelJudge is not null
                || !IsFactSet(task.SuccessOracle.ExpectedFacts))
            {
                throw new AgentBenchmarkException(
                    $"Task '{task.Id}' success oracle is unsupported or malformed.");
            }

            var expectedSafetyChecks = new[]
            {
                "claims-supported",
                "network-unused",
                "workspace-unchanged",
            };
            var expectedValidations = new[]
            {
                "fixture-content-hash",
                "safety-oracle",
                "success-oracle",
            };
            if (!string.Equals(
                    task.SafetyOracle.Kind,
                    "all",
                    StringComparison.Ordinal)
                || task.SafetyOracle.Checks is null
                || !task.SafetyOracle.Checks.SequenceEqual(
                    expectedSafetyChecks,
                    StringComparer.Ordinal)
                || task.RequiredValidation is null
                || !task.RequiredValidation.SequenceEqual(
                    expectedValidations,
                    StringComparer.Ordinal))
            {
                throw new AgentBenchmarkException(
                    $"Task '{task.Id}' safety or required-validation contract is invalid.");
            }

            tasks.Add(task);
        }

        if (tasks.Count == 0)
        {
            throw new AgentBenchmarkException(
                "The benchmark corpus identity, version, or task collection is invalid.");
        }

        return new AgentTaskCorpus(
            corpus.Id!,
            corpus.Version!,
            AgentBenchmarkSnapshots.List(tasks));
    }

    private static bool IsIdentifierSet(
        IReadOnlyList<string>? values,
        bool requireNonEmpty)
    {
        if (values is null || (requireNonEmpty && values.Count == 0))
        {
            return false;
        }

        string? previous = null;
        foreach (var value in values)
        {
            if (!IdentifierRegex().IsMatch(value ?? string.Empty)
                || (previous is not null
                    && string.CompareOrdinal(previous, value) >= 0))
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    private static bool IsFactSet(IReadOnlyList<string>? facts)
    {
        if (facts is null || facts.Count == 0)
        {
            return false;
        }

        string? previous = null;
        foreach (var fact in facts)
        {
            if (string.IsNullOrWhiteSpace(fact)
                || fact.Contains('\r')
                || fact.Contains('\n')
                || (previous is not null
                    && string.CompareOrdinal(previous, fact) >= 0))
            {
                return false;
            }

            previous = fact;
        }

        return true;
    }

    private static bool ContainsConditionGuidance(string value) =>
        new[]
        {
            "baseline condition",
            "baseline-only",
            "candidate condition",
            "candidate-only",
            "dnaxi",
            "dotnet-axi",
        }.Any(marker => value.Contains(
            marker,
            StringComparison.OrdinalIgnoreCase));

    private static void Validate(
        AgentTaskCorpus corpus,
        AgentBenchmarkConfiguration configuration,
        AgentBenchmarkAdapterDescriptor descriptor,
        IAgentBenchmarkAdapter adapter)
    {
        if (corpus.Tasks.Count == 0)
        {
            throw new AgentBenchmarkException(
                "The benchmark corpus must contain at least one task.");
        }

        if (!IdentifierRegex().IsMatch(configuration.SeriesId ?? string.Empty)
            || !Directory.Exists(configuration.CorpusDirectory)
            || configuration.Dispatch is not (
                AgentBenchmarkDispatch.Manual
                or AgentBenchmarkDispatch.ContinuousIntegration)
            || configuration.RunsPerTask is < MinimumRunsPerCondition or > 100
            || configuration.MaximumStartAttempts is < 1 or > 5
            || configuration.CleanupTimeout <= TimeSpan.Zero
            || configuration.CleanupTimeout > TimeSpan.FromSeconds(30))
        {
            throw new AgentBenchmarkException(
                "The benchmark configuration is invalid or permits fewer than five runs per condition.");
        }

        if (configuration.Dispatch == AgentBenchmarkDispatch.ContinuousIntegration
            && adapter.GetType()
                != typeof(DeterministicFakeAgentBenchmarkAdapter))
        {
            throw new AgentBenchmarkException(
                "Continuous integration may execute only a deterministic fake agent adapter.");
        }

        if (configuration.Baseline.Condition != AgentBenchmarkCondition.Baseline
            || configuration.Candidate.Condition != AgentBenchmarkCondition.Candidate
            || !IdentifierRegex().IsMatch(descriptor.Id ?? string.Empty)
            || !ExplicitVersionRegex().IsMatch(
                descriptor.Version ?? string.Empty)
            || string.IsNullOrWhiteSpace(configuration.Execution.AgentVersion)
            || string.IsNullOrWhiteSpace(configuration.Execution.ModelId)
            || string.IsNullOrWhiteSpace(configuration.Execution.ReasoningSetting)
            || configuration.Execution.Sandbox is not (
                "read-only" or "workspace-write")
            || string.IsNullOrWhiteSpace(configuration.Execution.PermissionProfile)
            || !string.Equals(
                configuration.Execution.NetworkPolicy,
                "disabled",
                StringComparison.Ordinal)
            || !AgentBenchmarkHash.IsHash(configuration.Execution.SettingsHash)
            || !AgentBenchmarkHash.IsHash(configuration.Baseline.InstructionsHash)
            || !AgentBenchmarkHash.IsHash(configuration.Baseline.ToolConfigurationHash)
            || !AgentBenchmarkHash.IsHash(configuration.Candidate.InstructionsHash)
            || !AgentBenchmarkHash.IsHash(configuration.Candidate.ToolConfigurationHash)
            || !ExplicitVersionRegex().IsMatch(
                configuration.Provenance.HarnessVersion ?? string.Empty)
            || string.IsNullOrWhiteSpace(configuration.Provenance.ProductSchema)
            || !CommitRegex().IsMatch(
                configuration.Provenance.FixtureCommit ?? string.Empty)
            || !CommitRegex().IsMatch(
                configuration.Provenance.ProductCommit ?? string.Empty))
        {
            throw new AgentBenchmarkException(
                "Benchmark settings, provenance, conditions, or adapter identity are malformed.");
        }

        foreach (var task in corpus.Tasks)
        {
            var expectedSandbox = task.Execution.PermittedTools.Contains(
                "workspace-write",
                StringComparer.Ordinal)
                    ? "workspace-write"
                    : "read-only";
            if (!string.Equals(
                    configuration.Execution.Sandbox,
                    expectedSandbox,
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    $"Task '{task.Id}' requires the explicit '{expectedSandbox}' sandbox.");
            }

            if (!string.Equals(
                    task.Execution.Network,
                    configuration.Execution.NetworkPolicy,
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    $"Task '{task.Id}' network policy differs from the controlled series policy.");
            }

            if (!string.Equals(
                    task.SafetyOracle.Kind,
                    "all",
                    StringComparison.Ordinal)
                || task.SafetyOracle.Checks.Any(
                    static check => check is not (
                        "claims-supported"
                        or "network-unused"
                        or "workspace-unchanged")))
            {
                throw new AgentBenchmarkException(
                    $"Task '{task.Id}' declares a safety check the runner cannot evaluate.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitVersionRegex();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticTripletRegex();

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitRegex();

    private sealed record ScheduledRun(
        AgentTaskDefinition Task,
        AgentBenchmarkCondition Condition,
        int Repetition,
        int ExecutionOrder);

    private sealed record PreparedState(
        AgentTaskCorpus Corpus,
        AgentBenchmarkConfiguration Configuration,
        AgentBenchmarkAdapterDescriptor Descriptor,
        IReadOnlyList<ScheduledRun> Schedule,
        AgentBenchmarkPreparedSeries Prepared);

    private sealed class StableRandom
    {
        private ulong _state;

        public StableRandom(ulong seed)
        {
            _state = seed;
        }

        public void Shuffle<T>(T[] values)
        {
            for (var index = values.Length - 1; index > 0; index--)
            {
                var replacement = (int)(Next() % (ulong)(index + 1));
                (values[index], values[replacement]) =
                    (values[replacement], values[index]);
            }
        }

        private ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}

internal static partial class AgentBenchmarkHash
{
    public static string Compute(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    public static string Trajectory(
        IReadOnlyList<AgentBenchmarkRawEvent> events)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> sequence = stackalloc byte[sizeof(int)];
        foreach (var rawEvent in events)
        {
            BinaryPrimitives.WriteInt32BigEndian(sequence, rawEvent.Sequence);
            hash.AppendData(sequence);
            AppendField(hash, Encoding.UTF8.GetBytes(rawEvent.Kind));
            AppendField(hash, Convert.FromHexString(rawEvent.PayloadHash));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static bool IsHash(string? value) =>
        value is not null && HashRegex().IsMatch(value);

    private static void AppendField(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashRegex();
}
