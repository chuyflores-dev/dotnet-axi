using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetAxi.Testing;

return await CodexSmokeProgram.RunAsync(args);

internal static class CodexSmokeProgram
{
    private const string RequestSchema =
        "dotnet-axi/codex-adapter-smoke-request/v1";
    private const string EvidenceSchema =
        "dotnet-axi/codex-adapter-smoke-evidence/v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4
            || args[0] != "--manifest"
            || args[2] != "--output")
        {
            Console.Error.WriteLine(
                "Usage: dotnet run --project tests/DotNetAxi.CodexSmoke -- --manifest /absolute/request.json --output /absolute/new-evidence.json");
            return 64;
        }

        var manifestPath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[3]);
        if (!Path.IsPathFullyQualified(args[1])
            || !Path.IsPathFullyQualified(args[3])
            || File.Exists(outputPath)
            || !Directory.Exists(Path.GetDirectoryName(outputPath)))
        {
            Console.Error.WriteLine(
                "The manifest and new evidence paths must be absolute; the evidence parent must exist and the evidence file must not.");
            return 64;
        }

        CodexSmokeRequest request;
        await using (var manifest = File.OpenRead(manifestPath))
        {
            request = await JsonSerializer.DeserializeAsync<CodexSmokeRequest>(
                    manifest,
                    JsonOptions)
                ?? throw new InvalidDataException("The smoke request is empty.");
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            Console.Error.WriteLine(validationError);
            return 64;
        }

        var condition = request.Condition == "baseline"
            ? AgentBenchmarkCondition.Baseline
            : AgentBenchmarkCondition.Candidate;
        var exposure = new CodexBenchmarkConditionExposure(
            condition,
            request.InstructionsHash,
            request.ToolConfigurationHash,
            request.ConfigurationOverrides);
        var otherExposure = exposure with
        {
            Condition = condition == AgentBenchmarkCondition.Baseline
                ? AgentBenchmarkCondition.Candidate
                : AgentBenchmarkCondition.Baseline,
        };
        var adapter = new CodexAgentBenchmarkAdapter(
            new CodexAgentBenchmarkAdapterOptions(
                request.CodexExecutablePath,
                request.CodexCliVersion,
                condition == AgentBenchmarkCondition.Baseline
                    ? exposure
                    : otherExposure,
                condition == AgentBenchmarkCondition.Candidate
                    ? exposure
                    : otherExposure,
                authenticationEnvironment:
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["CODEX_HOME"] = request.CodexHomePath,
                    }));
        var task = new AgentTaskDefinition(
            "manual-codex-smoke",
            "0.3.0",
            [],
            request.Prompt,
            new AgentTaskRepositoryState(
                "manual-smoke",
                "manual-codex-smoke",
                60,
                request.TaskContentHash,
                "controlled-smoke"),
            new AgentTaskApplicability(
                condition == AgentBenchmarkCondition.Baseline,
                condition == AgentBenchmarkCondition.Candidate),
            new AgentTaskExecutionPolicy(
                request.PermittedTools,
                request.TimeoutSeconds,
                request.NetworkPolicy,
                "invariant",
                "UTC"),
            new AgentTaskSuccessOracle(
                "exact-fact-set",
                "ordinal-lines/v1",
                [],
                null),
            new AgentTaskSafetyOracle(
                "all",
                ["claims-supported", "network-unused", "workspace-unchanged"]),
            ["smoke-reconciliation"]);
        var executionSettings = new AgentBenchmarkExecutionSettings(
            request.CodexCliVersion,
            request.ModelId,
            request.ReasoningSetting,
            request.SettingsHash,
            request.Sandbox,
            request.PermissionProfile,
            request.NetworkPolicy);
        var input = new AgentBenchmarkAdapterInput(
            "manual-codex-smoke/000001",
            1,
            0,
            1,
            condition,
            task,
            request.WorkspacePath,
            new Dictionary<string, string>(StringComparer.Ordinal),
            executionSettings,
            Hash(request.Prompt),
            request.InstructionsHash,
            request.ToolConfigurationHash);

        var stopwatch = Stopwatch.StartNew();
        var execution = await adapter.StartAsync(input);
        AgentBenchmarkAdapterResult? result = null;
        AgentBenchmarkProgressSnapshot? progress = null;
        var timedOut = false;
        try
        {
            var timeout = Task.Delay(
                TimeSpan.FromSeconds(request.TimeoutSeconds));
            var completed = await Task.WhenAny(execution.Completion, timeout);
            if (ReferenceEquals(completed, timeout))
            {
                timedOut = true;
                progress = execution.GetProgressSnapshot();
            }
            else
            {
                result = await execution.Completion;
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(
                TimeSpan.FromSeconds(request.CleanupTimeoutSeconds));
            await execution.StopAsync(cleanup.Token);
            await execution.DisposeAsync().AsTask().WaitAsync(cleanup.Token);
        }

        stopwatch.Stop();
        if (timedOut)
        {
            progress = execution.GetProgressSnapshot();
        }

        var rawEvents = result?.RawEvents ?? progress!.RawEvents;
        var reconciliation = result is null
            ? SmokeReconciliation.TimedOut(rawEvents)
            : Reconcile(result);
        var evidence = new
        {
            schema = EvidenceSchema,
            timedOut,
            durationMilliseconds = stopwatch.ElapsedMilliseconds,
            pins = new
            {
                request.CodexExecutablePath,
                request.CodexCliVersion,
                authenticationHomeHash = Hash(request.CodexHomePath),
                request.WorkspacePath,
                request.ModelId,
                request.ReasoningSetting,
                request.SettingsHash,
                request.Sandbox,
                request.PermissionProfile,
                request.NetworkPolicy,
                request.Condition,
                request.InstructionsHash,
                request.ToolConfigurationHash,
                request.TaskContentHash,
                promptHash = Hash(request.Prompt),
            },
            normalized = result is not null
                ? (object)result
                : new
                {
                    status = "timed-out",
                    progress!.InputTokens,
                    progress.OutputTokens,
                    progress.Turns,
                    progress.ToolCalls,
                    progress.InspectedScope,
                    progress.RawEvents,
                },
            reconciliation,
        };
        await using (var output = new FileStream(
                         outputPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            await JsonSerializer.SerializeAsync(output, evidence, JsonOptions);
            await output.WriteAsync("\n"u8.ToArray());
        }

        return !timedOut
               && result?.Status == "completed"
               && reconciliation.Passed
            ? 0
            : 1;
    }

    private static string? Validate(CodexSmokeRequest request)
    {
        var writeCapable = request.PermittedTools.Contains(
            "workspace-write",
            StringComparer.Ordinal);
        var expectedSandbox = writeCapable
            ? "workspace-write"
            : "read-only";
        if (request.Schema != RequestSchema
            || !Path.IsPathFullyQualified(request.CodexExecutablePath)
            || !File.Exists(request.CodexExecutablePath)
            || !Path.IsPathFullyQualified(request.CodexHomePath)
            || !Directory.Exists(request.CodexHomePath)
            || !Path.IsPathFullyQualified(request.WorkspacePath)
            || !Directory.Exists(request.WorkspacePath)
            || string.IsNullOrWhiteSpace(request.CodexCliVersion)
            || string.IsNullOrWhiteSpace(request.ModelId)
            || string.IsNullOrWhiteSpace(request.ReasoningSetting)
            || !IsHash(request.SettingsHash)
            || request.Sandbox != expectedSandbox
            || request.PermissionProfile != "never"
            || request.NetworkPolicy != "disabled"
            || request.Condition is not ("baseline" or "candidate")
            || !IsHash(request.InstructionsHash)
            || !IsHash(request.ToolConfigurationHash)
            || !IsHash(request.TaskContentHash)
            || request.ConfigurationOverrides is null
            || request.PermittedTools is null
            || request.PermittedTools.Count == 0
            || string.IsNullOrWhiteSpace(request.Prompt)
            || request.TimeoutSeconds is < 1 or > 1_800
            || request.CleanupTimeoutSeconds is < 1 or > 30)
        {
            return "The smoke request is invalid or does not pin the required controlled execution policy.";
        }

        return null;
    }

    private static SmokeReconciliation Reconcile(
        AgentBenchmarkAdapterResult result)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        var turns = 0;
        var toolCalls = 0;
        var answer = string.Empty;
        int? processId = null;
        int? exitProcessId = null;
        int? exitCode = null;
        var rawValid = true;
        for (var index = 0; index < result.RawEvents.Count; index++)
        {
            var raw = result.RawEvents[index];
            rawValid &= raw.Sequence == index
                        && raw.PayloadHash == Hash(raw.Payload);
            try
            {
                if (raw.Kind == "adapter.process.started")
                {
                    using var document = JsonDocument.Parse(raw.Payload);
                    processId = document.RootElement
                        .GetProperty("processId").GetInt32();
                }
                else if (raw.Kind == "adapter.process.exited")
                {
                    using var document = JsonDocument.Parse(raw.Payload);
                    exitProcessId = document.RootElement
                        .GetProperty("processId").GetInt32();
                    exitCode = document.RootElement
                        .GetProperty("exitCode").GetInt32();
                }
                else if (raw.Kind == "turn.started")
                {
                    turns++;
                }
                else if (raw.Kind == "turn.completed")
                {
                    using var document = JsonDocument.Parse(raw.Payload);
                    var usage = document.RootElement.GetProperty("usage");
                    inputTokens = checked(
                        inputTokens
                        + usage.GetProperty("input_tokens").GetInt64());
                    outputTokens = checked(
                        outputTokens
                        + usage.GetProperty("output_tokens").GetInt64());
                }
                else if (raw.Kind == "item.completed")
                {
                    using var document = JsonDocument.Parse(raw.Payload);
                    var item = document.RootElement.GetProperty("item");
                    var type = item.GetProperty("type").GetString();
                    if (type == "agent_message")
                    {
                        answer = item.GetProperty("text").GetString()
                                 ?? string.Empty;
                    }
                    else if (type is "command_execution" or "file_change"
                             or "mcp_tool_call" or "web_search")
                    {
                        toolCalls++;
                    }
                }
            }
            catch (Exception exception)
                when (exception is JsonException
                      or InvalidOperationException
                      or KeyNotFoundException
                      or OverflowException)
            {
                rawValid = false;
            }
        }

        var usageMatches = inputTokens == result.InputTokens
                           && outputTokens == result.OutputTokens;
        var turnsMatch = turns == result.Turns;
        var toolsMatch = toolCalls == result.ToolCalls.Count;
        var answerMatches = answer == result.Answer;
        var exitMatches = processId is not null
                          && processId == exitProcessId
                          && exitCode is not null;
        return new SmokeReconciliation(
            rawValid,
            usageMatches,
            turnsMatch,
            toolsMatch,
            answerMatches,
            exitMatches,
            rawValid && usageMatches && turnsMatch && toolsMatch
            && answerMatches && exitMatches);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsHash(string? value) =>
        value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');
}

