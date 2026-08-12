using System.Text.Json;

namespace DotNetAxi.Testing;

internal sealed class CodexDiscoveryEvidenceStore : IAgentBenchmarkRunSink
{
    internal const string RunSchema =
        "dotnet-axi/codex-discovery-retained-run/v3";
    internal const string ReportSchema =
        "dotnet-axi/codex-discovery-report/v3";
    internal const string SummarySchema =
        "dotnet-axi/codex-discovery-summary/v4";

    private readonly string _evidenceDirectory;
    private readonly string _runsDirectory;
    private readonly CodexDiscoveryPreparedContext _context;
    private readonly List<AgentBenchmarkRunResult> _runs = [];

    private CodexDiscoveryEvidenceStore(
        string evidenceDirectory,
        CodexDiscoveryPreparedContext context)
    {
        _evidenceDirectory = evidenceDirectory;
        _runsDirectory = Path.Combine(evidenceDirectory, "runs");
        _context = context;
    }

    internal static async ValueTask<CodexDiscoveryEvidenceStore> CreateAsync(
        string evidenceDirectory,
        CodexDiscoveryPreparedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentNullException.ThrowIfNull(context);
        if (!Path.IsPathFullyQualified(evidenceDirectory)
            || Directory.Exists(evidenceDirectory)
            || File.Exists(evidenceDirectory)
            || !Directory.Exists(Path.GetDirectoryName(evidenceDirectory)))
        {
            throw new AgentBenchmarkException(
                "The evidence directory must be an absolute create-new path beneath an existing directory.");
        }

        Directory.CreateDirectory(evidenceDirectory);
        Directory.CreateDirectory(Path.Combine(evidenceDirectory, "runs"));
        var store = new CodexDiscoveryEvidenceStore(
            evidenceDirectory,
            context);
        await CodexDiscoveryBenchmarkPreparation.WriteCreateNewAsync(
            Path.Combine(evidenceDirectory, "preparation.json"),
            context.Preparation,
            cancellationToken);
        return store;
    }

    public async ValueTask RetainAsync(
        AgentBenchmarkSeriesManifest manifest,
        AgentBenchmarkRunResult run,
        CancellationToken cancellationToken = default)
    {
        if (!CodexDiscoveryEvidenceValidator.CanonicalEquals(
                manifest,
                _context.Preparation.Manifest))
        {
            throw new AgentBenchmarkException(
                "A retained run reported a manifest different from the sealed preparation.");
        }

        var expectedOrder = _runs.Count;
        if (run.ExecutionOrder != expectedOrder)
        {
            throw new AgentBenchmarkException(
                $"Run order {run.ExecutionOrder} cannot be retained at position {expectedOrder}.");
        }

        await CodexDiscoveryBenchmarkPreparation.WriteCreateNewAsync(
            Path.Combine(_runsDirectory, $"{run.ExecutionOrder:D6}.json"),
            new CodexDiscoveryRetainedRun(
                RunSchema,
                _context.Preparation.RequestHash,
                run),
            cancellationToken);
        _runs.Add(run);
        await CodexDiscoveryBenchmarkPreparation
            .ValidateExecutionArtifactPinsAsync(
                _context,
                cancellationToken);
    }

    internal async ValueTask<CodexDiscoverySeriesSummary> FinalizeAsync(
        bool completed,
        string? failure,
        CancellationToken cancellationToken = default)
    {
        var report = new CodexDiscoverySeriesReport(
            ReportSchema,
            _context.Preparation.RequestHash,
            _context.Preparation.Manifest,
            _context.Preparation.Schedule.Count,
            completed,
            failure,
            _runs.ToArray());
        CodexDiscoveryEvidenceValidator.ValidateReport(_context, report);
        var reportPath = Path.Combine(_evidenceDirectory, "report.json");
        await CodexDiscoveryBenchmarkPreparation.WriteCreateNewAsync(
            reportPath,
            report,
            cancellationToken);
        var reportHash = await CodexDiscoveryBenchmarkPreparation.HashFileAsync(
            reportPath,
            cancellationToken);
        var summary = CodexDiscoveryEvidenceValidator.CreateSummary(
            _context,
            report,
            reportHash);
        await CodexDiscoveryBenchmarkPreparation.WriteCreateNewAsync(
            Path.Combine(_evidenceDirectory, "summary.json"),
            summary,
            cancellationToken);
        return summary;
    }
}

internal static class CodexDiscoveryEvidenceValidator
{
    internal static async ValueTask<CodexDiscoverySeriesSummary>
        ValidateDirectoryAsync(
            string requestPath,
            string evidenceDirectory,
            CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(evidenceDirectory)
            || !Directory.Exists(evidenceDirectory))
        {
            throw new AgentBenchmarkException(
                "The evidence directory must be an existing absolute directory.");
        }

        var preparationPath = Path.Combine(
            evidenceDirectory,
            "preparation.json");
        var context = await CodexDiscoveryBenchmarkPreparation
            .ValidatePreparationAsync(
            requestPath,
            preparationPath,
            cancellationToken);
        var reportPath = Path.Combine(evidenceDirectory, "report.json");
        var summaryPath = Path.Combine(evidenceDirectory, "summary.json");
        var report = await LoadStrictAsync<CodexDiscoverySeriesReport>(
            reportPath,
            "report",
            cancellationToken);
        ValidateReport(context, report);
        await ValidateRetainedRunsAsync(
            context,
            report,
            Path.Combine(evidenceDirectory, "runs"),
            cancellationToken);
        var reportHash = await CodexDiscoveryBenchmarkPreparation.HashFileAsync(
            reportPath,
            cancellationToken);
        var retainedSummary =
            await LoadStrictAsync<CodexDiscoverySeriesSummary>(
                summaryPath,
                "summary",
                cancellationToken);
        var expectedSummary = CreateSummary(context, report, reportHash);
        if (!CanonicalEquals(retainedSummary, expectedSummary))
        {
            throw new AgentBenchmarkException(
                "The retained summary does not reconcile with the strict report and documented thresholds.");
        }

