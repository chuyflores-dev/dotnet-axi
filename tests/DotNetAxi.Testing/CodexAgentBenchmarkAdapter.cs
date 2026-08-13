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
    IReadOnlyList<string>? ExecutableSearchPathEntries = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    string? SkillDirectoryPath = null);

public sealed class CodexAgentBenchmarkAdapterOptions
{
    private static readonly HashSet<string> AuthenticationVariableNames =
        new(StringComparer.Ordinal)
        {
            "CODEX_HOME",
            "CODEX_API_KEY",
            "OPENAI_API_KEY",
        };

    private static readonly HashSet<string> ConditionVariableNames =
        new(StringComparer.Ordinal)
        {
            "DNAXI_LOCAL_FEED",
        };

    private readonly IReadOnlyDictionary<string, string>
        _authenticationEnvironment;

    public CodexAgentBenchmarkAdapterOptions(
        string executablePath,
        string cliVersion,
        CodexBenchmarkConditionExposure baseline,
        CodexBenchmarkConditionExposure candidate,
        IReadOnlyList<string>? executablePrefixArguments = null,
        IReadOnlyDictionary<string, string>? authenticationEnvironment = null,
        string? expectedDnxExecutablePath = null)
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
        if (expectedDnxExecutablePath is not null
            && !Path.IsPathFullyQualified(expectedDnxExecutablePath))
        {
            throw new ArgumentException(
                "The expected dnx executable path must be absolute.",
                nameof(expectedDnxExecutablePath));
        }

        ExpectedDnxExecutablePath = expectedDnxExecutablePath;

