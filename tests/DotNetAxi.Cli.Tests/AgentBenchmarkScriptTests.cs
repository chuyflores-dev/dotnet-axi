using System.Diagnostics;
using System.Text.Json;

namespace DotNetAxi.Cli.Tests;

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

        Assert.Equal(2, tasks.Length);
        Assert.Equal(
            ["refactor", "feature"],
            tasks.Select(static task =>
                task.GetProperty("kind").GetString()!).ToArray());
        var expectedChanges = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["refactor-owned-scope-probe"] = "src/Worker/ScopeProbe.cs",
            ["add-ledger-try-format"] = "src/Core/LedgerService.cs",
        };
        Assert.All(
            tasks,
            task =>
            {
                Assert.Equal(
                    expectedChanges[task.GetProperty("id").GetString()!],
                    task.GetProperty("allowedChanges")[0].GetString());
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
