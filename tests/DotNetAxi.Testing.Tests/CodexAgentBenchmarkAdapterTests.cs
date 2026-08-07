using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotNetAxi.Testing.Tests;

public sealed class CodexAgentBenchmarkAdapterTests
{
    [Fact]
    public void Invocation_is_ephemeral_machine_readable_and_condition_isolated()
    {
        using var workspace = new TemporaryWorkspace();
        var adapter = Adapter();
        var baseline = adapter.CreateStartInfo(
            Input(workspace.Path, AgentBenchmarkCondition.Baseline));
        var candidate = adapter.CreateStartInfo(
            Input(workspace.Path, AgentBenchmarkCondition.Candidate));
        var baselineArguments = baseline.ArgumentList.ToArray();
        var candidateArguments = candidate.ArgumentList.ToArray();

        Assert.Equal(ProcessApplicationPath(), baseline.FileName);
        Assert.False(baseline.UseShellExecute);
        Assert.Equal(workspace.Path, baseline.WorkingDirectory);
        Assert.Contains("exec", baselineArguments);
        Assert.Contains("--ephemeral", baselineArguments);
        Assert.Contains("--json", baselineArguments);
        Assert.Contains("--ignore-user-config", baselineArguments);
        Assert.Contains("--ignore-rules", baselineArguments);
        Assert.Contains("--skip-git-repo-check", baselineArguments);
        AssertOption(baselineArguments, "--model", "gpt-5.6-codex");
        AssertOption(baselineArguments, "--cd", workspace.Path);
        AssertOption(baselineArguments, "--sandbox", "read-only");
        Assert.Contains("model_reasoning_effort=\"high\"", baselineArguments);
        Assert.Contains("approval_policy=\"never\"", baselineArguments);
        Assert.Contains("web_search=\"disabled\"", baselineArguments);
        Assert.Contains(
            "sandbox_workspace_write.network_access=false",
            baselineArguments);
        Assert.DoesNotContain("--full-auto", baselineArguments);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox",
            baselineArguments);
        Assert.DoesNotContain("--yolo", baselineArguments);
        Assert.Equal("C", baseline.Environment["LC_ALL"]);
        Assert.Equal("UTC", baseline.Environment["TZ"]);
        Assert.False(baseline.Environment.ContainsKey("HTTPS_PROXY"));

