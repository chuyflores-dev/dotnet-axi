using System.Diagnostics;
using System.Text.Json;

namespace DotNetAxi.Cli.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentBenchmarkIntegrationCollection
{
    public const string Name = "Agent benchmark integration";
}

[Collection(AgentBenchmarkIntegrationCollection.Name)]
public sealed class AgentBenchmarkScriptTests
{
    [Fact]
    public async Task List_tasks_parses_the_corpus_without_dispatching_an_agent()
    {
        var result = await RunAsync("-ListTasks");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(
            "refactor-owned-scope-probe",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "add-ledger-try-format",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "rename-ledger-format-contract",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "rename-report-format-string-overload",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rename-canonical-status-format", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rename-interface-dispatch-format", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rename-generic-envelope-base", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rename-virtual-format-overrides", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rename-message-extension-format", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rename-partial-linked-format", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rename-conditional-format", result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_task_is_rejected_before_external_tool_checks()
    {
        var result = await RunAsync(
            "-Condition", "baseline",
            "-Task", "missing-task",
            "-CodexExecutable", "definitely-not-a-command");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Task 'missing-task' was not found exactly once",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Required command",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_tasks_are_real_changes_with_hidden_validation()
    {
        var corpusPath = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Fixtures",
            "AgentTasks",
            "repository-work",
            "corpus.json");
        await using var stream = File.OpenRead(corpusPath);
        using var document = await JsonDocument.ParseAsync(stream);
        var tasks = document.RootElement.GetProperty("tasks")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(11, tasks.Length);
        Assert.Equal(
            ["refactor", "feature", "refactor", "refactor", "refactor", "refactor", "refactor", "refactor", "refactor", "refactor", "refactor"],
            tasks.Select(static task =>
                task.GetProperty("kind").GetString()!).ToArray());
        var expectedChanges = new Dictionary<string, string[]>(
            StringComparer.Ordinal)
        {
            ["refactor-owned-scope-probe"] = ["src/Worker/ScopeProbe.cs"],
            ["add-ledger-try-format"] = ["src/Core/LedgerService.cs"],
            ["rename-ledger-format-contract"] =
            [
                "src/Consumers/LedgerReport.cs",
                "src/Consumers/WorkerJob.cs",
                "src/Contracts/ILedgerFormatter.cs",
                "src/Implementations/LedgerFormatter.cs",
                "src/Implementations/WorkerLedgerFormatter.cs",
            ],
            ["rename-report-format-string-overload"] =
            [
                "src/Consumers/ReportView.cs",
                "src/Consumers/WorkerReportView.cs",
                "src/Contracts/IReportFormatter.cs",
                "src/Implementations/ReportFormatter.cs",
                "src/Implementations/WorkerReportFormatter.cs",
            ],
            ["rename-canonical-status-format"] =
            [
                "src/Canonical.Contracts/IStatusFormatter.cs",
                "src/Canonical.Implementations/StatusFormatter.cs",
                "src/Canonical.Consumers/StatusView.cs",
            ],
            ["rename-interface-dispatch-format"] =
            [
                "src/Contracts/IMessageFormatter.cs",
                "src/Implementations/EmailMessageFormatter.cs",
                "src/Implementations/SmsMessageFormatter.cs",
                "src/Implementations/PushMessageFormatter.cs",
                "src/Consumers/MessageDispatcher.cs",
            ],
            ["rename-generic-envelope-base"] =
            [
                "src/Models/EnvelopeModels.cs",
                "src/Consumers/EnvelopePresenter.cs",
            ],
            ["rename-virtual-format-overrides"] =
            ["src/Models/Formatters.cs", "src/Consumers/FormatterPresenter.cs"],
            ["rename-message-extension-format"] =
            ["src/Models/Formatters.cs", "src/Consumers/FormatView.cs"],
            ["rename-partial-linked-format"] =
            [
                "src/Shared/LinkedFormatter.Format.cs",
                "src/Primary/PrimaryView.cs",
                "src/Secondary/SecondaryView.cs",
            ],
            ["rename-conditional-format"] =
            ["src/Models/ConditionalFormatter.cs", "src/Consumers/ConditionalView.cs"],
        };
        Assert.All(
            tasks,
            task =>
            {
                Assert.Equal(
                    expectedChanges[task.GetProperty("id").GetString()!],
                    task.GetProperty("allowedChanges")
                        .EnumerateArray()
                        .Select(static path => path.GetString()!)
                        .ToArray());
                Assert.All(
                    task.GetProperty("validation").GetProperty("files")
                        .EnumerateArray(),
                    static file => Assert.StartsWith(
                        ".benchmark-validation/",
                        file.GetProperty("path").GetString()!,
                        StringComparison.Ordinal));
            });
    }

    [Fact]
    public async Task Semantic_relationship_task_is_neutral_and_hidden()
    {
        var corpusPath = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Fixtures",
            "AgentTasks",
            "repository-work",
            "corpus.json");
        using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(
            corpusPath));
        var task = corpus.RootElement.GetProperty("tasks")
            .EnumerateArray()
            .Single(task => task.GetProperty("id").GetString() ==
                "rename-ledger-format-contract");

        Assert.Equal("0.6.0", task.GetProperty("milestone").GetString());
        Assert.True(task.GetProperty("applicability").GetProperty("baseline")
            .GetBoolean());
        Assert.True(task.GetProperty("applicability").GetProperty("candidate")
            .GetBoolean());
        var prompt = task.GetProperty("prompt").GetString()!;
        Assert.DoesNotContain("dnaxi", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("search references", prompt,
            StringComparison.OrdinalIgnoreCase);

        var manifestPath = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(corpusPath)!,
                task.GetProperty("repository")
                    .GetProperty("fixtureManifest")
                    .GetString()!));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            manifestPath));
        Assert.DoesNotContain(
            manifest.RootElement.GetProperty("files").EnumerateArray(),
            static file => file.GetProperty("path").GetString()!
                .StartsWith(".benchmark-validation/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Semantic_relationship_oracle_rejects_bad_states_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticRelationshipTaskAsync();
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);

            Assert.Contains("semantic-oracle: rejected", original.Output);

            var productionFiles = new[]
            {
                "src/Consumers/LedgerReport.cs",
                "src/Consumers/WorkerJob.cs",
                "src/Contracts/ILedgerFormatter.cs",
                "src/Implementations/LedgerFormatter.cs",
                "src/Implementations/WorkerLedgerFormatter.cs",
            };
            foreach (var relativePath in productionFiles)
            {
                var path = Path.Combine(root, relativePath);
                var content = await File.ReadAllTextAsync(path);
                await File.WriteAllTextAsync(
                    path,
                    content
                        .Replace(" Format(", " Render(", StringComparison.Ordinal)
                        .Replace(".Format(", ".Render(", StringComparison.Ordinal));
            }

            var correctContents = productionFiles.ToDictionary(
                relativePath => relativePath,
                relativePath => File.ReadAllText(Path.Combine(root, relativePath)),
                StringComparer.Ordinal);
            var correct = await RunValidatorAsync(root);
            Assert.Equal(0, correct.ExitCode);
            Assert.Contains("semantic-oracle: verified", correct.Output);

            var ledgerReportPath = Path.Combine(
                root,
                "src/Consumers/LedgerReport.cs");
            await File.WriteAllTextAsync(
                ledgerReportPath,
                correctContents["src/Consumers/LedgerReport.cs"].Replace(
                    "formatter.Render(value)",
                    "$\"ledger:{value}\"",
                    StringComparison.Ordinal));
            var bypassedInterfaceCall = await RunValidatorAsync(root);
            Assert.NotEqual(0, bypassedInterfaceCall.ExitCode);
            Assert.Contains(
                "semantic-oracle: rejected",
                bypassedInterfaceCall.Output);
            await File.WriteAllTextAsync(
                ledgerReportPath,
                correctContents["src/Consumers/LedgerReport.cs"]);

            var workerJobPath = Path.Combine(root, "src/Consumers/WorkerJob.cs");
            await File.WriteAllTextAsync(
                workerJobPath,
                correctContents["src/Consumers/WorkerJob.cs"].Replace(
                    "formatter.Render(value)",
                    "$\"worker:{value}\"",
                    StringComparison.Ordinal));
            var bypassedConcreteCall = await RunValidatorAsync(root);
            Assert.NotEqual(0, bypassedConcreteCall.ExitCode);
            Assert.Contains(
                "semantic-oracle: rejected",
                bypassedConcreteCall.Output);
            await File.WriteAllTextAsync(
                workerJobPath,
                correctContents["src/Consumers/WorkerJob.cs"]);

            await File.WriteAllTextAsync(
                ledgerReportPath,
                correctContents["src/Consumers/LedgerReport.cs"].Replace(
                    "public string Create(string value) => formatter.Render(value);",
                    """
                    public string Create(string value)
                    {
                        _ = formatter.Render(value);
                        return $"ledger:{value}";
                    }
                    """,
                    StringComparison.Ordinal));
            var ignoredInterfaceResult = await RunValidatorAsync(root);
            Assert.NotEqual(0, ignoredInterfaceResult.ExitCode);
            Assert.Contains(
                "semantic-oracle: rejected",
                ignoredInterfaceResult.Output);
            await File.WriteAllTextAsync(
                ledgerReportPath,
                correctContents["src/Consumers/LedgerReport.cs"]);

            await File.WriteAllTextAsync(
                workerJobPath,
                correctContents["src/Consumers/WorkerJob.cs"].Replace(
                    "public string Run(string value) => formatter.Render(value);",
                    """
                    public string Run(string value)
                    {
                        _ = formatter.Render(value);
                        return $"worker:{value}";
                    }
                    """,
                    StringComparison.Ordinal));
            var ignoredConcreteResult = await RunValidatorAsync(root);
            Assert.NotEqual(0, ignoredConcreteResult.ExitCode);
            Assert.Contains(
                "semantic-oracle: rejected",
                ignoredConcreteResult.Output);
            await File.WriteAllTextAsync(
                workerJobPath,
                correctContents["src/Consumers/WorkerJob.cs"]);

            AddForwarder(
                Path.Combine(root, "src/Contracts/ILedgerFormatter.cs"),
                "string Format(string value) => Render(value);");
            AddForwarder(
                Path.Combine(root, "src/Implementations/LedgerFormatter.cs"),
                "public string Format(string value) => Render(value);");
            AddForwarder(
                Path.Combine(root, "src/Implementations/WorkerLedgerFormatter.cs"),
                "public string Format(string value) => Render(value);");
            var retainedOldMember = await RunValidatorAsync(root);
            Assert.NotEqual(0, retainedOldMember.ExitCode);
            Assert.Contains("semantic-oracle: rejected", retainedOldMember.Output);

            foreach (var (relativePath, content) in correctContents)
            {
                await File.WriteAllTextAsync(Path.Combine(root, relativePath), content);
            }
            var ledgerPath = Path.Combine(
                root,
                "src/Implementations/LedgerFormatter.cs");
            await File.WriteAllTextAsync(
                ledgerPath,
                (await File.ReadAllTextAsync(ledgerPath)).Replace(
                    "ledger:{value}",
                    "changed:{value}",
                    StringComparison.Ordinal));
            var changedBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedBehavior.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Exact_overload_semantic_task_is_neutral_and_hidden()
    {
        var corpusPath = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Fixtures",
            "AgentTasks",
            "repository-work",
            "corpus.json");
        using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(
            corpusPath));
        var task = corpus.RootElement.GetProperty("tasks")
            .EnumerateArray()
            .Single(task => task.GetProperty("id").GetString() ==
                "rename-report-format-string-overload");

        Assert.Equal("0.6.0", task.GetProperty("milestone").GetString());
        Assert.True(task.GetProperty("applicability").GetProperty("baseline")
            .GetBoolean());
        Assert.True(task.GetProperty("applicability").GetProperty("candidate")
            .GetBoolean());
        var prompt = task.GetProperty("prompt").GetString()!;
        Assert.DoesNotContain("dnaxi", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("search references", prompt,
            StringComparison.OrdinalIgnoreCase);

        var manifestPath = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(corpusPath)!,
                task.GetProperty("repository")
                    .GetProperty("fixtureManifest")
                    .GetString()!));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            manifestPath));
        Assert.DoesNotContain(
            manifest.RootElement.GetProperty("files").EnumerateArray(),
            static file => file.GetProperty("path").GetString()!
                .StartsWith(".benchmark-validation/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exact_overload_oracle_rejects_bad_states_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticOverloadTaskAsync();
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);
            Assert.Contains("semantic-oracle: rejected", original.Output);

            var productionFiles = new[]
            {
                "src/Consumers/ReportView.cs",
                "src/Consumers/WorkerReportView.cs",
                "src/Contracts/IReportFormatter.cs",
                "src/Implementations/ReportFormatter.cs",
                "src/Implementations/WorkerReportFormatter.cs",
            };
            var originalContents = productionFiles.ToDictionary(
                relativePath => relativePath,
                relativePath => File.ReadAllText(Path.Combine(root, relativePath)),
                StringComparer.Ordinal);

            foreach (var relativePath in productionFiles)
            {
                var path = Path.Combine(root, relativePath);
                await File.WriteAllTextAsync(
                    path,
                    (await File.ReadAllTextAsync(path))
                        .Replace("Format(int", "Render(int", StringComparison.Ordinal)
                        .Replace(".Format(revision)", ".Render(revision)", StringComparison.Ordinal));
            }

            var wrongOverload = await RunValidatorAsync(root);
            Assert.NotEqual(0, wrongOverload.ExitCode);
            Assert.Contains("semantic-oracle: rejected", wrongOverload.Output);

            foreach (var (relativePath, content) in originalContents)
            {
                await File.WriteAllTextAsync(Path.Combine(root, relativePath), content);
            }

            foreach (var relativePath in productionFiles)
            {
                var path = Path.Combine(root, relativePath);
                await File.WriteAllTextAsync(
                    path,
                    (await File.ReadAllTextAsync(path))
                        .Replace("Format(string", "Render(string", StringComparison.Ordinal)
                        .Replace(".Format(value)", ".Render(value)", StringComparison.Ordinal));
            }

            var correctContents = productionFiles.ToDictionary(
                relativePath => relativePath,
                relativePath => File.ReadAllText(Path.Combine(root, relativePath)),
                StringComparer.Ordinal);
            var correct = await RunValidatorAsync(root);
            Assert.Equal(0, correct.ExitCode);
            Assert.Contains("semantic-oracle: verified", correct.Output);

            AddForwarder(
                Path.Combine(root, "src/Contracts/IReportFormatter.cs"),
                "string Format(string value) => Render(value);");
            AddForwarder(
                Path.Combine(root, "src/Implementations/ReportFormatter.cs"),
                "public string Format(string value) => Render(value);");
            AddForwarder(
                Path.Combine(root, "src/Implementations/WorkerReportFormatter.cs"),
                "public string Format(string value) => Render(value);");
            var retainedStringMember = await RunValidatorAsync(root);
            Assert.NotEqual(0, retainedStringMember.ExitCode);
            Assert.Contains("semantic-oracle: rejected", retainedStringMember.Output);

            foreach (var (relativePath, content) in correctContents)
            {
                await File.WriteAllTextAsync(Path.Combine(root, relativePath), content);
            }
            var formatterPath = Path.Combine(
                root,
                "src/Implementations/ReportFormatter.cs");
            await File.WriteAllTextAsync(
                formatterPath,
                (await File.ReadAllTextAsync(formatterPath)).Replace(
                    "report:{value}",
                    "changed:{value}",
                    StringComparison.Ordinal));
            var changedBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedBehavior.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Unrelated_name_oracle_rejects_wrong_scope_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticTaskAsync(
            "semantic-unrelated-names",
            "RenameCanonicalStatusFormat.cs",
            "UnrelatedNameVerifier.csproj");
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);

            var archiveFiles = new[]
            {
                "src/Archive/IStatusFormatter.cs",
                "src/Archive/StatusFormatter.cs",
                "src/Archive/StatusView.cs",
            };
            var archiveContents = archiveFiles.ToDictionary(
                relativePath => relativePath,
                relativePath => File.ReadAllText(Path.Combine(root, relativePath)),
                StringComparer.Ordinal);
            foreach (var relativePath in archiveFiles)
            {
                var path = Path.Combine(root, relativePath);
                await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path))
                    .Replace("Format(string", "Render(string", StringComparison.Ordinal)
                    .Replace(".Format(value)", ".Render(value)", StringComparison.Ordinal));
            }
            var wrongTarget = await RunValidatorAsync(root);
            Assert.NotEqual(0, wrongTarget.ExitCode);
            Assert.Contains("semantic-oracle: rejected", wrongTarget.Output);
            foreach (var (relativePath, content) in archiveContents)
            {
                await File.WriteAllTextAsync(Path.Combine(root, relativePath), content);
            }

            var canonicalFiles = new[]
            {
                "src/Canonical.Contracts/IStatusFormatter.cs",
                "src/Canonical.Implementations/StatusFormatter.cs",
                "src/Canonical.Consumers/StatusView.cs",
            };
            foreach (var relativePath in canonicalFiles)
            {
                var path = Path.Combine(root, relativePath);
                await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path))
                    .Replace("Format(string", "Render(string", StringComparison.Ordinal)
                    .Replace(".Format(value)", ".Render(value)", StringComparison.Ordinal));
            }

            var correct = await RunValidatorAsync(root);
            Assert.True(correct.ExitCode == 0, correct.Output);
            Assert.Contains("semantic-oracle: verified", correct.Output);

            AddForwarder(
                Path.Combine(root, "src/Canonical.Contracts/IStatusFormatter.cs"),
                "string Format(string value) => Render(value);");
            AddForwarder(
                Path.Combine(root, "src/Canonical.Implementations/StatusFormatter.cs"),
                "public string Format(string value) => Render(value);");
            var retainedMember = await RunValidatorAsync(root);
            Assert.NotEqual(0, retainedMember.ExitCode);
            Assert.Contains("semantic-oracle: rejected", retainedMember.Output);

            foreach (var relativePath in canonicalFiles)
            {
                var path = Path.Combine(root, relativePath);
                await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path))
                    .Replace("public string Format(string value) => Render(value);", string.Empty,
                        StringComparison.Ordinal)
                    .Replace("string Format(string value) => Render(value);", string.Empty,
                        StringComparison.Ordinal));
            }
            var canonicalFormatterPath = Path.Combine(root,
                "src/Canonical.Implementations/StatusFormatter.cs");
            await File.WriteAllTextAsync(canonicalFormatterPath,
                (await File.ReadAllTextAsync(canonicalFormatterPath)).Replace(
                    "canonical:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedBehavior.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Interface_dispatch_oracle_rejects_incomplete_updates_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticTaskAsync(
            "semantic-interface-dispatch",
            "RenameInterfaceDispatchFormat.cs",
            "InterfaceDispatchVerifier.csproj");
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);
            Assert.Contains("semantic-oracle: rejected", original.Output);

            var productionFiles = new[]
            {
                "src/Contracts/IMessageFormatter.cs",
                "src/Implementations/EmailMessageFormatter.cs",
                "src/Implementations/SmsMessageFormatter.cs",
                "src/Implementations/PushMessageFormatter.cs",
                "src/Consumers/MessageDispatcher.cs",
            };
            foreach (var relativePath in productionFiles)
            {
                var path = Path.Combine(root, relativePath);
                await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path))
                    .Replace("Format(string", "Render(string", StringComparison.Ordinal)
                    .Replace(".Format(value)", ".Render(value)", StringComparison.Ordinal));
            }

            var correctContents = productionFiles.ToDictionary(
                relativePath => relativePath,
                relativePath => File.ReadAllText(Path.Combine(root, relativePath)),
                StringComparer.Ordinal);
            var correct = await RunValidatorAsync(root);
            Assert.True(correct.ExitCode == 0, correct.Output);
            Assert.Contains("semantic-oracle: verified", correct.Output);

            var smsFormatterPath = Path.Combine(root,
                "src/Implementations/SmsMessageFormatter.cs");
            await File.WriteAllTextAsync(smsFormatterPath,
                (await File.ReadAllTextAsync(smsFormatterPath)).Replace(
                    "public string Render(string",
                    "string SemanticInterfaceDispatch.Contracts.IMessageFormatter.Render(string",
                    StringComparison.Ordinal));
            var incompleteImplementation = await RunValidatorAsync(root);
            Assert.NotEqual(0, incompleteImplementation.ExitCode);
            Assert.Contains("semantic-oracle: rejected", incompleteImplementation.Output);
            await File.WriteAllTextAsync(smsFormatterPath,
                correctContents["src/Implementations/SmsMessageFormatter.cs"]);

            var dispatcherPath = Path.Combine(root,
                "src/Consumers/MessageDispatcher.cs");
            await File.WriteAllTextAsync(dispatcherPath,
                (await File.ReadAllTextAsync(dispatcherPath)).Replace(
                    "sms.Render(value)", "$\"sms:{value}\"", StringComparison.Ordinal));
            var incompleteDispatch = await RunValidatorAsync(root);
            Assert.NotEqual(0, incompleteDispatch.ExitCode);
            Assert.Contains("semantic-oracle: rejected", incompleteDispatch.Output);
            await File.WriteAllTextAsync(dispatcherPath,
                correctContents["src/Consumers/MessageDispatcher.cs"]);

            AddForwarder(
                Path.Combine(root, "src/Implementations/EmailMessageFormatter.cs"),
                "private string Format(string value) => Render(value);");
            var retainedMember = await RunValidatorAsync(root);
            Assert.NotEqual(0, retainedMember.ExitCode);
            Assert.Contains("semantic-oracle: rejected", retainedMember.Output);
            foreach (var (relativePath, content) in correctContents)
            {
                await File.WriteAllTextAsync(Path.Combine(root, relativePath), content);
            }

            var emailFormatterPath = Path.Combine(root,
                "src/Implementations/EmailMessageFormatter.cs");
            await File.WriteAllTextAsync(emailFormatterPath,
                (await File.ReadAllTextAsync(emailFormatterPath)).Replace(
                    "email:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedBehavior.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Generic_inheritance_oracle_rejects_decoys_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticTaskAsync(
            "semantic-generic-inheritance",
            "RenameGenericEnvelope.cs",
            "GenericInheritanceVerifier.csproj");
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);
            Assert.Contains("semantic-oracle: rejected", original.Output);

            var productionFiles = new[]
            {
                "src/Models/EnvelopeModels.cs",
                "src/Consumers/EnvelopePresenter.cs",
            };
            foreach (var relativePath in productionFiles)
            {
                var path = Path.Combine(root, relativePath);
                await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace(
                    "GenericEnvelope", "MessageEnvelope", StringComparison.Ordinal));
            }
            var correctContents = productionFiles.ToDictionary(
                relativePath => relativePath,
                relativePath => File.ReadAllText(Path.Combine(root, relativePath)),
                StringComparer.Ordinal);
            var correct = await RunValidatorAsync(root);
            Assert.True(correct.ExitCode == 0, correct.Output);
            Assert.Contains("semantic-oracle: verified", correct.Output);

            var modelsPath = Path.Combine(root, "src/Models/EnvelopeModels.cs");
            await File.WriteAllTextAsync(modelsPath,
                (await File.ReadAllTextAsync(modelsPath)) + "\npublic class GenericEnvelope<T> { }\n");
            var retainedType = await RunValidatorAsync(root);
            Assert.NotEqual(0, retainedType.ExitCode);
            Assert.Contains("semantic-oracle: rejected", retainedType.Output);
            await File.WriteAllTextAsync(modelsPath, correctContents["src/Models/EnvelopeModels.cs"]);

            await File.WriteAllTextAsync(modelsPath,
                (await File.ReadAllTextAsync(modelsPath)).Replace(
                    "new string Label(string", "new string Describe(string", StringComparison.Ordinal));
            var changedHiddenMember = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedHiddenMember.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedHiddenMember.Output);
            await File.WriteAllTextAsync(modelsPath, correctContents["src/Models/EnvelopeModels.cs"]);

            await File.WriteAllTextAsync(modelsPath,
                (await File.ReadAllTextAsync(modelsPath)).Replace(
                    "shadow:{value}", "altered:{value}", StringComparison.Ordinal));
            var changedHiddenBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedHiddenBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedHiddenBehavior.Output);
            await File.WriteAllTextAsync(modelsPath, correctContents["src/Models/EnvelopeModels.cs"]);

            await File.WriteAllTextAsync(modelsPath,
                (await File.ReadAllTextAsync(modelsPath)).Replace(
                    "base:{typeof(T).Name}:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedBehavior.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Virtual_override_oracle_rejects_decoys_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticTaskAsync("semantic-virtual-overrides", "RenameVirtualFormat.cs", "VirtualOverrideVerifier.csproj");
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);
            Assert.Contains("semantic-oracle: rejected", original.Output);
            var modelPath = Path.Combine(root, "src/Models/Formatters.cs");
            var consumerPath = Path.Combine(root, "src/Consumers/FormatterPresenter.cs");
            var model = await File.ReadAllTextAsync(modelPath);
            await File.WriteAllTextAsync(modelPath, model.Replace("virtual string Format", "virtual string Render", StringComparison.Ordinal).Replace("override string Format", "override string Render", StringComparison.Ordinal));
            var consumer = await File.ReadAllTextAsync(consumerPath);
            await File.WriteAllTextAsync(consumerPath, consumer.Replace("baseFormatter.Format(value)", "baseFormatter.Render(value)", StringComparison.Ordinal).Replace("auditFormatter.Format(value)", "auditFormatter.Render(value)", StringComparison.Ordinal).Replace("workerFormatter.Format(value)", "workerFormatter.Render(value)", StringComparison.Ordinal));
            var correctModels = await File.ReadAllTextAsync(modelPath);
            var correctConsumers = await File.ReadAllTextAsync(consumerPath);
            var correct = await RunValidatorAsync(root);
            Assert.True(correct.ExitCode == 0, correct.Output);
            await File.WriteAllTextAsync(modelPath, correctModels.Replace("public override string Render", "public new string Format", StringComparison.Ordinal));
            var incompleteOverride = await RunValidatorAsync(root);
            Assert.NotEqual(0, incompleteOverride.ExitCode);
            Assert.Contains("semantic-oracle: rejected", incompleteOverride.Output);
            await File.WriteAllTextAsync(modelPath, correctModels);
            await File.WriteAllTextAsync(consumerPath, correctConsumers.Replace("auditFormatter.Render(value)", "$\"audit:{value}\"", StringComparison.Ordinal));
            var incompleteDispatch = await RunValidatorAsync(root);
            Assert.NotEqual(0, incompleteDispatch.ExitCode);
            Assert.Contains("semantic-oracle: rejected", incompleteDispatch.Output);
            await File.WriteAllTextAsync(consumerPath, correctConsumers);
            await File.WriteAllTextAsync(modelPath, correctModels.Replace(
                "return $\"audit:{value}\"; } }",
                "return $\"audit:{value}\"; } private string Format(string value) => Render(value); }",
                StringComparison.Ordinal));
            var retainedMember = await RunValidatorAsync(root);
            Assert.NotEqual(0, retainedMember.ExitCode);
            Assert.Contains("semantic-oracle: rejected", retainedMember.Output);
            await File.WriteAllTextAsync(modelPath, correctModels);
            await File.WriteAllTextAsync(modelPath, correctModels.Replace("shadow:{value}", "changed:{value}", StringComparison.Ordinal));
            var hiddenBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, hiddenBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", hiddenBehavior.Output);
            await File.WriteAllTextAsync(modelPath, correctModels.Replace("base:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedBehavior.Output);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Extension_method_oracle_rejects_decoys_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticTaskAsync("semantic-extension-methods", "RenameExtensionFormat.cs", "ExtensionMethodVerifier.csproj");
        try
        {
            var original = await RunValidatorAsync(root); Assert.NotEqual(0, original.ExitCode); Assert.Contains("semantic-oracle: rejected", original.Output);
            var models = Path.Combine(root,"src/Models/Formatters.cs"); var view = Path.Combine(root,"src/Consumers/FormatView.cs");
            var originalModels = await File.ReadAllTextAsync(models);
            await File.WriteAllTextAsync(models, originalModels.Replace("public static string Format(this", "public static string Render(this", StringComparison.Ordinal));
            await File.WriteAllTextAsync(view, (await File.ReadAllTextAsync(view)).Replace("message.Format(value)", "message.Render(value)", StringComparison.Ordinal));
            var correctModels=await File.ReadAllTextAsync(models); var correctView=await File.ReadAllTextAsync(view);
            var correct=await RunValidatorAsync(root); Assert.True(correct.ExitCode==0,correct.Output);
            await File.WriteAllTextAsync(models, correctModels.Replace("public static class StaticFormatter { public static string Format", "public static class StaticFormatter { public static string Render", StringComparison.Ordinal));
            await File.WriteAllTextAsync(view, correctView.Replace("StaticFormatter.Format(value)", "StaticFormatter.Render(value)", StringComparison.Ordinal));
            var wrongTarget=await RunValidatorAsync(root); Assert.NotEqual(0,wrongTarget.ExitCode); Assert.Contains("semantic-oracle: rejected",wrongTarget.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(view, correctView);
            await File.WriteAllTextAsync(models, correctModels.Replace("=> $\"extension:{value}\"; }", "=> $\"extension:{value}\"; private static string Format(Message message, string value) => Render(message, value); }", StringComparison.Ordinal));
            var retained=await RunValidatorAsync(root); Assert.NotEqual(0,retained.ExitCode); Assert.Contains("semantic-oracle: rejected",retained.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(models, correctModels.Replace("=> $\"extension:{value}\"; }", "=> $\"extension:{value}\"; private static string Format(Message message, string value, object? ignored = null) => Render(message, value); }", StringComparison.Ordinal));
            var optionalRetained=await RunValidatorAsync(root); Assert.NotEqual(0,optionalRetained.ExitCode); Assert.Contains("semantic-oracle: rejected",optionalRetained.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(models, correctModels + " public static class CompatibilityExtensions { public static string Format(this Message message, string value) => MessageExtensions.Render(message, value); }");
            var compatibilityExtension=await RunValidatorAsync(root); Assert.NotEqual(0,compatibilityExtension.ExitCode); Assert.Contains("semantic-oracle: rejected",compatibilityExtension.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(models, correctModels.Replace("public sealed class Message;", "public sealed class Message { public string Render(string value) => $\"extension:{value}\"; }", StringComparison.Ordinal));
            var shadowedExtension=await RunValidatorAsync(root); Assert.NotEqual(0,shadowedExtension.ExitCode); Assert.Contains("semantic-oracle: rejected",shadowedExtension.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(models, correctModels.Replace("public sealed class Message;", "public sealed class Message { public string Render(string value, object? ignored = null) => $\"extension:{value}\"; }", StringComparison.Ordinal));
            var optionalShadowedExtension=await RunValidatorAsync(root); Assert.NotEqual(0,optionalShadowedExtension.ExitCode); Assert.Contains("semantic-oracle: rejected",optionalShadowedExtension.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(models, correctModels.Replace("public sealed class Message;", "public sealed class Message { public string Format(string value) => $\"extension:{value}\"; }", StringComparison.Ordinal));
            await File.WriteAllTextAsync(view, correctView.Replace("message.Render(value)", "message.Format(value)", StringComparison.Ordinal));
            var formatShadow=await RunValidatorAsync(root); Assert.NotEqual(0,formatShadow.ExitCode); Assert.Contains("semantic-oracle: rejected",formatShadow.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(view, correctView.Replace("message.Render(value)", "\"extension:\" + value", StringComparison.Ordinal));
            var bypassedCall=await RunValidatorAsync(root); Assert.NotEqual(0,bypassedCall.ExitCode); Assert.Contains("semantic-oracle: rejected",bypassedCall.Output);
            await File.WriteAllTextAsync(view, correctView);
            await File.WriteAllTextAsync(models, correctModels.Replace("public static class MessageExtensions { public static string Render(this Message message, string value) => $\"extension:{value}\"; }", "public static class MessageExtensions { public static string Render(this Message message, string value) {\n#if NET8_0\nreturn $\"changed:{value}\";\n#else\nreturn $\"extension:{value}\";\n#endif\n} }", StringComparison.Ordinal));
            var net8Behavior=await RunValidatorAsync(root); Assert.NotEqual(0,net8Behavior.ExitCode); Assert.Contains("semantic-oracle: rejected",net8Behavior.Output);
            await File.WriteAllTextAsync(models, correctModels);
            await File.WriteAllTextAsync(models, correctModels.Replace("extension:{value}", "changed:{value}", StringComparison.Ordinal));
            var behavior=await RunValidatorAsync(root); Assert.NotEqual(0,behavior.ExitCode); Assert.Contains("semantic-oracle: rejected",behavior.Output);
            await File.WriteAllTextAsync(view, correctView);
        }
        finally { Directory.Delete(root,recursive:true); }
    }

    [Fact]
    public async Task Partial_linked_ownership_oracle_rejects_bad_states_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticTaskAsync(
            "semantic-partial-linked-ownership",
            "RenamePartialLinkedFormat.cs",
            "PartialLinkedOwnershipVerifier.csproj");
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);
            Assert.Contains("semantic-oracle: rejected", original.Output);

            var sharedPath = Path.Combine(root, "src/Shared/LinkedFormatter.Format.cs");
            var corePath = Path.Combine(root, "src/Shared/LinkedFormatter.Core.cs");
            var primaryPath = Path.Combine(root, "src/Primary/PrimaryView.cs");
            var secondaryPath = Path.Combine(root, "src/Secondary/SecondaryView.cs");
            var originalShared = await File.ReadAllTextAsync(sharedPath);
            var originalCore = await File.ReadAllTextAsync(corePath);
            var originalPrimary = await File.ReadAllTextAsync(primaryPath);
            var originalSecondary = await File.ReadAllTextAsync(secondaryPath);

            await File.WriteAllTextAsync(sharedPath, originalShared.Replace(
                "public string Format(string", "public string Render(string", StringComparison.Ordinal));
            await File.WriteAllTextAsync(primaryPath, originalPrimary.Replace(
                "formatter.Format(value)", "formatter.Render(value)", StringComparison.Ordinal));
            await File.WriteAllTextAsync(secondaryPath, originalSecondary.Replace(
                "formatter.Format(value)", "formatter.Render(value)", StringComparison.Ordinal));
            var correctShared = await File.ReadAllTextAsync(sharedPath);
            var correctPrimary = await File.ReadAllTextAsync(primaryPath);
            var correctSecondary = await File.ReadAllTextAsync(secondaryPath);
            var correct = await RunValidatorAsync(root);
            Assert.True(correct.ExitCode == 0, correct.Output);
            Assert.Contains("semantic-oracle: verified", correct.Output);

            await File.WriteAllTextAsync(sharedPath, """
                namespace SemanticPartialLinked;
                public sealed class LinkedFormatter
                {
                    private int callCount;
                    public int CallCount => callCount;
                    private string Record(string value) => $"linked:{value}:{++callCount}";
                    public string Format(int value) => $"number:{value}";
                    public string Render(string value) => Record(value);
                }
                """);
            await File.WriteAllTextAsync(corePath, "namespace SemanticPartialLinked;");
            var removedPartial = await RunValidatorAsync(root);
            Assert.NotEqual(0, removedPartial.ExitCode);
            Assert.Contains("semantic-oracle: rejected", removedPartial.Output);
            await File.WriteAllTextAsync(sharedPath, correctShared);
            await File.WriteAllTextAsync(corePath, originalCore);

            await File.WriteAllTextAsync(sharedPath, "// public sealed partial class LinkedFormatter");
            await File.WriteAllTextAsync(primaryPath, """
                using SemanticPartialLinked;
                namespace SemanticPartialLinked.Primary
                {
                    public sealed class PrimaryView
                    {
                        public string Create(LinkedFormatter formatter, string value) => $"primary:{formatter.Render(value)}|{formatter.Format(7)}";
                    }
                }
                namespace SemanticPartialLinked
                {
                    public sealed partial class LinkedFormatter
                    {
                        public string Render(string value) => Record(value);
                    }
                }
                """);
            await File.WriteAllTextAsync(secondaryPath, """
                using SemanticPartialLinked;
                namespace SemanticPartialLinked.Secondary
                {
                    public sealed class SecondaryView
                    {
                        public string Create(LinkedFormatter formatter, string value) => $"secondary:{formatter.Render(value)}|{formatter.Format(7)}";
                    }
                }
                namespace SemanticPartialLinked
                {
                    public sealed partial class LinkedFormatter
                    {
                        public string Render(string value) => Record(value);
                    }
                }
                """);
            var relocatedRender = await RunValidatorAsync(root);
            Assert.NotEqual(0, relocatedRender.ExitCode);
            Assert.Contains("semantic-oracle: rejected", relocatedRender.Output);
            await File.WriteAllTextAsync(sharedPath, correctShared);
            await File.WriteAllTextAsync(primaryPath, correctPrimary);
            await File.WriteAllTextAsync(secondaryPath, correctSecondary);

            await File.WriteAllTextAsync(corePath, originalCore.Replace(
                "public string Format(int", "public string Render(int", StringComparison.Ordinal));
            await File.WriteAllTextAsync(primaryPath, correctPrimary.Replace(
                "formatter.Format(7)", "formatter.Render(7)", StringComparison.Ordinal));
            await File.WriteAllTextAsync(secondaryPath, correctSecondary.Replace(
                "formatter.Format(7)", "formatter.Render(7)", StringComparison.Ordinal));
            var wrongTarget = await RunValidatorAsync(root);
            Assert.NotEqual(0, wrongTarget.ExitCode);
            Assert.Contains("semantic-oracle: rejected", wrongTarget.Output);
            await File.WriteAllTextAsync(corePath, originalCore);
            await File.WriteAllTextAsync(primaryPath, correctPrimary);
            await File.WriteAllTextAsync(secondaryPath, correctSecondary);

            await File.WriteAllTextAsync(sharedPath, correctShared.Replace(
                "=> Record(value);\n}",
                "=> Record(value);\n    public string Format(string value) => Render(value);\n}",
                StringComparison.Ordinal));
            await File.WriteAllTextAsync(secondaryPath, correctSecondary.Replace(
                "formatter.Render(value)", "formatter.Format(value)", StringComparison.Ordinal));
            var incompleteOwner = await RunValidatorAsync(root);
            Assert.NotEqual(0, incompleteOwner.ExitCode);
            Assert.Contains("semantic-oracle: rejected", incompleteOwner.Output);
            await File.WriteAllTextAsync(sharedPath, correctShared);
            await File.WriteAllTextAsync(secondaryPath, correctSecondary);

            await File.WriteAllTextAsync(primaryPath, correctPrimary.Replace(
                "formatter.Render(value)", "\"linked:\" + value + \":1\"", StringComparison.Ordinal));
            var bypassedCall = await RunValidatorAsync(root);
            Assert.NotEqual(0, bypassedCall.ExitCode);
            Assert.Contains("semantic-oracle: rejected", bypassedCall.Output);
            await File.WriteAllTextAsync(primaryPath, correctPrimary);

            await File.WriteAllTextAsync(corePath, originalCore.Replace(
                "number:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedOverload = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedOverload.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedOverload.Output);
            await File.WriteAllTextAsync(corePath, originalCore.Replace(
                "linked:{value}:{++callCount}", "changed:{value}", StringComparison.Ordinal));
            var changedBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedBehavior.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Multi_target_conditional_oracle_rejects_bad_states_and_accepts_exact_change()
    {
        var root = await MaterializeSemanticTaskAsync(
            "semantic-multitarget-conditional",
            "RenameConditionalFormat.cs",
            "MultiTargetConditionalVerifier.csproj");
        try
        {
            var original = await RunValidatorAsync(root);
            Assert.NotEqual(0, original.ExitCode);
            Assert.Contains("semantic-oracle: rejected", original.Output);
            var modelPath = Path.Combine(root, "src/Models/ConditionalFormatter.cs");
            var viewPath = Path.Combine(root, "src/Consumers/ConditionalView.cs");
            var originalModel = await File.ReadAllTextAsync(modelPath);
            var originalView = await File.ReadAllTextAsync(viewPath);
            await File.WriteAllTextAsync(modelPath, originalModel.Replace("Format(string", "Render(string", StringComparison.Ordinal));
            await File.WriteAllTextAsync(viewPath, originalView.Replace("formatter.Format(value)", "formatter.Render(value)", StringComparison.Ordinal));
            var correctModel = await File.ReadAllTextAsync(modelPath);
            var correctView = await File.ReadAllTextAsync(viewPath);
            var correct = await RunValidatorAsync(root);
            Assert.True(correct.ExitCode == 0, correct.Output);

            await File.WriteAllTextAsync(modelPath, correctModel.Replace("public string Format(int", "public string Render(int", StringComparison.Ordinal));
            await File.WriteAllTextAsync(viewPath, correctView.Replace("formatter.Format(7)", "formatter.Render(7)", StringComparison.Ordinal));
            var wrongTarget = await RunValidatorAsync(root);
            Assert.NotEqual(0, wrongTarget.ExitCode);
            Assert.Contains("semantic-oracle: rejected", wrongTarget.Output);
            await File.WriteAllTextAsync(modelPath, correctModel);
            await File.WriteAllTextAsync(viewPath, correctView);

            await File.WriteAllTextAsync(modelPath, correctModel.Replace("#else\n    public string Render", "#else\n    public string Format", StringComparison.Ordinal));
            await File.WriteAllTextAsync(viewPath,
                "using SemanticMultiTargetConditional.Models;\nnamespace SemanticMultiTargetConditional.Consumers;\npublic sealed class ConditionalView\n{\n    public string Create(ConditionalFormatter formatter, string value)\n    {\n#if NET8_0\n        return $\"{formatter.Render(value)}|{formatter.Format(7)}\";\n#else\n        return $\"{formatter.Format(value)}|{formatter.Format(7)}\";\n#endif\n    }\n}\n");
            var incompleteFramework = await RunValidatorAsync(root);
            Assert.NotEqual(0, incompleteFramework.ExitCode);
            Assert.Contains("semantic-oracle: rejected", incompleteFramework.Output);
            await File.WriteAllTextAsync(modelPath, correctModel);
            await File.WriteAllTextAsync(viewPath, correctView);

            await File.WriteAllTextAsync(viewPath,
                "using SemanticMultiTargetConditional.Models;\nnamespace SemanticMultiTargetConditional.Consumers;\npublic sealed class ConditionalView\n{\n    public string Create(ConditionalFormatter formatter, string value)\n    {\n#if NET8_0\n        return $\"legacy:{value}|{formatter.Format(7)}\";\n#else\n        return $\"modern:{value}|{formatter.Format(7)}\";\n#endif\n    }\n}\n");
            var bypassedCallSite = await RunValidatorAsync(root);
            Assert.NotEqual(0, bypassedCallSite.ExitCode);
            Assert.Contains("semantic-oracle: rejected", bypassedCallSite.Output);
            await File.WriteAllTextAsync(viewPath, correctView);

            await File.WriteAllTextAsync(modelPath, correctModel.Replace("number:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedNumericBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedNumericBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedNumericBehavior.Output);
            await File.WriteAllTextAsync(modelPath, correctModel);

            await File.WriteAllTextAsync(viewPath, correctView.Replace("formatter.Format(7)", "\"number:7\"", StringComparison.Ordinal));
            var bypassedNumericCallSite = await RunValidatorAsync(root);
            Assert.NotEqual(0, bypassedNumericCallSite.ExitCode);
            Assert.Contains("semantic-oracle: rejected", bypassedNumericCallSite.Output);
            await File.WriteAllTextAsync(viewPath, correctView);

            await File.WriteAllTextAsync(modelPath, correctModel.Replace("#endif\n    public string Format(int", "#endif\n    public string Format(string value) => Render(value);\n    public string Format(int", StringComparison.Ordinal));
            var retainedMember = await RunValidatorAsync(root);
            Assert.NotEqual(0, retainedMember.ExitCode);
            Assert.Contains("semantic-oracle: rejected", retainedMember.Output);
            await File.WriteAllTextAsync(modelPath, correctModel);

            await File.WriteAllTextAsync(modelPath, correctModel.Replace("modern:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedConditionalBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedConditionalBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedConditionalBehavior.Output);
            await File.WriteAllTextAsync(modelPath, correctModel.Replace("legacy:{value}", "changed:{value}", StringComparison.Ordinal));
            var changedLegacyBehavior = await RunValidatorAsync(root);
            Assert.NotEqual(0, changedLegacyBehavior.ExitCode);
            Assert.Contains("semantic-oracle: rejected", changedLegacyBehavior.Output);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Event_metrics_count_completed_dnaxi_and_raw_read_commands_deterministically()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-agent-events-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var eventsPath = Path.Combine(root, "events.jsonl");
            await File.WriteAllLinesAsync(
                eventsPath,
                [
                    "{\"type\":\"turn.started\"}",
                    "{\"type\":\"item.started\",\"item\":{\"id\":\"read-1\",\"type\":\"command_execution\",\"command\":\"cat src/A.cs\"}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"read-1\",\"type\":\"command_execution\",\"command\":\"cat src/A.cs\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"read-2\",\"type\":\"command_execution\",\"command\":\"/usr/bin/cat src/B.cs\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"read-3\",\"type\":\"command_execution\",\"command\":\"rg Render src\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"read-win-rg\",\"type\":\"command_execution\",\"command\":\"C:\\\\Tools\\\\rg.exe Render src\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"read-win-git\",\"type\":\"command_execution\",\"command\":\"\\\"C:\\\\Program Files\\\\Git\\\\bin\\\\git.exe\\\" show HEAD:file\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"read-gci\",\"type\":\"command_execution\",\"command\":\"Get-ChildItem src\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"read-select\",\"type\":\"command_execution\",\"command\":\"Select-String Render src\\\\A.cs\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"raw-mention\",\"type\":\"command_execution\",\"command\":\"echo cat\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"dnaxi-1\",\"type\":\"command_execution\",\"command\":\"dnx dnaxi@0.6.0 --source feed -- search references X\",\"exit_code\":1}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"dnaxi-2\",\"type\":\"command_execution\",\"command\":\"dnaxi search implementations X\",\"exit_code\":0}}",
                    "{\"type\":\"item.started\",\"item\":{\"id\":\"dnaxi-3\",\"type\":\"command_execution\",\"command\":\"cd repo && dnx dnaxi@0.6.0 search references X\"}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"dnaxi-win\",\"type\":\"command_execution\",\"command\":\"C:\\\\Tools\\\\dnx.exe \\\"dnaxi@0.6.0\\\" search references X\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"wrong-version\",\"type\":\"command_execution\",\"command\":\"dnx dnaxi@0.5.0 search references X\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"dnaxi-argument\",\"type\":\"command_execution\",\"command\":\"rg dnaxi SKILL.md\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"dnaxi-prose\",\"type\":\"command_execution\",\"command\":\"echo dnx dnaxi@0.6.0 search references X\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"quoted-raw-prose\",\"type\":\"command_execution\",\"command\":\"echo \\\"note && cat src/C.cs\\\"\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"quoted-dnaxi-prose\",\"type\":\"command_execution\",\"command\":\"echo \\\"note; dnx dnaxi@0.6.0 search references X\\\"\",\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"argv-prose\",\"type\":\"command_execution\",\"command\":[\"echo\",\"&&\",\"cat\",\"src/C.cs\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"argv-git\",\"type\":\"command_execution\",\"command\":[\"C:\\\\Program Files\\\\Git\\\\bin\\\\git.exe\",\"show\",\"HEAD:file\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"argv-dnaxi\",\"type\":\"command_execution\",\"command\":[\"C:\\\\Tools\\\\dnx.exe\",\"dnaxi@0.6.0\",\"search\",\"references\",\"X\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-raw\",\"type\":\"command_execution\",\"command\":[\"bash\",\"-c\",\"cat src/D.cs\",\"ignored\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-raw-no-switch\",\"type\":\"command_execution\",\"command\":[\"bash\",\"script.sh\",\"cat src/D.cs\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-raw-lc\",\"type\":\"command_execution\",\"command\":[\"bash\",\"-lc\",\"rg Render src\",\"ignored\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-raw-uppercase-switch\",\"type\":\"command_execution\",\"command\":[\"bash\",\"-C\",\"cat src/D.cs\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-raw-script-argument\",\"type\":\"command_execution\",\"command\":[\"bash\",\"script.sh\",\"-c\",\"cat src/D.cs\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-dnaxi\",\"type\":\"command_execution\",\"command\":[\"pwsh\",\"-Command\",\"dnx dnaxi@0.6.0 search references X\",\"ignored\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-dnaxi-pwsh-c\",\"type\":\"command_execution\",\"command\":[\"pwsh\",\"-c\",\"dnx dnaxi@0.6.0 search references X\",\"ignored\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-dnaxi-cmd\",\"type\":\"command_execution\",\"command\":[\"cmd\",\"/c\",\"dnx dnaxi@0.6.0 search references X\",\"ignored\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-dnaxi-no-switch\",\"type\":\"command_execution\",\"command\":[\"pwsh\",\"script.ps1\",\"dnx dnaxi@0.6.0 search references X\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell-dnaxi-script-argument\",\"type\":\"command_execution\",\"command\":[\"pwsh\",\"script.ps1\",\"-Command\",\"dnx dnaxi@0.6.0 search references X\"],\"exit_code\":0}}",
                    "{\"type\":\"item.completed\",\"item\":{\"id\":\"message\",\"type\":\"agent_message\",\"text\":\"cat and dnaxi are prose only\"}}",
                    "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":10,\"cached_input_tokens\":3,\"cache_write_input_tokens\":1,\"output_tokens\":4,\"reasoning_output_tokens\":2}}",
                ]);
            var startInfo = new ProcessStartInfo
            {
                FileName = "jq",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[]
                     {
                         "-s", "--arg", "version", "0.6.0", "-f",
                         Path.Combine(RepositoryRoot(), "eng", "benchmark-agent-events.jq"),
                         eventsPath,
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await error);
            using var metrics = JsonDocument.Parse(await output);
            var rootElement = metrics.RootElement;
            Assert.Equal(11, rootElement.GetProperty(
                "rawRepositoryReadCommandCount").GetInt32());
            Assert.Equal(8, rootElement.GetProperty(
                "dnaxiInvocations").GetInt32());
            Assert.Equal(6, rootElement.GetProperty(
                "dnaxiSuccessfulInvocations").GetInt32());
            Assert.Equal(1, rootElement.GetProperty(
                "dnaxiNonzeroExits").GetInt32());
            Assert.Equal(30, rootElement.GetProperty("toolCalls").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Harness_retains_the_diff_and_blocks_dnaxi_from_the_baseline()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(),
            "eng",
            "benchmark-agent.ps1"));

        Assert.Contains("'changes.patch'", script, StringComparison.Ordinal);
        Assert.Contains(
            "@('dnx', 'dnaxi', 'dotnet-dnaxi')",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "[IO.Path]::PathSeparator",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "[string]$ProductVersion = '0.6.0'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "--write-semantic-relationships $ProductVersion",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "--output-root $candidateSkillRoot",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "candidate-skill",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "'dnaxi@0.5.0',",
            script,
            StringComparison.Ordinal);
        Assert.Contains("fixtureHash = $fixtureHash", script);
        Assert.Contains("semanticOracleOutcome = $semanticOracleOutcome", script);
        Assert.Contains("recoveredDnaxiFailure = $recoveredDnaxiFailure", script);
        Assert.Contains("rawRepositoryReadCommandCount", script);
        Assert.Contains(
            "[Collections.Generic.SortedSet[string]]::new(",
            script);
        Assert.Contains(
            "[Collections.Generic.HashSet[string]]::new(",
            script);
        Assert.Contains("[StringComparer]::Ordinal", script);
        Assert.Contains("$allowedChangeSet.Contains([string]$_)", script);
    }

    private static async Task<string> MaterializeSemanticRelationshipTaskAsync()
        => await MaterializeSemanticTaskAsync(
            "semantic-relationships",
            "RenameLedgerFormatContract.cs");

    private static async Task<string> MaterializeSemanticOverloadTaskAsync()
        => await MaterializeSemanticTaskAsync(
            "semantic-overload-relationships",
            "RenameReportFormatStringOverload.cs");

    private static async Task<string> MaterializeSemanticTaskAsync(
        string fixtureName,
        string validatorName,
        string verifierName = "SemanticRelationshipVerifier.csproj")
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-semantic-task-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var fixtureDirectory = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Fixtures",
            "AgentTasks",
            fixtureName);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(fixtureDirectory, "fixture.json")));
        foreach (var file in manifest.RootElement.GetProperty("files")
                     .EnumerateArray())
        {
            var destination = Path.Combine(
                root,
                file.GetProperty("path").GetString()!);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(
                Path.Combine(
                    fixtureDirectory,
                    file.GetProperty("template").GetString()!),
                destination);
        }

        var validationDirectory = Path.Combine(root, ".benchmark-validation");
        Directory.CreateDirectory(validationDirectory);
        var validators = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Fixtures",
            "AgentTasks",
            "repository-work",
            "validators");
        File.Copy(
            Path.Combine(validators, verifierName),
            Path.Combine(validationDirectory, "Verifier.csproj"));
        File.Copy(
            Path.Combine(validators, "Validate.ps1"),
            Path.Combine(validationDirectory, "Validate.ps1"));
        File.Copy(
            Path.Combine(validators, validatorName),
            Path.Combine(validationDirectory, "Program.cs"));
        return root;
    }

    private static async Task<ScriptResult> RunValidatorAsync(string root)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
                     ".benchmark-validation/Validate.ps1",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ScriptResult(process.ExitCode, await output + await error);
    }

    private static void AddForwarder(string path, string member)
    {
        var content = File.ReadAllText(path);
        var closingBrace = content.LastIndexOf('}');
        Assert.True(closingBrace >= 0, $"No closing brace in '{path}'.");
        File.WriteAllText(
            path,
            content.Insert(closingBrace, $"    {member}\n"));
    }

    private static async Task<ScriptResult> RunAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(
            RepositoryRoot(),
            "eng",
            "benchmark-agent.ps1"));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start pwsh.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ScriptResult(
            process.ExitCode,
            await standardOutput + await standardError);
    }

    private static string RepositoryRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed record ScriptResult(int ExitCode, string Output);
}
