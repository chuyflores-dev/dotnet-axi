using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotNetAxi.Testing;

public sealed record CodexBenchmarkConditionExposure(
    AgentBenchmarkCondition Condition,
    string InstructionsHash,
    string ToolConfigurationHash,
    IReadOnlyList<string> ConfigurationOverrides,
    IReadOnlyList<string>? ExecutableSearchPathEntries = null);

public sealed class CodexAgentBenchmarkAdapterOptions
{
    private static readonly HashSet<string> AuthenticationVariableNames =
        new(StringComparer.Ordinal)
        {
            "CODEX_HOME",
            "CODEX_API_KEY",
            "OPENAI_API_KEY",
        };

    private readonly IReadOnlyDictionary<string, string>
        _authenticationEnvironment;

    public CodexAgentBenchmarkAdapterOptions(
        string executablePath,
        string cliVersion,
        CodexBenchmarkConditionExposure baseline,
        CodexBenchmarkConditionExposure candidate,
        IReadOnlyList<string>? executablePrefixArguments = null,
        IReadOnlyDictionary<string, string>? authenticationEnvironment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliVersion);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "The pinned Codex executable path must be absolute.",
                nameof(executablePath));
        }

        ValidateExposure(baseline, AgentBenchmarkCondition.Baseline);
        ValidateExposure(candidate, AgentBenchmarkCondition.Candidate);
        ExecutablePath = executablePath;
        CliVersion = cliVersion;
        Baseline = Snapshot(baseline);
        Candidate = Snapshot(candidate);
        ExecutablePrefixArguments = Array.AsReadOnly(
            (executablePrefixArguments ?? []).ToArray());

        var authentication = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var variable in authenticationEnvironment
                     ?? new Dictionary<string, string>())
        {
            if (!AuthenticationVariableNames.Contains(variable.Key)
                || string.IsNullOrEmpty(variable.Value))
            {
                throw new ArgumentException(
                    $"Authentication environment variable '{variable.Key}' is not supported.",
                    nameof(authenticationEnvironment));
            }

            authentication.Add(variable.Key, variable.Value);
        }

        _authenticationEnvironment = new ReadOnlyDictionary<string, string>(
            authentication);
    }

    public string ExecutablePath { get; }

    public string CliVersion { get; }

    public CodexBenchmarkConditionExposure Baseline { get; }

    public CodexBenchmarkConditionExposure Candidate { get; }

    public IReadOnlyList<string> ExecutablePrefixArguments { get; }

    internal IReadOnlyDictionary<string, string> AuthenticationEnvironment =>
        _authenticationEnvironment;

    private static CodexBenchmarkConditionExposure Snapshot(
        CodexBenchmarkConditionExposure exposure) =>
        exposure with
        {
            ConfigurationOverrides = Array.AsReadOnly(
                exposure.ConfigurationOverrides.ToArray()),
            ExecutableSearchPathEntries = Array.AsReadOnly(
                (exposure.ExecutableSearchPathEntries ?? []).ToArray()),
        };

    private static void ValidateExposure(
        CodexBenchmarkConditionExposure exposure,
        AgentBenchmarkCondition condition)
    {
        if (exposure.Condition != condition
            || !AgentBenchmarkHash.IsHash(exposure.InstructionsHash)
            || !AgentBenchmarkHash.IsHash(exposure.ToolConfigurationHash)
            || exposure.ConfigurationOverrides is null
            || (exposure.ExecutableSearchPathEntries ?? []).Any(path =>
                !Path.IsPathFullyQualified(path)))
        {
            throw new ArgumentException(
                $"The {condition} Codex exposure is malformed.",
                nameof(exposure));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configurationOverride in exposure.ConfigurationOverrides)
        {
            var separator = configurationOverride?.IndexOf('=') ?? -1;
            var key = separator > 0
                ? configurationOverride![..separator].Trim()
                : string.Empty;
            if (!(key.StartsWith("skills.", StringComparison.Ordinal)
                  || key.StartsWith("mcp_servers.", StringComparison.Ordinal))
                || !keys.Add(key))
            {
                throw new ArgumentException(
                    "Condition-specific Codex configuration may declare only unique skill or MCP tool exposure.",
                    nameof(exposure));
            }
        }
    }
}