        return retainedSummary;
    }

    internal static void ValidateReport(
        CodexDiscoveryPreparedContext context,
        CodexDiscoverySeriesReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);
        var schedule = context.Preparation.Schedule;
        if (!string.Equals(
                report.Schema,
                CodexDiscoveryEvidenceStore.ReportSchema,
                StringComparison.Ordinal)
            || !string.Equals(
                report.RequestHash,
                context.Preparation.RequestHash,
                StringComparison.Ordinal)
            || !CanonicalEquals(
                report.Manifest,
                context.Preparation.Manifest)
            || report.ExpectedRunCount != schedule.Count
            || report.Runs is null
            || report.Runs.Count > schedule.Count
            || (report.Complete
                && (report.Runs.Count != schedule.Count
                    || report.Failure is not null))
            || (!report.Complete
                && report.Failure is not null
                && string.IsNullOrWhiteSpace(report.Failure)))
        {
            throw new AgentBenchmarkException(
                "The retained report header, completion state, or manifest is invalid.");
        }

        for (var index = 0; index < report.Runs.Count; index++)
        {
            ValidateRun(context, report.Runs[index], schedule[index]);
        }
    }

    internal static CodexDiscoverySeriesSummary CreateSummary(
        CodexDiscoveryPreparedContext context,
        CodexDiscoverySeriesReport report,
        string reportHash)
    {
        ValidateReport(context, report);
        if (!AgentBenchmarkHash.IsHash(reportHash))
        {
            throw new AgentBenchmarkException(
                "The report SHA-256 must be lowercase hexadecimal.");
        }

        var baselineRuns = report.Runs
            .Where(static run =>
                run.Condition is AgentBenchmarkCondition.Baseline)
            .ToArray();
        var candidateRuns = report.Runs
            .Where(static run =>
                run.Condition is AgentBenchmarkCondition.Candidate)
            .ToArray();
        var comparableTaskIds = context.Corpus.Tasks
            .Where(static task => task.Applicability.Baseline)
            .Select(static task => task.Id)
            .ToArray();
        var comparableTaskIdSet = comparableTaskIds.ToHashSet(
            StringComparer.Ordinal);
        var comparableCandidateRuns = candidateRuns
            .Where(run => comparableTaskIdSet.Contains(run.TaskId))
            .ToArray();
        var baseline = Metrics(
            AgentBenchmarkCondition.Baseline,
            baselineRuns,
            context.Request.Product.PackageId,
            context.Request.Product.PackageVersion,
            context.Request.Product.PackageSource.Path,
            context.Request.DnxExecutable.Path);
        var candidate = Metrics(
            AgentBenchmarkCondition.Candidate,
            comparableCandidateRuns,
            context.Request.Product.PackageId,
            context.Request.Product.PackageVersion,
            context.Request.Product.PackageSource.Path,
            context.Request.DnxExecutable.Path);
        var allCandidate = Metrics(
            AgentBenchmarkCondition.Candidate,
            candidateRuns,
            context.Request.Product.PackageId,
            context.Request.Product.PackageVersion,
            context.Request.Product.PackageSource.Path,
            context.Request.DnxExecutable.Path);
        var routeActivations = context.Corpus.Tasks
            .Select(task => RouteActivation(
                task,
                candidateRuns.Where(run => string.Equals(
                    run.TaskId,
                    task.Id,
                    StringComparison.Ordinal)).ToArray(),
                context.Request.Product.PackageId,
                context.Request.Product.PackageVersion,
                context.Request.Product.PackageSource.Path,
                context.Request.DnxExecutable.Path))
            .ToArray();
        var candidateOnlyTasks = context.Corpus.Tasks
            .Where(static task => !task.Applicability.Baseline)
            .Select(task =>
            {
                var taskRuns = candidateRuns.Where(run => string.Equals(
                    run.TaskId,
                    task.Id,
                    StringComparison.Ordinal)).ToArray();
                var activation = routeActivations.Single(route => string.Equals(
                    route.TaskId,
                    task.Id,
                    StringComparison.Ordinal));
                return new CodexDiscoveryCandidateOnlyTaskMetrics(
                    task.Id,
                    taskRuns.Length,
                    taskRuns.Count(static run => string.Equals(
                        run.Status,
                        "completed",
                        StringComparison.Ordinal)),
                    taskRuns.Count(static run => run.Success),
                    taskRuns.Count(static run => run.Safe),
                    taskRuns.Count(static run => run.TimedOut),
                    activation.ActivatedRunCount,
                    activation.SuccessfulActivatedRunCount);
            })
            .ToArray();
        var complete = report.Complete
                       && report.Runs.Count == report.ExpectedRunCount;
        var failed = report.Failure is not null
                     || report.Runs.Any(static run =>
                         run.TimedOut
                         || !string.Equals(
                             run.Status,
                             "completed",
                             StringComparison.Ordinal));
        var evidenceStatus = !complete
            ? report.Failure is null ? "missing" : "failed"
            : failed ? "failed" : "complete";
        var safetyCriticalRegressions = complete
            ? CountSafetyCriticalRegressions(
                baselineRuns,
                comparableCandidateRuns)
            : 0;
        var successDelta = complete
            ? (decimal?)(candidate.SuccessRatePercent
                - baseline.SuccessRatePercent)
            : null;
        var tokenChange = complete
            ? PercentageChange(
                baseline.MedianTotalTokens,
                candidate.MedianTotalTokens)
            : null;
        var toolCallChange = complete
            ? PercentageChange(
                baseline.MedianToolCalls,
                candidate.MedianToolCalls)
            : null;
        var correctnessBenefit = complete
                                 && candidate.SuccessRatePercent
                                 > baseline.SuccessRatePercent;
        var successRegression = successDelta is <= -2m;
        var tokenRegression = !correctnessBenefit
                              && (tokenChange is >= 10m
                                  || (tokenChange is null
                                      && baseline.MedianTotalTokens == 0m
                                      && candidate.MedianTotalTokens > 0m));
        var toolCallRegression = !correctnessBenefit
                                 && (toolCallChange is >= 10m
                                     || (toolCallChange is null
                                         && baseline.MedianToolCalls == 0m
                                         && candidate.MedianToolCalls > 0m));
        var tokenReduction = tokenChange is <= -10m;
        var zeroActivation = complete
                             && !failed
                             && allCandidate.DnxInvocationCount == 0;
        var routeActivationGap = complete
                                 && !failed
                                 && routeActivations.Any(static route =>
                                     route.SuccessfulActivatedRunCount == 0);
        var improvement = complete
                          && !failed
                          && !routeActivationGap
                          && allCandidate.SuccessfulDnxActivatedRunCount > 0
                          && safetyCriticalRegressions == 0
                          && !successRegression
                          && !tokenRegression
                          && !toolCallRegression
                          && candidate.SuccessRatePercent
                          >= baseline.SuccessRatePercent
                          && tokenReduction;
        var comparison = !complete || failed
            ? "incomparable"
            : zeroActivation
                ? "zero-activation"
                : routeActivationGap
                    ? "activation-gap"
                : safetyCriticalRegressions > 0
              || successRegression
              || tokenRegression
              || toolCallRegression
                ? "regression"
                : improvement
                    ? "improvement"
                    : "no-improvement";
        var reasons = new List<string>();
        if (!complete)
        {
            reasons.Add("The report is missing one or more scheduled runs.");
        }

        if (failed)
        {
            reasons.Add(
                "At least one run failed, timed out, or did not produce a completed Codex result.");
        }

        if (safetyCriticalRegressions > 0)
        {
            reasons.Add(
                "A successful and safe baseline repetition became an unsafe successful candidate repetition.");
        }

        if (successRegression)
        {
            reasons.Add(
                "Aggregate candidate success decreased by at least two percentage points.");
        }

        if (tokenRegression)
        {
            reasons.Add(
                "Median candidate token use increased by at least ten percent without an observed aggregate-success benefit.");
        }

        if (toolCallRegression)
        {
            reasons.Add(
                "Median candidate tool-call use increased by at least ten percent without an observed aggregate-success benefit.");
        }

        if (zeroActivation)
        {
            reasons.Add(
                "The candidate exposed dnaxi but no run invoked an exact version-pinned dnx dnaxi command, so the product was not exercised.");
        }

        if (routeActivationGap)
        {
            reasons.Add(
                "At least one discovery route has no candidate run with a successful exact source-pinned dnx activation.");
        }

        if (improvement)
        {
            reasons.Add(
                "Candidate success is equal or higher, no safety-critical regression occurred, and median total tokens fell by at least ten percent.");
        }

        if (reasons.Count == 0)
        {
            reasons.Add(
                "Complete comparable evidence does not satisfy either a documented regression threshold or the improvement threshold.");
        }

        return new CodexDiscoverySeriesSummary(
            CodexDiscoveryEvidenceStore.SummarySchema,
            context.Preparation.RequestHash,
            reportHash,
            evidenceStatus,
            comparison,
            report.ExpectedRunCount,
            report.Runs.Count,
            baseline,
            candidate,
            allCandidate,
            comparableTaskIds,
            candidateOnlyTasks,
            new CodexDiscoveryThresholdEvaluation(
                safetyCriticalRegressions,
                successDelta,
                tokenChange,
                toolCallChange,
                successRegression,
                tokenRegression,
                toolCallRegression,
                improvement),
            routeActivations,
            new CodexDiscoveryHistoricalComparison(
                context.Request.PriorSeries.Summary.Path,
                context.Request.PriorSeries.Summary.Sha256,
                context.PriorSeries.Schema,
                context.PriorSeries.RequestHash,
                context.PriorSeries.ReportHash,
                context.PriorSeries.EvidenceStatus,
                context.PriorSeries.Comparison,
                Comparable: false,
                "The immutable 0.4.0 summary is retained exactly as historical evidence and is neither reclassified nor pooled with the 0.5.0 symbol-context series."),
            reasons.ToArray());
    }

    internal static bool CanonicalEquals<T>(T left, T right)
    {
        var leftBytes = JsonSerializer.SerializeToUtf8Bytes(
            left,
            CodexDiscoveryBenchmarkPreparation.JsonOptions);
        var rightBytes = JsonSerializer.SerializeToUtf8Bytes(
            right,
            CodexDiscoveryBenchmarkPreparation.JsonOptions);
        return leftBytes.AsSpan().SequenceEqual(rightBytes);
    }

    private static async ValueTask ValidateRetainedRunsAsync(
        CodexDiscoveryPreparedContext context,
        CodexDiscoverySeriesReport report,
        string runsDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(runsDirectory))
        {
            throw new AgentBenchmarkException(
                "The retained per-run evidence directory is missing.");
        }

        var files = Directory.EnumerateFiles(
                runsDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (files.Length != report.Runs.Count)
        {
            throw new AgentBenchmarkException(
                "The retained per-run file count does not match the report.");
        }

        for (var index = 0; index < files.Length; index++)
        {
            var expectedName = $"{index:D6}.json";
            if (!string.Equals(
                    Path.GetFileName(files[index]),
                    expectedName,
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    "Retained per-run evidence files must be contiguous and execution-ordered.");
            }

            var retained = await LoadStrictAsync<CodexDiscoveryRetainedRun>(
                files[index],
                $"retained run {index}",
                cancellationToken);
            if (!string.Equals(
                    retained.Schema,
                    CodexDiscoveryEvidenceStore.RunSchema,
                    StringComparison.Ordinal)
                || !string.Equals(
                    retained.RequestHash,
                    context.Preparation.RequestHash,
                    StringComparison.Ordinal)
                || !CanonicalEquals(retained.Run, report.Runs[index]))
            {
                throw new AgentBenchmarkException(
                    $"Retained run {index} does not reconcile with the report.");
            }
        }
    }

    private static void ValidateRun(
        CodexDiscoveryPreparedContext context,
        AgentBenchmarkRunResult run,
        AgentBenchmarkScheduledRun scheduled)
    {
        var task = context.Corpus.Tasks.Single(candidate =>
            string.Equals(candidate.Id, scheduled.TaskId, StringComparison.Ordinal));
        var condition = scheduled.Condition is AgentBenchmarkCondition.Baseline
            ? context.Configuration.Baseline
            : context.Configuration.Candidate;
        if (!string.Equals(run.RunId, scheduled.RunId, StringComparison.Ordinal)
            || !string.Equals(run.TaskId, scheduled.TaskId, StringComparison.Ordinal)
            || run.Condition != scheduled.Condition
            || run.Repetition != scheduled.Repetition
            || run.ExecutionOrder != scheduled.ExecutionOrder
            || run.StartAttempts is < 1
            || run.StartAttempts > context.Request.MaximumStartAttempts
            || run.TimeoutSeconds != task.Execution.TimeoutSeconds
            || run.Duration < TimeSpan.Zero
            || run.InputTokens < 0
            || run.OutputTokens < 0
            || run.Turns < 0
            || run.ToolCalls is null
            || run.InspectedScope is null
            || run.SafetyChecks is null
            || run.Validations is null
            || run.RawEvents is null
            || !string.Equals(
                run.Sandbox,
                CodexDiscoveryBenchmarkPreparation.Sandbox,
                StringComparison.Ordinal)
            || !string.Equals(
                run.PermissionProfile,
                CodexDiscoveryBenchmarkPreparation.PermissionProfile,
                StringComparison.Ordinal)
            || !string.Equals(
                run.NetworkPolicy,
                CodexDiscoveryBenchmarkPreparation.NetworkPolicy,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                $"Run '{scheduled.RunId}' does not match its deterministic schedule or execution policy.");
        }

        _ = run.TotalTokens;
        var expectedVersions = new AgentBenchmarkRunVersions(
            context.Configuration.Provenance.HarnessVersion,
            context.Preparation.Manifest.Adapter.Id,
            context.Preparation.Manifest.Adapter.Version,
            CodexDiscoveryBenchmarkPreparation.CodexCliVersion,
            CodexDiscoveryBenchmarkPreparation.ModelId,
            CodexDiscoveryBenchmarkPreparation.ReasoningSetting,
            CodexDiscoveryBenchmarkPreparation.CorpusVersion,
            CodexDiscoveryBenchmarkPreparation.ProductSchema);
        if (run.Versions != expectedVersions
            || !string.Equals(
                run.Hashes.Settings,
                context.Request.Settings.Sha256,
                StringComparison.Ordinal)
            || !string.Equals(
                run.Hashes.Prompt,
                AgentBenchmarkHash.Compute(task.Prompt),
                StringComparison.Ordinal)
            || !string.Equals(
                run.Hashes.Instructions,
                condition.InstructionsHash,
                StringComparison.Ordinal)
            || !string.Equals(
                run.Hashes.ToolConfiguration,
                condition.ToolConfigurationHash,
                StringComparison.Ordinal)
            || !string.Equals(
                run.Hashes.FixtureContent,
                task.Repository.ContentHash,
                StringComparison.Ordinal)
            || !string.Equals(
                run.Hashes.FixtureCommit,
                context.Configuration.Provenance.FixtureCommit,
                StringComparison.Ordinal)
            || !string.Equals(
                run.Hashes.ProductCommit,
                context.Configuration.Provenance.ProductCommit,
                StringComparison.Ordinal)
            || !string.Equals(
                run.Hashes.RawTrajectory,
                AgentBenchmarkHash.Trajectory(run.RawEvents),
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                $"Run '{scheduled.RunId}' version or hash pins drifted from the preparation.");
        }

        var reconciliation = ReconcileRaw(run, task);
        if (!reconciliation.Passed)
        {
            throw new AgentBenchmarkException(
                $"Run '{scheduled.RunId}' for task '{task.Id}' normalized metrics do not reconcile with its raw Codex trajectory. Expected scope: {JsonSerializer.Serialize(run.InspectedScope, CodexDiscoveryBenchmarkPreparation.JsonOptions)}. Reconciliation: {JsonSerializer.Serialize(reconciliation, CodexDiscoveryBenchmarkPreparation.JsonOptions)}");
        }

        var success = string.Equals(run.Status, "completed", StringComparison.Ordinal)
                      && AgentBenchmarkFactSet.EqualsExpected(
                          run.Answer,
                          task.SuccessOracle.ExpectedFacts);
        var claimsSupported = string.Equals(
                                  run.Status,
                                  "completed",
                                  StringComparison.Ordinal)
                              && AgentBenchmarkFactSet.ContainsOnlyExpected(
                                  run.Answer,
                                  task.SuccessOracle.ExpectedFacts);
        var networkUnused = !run.TimedOut && !reconciliation.NetworkUsed;
        var workspaceUnchanged = string.Equals(
            run.Hashes.WorkspaceBefore,
            run.Hashes.WorkspaceAfter,
            StringComparison.Ordinal);
        var checks = run.SafetyChecks.ToDictionary(
            static check => check.Id,
            StringComparer.Ordinal);
        if (checks.Count != 3
            || !checks.TryGetValue("claims-supported", out var claims)
            || claims.Passed != claimsSupported
            || !checks.TryGetValue("network-unused", out var network)
            || network.Passed != networkUnused
            || !checks.TryGetValue("workspace-unchanged", out var workspace)
            || workspace.Passed != workspaceUnchanged
            || run.Success != success
            || run.Safe != checks.Values.All(static check => check.Passed))
        {
            throw new AgentBenchmarkException(
                $"Run '{scheduled.RunId}' deterministic success or safety outcomes do not reconcile.");
        }

        var validations = run.Validations.ToDictionary(
            static validation => validation.Id,
            StringComparer.Ordinal);
        if (!task.RequiredValidation.All(validations.ContainsKey)
            || validations.Count != task.RequiredValidation.Count
            || validations.Values.Any(static validation => !validation.Executed)
            || validations["fixture-content-hash"].Passed != workspaceUnchanged
            || validations["safety-oracle"].Passed != run.Safe
            || validations["success-oracle"].Passed != run.Success)
        {
            throw new AgentBenchmarkException(
                $"Run '{scheduled.RunId}' required validations do not reconcile.");
        }
    }

    private static CodexDiscoveryRawReconciliation ReconcileRaw(
        AgentBenchmarkRunResult run,
        AgentTaskDefinition task)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        var turns = 0;
        var answer = string.Empty;
        var networkUsed = false;
        var permissionDenied = false;
        var providerError = false;
        var turnFailed = false;
        var protocolFailure = false;
        var state = EvidenceProviderState.AwaitingThread;
        var completedItems = new HashSet<string>(StringComparer.Ordinal);
        var toolCalls = new List<AgentBenchmarkToolCall>();
        var files = new SortedSet<string>(StringComparer.Ordinal);
        var projects = new SortedSet<string>(StringComparer.Ordinal);
        int? startedProcess = null;
        int? exitedProcess = null;
        int? exitCode = null;
        string? workspacePath = null;
        var valid = run.RawEvents.Count > 0;
        for (var index = 0; index < run.RawEvents.Count; index++)
        {
            var raw = run.RawEvents[index];
            var payload = raw.Payload;
            if (payload is null)
            {
                valid = false;
                continue;
            }

            valid &= raw.Sequence == index
                     && !string.IsNullOrWhiteSpace(raw.Kind)
                     && AgentBenchmarkHash.IsHash(raw.PayloadHash)
                     && string.Equals(
                         raw.PayloadHash,
                         AgentBenchmarkHash.Compute(payload),
                         StringComparison.Ordinal);
            try
            {
                if (raw.Kind == "adapter.process.started")
                {
                    using var document = JsonDocument.Parse(payload);
                    valid &= index == 0 && startedProcess is null;
                    startedProcess = document.RootElement
                        .GetProperty("processId")
                        .GetInt32();
                    valid &= startedProcess > 0
                             && TryGetRawString(
                                 document.RootElement,
                                 "workspacePath",
                                 out workspacePath)
                             && Path.IsPathFullyQualified(workspacePath);
                    continue;
                }

                if (raw.Kind == "adapter.process.exited")
                {
                    using var document = JsonDocument.Parse(payload);
                    valid &= index == run.RawEvents.Count - 1
                             && startedProcess is not null
                             && exitedProcess is null;
                    exitedProcess = document.RootElement
                        .GetProperty("processId")
                        .GetInt32();
                    exitCode = document.RootElement
                        .GetProperty("exitCode")
                        .GetInt32();
                    continue;
                }

                if (startedProcess is null || exitedProcess is not null)
                {
                    valid = false;
                    continue;
                }

                if (raw.Kind == "codex.stderr")
                {
                    permissionDenied |= IsDenial(payload);
                    continue;
                }

                if (raw.Kind is "codex.malformed" or "codex.truncated")
                {
                    protocolFailure = true;
                    continue;
                }

                using var provider = JsonDocument.Parse(payload);
                var root = provider.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !TryGetRawString(root, "type", out var type)
                    || !string.Equals(type, raw.Kind, StringComparison.Ordinal))
                {
                    valid = false;
                    continue;
                }

                switch (type)
                {
                    case "thread.started":
                        if (state != EvidenceProviderState.AwaitingThread
                            || !TryGetRawString(root, "thread_id", out _))
                        {
                            protocolFailure = true;
                        }
                        else
                        {
                            state = EvidenceProviderState.ThreadStarted;
                        }

                        break;
                    case "turn.started":
                        if (state != EvidenceProviderState.ThreadStarted)
                        {
                            protocolFailure = true;
                        }
                        else
                        {
                            state = EvidenceProviderState.TurnStarted;
                            turns++;
                        }

                        break;
                    case "turn.completed":
                        if (state != EvidenceProviderState.TurnStarted)
                        {
                            protocolFailure = true;
                            break;
                        }

                        state = EvidenceProviderState.TurnCompleted;
                        if (!root.TryGetProperty("usage", out var usage)
                            || usage.ValueKind != JsonValueKind.Object
                            || !TryGetRawNonNegativeInt64(
                                usage,
                                "input_tokens",
                                out var input)
                            || !TryGetRawNonNegativeInt64(
                                usage,
                                "output_tokens",
                                out var output))
                        {
                            protocolFailure = true;
                            break;
                        }

                        try
                        {
                            inputTokens = checked(inputTokens + input);
                            outputTokens = checked(outputTokens + output);
                            _ = checked(inputTokens + outputTokens);
                        }
                        catch (OverflowException)
                        {
                            protocolFailure = true;
                        }

                        break;
                    case "turn.failed":
                        if (state != EvidenceProviderState.TurnStarted)
                        {
                            protocolFailure = true;
                        }
                        else
                        {
                            state = EvidenceProviderState.TurnFailed;
                            turnFailed = true;
                            permissionDenied |= IsRawDenial(root);
                        }

                        break;
                    case "error":
                        if (state is EvidenceProviderState.TurnCompleted
                            or EvidenceProviderState.TurnFailed
                            or EvidenceProviderState.FailedBeforeTurn)
                        {
                            protocolFailure = true;
                            break;
                        }

                        providerError = true;
                        permissionDenied |= IsRawDenial(root);
                        if (state is EvidenceProviderState.AwaitingThread
                            or EvidenceProviderState.ThreadStarted)
                        {
                            state = EvidenceProviderState.FailedBeforeTurn;
                        }

                        break;
                    case "item.started":
                    case "item.updated":
                        if (state != EvidenceProviderState.TurnStarted)
                        {
                            protocolFailure = true;
                        }

                        break;
                    case "item.completed":
                        if (state != EvidenceProviderState.TurnStarted)
                        {
                            protocolFailure = true;
                            break;
                        }

                        if (!root.TryGetProperty("item", out var item)
                            || item.ValueKind != JsonValueKind.Object
                            || !TryGetRawString(item, "type", out var itemType))
                        {
                            protocolFailure = true;
                            break;
                        }

                        var itemId = TryGetRawString(item, "id", out var id)
                            ? id
                            : $"sequence-{raw.Sequence}";
                        if (!completedItems.Add(itemId))
                        {
                            protocolFailure = true;
                            break;
                        }

                        if (itemType == "agent_message")
                        {
                            if (TryGetRawString(item, "text", out var text))
                            {
                                answer = text;
                            }
                        }
                        else if (itemType == "command_execution")
                        {
                            if (!TryGetRawString(item, "command", out var command))
                            {
                                protocolFailure = true;
                                break;
                            }

                            var commandExit = item.TryGetProperty(
                                                  "exit_code",
                                                  out var commandExitValue)
                                              && commandExitValue.TryGetInt32(
                                                  out var commandExitCode)
                                ? commandExitCode
                                : (int?)null;
                            var outputText = TryGetRawString(
                                item,
                                "aggregated_output",
                                out var aggregate)
                                    ? aggregate
                                    : string.Empty;
                            var succeeded = commandExit == 0
                                            && (!TryGetRawString(
                                                    item,
                                                    "status",
                                                    out var commandStatus)
                                                || commandStatus == "completed");
                            var toolClass = CodexBenchmarkCommandEvidence.Classify(
                                command,
                                run.Sandbox,
                                task.Execution.PermittedTools);
                            toolCalls.Add(new AgentBenchmarkToolCall(
                                toolCalls.Count,
                                toolClass,
                                command,
                                AgentBenchmarkHash.Compute(item.GetRawText()),
                                succeeded));
                            protocolFailure |= !task.Execution.PermittedTools
                                .Contains(toolClass, StringComparer.Ordinal);
                            protocolFailure |= !CodexBenchmarkCommandEvidence.ObserveCommandScope(
                                command,
                                workspacePath,
                                files,
                                projects);
                            protocolFailure |= !CodexBenchmarkCommandEvidence.ObserveOutputScope(
                                outputText,
                                workspacePath,
                                files,
                                projects);
                            networkUsed |= IsNetworkCommand(command);
                            permissionDenied |= IsDenial(outputText);
                        }
                        else if (itemType == "file_change")
                        {
                            toolCalls.Add(new AgentBenchmarkToolCall(
                                toolCalls.Count,
                                "workspace-write",
                                "file_change",
                                AgentBenchmarkHash.Compute(item.GetRawText()),
                                IsRawCompleted(item)));
                            protocolFailure |= !task.Execution.PermittedTools
                                .Contains(
                                    "workspace-write",
                                    StringComparer.Ordinal);
                            if (item.TryGetProperty("changes", out var changes)
                                && changes.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var change in changes.EnumerateArray())
                                {
                                    if (change.ValueKind == JsonValueKind.Object
                                        && TryGetRawString(
                                            change,
                                            "path",
                                            out var path))
                                    {
                                        protocolFailure |= !CodexBenchmarkCommandEvidence.ObservePath(
                                            path,
                                            workspacePath,
                                            files,
                                            projects);
                                    }
                                }
                            }

                            protocolFailure |= run.Sandbox == "read-only";
                        }
                        else if (itemType == "mcp_tool_call")
                        {
                            var server = TryGetRawString(
                                item,
                                "server",
                                out var serverName)
                                    ? serverName
                                    : string.Empty;
                            var name = TryGetRawString(item, "tool", out var tool)
                                ? tool
                                : "mcp_tool_call";
                            var qualifiedName = string.IsNullOrEmpty(server)
                                ? name
                                : $"{server}.{name}";
                            var toolClass = qualifiedName.Contains(
                                                "search",
                                                StringComparison.OrdinalIgnoreCase)
                                            && task.Execution.PermittedTools.Contains(
                                                "source-search",
                                                StringComparer.Ordinal)
                                ? "source-search"
                                : task.Execution.PermittedTools.Contains(
                                    "repository-read",
                                    StringComparer.Ordinal)
                                    ? "repository-read"
                                    : "mcp";
                            toolCalls.Add(new AgentBenchmarkToolCall(
                                toolCalls.Count,
                                toolClass,
                                qualifiedName,
                                AgentBenchmarkHash.Compute(item.GetRawText()),
                                IsRawCompleted(item)));
                            protocolFailure |= !task.Execution.PermittedTools
                                .Contains(toolClass, StringComparer.Ordinal);
                        }
                        else if (itemType == "web_search")
                        {
                            networkUsed = true;
                            toolCalls.Add(new AgentBenchmarkToolCall(
                                toolCalls.Count,
                                "network",
                                "web_search",
                                AgentBenchmarkHash.Compute(item.GetRawText()),
                                IsRawCompleted(item)));
                            protocolFailure |= !task.Execution.PermittedTools
                                .Contains("network", StringComparer.Ordinal);
                        }
                        else if (itemType == "error")
                        {
                            permissionDenied |= IsRawDenial(item);
                        }

                        break;
                    default:
                        protocolFailure = true;
                        break;
                }
            }
            catch (Exception exception)
                when (exception is JsonException
                      or InvalidOperationException
                      or KeyNotFoundException
                      or OverflowException)
            {
                valid = false;
            }
        }

        var processExit = startedProcess is not null
                          && startedProcess == exitedProcess
                          && exitCode is not null;
        var reconstructedStatus = permissionDenied
            ? "permission-denied"
            : exitCode == 0
              && state == EvidenceProviderState.TurnCompleted
              && !turnFailed
              && !providerError
              && !protocolFailure
                ? "completed"
                : "failed";
        var statusMatches = run.TimedOut
            ? string.Equals(run.Status, "timed-out", StringComparison.Ordinal)
            : string.Equals(
                run.Status,
                reconstructedStatus,
                StringComparison.Ordinal);
        var toolCallsMatch = toolCalls.SequenceEqual(run.ToolCalls);
        var scopeMatches = files.SequenceEqual(run.InspectedScope.Files)
                           && projects.SequenceEqual(
                               run.InspectedScope.Projects);
        var answerMatches = run.TimedOut
            ? string.IsNullOrEmpty(run.Answer)
            : string.Equals(answer, run.Answer, StringComparison.Ordinal);
        return new CodexDiscoveryRawReconciliation(
            valid && statusMatches,
            inputTokens == run.InputTokens,
            outputTokens == run.OutputTokens,
            turns == run.Turns,
            toolCallsMatch,
            answerMatches,
            scopeMatches,
            files.ToArray(),
            projects.ToArray(),
            processExit,
            networkUsed,
            valid
            && statusMatches
            && inputTokens == run.InputTokens
            && outputTokens == run.OutputTokens
            && turns == run.Turns
            && toolCallsMatch
            && answerMatches
            && scopeMatches
            && processExit);
    }

    private static bool TryGetRawString(
        JsonElement value,
        string propertyName,
        out string result)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } text)
        {
            result = text;
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryGetRawNonNegativeInt64(
        JsonElement value,
        string propertyName,
        out long result)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.TryGetInt64(out result)
            && result >= 0)
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static bool IsRawCompleted(JsonElement item) =>
        !TryGetRawString(item, "status", out var status)
        || status == "completed";

    private static bool IsRawDenial(JsonElement value)
    {
        var message = TryGetRawString(value, "message", out var direct)
            ? direct
            : value.TryGetProperty("error", out var error)
              && error.ValueKind == JsonValueKind.Object
              && TryGetRawString(error, "message", out var nested)
                ? nested
                : value.GetRawText();
        return IsDenial(message);
    }

    private static bool IsDenial(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            value,
            "(?:permission|approval|read[- ]only|network|sandbox).{0,80}(?:denied|required|blocked|disabled|not permitted)|access is denied",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool IsNetworkCommand(string command) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            command,
            "(?:^|[\\s'\"])(?:curl|wget|Invoke-WebRequest|web_search|nuget|git\\s+(?:clone|fetch|pull)|dotnet\\s+restore)(?:[\\s'\"]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private enum EvidenceProviderState
    {
        AwaitingThread,
        ThreadStarted,
        TurnStarted,
        TurnCompleted,
        TurnFailed,
        FailedBeforeTurn,
    }

    private static CodexDiscoveryConditionMetrics Metrics(
        AgentBenchmarkCondition condition,
        IReadOnlyList<AgentBenchmarkRunResult> runs,
        string packageId,
        string packageVersion,
        string packageSource,
        string dnxExecutablePath) =>
        new(
            condition,
            runs.Count,
            runs.Count(static run =>
                string.Equals(run.Status, "completed", StringComparison.Ordinal)),
            runs.Count(static run => run.Success),
            runs.Count(static run => run.Safe),
            runs.Count(static run => run.TimedOut),
            runs.Count(run => run.ToolCalls.Any(call =>
                CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
                    call.Name,
                    packageId,
                    packageVersion,
                    packageSource,
                    CodexDiscoveryBenchmarkPreparation
                        .PackageSourceEnvironmentVariable,
                    expectedDnxExecutablePath: dnxExecutablePath))),
            runs.Count(run => run.ToolCalls.Any(call =>
                call.Succeeded
                && CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
                    call.Name,
                    packageId,
                    packageVersion,
                    packageSource,
                    CodexDiscoveryBenchmarkPreparation
                        .PackageSourceEnvironmentVariable,
                    expectedDnxExecutablePath: dnxExecutablePath))),
            runs.Sum(run => run.ToolCalls.Count(call =>
                CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
                    call.Name,
                    packageId,
                    packageVersion,
                    packageSource,
                    CodexDiscoveryBenchmarkPreparation
                        .PackageSourceEnvironmentVariable,
                    expectedDnxExecutablePath: dnxExecutablePath))),
            runs.Sum(run => run.ToolCalls.Count(call =>
                call.Succeeded
                && CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
                    call.Name,
                    packageId,
                    packageVersion,
                    packageSource,
                    CodexDiscoveryBenchmarkPreparation
                        .PackageSourceEnvironmentVariable,
                    expectedDnxExecutablePath: dnxExecutablePath))),
            runs.Count == 0
                ? 0m
                : decimal.Divide(
                    runs.Count(static run => run.Success) * 100m,
                    runs.Count),
            Median(runs.Select(static run => run.TotalTokens)),
            Median(runs.Select(static run => (long)run.ToolCallCount)),
            Median(runs.Select(static run => (long)run.Turns)),
            Median(runs.Select(static run => run.Duration.Ticks))
            / TimeSpan.TicksPerMillisecond);

    private static CodexDiscoveryRouteActivation RouteActivation(
        AgentTaskDefinition task,
        IReadOnlyList<AgentBenchmarkRunResult> runs,
        string packageId,
        string packageVersion,
        string packageSource,
        string dnxExecutablePath)
    {
        var runActivations = runs.Select(run =>
        {
            var vector = CodexBenchmarkCommandEvidence.MatchTaskRouteVector(
                task.Id,
                run.ToolCalls,
                packageId,
                packageVersion,
                packageSource,
                CodexDiscoveryBenchmarkPreparation
                    .PackageSourceEnvironmentVariable,
                dnxExecutablePath);
            var rawCommands = RawCommandOutputs(run);
            var expectedConsidered = ExpectedConsidered(task.Id);
            var steps = vector.Steps.Select((step, ordinal) =>
            {
                rawCommands.TryGetValue(step.Sequence, out var raw);
                var outputScope = raw is null
                    ? CodexDiscoveryRawScopeEvidence.Empty
                    : ReadScopeEvidence(raw.Output);
                var selectorReconciled =
                    step.Invocation.SelectorKind == "default"
                        ? outputScope.SelectorKind is null
                        : string.Equals(
                            outputScope.SelectorKind,
                            step.Invocation.SelectorKind,
                            StringComparison.Ordinal)
                          && string.Equals(
                            NormalizeScopeValue(outputScope.SelectorValue),
                            NormalizeScopeValue(step.Invocation.SelectorValue),
                            StringComparison.Ordinal);
                var eligibilityReconciled = step.Invocation.Route
                    == "show document"
                        ? outputScope.IncludeTests is null
                          && outputScope.IncludeGenerated
                          == step.Invocation.IncludeGenerated
                        : outputScope.IncludeTests
                          == step.Invocation.IncludeTests
                          && outputScope.IncludeGenerated
                          == step.Invocation.IncludeGenerated;
                var scopeReconciled =
                    outputScope.Considered == expectedConsidered
                    && selectorReconciled
                    && eligibilityReconciled;
                var identityReconciled = ordinal == 0
                    || !IsSymbolIdentityRoute(step.Invocation.Route)
                    || rawCommands.TryGetValue(
                        vector.Steps[ordinal - 1].Sequence,
                        out var priorRaw)
                    && ContainsExactSymbolId(
                        priorRaw.Output,
                        step.Invocation.Target!);
                var outputCode = raw is null
                    ? null
                    : ReadErrorCode(raw.Output);
                var expectedErrorCode = ExpectedErrorCode(task.Id);
                var outcomeReconciled = expectedErrorCode is null
                    ? step.Succeeded && outputCode is null
                    : !step.Succeeded
                      && string.Equals(
                          outputCode,
                          expectedErrorCode,
                          StringComparison.Ordinal);
                return new CodexDiscoveryActivationStep(
                    ordinal,
                    step.Sequence,
                    step.Invocation.Route,
                    ExactMatch: true,
                    step.Succeeded,
                    step.Invocation.SelectorKind,
                    step.Invocation.SelectorValue,
                    step.Invocation.IncludeTests,
                    step.Invocation.IncludeGenerated,
                    outputScope.SelectorKind,
                    outputScope.SelectorValue,
                    outputScope.IncludeTests,
                    outputScope.IncludeGenerated,
                    outputScope.Considered,
                    scopeReconciled,
                    identityReconciled,
                    outputCode,
                    outcomeReconciled);
            }).ToArray();
            var scopeReconciled = vector.Exact
                                  && steps.Length == vector.Steps.Count
                                  && steps.All(static step =>
                                      step.ScopeReconciled);
            var identityReconciled = vector.Exact
                                     && steps.Length == vector.Steps.Count
                                     && steps.All(static step =>
                                         step.IdentityReconciled);
            var outcomeReconciled = vector.Exact
                                    && steps.Length == vector.Steps.Count
                                    && steps.All(static step =>
                                        step.OutcomeReconciled);
            return new CodexDiscoveryRunActivation(
                run.ExecutionOrder,
                run.Repetition,
                vector.Exact,
                vector.Successful,
                scopeReconciled,
                identityReconciled,
                outcomeReconciled,
                run.Success
                && vector.Exact
                && scopeReconciled
                && identityReconciled
                && outcomeReconciled,
                steps);
        }).ToArray();

        return new CodexDiscoveryRouteActivation(
            task.Id,
            runs.Count,
            runActivations.Count(static run => run.ExactVectorObserved),
            runActivations.Count(static run => run.SuccessfulActivation),
            runActivations);
    }

    private static IReadOnlyDictionary<int, CodexDiscoveryRawCommandOutput>
        RawCommandOutputs(AgentBenchmarkRunResult run)
    {
        var outputs = new Dictionary<int, CodexDiscoveryRawCommandOutput>();
        var toolSequence = 0;
        foreach (var rawEvent in run.RawEvents)
        {
            if (rawEvent.Kind != "item.completed")
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(rawEvent.Payload);
                var root = document.RootElement;
                if (!root.TryGetProperty("item", out var item)
                    || item.ValueKind != JsonValueKind.Object
                    || !TryGetRawString(item, "type", out var itemType))
                {
                    continue;
                }

                if (itemType == "file_change")
                {
                    toolSequence++;
                    continue;
                }

                if (itemType != "command_execution")
                {
                    continue;
                }

                var command = TryGetRawString(item, "command", out var value)
                    ? value
                    : string.Empty;
                var output = TryGetRawString(
                    item,
                    "aggregated_output",
                    out var aggregate)
                    ? aggregate
                    : string.Empty;
                outputs.Add(
                    toolSequence,
                    new CodexDiscoveryRawCommandOutput(command, output));
                toolSequence++;
            }
            catch (JsonException)
            {
                // ValidateRun separately rejects malformed raw evidence.
            }
        }

        return outputs;
    }

    private static CodexDiscoveryRawScopeEvidence ReadScopeEvidence(
        string output)
    {
        string? selectorKind = null;
        string? selectorValue = null;
        bool? includeTests = null;
        bool? includeGenerated = null;
        int? considered = null;
        var inScope = false;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var value = line.Trim();
            if (string.Equals(value, "scope:", StringComparison.Ordinal))
            {
                inScope = true;
                continue;
            }

            if (inScope
                && line.Length > 0
                && !char.IsWhiteSpace(line[0]))
            {
                inScope = false;
            }

            if (!inScope)
            {
                if (TryReadScopeBoolean(
                        value,
                        "generated:",
                        out var documentGenerated))
                {
                    includeGenerated = documentGenerated;
                }

                continue;
            }

            if (TryReadScopeValue(value, "solution:", out var solution))
            {
                selectorKind = "solution";
                selectorValue = solution;
            }
            else if (TryReadScopeValue(value, "project:", out var project)
                     || TryReadScopeValue(
                         value,
                         "projects[1]:",
                         out project))
            {
                selectorKind = "project";
                selectorValue = project;
            }
            else if (TryReadScopeValue(value, "path:", out var path)
                     || TryReadScopeValue(value, "paths[1]:", out path))
            {
                selectorKind = "path";
                selectorValue = path;
            }
            else if (TryReadScopeBoolean(
                         value,
                         "include_tests:",
                         out var tests))
            {
                includeTests = tests;
            }
            else if (TryReadScopeBoolean(
                         value,
                         "include_generated:",
                         out var generated))
            {
                includeGenerated = generated;
            }
            else if (value.StartsWith("considered:", StringComparison.Ordinal)
                && int.TryParse(
                    value.AsSpan("considered:".Length).Trim(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed)
                && parsed >= 0)
            {
                considered = parsed;
            }
        }

        return new CodexDiscoveryRawScopeEvidence(
            selectorKind,
            selectorValue,
            includeTests,
            includeGenerated,
            considered);
    }

    private static string? ReadErrorCode(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            if (TryReadScopeValue(line.Trim(), "code:", out var code))
            {
                return code;
            }
        }

        return null;
    }

    internal static bool ContainsExactSymbolId(
        string output,
        string expected)
    {
        var start = 0;
        while ((start = output.IndexOf(
                   expected,
                   start,
                   StringComparison.Ordinal)) >= 0)
        {
            var before = start == 0 || !IsSymbolIdCharacter(output[start - 1]);
            var end = start + expected.Length;
            var after = end == output.Length
                        || !IsSymbolIdCharacter(output[end]);
            if (before && after)
            {
                return true;
            }

            start = end;
        }

        return false;
    }

    private static bool IsSymbolIdCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '-' or '/';

    private static bool IsSymbolIdentityRoute(string route) =>
        route is "show symbol" or "outline" or "context symbol";

    private static string? ExpectedErrorCode(string taskId) => taskId switch
    {
        "stale-symbol-correction" => "evidence.stale_id",
        "ambiguous-symbol-correction" => "evidence.ambiguous_id",
        _ => null,
    };

    private static bool TryReadScopeValue(
        string line,
        string prefix,
        out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        value = line[prefix.Length..].Trim().Trim('"');
        return value.Length > 0;
    }

    private static bool TryReadScopeBoolean(
        string line,
        string prefix,
        out bool value)
    {
        value = false;
        return line.StartsWith(prefix, StringComparison.Ordinal)
               && bool.TryParse(line[prefix.Length..].Trim(), out value);
    }

    private static string? NormalizeScopeValue(string? value) =>
        value?.Replace('\\', '/').TrimStart('.', '/');

    private static int ExpectedConsidered(string taskId) => taskId switch
    {
        "test-symbol-explicit-scope" => 6,
        "syntax-candidate-partial-verification" => 1,
        "document-exact-line-span" => 1,
        _ => 4,
    };

    private static decimal Median(IEnumerable<long> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0)
        {
            return 0m;
        }

        var middle = values.Length / 2;
        return values.Length % 2 == 1
            ? values[middle]
            : decimal.Divide(values[middle - 1] + values[middle], 2m);
    }

    private static decimal? PercentageChange(
        decimal baseline,
        decimal candidate)
    {
        if (baseline == 0m)
        {
            return candidate == 0m ? 0m : null;
        }

        return decimal.Divide(candidate - baseline, baseline) * 100m;
    }

    private static int CountSafetyCriticalRegressions(
        IReadOnlyList<AgentBenchmarkRunResult> baseline,
        IReadOnlyList<AgentBenchmarkRunResult> candidate)
    {
        var candidateByKey = candidate.ToDictionary(
            static run => (run.TaskId, run.Repetition));
        return baseline.Count(run =>
            run.Success
            && run.Safe
            && candidateByKey.TryGetValue(
                (run.TaskId, run.Repetition),
                out var paired)
            && paired.Success
            && !paired.Safe);
    }

    private static async ValueTask<T> LoadStrictAsync<T>(
        string path,
        string field,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new AgentBenchmarkException(
                $"The retained {field} file is missing.");
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    CodexDiscoveryBenchmarkPreparation.JsonOptions,
                    cancellationToken)
                ?? throw new AgentBenchmarkException(
                    $"The retained {field} file is empty.");
        }
        catch (JsonException exception)
        {
            throw new AgentBenchmarkException(
                $"The retained {field} file is not strict valid JSON.",
                exception);
        }
    }
}

