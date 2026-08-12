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
                Assert.Equal(
                    "ordinal-lines/v1",
                    task.SuccessOracle.Normalizer);
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

    [Theory]
    [InlineData("search.references")]
    [InlineData("search.implementations")]
    [InlineData("search.derived")]
    [InlineData("search.bases")]
    [InlineData("search.overrides")]
    [InlineData("search.callers")]
    [InlineData("search.callees")]
    [InlineData("search.relationships")]
    [InlineData("search.symbol.references")]
    [InlineData("search.symbol.implementations")]
    [InlineData("search.symbol.inheritance")]
    [InlineData("search.symbol.overrides")]
    [InlineData("search.symbol.derived-types")]
    [InlineData("search.symbol.relationships")]
    [InlineData("context.callers")]
    [InlineData("context.callees")]
    [InlineData("context.relationships")]
    [InlineData("context.tests")]
    [InlineData("context.symbol.callers")]
    [InlineData("context.symbol.relationships")]
    [InlineData("context.symbol.tests")]
    [InlineData("graph.project")]
    [InlineData("graph.path")]
    [InlineData("graph.paths")]
    [InlineData("graph.cycle")]
    [InlineData("graph.cycles")]
    [InlineData("analysis.impact")]
    [InlineData("project.graph")]
    [InlineData("dependency.graph")]
    [InlineData("mutation.rename")]
    public Task Unshipped_relationship_expectations_are_rejected(
        string capability) =>
        AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["requiredCapabilities"]![0] =
                capability,
            "cannot require unshipped relationship or mutation capabilities");

    [Theory]
    [InlineData("search.symbol.reference-value")]
    [InlineData("search.symbol.implementation-detail")]
    [InlineData("search.symbol.inheritance-metadata")]
    [InlineData("search.symbol.override-metadata")]
    [InlineData("search.symbol.graph-color")]
    [InlineData("analysis.impact-metadata")]
    [InlineData("metadata.reference")]
    [InlineData("workspace.path")]
    [InlineData("workspace.path-normalization")]
    public async Task Relationship_guard_does_not_match_unrelated_tokens(
        string capability)
    {
        var corpus = await LoadMutationAsync(
            document => document["tasks"]![0]!["requiredCapabilities"]![0] =
                capability);

        Assert.Equal(capability, corpus.Tasks[0].RequiredCapabilities[0]);
    }

    [Theory]
    [InlineData("Find every caller of ArchiveHandler.")]
    [InlineData("Find callers in src/Caller.cs.")]
    [InlineData("Inspect callers/callees.")]
    [InlineData("Inspect callers\\callees.")]
    [InlineData("Inspect references/implementations.")]
    [InlineData("Inspect references\\implementations.")]
    [InlineData("Inspect symbol/references.")]
    [InlineData("Inspect references/symbol.")]
    [InlineData("Inspect search/references.")]
    [InlineData("Inspect references/search.")]
    [InlineData("Inspect context/callers.")]
    [InlineData("Inspect callers/context.")]
    [InlineData("Inspect context/tests.")]
    [InlineData("Inspect tests/context.")]
    [InlineData("Inspect context\\tests.")]
    [InlineData("Inspect tests\\context.")]
    [InlineData("Inspect graph/projects.")]
    [InlineData("Inspect projects/graph.")]
    [InlineData("Inspect graph\\dependencies.")]
    [InlineData("Inspect dependencies\\graph.")]
    [InlineData("Return the project graph for the fixture.")]
    [InlineData("Return project/graph for the fixture.")]
    [InlineData("Perform impact analysis for ArchiveHandler.")]
    public Task Unshipped_prompt_expectations_are_rejected(string prompt) =>
        AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["prompt"] = prompt,
            "prompt cannot require unshipped relationship, graph, impact, or mutation outcomes");

    [Theory]
    [InlineData("caller: ArchiveHandler.Run")]
    [InlineData("caller: src/Caller.cs")]
    [InlineData("relationships: callers/callees")]
    [InlineData("relationships: callers\\callees")]
    [InlineData("relationships: references/implementations")]
    [InlineData("relationships: references\\implementations")]
    [InlineData("command: symbol/references")]
    [InlineData("command: references/symbol")]
    [InlineData("command: context/callers")]
    [InlineData("command: callers/context")]
    [InlineData("command: context/tests")]
    [InlineData("command: tests/context")]
    [InlineData("command: graph/projects")]
    [InlineData("command: projects/graph")]
    [InlineData("command: graph\\impact")]
    [InlineData("command: impact\\graph")]
    [InlineData("dependency graph: App -> Core")]
    [InlineData("impact analysis: tests/App.Tests.csproj")]
    public Task Unshipped_oracle_expectations_are_rejected(string fact) =>
        AssertInvalidMutationAsync(
            document =>
            {
                var facts = document["tasks"]![0]!["successOracle"]![
                    "expectedFacts"]!.AsArray();
                facts.Clear();
                facts.Add(fact);
            },
            "expected facts cannot require unshipped relationship, graph, impact, or mutation outcomes");

    [Theory]
    [InlineData("src/Discovery/Handlers/AuditHandler.cs")]
    [InlineData("src/Discovery/Handlers")]
    [InlineData("AuditHandler.cs")]
    [InlineData("src\\Discovery\\Handlers\\AuditHandler.cs")]
    [InlineData("src\\Discovery\\Handlers")]
    public async Task Prompt_and_oracle_guard_allow_legitimate_source_paths(
        string path)
    {
        var corpus = await LoadMutationAsync(
            document =>
            {
                document["tasks"]![0]!["prompt"] =
                    $"Inspect the source path {path} and return its file path.";
                var facts = document["tasks"]![0]!["successOracle"]![
                    "expectedFacts"]!.AsArray();
                facts.Clear();
                facts.Add($"path: {path}");
            });

        Assert.Equal(
            $"path: {path}",
            Assert.Single(corpus.Tasks[0].SuccessOracle.ExpectedFacts));
    }

    [Theory]
    [InlineData("callers/callees")]
    [InlineData("callers\\callees")]
    public async Task Declared_all_reserved_path_is_allowed(string path)
    {
        var corpus = await LoadMutationAsync(
            document =>
            {
                document["tasks"]![0]!["prompt"] =
                    $"Inspect the declared repository path {path}.";
                var facts = document["tasks"]![0]!["successOracle"]![
                    "expectedFacts"]!.AsArray();
                facts.Clear();
                facts.Add($"path: {path}");
            },
            AddAllReservedFixturePathAsync);

        Assert.Equal(
            $"path: {path}",
            Assert.Single(corpus.Tasks[0].SuccessOracle.ExpectedFacts));
    }

    [Theory]
    [InlineData("callers/callees")]
    [InlineData("callers\\callees")]
    public async Task Declared_all_reserved_path_does_not_mask_command_usage(
        string command)
    {
        var exception = await Assert.ThrowsAsync<AgentTaskCorpusException>(
            () => LoadMutationAsync(
                document => document["tasks"]![0]!["prompt"] =
                    $"Run command: {command}.",
                AddAllReservedFixturePathAsync));

        Assert.Contains(
            "prompt cannot require unshipped relationship, graph, impact, or mutation outcomes",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("workspace-write")]
    [InlineData("workspace:write")]
    [InlineData("repository:edit")]
    public Task Pre_0_6_tasks_cannot_permit_workspace_mutation(string tool) =>
        AssertInvalidMutationAsync(
            document => AddSortedIdentifier(
                document["tasks"]![0]!["execution"]![
                    "permittedTools"]!.AsArray(),
                tool),
            "cannot permit workspace mutation tools");

    [Theory]
    [InlineData("output-write")]
    [InlineData("stdout-write")]
    public async Task Pre_0_6_tasks_allow_non_workspace_output_tools(
        string tool)
    {
        var corpus = await LoadMutationAsync(
            document => AddSortedIdentifier(
                document["tasks"]![0]!["execution"]![
                    "permittedTools"]!.AsArray(),
                tool));

        Assert.Contains(
            tool,
            corpus.Tasks[0].Execution.PermittedTools,
            StringComparer.Ordinal);
    }

    [Fact]
    public Task Pre_0_6_prompts_cannot_request_repository_edits() =>
        AssertInvalidMutationAsync(
            document => document["tasks"]![0]!["prompt"] =
                "Edit src/Discovery/Handlers/AuditHandler.cs to change the returned value.",
            "prompt cannot require unshipped relationship, graph, impact, or mutation outcomes");

    [Fact]
    public async Task Pre_0_6_prompts_allow_file_path_output_instructions()
    {
        const string prompt =
            "Read the repository and write file paths to standard output.";
        var corpus = await LoadMutationAsync(
            document => document["tasks"]![0]!["prompt"] = prompt);

        Assert.Equal(prompt, corpus.Tasks[0].Prompt);
    }

    [Fact]
    public async Task Shipped_document_and_outline_references_are_allowed()
    {
        var corpus = await LoadMutationAsync(
            document =>
            {
                document["tasks"]![0]!["prompt"] =
                    "Return whether the outline reference is available.";
                var facts = document["tasks"]![0]!["successOracle"]![
                    "expectedFacts"]!.AsArray();
                facts.Clear();
                facts.Add("document reference: available");
            });

        Assert.Equal(
            "document reference: available",
            Assert.Single(corpus.Tasks[0].SuccessOracle.ExpectedFacts));
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
        var exception = await Assert.ThrowsAsync<AgentTaskCorpusException>(
            () => LoadMutationAsync(mutation));
        Assert.Contains(
            expectedMessage,
            exception.Message,
            StringComparison.Ordinal);
    }

    private static async Task<AgentTaskCorpus> LoadMutationAsync(
        Action<JsonObject> mutation,
        Func<string, JsonObject, Task>? prepareFixture = null)
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
            if (prepareFixture is not null)
            {
                await prepareFixture(testRoot, document);
            }

            await File.WriteAllTextAsync(
                corpusPath,
                document.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));

            return await AgentTaskCorpusLoader.LoadAsync(corpusPath);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static async Task AddAllReservedFixturePathAsync(
        string testRoot,
        JsonObject corpus)
    {
        var manifestPath = Path.Combine(testRoot, "fixture.json");
        var manifest = JsonNode.Parse(
                await File.ReadAllTextAsync(manifestPath))!
            .AsObject();
        manifest["files"]!.AsArray().Add(
            new JsonObject
            {
                ["path"] = "callers/callees",
                ["template"] = "templates/README.md",
                ["expandTokens"] = false,
            });
        await File.WriteAllTextAsync(
            manifestPath,
            manifest.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));

        var factory = new RepositoryFixtureFactory(
            Path.Combine(testRoot, "materialized"));
        await using var fixture = await factory.CreateAsync(manifestPath);
        foreach (var task in corpus["tasks"]!.AsArray())
        {
            task!["repository"]!["contentHash"] = fixture.ContentHash;
        }
    }

    private static void AddSortedIdentifier(JsonArray identifiers, string value)
    {
        var ordered = identifiers
            .Select(static node => node!.GetValue<string>())
            .Append(value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        identifiers.Clear();
        foreach (var identifier in ordered)
        {
            identifiers.Add(identifier);
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
