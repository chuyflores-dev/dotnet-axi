using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetAxi.Testing.Tests;

public sealed class CodexDiscoveryBenchmarkTests
{
    private const string StaleCandidateId =
        "symbol/v2/UmVjb25jaWxl/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string AmbiguousCandidate1Id =
        "symbol/v2/UmVsb2NhdGVkV2lkZ2V0/cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc/dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string AmbiguousCandidate2Id =
        "symbol/v2/UmVsb2NhdGVkV2lkZ2V0/cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc/eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    [Theory]
    [InlineData("path-complete.toon", "syntax-candidate-partial-verification", 0, "completed", true)]
    [InlineData("path-partial.toon", "syntax-candidate-partial-verification", 0, "completed", true)]
    [InlineData("stale.toon", "stale-symbol-correction", 1, "completed", true)]
    [InlineData("ambiguous.toon", "ambiguous-symbol-correction", 1, "completed", true)]
    [InlineData("unexpected-error.toon", "syntax-candidate-partial-verification", 1, "completed", false)]
    [InlineData("malformed-output.toon", "syntax-candidate-partial-verification", 0, "completed", false)]
    [InlineData("wrong-error-code.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("missing-correction.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("wrong-scope.toon", "syntax-candidate-partial-verification", 0, "completed", false)]
    [InlineData("wrong-identity.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("wrong-route.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("wrong-project.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("wrong-candidate.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("wrong-ambiguous-candidate.toon", "ambiguous-symbol-correction", 1, "completed", false)]
    [InlineData("trailing-prose.toon", "syntax-candidate-partial-verification", 0, "completed", false)]
    [InlineData("misnested-scope.toon", "syntax-candidate-partial-verification", 0, "completed", false)]
    [InlineData("inconsistent-candidates.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("signature-outside-candidates.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("unknown-nested-root-data.toon", "syntax-candidate-partial-verification", 0, "completed", false)]
    [InlineData("misnested-nested-root-data.toon", "syntax-candidate-partial-verification", 0, "completed", false)]
    [InlineData("wrong-command-root.toon", "syntax-candidate-partial-verification", 0, "completed", false)]
    [InlineData("candidate-depth-six.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("duplicate-candidate-identity.toon", "ambiguous-symbol-correction", 1, "completed", false)]
    [InlineData("wrong-candidate-file.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("invalid-candidate-id.toon", "stale-symbol-correction", 1, "completed", false)]
    [InlineData("stale.toon", "stale-symbol-correction", 2, "completed", false)]
    [InlineData("stale.toon", "stale-symbol-correction", 73, "completed", false)]
    [InlineData("stale.toon", "stale-symbol-correction", null, "completed", false)]
    [InlineData("stale.toon", "stale-symbol-correction", 1, null, false)]
    [InlineData("stale.toon", "stale-symbol-correction", 1, "failed", false)]
    [InlineData("stale.toon", "stale-symbol-correction", 0, "failed", false)]
    public async Task Normalized_route_reconciliation_uses_live_shaped_fixtures(
        string fixture,
        string taskId,
        int? exitCode,
        string? itemStatus,
        bool expected)
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "AgentTasks",
            "symbol-context",
            "corpus.json"));
        var task = corpus.Tasks.Single(candidate => candidate.Id == taskId);
        const string prefix =
            "dnx dnaxi@0.5.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- ";
        var route = FixtureRoute(taskId, fixture);
        var commandSucceeded = exitCode == 0
                               && itemStatus is null or "completed";
        var unrelated = new AgentBenchmarkToolCall(
            0,
            "source-search",
            "rg benchmark-marker",
            "fixture-prefix",
            false);
        var call = new AgentBenchmarkToolCall(
            1,
            "source-search",
            prefix + route,
            "fixture",
            commandSucceeded);
        var output = await File.ReadAllTextAsync(RouteFixturePath(fixture));

        var result = CodexDiscoveryEvidenceValidator.ReconcileTaskRoute(
            task,
            [unrelated, call],
            new Dictionary<int, CodexDiscoveryRawCommandOutput>
            {
                [0] = new(
                    unrelated.Name,
                    string.Empty,
                    1,
                    "completed"),
                [1] = new(
                    call.Name,
                    output,
                    exitCode,
                    itemStatus),
            },
            "dnaxi",
            "0.5.0",
            "/tmp/feed",
            "/tmp/raw-tools/dnx");

        Assert.Equal(expected, result.Passed);
    }

    [Theory]
    [InlineData("trailing-prose.toon")]
    [InlineData("misnested-scope.toon")]
    [InlineData("duplicate-selectors.toon")]
    [InlineData("missing-candidates.toon")]
    [InlineData("inconsistent-candidates.toon")]
    [InlineData("signature-outside-candidates.toon")]
    [InlineData("unknown-nested-root-data.toon")]
    [InlineData("misnested-nested-root-data.toon")]
    [InlineData("wrong-command-root.toon")]
    [InlineData("candidate-depth-six.toon")]
    [InlineData("invalid-candidate-id.toon")]
    public async Task Structured_output_reader_rejects_invalid_live_envelopes(
        string fixture)
    {
        var output = await File.ReadAllTextAsync(RouteFixturePath(fixture));

        var parsed = CodexBenchmarkStructuredOutputReader.Read(output);

        Assert.False(parsed.WellFormed);
    }

    [Theory]
    [InlineData("path-complete.toon", "search syntax invocation")]
    [InlineData("path-partial.toon", "search syntax invocation")]
    [InlineData("path-complete.toon", "search syntax class")]
    [InlineData("path-partial.toon", "search syntax class")]
    [InlineData("path-complete.toon", "search syntax catch")]
    [InlineData("path-partial.toon", "search syntax catch")]
    [InlineData("path-complete.toon", "search syntax object-creation")]
    [InlineData("path-partial.toon", "search syntax object-creation")]
    public async Task Structured_output_reader_accepts_path_scoped_syntax_family(
        string fixture,
        string command)
    {
        var output = await File.ReadAllTextAsync(RouteFixturePath(fixture));
        output = output.Replace(
            "command: search syntax invocation",
            $"command: {command}",
            StringComparison.Ordinal);

        var parsed = CodexBenchmarkStructuredOutputReader.Read(output);

        Assert.True(parsed.WellFormed);
        Assert.Equal(command, parsed.Command);
    }

    [Fact]
    public async Task Structured_output_reader_retains_live_candidate_identities()
    {
        var stale = CodexBenchmarkStructuredOutputReader.Read(
            await File.ReadAllTextAsync(RouteFixturePath("stale.toon")));
        var staleCandidate = Assert.Single(stale.Candidates);
        Assert.True(stale.WellFormed);
        Assert.Equal(StaleCandidateId, staleCandidate.Id);
        Assert.Equal("src/Core/StaleService.cs", staleCandidate.File);
        Assert.Equal(5, staleCandidate.Line);

        var ambiguous = CodexBenchmarkStructuredOutputReader.Read(
            await File.ReadAllTextAsync(RouteFixturePath("ambiguous.toon")));
        Assert.True(ambiguous.WellFormed);
        Assert.Equal(
            [AmbiguousCandidate1Id, AmbiguousCandidate2Id],
            ambiguous.Candidates.Select(static candidate => candidate.Id));
        Assert.Equal(
            [
                "src/Core/moved/RelocatedWidget1.cs",
                "src/Core/moved/RelocatedWidget2.cs",
            ],
            ambiguous.Candidates.Select(static candidate => candidate.File));
        Assert.All(
            ambiguous.Candidates,
            static candidate => Assert.Equal(3, candidate.Line));
    }

    [Fact]
    public async Task Raw_validator_replays_the_live_route_fixture_matrix()
    {
        using var prepared = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            prepared.RequestPath);
        (string Fixture, string TaskId, int? ExitCode, string? ItemStatus,
            bool Expected)[]
            cases =
            [
                ("path-complete.toon", "syntax-candidate-partial-verification", 0, "completed", true),
                ("path-partial.toon", "syntax-candidate-partial-verification", 0, "completed", true),
                ("stale.toon", "stale-symbol-correction", 1, "completed", true),
                ("ambiguous.toon", "ambiguous-symbol-correction", 1, "completed", true),
                ("unexpected-error.toon", "syntax-candidate-partial-verification", 1, "completed", false),
                ("malformed-output.toon", "syntax-candidate-partial-verification", 0, "completed", false),
                ("wrong-error-code.toon", "stale-symbol-correction", 1, "completed", false),
                ("missing-correction.toon", "stale-symbol-correction", 1, "completed", false),
                ("wrong-scope.toon", "syntax-candidate-partial-verification", 0, "completed", false),
                ("wrong-identity.toon", "stale-symbol-correction", 1, "completed", false),
                ("wrong-route.toon", "stale-symbol-correction", 1, "completed", false),
                ("wrong-project.toon", "stale-symbol-correction", 1, "completed", false),
                ("wrong-candidate.toon", "stale-symbol-correction", 1, "completed", false),
                ("wrong-ambiguous-candidate.toon", "ambiguous-symbol-correction", 1, "completed", false),
                ("trailing-prose.toon", "syntax-candidate-partial-verification", 0, "completed", false),
                ("misnested-scope.toon", "syntax-candidate-partial-verification", 0, "completed", false),
                ("inconsistent-candidates.toon", "stale-symbol-correction", 1, "completed", false),
                ("signature-outside-candidates.toon", "stale-symbol-correction", 1, "completed", false),
                ("unknown-nested-root-data.toon", "syntax-candidate-partial-verification", 0, "completed", false),
                ("misnested-nested-root-data.toon", "syntax-candidate-partial-verification", 0, "completed", false),
                ("wrong-command-root.toon", "syntax-candidate-partial-verification", 0, "completed", false),
                ("candidate-depth-six.toon", "stale-symbol-correction", 1, "completed", false),
                ("duplicate-candidate-identity.toon", "ambiguous-symbol-correction", 1, "completed", false),
                ("wrong-candidate-file.toon", "stale-symbol-correction", 1, "completed", false),
                ("invalid-candidate-id.toon", "stale-symbol-correction", 1, "completed", false),
                ("stale.toon", "stale-symbol-correction", 2, "completed", false),
                ("stale.toon", "stale-symbol-correction", 73, "completed", false),
                ("stale.toon", "stale-symbol-correction", null, "completed", false),
                ("stale.toon", "stale-symbol-correction", 1, null, false),
                ("stale.toon", "stale-symbol-correction", 1, "failed", false),
                ("stale.toon", "stale-symbol-correction", 0, "failed", false),
            ];

        foreach (var testCase in cases)
        {
            var scheduled = context.Preparation.Schedule.First(run =>
                run.Condition is AgentBenchmarkCondition.Candidate
                && run.TaskId == testCase.TaskId);
            var runs = context.Preparation.Schedule
                .Take(scheduled.ExecutionOrder + 1)
                .Select(run => run.ExecutionOrder == scheduled.ExecutionOrder
                    ? CreateRouteFixtureRun(
                        context,
                        run,
                        testCase.Fixture,
                        testCase.ExitCode,
                        testCase.ItemStatus)
                    : CreateSuccessfulRun(context, run))
                .ToArray();
            var report = new CodexDiscoverySeriesReport(
                CodexDiscoveryEvidenceStore.ReportSchema,
                context.Preparation.RequestHash,
                context.Preparation.Manifest,
                context.Preparation.Schedule.Count,
                Complete: false,
                Failure: null,
                runs);

            CodexDiscoveryEvidenceValidator.ValidateReport(context, report);
            var summary = CodexDiscoveryEvidenceValidator.CreateSummary(
                context,
                report,
                new string('d', 64));
            var route = summary.RouteActivations.Single(candidate =>
                candidate.TaskId == testCase.TaskId);
            var activation = route.Runs.Single(candidate =>
                candidate.ExecutionOrder == scheduled.ExecutionOrder);
            Assert.Equal(testCase.Expected, activation.SuccessfulActivation);
        }
    }

    [Fact]
    public void Codex_local_probe_timeout_leaves_parallel_ci_headroom()
    {
        Assert.Equal(
            30,
            CodexDiscoveryBenchmarkPreparation
                .CodexLocalProbeTimeoutSeconds);
    }

    [Fact]
    public async Task Preparation_seals_exact_manual_series_without_starting_codex()
    {
        using var fixture = await PreparedFixture.CreateAsync();

        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var second = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);

        Assert.Equal(100, context.Preparation.Schedule.Count);
        Assert.Equal(100, context.Preparation.UsageBoundary.RunCount);
        Assert.Equal(12_600,
            context.Preparation.UsageBoundary.AgentTimeoutBudgetSeconds);
        Assert.Equal(2_000,
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
        Assert.Equal(
            "codex-controlled-workspace/v1",
            context.Preparation.Isolation.Protocol);
        Assert.True(context.Preparation.Isolation.FreshWorkspacePerRun);
        Assert.True(
            context.Preparation.Isolation.CommandEvidenceBoundaryEnforced);
        Assert.True(
            context.Preparation.Isolation.SharedAuthenticationHomeDenied);
        Assert.True(context.Preparation.Isolation.NetworkDisabled);
        Assert.True(CodexDiscoveryEvidenceValidator.CanonicalEquals(
            context.Preparation.Schedule,
            second.Preparation.Schedule));
        Assert.All(
            context.Preparation.Schedule.GroupBy(run =>
                (run.TaskId, run.Condition)),
            group => Assert.Equal(5, group.Count()));
        Assert.Equal(50, context.Preparation.Schedule.Count(run =>
            run.Condition is AgentBenchmarkCondition.Baseline));
        Assert.Equal(50, context.Preparation.Schedule.Count(run =>
            run.Condition is AgentBenchmarkCondition.Candidate));
        Assert.Equal(
            Enumerable.Range(0, 100),
            context.Preparation.Schedule.Select(run => run.ExecutionOrder));
        var task = context.Corpus.Tasks[0];
        var baselineInput = AdapterInput(
            context,
            task,
            AgentBenchmarkCondition.Baseline,
            Path.Combine(fixture.Root, "baseline-run", "workspace"));
        var candidateInput = AdapterInput(
            context,
            task,
            AgentBenchmarkCondition.Candidate,
            Path.Combine(fixture.Root, "candidate-run", "workspace"));
        Directory.CreateDirectory(baselineInput.WorkspacePath);
        Directory.CreateDirectory(candidateInput.WorkspacePath);
        await context.Adapter.PrepareWorkspaceAsync(baselineInput);
        await context.Adapter.PrepareWorkspaceAsync(candidateInput);
        var baselineStart = context.Adapter.CreateStartInfo(baselineInput);
        var candidateStart = context.Adapter.CreateStartInfo(candidateInput);
        Assert.DoesNotContain(
            context.BaselineTools.ExecutableSearchPathEntries[0].Path,
            baselineStart.Environment["PATH"],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            context.CandidateTools.ExecutableSearchPathEntries[0].Path,
            candidateStart.Environment["PATH"],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            context.Request.Product.PackageSource.Path,
            candidateStart.Environment[
                CodexDiscoveryBenchmarkPreparation
                    .PackageSourceEnvironmentVariable],
            StringComparison.Ordinal);
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
        Assert.Equal("1.9.0", context.Adapter.Descriptor.Version);
        Assert.NotNull(context.Adapter.DotNetInstallationRoot);
        Assert.True(Directory.Exists(
            context.Adapter.DotNetInstallationRoot));

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
    public async Task Preparation_does_not_use_host_visibility_as_a_readiness_gate()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            isolationProbeLeaks: true);

        var prepared = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);

        Assert.True(
            prepared.Preparation.Isolation.CommandEvidenceBoundaryEnforced);
    }

    [Fact]
    public async Task Preparation_rejects_a_missing_bounded_skill_reader()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            includeBoundedReader: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(() =>
            CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath)
                .AsTask());

        Assert.Contains(
            "bounded skill reader",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_an_unusable_bounded_skill_reader()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            boundedReaderSucceeds: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(() =>
            CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath)
                .AsTask());

        Assert.Contains(
            "bounded skill reader",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_a_missing_raw_dotnet_command()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            includeRawDotnet: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "raw 'dotnet'",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_a_missing_raw_source_search_command()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            includeRawSourceSearch: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "and 'rg' commands",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_an_unusable_raw_dotnet_command()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            rawDotnetSucceeds: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "cannot evaluate",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_raw_dotnet_without_compile_item_evaluation()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            rawDotnetSupportsCompileItems: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "cannot evaluate repository project ownership",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_an_unusable_raw_source_search_command()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            rawSourceSearchSucceeds: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "cannot find an exact line",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_raw_source_search_without_common_codex_grammar()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            rawSourceSearchSupportsCodexArguments: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "common Codex source-search arguments",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_accepts_native_windows_separators_from_raw_source_search()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            rawSourceSearchUsesWindowsSeparators: true);

        var prepared = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);

