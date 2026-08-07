using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;

namespace DotNetAxi.Testing;

internal static partial class CodexDiscoveryBenchmarkPreparation
{
    internal const string RequestSchema =
        "dotnet-axi/codex-discovery-request/v1";
    internal const string PreparationSchema =
        "dotnet-axi/codex-discovery-preparation/v1";
    internal const string SettingsSchema =
        "dotnet-axi/codex-discovery-settings/v1";
    internal const string ToolConfigurationSchema =
        "dotnet-axi/codex-discovery-tool-configuration/v1";
    internal const string CodexCliVersion = "codex-cli 0.146.0";
    internal const string ModelId = "gpt-5.6-sol";
    internal const string ReasoningSetting = "low";
    internal const string Sandbox = "read-only";
    internal const string PermissionProfile = "never";
    internal const string NetworkPolicy = "disabled";
    internal const string AuthenticationMethod = "chatgpt";
    internal const string ProductMilestone = "0.3.0";
    internal const string CorpusId = "source-discovery";
    internal const string CorpusVersion = "1.0.0";
    internal const string PackageId = "dotnet-axi";
    internal const string PackageVersion = "0.3.0";
    internal const string ProductSchema = "dotnet-axi/v1";
    internal const int RunsPerTask = 5;

    private static readonly string[] ExpectedTaskIds =
    [
        "file-handler-paths",
        "literal-archive-status",
        "regex-handler-methods",
        "syntax-attributed-classes",
        "syntax-catch-timeout",
        "syntax-invocation-record",
        "syntax-object-creation-archive-client",
    ];