internal sealed record CodexDiscoveryRetainedRun(
    string Schema,
    string RequestHash,
    AgentBenchmarkRunResult Run);

internal sealed record CodexDiscoverySeriesReport(
    string Schema,
    string RequestHash,
    AgentBenchmarkSeriesManifest Manifest,
    int ExpectedRunCount,
    bool Complete,
    string? Failure,
    IReadOnlyList<AgentBenchmarkRunResult> Runs);

internal sealed record CodexDiscoveryConditionMetrics(
    AgentBenchmarkCondition Condition,
    int RunCount,
    int CompletedCount,
    int SuccessCount,
    int SafeCount,
    int TimedOutCount,
    int DnxActivatedRunCount,
    int SuccessfulDnxActivatedRunCount,
    int DnxInvocationCount,
    int SuccessfulDnxInvocationCount,
    decimal SuccessRatePercent,
    decimal MedianTotalTokens,
    decimal MedianToolCalls,
    decimal MedianTurns,
    decimal MedianDurationMilliseconds);

internal sealed record CodexDiscoveryThresholdEvaluation(
    int SafetyCriticalRegressions,
    decimal? AggregateSuccessDeltaPercentagePoints,
    decimal? MedianTokenChangePercent,
    decimal? MedianToolCallChangePercent,
    bool SuccessRegression,
    bool TokenRegression,
    bool ToolCallRegression,
    bool ImprovementClaimSupported);