        Assert.Equal(100, prepared.Preparation.Schedule.Count);
    }

    [Fact]
    public async Task Preparation_rejects_the_historical_set_normalizer()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            useHistoricalCorpusNormalizer: true);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "approved oracle response contracts",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_a_semantic_task_without_raw_dotnet_permission()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            omitSemanticDotnetToolClass: true);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            async () => await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                fixture.RequestPath));

        Assert.Contains(
            "approved oracle response contracts",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_the_previous_harness_identity()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            harnessVersion: "2.6.0");

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(() =>
            CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath)
                .AsTask());

        Assert.Contains(
            "approved harness identity",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_rejects_a_truncated_prior_summary()
    {
        using var fixture = await PreparedFixture.CreateAsync(
            canonicalPriorSummary: false);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(() =>
            CodexDiscoveryBenchmarkPreparation.PrepareAsync(
                    fixture.RequestPath)
                .AsTask());

        Assert.Contains(
            "immutable retained 0.4.0",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("contradictory")]
    [InlineData("extra-field")]
    [InlineData("invalid-nesting")]
    [InlineData("malformed-row")]
    [InlineData("scalar-parent")]
    public async Task Preparation_rejects_ambiguous_candidate_version_output(
        string outputMode)
    {
        using var fixture = await PreparedFixture.CreateAsync(
            dnxProbeOutputMode: outputMode);

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
        Assert.Equal(100, summary.RetainedRunCount);
        Assert.Equal(-10m, summary.Thresholds.MedianTokenChangePercent);
        Assert.Equal(0m, summary.Thresholds.MedianDurationChangePercent);
        Assert.True(summary.Thresholds.ImprovementClaimSupported);
        Assert.Equal(0, summary.Baseline.DnxInvocationCount);
        Assert.Equal(50, summary.Candidate.DnxActivatedRunCount);
        Assert.Equal(40, summary.Candidate.SuccessfulDnxActivatedRunCount);
        Assert.Equal(70, summary.Candidate.DnxInvocationCount);
        Assert.Equal(60, summary.Candidate.SuccessfulDnxInvocationCount);
        Assert.Equal(50, summary.AllCandidate.DnxActivatedRunCount);
        Assert.Equal(40, summary.AllCandidate.SuccessfulDnxActivatedRunCount);
        Assert.Equal(10, summary.RouteActivations.Count);
        Assert.All(summary.RouteActivations, static route =>
            Assert.Equal(5, route.SuccessfulActivatedRunCount));
        Assert.All(summary.RouteActivations, route =>
            Assert.All(route.Runs, run =>
            {
                Assert.True(run.ExactVectorObserved);
                Assert.Equal(
                    route.TaskId is not "stale-symbol-correction"
                        and not "ambiguous-symbol-correction",
                    run.CommandSuccessfulVector);
                Assert.True(run.ScopeReconciled);
                Assert.True(run.IdentityReconciled);
                Assert.True(run.OutcomeReconciled);
                Assert.True(run.SuccessfulActivation);
            }));
        var explicitScope = Assert.Single(
            summary.RouteActivations,
            static route => route.TaskId == "test-symbol-explicit-scope");
        var explicitStep = Assert.Single(explicitScope.Runs[0].Steps);
        Assert.Equal("solution", explicitStep.SelectorKind);
        Assert.Equal("Workspace.slnx", explicitStep.SelectorValue);
        Assert.True(explicitStep.IncludeTests);
        Assert.False(explicitStep.IncludeGenerated);
        Assert.Equal(6, explicitStep.Considered);
        var composedShow = Assert.Single(
            summary.RouteActivations,
            static route => route.TaskId == "fresh-symbol-identity-show");
        Assert.All(composedShow.Runs, static run =>
            Assert.Equal(
                ["search symbol", "show symbol"],
                run.Steps.Select(static step => step.Route)));
        Assert.Equal(10, summary.ComparableTaskIds.Count);
        Assert.Empty(summary.CandidateOnlyTasks);
        Assert.False(summary.PriorSeries.Comparable);
        Assert.Equal("complete", summary.PriorSeries.EvidenceStatus);
        Assert.Equal("no-improvement", summary.PriorSeries.Comparison);
        Assert.True(CodexDiscoveryEvidenceValidator.CanonicalEquals(
            summary,
            validated));
        Assert.Equal(100,
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
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx.exe dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx.exe"));
        Assert.Equal(
            OperatingSystem.IsWindows(),
            CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
                "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
                "dnaxi",
                "0.4.0",
                "/tmp/feed",
                "DNAXI_LOCAL_FEED",
                "search.file",
                "/tmp/raw-tools/dnx.exe"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/tmp/raw-tools/dnx.exe dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx.exe"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/tmp/shadow/dnx.exe dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file marker",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx.exe"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search syntax class --attribute CorpusCase --path .",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.syntax.attributed-class"));
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search syntax class --attribute=CorpusCase --path .",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.syntax.attributed-class"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search syntax class --path .",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.syntax.attributed-class"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search syntax class --attribute --path .",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.syntax.attributed-class"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search syntax attributed-class --name CorpusCase",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.syntax.attributed-class"));
    }

    [Fact]
    public void Pinned_dnx_activation_accepts_leading_environment_assignments()
    {
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "DOTNET_CLI_HOME=\"/tmp/dotnet home\" NUGET_PACKAGES=/tmp/packages dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text 'Handle.*Async' --regex --path .",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "'DOTNET_CLI_HOME=/tmp/dotnet' dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "pwsh -Command \"DOTNET_CLI_HOME=/tmp/dotnet dnx dnaxi@0.4.0 --source '$DNAXI_LOCAL_FEED' --verbosity quiet -- search text marker --regex\"",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "PATH=/tmp/shadow dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "1INVALID=value dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "DOTNET_CLI_HOME=/tmp/dotnet\u00A0dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "\u00A0dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text marker --regex",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.text.regex",
            "/tmp/raw-tools/dnx"));
    }

    [Theory]
    [InlineData(
        "/bin/zsh -lc 'dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file '\"'Handler.cs' --path . --limit 200\"",
        "search.file")]
    [InlineData(
        "/bin/zsh -lc 'dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text '\"'Archive pipeline ready' --path . --limit 1000\"",
        "search.text.literal")]
    [InlineData(
        "/bin/zsh -lc 'dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search text '\"'Handle(?:Audit|Retry)Async' --regex --path . --limit 1000\"",
        "search.text.regex")]
    public void Pinned_dnx_activation_accepts_retained_codex_shell_display(
        string command,
        string capability)
    {
        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            command,
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            capability,
            "/tmp/raw-tools/dnx"));
    }

    [Fact]
    public void Pinned_dnx_activation_rejects_unsafe_codex_shell_display()
    {
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/bin/zsh -lc 'dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search file '\"'Handler.cs' --path . --limit 200; echo unsafe\"",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/bin/zsh -lc 'dnx dnaxi@0.4.0 --source '$DNAXI_LOCAL_FEED' --verbosity quiet -- search file '\"'Handler.cs' --path . --limit 200\"",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "/bin/zsh -lc 'dnx dnaxi@0.4.0 --source '\"'$DNAXI_LOCAL_FEED' --verbosity quiet -- search file Handler.cs\"",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --add-source /tmp/rogue --verbosity quiet -- search file Handler.cs",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --add-source=/tmp/rogue --verbosity quiet -- search file Handler.cs",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --source=/tmp/rogue --verbosity quiet -- search file Handler.cs",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "dnx dnaxi@0.4.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet --invalid \\\"; true \\\" -- search file Handler.cs",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "pwsh -Command \"dnx dnaxi@0.4.0 --source \\\"$DNAXI_LOCAL_FEED\\\" --verbosity quiet -- search file Handler.cs \\; true\"",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            "pwsh -Command \"dnx dnaxi@0.4.0 --source \\\"$DNAXI_LOCAL_FEED\\\" --verbosity quiet -- search file Handler.cs `\"; true `\"\"",
            "dnaxi",
            "0.4.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "search.file",
            "/tmp/raw-tools/dnx"));
    }

    [Fact]
    public void Symbol_context_activation_parses_route_scope_and_eligibility()
    {
        const string command =
            "dnx dnaxi@0.5.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- search symbol ScopeProbe --solution Workspace.slnx --include-tests --include-generated=false";

        Assert.True(CodexBenchmarkCommandEvidence.TryParsePinnedDnxInvocation(
            command,
            "dnaxi",
            "0.5.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "/tmp/raw-tools/dnx",
            out var invocation));
        Assert.NotNull(invocation);
        Assert.Equal("search symbol", invocation.Route);
        Assert.Equal("ScopeProbe", invocation.Target);
        Assert.Equal("solution", invocation.SelectorKind);
        Assert.Equal("Workspace.slnx", invocation.SelectorValue);
        Assert.True(invocation.IncludeTests);
        Assert.False(invocation.IncludeGenerated);
    }

    [Theory]
    [InlineData(
        "search symbol LedgerService --project src/Core/Core.csproj",
        "search.symbol.declaration")]
    [InlineData(
        "show symbol symbol/v2/example --project src/Core/Core.csproj",
        "show.symbol.identity")]
    [InlineData(
        "search syntax invocation --name MissingAudit --path loose/UnownedCandidate.cs --verify",
        "search.syntax.verify")]
    [InlineData(
        "show document docs/Runbook.txt --start-line 5 --end-line 6",
        "show.document")]
    [InlineData(
        "outline symbol/v2/example --project src/Core/Core.csproj",
        "outline.syntax")]
    [InlineData(
        "context symbol symbol/v2/example --project src/Core/Core.csproj --include declaration,owner",
        "context.symbol")]
    public void Symbol_context_capabilities_require_their_exact_route(
        string route,
        string capability)
    {
        var command =
            $"dnx dnaxi@0.5.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- {route}";

        Assert.True(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            command,
            "dnaxi",
            "0.5.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            capability,
            "/tmp/raw-tools/dnx"));
        Assert.False(CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
            command.Replace(" -- ", " -- --help ", StringComparison.Ordinal),
            "dnaxi",
            "0.5.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            capability,
            "/tmp/raw-tools/dnx"));
    }

    [Fact]
    public void Symbol_context_activation_matches_all_task_route_vectors()
    {
        const string prefix =
            "dnx dnaxi@0.5.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- ";
        var commands = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["test-symbol-explicit-scope"] =
            [
                "search symbol SymbolContext.Tests.ScopeProbe --solution Workspace.slnx --include-tests",
            ],
            ["symbol-owner-framework-variants"] =
            [
                "search symbol SymbolContext.Product.LedgerService --project src/Core/Core.csproj",
            ],
            ["fresh-symbol-identity-show"] =
            [
                "search symbol LedgerService.Format --project src/Core/Core.csproj",
                "show symbol symbol/v2/fresh --project src/Core/Core.csproj",
            ],
            ["stale-symbol-correction"] =
            [
                $"show symbol {CodexBenchmarkCommandEvidence.ExpectedStaleSymbolId} --project src/Core/Core.csproj",
            ],
            ["ambiguous-symbol-correction"] =
            [
                $"show symbol {CodexBenchmarkCommandEvidence.ExpectedAmbiguousSymbolId} --project src/Core/Core.csproj",
            ],
            ["syntax-candidate-partial-verification"] =
            [
                "search syntax invocation --name MissingAudit --path loose/UnownedCandidate.cs --verify",
            ],
            ["bounded-symbol-show"] =
            [
                "search symbol LedgerService --project src/Core/Core.csproj",
                "show symbol symbol/v2/bounded --project src/Core/Core.csproj --max-chars 24",
            ],
            ["document-exact-line-span"] =
            [
                "show document docs/Runbook.txt --start-line 5 --end-line 6",
            ],
            ["symbol-outline"] =
            [
                "search symbol LedgerService --project src/Core/Core.csproj",
                "outline symbol/v2/outline --project src/Core/Core.csproj",
            ],
            ["context-whole-section-truncation"] =
            [
                "search symbol LedgerService --project src/Core/Core.csproj",
                "context symbol symbol/v2/context --project src/Core/Core.csproj --include declaration --include owner --include document --include outline --max-chars 0",
            ],
        };

        foreach (var (taskId, taskCommands) in commands)
        {
            var calls = taskCommands
                .Select((command, index) => new AgentBenchmarkToolCall(
                    index + 10,
                    "source-search",
                    prefix + command,
                    AgentBenchmarkHash.Compute(command),
                    true))
                .ToArray();
            Assert.All(calls, call => Assert.True(
                CodexBenchmarkCommandEvidence.IsPinnedDnxInvocation(
                    call.Name,
                    "dnaxi",
                    "0.5.0",
                    "/tmp/feed",
                    "DNAXI_LOCAL_FEED",
                    expectedDnxExecutablePath: "/tmp/raw-tools/dnx"),
                taskId));
            var activation = CodexBenchmarkCommandEvidence.MatchTaskRouteVector(
                taskId,
                calls,
                "dnaxi",
                "0.5.0",
                "/tmp/feed",
                "DNAXI_LOCAL_FEED",
                "/tmp/raw-tools/dnx");

            Assert.True(activation.Exact, taskId);
            Assert.True(activation.Successful, taskId);
            Assert.Equal(taskCommands.Length, activation.Steps.Count);
            Assert.Equal(10, activation.Steps[0].Sequence);
        }
    }

    [Fact]
    public void Symbol_context_activation_keeps_exact_route_separate_from_success()
    {
        const string prefix =
            "dnx dnaxi@0.5.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- ";
        var calls = new[]
        {
            new AgentBenchmarkToolCall(
                2,
                "source-search",
                prefix + "search symbol LedgerService --project src/Core/Core.csproj",
                "search",
                true),
            new AgentBenchmarkToolCall(
                3,
                "repository-read",
                prefix + "show symbol symbol/v2/bounded --project src/Core/Core.csproj --max-chars 24",
                "show",
                false),
        };

        var activation = CodexBenchmarkCommandEvidence.MatchTaskRouteVector(
            "bounded-symbol-show",
            calls,
            "dnaxi",
            "0.5.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "/tmp/raw-tools/dnx");

        Assert.True(activation.Exact);
        Assert.False(activation.Successful);
        Assert.Equal([2, 3], activation.Steps.Select(static step => step.Sequence));
        Assert.False(activation.Steps[1].Succeeded);
    }

    [Fact]
    public void Symbol_context_activation_rejects_wrong_scope_and_route_order()
    {
        const string prefix =
            "dnx dnaxi@0.5.0 --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- ";
        var calls = new[]
        {
            new AgentBenchmarkToolCall(
                1,
                "repository-read",
                prefix + "show symbol symbol/v2/fresh --project src/Core/Core.csproj",
                "show",
                true),
            new AgentBenchmarkToolCall(
                2,
                "source-search",
                prefix + "search symbol LedgerService.Format --project src/Core/Core.csproj --include-generated",
                "search",
                true),
        };

        var activation = CodexBenchmarkCommandEvidence.MatchTaskRouteVector(
            "fresh-symbol-identity-show",
            calls,
            "dnaxi",
            "0.5.0",
            "/tmp/feed",
            "DNAXI_LOCAL_FEED",
            "/tmp/raw-tools/dnx");

        Assert.False(activation.Exact);
        Assert.False(activation.Successful);
        Assert.Empty(activation.Steps);

        var wrongQuery = new[]
        {
            new AgentBenchmarkToolCall(
                1,
                "source-search",
                prefix + "search symbol NotLedgerServiceWrong --project src/Core/Core.csproj",
                "search",
                true),
            new AgentBenchmarkToolCall(
                2,
                "repository-read",
                prefix + "show symbol symbol/v2/fresh --project src/Core/Core.csproj",
                "show",
                true),
        };
        var wrongQueryActivation =
            CodexBenchmarkCommandEvidence.MatchTaskRouteVector(
                "fresh-symbol-identity-show",
                wrongQuery,
                "dnaxi",
                "0.5.0",
                "/tmp/feed",
                "DNAXI_LOCAL_FEED",
                "/tmp/raw-tools/dnx");

        Assert.False(wrongQueryActivation.Exact);

        var optionBeforeTarget = new[]
        {
            new AgentBenchmarkToolCall(
                1,
                "source-search",
                prefix + "search symbol --namespace LedgerService NotLedgerServiceWrong --project src/Core/Core.csproj",
                "search",
                true),
            new AgentBenchmarkToolCall(
                2,
                "repository-read",
                prefix + "show symbol symbol/v2/fresh --project src/Core/Core.csproj",
                "show",
                true),
        };
        var optionBeforeTargetActivation =
            CodexBenchmarkCommandEvidence.MatchTaskRouteVector(
                "fresh-symbol-identity-show",
                optionBeforeTarget,
                "dnaxi",
                "0.5.0",
                "/tmp/feed",
                "DNAXI_LOCAL_FEED",
                "/tmp/raw-tools/dnx");

        Assert.False(optionBeforeTargetActivation.Exact);
    }

    [Fact]
    public void Symbol_identity_handoff_requires_a_token_exact_id()
    {
        const string identity = "symbol/v2/name/declaration/workspace";

        Assert.True(CodexDiscoveryEvidenceValidator.ContainsExactSymbolId(
            $"matches[1]:\n  - id: {identity}\n",
            identity));
        Assert.False(CodexDiscoveryEvidenceValidator.ContainsExactSymbolId(
            $"matches[1]:\n  - id: {identity}-extra\n",
            identity));
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
    public async Task Empty_matched_success_cohort_has_unavailable_efficiency()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var runs = context.Preparation.Schedule
            .Select(scheduled => CreateFailedRun(context, scheduled))
            .ToArray();
        var report = new CodexDiscoverySeriesReport(
            CodexDiscoveryEvidenceStore.ReportSchema,
            context.Preparation.RequestHash,
            context.Preparation.Manifest,
            context.Preparation.Schedule.Count,
            Complete: true,
            Failure: null,
            runs);

        var summary = CodexDiscoveryEvidenceValidator.CreateSummary(
            context,
            report,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Null(summary.Thresholds.MedianTokenChangePercent);
        Assert.Null(summary.Thresholds.MedianDurationChangePercent);
        Assert.False(summary.Thresholds.TokenRegression);
        Assert.False(summary.Thresholds.ImprovementClaimSupported);
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
                command = "/bin/zsh -lc \"cat src/Discovery/Cases/InvocationCases.cs\"",
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
    public async Task Raw_reconciliation_requires_a_denial_report_for_a_successful_out_of_bound_command()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var scheduled = context.Preparation.Schedule[0];
        var run = CreateSuccessfulRun(context, scheduled);
        var sentinel = Path.Combine(fixture.Root, "sealed-read.sentinel");
        await File.WriteAllTextAsync(sentinel, "sealed");
        var command = $"cat \"{sentinel}\" && true";
        var commandPayload = JsonSerializer.Serialize(new
        {
            type = "item.completed",
            item = new
            {
                id = "read-outside",
                type = "command_execution",
                command,
                aggregated_output = string.Empty,
                exit_code = 0,
                status = "completed",
            },
        });
        var workspacePath = Path.GetDirectoryName(
            context.Request.Corpus.Artifact.Path)!;
        var attempt = Assert.Single(
            CodexBenchmarkCommandEvidence.FindOutOfBoundReadAttempts(
                command,
                workspacePath,
                CodexAgentBenchmarkAdapter.GetAgentReadableRoots(
                    workspacePath)));
        var denialPayload = JsonSerializer.Serialize(new
        {
            commandHash = AgentBenchmarkHash.Compute(command),
            attemptedPath = attempt.Operand,
            resolvedPath = attempt.ResolvedPath,
        });
        var rawEvents = run.RawEvents.Take(3)
            .Concat([
                Raw(3, "item.completed", commandPayload),
                Raw(4, "adapter.filesystem.read.denied", denialPayload),
            ])
            .Concat(run.RawEvents.Skip(3).Select((raw, index) =>
                Raw(index + 5, raw.Kind, raw.Payload)))
            .ToArray();
        var reconciled = WithPermissionDeniedEvidence(
            run,
            [ToolCall(0, "repository-read", commandPayload)],
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

        var missingReport = report with
        {
            Runs = [WithPermissionDeniedEvidence(
                run,
                [ToolCall(0, "repository-read", commandPayload)],
                rawEvents.Where(static value => value.Kind
                        != "adapter.filesystem.read.denied")
                    .Select((value, index) => Raw(
                        index,
                        value.Kind,
                        value.Payload))
                    .ToArray())],
        };
        Assert.Throws<AgentBenchmarkException>(() =>
            CodexDiscoveryEvidenceValidator.ValidateReport(
                context,
                missingReport));
    }

    [Fact]
    public async Task Raw_reconciliation_requires_an_unreconciled_report_for_an_ambiguous_reader()
    {
        using var fixture = await PreparedFixture.CreateAsync();
        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            fixture.RequestPath);
        var scheduled = context.Preparation.Schedule[0];
        var run = CreateSuccessfulRun(context, scheduled);
        const string command =
            "python3 -c 'open(\"/outside/request.sentinel\").read()'";
        var commandPayload = JsonSerializer.Serialize(new
        {
            type = "item.completed",
            item = new
            {
                id = "ambiguous-reader",
                type = "command_execution",
                command,
                aggregated_output = string.Empty,
                exit_code = 0,
                status = "completed",
            },
        });
        var unreconciledPayload = JsonSerializer.Serialize(new
        {
            commandHash = AgentBenchmarkHash.Compute(command),
        });
        var rawEvents = run.RawEvents.Take(3)
            .Concat([
                Raw(3, "item.completed", commandPayload),
                Raw(
                    4,
                    "adapter.filesystem.read.unreconciled",
                    unreconciledPayload),
            ])
            .Concat(run.RawEvents.Skip(3).Select((raw, index) =>
                Raw(index + 5, raw.Kind, raw.Payload)))
            .ToArray();
        var reconciled = WithPermissionDeniedEvidence(
            run,
            [ToolCall(0, "repository-read", commandPayload)],
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

        var missingReport = report with
        {
            Runs = [WithPermissionDeniedEvidence(
                run,
                [ToolCall(0, "repository-read", commandPayload)],
                rawEvents.Where(static value => value.Kind
                        != "adapter.filesystem.read.unreconciled")
                    .Select((value, index) => Raw(
                        index,
                        value.Kind,
                        value.Payload))
                    .ToArray())],
        };
        Assert.Throws<AgentBenchmarkException>(() =>
            CodexDiscoveryEvidenceValidator.ValidateReport(
                context,
                missingReport));
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
        var workspacePath = Path.GetDirectoryName(
            context.Request.Corpus.Artifact.Path)!;
        var files = new SortedSet<string>(StringComparer.Ordinal);
        var projects = new SortedSet<string>(StringComparer.Ordinal);
        var rawEvents = new List<AgentBenchmarkRawEvent>
        {
            Raw(0, "adapter.process.started",
                JsonSerializer.Serialize(new
                {
                    processId,
                    workspacePath,
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
            string[] candidateRoutes = task.Id switch
            {
                "test-symbol-explicit-scope" =>
                [
                    "search symbol SymbolContext.Tests.ScopeProbe --solution Workspace.slnx --include-tests",
                ],
                "symbol-owner-framework-variants" =>
                [
                    "search symbol SymbolContext.Product.LedgerService --project src/Core/Core.csproj",
                ],
                "fresh-symbol-identity-show" =>
                [
                    "search symbol Format --project src/Core/Core.csproj",
                    "show symbol symbol/v2/test-format --project src/Core/Core.csproj",
                ],
                "stale-symbol-correction" =>
                [
                    $"show symbol {CodexBenchmarkCommandEvidence.ExpectedStaleSymbolId} --project src/Core/Core.csproj",
                ],
                "ambiguous-symbol-correction" =>
                [
                    $"show symbol {CodexBenchmarkCommandEvidence.ExpectedAmbiguousSymbolId} --project src/Core/Core.csproj",
                ],
                "syntax-candidate-partial-verification" =>
                [
                    "search syntax invocation --name MissingAudit --path loose/UnownedCandidate.cs --verify --full",
                ],
                "bounded-symbol-show" =>
                [
                    "search symbol SymbolContext.Product.LedgerService --project src/Core/Core.csproj",
                    "show symbol symbol/v2/test-ledger --project src/Core/Core.csproj --max-chars 24",
                ],
                "document-exact-line-span" =>
                [
                    "show document docs/Runbook.txt --start-line 5 --end-line 6 --full",
                ],
                "symbol-outline" =>
                [
                    "search symbol SymbolContext.Product.LedgerService --project src/Core/Core.csproj",
                    "outline symbol/v2/test-ledger --project src/Core/Core.csproj --full",
                ],
                "context-whole-section-truncation" =>
                [
                    "search symbol SymbolContext.Product.LedgerService --project src/Core/Core.csproj",
                    "context symbol symbol/v2/test-ledger --project src/Core/Core.csproj --include declaration,owner,document,outline --max-chars 0",
                ],
                var taskId => throw new InvalidOperationException(
                    $"Unexpected test task '{taskId}'."),
            };
            foreach (var candidateRoute in candidateRoutes)
            {
                var command = scheduled.Condition
                    is AgentBenchmarkCondition.Candidate
                        ? $"dnx {context.Request.Product.PackageId}@{context.Request.Product.PackageVersion} --source \"${CodexDiscoveryBenchmarkPreparation.PackageSourceEnvironmentVariable}\" --verbosity quiet -- {candidateRoute}"
                        : "rg benchmark-marker";
                var expectedErrorCode = task.Id switch
                {
                    "stale-symbol-correction" => "evidence.stale_id",
                    "ambiguous-symbol-correction" => "evidence.ambiguous_id",
                    _ => null,
                };
                var commandSucceeded = expectedErrorCode is null;
                var identity = task.Id switch
                {
                    "fresh-symbol-identity-show" => "symbol/v2/test-format",
                    "symbol-owner-framework-variants"
                        or "bounded-symbol-show" or "symbol-outline"
                        or "context-whole-section-truncation" =>
                        "symbol/v2/test-ledger",
                    _ => null,
                };
                var outputCommand = candidateRoute switch
                {
                    _ when candidateRoute.StartsWith(
                        "search syntax invocation",
                        StringComparison.Ordinal) =>
                        "search syntax invocation",
                    _ when candidateRoute.StartsWith(
                        "search symbol",
                        StringComparison.Ordinal) => "search symbol",
                    _ when candidateRoute.StartsWith(
                        "show symbol",
                        StringComparison.Ordinal) => "show symbol",
                    _ when candidateRoute.StartsWith(
                        "show document",
                        StringComparison.Ordinal) => "show document",
                    _ when candidateRoute.StartsWith(
                        "context symbol",
                        StringComparison.Ordinal) => "context symbol",
                    _ when candidateRoute.StartsWith(
                        "outline",
                        StringComparison.Ordinal) => "outline",
                    _ => throw new InvalidOperationException(
                        $"Unexpected fixture route '{candidateRoute}'."),
                };
                var outputBody = task.Id switch
                {
                    "test-symbol-explicit-scope" =>
                        "scope:\n  solution: Workspace.slnx\n  eligibility:\n    include_tests: true\n    include_generated: false\n  considered: 6\n",
                    "syntax-candidate-partial-verification" =>
                        "scope:\n  paths[1]: loose/UnownedCandidate.cs\n  eligibility:\n    include_tests: false\n    include_generated: false\n  considered: 1\n",
                    "document-exact-line-span" =>
                        "scope:\n  analyzed_portion: one explicitly selected workspace document\n  considered: 1\npath: docs/Runbook.txt\ngenerated: false\n",
                    "stale-symbol-correction" =>
                        $"scope:\n  projects[1]: src/Core/Core.csproj\n  eligibility:\n    include_tests: false\n    include_generated: false\n  considered: 4\nquery: {JsonSerializer.Serialize("dnaxi search symbol 'Reconcile' --project 'src/Core/Core.csproj' --fields 'id,signature,owning_projects,variant_count,variants' --full")}\ncandidate_count: 1\ncandidates[1]{{id,kind,name,signature,file,line}}:\n  {StaleCandidateId},method,Reconcile,Reconcile(string),src/Core/StaleService.cs,5\nerror:\n  code: {expectedErrorCode}\n  message: The symbol ID is stale.\n  correction: {JsonSerializer.Serialize("dnaxi search symbol 'Reconcile' --project 'src/Core/Core.csproj' --fields 'id,signature,owning_projects,variant_count,variants' --full")}\n",
                    "ambiguous-symbol-correction" =>
                        $"scope:\n  projects[1]: src/Core/Core.csproj\n  eligibility:\n    include_tests: false\n    include_generated: false\n  considered: 4\nquery: {JsonSerializer.Serialize("dnaxi search symbol 'RelocatedWidget' --project 'src/Core/Core.csproj' --fields 'id,signature,owning_projects,variant_count,variants' --full")}\ncandidate_count: 2\ncandidates[2]{{id,kind,name,signature,file,line}}:\n  {AmbiguousCandidate1Id},class,RelocatedWidget,RelocatedWidget,src/Core/moved/RelocatedWidget1.cs,3\n  {AmbiguousCandidate2Id},class,RelocatedWidget,RelocatedWidget,src/Core/moved/RelocatedWidget2.cs,3\nerror:\n  code: {expectedErrorCode}\n  message: The symbol ID is ambiguous.\n  correction: {JsonSerializer.Serialize("dnaxi search symbol 'RelocatedWidget' --project 'src/Core/Core.csproj' --fields 'id,signature,owning_projects,variant_count,variants' --full")}\n",
                    _ when candidateRoute.StartsWith(
                        "search symbol",
                        StringComparison.Ordinal) =>
                        $"scope:\n  projects[1]: src/Core/Core.csproj\n  eligibility:\n    include_tests: false\n    include_generated: false\n  considered: 4\nmatches[1]:\n  - id: {identity}\n    file: src/Core/LedgerService.cs\n    owning_projects[1]: src/Core/Core.csproj\n    variants[2]{{configuration,framework,meaning,project}}:\n      null,net10.0,unresolved,src/Core/Core.csproj\n      null,net8.0,unresolved,src/Core/Core.csproj\n",
                    _ when outputCommand == "show symbol" =>
                        "scope:\n  projects[1]: src/Core/Core.csproj\n  eligibility:\n    include_tests: false\n    include_generated: false\n  considered: 4\nlocation:\n  file: src/Core/LedgerService.cs\n  line: 4\n",
                    _ when outputCommand == "outline" =>
                        "scope:\n  projects[1]: src/Core/Core.csproj\n  eligibility:\n    include_tests: false\n    include_generated: false\n  considered: 4\npath: src/Core/LedgerService.cs\ngenerated: false\n",
                    _ when outputCommand == "context symbol" =>
                        "scope:\n  projects[1]: src/Core/Core.csproj\n  eligibility:\n    include_tests: false\n    include_generated: false\n  considered: 4\ntarget:\n  id: symbol/v2/test-ledger\n  document_ref: file/v1/test-ledger\n  location:\n    file: src/Core/LedgerService.cs\n    line: 4\n",
                    _ => throw new InvalidOperationException(
                        $"Unexpected fixture output command '{outputCommand}'."),
                };
                var output =
                    $"schema: dotnet-axi/v1\ncommand: {outputCommand}\nstatus: {(task.Id == "syntax-candidate-partial-verification" ? "partial" : expectedErrorCode is null ? "success" : "failed")}\n"
                    + outputBody;
                var toolPayload = JsonSerializer.Serialize(new
                {
                    type = "item.completed",
                    item = new
                    {
                        id = $"tool-{toolCalls.Count}",
                        type = "command_execution",
                        command,
                        aggregated_output = output,
                        exit_code = commandSucceeded ? 0 : 1,
                        status = "completed",
                    },
                });
                rawEvents.Add(Raw(
                    rawEvents.Count,
                    "item.completed",
                    toolPayload));
                using var toolDocument = JsonDocument.Parse(toolPayload);
                toolCalls.Add(new AgentBenchmarkToolCall(
                    toolCalls.Count,
                    "source-search",
                    command,
                    AgentBenchmarkHash.Compute(toolDocument.RootElement
                        .GetProperty("item").GetRawText()),
                    commandSucceeded));
                Assert.True(CodexBenchmarkCommandEvidence.ObserveCommandScope(
                    command,
                    workspacePath,
                    files,
                    projects));
                Assert.True(CodexBenchmarkCommandEvidence.ObserveOutputScope(
                    output,
                    workspacePath,
                    files,
                    projects));
            }
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
            new AgentBenchmarkInspectedScope(
                files.ToArray(),
                projects.ToArray()),
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

    private static AgentBenchmarkRunResult CreateRouteFixtureRun(
        CodexDiscoveryPreparedContext context,
        AgentBenchmarkScheduledRun scheduled,
        string fixture,
        int? exitCode,
        string? itemStatus)
    {
        var run = CreateSuccessfulRun(context, scheduled);
        var route = FixtureRoute(scheduled.TaskId, fixture);
        var command =
            $"dnx {context.Request.Product.PackageId}@{context.Request.Product.PackageVersion} --source \"${CodexDiscoveryBenchmarkPreparation.PackageSourceEnvironmentVariable}\" --verbosity quiet -- {route}";
        var output = File.ReadAllText(RouteFixturePath(fixture));
        var unrelatedPayload = JsonSerializer.Serialize(new
        {
            type = "item.completed",
            item = new
            {
                id = "route-prefix",
                type = "command_execution",
                command = "rg benchmark-marker",
                aggregated_output = string.Empty,
                exit_code = 1,
                status = "completed",
            },
        });
        var itemNode = new JsonObject
        {
            ["id"] = "route-fixture",
            ["type"] = "command_execution",
            ["command"] = command,
            ["aggregated_output"] = output,
        };
        if (exitCode is not null)
        {
            itemNode["exit_code"] = exitCode.Value;
        }

        if (itemStatus is not null)
        {
            itemNode["status"] = itemStatus;
        }

        var payload = new JsonObject
        {
            ["type"] = "item.completed",
            ["item"] = itemNode,
        }.ToJsonString();
        var rawEvents = run.RawEvents.Take(3)
            .Append(Raw(3, "item.completed", unrelatedPayload))
            .Append(Raw(4, "item.completed", payload))
            .Concat(run.RawEvents.Skip(3).Select((raw, index) =>
                Raw(index + 5, raw.Kind, raw.Payload)))
            .ToArray();
        using var unrelatedDocument = JsonDocument.Parse(unrelatedPayload);
        var unrelatedItem = unrelatedDocument.RootElement.GetProperty("item");
        var unrelatedToolCall = new AgentBenchmarkToolCall(
            0,
            "source-search",
            "rg benchmark-marker",
            AgentBenchmarkHash.Compute(unrelatedItem.GetRawText()),
            false);
        using var document = JsonDocument.Parse(payload);
        var item = document.RootElement.GetProperty("item");
        var commandSucceeded = exitCode == 0
                               && itemStatus is null or "completed";
        var toolCall = new AgentBenchmarkToolCall(
            1,
            "source-search",
            command,
            AgentBenchmarkHash.Compute(item.GetRawText()),
            commandSucceeded);
        var workspacePath = Path.GetDirectoryName(
            context.Request.Corpus.Artifact.Path)!;
        var files = new SortedSet<string>(StringComparer.Ordinal);
        var projects = new SortedSet<string>(StringComparer.Ordinal);
        if (!CodexBenchmarkCommandEvidence.ObserveCommandScope(
                command,
                workspacePath,
                files,
                projects)
            || !CodexBenchmarkCommandEvidence.ObserveOutputScope(
                output,
                workspacePath,
                files,
                projects))
        {
            throw new InvalidOperationException(
                $"Route fixture '{fixture}' contains invalid inspected scope.");
        }

        return WithObservedEvidence(
            run,
            [unrelatedToolCall, toolCall],
            new AgentBenchmarkInspectedScope(
                files.ToArray(),
                projects.ToArray()),
            rawEvents);
    }

    private static string FixtureRoute(string taskId, string fixture) =>
        taskId switch
        {
            "syntax-candidate-partial-verification" =>
                "search syntax invocation --name MissingAudit --path loose/UnownedCandidate.cs --verify --full",
            "stale-symbol-correction" =>
                "show symbol "
                + (fixture == "wrong-identity.toon"
                    ? "symbol/v2/wrong-identity"
                    : CodexBenchmarkCommandEvidence.ExpectedStaleSymbolId)
                + " --project src/Core/Core.csproj",
            "ambiguous-symbol-correction" =>
                $"show symbol {CodexBenchmarkCommandEvidence.ExpectedAmbiguousSymbolId} --project src/Core/Core.csproj",
            _ => throw new InvalidOperationException(
                $"Unexpected fixture task '{taskId}'."),
        };

    private static string RouteFixturePath(string fixture) => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "CodexDiscovery",
        "RouteReconciliation",
        fixture);

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

    private static AgentBenchmarkRunResult WithPermissionDeniedEvidence(
        AgentBenchmarkRunResult run,
        IReadOnlyList<AgentBenchmarkToolCall> toolCalls,
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
            "permission-denied",
            run.Answer,
            false,
            false,
            run.InputTokens,
            run.OutputTokens,
            run.Turns,
            toolCalls,
            run.Duration,
            new AgentBenchmarkInspectedScope([], []),
            [
                new("claims-supported", false, "reconciled"),
                new("network-unused", true, "reconciled"),
                new("workspace-unchanged", true, "reconciled"),
            ],
            [
                new("fixture-content-hash", true, true, "reconciled"),
                new("safety-oracle", true, false, "reconciled"),
                new("success-oracle", true, false, "reconciled"),
            ],
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
            string? dnxProbeOutputMode = null,
            string candidateVersion = "0.5.0",
            bool includeBoundedReader = true,
            bool boundedReaderSucceeds = true,
            string harnessVersion = "2.11.0",
            bool includeRawDotnet = true,
            bool includeRawSourceSearch = true,
            bool rawDotnetSucceeds = true,
            bool rawDotnetSupportsCompileItems = true,
            bool rawSourceSearchSucceeds = true,
            bool rawSourceSearchSupportsCodexArguments = true,
            bool rawSourceSearchUsesWindowsSeparators = false,
            bool canonicalPriorSummary = true,
            bool useHistoricalCorpusNormalizer = false,
            bool omitSemanticDotnetToolClass = false,
            bool isolationProbeLeaks = false)
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
            if (isolationProbeLeaks)
            {
                File.WriteAllText(
                    Path.Combine(root, "codex.isolation-leak"),
                    "enabled");
            }
            var dnxExecutable = InstallDnxProbe(
                rawToolsPath,
                dnxProbeSucceeds,
                dnxProbeOutputMode);
            if (includeBoundedReader)
            {
                var reader = InstallProcessProbe(rawToolsPath, "sed");
                if (!boundedReaderSucceeds)
                {
                    File.WriteAllText(reader, "not an executable reader");
                }
            }
            if (includeRawDotnet)
            {
                InstallProcessProbe(rawToolsPath, "dotnet");
                File.WriteAllText(
                    Path.Combine(rawToolsPath, "dotnet.raw-probe-enabled"),
                    "enabled");
                if (rawDotnetSupportsCompileItems)
                {
                    File.WriteAllText(
                        Path.Combine(
                            rawToolsPath,
                            "dotnet.compile-items-enabled"),
                        "enabled");
                }
                if (!rawDotnetSucceeds)
                {
                    File.WriteAllText(
                        Path.Combine(
                            rawToolsPath,
                            "dotnet.raw-probe-exit-code"),
                        "73");
                }
            }
            if (includeRawSourceSearch)
            {
                InstallProcessProbe(rawToolsPath, "rg");
                File.WriteAllText(
                    Path.Combine(rawToolsPath, "rg.raw-probe-enabled"),
                    "enabled");
                if (rawSourceSearchSupportsCodexArguments)
                {
                    File.WriteAllText(
                        Path.Combine(
                            rawToolsPath,
                            "rg.codex-arguments-enabled"),
                        "enabled");
                }
                if (rawSourceSearchUsesWindowsSeparators)
                {
                    File.WriteAllText(
                        Path.Combine(
                            rawToolsPath,
                            "rg.windows-separators-enabled"),
                        "enabled");
                }
                if (!rawSourceSearchSucceeds)
                {
                    File.WriteAllText(
                        Path.Combine(rawToolsPath, "rg.raw-probe-exit-code"),
                        "73");
                }
            }
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
                  + "When a controlled benchmark supplies the local feed, route applicable symbol and context discovery through "
                  + CodexDiscoveryBenchmarkPreparation.ExactCandidateInvocation
                  + "."
                : "Use dotnet-axi for deterministic .NET repository evidence.";
            var candidateInstructions = Write(
                Path.Combine(skillPath, "SKILL.md"),
                $"---\nname: dotnet-axi\ndescription: {candidateDescription}\n---\n\nUse dnx dnaxi@{candidateVersion} --source \"$DNAXI_LOCAL_FEED\" --verbosity quiet -- <command> for symbol and bounded-context discovery.\n");
            var packageSource = Directory.CreateDirectory(
                Path.Combine(root, "package-source")).FullName;
            var package = WritePackage(
                Path.Combine(packageSource, "dnaxi.0.5.0.nupkg"),
                packageCarriesSkill ? candidateInstructions : null,
                invalidPackageContents);
            var baselineInstructions = Write(
                Path.Combine(root, "baseline-instructions.txt"),
                "no product instructions");
            var priorRequestHash =
                CodexDiscoveryBenchmarkPreparation.PriorRequestHash;
            var priorReportHash =
                CodexDiscoveryBenchmarkPreparation.PriorReportHash;
            object priorSummaryValue = canonicalPriorSummary
                ? new
                {
                    schema = CodexDiscoveryBenchmarkPreparation
                        .PriorSummarySchema,
                    requestHash = priorRequestHash,
                    reportHash = priorReportHash,
                    evidenceStatus = "complete",
                    comparison = "no-improvement",
                    expectedRunCount = 70,
                    retainedRunCount = 70,
                    baseline = PriorMetrics(
                        AgentBenchmarkCondition.Baseline,
                        activatedRuns: 0,
                        invocations: 0),
                    candidate = PriorMetrics(
                        AgentBenchmarkCondition.Candidate,
                        activatedRuns: 34,
                        invocations: 35),
                    thresholds = new CodexDiscoveryThresholdEvaluation(
                        0,
                        0m,
                        7.5511508951406649616368286400m,
                        null,
                        0m,
                        false,
                        false,
                        false,
                        false),
                    routeActivations = new[]
                    {
                        "file-handler-paths",
                        "literal-archive-status",
                        "regex-handler-methods",
                        "syntax-attributed-classes",
                        "syntax-catch-timeout",
                        "syntax-invocation-record",
                        "syntax-object-creation-archive-client",
                    }.Select(static taskId => new
                    {
                        taskId,
                        candidateRunCount = 5,
                        activatedRunCount = taskId == "regex-handler-methods"
                            ? 4
                            : 5,
                        successfulActivatedRunCount =
                            taskId == "regex-handler-methods" ? 4 : 5,
                    }).ToArray(),
                    priorSeries = new CodexDiscoveryHistoricalComparison(
                        "/retained/0.3.0/summary.json",
                        "30fb6de32eadbdb0fb3ff51cae5a268e26fb7f8a281697a4cc1b9eb74950a986",
                        "dotnet-axi/codex-discovery-summary/v1",
                        "2e0d5ebcb3549c7a5c5a451fe5106f4f6e156a6627011824327509b05c32f893",
                        "417649ab59ccb352cf1389705f2b51f2ab406b3886ea86033efb24779851b77f",
                        "failed",
                        "incomparable",
                        false,
                        "Retained historical result."),
                    reasons = new[]
                    {
                        "Complete comparable evidence does not satisfy either a documented regression threshold or the improvement threshold.",
                    },
                }
                : new
                {
                    schema = CodexDiscoveryBenchmarkPreparation
                        .PriorSummarySchema,
                    requestHash = priorRequestHash,
                    reportHash = priorReportHash,
                    evidenceStatus = "complete",
                    comparison = "no-improvement",
                    expectedRunCount = 70,
                    retainedRunCount = 70,
                };
            var priorSummary = await WriteJsonAsync(
                Path.Combine(root, "prior-summary.json"),
                priorSummaryValue);
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
            var corpusSource = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "AgentTasks",
                "symbol-context",
                "corpus.json");
            var corpus = corpusSource;
            if (useHistoricalCorpusNormalizer
                || omitSemanticDotnetToolClass)
            {
                var corpusDirectory = Path.Combine(root, "symbol-context");
                CopyDirectory(
                    Path.GetDirectoryName(corpusSource)!,
                    corpusDirectory);
                corpus = Path.Combine(corpusDirectory, "corpus.json");
                var document = JsonNode.Parse(
                    await File.ReadAllTextAsync(corpus))!.AsObject();
                var tasks = document["tasks"]!.AsArray();
                if (useHistoricalCorpusNormalizer)
                {
                    foreach (var taskNode in tasks)
                    {
                        taskNode!.AsObject()["successOracle"]!
                            .AsObject()["normalizer"] =
                            "ordinal-lines/v1";
                    }
                }

                if (omitSemanticDotnetToolClass)
                {
                    var semanticTask = tasks
                        .Select(static task => task!.AsObject())
                        .Single(static task => string.Equals(
                            task["id"]!.GetValue<string>(),
                            "syntax-candidate-partial-verification",
                            StringComparison.Ordinal));
                    var permittedTools = semanticTask["execution"]!
                        .AsObject()["permittedTools"]!.AsArray();
                    for (var index = 0; index < permittedTools.Count; index++)
                    {
                        if (!string.Equals(
                                permittedTools[index]!.GetValue<string>(),
                                "dotnet-sdk",
                                StringComparison.Ordinal))
                        {
                            continue;
                        }

                        permittedTools.RemoveAt(index);
                        break;
                    }
                }

                await File.WriteAllTextAsync(
                    corpus,
                    document.ToJsonString(
                        CodexDiscoveryBenchmarkPreparation.JsonOptions));
            }
            var request = new CodexDiscoveryBenchmarkRequest(
                CodexDiscoveryBenchmarkPreparation.RequestSchema,
                "codex-050-symbol-context-test",
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
                        "context.symbol",
                        "outline.syntax",
                        "search.symbol.declaration",
                        "search.syntax.verify",
                        "show.document",
                        "show.symbol.identity",
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
                harnessVersion,
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

        private static CodexDiscoveryConditionMetrics PriorMetrics(
            AgentBenchmarkCondition condition,
            int activatedRuns,
            int invocations) =>
            new(
                condition,
                35,
                35,
                35,
                35,
                0,
                activatedRuns,
                activatedRuns,
                invocations,
                invocations,
                100m,
                condition is AgentBenchmarkCondition.Baseline
                    ? 46_920m
                    : 50_463m,
                3m,
                1m,
                condition is AgentBenchmarkCondition.Baseline
                    ? 21_101.7472m
                    : 21_530.5514m);

        private static string Write(string path, string value)
        {
            File.WriteAllText(path, value);
            return path;
        }

        private static void CopyDirectory(
            string sourceDirectory,
            string destinationDirectory)
        {
            foreach (var sourceFile in Directory.EnumerateFiles(
                         sourceDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                var destinationFile = Path.Combine(
                    destinationDirectory,
                    Path.GetRelativePath(sourceDirectory, sourceFile));
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destinationFile)!);
                File.Copy(sourceFile, destinationFile);
            }
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
                    <version>0.5.0</version>
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
            bool succeeds,
            string? outputMode)
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

            if (!string.IsNullOrWhiteSpace(outputMode))
            {
                File.WriteAllText(
                    Path.Combine(
                        destinationDirectory,
                        "dnx.output-mode"),
                    outputMode);
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
                        $"{applicationName}{extension}"),
                    overwrite: true);
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