        var authentication = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var variable in authenticationEnvironment
                     ?? new Dictionary<string, string>())
        {
            if (!AuthenticationVariableNames.Contains(variable.Key)
                || string.IsNullOrEmpty(variable.Value)
                || (string.Equals(
                        variable.Key,
                        "CODEX_HOME",
                        StringComparison.Ordinal)
                    && !Path.IsPathFullyQualified(variable.Value)))
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

    public string? ExpectedDnxExecutablePath { get; }

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
            EnvironmentVariables = new ReadOnlyDictionary<string, string>(
                (exposure.EnvironmentVariables
                 ?? new Dictionary<string, string>())
                .ToDictionary(
                    static variable => variable.Key,
                    static variable => variable.Value,
                    StringComparer.Ordinal)),
            SkillDirectoryPath = exposure.SkillDirectoryPath is null
                ? null
                : Path.GetFullPath(exposure.SkillDirectoryPath),
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
                !Path.IsPathFullyQualified(path))
            || (exposure.SkillDirectoryPath is not null
                && (!Path.IsPathFullyQualified(exposure.SkillDirectoryPath)
                    || !Directory.Exists(exposure.SkillDirectoryPath)))
            || (exposure.EnvironmentVariables
                ?? new Dictionary<string, string>()).Any(variable =>
                !ConditionVariableNames.Contains(variable.Key)
                || string.IsNullOrEmpty(variable.Value)
                || !Path.IsPathFullyQualified(variable.Value)
                || !Directory.Exists(variable.Value)))
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
    internal const string ShellSearchPathEnvironmentVariable =
        "DOTNET_AXI_BENCHMARK_PATH";
    internal const string RuntimePermissionProfileName =
        "dnaxi-benchmark";

    private static readonly string[] RuntimeStatePathVariables =
    [
        "HOME",
        "USERPROFILE",
        "APPDATA",
        "LOCALAPPDATA",
        "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME",
        "GIT_CONFIG_GLOBAL",
        "DOTNET_CLI_HOME",
        "NUGET_PACKAGES",
        "NUGET_HTTP_CACHE_PATH",
        "NUGET_PLUGINS_CACHE_PATH",
        "RestoreConfigFile",
        "TMPDIR",
        "TMP",
        "TEMP",
        "DOTNET_AXI_ARTIFACTS",
    ];

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
        new("codex", "1.8.0");

    public ValueTask PrepareWorkspaceAsync(
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInput(input);
        var exposure = Exposure(input.Condition);
        var installation = Path.Combine(
            input.WorkspacePath,
            ".agents",
            "skills",
            "dotnet-axi");
        if (File.Exists(installation) || Directory.Exists(installation))
        {
            throw new AgentBenchmarkException(
                "The materialized benchmark workspace already exposes dotnet-axi skill content.");
        }

        if (exposure.SkillDirectoryPath is not null)
        {
            CopySkillDirectory(
                exposure.SkillDirectoryPath,
                installation,
                cancellationToken);
        }

        MaterializeConditionArtifacts(
            input.WorkspacePath,
            exposure,
            cancellationToken);

        return ValueTask.CompletedTask;
    }

    public async ValueTask<IAgentBenchmarkExecution> StartAsync(
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInput(input);
        var startInfo = CreateStartInfo(input);
        if (_options.ExpectedDnxExecutablePath is not null)
        {
            await ValidateLoginShellDnxResolutionAsync(
                startInfo,
                GetMaterializedExecutablePath(
                    input,
                    _options.ExpectedDnxExecutablePath),
                cancellationToken);
        }

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
            return new CodexAgentBenchmarkExecution(process, input);
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
            var materializedSearchPathEntries =
                GetMaterializedSearchPathEntries(input, exposure);
            var searchPath = string.Join(
                Path.PathSeparator,
                materializedSearchPathEntries);
            startInfo.Environment["PATH"] = searchPath;
            startInfo.Environment[ShellSearchPathEnvironmentVariable] =
                searchPath;
        }

        foreach (var variable in exposure.EnvironmentVariables
                     ?? new Dictionary<string, string>())
        {
            startInfo.Environment[variable.Key] = variable.Key == "DNAXI_LOCAL_FEED"
                ? GetMaterializedFeedPath(input.WorkspacePath)
                : variable.Value;
        }

        return startInfo;
    }

    private static async ValueTask ValidateLoginShellDnxResolutionAsync(
        ProcessStartInfo codexStartInfo,
        string expectedDnxExecutablePath,
        CancellationToken cancellationToken)
    {
        var probe = CreateLoginShellProbe(codexStartInfo);
        using var process = new Process
        {
            StartInfo = probe,
        };
        var processStarted = false;
        try
        {
            if (!process.Start())
            {
                throw new AgentBenchmarkStartException(
                    "The login-shell dnx resolution probe did not start.",
                    retryable: false);
            }

            processStarted = true;

            var standardOutput = process.StandardOutput.ReadToEndAsync(
                cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(
                cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await standardOutput;
            var error = await standardError;
            var resolved = output.ReplaceLineEndings("\n")
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (process.ExitCode != 0
                || !string.IsNullOrWhiteSpace(error)
                || !string.Equals(
                    resolved,
                    Path.GetFullPath(expectedDnxExecutablePath),
                    comparison))
            {
                throw new AgentBenchmarkStartException(
                    "The login shell did not resolve dnx to the request-pinned executable.",
                    retryable: false);
            }
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
            throw new AgentBenchmarkStartException(
                $"The login-shell dnx resolution probe failed: {exception.Message}",
                retryable: false);
        }
        finally
        {
            if (processStarted && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static ProcessStartInfo CreateLoginShellProbe(
        ProcessStartInfo codexStartInfo)
    {
        var probe = new ProcessStartInfo
        {
            WorkingDirectory = codexStartInfo.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            probe.FileName = "where.exe";
            probe.ArgumentList.Add("dnx");
        }
        else
        {
            probe.FileName = File.Exists("/bin/zsh")
                ? "/bin/zsh"
                : File.Exists("/bin/bash")
                    ? "/bin/bash"
                    : "/bin/sh";
            probe.ArgumentList.Add("-lc");
            probe.ArgumentList.Add("command -v dnx");
        }

        probe.Environment.Clear();
        foreach (var variable in codexStartInfo.Environment)
        {
            probe.Environment[variable.Key] = variable.Value;
        }

        return probe;
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
        arguments.Add("--skip-git-repo-check");
        arguments.Add("--model");
        arguments.Add(input.Execution.ModelId);
        arguments.Add("--cd");
        arguments.Add(input.WorkspacePath);
        AddConfig(
            arguments,
            "default_permissions",
            RuntimePermissionProfileName);
        arguments.Add("--config");
        arguments.Add(CreateRuntimePermissionProfileConfig(
            input.WorkspacePath,
            GetRuntimeStateRoot(input),
            GetMaterializedArtifactRoot(input.WorkspacePath),
            GetAuthenticationHomePath(),
            input.Execution.Sandbox));

        AddConfig(
            arguments,
            "model_reasoning_effort",
            input.Execution.ReasoningSetting);
        AddConfig(
            arguments,
            "approval_policy",
            input.Execution.PermissionProfile);
        AddConfig(arguments, "web_search", "disabled");
        var exposure = Exposure(input.Condition);
        foreach (var configurationOverride in exposure.ConfigurationOverrides)
        {
            arguments.Add("--config");
            arguments.Add(configurationOverride);
        }

        arguments.Add("--");
        arguments.Add(input.Task.Prompt);
    }

    internal static string CreateRuntimePermissionProfileConfig(
        string workspacePath,
        string? runtimeStateRoot,
        string artifactRoot,
        string? authenticationHomePath,
        string sandbox)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        if (!Path.IsPathFullyQualified(workspacePath)
            || !Path.IsPathFullyQualified(artifactRoot)
            || (runtimeStateRoot is not null
                && !Path.IsPathFullyQualified(runtimeStateRoot))
            || (authenticationHomePath is not null
                && !Path.IsPathFullyQualified(authenticationHomePath)))
        {
            throw new ArgumentException(
                "The benchmark isolation roots must be absolute.",
                nameof(workspacePath));
        }

        var workspaceAccess = sandbox switch
        {
            "read-only" => "read",
            "workspace-write" => "write",
            _ => throw new ArgumentException(
                "The benchmark sandbox cannot be represented by the isolated runtime permission profile.",
                nameof(sandbox)),
        };

        var workspace = Path.GetFullPath(workspacePath);
        var artifacts = Path.GetFullPath(artifactRoot);
        var runtime = runtimeStateRoot is null
            ? null
            : Path.GetFullPath(runtimeStateRoot);
        var authentication = authenticationHomePath is null
            ? null
            : Path.GetFullPath(authenticationHomePath);
        if (IsContained(workspace, artifacts)
            || IsContained(artifacts, workspace)
            || (runtime is not null
                && (IsContained(workspace, runtime)
                    || IsContained(runtime, workspace)
                    || IsContained(artifacts, runtime)
                    || IsContained(runtime, artifacts))))
        {
            throw new ArgumentException(
                "The benchmark workspace, runtime, and artifacts must be isolated roots.",
                nameof(workspacePath));
        }

        var fileSystem = new List<string>
        {
            "\":root\"=\"deny\"",
            "\":minimal\"=\"read\"",
            "\":tmpdir\"=\"write\"",
            "\":slash_tmp\"=\"deny\"",
            $"{JsonSerializer.Serialize(workspace)}={JsonSerializer.Serialize(workspaceAccess)}",
            $"{JsonSerializer.Serialize(artifacts)}=\"read\"",
        };
        if (runtime is not null)
        {
            fileSystem.Add(
                $"{JsonSerializer.Serialize(runtime)}=\"write\"");
        }

        if (authentication is not null)
        {
            fileSystem.Add(
                $"{JsonSerializer.Serialize(authentication)}=\"deny\"");
        }

        return string.Concat(
            "permissions={",
            RuntimePermissionProfileName,
            "={filesystem={",
            string.Join(',', fileSystem),
            "},network={enabled=false}}}");
    }

    internal static string GetMaterializedArtifactRoot(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var parent = Path.GetDirectoryName(Path.GetFullPath(workspacePath))
            ?? throw new AgentBenchmarkException(
                "The materialized benchmark workspace has no parent directory.");
        return Path.Combine(
            parent,
            $"agent-boundary-{Path.GetFileName(Path.GetFullPath(workspacePath))}");
    }

    internal IReadOnlyList<string> GetMaterializedSearchPathEntries(
        AgentBenchmarkAdapterInput input) =>
        GetMaterializedSearchPathEntries(input, Exposure(input.Condition));

    internal string GetMaterializedExecutablePath(
        AgentBenchmarkAdapterInput input,
        string sourceExecutablePath)
    {
        var exposure = Exposure(input.Condition);
        var sourceDirectory = Path.GetDirectoryName(
                Path.GetFullPath(sourceExecutablePath))
            ?? throw new AgentBenchmarkStartException(
                "The pinned executable has no parent directory.",
                retryable: false);
        var index = (exposure.ExecutableSearchPathEntries ?? [])
            .Select((path, candidate) => new { path, candidate })
            .Where(value => PathsEqual(value.path, sourceDirectory))
            .Select(static value => value.candidate)
            .DefaultIfEmpty(-1)
            .Single();
        if (index < 0)
        {
            throw new AgentBenchmarkStartException(
                "The pinned executable is not assigned to this condition's materialized tool path.",
                retryable: false);
        }

        var path = Path.Combine(
            GetMaterializedToolPath(input.WorkspacePath, index),
            Path.GetFileName(sourceExecutablePath));
        if (!File.Exists(path))
        {
            throw new AgentBenchmarkStartException(
                "The condition-assigned executable was not materialized for this run.",
                retryable: false);
        }

        return path;
    }

    internal static IReadOnlyList<string> GetAgentReadableRoots(
        AgentBenchmarkAdapterInput input)
    {
        var roots = new List<string>
        {
            Path.GetFullPath(input.WorkspacePath),
            Path.GetFullPath(GetMaterializedArtifactRoot(input.WorkspacePath)),
        };
        var runtimeStateRoot = GetRuntimeStateRoot(input);
        if (runtimeStateRoot is not null)
        {
            roots.Add(runtimeStateRoot);
        }

        return roots.AsReadOnly();
    }

    internal static IReadOnlyList<string> GetAgentReadableRoots(
        string workspacePath)
    {
        var workspace = Path.GetFullPath(workspacePath);
        var parent = Path.GetDirectoryName(workspace)
            ?? throw new AgentBenchmarkException(
                "The recorded benchmark workspace has no parent directory.");
        return Array.AsReadOnly(
            new[]
            {
                workspace,
                GetMaterializedArtifactRoot(workspace),
                Path.Combine(parent, "state"),
            });
    }

    private static IReadOnlyList<string> GetMaterializedSearchPathEntries(
        AgentBenchmarkAdapterInput input,
        CodexBenchmarkConditionExposure exposure)
    {
        var entries = Enumerable.Range(
                0,
                (exposure.ExecutableSearchPathEntries ?? []).Count)
            .Select(index => GetMaterializedToolPath(
                input.WorkspacePath,
                index))
            .ToArray();
        if (entries.Any(static path => !Directory.Exists(path)))
        {
            throw new AgentBenchmarkStartException(
                "The condition-assigned executable path was not materialized for this run.",
                retryable: false);
        }

        return Array.AsReadOnly(entries);
    }

    private static string GetMaterializedToolPath(
        string workspacePath,
        int index) =>
        Path.Combine(
            GetMaterializedArtifactRoot(workspacePath),
            "tools",
            index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));

    private static string GetMaterializedFeedPath(string workspacePath) =>
        Path.Combine(GetMaterializedArtifactRoot(workspacePath), "feed");

    private string? GetAuthenticationHomePath() =>
        _options.AuthenticationEnvironment.TryGetValue(
            "CODEX_HOME",
            out var path)
            ? Path.GetFullPath(path)
            : null;

    private static void MaterializeConditionArtifacts(
        string workspacePath,
        CodexBenchmarkConditionExposure exposure,
        CancellationToken cancellationToken)
    {
        var artifactRoot = GetMaterializedArtifactRoot(workspacePath);
        if (File.Exists(artifactRoot) || Directory.Exists(artifactRoot))
        {
            throw new AgentBenchmarkException(
                "The materialized benchmark run already contains agent-boundary artifacts.");
        }

        Directory.CreateDirectory(artifactRoot);
        var sourcePaths = exposure.ExecutableSearchPathEntries ?? [];
        for (var index = 0; index < sourcePaths.Count; index++)
        {
            CopyDirectory(
                sourcePaths[index],
                GetMaterializedToolPath(workspacePath, index),
                cancellationToken);
        }

        if ((exposure.EnvironmentVariables
             ?? new Dictionary<string, string>()).TryGetValue(
                "DNAXI_LOCAL_FEED",
                out var feedPath))
        {
            CopyDirectory(
                feedPath,
                GetMaterializedFeedPath(workspacePath),
                cancellationToken);
        }
    }

    private static string? GetRuntimeStateRoot(
        AgentBenchmarkAdapterInput input)
    {
        if (!input.EnvironmentVariables.TryGetValue(
                "DOTNET_CLI_HOME",
                out var dotNetHomePath))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(dotNetHomePath))
        {
            throw InvalidRuntimeState();
        }

        var runtimeStateRoot = Path.GetDirectoryName(
                Path.GetFullPath(dotNetHomePath))
            ?? throw InvalidRuntimeState();
        var workspaceParent = Path.GetDirectoryName(
                Path.GetFullPath(input.WorkspacePath))
            ?? throw InvalidRuntimeState();
        var runtimeStateParent = Path.GetDirectoryName(runtimeStateRoot);
        if (!PathsEqual(workspaceParent, runtimeStateParent)
            || IsContained(runtimeStateRoot, input.WorkspacePath))
        {
            throw InvalidRuntimeState();
        }

        foreach (var variableName in RuntimeStatePathVariables)
        {
            if (!input.EnvironmentVariables.TryGetValue(
                    variableName,
                    out var value))
            {
                continue;
            }

            if (!Path.IsPathFullyQualified(value)
                || !IsContained(runtimeStateRoot, value))
            {
                throw InvalidRuntimeState();
            }
        }

        return runtimeStateRoot;
    }

    private static bool IsContained(string root, string path)
    {
        var relative = Path.GetRelativePath(
            Path.GetFullPath(root),
            Path.GetFullPath(path));
        return !Path.IsPathFullyQualified(relative)
               && !string.Equals(relative, "..", StringComparison.Ordinal)
               && !relative.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   StringComparison.Ordinal)
               && !relative.StartsWith(
                   $"..{Path.AltDirectorySeparatorChar}",
                   StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string? right) =>
        right is not null
        && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static AgentBenchmarkStartException InvalidRuntimeState() =>
        new(
            "The isolated benchmark runtime state must be a workspace sibling and contain every declared writable runtime path.",
            retryable: false);

    private static void AddConfig(
        Collection<string> arguments,
        string key,
        string value)
    {
        arguments.Add("--config");
        arguments.Add($"{key}={JsonSerializer.Serialize(value)}");
    }

    internal static void CopySkillDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken) =>
        CopyDirectory(source, destination, cancellationToken);

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(source);
        var destinationRoot = Path.GetFullPath(destination);
        if (!Directory.Exists(sourceRoot)
            || IsContained(sourceRoot, destinationRoot))
        {
            throw new AgentBenchmarkException(
                "The pinned benchmark artifact source or destination is unsafe.");
        }

        RejectReparsePoint(sourceRoot);
        Directory.CreateDirectory(destination);
        CopyDirectoryEntries(sourceRoot, destinationRoot, cancellationToken);
    }

    private static void CopyDirectoryEntries(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     source,
                     "*",
                     SearchOption.TopDirectoryOnly)
                 .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(target);
                CopyDirectoryEntries(entry, target, cancellationToken);
            }
            else
            {
                File.Copy(entry, target, overwrite: false);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(target, File.GetUnixFileMode(entry));
                }
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new AgentBenchmarkException(
                $"The pinned benchmark artifact path '{path}' is a reparse point.");
        }
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
