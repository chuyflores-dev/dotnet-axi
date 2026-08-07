using System.Text.Json;

namespace DotNetAxi.Testing.Tests;

public sealed class CodexDiscoveryBenchmarkTests
{
    [Fact]
    public async Task Preparation_seals_exact_manual_series_without_starting_codex()
    {
        using var fixture = await PreparedFixture.CreateAsync();

        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var second = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);

        Assert.Equal(70, context.Preparation.Schedule.Count);
        Assert.Equal(70, context.Preparation.UsageBoundary.RunCount);
        Assert.Equal(8_400,
            context.Preparation.UsageBoundary.AgentTimeoutBudgetSeconds);
        Assert.Equal(1_400,
            context.Preparation.UsageBoundary.FinalizationBudgetSeconds);
        Assert.Null(context.Preparation.UsageBoundary.ProviderTokenLimit);
        Assert.Equal(AgentBenchmarkDispatch.Manual,
            context.Preparation.Manifest.Dispatch);
        Assert.Equal("codex-cli 0.146.0",
            context.Preparation.Manifest.Execution.AgentVersion);
        Assert.Equal("gpt-5.6-sol",
            context.Preparation.Manifest.Execution.ModelId);
        Assert.Equal("low",
            context.Preparation.Manifest.Execution.ReasoningSetting);
        Assert.Equal("read-only",
            context.Preparation.Manifest.Execution.Sandbox);
        Assert.Equal("never",
            context.Preparation.Manifest.Execution.PermissionProfile);
        Assert.Equal("disabled",
            context.Preparation.Manifest.Execution.NetworkPolicy);
        Assert.True(CodexDiscoveryEvidenceValidator.CanonicalEquals(
            context.Preparation.Schedule,
            second.Preparation.Schedule));
        Assert.All(
            context.Preparation.Schedule.GroupBy(run =>
                (run.TaskId, run.Condition)),
            group => Assert.Equal(5, group.Count()));
        Assert.Equal(
            Enumerable.Range(0, 70),
            context.Preparation.Schedule.Select(run => run.ExecutionOrder));
        var task = context.Corpus.Tasks[0];
        var baselineStart = context.Adapter.CreateStartInfo(AdapterInput(
            context,
            task,
            AgentBenchmarkCondition.Baseline,
            fixture.Root));
        var candidateStart = context.Adapter.CreateStartInfo(AdapterInput(
            context,
            task,
            AgentBenchmarkCondition.Candidate,
            fixture.Root));
        Assert.Equal(
            string.Join(Path.PathSeparator,
                context.BaselineTools.ExecutableSearchPathEntries.Select(
                    static entry => entry.Path)),
            baselineStart.Environment["PATH"]);
        Assert.Equal(
            string.Join(Path.PathSeparator,
                context.CandidateTools.ExecutableSearchPathEntries.Select(
                    static entry => entry.Path)),
            candidateStart.Environment["PATH"]);
        Assert.DoesNotContain(baselineStart.ArgumentList,
            argument => argument.Contains("mcp_servers.",
                StringComparison.Ordinal));
        Assert.DoesNotContain(candidateStart.ArgumentList,
            argument => argument.Contains("mcp_servers.",
                StringComparison.Ordinal));