public sealed class CodexAgentBenchmarkAdapter : IAgentBenchmarkAdapter
{
    private static readonly HashSet<string> RemovedNetworkEnvironmentVariables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ALL_PROXY",
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "NO_PROXY",
        };

    private readonly CodexAgentBenchmarkAdapterOptions _options;

    public CodexAgentBenchmarkAdapter(
        CodexAgentBenchmarkAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public AgentBenchmarkAdapterDescriptor Descriptor { get; } =
        new("codex", "1.0.0");

    public ValueTask<IAgentBenchmarkExecution> StartAsync(
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInput(input);
        var startInfo = CreateStartInfo(input);
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        var processStarted = false;
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new AgentBenchmarkStartException(
                    "The pinned Codex CLI did not create a process.",
                    retryable: true);
            }

            processStarted = true;
            process.StandardInput.Close();
            return ValueTask.FromResult<IAgentBenchmarkExecution>(
                new CodexAgentBenchmarkExecution(process, input));
        }
        catch (AgentBenchmarkStartException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or System.ComponentModel.Win32Exception
                  or IOException)
        {
            if (processStarted)
            {
                StopFailedStart(process);
            }

            process.Dispose();
            throw new AgentBenchmarkStartException(
                $"The pinned Codex CLI could not start: {exception.Message}",
                retryable: !processStarted);
        }
    }

    internal ProcessStartInfo CreateStartInfo(AgentBenchmarkAdapterInput input)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            WorkingDirectory = input.WorkspacePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in _options.ExecutablePrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        AddArguments(startInfo.ArgumentList, input);
        var inherited = startInfo.Environment
            .Where(static variable => variable.Key is
                "COMSPEC" or "ComSpec" or "PATH" or "Path" or
                "PATHEXT" or "SystemRoot" or "WINDIR")
            .ToArray();
        startInfo.Environment.Clear();
        foreach (var variable in inherited)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        foreach (var variable in input.EnvironmentVariables)
        {
            if (!RemovedNetworkEnvironmentVariables.Contains(variable.Key))
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["TZ"] = "UTC";
        foreach (var variable in _options.AuthenticationEnvironment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        var exposure = Exposure(input.Condition);
        if (exposure.ExecutableSearchPathEntries is { Count: > 0 })
        {
            startInfo.Environment["PATH"] = string.Join(
                Path.PathSeparator,
                exposure.ExecutableSearchPathEntries);
        }

        return startInfo;
    }

    private void AddArguments(
        Collection<string> arguments,
        AgentBenchmarkAdapterInput input)
    {
        arguments.Add("exec");
        arguments.Add("--ephemeral");
        arguments.Add("--json");
        arguments.Add("--ignore-user-config");
        arguments.Add("--ignore-rules");
        arguments.Add("--model");
        arguments.Add(input.Execution.ModelId);
        arguments.Add("--cd");
        arguments.Add(input.WorkspacePath);
        arguments.Add("--sandbox");
        arguments.Add(input.Execution.Sandbox);
        AddConfig(
            arguments,
            "model_reasoning_effort",
            input.Execution.ReasoningSetting);
        AddConfig(
            arguments,
            "approval_policy",
            input.Execution.PermissionProfile);
        AddConfig(arguments, "web_search", "disabled");
        arguments.Add("--config");
        arguments.Add("sandbox_workspace_write.network_access=false");
        var exposure = Exposure(input.Condition);
        foreach (var configurationOverride in exposure.ConfigurationOverrides)
        {
            arguments.Add("--config");
            arguments.Add(configurationOverride);
        }

        arguments.Add("--");
        arguments.Add(input.Task.Prompt);
    }

    private static void AddConfig(
        Collection<string> arguments,
        string key,
        string value)
    {
        arguments.Add("--config");
        arguments.Add($"{key}={JsonSerializer.Serialize(value)}");
    }

    private void ValidateInput(AgentBenchmarkAdapterInput input)
    {
        var expectedSandbox = input.Task.Execution.PermittedTools.Contains(
            "workspace-write",
            StringComparer.Ordinal)
                ? "workspace-write"
                : "read-only";
        var exposure = Exposure(input.Condition);
        if (!Directory.Exists(input.WorkspacePath)
            || !string.Equals(
                input.Execution.AgentVersion,
                _options.CliVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                input.Execution.Sandbox,
                expectedSandbox,
                StringComparison.Ordinal)
            || !string.Equals(
                input.Execution.PermissionProfile,
                "never",
                StringComparison.Ordinal)
            || !string.Equals(
                input.Execution.NetworkPolicy,
                "disabled",
                StringComparison.Ordinal)
            || !string.Equals(
                input.Task.Execution.Network,
                "disabled",
                StringComparison.Ordinal)
            || !string.Equals(
                input.InstructionsHash,
                exposure.InstructionsHash,
                StringComparison.Ordinal)
            || !string.Equals(
                input.ToolConfigurationHash,
                exposure.ToolConfigurationHash,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkStartException(
                "The Codex run does not match the pinned execution or condition exposure.",
                retryable: false);
        }
    }

    private CodexBenchmarkConditionExposure Exposure(
        AgentBenchmarkCondition condition) =>
        condition == AgentBenchmarkCondition.Baseline
            ? _options.Baseline
            : _options.Candidate;

    private static void StopFailedStart(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(milliseconds: 5_000);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or System.ComponentModel.Win32Exception)
        {
            // A process identity was created, so the failure remains
            // non-retryable even if bounded cleanup cannot prove it was reaped.
        }
    }
}
