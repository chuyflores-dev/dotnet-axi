using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Text.Json;

namespace DotNetAxi.Testing.Tests;

public sealed class AgentBenchmarkRunnerTests
{
    [Fact]
    public async Task Exact_fact_sets_ignore_response_order_and_duplicates()
    {
        var source = await SingleTaskCorpusAsync();
        var expectedFacts = new[]
        {
            "src/Discovery/Case10.cs:10",
            "src/Discovery/Case2.cs:2",
        };
        var task = source.Tasks[0] with
        {
            SuccessOracle = source.Tasks[0].SuccessOracle with
            {
                ExpectedFacts = expectedFacts,
            },
        };
        var corpus = source with { Tasks = [task] };
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                await using var execution = await fake.StartAsync(
                    input,
                    cancellationToken);
                var result = await execution.Completion;
                return new CompletedExecution(
                    result with
                    {
                        Answer = string.Join(
                            '\n',
                            expectedFacts[1],
                            expectedFacts[0],
                            expectedFacts[1]),
                    },
                    result.RawEvents);
            });

        var series = await RunAsync(corpus, adapter, Configuration(7));

        Assert.All(series.Runs, static run => Assert.True(run.Success));
    }

    [Fact]
    public async Task Full_run_matrix_is_randomized_and_replayable()
    {
        var corpus = await SingleTaskCorpusAsync();
        var first = await RunFakeAsync(corpus, seed: 20260805);
        var replay = await RunFakeAsync(corpus, seed: 20260805);
        var different = await RunFakeAsync(corpus, seed: 20260806);

        var firstOrder = Order(first);
        Assert.Equal(firstOrder, Order(replay));
        Assert.NotEqual(firstOrder, Order(different));
        Assert.Equal(
            DeterministicEvidence(first),
            DeterministicEvidence(replay));
        Assert.Equal(10, first.Runs.Count);
        Assert.Equal(
            5,
            first.Runs.Count(static run =>
                run.Condition == AgentBenchmarkCondition.Baseline));
        Assert.Equal(
            5,
            first.Runs.Count(static run =>
                run.Condition == AgentBenchmarkCondition.Candidate));
        Assert.Contains(
            Enumerable.Range(1, 5),
            repetition =>
            {
                var positions = first.Runs
                    .Where(run => run.Repetition == repetition)
                    .Select(static run => run.ExecutionOrder)
                    .Order()
                    .ToArray();
                return positions[1] - positions[0] > 1;
            });
    }

    [Fact]
    public async Task Runner_enforces_parity_and_captures_metrics_and_evidence()
    {
        var corpus = await SingleTaskCorpusAsync();
        var inputs = new List<AgentBenchmarkAdapterInput>();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                inputs.Add(input);
                return await fake.StartAsync(input, cancellationToken);
            });
        var configuration = Configuration(41);
        var series = await RunAsync(corpus, adapter, configuration);

        Assert.Equal(10, inputs.Count);
        Assert.Equal(
            AgentBenchmarkDispatch.Manual,
            series.Manifest.Dispatch);
        Assert.Equal(configuration.Baseline, series.Manifest.Baseline);
        Assert.Equal(configuration.Candidate, series.Manifest.Candidate);
        Assert.Equal("read-only", series.Manifest.Execution.Sandbox);
        Assert.NotSame(configuration.Baseline, series.Manifest.Baseline);
        Assert.NotSame(configuration.Candidate, series.Manifest.Candidate);
        Assert.All(
            inputs,
            input =>
            {
                Assert.Equal("fake-agent-1.0.0", input.Execution.AgentVersion);
                Assert.Equal("fake-model", input.Execution.ModelId);
                Assert.Equal("controlled", input.Execution.ReasoningSetting);
                Assert.Equal("read-only", input.Execution.Sandbox);
                Assert.Equal("never", input.Execution.PermissionProfile);
                Assert.Equal("disabled", input.Execution.NetworkPolicy);
                Assert.Equal(corpus.Tasks[0].Prompt, input.Task.Prompt);
            });
        Assert.Equal(
            2,
            inputs.Select(static input => input.InstructionsHash)
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.All(
            series.Runs,
            run =>
            {
                Assert.True(run.Success);
                Assert.True(run.Safe);
                Assert.False(run.TimedOut);
                Assert.Equal(
                    string.Join("\n", corpus.Tasks[0].SuccessOracle.ExpectedFacts),
                    run.Answer);
                Assert.True(run.InputTokens > 0);
                Assert.True(run.OutputTokens > 0);
                Assert.Equal(
                    run.InputTokens + run.OutputTokens,
                    run.TotalTokens);
                Assert.Equal(2, run.Turns);
                Assert.Single(run.ToolCalls);
                Assert.NotEmpty(run.InspectedScope.Files);
                Assert.All(run.Validations, static validation =>
                {
                    Assert.True(validation.Executed);
                    Assert.True(validation.Passed);
                });
                Assert.Equal("1.0.0", run.Versions.HarnessVersion);
                Assert.Equal("fake-model", run.Versions.ModelId);
                Assert.Equal("read-only", run.Sandbox);
                Assert.Equal("never", run.PermissionProfile);
                Assert.Equal(corpus.Version, run.Versions.CorpusVersion);
                Assert.Equal(corpus.Tasks[0].Repository.ContentHash,
                    run.Hashes.FixtureContent);
                Assert.Equal(
                    run.Hashes.WorkspaceBefore,
                    run.Hashes.WorkspaceAfter);
                AssertHash(run.Hashes.RawTrajectory);
                Assert.Equal(2, run.RawEvents.Count);
                Assert.Equal(
                    new[]
                    {
                        "claims-supported",
                        "network-unused",
                        "workspace-unchanged",
                    },
                    run.SafetyChecks.Select(static check => check.Id));
                Assert.All(run.SafetyChecks, static check =>
                    Assert.True(check.Passed));
                Assert.Throws<NotSupportedException>(
                    () => ((IList<AgentBenchmarkRawEvent>)run.RawEvents)
                        .RemoveAt(0));
            });
    }

    [Fact]
    public async Task Only_retryable_pre_start_failures_are_retried()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var startCalls = 0;
        var liveRunIds = new HashSet<string>(StringComparer.Ordinal);
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                startCalls++;
                if (startCalls <= 2)
                {
                    throw new AgentBenchmarkStartException(
                        "Fake capacity is temporarily unavailable.",
                        retryable: true);
                }

                Assert.True(liveRunIds.Add(input.RunId));
                return await fake.StartAsync(input, cancellationToken);
            });

        var series = await RunAsync(
            corpus,
            adapter,
            Configuration(72) with { MaximumStartAttempts = 3 });

        Assert.Equal(12, startCalls);
        Assert.Equal(10, liveRunIds.Count);
        Assert.Equal(3, series.Runs.Single(run => run.StartAttempts == 3)
            .StartAttempts);
        Assert.Equal(9, series.Runs.Count(run => run.StartAttempts == 1));
    }

    [Fact]
    public async Task Timeout_stops_one_live_run_without_retrying_it()
    {
        var corpus = await SingleTaskCorpusAsync(timeoutSeconds: 1);
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        HangingExecution? hanging = null;
        var starts = 0;
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                starts++;
                if (hanging is null)
                {
                    hanging = new HangingExecution(input.RunId);
                    return hanging;
                }

                return await fake.StartAsync(input, cancellationToken);
            });

        var series = await RunAsync(corpus, adapter, Configuration(17));

        var timedOut = Assert.Single(
            series.Runs,
            static run => run.TimedOut);
        Assert.Equal("timed-out", timedOut.Status);
        Assert.False(timedOut.Success);
        Assert.False(timedOut.Safe);
        Assert.Equal(3, timedOut.InputTokens);
        Assert.Equal(1, timedOut.Turns);
        Assert.Equal(2, timedOut.RawEvents.Count);
        var started = Assert.Single(
            timedOut.RawEvents,
            static value => value.Kind == "adapter.process.started");
        var exited = Assert.Single(
            timedOut.RawEvents,
            static value => value.Kind == "adapter.process.exited");
        using var startedPayload = JsonDocument.Parse(started.Payload);
        using var exitedPayload = JsonDocument.Parse(exited.Payload);
        Assert.Equal(
            startedPayload.RootElement.GetProperty("processId").GetInt32(),
            exitedPayload.RootElement.GetProperty("processId").GetInt32());
        Assert.Equal(
            137,
            exitedPayload.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, timedOut.StartAttempts);
        Assert.Equal(10, starts);
        Assert.Equal(1, hanging!.StopCalls);
        Assert.Equal(1, hanging.DisposeCalls);
    }

    [Fact]
    public async Task Cancellation_stops_and_disposes_the_live_execution()
    {
        var corpus = await SingleTaskCorpusAsync();
        HangingExecution? hanging = null;
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new DelegatingAdapter(
            new("deterministic-fake", "1.0.0"),
            (input, _) =>
            {
                hanging = new HangingExecution(input.RunId);
                started.TrySetResult();
                return ValueTask.FromResult<IAgentBenchmarkExecution>(hanging);
            });
        using var cancellation = new CancellationTokenSource();

        var run = RunAsync(
            corpus,
            adapter,
            Configuration(19),
            cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.NotNull(hanging);
        Assert.Equal(1, hanging!.StopCalls);
        Assert.Equal(1, hanging.DisposeCalls);
    }

    [Fact]
    public async Task Malformed_adapter_output_is_rejected_fail_closed()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                await using var execution = await fake.StartAsync(
                    input,
                    cancellationToken);
                var result = await execution.Completion;
                return new CompletedExecution(
                    result with { InputTokens = -1 },
                    result.RawEvents);
            });

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(corpus, adapter, Configuration(23)));

        Assert.Contains(
            "malformed normalized metrics",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unpermitted_tool_is_retained_as_failed_evidence()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                await using var execution = await fake.StartAsync(
                    input,
                    cancellationToken);
                var result = await execution.Completion;
                return new CompletedExecution(
                    result with
                    {
                        ToolCalls =
                        [
                            new AgentBenchmarkToolCall(
                                0,
                                "network",
                                "web_search",
                                Hash("web_search"),
                                true),
                        ],
                    },
                    result.RawEvents);
            });

        var series = await RunAsync(corpus, adapter, Configuration(25));

        Assert.Equal(10, series.Runs.Count);
        Assert.All(series.Runs, run =>
        {
            Assert.Equal("failed", run.Status);
            Assert.False(run.Success);
            Assert.False(run.Safe);
            Assert.NotEmpty(run.RawEvents);
        });
    }

    [Fact]
    public async Task Raw_trajectory_hash_drift_is_rejected_fail_closed()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                await using var execution = await fake.StartAsync(
                    input,
                    cancellationToken);
                var result = await execution.Completion;
                var rawEvents = result.RawEvents.ToArray();
                rawEvents[0] = rawEvents[0] with
                {
                    Payload = "tampered provider event",
                };
                return new CompletedExecution(
                    result with { RawEvents = rawEvents },
                    rawEvents);
            });

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(corpus, adapter, Configuration(27)));

        Assert.Contains(
            "malformed raw trajectory evidence",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Swapped_condition_descriptors_are_rejected_before_start()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            fake.StartAsync);
        var configuration = Configuration(28);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(
                corpus,
                adapter,
                configuration with
                {
                    Baseline = configuration.Baseline with
                    {
                        Condition = AgentBenchmarkCondition.Candidate,
                    },
                }));

        Assert.Contains(
            "settings, provenance, conditions",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, adapter.StartCalls);
    }

    [Fact]
    public async Task Workspace_mutation_fails_runner_owned_safety_validation()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var mutated = false;
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                if (!mutated)
                {
                    mutated = true;
                    await File.WriteAllTextAsync(
                        Path.Combine(input.WorkspacePath, "unexpected.txt"),
                        "unexpected mutation\n",
                        cancellationToken);
                }

                return await fake.StartAsync(input, cancellationToken);
            });

        var series = await RunAsync(corpus, adapter, Configuration(29));

        var unsafeRun = Assert.Single(
            series.Runs,
            static run => !run.Safe);
        Assert.True(unsafeRun.Success);
        Assert.False(unsafeRun.Validations.Single(validation =>
            validation.Id == "fixture-content-hash").Passed);
        Assert.False(unsafeRun.Validations.Single(validation =>
            validation.Id == "safety-oracle").Passed);
    }

    [Fact]
    public async Task Continuous_integration_rejects_a_lying_fake_impostor()
    {
        var corpus = await SingleTaskCorpusAsync();
        var adapter = new DelegatingAdapter(
            new("deterministic-fake", "1.0.0"),
            static (_, _) => throw new InvalidOperationException(
                "A real adapter must not be started in CI."));
        var configuration = Configuration(31) with
        {
            Dispatch = AgentBenchmarkDispatch.ContinuousIntegration,
        };

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(corpus, adapter, configuration));

        Assert.Contains(
            "Continuous integration",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, adapter.StartCalls);
    }

    [Fact]
    public async Task Undefined_dispatch_is_rejected_before_start()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            fake.StartAsync);

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(
                corpus,
                adapter,
                Configuration(37) with
                {
                    Dispatch = (AgentBenchmarkDispatch)99,
                }));

        Assert.Contains(
            "benchmark configuration is invalid",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, adapter.StartCalls);
    }

    [Fact]
    public async Task Malformed_public_corpus_fails_before_fixture_or_adapter()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(fake.Descriptor, fake.StartAsync);
        var invalidTimeout = corpus with
        {
            Tasks =
            [
                corpus.Tasks[0] with
                {
                    Execution = corpus.Tasks[0].Execution with
                    {
                        TimeoutSeconds = 0,
                    },
                },
            ],
        };

        await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(invalidTimeout, adapter, Configuration(38)));
        await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(
                corpus with
                {
                    Tasks = [corpus.Tasks[0], corpus.Tasks[0]],
                },
                adapter,
                Configuration(39)));

        Assert.Equal(0, adapter.StartCalls);
    }

    [Fact]
    public async Task Completed_execution_is_stopped_and_dispose_mutation_is_unsafe()
    {
        var corpus = await SingleTaskCorpusAsync();
        CompletedExecution? firstExecution = null;
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                if (firstExecution is not null)
                {
                    return await fake.StartAsync(input, cancellationToken);
                }

                firstExecution = await CompletedFromFakeAsync(
                    input,
                    cancellationToken,
                    onDispose: async () =>
                    {
                        await File.WriteAllTextAsync(
                            Path.Combine(
                                input.WorkspacePath,
                                "dispose-mutation.txt"),
                            "mutated during dispose\n");
                    });
                return firstExecution;
            });

        var series = await RunAsync(corpus, adapter, Configuration(40));

        Assert.NotNull(firstExecution);
        Assert.Equal(1, firstExecution!.StopCalls);
        Assert.Equal(1, firstExecution.DisposeCalls);
        var unsafeRun = Assert.Single(
            series.Runs,
            static run => !run.Safe);
        Assert.True(unsafeRun.Success);
        Assert.False(Safety(unsafeRun, "workspace-unchanged").Passed);
        Assert.NotEmpty(unsafeRun.RawEvents);
    }

    [Fact]
    public async Task Retryable_start_contamination_never_reaches_a_live_retry()
    {
        var corpus = await SingleTaskCorpusAsync();
        var starts = 0;
        var adapter = new DelegatingAdapter(
            new("retry-contamination", "1.0.0"),
            async (input, cancellationToken) =>
            {
                starts++;
                await File.WriteAllTextAsync(
                    Path.Combine(input.WorkspacePath, "contaminated.txt"),
                    "contaminated\n",
                    cancellationToken);
                throw new AgentBenchmarkStartException(
                    "Retryable only before a live run.",
                    retryable: true);
            });

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(
                corpus,
                adapter,
                Configuration(42) with { MaximumStartAttempts = 3 }));

        Assert.Contains(
            "contaminated",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task Safety_check_outcomes_are_retained_independently()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var starts = 0;
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                var index = starts++;
                if (index > 2)
                {
                    return await fake.StartAsync(input, cancellationToken);
                }

                return await CompletedFromFakeAsync(
                    input,
                    cancellationToken,
                    transform: result => index switch
                    {
                        0 => result with
                        {
                            Answer = result.Answer + "\nunsupported/fact.cs",
                        },
                        1 => result with { NetworkUsed = true },
                        _ => result,
                    },
                    onDispose: index == 2
                        ? async () =>
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(
                                    input.WorkspacePath,
                                    "workspace-change.txt"),
                                "changed\n");
                        }
                : null);
            });

        var series = await RunAsync(corpus, adapter, Configuration(43));

        Assert.Contains(
            series.Runs,
            run => !Safety(run, "claims-supported").Passed
                && Safety(run, "network-unused").Passed
                && Safety(run, "workspace-unchanged").Passed);
        Assert.Contains(
            series.Runs,
            run => Safety(run, "claims-supported").Passed
                && !Safety(run, "network-unused").Passed
                && Safety(run, "workspace-unchanged").Passed);
        Assert.Contains(
            series.Runs,
            run => Safety(run, "claims-supported").Passed
                && Safety(run, "network-unused").Passed
                && !Safety(run, "workspace-unchanged").Passed);
    }

    [Fact]
    public async Task Missing_declared_and_huge_special_additions_preserve_evidence()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var mutated = false;
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                if (mutated)
                {
                    return await fake.StartAsync(input, cancellationToken);
                }

                mutated = true;
                return await CompletedFromFakeAsync(
                    input,
                    cancellationToken,
                    onDispose: () =>
                    {
                        File.Delete(Directory.EnumerateFiles(
                                input.WorkspacePath,
                                "*.cs",
                                SearchOption.AllDirectories)
                            .First());
                        using (var huge = new FileStream(
                                   Path.Combine(
                                       input.WorkspacePath,
                                       "huge.bin"),
                                   FileMode.CreateNew,
                                   FileAccess.Write,
                                   FileShare.None))
                        {
                            huge.SetLength(4L * 1024 * 1024 * 1024);
                        }

                        CreateFifoWhenSupported(
                            Path.Combine(input.WorkspacePath, "special.fifo"));
                        return ValueTask.CompletedTask;
                    });
            });

        var series = await RunAsync(corpus, adapter, Configuration(44));

        var unsafeRun = Assert.Single(
            series.Runs,
            run => !Safety(run, "workspace-unchanged").Passed);
        Assert.True(unsafeRun.Success);
        Assert.NotEmpty(unsafeRun.RawEvents);
        AssertHash(unsafeRun.Hashes.WorkspaceAfter);
    }

    [Fact]
    public async Task Root_reparse_point_is_fingerprinted_without_traversal()
    {
        if (!CanCreateSymbolicLinks())
        {
            return;
        }

        var corpus = await SingleTaskCorpusAsync();
        var series = await RunOneMutationAsync(
            corpus,
            static input =>
            {
                var original = input.WorkspacePath + "-original";
                Directory.Move(input.WorkspacePath, original);
                Directory.CreateSymbolicLink(input.WorkspacePath, original);
            },
            seed: 45);

        var unsafeRun = Assert.Single(
            series.Runs,
            run => !Safety(run, "workspace-unchanged").Passed);
        Assert.NotEmpty(unsafeRun.RawEvents);
        AssertHash(unsafeRun.Hashes.WorkspaceAfter);
    }

    [Fact]
    public async Task Regular_file_to_symlink_substitution_is_unsafe()
    {
        if (!CanCreateSymbolicLinks())
        {
            return;
        }

        var corpus = await SingleTaskCorpusAsync();
        var series = await RunOneMutationAsync(
            corpus,
            static input =>
            {
                var path = Directory.EnumerateFiles(
                        input.WorkspacePath,
                        "*.cs",
                        SearchOption.AllDirectories)
                    .First();
                var target = Directory.EnumerateFiles(
                        input.WorkspacePath,
                        "*.md",
                        SearchOption.AllDirectories)
                    .First();
                File.Delete(path);
                File.CreateSymbolicLink(path, target);
            },
            seed: 46);

        var unsafeRun = Assert.Single(
            series.Runs,
            run => !Safety(run, "workspace-unchanged").Passed);
        Assert.NotEmpty(unsafeRun.RawEvents);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("src/file.")]
    [InlineData("src/file ")]
    [InlineData("src/cafe\u0301.cs")]
    [InlineData("src/a:b.cs")]
    [InlineData("src/*.cs")]
    [InlineData("src\\file.cs")]
    [InlineData("../file.cs")]
    [InlineData("/rooted.cs")]
    [InlineData("src/\u0001.cs")]
    public async Task Non_portable_inspected_scope_is_rejected(string path)
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
                await CompletedFromFakeAsync(
                    input,
                    cancellationToken,
                    transform: result => result with
                    {
                        InspectedScope = new AgentBenchmarkInspectedScope(
                            [path],
                            []),
                    }));

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(corpus, adapter, Configuration(47)));

        Assert.Contains(
            "malformed inspected file scope",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, adapter.StartCalls);
    }

    [Fact]
    public async Task Token_total_overflow_is_rejected()
    {
        var corpus = await SingleTaskCorpusAsync();
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
                await CompletedFromFakeAsync(
                    input,
                    cancellationToken,
                    transform: result => result with
                    {
                        InputTokens = long.MaxValue,
                        OutputTokens = 1,
                    }));

        var exception = await Assert.ThrowsAsync<AgentBenchmarkException>(
            () => RunAsync(corpus, adapter, Configuration(48)));

        Assert.Contains(
            "token totals overflow",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corpus_collections_are_snapshotted_before_validation()
    {
        var corpus = await SingleTaskCorpusAsync();
        var capabilities = new AlternatingReadOnlyList<string>(
            ["search.file"],
            ["malformed capability"]);
        var task = corpus.Tasks[0] with
        {
            RequiredCapabilities = capabilities,
        };
        corpus = corpus with
        {
            Tasks = Array.AsReadOnly(new[] { task }),
        };
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                Assert.Equal(
                    ["search.file"],
                    input.Task.RequiredCapabilities);
                return await fake.StartAsync(input, cancellationToken);
            });

        var series = await RunAsync(corpus, adapter, Configuration(49));

        Assert.Equal(10, series.Runs.Count);
        Assert.Equal(10, adapter.StartCalls);
        Assert.Equal(1, capabilities.EnumerationCount);
    }

    private static AgentBenchmarkSafetyCheckResult Safety(
        AgentBenchmarkRunResult run,
        string id) =>
        run.SafetyChecks.Single(check => check.Id == id);

    private static async ValueTask<CompletedExecution> CompletedFromFakeAsync(
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken,
        Func<AgentBenchmarkAdapterResult, AgentBenchmarkAdapterResult>?
            transform = null,
        Func<ValueTask>? onDispose = null)
    {
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        await using var execution = await fake.StartAsync(
            input,
            cancellationToken);
        var result = await execution.Completion;
        result = transform?.Invoke(result) ?? result;
        return new CompletedExecution(
            result,
            result.RawEvents,
            onDispose: onDispose);
    }

    private static async Task<AgentBenchmarkSeriesResult> RunOneMutationAsync(
        AgentTaskCorpus corpus,
        Action<AgentBenchmarkAdapterInput> mutation,
        ulong seed)
    {
        var fake = new DeterministicFakeAgentBenchmarkAdapter();
        var mutated = false;
        var adapter = new DelegatingAdapter(
            fake.Descriptor,
            async (input, cancellationToken) =>
            {
                if (mutated)
                {
                    return await fake.StartAsync(input, cancellationToken);
                }

                mutated = true;
                return await CompletedFromFakeAsync(
                    input,
                    cancellationToken,
                    onDispose: () =>
                    {
                        mutation(input);
                        return ValueTask.CompletedTask;
                    });
            });
        return await RunAsync(corpus, adapter, Configuration(seed));
    }

    private static bool CanCreateSymbolicLinks()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-agent-symlink-probe",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, "target.txt");
            var link = Path.Combine(root, "link.txt");
            File.WriteAllText(target, "target");
            File.CreateSymbolicLink(link, target);
            return (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                  or IOException
                  or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateFifoWhenSupported(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "mkfifo",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo)
            ?? throw new IOException("Could not start mkfifo.");
        if (!process.WaitForExit(milliseconds: 5_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("mkfifo did not exit within five seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new IOException($"mkfifo exited with {process.ExitCode}.");
        }
    }

    private static IReadOnlyList<string> Order(
        AgentBenchmarkSeriesResult result) =>
        result.Runs
            .Select(run => $"{run.TaskId}:{run.Condition}:{run.Repetition}")
            .ToArray();

    private static IReadOnlyList<string> DeterministicEvidence(
        AgentBenchmarkSeriesResult result) =>
        result.Runs
            .Select(run => string.Join(
                ':',
                run.RunId,
                run.Answer,
                run.InputTokens,
                run.OutputTokens,
                run.Turns,
                run.ToolCallCount,
                run.Hashes.RawTrajectory,
                run.Success,
                run.Safe))
            .ToArray();

    private static async Task<AgentBenchmarkSeriesResult> RunFakeAsync(
        AgentTaskCorpus corpus,
        ulong seed) =>
        await RunAsync(
            corpus,
            new DeterministicFakeAgentBenchmarkAdapter(),
            Configuration(seed));

    private static async Task<AgentBenchmarkSeriesResult> RunAsync(
        AgentTaskCorpus corpus,
        IAgentBenchmarkAdapter adapter,
        AgentBenchmarkConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-agent-runner-tests",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            var runner = new AgentBenchmarkRunner(
                new RepositoryFixtureFactory(fixtureRoot));
            return await runner.RunAsync(
                corpus,
                configuration,
                adapter,
                cancellationToken);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static AgentBenchmarkConfiguration Configuration(ulong seed) =>
        new(
            "source-discovery-test",
            CorpusDirectory(),
            AgentBenchmarkDispatch.Manual,
            RunsPerTask: 5,
            RandomizationSeed: seed,
            MaximumStartAttempts: 2,
            CleanupTimeout: TimeSpan.FromSeconds(1),
            new AgentBenchmarkExecutionSettings(
                "fake-agent-1.0.0",
                "fake-model",
                "controlled",
                Hash("settings"),
                "read-only",
                "never",
                "disabled"),
            new AgentBenchmarkProvenance(
                "1.0.0",
                new string('a', 40),
                new string('b', 40),
                "dotnet-axi/v1"),
            new AgentBenchmarkConditionConfiguration(
                AgentBenchmarkCondition.Baseline,
                Hash("baseline-instructions"),
                Hash("baseline-tools")),
            new AgentBenchmarkConditionConfiguration(
                AgentBenchmarkCondition.Candidate,
                Hash("candidate-instructions"),
                Hash("candidate-tools")));

    private static async Task<AgentTaskCorpus> SingleTaskCorpusAsync(
        int? timeoutSeconds = null)
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(CorpusPath());
        var task = corpus.Tasks[0];
        if (timeoutSeconds is not null)
        {
            task = task with
            {
                Execution = task.Execution with
                {
                    TimeoutSeconds = timeoutSeconds.Value,
                },
            };
        }

        return corpus with
        {
            Tasks = Array.AsReadOnly(new[] { task }),
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void AssertHash(string value)
    {
        Assert.Equal(64, value.Length);
        Assert.All(value, static character => Assert.True(
            character is >= '0' and <= '9'
                or >= 'a' and <= 'f'));
    }

    private static string CorpusPath() => Path.Combine(
        CorpusDirectory(),
        "corpus.json");

    private static string CorpusDirectory() => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "AgentTasks",
        "source-discovery");

    private sealed class DelegatingAdapter(
        AgentBenchmarkAdapterDescriptor descriptor,
        Func<AgentBenchmarkAdapterInput, CancellationToken,
            ValueTask<IAgentBenchmarkExecution>> start)
        : IAgentBenchmarkAdapter
    {
        public AgentBenchmarkAdapterDescriptor Descriptor { get; } = descriptor;

        public int StartCalls { get; private set; }

        public ValueTask<IAgentBenchmarkExecution> StartAsync(
            AgentBenchmarkAdapterInput input,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return start(input, cancellationToken);
        }
    }

    private sealed class AlternatingReadOnlyList<T>(
        IReadOnlyList<T> first,
        IReadOnlyList<T> subsequent)
        : IReadOnlyList<T>
    {
        private int _enumerationCount;

        public int Count => first.Count;

        public int EnumerationCount => Volatile.Read(ref _enumerationCount);

        public T this[int index] => first[index];

        public IEnumerator<T> GetEnumerator()
        {
            var values = Interlocked.Increment(ref _enumerationCount) == 1
                ? first
                : subsequent;
            return values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class CompletedExecution(
        AgentBenchmarkAdapterResult result,
        IReadOnlyList<AgentBenchmarkRawEvent> rawEvents,
        Func<ValueTask>? onStop = null,
        Func<ValueTask>? onDispose = null)
        : IAgentBenchmarkExecution
    {
        public Task<AgentBenchmarkAdapterResult> Completion { get; } =
            Task.FromResult(result);

        public AgentBenchmarkProgressSnapshot GetProgressSnapshot() =>
            new(
                result.InputTokens,
                result.OutputTokens,
                result.Turns,
                result.ToolCalls,
                result.InspectedScope,
                rawEvents);

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public async ValueTask StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCalls++;
            if (onStop is not null)
            {
                await onStop();
            }
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            if (onDispose is not null)
            {
                await onDispose();
            }
        }
    }

    private sealed class HangingExecution : IAgentBenchmarkExecution
    {
        private readonly List<AgentBenchmarkRawEvent> _rawEvents = [];
        private readonly TaskCompletionSource<AgentBenchmarkAdapterResult>
            _completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public HangingExecution(string runId)
        {
            var payload = JsonSerializer.Serialize(new
            {
                processId = 60_060,
                runId,
            });
            _rawEvents.Add(
                new AgentBenchmarkRawEvent(
                    0,
                    "adapter.process.started",
                    payload,
                    Hash(payload)));
        }

        public Task<AgentBenchmarkAdapterResult> Completion => _completion.Task;

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public AgentBenchmarkProgressSnapshot GetProgressSnapshot() =>
            new(
                InputTokens: 3,
                OutputTokens: 0,
                Turns: 1,
                ToolCalls: [],
                new AgentBenchmarkInspectedScope([], []),
                Array.AsReadOnly(_rawEvents.ToArray()));

        public ValueTask StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCalls++;
            var payload = JsonSerializer.Serialize(new
            {
                processId = 60_060,
                exitCode = 137,
            });
            _rawEvents.Add(
                new AgentBenchmarkRawEvent(
                    _rawEvents.Count,
                    "adapter.process.exited",
                    payload,
                    Hash(payload)));
            _completion.TrySetCanceled(cancellationToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
