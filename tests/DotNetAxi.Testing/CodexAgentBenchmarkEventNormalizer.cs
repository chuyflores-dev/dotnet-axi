using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetAxi.Testing;

internal sealed partial class CodexAgentBenchmarkEventNormalizer
{
    private readonly object _gate = new();
    private readonly AgentBenchmarkAdapterInput _input;
    private readonly List<AgentBenchmarkRawEvent> _rawEvents = [];
    private readonly List<AgentBenchmarkToolCall> _toolCalls = [];
    private readonly HashSet<string> _completedItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _projects = new(StringComparer.Ordinal);
    private long _inputTokens;
    private long _outputTokens;
    private int _turns;
    private ProviderState _providerState;
    private bool _providerError;
    private bool _turnFailed;
    private bool _permissionDenied;
    private bool _networkUsed;
    private bool _protocolFailure;
    private string _answer = string.Empty;

    public CodexAgentBenchmarkEventNormalizer(
        AgentBenchmarkAdapterInput input)
    {
        _input = input;
    }

    public void AddAdapterEvent(string kind, string payload)
    {
        lock (_gate)
        {
            AddRawEvent(kind, payload);
        }
    }

    public void AddProviderLine(string line)
    {
        lock (_gate)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !TryGetString(root, "type", out var type))
                {
                    AddRawEvent("codex.malformed", line);
                    _protocolFailure = true;
                    return;
                }

