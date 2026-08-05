using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetAxi.Testing.Tests;

public sealed class AgentTaskCorpusTests
{
    private static readonly string[] DiscoveryCapabilities =
    [
        "search.file",
        "search.syntax.attributed-class",
        "search.syntax.catch",
        "search.syntax.invocation",
        "search.syntax.object-creation",
        "search.text.literal",
        "search.text.regex",
    ];

    [Fact]
    public async Task Source_discovery_corpus_is_complete_and_deterministic()
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(CorpusPath());

        Assert.Equal("source-discovery", corpus.Id);
        Assert.Equal("1.0.0", corpus.Version);
        Assert.Equal(7, corpus.Tasks.Count);
        Assert.Equal(
            DiscoveryCapabilities,
            corpus.Tasks
                .SelectMany(static task => task.RequiredCapabilities)
                .Order(StringComparer.Ordinal));
        Assert.All(
            corpus.Tasks,
            static task =>
            {
                Assert.Equal("0.3.0", task.Milestone);
                Assert.True(task.Applicability.Baseline);
                Assert.True(task.Applicability.Candidate);
                Assert.Equal("materialized-clean", task.Repository.State);
                Assert.Equal("disabled", task.Execution.Network);
                Assert.Equal("invariant", task.Execution.Locale);
                Assert.Equal("UTC", task.Execution.TimeZone);
                Assert.Equal("exact-fact-set", task.SuccessOracle.Kind);
                Assert.Null(task.SuccessOracle.ModelJudge);
                Assert.Contains(
                    "workspace-unchanged",
                    task.SafetyOracle.Checks);
                Assert.Contains(
                    "success-oracle",
                    task.RequiredValidation);
            });
    }

    [Fact]
    public async Task Later_milestone_tasks_are_not_missing_current_evidence()
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(CorpusPath());
        var futureTask = corpus.Tasks[0] with
        {
            Id = "symbol-declaration",
            Milestone = "0.4.0",
            RequiredCapabilities = ["search.symbol.declaration"],
        };
        var extended = corpus with
        {
            Tasks = [.. corpus.Tasks, futureTask],
        };

        var current = extended.SelectApplicableTasks(
            "0.3.0",
            DiscoveryCapabilities);
        var future = extended.SelectApplicableTasks(
            "0.4.0",
            [.. DiscoveryCapabilities, "search.symbol.declaration"]);

        Assert.Equal(corpus.Tasks, current);
        Assert.Contains(futureTask, future);
    }

    [Fact]
    public async Task Source_discovery_fixture_builds_cleanly()
    {
        var permissions = FixtureExecutionPermissions.Restore
            | FixtureExecutionPermissions.RepositoryCode;
        var factory = new RepositoryFixtureFactory();
        await using var fixture = await factory.CreateAsync(
            FixtureManifestPath(),
            new RepositoryFixtureOptions(permissions));
        var verification = Assert.IsType<FixtureBuildVerification>(
            fixture.BuildVerification);
        var startInfo = fixture.CreateProcessStartInfo(
            FixtureProcessKind.Restore
            | FixtureProcessKind.RepositoryCode,
            fixture.DotNetHostPath,
            "build",
            verification.Target,
            "--configuration",
            "Release",
            "--verbosity",
            "minimal",
            "--nologo",
            "--disable-build-servers",
            "--artifacts-path",
            Path.Combine(fixture.ArtifactsPath, "build"));
        using var process = new Process
        {
            StartInfo = startInfo,
        };

        Assert.True(process.Start());
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new TimeoutException(
                "The source-discovery fixture build exceeded two minutes.");
        }

        var output = string.Join(
            Environment.NewLine,
            await standardOutput,
            await standardError);
        Assert.True(process.ExitCode == 0, output);
    }

    [Fact]
    public async Task Ambiguous_duplicate_outcomes_are_rejected()
    {
        await AssertInvalidMutationAsync(
            document =>
            {
                var facts = document["tasks"]![0]!["successOracle"]![
                    "expectedFacts"]!.AsArray();
                facts.Add(facts[0]!.DeepClone());
            },
            "ambiguous duplicate fact");
    }

    [Fact]
    public async Task Candidate_guidance_in_a_common_prompt_is_rejected()
    {
        await AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["prompt"] =
                "Use dnaxi to locate the files.",
            "leaks condition-specific candidate guidance");
    }

    [Fact]
    public async Task Invalid_fixture_identity_is_rejected()
    {
        await AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["repository"]![
                "fixtureSeed"] = 9999,
            "fixture identity does not match");
    }

    [Fact]
    public async Task Fixture_content_drift_is_rejected()
    {
        await AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["repository"]![
                "contentHash"] = new string('f', 64),
            "fixture content hash does not match");
    }

    [Fact]
    public async Task Missing_required_validation_is_rejected()
    {
        await AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["requiredValidation"]!
                .AsArray()
                .RemoveAt(2),
            "must include 'success-oracle'");
    }

    [Fact]
    public async Task Nondeterministic_execution_setup_is_rejected()
    {
        await AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["execution"]!["network"] =
                "ambient",
            "must disable network");
    }

    [Fact]
    public async Task Model_judges_require_blinding_and_an_independent_version()
    {
        await AssertInvalidMutationAsync(
            document =>
            {
                var oracle = document["tasks"]![0]!["successOracle"]!
                    .AsObject();
                oracle["kind"] = "model-judged";
                oracle.Remove("normalizer");
                oracle.Remove("expectedFacts");
                oracle["modelJudge"] = new JsonObject
                {
                    ["version"] = "1.0.0",
                    ["conditionBlinded"] = false,
                    ["rubric"] = "Assess whether the answer is complete.",
                };
            },
            "must be condition-blinded");

        await AssertInvalidMutationAsync(
            document =>
            {
                var oracle = document["tasks"]![0]!["successOracle"]!
                    .AsObject();
                oracle["kind"] = "model-judged";
                oracle.Remove("normalizer");
                oracle.Remove("expectedFacts");
                oracle["modelJudge"] = new JsonObject
                {
                    ["version"] = "latest",
                    ["conditionBlinded"] = true,
                    ["rubric"] = "Assess whether the answer is complete.",
                };
            },
            "model judge version must be an explicit");
    }

    private static async Task AssertInvalidMutationAsync(
        Action<JsonObject> mutation,
        string expectedMessage)
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-agent-corpus-tests",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(Path.GetDirectoryName(CorpusPath())!, testRoot);
            var corpusPath = Path.Combine(testRoot, "corpus.json");
            var document = JsonNode.Parse(
                    await File.ReadAllTextAsync(corpusPath))!
                .AsObject();
            mutation(document);
            await File.WriteAllTextAsync(
                corpusPath,
                document.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));

            var exception = await Assert.ThrowsAsync<AgentTaskCorpusException>(
                () => AgentTaskCorpusLoader
                    .LoadAsync(corpusPath)
                    .AsTask());
            Assert.Contains(
                expectedMessage,
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string CorpusPath() => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "AgentTasks",
        "source-discovery",
        "corpus.json");

    private static string FixtureManifestPath() => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "AgentTasks",
        "source-discovery",
        "fixture.json");
}