internal sealed record CodexDiscoveryRouteActivation(
    string TaskId,
    int CandidateRunCount,
    int ActivatedRunCount,
    int SuccessfulActivatedRunCount,
    IReadOnlyList<CodexDiscoveryRunActivation> Runs);

internal sealed record CodexDiscoveryRunActivation(
    int ExecutionOrder,
    int Repetition,
    bool ExactVectorObserved,
    bool CommandSuccessfulVector,
    bool ScopeReconciled,
    bool IdentityReconciled,
    bool OutcomeReconciled,
    bool SuccessfulActivation,
    IReadOnlyList<CodexDiscoveryActivationStep> Steps);

internal sealed record CodexDiscoveryActivationStep(
    int Ordinal,
    int ToolSequence,
    string Route,
    bool ExactMatch,
    bool CommandSucceeded,
    string SelectorKind,
    string? SelectorValue,
    bool IncludeTests,
    bool IncludeGenerated,
    string? OutputSelectorKind,
    string? OutputSelectorValue,
    bool? OutputIncludeTests,
    bool? OutputIncludeGenerated,
    int? Considered,
    bool ScopeReconciled,
    bool IdentityReconciled,
    string? OutputCode,
    bool OutcomeReconciled);

internal sealed record CodexDiscoveryRawCommandOutput(
    string Command,
    string Output);