        var preparationPath = Path.Combine(fixture.Root, "preparation.json");
        await CodexDiscoveryBenchmarkPreparation.WriteCreateNewAsync(
            preparationPath,
            context.Preparation);
        await CodexDiscoveryBenchmarkPreparation.ValidatePreparationAsync(
            fixture.RequestPath,
            preparationPath);
    }

    [Fact]
    public async Task Retained_series_reconciles_all_runs_and_thresholds()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var evidencePath = Path.Combine(fixture.Root, "evidence");
        var store = await CodexDiscoveryEvidenceStore.CreateAsync(
            evidencePath,
            context);

        foreach (var scheduled in context.Preparation.Schedule)
        {
            await store.RetainAsync(
                context.Preparation.Manifest,
                CreateSuccessfulRun(context, scheduled));
        }

        var summary = await store.FinalizeAsync(true, null);
        var validated =
            await CodexDiscoveryEvidenceValidator.ValidateDirectoryAsync(
                fixture.RequestPath,
                evidencePath);

        Assert.Equal("complete", summary.EvidenceStatus);
        Assert.Equal("improvement", summary.Comparison);
        Assert.Equal(70, summary.RetainedRunCount);
        Assert.Equal(-10m, summary.Thresholds.MedianTokenChangePercent);
        Assert.True(summary.Thresholds.ImprovementClaimSupported);
        Assert.True(CodexDiscoveryEvidenceValidator.CanonicalEquals(
            summary,
            validated));
        Assert.Equal(70,
            Directory.EnumerateFiles(
                Path.Combine(evidencePath, "runs"),
                "*.json").Count());
    }

    [Fact]
    public async Task Failed_timed_out_and_missing_evidence_stays_incomparable()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var failedPath = Path.Combine(fixture.Root, "failed-evidence");
        var failedStore = await CodexDiscoveryEvidenceStore.CreateAsync(
            failedPath,
            context);
        foreach (var scheduled in context.Preparation.Schedule)
        {
            var run = scheduled.ExecutionOrder switch
            {
                0 => CreateFailedRun(context, scheduled),
                1 => CreateTimedOutRun(context, scheduled),
                _ => CreateSuccessfulRun(context, scheduled),
            };
            await failedStore.RetainAsync(context.Preparation.Manifest, run);
        }

        var failed = await failedStore.FinalizeAsync(true, null);
        var failedValidated =
            await CodexDiscoveryEvidenceValidator.ValidateDirectoryAsync(
                fixture.RequestPath,
                failedPath);
        Assert.Equal("failed", failed.EvidenceStatus);
        Assert.Equal("incomparable", failed.Comparison);
        Assert.True(CodexDiscoveryEvidenceValidator.CanonicalEquals(
            failed,
            failedValidated));

        var missingPath = Path.Combine(fixture.Root, "missing-evidence");
        var missingStore = await CodexDiscoveryEvidenceStore.CreateAsync(
            missingPath,
            context);
        var first = context.Preparation.Schedule[0];
        await missingStore.RetainAsync(
            context.Preparation.Manifest,
            CreateSuccessfulRun(context, first));
        var missing = await missingStore.FinalizeAsync(false, null);
        var missingValidated =
            await CodexDiscoveryEvidenceValidator.ValidateDirectoryAsync(
                fixture.RequestPath,
                missingPath);
        Assert.Equal("missing", missing.EvidenceStatus);
        Assert.Equal("incomparable", missing.Comparison);
        Assert.True(CodexDiscoveryEvidenceValidator.CanonicalEquals(
            missing,
            missingValidated));
    }

    [Fact]
    public async Task Raw_exit_drift_cannot_certify_a_successful_run()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var scheduled = context.Preparation.Schedule[0];
        var run = CreateSuccessfulRun(context, scheduled);
        var raw = run.RawEvents.ToArray();
        var exitPayload = JsonSerializer.Serialize(new
        {
            processId = 10_000 + scheduled.ExecutionOrder,
            exitCode = 1,
        });
        raw[^1] = Raw(raw.Length - 1, "adapter.process.exited", exitPayload);
        var falsified = WithRawEvents(run, raw);
        var report = new CodexDiscoverySeriesReport(
            CodexDiscoveryEvidenceStore.ReportSchema,
            context.Preparation.RequestHash,
            context.Preparation.Manifest,
            context.Preparation.Schedule.Count,
            Complete: false,
            Failure: null,
            [falsified]);

        Assert.Throws<AgentBenchmarkException>(() =>
            CodexDiscoveryEvidenceValidator.ValidateReport(context, report));
    }

    [Fact]
    public async Task Raw_read_only_fallback_and_glob_scope_reconcile()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var scheduled = context.Preparation.Schedule[0];
        var run = CreateSuccessfulRun(context, scheduled);
        var searchPayload = JsonSerializer.Serialize(new
        {
            type = "item.completed",
            item = new
            {
                id = "search",
                type = "command_execution",
                command = "/bin/zsh -lc \"rg -n --glob '*.cs' Record .\"",
                aggregated_output =
                    "./src/Discovery/Cases/InvocationCases.cs:7:Record();\n",
                exit_code = 0,
                status = "completed",
            },
        });
        var fallbackPayload = JsonSerializer.Serialize(new
        {
            type = "item.completed",
            item = new
            {
                id = "fallback",
                type = "command_execution",
                command = "/bin/zsh -lc \"python3 -c 'print(1)'\"",
                aggregated_output =
                    "./src/Discovery/Cases/InvocationCases.cs\n",
                exit_code = 0,
                status = "completed",
            },
        });
        var rawEvents = run.RawEvents.Take(3)
            .Concat([
                Raw(3, "item.completed", searchPayload),
                Raw(4, "item.completed", fallbackPayload),
            ])
            .Concat(run.RawEvents.Skip(3).Select((raw, index) =>
                Raw(index + 5, raw.Kind, raw.Payload)))
            .ToArray();
        var toolCalls = new[]
        {
            ToolCall(0, "source-search", searchPayload),
            ToolCall(1, "repository-read", fallbackPayload),
        };
        var reconciled = WithObservedEvidence(
            run,
            toolCalls,
            new AgentBenchmarkInspectedScope(
                ["src/Discovery/Cases/InvocationCases.cs"],
                []),
            rawEvents);
        var report = new CodexDiscoverySeriesReport(
            CodexDiscoveryEvidenceStore.ReportSchema,
            context.Preparation.RequestHash,
            context.Preparation.Manifest,
            context.Preparation.Schedule.Count,
            Complete: false,
            Failure: null,
            [reconciled]);

        CodexDiscoveryEvidenceValidator.ValidateReport(context, report);
    }

    [Fact]
    public async Task Zero_baseline_tool_calls_and_positive_candidate_is_regression()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var evidencePath = Path.Combine(fixture.Root, "tool-regression");
        var store = await CodexDiscoveryEvidenceStore.CreateAsync(
            evidencePath,
            context);
        foreach (var scheduled in context.Preparation.Schedule)
        {
            await store.RetainAsync(
                context.Preparation.Manifest,
                CreateSuccessfulRun(
                    context,
                    scheduled,
                    addToolCall: scheduled.Condition
                        is AgentBenchmarkCondition.Candidate));
        }

        var summary = await store.FinalizeAsync(true, null);
        Assert.Equal("regression", summary.Comparison);
        Assert.True(summary.Thresholds.ToolCallRegression);
    }

    [Fact]
    public async Task Preparation_proves_runtime_auth_and_tool_directory_pins()
    {
        using (var fixture = await PreparedFixture.CreateAsync())
        {
            var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath);
            var evidencePath = Path.Combine(
                fixture.Root,
                "drift-evidence");
            var store = await CodexDiscoveryEvidenceStore.CreateAsync(
                evidencePath,
                context);
            File.WriteAllText(
                Path.Combine(fixture.RawToolsPath, "drift"),
                "changed");
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await store.RetainAsync(
                    context.Preparation.Manifest,
                    CreateSuccessfulRun(
                        context,
                        context.Preparation.Schedule[0])));
            Assert.Single(Directory.EnumerateFiles(
                Path.Combine(evidencePath, "runs"),
                "*.json"));
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync())
        {
            File.WriteAllText(
                Path.Combine(
                    fixture.CodexHomePath,
                    "probe-authentication.txt"),
                "Logged in using API key");
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync())
        {
            File.WriteAllText(
                Path.Combine(fixture.CodexHomePath, "probe-version.txt"),
                "codex-cli 0.145.0");
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync(
                   rawToolsPathContainsSeparator: true))
        {
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }
    }

    private static AgentBenchmarkRunResult CreateSuccessfulRun(
        CodexDiscoveryPreparedContext context,
        AgentBenchmarkScheduledRun scheduled,
        bool addToolCall = false)
    {
        var task = context.Corpus.Tasks.Single(candidate =>
            candidate.Id == scheduled.TaskId);
        var condition = scheduled.Condition is AgentBenchmarkCondition.Baseline
            ? context.Configuration.Baseline
            : context.Configuration.Candidate;
        var answer = string.Join('\n', task.SuccessOracle.ExpectedFacts);
        var inputTokens = scheduled.Condition is AgentBenchmarkCondition.Baseline
            ? 90L
            : 81L;
        var outputTokens = scheduled.Condition is AgentBenchmarkCondition.Baseline
            ? 10L
            : 9L;
        var processId = 10_000 + scheduled.ExecutionOrder;
        var rawEvents = new List<AgentBenchmarkRawEvent>
        {
            Raw(0, "adapter.process.started",
                JsonSerializer.Serialize(new
                {
                    processId,
                    workspacePath = Path.GetDirectoryName(
                        context.Request.Corpus.Artifact.Path),
                })),
            Raw(1, "thread.started",
                JsonSerializer.Serialize(new
                {
                    type = "thread.started",
                    thread_id = $"thread-{scheduled.ExecutionOrder}",
                })),
            Raw(2, "turn.started",
                JsonSerializer.Serialize(new { type = "turn.started" })),
        };
        var toolCalls = new List<AgentBenchmarkToolCall>();
        if (addToolCall)
        {
            var toolPayload = JsonSerializer.Serialize(new
            {
                type = "item.completed",
                item = new
                {
                    id = "tool-0",
                    type = "command_execution",
                    command = "rg benchmark-marker",
                    aggregated_output = string.Empty,
                    exit_code = 0,
                    status = "completed",
                },
            });
            rawEvents.Add(Raw(
                rawEvents.Count,
                "item.completed",
                toolPayload));
            using var toolDocument = JsonDocument.Parse(toolPayload);
            toolCalls.Add(new AgentBenchmarkToolCall(
                0,
                "source-search",
                "rg benchmark-marker",
                AgentBenchmarkHash.Compute(toolDocument.RootElement
                    .GetProperty("item").GetRawText()),
                true));
        }

        rawEvents.Add(Raw(
            rawEvents.Count,
            "item.completed",
            JsonSerializer.Serialize(new
            {
                type = "item.completed",
                item = new
                {
                    id = "message-0",
                    type = "agent_message",
                    text = answer,
                },
            })));
        rawEvents.Add(Raw(
            rawEvents.Count,
            "turn.completed",
            JsonSerializer.Serialize(new
            {
                type = "turn.completed",
                usage = new
                {
                    input_tokens = inputTokens,
                    output_tokens = outputTokens,
                },
            })));
        rawEvents.Add(Raw(
            rawEvents.Count,
            "adapter.process.exited",
            JsonSerializer.Serialize(new { processId, exitCode = 0 })));
        const string workspaceHash =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        return new AgentBenchmarkRunResult(
            scheduled.RunId,
            scheduled.TaskId,
            scheduled.Condition,
            scheduled.Repetition,
            scheduled.ExecutionOrder,
            1,
            task.Execution.TimeoutSeconds,
            false,
            "completed",
            answer,
            true,
            true,
            inputTokens,
            outputTokens,
            1,
            toolCalls,
            TimeSpan.FromMilliseconds(20),
            new AgentBenchmarkInspectedScope([], []),
            [
                new("claims-supported", true, "reconciled"),
                new("network-unused", true, "reconciled"),
                new("workspace-unchanged", true, "reconciled"),
            ],
            [
                new("fixture-content-hash", true, true, "reconciled"),
                new("safety-oracle", true, true, "reconciled"),
                new("success-oracle", true, true, "reconciled"),
            ],
            new AgentBenchmarkRunVersions(
                context.Configuration.Provenance.HarnessVersion,
                context.Preparation.Manifest.Adapter.Id,
                context.Preparation.Manifest.Adapter.Version,
                CodexDiscoveryBenchmarkPreparation.CodexCliVersion,
                CodexDiscoveryBenchmarkPreparation.ModelId,
                CodexDiscoveryBenchmarkPreparation.ReasoningSetting,
                CodexDiscoveryBenchmarkPreparation.CorpusVersion,
                CodexDiscoveryBenchmarkPreparation.ProductSchema),
            new AgentBenchmarkRunHashes(
                context.Request.Settings.Sha256,
                AgentBenchmarkHash.Compute(task.Prompt),
                condition.InstructionsHash,
                condition.ToolConfigurationHash,
                task.Repository.ContentHash,
                workspaceHash,
                workspaceHash,
                context.Configuration.Provenance.FixtureCommit,
                context.Configuration.Provenance.ProductCommit,
                AgentBenchmarkHash.Trajectory(rawEvents)),
            CodexDiscoveryBenchmarkPreparation.Sandbox,
            CodexDiscoveryBenchmarkPreparation.PermissionProfile,
            CodexDiscoveryBenchmarkPreparation.NetworkPolicy,
            rawEvents);
    }

    private static AgentBenchmarkRunResult CreateFailedRun(
        CodexDiscoveryPreparedContext context,
        AgentBenchmarkScheduledRun scheduled) =>
        CreateIncompleteRun(context, scheduled, timedOut: false);

    private static AgentBenchmarkRunResult CreateTimedOutRun(
        CodexDiscoveryPreparedContext context,
        AgentBenchmarkScheduledRun scheduled) =>
        CreateIncompleteRun(context, scheduled, timedOut: true);

    private static AgentBenchmarkRunResult CreateIncompleteRun(
        CodexDiscoveryPreparedContext context,
        AgentBenchmarkScheduledRun scheduled,
        bool timedOut)
    {
        var task = context.Corpus.Tasks.Single(candidate =>
            candidate.Id == scheduled.TaskId);
        var condition = scheduled.Condition is AgentBenchmarkCondition.Baseline
            ? context.Configuration.Baseline
            : context.Configuration.Candidate;
        var processId = 10_000 + scheduled.ExecutionOrder;
        var rawEvents = new List<AgentBenchmarkRawEvent>
        {
            Raw(0, "adapter.process.started",
                JsonSerializer.Serialize(new
                {
                    processId,
                    workspacePath = Path.GetDirectoryName(
                        context.Request.Corpus.Artifact.Path),
                })),
            Raw(1, "thread.started",
                JsonSerializer.Serialize(new
                {
                    type = "thread.started",
                    thread_id = $"thread-{scheduled.ExecutionOrder}",
                })),
            Raw(2, "turn.started",
                JsonSerializer.Serialize(new { type = "turn.started" })),
        };
        if (!timedOut)
        {
            rawEvents.Add(Raw(
                rawEvents.Count,
                "turn.failed",
                JsonSerializer.Serialize(new
                {
                    type = "turn.failed",
                    error = new { message = "provider failed" },
                })));
        }

        rawEvents.Add(Raw(
            rawEvents.Count,
            "adapter.process.exited",
            JsonSerializer.Serialize(new { processId, exitCode = 1 })));
        const string workspaceHash =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        return new AgentBenchmarkRunResult(
            scheduled.RunId,
            scheduled.TaskId,
            scheduled.Condition,
            scheduled.Repetition,
            scheduled.ExecutionOrder,
            1,
            task.Execution.TimeoutSeconds,
            timedOut,
            timedOut ? "timed-out" : "failed",
            string.Empty,
            false,
            false,
            0,
            0,
            1,
            [],
            TimeSpan.FromMilliseconds(20),
            new AgentBenchmarkInspectedScope([], []),
            [
                new("claims-supported", false, "reconciled"),
                new("network-unused", !timedOut, "reconciled"),
                new("workspace-unchanged", true, "reconciled"),
            ],
            [
                new("fixture-content-hash", true, true, "reconciled"),
                new("safety-oracle", true, false, "reconciled"),
                new("success-oracle", true, false, "reconciled"),
            ],
            new AgentBenchmarkRunVersions(
                context.Configuration.Provenance.HarnessVersion,
                context.Preparation.Manifest.Adapter.Id,
                context.Preparation.Manifest.Adapter.Version,
                CodexDiscoveryBenchmarkPreparation.CodexCliVersion,
                CodexDiscoveryBenchmarkPreparation.ModelId,
                CodexDiscoveryBenchmarkPreparation.ReasoningSetting,
                CodexDiscoveryBenchmarkPreparation.CorpusVersion,
                CodexDiscoveryBenchmarkPreparation.ProductSchema),
            new AgentBenchmarkRunHashes(
                context.Request.Settings.Sha256,
                AgentBenchmarkHash.Compute(task.Prompt),
                condition.InstructionsHash,
                condition.ToolConfigurationHash,
                task.Repository.ContentHash,
                workspaceHash,
                workspaceHash,
                context.Configuration.Provenance.FixtureCommit,
                context.Configuration.Provenance.ProductCommit,
                AgentBenchmarkHash.Trajectory(rawEvents)),
            CodexDiscoveryBenchmarkPreparation.Sandbox,
            CodexDiscoveryBenchmarkPreparation.PermissionProfile,
            CodexDiscoveryBenchmarkPreparation.NetworkPolicy,
            rawEvents);
    }

    private static AgentBenchmarkRunResult WithRawEvents(
        AgentBenchmarkRunResult run,
        IReadOnlyList<AgentBenchmarkRawEvent> rawEvents) =>
        new(
            run.RunId,
            run.TaskId,
            run.Condition,
            run.Repetition,
            run.ExecutionOrder,
            run.StartAttempts,
            run.TimeoutSeconds,
            run.TimedOut,
            run.Status,
            run.Answer,
            run.Success,
            run.Safe,
            run.InputTokens,
            run.OutputTokens,
            run.Turns,
            run.ToolCalls,
            run.Duration,
            run.InspectedScope,
            run.SafetyChecks,
            run.Validations,
            run.Versions,
            run.Hashes with
            {
                RawTrajectory = AgentBenchmarkHash.Trajectory(rawEvents),
            },
            run.Sandbox,
            run.PermissionProfile,
            run.NetworkPolicy,
            rawEvents);

    private static AgentBenchmarkRunResult WithObservedEvidence(
        AgentBenchmarkRunResult run,
        IReadOnlyList<AgentBenchmarkToolCall> toolCalls,
        AgentBenchmarkInspectedScope inspectedScope,
        IReadOnlyList<AgentBenchmarkRawEvent> rawEvents) =>
        new(
            run.RunId,
            run.TaskId,
            run.Condition,
            run.Repetition,
            run.ExecutionOrder,
            run.StartAttempts,
            run.TimeoutSeconds,
            run.TimedOut,
            run.Status,
            run.Answer,
            run.Success,
            run.Safe,
            run.InputTokens,
            run.OutputTokens,
            run.Turns,
            toolCalls,
            run.Duration,
            inspectedScope,
            run.SafetyChecks,
            run.Validations,
            run.Versions,
            run.Hashes with
            {
                RawTrajectory = AgentBenchmarkHash.Trajectory(rawEvents),
            },
            run.Sandbox,
            run.PermissionProfile,
            run.NetworkPolicy,
            rawEvents);

    private static AgentBenchmarkToolCall ToolCall(
        int sequence,
        string toolClass,
        string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var item = document.RootElement.GetProperty("item");
        return new AgentBenchmarkToolCall(
            sequence,
            toolClass,
            item.GetProperty("command").GetString()!,
            AgentBenchmarkHash.Compute(item.GetRawText()),
            true);
    }

    private static AgentBenchmarkAdapterInput AdapterInput(
        CodexDiscoveryPreparedContext context,
        AgentTaskDefinition task,
        AgentBenchmarkCondition condition,
        string workspacePath)
    {
        var exposure = condition is AgentBenchmarkCondition.Baseline
            ? context.Configuration.Baseline
            : context.Configuration.Candidate;
        return new AgentBenchmarkAdapterInput(
            "path-check/000000",
            1,
            0,
            0,
            condition,
            task,
            workspacePath,
            new Dictionary<string, string>(),
            context.Configuration.Execution,
            AgentBenchmarkHash.Compute(task.Prompt),
            exposure.InstructionsHash,
            exposure.ToolConfigurationHash);
    }

    private static AgentBenchmarkRawEvent Raw(
        int sequence,
        string kind,
        string payload) =>
        new(sequence, kind, payload, AgentBenchmarkHash.Compute(payload));

    private sealed class PreparedFixture : IDisposable
    {
        private PreparedFixture(
            string root,
            string requestPath,
            string codexHomePath,
            string rawToolsPath)
        {
            Root = root;
            RequestPath = requestPath;
            CodexHomePath = codexHomePath;
            RawToolsPath = rawToolsPath;
        }

        public string Root { get; }

        public string RequestPath { get; }

        public string CodexHomePath { get; }

        public string RawToolsPath { get; }

        public static async ValueTask<PreparedFixture> CreateAsync(
            bool rawToolsPathContainsSeparator = false)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-codex-discovery-tests",
                $"{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var codexHome = Directory.CreateDirectory(
                Path.Combine(root, "codex-home")).FullName;
            var skillPath = Directory.CreateDirectory(
                Path.Combine(root, "skill")).FullName;
            var rawToolsPath = Directory.CreateDirectory(
                Path.Combine(
                    root,
                    rawToolsPathContainsSeparator
                        ? $"raw{Path.PathSeparator}tools"
                        : "raw-tools")).FullName;
            var candidateBinPath = Directory.CreateDirectory(
                Path.Combine(root, "candidate-bin")).FullName;
            var executable = InstallCodexProbe(root);
            var package = WriteExecutable(
                Path.Combine(
                    candidateBinPath,
                    OperatingSystem.IsWindows() ? "dnaxi.exe" : "dnaxi"),
                "package");
            var candidateInstructions = Write(
                Path.Combine(skillPath, "SKILL.md"),
                "candidate skill instructions");
            var baselineInstructions = Write(
                Path.Combine(root, "baseline-instructions.txt"),
                "no product instructions");
            var settings = await WriteJsonAsync(
                Path.Combine(root, "settings.json"),
                new CodexDiscoverySettings(
                    CodexDiscoveryBenchmarkPreparation.SettingsSchema,
                    CodexDiscoveryBenchmarkPreparation.CodexCliVersion,
                    CodexDiscoveryBenchmarkPreparation.ModelId,
                    CodexDiscoveryBenchmarkPreparation.ReasoningSetting,
                    CodexDiscoveryBenchmarkPreparation.Sandbox,
                    CodexDiscoveryBenchmarkPreparation.PermissionProfile,
                    CodexDiscoveryBenchmarkPreparation.NetworkPolicy,
                    CodexDiscoveryBenchmarkPreparation.AuthenticationMethod));
            var rawToolsPin = new CodexDiscoveryArtifactPin(
                rawToolsPath,
                await CodexDiscoveryBenchmarkPreparation.HashDirectoryAsync(
                    rawToolsPath));
            var candidateBinPin = new CodexDiscoveryArtifactPin(
                candidateBinPath,
                await CodexDiscoveryBenchmarkPreparation.HashDirectoryAsync(
                    candidateBinPath));
            var baselineTools = await WriteJsonAsync(
                Path.Combine(root, "baseline-tools.json"),
                new CodexDiscoveryToolConfiguration(
                    CodexDiscoveryBenchmarkPreparation.ToolConfigurationSchema,
                    [
                        "skills.config=[]",
                    ],
                    [rawToolsPin]));
            var candidateTools = await WriteJsonAsync(
                Path.Combine(root, "candidate-tools.json"),
                new CodexDiscoveryToolConfiguration(
                    CodexDiscoveryBenchmarkPreparation.ToolConfigurationSchema,
                    [
                        $"skills.config=[{{path={JsonSerializer.Serialize(candidateInstructions)},enabled=true}}]",
                    ],
                    [candidateBinPin, rawToolsPin]));
            var corpus = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "AgentTasks",
                "source-discovery",
                "corpus.json");
            var request = new CodexDiscoveryBenchmarkRequest(
                CodexDiscoveryBenchmarkPreparation.RequestSchema,
                "codex-030-discovery-test",
                await PinFileAsync(executable),
                codexHome,
                await PinFileAsync(settings),
                new CodexDiscoveryCorpusPin(
                    CodexDiscoveryBenchmarkPreparation.CorpusId,
                    CodexDiscoveryBenchmarkPreparation.CorpusVersion,
                    CodexDiscoveryBenchmarkPreparation.ProductMilestone,
                    await PinFileAsync(corpus),
                    [
                        "search.file",
                        "search.syntax.attributed-class",
                        "search.syntax.catch",
                        "search.syntax.invocation",
                        "search.syntax.object-creation",
                        "search.text.literal",
                        "search.text.regex",
                    ]),
                new CodexDiscoveryProductPin(
                    CodexDiscoveryBenchmarkPreparation.PackageId,
                    CodexDiscoveryBenchmarkPreparation.PackageVersion,
                    CodexDiscoveryBenchmarkPreparation.ProductSchema,
                    await PinFileAsync(package),
                    new CodexDiscoveryArtifactPin(
                        skillPath,
                        await CodexDiscoveryBenchmarkPreparation
                            .HashDirectoryAsync(skillPath))),
                5,
                20260806,
                1,
                10,
                "1.0.0",
                new string('a', 40),
                new string('b', 40),
                new CodexDiscoveryConditionPin(
                    await PinFileAsync(baselineInstructions),
                    await PinFileAsync(baselineTools)),
                new CodexDiscoveryConditionPin(
                    await PinFileAsync(candidateInstructions),
                    await PinFileAsync(candidateTools)));
            var requestPath = await WriteJsonAsync(
                Path.Combine(root, "request.json"),
                request);
            return new PreparedFixture(
                root,
                requestPath,
                codexHome,
                rawToolsPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string Write(string path, string value)
        {
            File.WriteAllText(path, value);
            return path;
        }

        private static string WriteExecutable(string path, string value)
        {
            File.WriteAllText(path, value);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }

            return path;
        }

        private static string InstallCodexProbe(string destinationDirectory)
        {
            const string applicationName =
                "DotNetAxi.DotNet.ProcessTestApp";
            var sourceDirectory = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "tests",
                "DotNetAxi.DotNet.ProcessTestApp",
                "bin",
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                "net10.0"));
            var sourceHost = Path.Combine(
                sourceDirectory,
                OperatingSystem.IsWindows()
                    ? $"{applicationName}.exe"
                    : applicationName);
            var destinationHost = Path.Combine(
                destinationDirectory,
                OperatingSystem.IsWindows() ? "codex.exe" : "codex");
            File.Copy(sourceHost, destinationHost);
            foreach (var extension in new[]
                     {
                         ".dll", ".deps.json", ".runtimeconfig.json",
                     })
            {
                File.Copy(
                    Path.Combine(
                        sourceDirectory,
                        $"{applicationName}{extension}"),
                    Path.Combine(
                        destinationDirectory,
                        $"{applicationName}{extension}"));
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    destinationHost,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }

            return destinationHost;
        }

        private static async ValueTask<string> WriteJsonAsync<T>(
            string path,
            T value)
        {
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(
                    value,
                    CodexDiscoveryBenchmarkPreparation.JsonOptions));
            return path;
        }

        private static async ValueTask<CodexDiscoveryArtifactPin> PinFileAsync(
            string path) =>
            new(path,
                await CodexDiscoveryBenchmarkPreparation.HashFileAsync(path));
    }
}