internal sealed record CodexSmokeRequest(
    string Schema,
    string CodexExecutablePath,
    string CodexCliVersion,
    string CodexHomePath,
    string WorkspacePath,
    string ModelId,
    string ReasoningSetting,
    string SettingsHash,
    string Sandbox,
    string PermissionProfile,
    string NetworkPolicy,
    string Condition,
    string InstructionsHash,
    string ToolConfigurationHash,
    IReadOnlyList<string> ConfigurationOverrides,
    string TaskContentHash,
    IReadOnlyList<string> PermittedTools,
    string Prompt,
    int TimeoutSeconds,
    int CleanupTimeoutSeconds);

internal sealed record SmokeReconciliation(
    bool RawSequenceAndHashes,
    bool Usage,
    bool Turns,
    bool ToolCalls,
    bool FinalAnswer,
    bool ProcessExit,
    bool Passed)
{
    public static SmokeReconciliation TimedOut(
        IReadOnlyList<AgentBenchmarkRawEvent> rawEvents)
    {
        var rawValid = rawEvents.Select(
                static (value, index) => (value, index))
            .All(pair => pair.value.Sequence == pair.index
                         && pair.value.PayloadHash
                         == CodexSmokeProgramHash(pair.value.Payload));
        var hasExit = rawEvents.Any(
            static value => value.Kind == "adapter.process.exited");
        return new SmokeReconciliation(
            rawValid,
            Usage: false,
            Turns: false,
            ToolCalls: false,
            FinalAnswer: false,
            ProcessExit: hasExit,
            Passed: false);
    }

    private static string CodexSmokeProgramHash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