    private static readonly string[] ExpectedCapabilities =
    [
        "search.file",
        "search.syntax.attributed-class",
        "search.syntax.catch",
        "search.syntax.invocation",
        "search.syntax.object-creation",
        "search.text.literal",
        "search.text.regex",
    ];

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
    };

    internal static async ValueTask<CodexDiscoveryPreparedContext>
        PrepareAsync(
            string requestPath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        if (!Path.IsPathFullyQualified(requestPath))
        {
            throw new AgentBenchmarkException(
                "The Codex discovery request path must be absolute.");
        }

        var fullRequestPath = Path.GetFullPath(requestPath);
        if (!File.Exists(fullRequestPath))
        {
            throw new AgentBenchmarkException(
                $"The Codex discovery request '{fullRequestPath}' does not exist.");
        }

        byte[] requestBytes;
        CodexDiscoveryBenchmarkRequest request;
        try
        {
            requestBytes = await File.ReadAllBytesAsync(
                fullRequestPath,
                cancellationToken);
            request = JsonSerializer.Deserialize<CodexDiscoveryBenchmarkRequest>(
                    requestBytes,
                    JsonOptions)
                ?? throw new AgentBenchmarkException(
                    "The Codex discovery request is empty.");
        }
        catch (JsonException exception)
        {
            throw new AgentBenchmarkException(
                "The Codex discovery request is not strict valid JSON.",
                exception);
        }

        ValidateRequestShape(request);
        var settings = await LoadPinnedJsonAsync<CodexDiscoverySettings>(
            request.Settings,
            "settings",
            cancellationToken);
        ValidateSettings(settings);
        var baselineTools =
            await LoadPinnedJsonAsync<CodexDiscoveryToolConfiguration>(
                request.Baseline.ToolConfiguration,
                "baseline tool configuration",
                cancellationToken);
        var candidateTools =
            await LoadPinnedJsonAsync<CodexDiscoveryToolConfiguration>(
                request.Candidate.ToolConfiguration,
                "candidate tool configuration",
                cancellationToken);
        await ValidateToolConfigurationAsync(
            baselineTools,
            AgentBenchmarkCondition.Baseline,
            cancellationToken);
        await ValidateToolConfigurationAsync(
            candidateTools,
            AgentBenchmarkCondition.Candidate,
            cancellationToken);
        ValidateConditionExposure(request, baselineTools, candidateTools);

        await ValidateFilePinAsync(
            request.CodexExecutable,
            "Codex executable",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Corpus.Artifact,
            "corpus",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Product.Package,
            "dotnet-axi package",
            cancellationToken);
        await ValidateDirectoryPinAsync(
            request.Product.Skill,
            "dotnet-axi skill",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Baseline.Instructions,
            "baseline instructions",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Candidate.Instructions,
            "candidate instructions",
            cancellationToken);
        await ValidateCodexRuntimeAsync(request, cancellationToken);

        AgentTaskCorpus corpus;
        try
        {
            corpus = await AgentTaskCorpusLoader.LoadAsync(
                request.Corpus.Artifact.Path,
                cancellationToken);
        }
        catch (AgentTaskCorpusException exception)
        {
            throw new AgentBenchmarkException(
                "The pinned source-discovery corpus is invalid.",
                exception);
        }

        if (!string.Equals(corpus.Id, CorpusId, StringComparison.Ordinal)
            || !string.Equals(
                corpus.Version,
                CorpusVersion,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The request does not select the controlled source-discovery corpus identity.");
        }

        var applicable = corpus.SelectApplicableTasks(
            ProductMilestone,
            request.Corpus.AvailableCapabilities);
        if (!applicable.Select(static task => task.Id).SequenceEqual(
                ExpectedTaskIds,
                StringComparer.Ordinal)
            || applicable.Any(static task =>
                !task.Applicability.Baseline
                || !task.Applicability.Candidate
                || task.Execution.PermittedTools.Contains(
                    "workspace-write",
                    StringComparer.Ordinal)))
        {
            throw new AgentBenchmarkException(
                "The request does not select the exact seven passive 0.3.0 discovery tasks for both conditions.");
        }

        var selectedCorpus = corpus with
        {
            Tasks = Array.AsReadOnly(applicable.ToArray()),
        };
        var baseline = new CodexBenchmarkConditionExposure(
            AgentBenchmarkCondition.Baseline,
            request.Baseline.Instructions.Sha256,
            request.Baseline.ToolConfiguration.Sha256,
            baselineTools.ConfigurationOverrides,
            baselineTools.ExecutableSearchPathEntries.Select(
                static entry => entry.Path).ToArray());
        var candidate = new CodexBenchmarkConditionExposure(
            AgentBenchmarkCondition.Candidate,
            request.Candidate.Instructions.Sha256,
            request.Candidate.ToolConfiguration.Sha256,
            candidateTools.ConfigurationOverrides,
            candidateTools.ExecutableSearchPathEntries.Select(
                static entry => entry.Path).ToArray());
        var adapter = new CodexAgentBenchmarkAdapter(
            new CodexAgentBenchmarkAdapterOptions(
                request.CodexExecutable.Path,
                CodexCliVersion,
                baseline,
                candidate,
                authenticationEnvironment:
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["CODEX_HOME"] = request.CodexHomePath,
                    }));
        var corpusDirectory = Path.GetDirectoryName(
                request.Corpus.Artifact.Path)
            ?? throw new AgentBenchmarkException(
                "The source-discovery corpus must have a parent directory.");
        var configuration = new AgentBenchmarkConfiguration(
            request.SeriesId,
            corpusDirectory,
            AgentBenchmarkDispatch.Manual,
            RunsPerTask,
            request.RandomizationSeed,
            request.MaximumStartAttempts,
            TimeSpan.FromSeconds(request.CleanupTimeoutSeconds),
            new AgentBenchmarkExecutionSettings(
                CodexCliVersion,
                ModelId,
                ReasoningSetting,
                request.Settings.Sha256,
                Sandbox,
                PermissionProfile,
                NetworkPolicy),
            new AgentBenchmarkProvenance(
                request.HarnessVersion,
                request.FixtureCommit,
                request.ProductCommit,
                ProductSchema),
            new AgentBenchmarkConditionConfiguration(
                AgentBenchmarkCondition.Baseline,
                request.Baseline.Instructions.Sha256,
                request.Baseline.ToolConfiguration.Sha256),
            new AgentBenchmarkConditionConfiguration(
                AgentBenchmarkCondition.Candidate,
                request.Candidate.Instructions.Sha256,
                request.Candidate.ToolConfiguration.Sha256));
        var prepared = new AgentBenchmarkRunner().Prepare(
            selectedCorpus,
            configuration,
            adapter);
        var taskTimeoutBudgetSeconds = checked(
            applicable.Sum(static task => task.Execution.TimeoutSeconds)
            * RunsPerTask
            * 2);
        var finalizationBudgetSeconds = checked(
            prepared.Schedule.Count
            * request.CleanupTimeoutSeconds
            * 2);
        var requestHash = HashBytes(requestBytes);
        var preparation = new CodexDiscoverySeriesPreparation(
            PreparationSchema,
            requestHash,
            new CodexDiscoveryRetainedPins(
                request.CodexExecutable.Path,
                request.CodexExecutable.Sha256,
                AgentBenchmarkHash.Compute(
                    Path.GetFullPath(request.CodexHomePath)),
                AuthenticationMethod,
                request.Settings.Path,
                request.Settings.Sha256,
                request.Corpus.Artifact.Path,
                request.Corpus.Artifact.Sha256,
                request.Product.PackageId,
                request.Product.PackageVersion,
                request.Product.Package.Path,
                request.Product.Package.Sha256,
                request.Product.Skill.Path,
                request.Product.Skill.Sha256,
                request.Baseline.Instructions.Path,
                request.Baseline.Instructions.Sha256,
                request.Baseline.ToolConfiguration.Path,
                request.Baseline.ToolConfiguration.Sha256,
                request.Candidate.Instructions.Path,
                request.Candidate.Instructions.Sha256,
                request.Candidate.ToolConfiguration.Path,
                request.Candidate.ToolConfiguration.Sha256,
                request.FixtureCommit,
                request.ProductCommit),
            prepared.Manifest,
            prepared.Schedule,
            new CodexDiscoveryUsageBoundary(
                prepared.Schedule.Count,
                taskTimeoutBudgetSeconds,
                finalizationBudgetSeconds,
                ProviderTokenLimit: null,
                "Manual authenticated dispatch is the external paid-usage boundary; the adapter has no provider token or cost ceiling."));
        return new CodexDiscoveryPreparedContext(
            request,
            settings,
            baselineTools,
            candidateTools,
            selectedCorpus,
            configuration,
            adapter,
            preparation);
    }

    internal static async ValueTask<CodexDiscoveryPreparedContext>
        ValidatePreparationAsync(
        string requestPath,
        string preparationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preparationPath);
        if (!Path.IsPathFullyQualified(preparationPath))
        {
            throw new AgentBenchmarkException(
                "The Codex discovery preparation path must be absolute.");
        }

        CodexDiscoverySeriesPreparation retained;
        try
        {
            await using var stream = File.OpenRead(preparationPath);
            retained = await JsonSerializer.DeserializeAsync<
                    CodexDiscoverySeriesPreparation>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                ?? throw new AgentBenchmarkException(
                    "The Codex discovery preparation is empty.");
        }
        catch (JsonException exception)
        {
            throw new AgentBenchmarkException(
                "The Codex discovery preparation is not strict valid JSON.",
                exception);
        }

        var current = await PrepareAsync(requestPath, cancellationToken);
        var retainedBytes = JsonSerializer.SerializeToUtf8Bytes(
            retained,
            JsonOptions);
        var currentBytes = JsonSerializer.SerializeToUtf8Bytes(
            current.Preparation,
            JsonOptions);
        if (!retainedBytes.AsSpan().SequenceEqual(currentBytes))
        {
            throw new AgentBenchmarkException(
                "The retained preparation does not match the current request, artifacts, corpus, or deterministic schedule.");
        }

        return current;
    }

    internal static async ValueTask WriteCreateNewAsync<T>(
        string outputPath,
        T value,
        CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(outputPath);
        if (!Path.IsPathFullyQualified(outputPath)
            || parent is null
            || !Directory.Exists(parent)
            || File.Exists(outputPath))
        {
            throw new AgentBenchmarkException(
                "Evidence output paths must be absolute, create-new files beneath an existing directory.");
        }

        await using var output = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(
            output,
            value,
            JsonOptions,
            cancellationToken);
        await output.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    internal static async ValueTask<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static async ValueTask<string> HashDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(path);
        RejectReparsePoint(root, "Skill root");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "dotnet-axi/codex-discovery-directory/v1"u8);
        var entries = Directory.EnumerateFileSystemEntries(
                root,
                "*",
                SearchOption.AllDirectories)
            .Select(entry => new
            {
                Path = entry,
                Relative = Path.GetRelativePath(root, entry)
                    .Replace(Path.DirectorySeparatorChar, '/'),
            })
            .OrderBy(static entry => entry.Relative, StringComparer.Ordinal)
            .ToArray();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(entry.Path, $"Skill entry '{entry.Relative}'");
            var directory = Directory.Exists(entry.Path);
            AppendHashField(hash, directory ? "directory"u8 : "file"u8);
            AppendHashField(hash, Encoding.UTF8.GetBytes(entry.Relative));
            if (!directory)
            {
                AppendHashField(
                    hash,
                    Convert.FromHexString(
                        await HashFileAsync(entry.Path, cancellationToken)));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static async ValueTask ValidateExecutionArtifactPinsAsync(
        CodexDiscoveryPreparedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;
        await ValidateFilePinAsync(
            request.CodexExecutable,
            "Codex executable",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Settings,
            "settings",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Corpus.Artifact,
            "corpus",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Product.Package,
            "dotnet-axi package",
            cancellationToken);
        await ValidateDirectoryPinAsync(
            request.Product.Skill,
            "dotnet-axi skill",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Baseline.Instructions,
            "baseline instructions",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Baseline.ToolConfiguration,
            "baseline tool configuration",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Candidate.Instructions,
            "candidate instructions",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Candidate.ToolConfiguration,
            "candidate tool configuration",
            cancellationToken);
        foreach (var entry in context.BaselineTools
                     .ExecutableSearchPathEntries)
        {
            await ValidateDirectoryPinAsync(
                entry,
                "baseline executable search path",
                cancellationToken);
        }

        foreach (var entry in context.CandidateTools
                     .ExecutableSearchPathEntries)
        {
            await ValidateDirectoryPinAsync(
                entry,
                "candidate executable search path",
                cancellationToken);
        }
    }

    private static void ValidateRequestShape(
        CodexDiscoveryBenchmarkRequest request)
    {
        if (!string.Equals(request.Schema, RequestSchema, StringComparison.Ordinal)
            || !IdentifierRegex().IsMatch(request.SeriesId ?? string.Empty)
            || request.CodexExecutable is null
            || request.Settings is null
            || request.Corpus is null
            || request.Corpus.Artifact is null
            || request.Product is null
            || request.Product.Package is null
            || request.Product.Skill is null
            || request.Baseline is null
            || request.Baseline.Instructions is null
            || request.Baseline.ToolConfiguration is null
            || request.Candidate is null
            || request.Candidate.Instructions is null
            || request.Candidate.ToolConfiguration is null
            || !Path.IsPathFullyQualified(request.CodexHomePath)
            || !Directory.Exists(request.CodexHomePath)
            || request.RunsPerTask != RunsPerTask
            || request.RandomizationSeed == 0
            || request.MaximumStartAttempts is < 1 or > 5
            || request.CleanupTimeoutSeconds is < 1 or > 30
            || !ExplicitVersionRegex().IsMatch(
                request.HarnessVersion ?? string.Empty)
            || !CommitRegex().IsMatch(request.FixtureCommit ?? string.Empty)
            || !CommitRegex().IsMatch(request.ProductCommit ?? string.Empty)
            || !string.Equals(
                request.Corpus.Id,
                CorpusId,
                StringComparison.Ordinal)
            || !string.Equals(
                request.Corpus.Version,
                CorpusVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                request.Corpus.ProductMilestone,
                ProductMilestone,
                StringComparison.Ordinal)
            || request.Corpus.AvailableCapabilities is null
            || !request.Corpus.AvailableCapabilities.SequenceEqual(
                ExpectedCapabilities,
                StringComparer.Ordinal)
            || !string.Equals(
                request.Product.PackageId,
                PackageId,
                StringComparison.Ordinal)
            || !string.Equals(
                request.Product.PackageVersion,
                PackageVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                request.Product.ProductSchema,
                ProductSchema,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The request does not pin the exact manual 0.3.0 Codex discovery series contract.");
        }

        ValidatePinShape(request.CodexExecutable, "Codex executable");
        ValidatePinShape(request.Settings, "settings");
        ValidatePinShape(request.Corpus.Artifact, "corpus");
        ValidatePinShape(request.Product.Package, "dotnet-axi package");
        ValidatePinShape(request.Product.Skill, "dotnet-axi skill");
        ValidatePinShape(request.Baseline.Instructions, "baseline instructions");
        ValidatePinShape(
            request.Baseline.ToolConfiguration,
            "baseline tool configuration");
        ValidatePinShape(request.Candidate.Instructions, "candidate instructions");
        ValidatePinShape(
            request.Candidate.ToolConfiguration,
            "candidate tool configuration");
        if (string.Equals(
                request.Baseline.Instructions.Sha256,
                request.Candidate.Instructions.Sha256,
                StringComparison.Ordinal)
            || string.Equals(
                request.Baseline.ToolConfiguration.Sha256,
                request.Candidate.ToolConfiguration.Sha256,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "Baseline and candidate instruction and concrete-tool hashes must be distinct.");
        }
    }

    private static void ValidateSettings(CodexDiscoverySettings settings)
    {
        if (!string.Equals(settings.Schema, SettingsSchema, StringComparison.Ordinal)
            || !string.Equals(
                settings.CodexCliVersion,
                CodexCliVersion,
                StringComparison.Ordinal)
            || !string.Equals(settings.ModelId, ModelId, StringComparison.Ordinal)
            || !string.Equals(
                settings.ReasoningSetting,
                ReasoningSetting,
                StringComparison.Ordinal)
            || !string.Equals(settings.Sandbox, Sandbox, StringComparison.Ordinal)
            || !string.Equals(
                settings.PermissionProfile,
                PermissionProfile,
                StringComparison.Ordinal)
            || !string.Equals(
                settings.NetworkPolicy,
                NetworkPolicy,
                StringComparison.Ordinal)
            || !string.Equals(
                settings.AuthenticationMethod,
                AuthenticationMethod,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The settings artifact does not pin the approved Codex 0.3.0 execution profile.");
        }
    }

    private static async ValueTask ValidateToolConfigurationAsync(
        CodexDiscoveryToolConfiguration tools,
        AgentBenchmarkCondition condition,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                tools.Schema,
                ToolConfigurationSchema,
                StringComparison.Ordinal)
            || tools.ConfigurationOverrides is null
            || tools.ExecutableSearchPathEntries is null)
        {
            throw new AgentBenchmarkException(
                $"The {condition} concrete-tool configuration is malformed.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in tools.ConfigurationOverrides)
        {
            var separator = value?.IndexOf('=') ?? -1;
            var key = separator > 0 ? value![..separator].Trim() : string.Empty;
            if (!key.StartsWith("skills.", StringComparison.Ordinal)
                || !keys.Add(key))
            {
                throw new AgentBenchmarkException(
                    $"The {condition} concrete-tool configuration contains an unsupported or duplicate override.");
            }
        }

        if (tools.ExecutableSearchPathEntries.Count == 0
            || tools.ExecutableSearchPathEntries.Any(entry =>
                entry is null
                || entry.Path.Contains(Path.PathSeparator))
            || tools.ExecutableSearchPathEntries.Select(
                    static entry => entry.Path).Distinct(
                    StringComparer.Ordinal).Count()
                != tools.ExecutableSearchPathEntries.Count)
        {
            throw new AgentBenchmarkException(
                $"The {condition} executable search path must contain unique existing absolute directories.");
        }

        foreach (var entry in tools.ExecutableSearchPathEntries)
        {
            await ValidateDirectoryPinAsync(
                entry,
                $"{condition} executable search path",
                cancellationToken);
        }

        if (condition is AgentBenchmarkCondition.Baseline)
        {
            if (!tools.ConfigurationOverrides.SequenceEqual(
                    [
                        "skills.config=[]",
                    ],
                    StringComparer.Ordinal))
            {
                throw new AgentBenchmarkException(
                    "The baseline must disable skill exposure.");
            }
        }
        else if (tools.ConfigurationOverrides.Count != 1
                 || !tools.ConfigurationOverrides[0].StartsWith(
                     "skills.config=",
                     StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The candidate must configure only the packaged skill; dnaxi is a CLI, not an MCP server.");
        }
    }

    private static void ValidateConditionExposure(
        CodexDiscoveryBenchmarkRequest request,
        CodexDiscoveryToolConfiguration baseline,
        CodexDiscoveryToolConfiguration candidate)
    {
        var packageDirectory = Path.GetDirectoryName(
                request.Product.Package.Path)
            ?? string.Empty;
        var packageName = Path.GetFileName(request.Product.Package.Path);
        var skillFile = Path.Combine(request.Product.Skill.Path, "SKILL.md");
        var expectedSkillOverride =
            $"skills.config=[{{path={JsonSerializer.Serialize(skillFile)},enabled=true}}]";
        if (!(string.Equals(packageName, "dnaxi", StringComparison.Ordinal)
              || string.Equals(packageName, "dnaxi.exe", StringComparison.Ordinal))
            || !IsExecutableFile(request.Product.Package.Path)
            || !IsExecutableFile(request.CodexExecutable.Path)
            || !string.Equals(
                request.Candidate.Instructions.Path,
                skillFile,
                StringComparison.Ordinal)
            || baseline.ExecutableSearchPathEntries.Any(entry =>
                string.Equals(
                    entry.Path,
                    packageDirectory,
                    StringComparison.Ordinal))
            || baseline.ExecutableSearchPathEntries.Any(entry =>
                ContainsDnaxi(entry.Path))
            || candidate.ExecutableSearchPathEntries.Count
                != baseline.ExecutableSearchPathEntries.Count + 1
            || !string.Equals(
                candidate.ExecutableSearchPathEntries[0].Path,
                packageDirectory,
                StringComparison.Ordinal)
            || !candidate.ExecutableSearchPathEntries.Skip(1).Select(
                    static entry => entry.Path).SequenceEqual(
                    baseline.ExecutableSearchPathEntries.Select(
                        static entry => entry.Path),
                    StringComparer.Ordinal)
            || !string.Equals(
                candidate.ConfigurationOverrides[0],
                expectedSkillOverride,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The baseline must use a dnaxi-free raw-tool path, and the candidate must add only the pinned dnaxi CLI directory and packaged SKILL.md.");
        }
    }

    private static bool ContainsDnaxi(string directory) =>
        File.Exists(Path.Combine(directory, "dnaxi"))
        || File.Exists(Path.Combine(directory, "dnaxi.exe"));

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return string.Equals(
                Path.GetExtension(path),
                ".exe",
                StringComparison.OrdinalIgnoreCase);
        }

        const UnixFileMode executable = UnixFileMode.UserExecute
                                        | UnixFileMode.GroupExecute
                                        | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & executable) != 0;
    }

    private static async ValueTask ValidateCodexRuntimeAsync(
        CodexDiscoveryBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CODEX_HOME"] = request.CodexHomePath,
            ["HOME"] = request.CodexHomePath,
            ["USERPROFILE"] = request.CodexHomePath,
        };
        foreach (var name in new[]
                 {
                     "COMSPEC", "ComSpec", "PATHEXT", "SystemRoot", "WINDIR",
                 })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                environment[name] = value;
            }
        }

        var workingDirectory = Path.GetDirectoryName(
                request.CodexExecutable.Path)
            ?? throw new AgentBenchmarkException(
                "The pinned Codex executable has no parent directory.");
        var version = await RunCodexProbeAsync(
            request.CodexExecutable.Path,
            workingDirectory,
            ["--version"],
            environment,
            cancellationToken);
        if (!string.Equals(
                version.StandardOutput.Trim(),
                CodexCliVersion,
                StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(version.StandardError))
        {
            throw new AgentBenchmarkException(
                "The pinned Codex executable did not report the required CLI version.");
        }

        var authentication = await RunCodexProbeAsync(
            request.CodexExecutable.Path,
            workingDirectory,
            ["login", "status"],
            environment,
            cancellationToken);
        var authenticationLines = string.Concat(
                authentication.StandardOutput,
                "\n",
                authentication.StandardError)
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries);
        if (!authenticationLines.SequenceEqual(
                ["Logged in using ChatGPT"],
                StringComparer.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The isolated Codex home must report active ChatGPT authentication; API-key authentication is not accepted.");
        }
    }

    private static async ValueTask<CodexProbeOutput> RunCodexProbeAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var result = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                executablePath,
                workingDirectory,
                arguments,
                environment,
                new ProcessOutputLimits(8 * 1024, 8 * 1024),
                TimeSpan.FromSeconds(10)),
            cancellationToken);
        if (result.Lifecycle is not ProcessLifecycle.Completed
            || result.Outcome is not ProcessRunOutcome.Completed
            || result.Exit?.ExitCode != 0
            || result.StandardOutput.LimitExceeded
            || result.StandardError.LimitExceeded)
        {
            throw new AgentBenchmarkException(
                "The pinned Codex executable failed its bounded local identity probe.");
        }

        return new CodexProbeOutput(
            result.StandardOutput.Text,
            result.StandardError.Text);
    }

    private static async ValueTask<T> LoadPinnedJsonAsync<T>(
        CodexDiscoveryArtifactPin pin,
        string field,
        CancellationToken cancellationToken)
    {
        await ValidateFilePinAsync(pin, field, cancellationToken);
        try
        {
            await using var stream = File.OpenRead(pin.Path);
            return await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                ?? throw new AgentBenchmarkException(
                    $"The pinned {field} artifact is empty.");
        }
        catch (JsonException exception)
        {
            throw new AgentBenchmarkException(
                $"The pinned {field} artifact is not strict valid JSON.",
                exception);
        }
    }

    private static async ValueTask ValidateFilePinAsync(
        CodexDiscoveryArtifactPin pin,
        string field,
        CancellationToken cancellationToken)
    {
        ValidatePinShape(pin, field);
        if (!File.Exists(pin.Path))
        {
            throw new AgentBenchmarkException(
                $"The pinned {field} file '{pin.Path}' does not exist.");
        }

        RejectReparsePoint(pin.Path, field);
        var actual = await HashFileAsync(pin.Path, cancellationToken);
        if (!string.Equals(actual, pin.Sha256, StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                $"The pinned {field} SHA-256 does not match '{pin.Path}'.");
        }
    }

    private static async ValueTask ValidateDirectoryPinAsync(
        CodexDiscoveryArtifactPin pin,
        string field,
        CancellationToken cancellationToken)
    {
        ValidatePinShape(pin, field);
        if (!Directory.Exists(pin.Path))
        {
            throw new AgentBenchmarkException(
                $"The pinned {field} directory '{pin.Path}' does not exist.");
        }

        var actual = await HashDirectoryAsync(pin.Path, cancellationToken);
        if (!string.Equals(actual, pin.Sha256, StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                $"The pinned {field} SHA-256 does not match '{pin.Path}'.");
        }
    }

    private static void ValidatePinShape(
        CodexDiscoveryArtifactPin pin,
        string field)
    {
        if (!Path.IsPathFullyQualified(pin.Path)
            || !AgentBenchmarkHash.IsHash(pin.Sha256))
        {
            throw new AgentBenchmarkException(
                $"The pinned {field} path must be absolute and its SHA-256 lowercase hexadecimal.");
        }
    }

    private static void RejectReparsePoint(string path, string field)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new AgentBenchmarkException(
                $"{field} cannot be a symbolic link or reparse point.");
        }
    }

    private static string HashBytes(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static void AppendHashField(
        IncrementalHash hash,
        ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    [GeneratedRegex(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitVersionRegex();

    [GeneratedRegex(
        "^(?:[0-9a-f]{40}|[0-9a-f]{64})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommitRegex();
}

internal sealed record CodexDiscoveryArtifactPin(
    string Path,
    string Sha256);

internal sealed record CodexProbeOutput(
    string StandardOutput,
    string StandardError);

internal sealed record CodexDiscoveryCorpusPin(
    string Id,
    string Version,
    string ProductMilestone,
    CodexDiscoveryArtifactPin Artifact,
    IReadOnlyList<string> AvailableCapabilities);

internal sealed record CodexDiscoveryProductPin(
    string PackageId,
    string PackageVersion,
    string ProductSchema,
    CodexDiscoveryArtifactPin Package,
    CodexDiscoveryArtifactPin Skill);

internal sealed record CodexDiscoveryConditionPin(
    CodexDiscoveryArtifactPin Instructions,
    CodexDiscoveryArtifactPin ToolConfiguration);

internal sealed record CodexDiscoveryBenchmarkRequest(
    string Schema,
    string SeriesId,
    CodexDiscoveryArtifactPin CodexExecutable,
    string CodexHomePath,
    CodexDiscoveryArtifactPin Settings,
    CodexDiscoveryCorpusPin Corpus,
    CodexDiscoveryProductPin Product,
    int RunsPerTask,
    ulong RandomizationSeed,
    int MaximumStartAttempts,
    int CleanupTimeoutSeconds,
    string HarnessVersion,
    string FixtureCommit,
    string ProductCommit,
    CodexDiscoveryConditionPin Baseline,
    CodexDiscoveryConditionPin Candidate);

internal sealed record CodexDiscoverySettings(
    string Schema,
    string CodexCliVersion,
    string ModelId,
    string ReasoningSetting,
    string Sandbox,
    string PermissionProfile,
    string NetworkPolicy,
    string AuthenticationMethod);

internal sealed record CodexDiscoveryToolConfiguration(
    string Schema,
    IReadOnlyList<string> ConfigurationOverrides,
    IReadOnlyList<CodexDiscoveryArtifactPin> ExecutableSearchPathEntries);

internal sealed record CodexDiscoveryRetainedPins(
    string CodexExecutablePath,
    string CodexExecutableHash,
    string AuthenticationHomePathHash,
    string AuthenticationMethod,
    string SettingsPath,
    string SettingsHash,
    string CorpusPath,
    string CorpusHash,
    string PackageId,
    string PackageVersion,
    string PackagePath,
    string PackageHash,
    string SkillPath,
    string SkillHash,
    string BaselineInstructionsPath,
    string BaselineInstructionsHash,
    string BaselineToolConfigurationPath,
    string BaselineToolConfigurationHash,
    string CandidateInstructionsPath,
    string CandidateInstructionsHash,
    string CandidateToolConfigurationPath,
    string CandidateToolConfigurationHash,
    string FixtureCommit,
    string ProductCommit);

internal sealed record CodexDiscoveryUsageBoundary(
    int RunCount,
    int AgentTimeoutBudgetSeconds,
    int FinalizationBudgetSeconds,
    long? ProviderTokenLimit,
    string Detail);

internal sealed record CodexDiscoverySeriesPreparation(
    string Schema,
    string RequestHash,
    CodexDiscoveryRetainedPins Pins,
    AgentBenchmarkSeriesManifest Manifest,
    IReadOnlyList<AgentBenchmarkScheduledRun> Schedule,
    CodexDiscoveryUsageBoundary UsageBoundary);

internal sealed record CodexDiscoveryPreparedContext(
    CodexDiscoveryBenchmarkRequest Request,
    CodexDiscoverySettings Settings,
    CodexDiscoveryToolConfiguration BaselineTools,
    CodexDiscoveryToolConfiguration CandidateTools,
    AgentTaskCorpus Corpus,
    AgentBenchmarkConfiguration Configuration,
    CodexAgentBenchmarkAdapter Adapter,
    CodexDiscoverySeriesPreparation Preparation);
