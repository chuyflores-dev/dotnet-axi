using System.IO.Compression;
using System.Text;
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
        Assert.Equal(
            context.Request.Product.PackageSource.Path,
            candidateStart.Environment[
                CodexDiscoveryBenchmarkPreparation
                    .PackageSourceEnvironmentVariable]);
        Assert.False(baselineStart.Environment.ContainsKey(
            CodexDiscoveryBenchmarkPreparation
                .PackageSourceEnvironmentVariable));
        Assert.DoesNotContain(baselineStart.ArgumentList,
            argument => argument.Contains("mcp_servers.",
                StringComparison.Ordinal));
        Assert.DoesNotContain(candidateStart.ArgumentList,
            argument => argument.Contains("mcp_servers.",
                StringComparison.Ordinal));
        Assert.Empty(context.BaselineTools.ConfigurationOverrides);
        Assert.Empty(context.CandidateTools.ConfigurationOverrides);
        Assert.Null(context.BaselineTools.SkillDirectoryPath);
        Assert.Equal(
            context.Request.Product.Skill.Path,
            context.CandidateTools.SkillDirectoryPath);
        Assert.Equal("1.5.0", context.Adapter.Descriptor.Version);

        var preparationPath = Path.Combine(fixture.Root, "preparation.json");
        await CodexDiscoveryBenchmarkPreparation.WriteCreateNewAsync(
            preparationPath,
            context.Preparation);
        await CodexDiscoveryBenchmarkPreparation.ValidatePreparationAsync(
            fixture.RequestPath,
            preparationPath);
    }

    [Fact]
    public async Task Preparation_rejects_a_candidate_that_cannot_execute()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            dnxProbeSucceeds: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(() =>
            CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath)
                .AsTask());

        Assert.Contains(
            "no paid benchmark run may start",
            exception.Message,
            StringComparison.Ordinal);
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
                CreateSuccessfulRun(
                    context,
                    scheduled,
                    addToolCall: true));
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
        Assert.Equal(0, summary.Baseline.DnxInvocationCount);
        Assert.Equal(35, summary.Candidate.DnxActivatedRunCount);
        Assert.Equal(35, summary.Candidate.SuccessfulDnxActivatedRunCount);
        Assert.Equal(35, summary.Candidate.DnxInvocationCount);
        Assert.Equal(35, summary.Candidate.SuccessfulDnxInvocationCount);
        Assert.Equal(7, summary.RouteActivations.Count);
        Assert.All(summary.RouteActivations, static route =>
            Assert.Equal(5, route.SuccessfulActivatedRunCount));
        Assert.False(summary.PriorSeries.Comparable);
        Assert.Equal("failed", summary.PriorSeries.EvidenceStatus);
        Assert.Equal("incomparable", summary.PriorSeries.Comparison);
        Assert.True(CodexDiscoveryEvidenceValidator.CanonicalEquals(
            summary,
            validated));
        Assert.Equal(70,
            Directory.EnumerateFiles(
                Path.Combine(evidencePath, "runs"),
                "*.json").Count());
    }

    [Fact]
    public async Task Zero_candidate_dnx_activation_is_explicit()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var evidencePath = Path.Combine(fixture.Root, "zero-activation");
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

        Assert.Equal("zero-activation", summary.Comparison);
        Assert.Equal(0, summary.Candidate.DnxActivatedRunCount);
        Assert.Equal(0, summary.Candidate.DnxInvocationCount);
        Assert.False(summary.Thresholds.ImprovementClaimSupported);
        Assert.Contains(
            summary.Reasons,
            static reason => reason.Contains(
                "product was not exercised",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_route_activation_blocks_the_candidate()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var evidencePath = Path.Combine(fixture.Root, "route-gap");
        var store = await CodexDiscoveryEvidenceStore.CreateAsync(
            evidencePath,
            context);
        var omittedTask = context.Corpus.Tasks[0].Id;
        foreach (var scheduled in context.Preparation.Schedule)
        {
            await store.RetainAsync(
                context.Preparation.Manifest,
                CreateSuccessfulRun(
                    context,
                    scheduled,
                    addToolCall: scheduled.Condition
                                     is AgentBenchmarkCondition.Candidate
                                 && !string.Equals(
                                     scheduled.TaskId,
                                     omittedTask,
                                     StringComparison.Ordinal)));
        }

        var summary = await store.FinalizeAsync(true, null);

        Assert.Equal("activation-gap", summary.Comparison);
        var route = Assert.Single(summary.RouteActivations, candidate =>
            candidate.TaskId == omittedTask);
        Assert.Equal(0, route.SuccessfulActivatedRunCount);
        Assert.False(summary.Thresholds.ImprovementClaimSupported);
    }

    [Fact]
    public void Pinned_dnx_activation_requires_the_executed_package_identity()
    {
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --verbosity quiet -- --version",
            "dnaxi",
            "0.4.0"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/bin/zsh -lc \"dnx dnaxi@0.4.0 --verbosity quiet -- --version\"",
            "dnaxi",
            "0.4.0"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --verbosity quiet -- --version 2>&1",
            "dnaxi",
            "0.4.0"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --verbosity quiet -- --version &>/dev/null",
            "dnaxi",
            "0.4.0"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --verbosity quiet -- --version &>>/tmp/dnaxi.log",
            "dnaxi",
            "0.4.0"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "echo 'dnx dnaxi@0.4.0 --verbosity quiet -- --version'",
            "dnaxi",
            "0.4.0"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@9.9.9 --verbosity quiet -- --version",
            "dnaxi",
            "0.4.0"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --help -- --version",
            "dnaxi",
            "0.4.0"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --version 9.9.9 -- --version",
            "dnaxi",
            "0.4.0"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "true || dnx dnaxi@0.4.0 --verbosity quiet -- --version",
            "dnaxi",
            "0.4.0"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --verbosity quiet -- --version || true",
            "dnaxi",
            "0.4.0"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --verbosity quiet -- --version & > /dev/null",
            "dnaxi",
            "0.4.0"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/bin/zsh -lc \"dnx dnaxi@0.4.0 --source \\\"$DNAXI_LOCAL_FEED\\\" --verbosity quiet -- search file marker\"",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source '$DNAXI_LOCAL_FEED' --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.syntax.catch"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex=true",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex=true",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.literal"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex=false",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.literal"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex=false",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/tmp/shadow/dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "env PATH=/tmp/shadow dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/tmp/raw-tools/dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
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
                command =
                    "/bin/zsh -lc \"rg -n --glob '*.cs' 'Record\\\\.(?:g\\\\.)?cs$' .\"",
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
        Assert.False(summary.Thresholds.ImprovementClaimSupported);
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

        using (var fixture = await PreparedFixture.CreateAsync())
        {
            var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath);
            File.WriteAllText(
                Path.Combine(
                    context.Request.Product.PackageSource.Path,
                    "unexpected.nupkg"),
                "unexpected");
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

        using (var fixture = await PreparedFixture.CreateAsync(
                   shadowDnxBeforePinned: true))
        {
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync(
                   persistentDnaxiOnPath: true))
        {
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync(
                   packageCarriesSkill: true))
        {
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync(
                   invalidPackageContents: true))
        {
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync(
                   candidateVersion: "0.3.0"))
        {
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync(
                   descriptionCarriesInvocation: false))
        {
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync())
        {
            File.WriteAllText(
                Path.Combine(
                    fixture.CodexHomePath,
                    "probe-skill-hidden.txt"),
                "hidden");
            await Assert.ThrowsAsync<AgentBenchmarkException>(async () =>
                await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath));
        }

        using (var fixture = await PreparedFixture.CreateAsync())
        {
            File.WriteAllText(
                Path.Combine(
                    fixture.CodexHomePath,
                    "probe-skill-leaked.txt"),
                "leaked");
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
            var candidateRoute = task.RequiredCapabilities.Single() switch
            {
                "search.file" => "search file benchmark-marker",
                "search.text.literal" => "search text benchmark-marker",
                "search.text.regex" =>
                    "search text benchmark-marker --regex",
                "search.syntax.attributed-class" =>
                    "search syntax attributed-class --name Benchmark",
                "search.syntax.catch" =>
                    "search syntax catch --name BenchmarkException",
                "search.syntax.invocation" =>
                    "search syntax invocation --name Benchmark",
                "search.syntax.object-creation" =>
                    "search syntax object-creation --name Benchmark",
                var capability => throw new InvalidOperationException(
                    $"Unexpected test capability '{capability}'."),
            };
            var command = scheduled.Condition
                is AgentBenchmarkCondition.Candidate
                    ? $"dnx {context.Request.Product.PackageId}@{context.Request.Product.PackageVersion} --source \"${CodexDiscoveryBenchmarkPreparation.PackageSourceEnvironmentVariable}\" --verbosity quiet -- {candidateRoute}"
                    : "rg benchmark-marker";
            var toolPayload = JsonSerializer.Serialize(new
            {
                type = "item.completed",
                item = new
                {
                    id = "tool-0",
                    type = "command_execution",
                    command,
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
                command,
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
            bool rawToolsPathContainsSeparator = false,
            bool shadowDnxBeforePinned = false,
            bool persistentDnaxiOnPath = false,
            bool packageCarriesSkill = false,
            bool invalidPackageContents = false,
            bool descriptionCarriesInvocation = true,
            bool dnxProbeSucceeds = true,
            string candidateVersion = "0.4.0")
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
            var executable = InstallCodexProbe(root);
            var dnxExecutable = InstallDnxProbe(
                rawToolsPath,
                dnxProbeSucceeds);
            if (persistentDnaxiOnPath)
            {
                WriteExecutable(
                    Path.Combine(
                        rawToolsPath,
                        OperatingSystem.IsWindows()
                            ? "dnaxi.cmd"
                            : "dnaxi"),
                    "dnaxi probe");
            }
            var shadowToolsPath = shadowDnxBeforePinned
                ? Directory.CreateDirectory(
                    Path.Combine(root, "shadow-tools")).FullName
                : null;
            if (shadowToolsPath is not null)
            {
                WriteExecutable(
                    Path.Combine(
                        shadowToolsPath,
                        OperatingSystem.IsWindows() ? "dnx.exe" : "dnx"),
                    "shadow dnx probe");
            }
            var candidateDescription = descriptionCarriesInvocation
                ? "Use dotnet-axi for deterministic .NET repository evidence. "
                  + "When a controlled benchmark supplies the local feed, route applicable source discovery through "
                  + CodexDiscoveryBenchmarkPreparation.ExactCandidateInvocation
                  + "."
                : "Use dotnet-axi for deterministic .NET repository evidence.";
            var candidateInstructions = Write(
                Path.Combine(skillPath, "SKILL.md"),
                $"---\nname: dotnet-axi\ndescription: {candidateDescription}\n---\n\nUse dnx dnaxi@{candidateVersion} --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- <command> for source discovery.\n");
            var referencesPath = Directory.CreateDirectory(
                Path.Combine(skillPath, "references")).FullName;
            Write(
                Path.Combine(referencesPath, "codex.md"),
                "# Codex sandbox operation\n");
            var packageSource = Directory.CreateDirectory(
                Path.Combine(root, "package-source")).FullName;
            var package = WritePackage(
                Path.Combine(packageSource, "dnaxi.0.4.0.nupkg"),
                packageCarriesSkill ? candidateInstructions : null,
                invalidPackageContents);
            var baselineInstructions = Write(
                Path.Combine(root, "baseline-instructions.txt"),
                "no product instructions");
            var priorRequestHash = new string('d', 64);
            var priorReportHash = new string('e', 64);
            var priorSummary = await WriteJsonAsync(
                Path.Combine(root, "prior-summary.json"),
                new
                {
                    schema = CodexDiscoveryBenchmarkPreparation
                        .PriorSummarySchema,
                    requestHash = priorRequestHash,
                    reportHash = priorReportHash,
                    evidenceStatus = "failed",
                    comparison = "incomparable",
                    expectedRunCount = 70,
                    retainedRunCount = 70,
                });
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
            var executableSearchPathEntries = shadowToolsPath is null
                ? [rawToolsPin]
                : new CodexDiscoveryArtifactPin[]
                {
                    new(
                        shadowToolsPath,
                        await CodexDiscoveryBenchmarkPreparation
                            .HashDirectoryAsync(shadowToolsPath)),
                    rawToolsPin,
                };
            var packageSourcePin = new CodexDiscoveryArtifactPin(
                packageSource,
                await CodexDiscoveryBenchmarkPreparation.HashDirectoryAsync(
                    packageSource));
            var baselineTools = await WriteJsonAsync(
                Path.Combine(root, "baseline-tools.json"),
                new CodexDiscoveryToolConfiguration(
                    CodexDiscoveryBenchmarkPreparation.ToolConfigurationSchema,
                    SkillDirectoryPath: null,
                    [],
                    executableSearchPathEntries,
                    new Dictionary<string, string>()));
            var candidateTools = await WriteJsonAsync(
                Path.Combine(root, "candidate-tools.json"),
                new CodexDiscoveryToolConfiguration(
                    CodexDiscoveryBenchmarkPreparation.ToolConfigurationSchema,
                    skillPath,
                    [],
                    executableSearchPathEntries,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [CodexDiscoveryBenchmarkPreparation
                            .PackageSourceEnvironmentVariable] = packageSource,
                    }));
            var corpus = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "AgentTasks",
                "source-discovery",
                "corpus.json");
            var request = new CodexDiscoveryBenchmarkRequest(
                CodexDiscoveryBenchmarkPreparation.RequestSchema,
                "codex-040-discovery-test",
                await PinFileAsync(executable),
                await PinFileAsync(dnxExecutable),
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
                    packageSourcePin,
                    new CodexDiscoveryArtifactPin(
                        skillPath,
                        await CodexDiscoveryBenchmarkPreparation
                            .HashDirectoryAsync(skillPath))),
                new CodexDiscoveryPriorSeriesPin(
                    await PinFileAsync(priorSummary),
                    priorRequestHash,
                    priorReportHash),
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

        private static string WritePackage(
            string path,
            string? skillPath,
            bool invalidContents)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            if (invalidContents)
            {
                return path;
            }

            WriteArchiveEntry(
                archive,
                "dnaxi.nuspec",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
                  <metadata>
                    <id>dnaxi</id>
                    <version>0.4.0</version>
                    <authors>dotnet-axi</authors>
                    <description>Test package.</description>
                    <packageTypes><packageType name="DotnetTool" /></packageTypes>
                  </metadata>
                </package>
                """);
            WriteArchiveEntry(
                archive,
                "tools/net10.0/any/DotnetToolSettings.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <DotNetCliTool Version="1">
                  <Commands>
                    <Command Name="dnaxi" EntryPoint="dnaxi.dll" Runner="dotnet" />
                  </Commands>
                </DotNetCliTool>
                """);
            foreach (var entryName in new[]
                     {
                         "tools/net10.0/any/dnaxi.dll",
                         "tools/net10.0/any/dnaxi.deps.json",
                         "tools/net10.0/any/dnaxi.runtimeconfig.json",
                     })
            {
                WriteArchiveEntry(archive, entryName, "test");
            }

            if (skillPath is not null)
            {
                archive.CreateEntryFromFile(
                    skillPath,
                    "skills/dotnet-axi/SKILL.md",
                    CompressionLevel.NoCompression);
            }

            return path;
        }

        private static void WriteArchiveEntry(
            ZipArchive archive,
            string entryName,
            string content)
        {
            var entry = archive.CreateEntry(
                entryName,
                CompressionLevel.NoCompression);
            using var writer = new StreamWriter(
                entry.Open(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
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
            return InstallProcessProbe(destinationDirectory, "codex");
        }

        private static string InstallDnxProbe(
            string destinationDirectory,
            bool succeeds)
        {
            var executable = InstallProcessProbe(
                destinationDirectory,
                "dnx");
            if (!succeeds)
            {
                File.WriteAllText(
                    Path.Combine(destinationDirectory, "dnx.exit-code"),
                    "73");
            }

            return executable;
        }

        private static string InstallProcessProbe(
            string destinationDirectory,
            string executableName)
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
                OperatingSystem.IsWindows()
                    ? $"{executableName}.exe"
                    : executableName);
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