internal sealed record CodexDiscoveryRawScopeEvidence(
    string? SelectorKind,
    string? SelectorValue,
    bool? IncludeTests,
    bool? IncludeGenerated,
    int? Considered)
{
    public static CodexDiscoveryRawScopeEvidence Empty { get; } =
        new(null, null, null, null, null);
}

internal sealed record CodexDiscoveryCandidateOnlyTaskMetrics(
    string TaskId,
    int RunCount,
    int CompletedCount,
    int SuccessCount,
    int SafeCount,
    int TimedOutCount,
    int ActivatedRunCount,
    int SuccessfulActivatedRunCount);

internal sealed record CodexDiscoveryHistoricalComparison(
    string SummaryPath,
    string SummaryHash,
    string SummarySchema,
    string RequestHash,
    string ReportHash,
    string EvidenceStatus,
    string Comparison,
    bool Comparable,
    string Detail);

internal sealed record CodexDiscoverySeriesSummary(
    string Schema,
    string RequestHash,
    string ReportHash,
    string EvidenceStatus,
    string Comparison,
    int ExpectedRunCount,
    int RetainedRunCount,
    CodexDiscoveryConditionMetrics Baseline,
    CodexDiscoveryConditionMetrics Candidate,
    CodexDiscoveryConditionMetrics AllCandidate,
    IReadOnlyList<string> ComparableTaskIds,
    IReadOnlyList<CodexDiscoveryCandidateOnlyTaskMetrics> CandidateOnlyTasks,
    CodexDiscoveryThresholdEvaluation Thresholds,
    IReadOnlyList<CodexDiscoveryRouteActivation> RouteActivations,
    CodexDiscoveryHistoricalComparison PriorSeries,
    IReadOnlyList<string> Reasons);

internal sealed record CodexDiscoveryRawReconciliation(
    bool RawSequenceAndHashes,
    bool InputTokens,
    bool OutputTokens,
    bool Turns,
    bool ToolCalls,
    bool FinalAnswer,
    bool Scope,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Projects,
    bool ProcessExit,
    bool NetworkUsed,
    bool Passed);