                AddRawEvent(type, line);
                NormalizeEvent(type, root);
            }
            catch (JsonException)
            {
                AddRawEvent("codex.malformed", line);
                _protocolFailure = true;
            }
        }
    }

    public void AddTruncatedProviderLine(string line)
    {
        lock (_gate)
        {
            AddRawEvent("codex.truncated", line);
            _protocolFailure = true;
        }
    }

    public void AddStandardError(string line)
    {
        lock (_gate)
        {
            AddRawEvent("codex.stderr", line);
            ObserveDenial(line);
        }
    }

    public AgentBenchmarkProgressSnapshot GetProgressSnapshot()
    {
        lock (_gate)
        {
            return new AgentBenchmarkProgressSnapshot(
                _inputTokens,
                _outputTokens,
                _turns,
                SnapshotToolCalls(),
                SnapshotScope(),
                SnapshotRawEvents());
        }
    }

    public AgentBenchmarkAdapterResult CreateResult(int exitCode)
    {
        lock (_gate)
        {
            var status = _permissionDenied
                ? "permission-denied"
                : exitCode == 0
                  && _providerState == ProviderState.TurnCompleted
                  && !_turnFailed
                  && !_providerError
                  && !_protocolFailure
                    ? "completed"
                    : "failed";
            return new AgentBenchmarkAdapterResult(
                status,
                _answer,
                _inputTokens,
                _outputTokens,
                _turns,
                SnapshotToolCalls(),
                SnapshotScope(),
                ClaimsSupported: status == "completed",
                NetworkUsed: _networkUsed,
                new AgentBenchmarkObservedConfiguration(
                    _input.Execution.AgentVersion,
                    _input.Execution.ModelId,
                    _input.Execution.ReasoningSetting,
                    _input.Execution.SettingsHash,
                    _input.Execution.Sandbox,
                    _input.Execution.PermissionProfile,
                    _input.Execution.NetworkPolicy,
                    _input.Task.Repository.ContentHash,
                    _input.PromptHash,
                    _input.InstructionsHash,
                    _input.ToolConfigurationHash),
                SnapshotRawEvents());
        }
    }

    private void NormalizeEvent(string type, JsonElement root)
    {
        switch (type)
        {
            case "thread.started":
                if (_providerState != ProviderState.AwaitingThread
                    || !TryGetString(root, "thread_id", out _))
                {
                    _protocolFailure = true;
                    break;
                }

                _providerState = ProviderState.ThreadStarted;
                break;
            case "turn.started":
                if (_providerState != ProviderState.ThreadStarted)
                {
                    _protocolFailure = true;
                    break;
                }

                _providerState = ProviderState.TurnStarted;
                _turns++;
                break;
            case "turn.completed":
                if (_providerState != ProviderState.TurnStarted)
                {
                    _protocolFailure = true;
                    break;
                }

                _providerState = ProviderState.TurnCompleted;
                ReadUsage(root);
                break;
            case "turn.failed":
                if (_providerState != ProviderState.TurnStarted)
                {
                    _protocolFailure = true;
                    break;
                }

                _providerState = ProviderState.TurnFailed;
                _turnFailed = true;
                ObserveError(root);
                break;
            case "error":
                if (_providerState is ProviderState.TurnCompleted
                    or ProviderState.TurnFailed
                    or ProviderState.FailedBeforeTurn)
                {
                    _protocolFailure = true;
                    break;
                }

                _providerError = true;
                ObserveError(root);
                if (_providerState is ProviderState.AwaitingThread
                    or ProviderState.ThreadStarted)
                {
                    _providerState = ProviderState.FailedBeforeTurn;
                }

                break;
            case "item.started":
            case "item.updated":
                if (_providerState != ProviderState.TurnStarted)
                {
                    _protocolFailure = true;
                }

                break;
            case "item.completed":
                if (_providerState != ProviderState.TurnStarted)
                {
                    _protocolFailure = true;
                    break;
                }

                NormalizeCompletedItem(root);
                break;
            default:
                _protocolFailure = true;
                break;
        }
    }

    private void NormalizeCompletedItem(JsonElement root)
    {
        if (!root.TryGetProperty("item", out var item)
            || item.ValueKind != JsonValueKind.Object
            || !TryGetString(item, "type", out var itemType))
        {
            _protocolFailure = true;
            return;
        }

        var itemId = TryGetString(item, "id", out var id)
            ? id
            : $"sequence-{_rawEvents.Count - 1}";
        if (!_completedItems.Add(itemId))
        {
            _protocolFailure = true;
            return;
        }

        switch (itemType)
        {
            case "agent_message":
                if (TryGetString(item, "text", out var text))
                {
                    _answer = text;
                }

                break;
            case "command_execution":
                NormalizeCommand(item);
                break;
            case "file_change":
                NormalizeFileChange(item);
                break;
            case "mcp_tool_call":
                NormalizeMcpCall(item);
                break;
            case "web_search":
                NormalizeWebSearch(item);
                break;
            case "error":
                ObserveError(item);
                break;
        }
    }

    private void NormalizeCommand(JsonElement item)
    {
        if (!TryGetString(item, "command", out var command))
        {
            _protocolFailure = true;
            return;
        }

        var exitCode = item.TryGetProperty("exit_code", out var exit)
                       && exit.TryGetInt32(out var value)
            ? value
            : (int?)null;
        var output = TryGetString(item, "aggregated_output", out var aggregate)
            ? aggregate
            : string.Empty;
        var succeeded = exitCode == 0
                        && (!TryGetString(item, "status", out var status)
                            || status == "completed");
        AddToolCall(
            MapCommandToolClass(command),
            command,
            item.GetRawText(),
            succeeded);
        var allowedReadRoots =
            CodexAgentBenchmarkAdapter.GetAgentReadableRoots(_input);
        var readAnalysis = CodexBenchmarkCommandEvidence.AnalyzeReadAttempts(
            command,
            _input.WorkspacePath,
            allowedReadRoots);
        foreach (var attempt in readAnalysis.Attempts)
        {
            AddRawEvent(
                "adapter.filesystem.read.denied",
                JsonSerializer.Serialize(new
                {
                    commandHash = AgentBenchmarkHash.Compute(command),
                    attemptedPath = attempt.Operand,
                    resolvedPath = attempt.ResolvedPath,
                }));
            _permissionDenied = true;
            _protocolFailure = true;
        }

        if (!readAnalysis.Complete)
        {
            AddRawEvent(
                "adapter.filesystem.read.unreconciled",
                JsonSerializer.Serialize(new
                {
                    commandHash = AgentBenchmarkHash.Compute(command),
                }));
            _permissionDenied = true;
            _protocolFailure = true;
        }

        ObserveCommandScope(command);
        ObserveOutputScope(output);
        if (NetworkCommandRegex().IsMatch(command))
        {
            _networkUsed = true;
        }

        ObserveDenial(output);
    }

    private void NormalizeFileChange(JsonElement item)
    {
        AddToolCall(
            "workspace-write",
            "file_change",
            item.GetRawText(),
            IsCompleted(item));
        if (item.TryGetProperty("changes", out var changes)
            && changes.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in changes.EnumerateArray())
            {
                if (change.ValueKind == JsonValueKind.Object
                    && TryGetString(change, "path", out var path))
                {
                    if (!CodexBenchmarkCommandEvidence.ObservePath(
                            path,
                            _input.WorkspacePath,
                            _files,
                            _projects))
                    {
                        _protocolFailure = true;
                    }
                }
            }
        }

        if (_input.Execution.Sandbox == "read-only")
        {
            _protocolFailure = true;
        }
    }

    private void NormalizeMcpCall(JsonElement item)
    {
        var server = TryGetString(item, "server", out var serverName)
            ? serverName
            : string.Empty;
        var name = TryGetString(item, "tool", out var tool)
            ? tool
            : "mcp_tool_call";
        var qualifiedName = string.IsNullOrEmpty(server)
            ? name
            : $"{server}.{name}";
        AddToolCall(
            MapMcpToolClass(qualifiedName),
            qualifiedName,
            item.GetRawText(),
            IsCompleted(item));
    }

    private void NormalizeWebSearch(JsonElement item)
    {
        _networkUsed = true;
        AddToolCall(
            "network",
            "web_search",
            item.GetRawText(),
            IsCompleted(item));
    }

    private void ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object
            || !TryGetNonNegativeInt64(usage, "input_tokens", out var input)
            || !TryGetNonNegativeInt64(usage, "output_tokens", out var output))
        {
            _protocolFailure = true;
            return;
        }

        try
        {
            _inputTokens = checked(_inputTokens + input);
            _outputTokens = checked(_outputTokens + output);
            _ = checked(_inputTokens + _outputTokens);
        }
        catch (OverflowException)
        {
            _protocolFailure = true;
        }
    }

    private void ObserveError(JsonElement value)
    {
        var message = TryGetString(value, "message", out var direct)
            ? direct
            : value.TryGetProperty("error", out var error)
              && error.ValueKind == JsonValueKind.Object
              && TryGetString(error, "message", out var nested)
                ? nested
                : value.GetRawText();
        ObserveDenial(message);
    }

    private void ObserveDenial(string value)
    {
        if (DenialRegex().IsMatch(value))
        {
            _permissionDenied = true;
        }
    }

    private void AddToolCall(
        string toolClass,
        string name,
        string input,
        bool succeeded)
    {
        if (!_input.Task.Execution.PermittedTools.Contains(
                toolClass,
                StringComparer.Ordinal))
        {
            _protocolFailure = true;
        }

        _toolCalls.Add(
            new AgentBenchmarkToolCall(
                _toolCalls.Count,
                toolClass,
                name,
                AgentBenchmarkHash.Compute(input),
                succeeded));
    }

    private string MapCommandToolClass(string command)
    {
        return CodexBenchmarkCommandEvidence.Classify(
            command,
            _input.Execution.Sandbox,
            _input.Task.Execution.PermittedTools);
    }

    private string MapMcpToolClass(string name)
    {
        if (name.Contains("search", StringComparison.OrdinalIgnoreCase)
            && _input.Task.Execution.PermittedTools.Contains(
                "source-search",
                StringComparer.Ordinal))
        {
            return "source-search";
        }

        if (_input.Task.Execution.PermittedTools.Contains(
                "repository-read",
                StringComparer.Ordinal))
        {
            return "repository-read";
        }

        return "mcp";
    }

    private void ObserveCommandScope(string value)
    {
        if (!CodexBenchmarkCommandEvidence.ObserveCommandScope(
                value,
                _input.WorkspacePath,
                _files,
                _projects,
                CodexAgentBenchmarkAdapter.GetAgentReadableRoots(_input)))
        {
            _protocolFailure = true;
        }
    }

    private void ObserveOutputScope(string value)
    {
        if (!CodexBenchmarkCommandEvidence.ObserveOutputScope(
                value,
                _input.WorkspacePath,
                _files,
                _projects))
        {
            _protocolFailure = true;
        }
    }

    private void AddRawEvent(string kind, string payload) =>
        _rawEvents.Add(
            new AgentBenchmarkRawEvent(
                _rawEvents.Count,
                kind,
                payload,
                AgentBenchmarkHash.Compute(payload)));

    private IReadOnlyList<AgentBenchmarkRawEvent> SnapshotRawEvents() =>
        AgentBenchmarkSnapshots.List(
            _rawEvents.Select(static rawEvent => rawEvent with { }));

    private IReadOnlyList<AgentBenchmarkToolCall> SnapshotToolCalls() =>
        AgentBenchmarkSnapshots.List(
            _toolCalls.Select(static call => call with { }));

    private AgentBenchmarkInspectedScope SnapshotScope() =>
        new(
            AgentBenchmarkSnapshots.List(_files.Order(StringComparer.Ordinal)),
            AgentBenchmarkSnapshots.List(_projects.Order(StringComparer.Ordinal)));

    private static bool IsCompleted(JsonElement item) =>
        !TryGetString(item, "status", out var status)
        || status == "completed";

    private static bool TryGetString(
        JsonElement value,
        string propertyName,
        out string result)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } text)
        {
            result = text;
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryGetNonNegativeInt64(
        JsonElement value,
        string propertyName,
        out long result)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.TryGetInt64(out result)
            && result >= 0)
        {
            return true;
        }

        result = 0;
        return false;
    }

    [GeneratedRegex(
        "(?:permission|approval|read[- ]only|network|sandbox).{0,80}(?:denied|required|blocked|disabled|not permitted)|access is denied",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DenialRegex();

    [GeneratedRegex(
        "(?:^|[\\s'\"])(?:curl|wget|Invoke-WebRequest|web_search|nuget|git\\s+(?:clone|fetch|pull)|dotnet\\s+restore)(?:[\\s'\"]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NetworkCommandRegex();

    private enum ProviderState
    {
        AwaitingThread,
        ThreadStarted,
        TurnStarted,
        TurnCompleted,
        TurnFailed,
        FailedBeforeTurn,
    }
}
