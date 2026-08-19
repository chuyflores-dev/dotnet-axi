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

        Assert.Equal(3, tasks.Length);
        Assert.Equal(
            ["refactor", "feature", "refactor"],
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
            "semantic-relationships");
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
            Path.Combine(validators, "SemanticRelationshipVerifier.csproj"),
            Path.Combine(validationDirectory, "Verifier.csproj"));
        File.Copy(
            Path.Combine(validators, "Validate.ps1"),
            Path.Combine(validationDirectory, "Validate.ps1"));
        File.Copy(
            Path.Combine(validators, "RenameLedgerFormatContract.cs"),
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