        var baselineExposure = baselineArguments
            .Where(IsExposureOverride)
            .ToArray();
        var candidateExposure = candidateArguments
            .Where(IsExposureOverride)
            .ToArray();
        Assert.Equal(
            ["skills.config=[]", "mcp_servers.dnaxi.enabled=false"],
            baselineExposure);
        Assert.Equal(
            [
                "skills.config=[{path=\"skills/dotnet-axi\"}]",
                "mcp_servers.dnaxi.enabled=true",
            ],
            candidateExposure);
        Assert.Equal(
            baselineArguments.Where(static value => !IsExposureOverride(value)),
            candidateArguments.Where(static value => !IsExposureOverride(value)));
    }

    [Fact]
    public async Task Success_captures_usage_answer_commands_changes_scope_and_exit()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Input(
            workspace.Path,
            AgentBenchmarkCondition.Candidate,
            sandbox: "workspace-write",
            permittedTools: ["source-search", "workspace-write"],
            fixture: "success.jsonl");
        await using var execution = await Adapter().StartAsync(input);

        var result = await execution.Completion.WaitAsync(
            TimeSpan.FromSeconds(5));
        await execution.StopAsync();

        Assert.Equal("completed", result.Status);
        Assert.Equal("src/Discovery/Handlers/AuditHandler.cs:5", result.Answer);
        Assert.Equal(121, result.InputTokens);
        Assert.Equal(17, result.OutputTokens);
        Assert.Equal(1, result.Turns);
        Assert.Collection(
            result.ToolCalls,
            command =>
            {
                Assert.Equal("source-search", command.ToolClass);
                Assert.True(command.Succeeded);
            },
            change =>
            {
                Assert.Equal("workspace-write", change.ToolClass);
                Assert.True(change.Succeeded);
            });
        Assert.Contains(
            "src/Discovery/Handlers/AuditHandler.cs",
            result.InspectedScope.Files);
        Assert.Contains(
            "src/Discovery/Generated.cs",
            result.InspectedScope.Files);
        Assert.Contains(
            "src/Discovery/Discovery.csproj",
            result.InspectedScope.Projects);
        Assert.False(result.NetworkUsed);
        Assert.Equal("workspace-write", result.ObservedConfiguration.Sandbox);
        Assert.Contains(
            result.RawEvents,
            static value => value.Kind == "adapter.process.started");
        var exited = Assert.Single(
            result.RawEvents,
            static value => value.Kind == "adapter.process.exited");
        using var exitPayload = JsonDocument.Parse(exited.Payload);
        Assert.Equal(0, exitPayload.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task Read_only_shell_fallback_and_globs_preserve_scope()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Input(
            workspace.Path,
            AgentBenchmarkCondition.Baseline,
            fixture: "read-only-shell.jsonl");
        await using var execution = await Adapter().StartAsync(input);

        var result = await execution.Completion.WaitAsync(
            TimeSpan.FromSeconds(5));
        await execution.StopAsync();

        Assert.Equal("completed", result.Status);
        Assert.Collection(
            result.ToolCalls,
            search => Assert.Equal("source-search", search.ToolClass),
            fallback => Assert.Equal("repository-read", fallback.ToolClass));
        Assert.Equal(
            ["src/Discovery/Cases/InvocationCases.cs"],
            result.InspectedScope.Files);
    }

    [Theory]
    [InlineData("permission-denied.jsonl", "emit", "1", "permission-denied", false, "error")]
    [InlineData("read-only.jsonl", "emit", "1", "permission-denied", false, "turn.failed")]
    [InlineData("network-denied.jsonl", "emit", "1", "permission-denied", true, "turn.failed")]
    [InlineData("malformed.jsonl", "emit", "0", "failed", false, "codex.malformed")]
    [InlineData("truncated.jsonl", "truncate", "0", "failed", false, "codex.truncated")]
    [InlineData("duplicate-completion.jsonl", "emit", "0", "failed", false, "turn.completed")]
    [InlineData("completion-before-thread.jsonl", "emit", "0", "failed", false, "turn.completed")]
    [InlineData("completion-before-start.jsonl", "emit", "0", "failed", false, "turn.completed")]
    [InlineData("item-after-completion.jsonl", "emit", "0", "failed", false, "item.completed")]
    [InlineData("scope-outside.jsonl", "emit", "0", "failed", false, "item.completed")]
    public async Task Failure_contracts_preserve_raw_evidence(
        string fixture,
        string behavior,
        string exitCode,
        string expectedStatus,
        bool expectedNetworkUsed,
        string expectedEvent)
    {
        using var workspace = new TemporaryWorkspace();
        var input = Input(
            workspace.Path,
            AgentBenchmarkCondition.Baseline,
            fixture: fixture,
            behavior: behavior,
            exitCode: exitCode);
        await using var execution = await Adapter().StartAsync(input);

        var result = await execution.Completion.WaitAsync(
            TimeSpan.FromSeconds(5));
        await execution.StopAsync();

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedNetworkUsed, result.NetworkUsed);
        Assert.Contains(
            result.RawEvents,
            value => value.Kind == expectedEvent);
        Assert.Equal(
            Enumerable.Range(0, result.RawEvents.Count),
            result.RawEvents.Select(static value => value.Sequence));
        Assert.All(result.RawEvents, static value =>
            Assert.Equal(Hash(value.Payload), value.PayloadHash));
    }

    [Fact]
    public async Task Scope_capture_normalizes_bare_rooted_and_quoted_paths()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Input(
            workspace.Path,
            AgentBenchmarkCondition.Baseline,
            fixture: "scope-paths.jsonl");
        await using var execution = await Adapter().StartAsync(input);

        var result = await execution.Completion.WaitAsync(
            TimeSpan.FromSeconds(5));
        await execution.StopAsync();

        Assert.Equal("completed", result.Status);
        Assert.Equal(
            [
                "Nested/Absolute.cs",
                "Program.cs",
                "src/My Folder/Quoted.cs",
            ],
            result.InspectedScope.Files);
    }

    [Fact]
    public async Task Launcher_stderr_permission_denial_is_normalized()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Input(
            workspace.Path,
            AgentBenchmarkCondition.Baseline,
            behavior: "stderr-denied",
            exitCode: "1");
        await using var execution = await Adapter().StartAsync(input);

        var result = await execution.Completion.WaitAsync(
            TimeSpan.FromSeconds(5));
        await execution.StopAsync();

        Assert.Equal("permission-denied", result.Status);
        Assert.Contains(
            result.RawEvents,
            static value => value.Kind == "codex.stderr");
    }

    [Theory]
    [InlineData("silent-live.jsonl")]
    [InlineData(null)]
    public async Task Event_silence_keeps_one_live_identity_until_bounded_stop(
        string? fixture)
    {
        using var workspace = new TemporaryWorkspace();
        var input = Input(
            workspace.Path,
            AgentBenchmarkCondition.Baseline,
            fixture: fixture,
            behavior: "hang");
        await using var execution = await Adapter().StartAsync(input);
        await WaitUntilAsync(
            () => execution.GetProgressSnapshot().RawEvents.Count
                  >= (fixture is null ? 1 : 2));
        var progress = execution.GetProgressSnapshot();
        var started = Assert.Single(
            progress.RawEvents,
            static value => value.Kind == "adapter.process.started");
        using var payload = JsonDocument.Parse(started.Payload);
        var processId = payload.RootElement.GetProperty("processId").GetInt32();

        await Assert.ThrowsAsync<TimeoutException>(
            () => execution.Completion.WaitAsync(
                TimeSpan.FromMilliseconds(100)));
        Assert.Single(
            execution.GetProgressSnapshot().RawEvents,
            static value => value.Kind == "adapter.process.started");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await execution.StopAsync(stop.Token);
        await execution.DisposeAsync();
        AssertProcessExited(processId);
    }

    [Fact]
    public void Write_capable_tasks_require_the_declared_workspace_sandbox()
    {
        using var workspace = new TemporaryWorkspace();
        var adapter = Adapter();
        var mismatched = Input(
            workspace.Path,
            AgentBenchmarkCondition.Baseline,
            sandbox: "read-only",
            permittedTools: ["workspace-write"]);

        var exception = Assert.Throws<AgentBenchmarkStartException>(
            () => adapter.StartAsync(mismatched).AsTask().GetAwaiter().GetResult());
        Assert.False(exception.Retryable);
    }

    private static CodexAgentBenchmarkAdapter Adapter()
    {
        var baselineInstructions = Hash("baseline-instructions");
        var baselineTools = Hash("baseline-tools");
        var candidateInstructions = Hash("candidate-instructions");
        var candidateTools = Hash("candidate-tools");
        return new CodexAgentBenchmarkAdapter(
            new CodexAgentBenchmarkAdapterOptions(
                ProcessApplicationPath(),
                "codex-cli-0.84.0",
                new CodexBenchmarkConditionExposure(
                    AgentBenchmarkCondition.Baseline,
                    baselineInstructions,
                    baselineTools,
                    [
                        "skills.config=[]",
                        "mcp_servers.dnaxi.enabled=false",
                    ]),
                new CodexBenchmarkConditionExposure(
                    AgentBenchmarkCondition.Candidate,
                    candidateInstructions,
                    candidateTools,
                    [
                        "skills.config=[{path=\"skills/dotnet-axi\"}]",
                        "mcp_servers.dnaxi.enabled=true",
                    ]),
                ["codex-fixture"]));
    }

    private static AgentBenchmarkAdapterInput Input(
        string workspace,
        AgentBenchmarkCondition condition,
        string sandbox = "read-only",
        IReadOnlyList<string>? permittedTools = null,
        string? fixture = null,
        string behavior = "emit",
        string exitCode = "0")
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CODEX_FIXTURE_BEHAVIOR"] = behavior,
            ["CODEX_FIXTURE_EXIT_CODE"] = exitCode,
            ["HTTPS_PROXY"] = "http://must-not-be-inherited.invalid",
        };
        if (fixture is not null)
        {
            environment["CODEX_FIXTURE_PATH"] = FixturePath(fixture);
        }

        var task = new AgentTaskDefinition(
            "codex-contract",
            "0.3.0",
            [],
            "Return only the exact requested facts.",
            new AgentTaskRepositoryState(
                "fixture.json",
                "codex-contract",
                60,
                Hash("fixture-content"),
                "materialized-clean"),
            new AgentTaskApplicability(true, true),
            new AgentTaskExecutionPolicy(
                permittedTools ?? ["repository-read", "source-search"],
                1,
                "disabled",
                "invariant",
                "UTC"),
            new AgentTaskSuccessOracle(
                "exact-fact-set",
                "ordinal-lines/v1",
                ["src/Discovery/Handlers/AuditHandler.cs:5"],
                null),
            new AgentTaskSafetyOracle(
                "all",
                ["claims-supported", "network-unused", "workspace-unchanged"]),
            ["fixture-content-hash", "safety-oracle", "success-oracle"]);
        var execution = new AgentBenchmarkExecutionSettings(
            "codex-cli-0.84.0",
            "gpt-5.6-codex",
            "high",
            Hash("settings"),
            sandbox,
            "never",
            "disabled");
        return new AgentBenchmarkAdapterInput(
            "codex-contract/000001",
            1,
            0,
            1,
            condition,
            task,
            workspace,
            environment,
            execution,
            Hash(task.Prompt),
            condition == AgentBenchmarkCondition.Baseline
                ? Hash("baseline-instructions")
                : Hash("candidate-instructions"),
            condition == AgentBenchmarkCondition.Baseline
                ? Hash("baseline-tools")
                : Hash("candidate-tools"));
    }

    private static bool IsExposureOverride(string value) =>
        value.StartsWith("skills.", StringComparison.Ordinal)
        || value.StartsWith("mcp_servers.", StringComparison.Ordinal);

    private static void AssertOption(
        IReadOnlyList<string> arguments,
        string option,
        string value)
    {
        var index = Array.IndexOf(arguments.ToArray(), option);
        Assert.True(index >= 0 && index + 1 < arguments.Count);
        Assert.Equal(value, arguments[index + 1]);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static void AssertProcessExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException)
        {
        }
    }

    private static string FixturePath(string fileName) => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "CodexAdapter",
        fileName);

    private static string ProcessApplicationPath()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var executable = OperatingSystem.IsWindows()
            ? "DotNetAxi.DotNet.ProcessTestApp.exe"
            : "DotNetAxi.DotNet.ProcessTestApp";
        return Path.Combine(
            RepositoryRoot(),
            "tests",
            "DotNetAxi.DotNet.ProcessTestApp",
            "bin",
            configuration,
            "net10.0",
            executable);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotnet-axi-codex-adapter-tests",
                $"{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
