using System.Buffers.Binary;
using System.IO.Compression;
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
        "dotnet-axi/codex-discovery-request/v3";
    internal const string PreparationSchema =
        "dotnet-axi/codex-discovery-preparation/v3";
    internal const string SettingsSchema =
        "dotnet-axi/codex-discovery-settings/v1";
    internal const string ToolConfigurationSchema =
        "dotnet-axi/codex-discovery-tool-configuration/v3";
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
    internal const string PackageId = "dnaxi";
    internal const string PackageVersion = "0.4.0";
    internal const string ProductSchema = "dotnet-axi/v1";
    internal const string PackageSourceEnvironmentVariable =
        "DNAXI_LOCAL_FEED";
    internal const string PriorSummarySchema =
        "dotnet-axi/codex-discovery-summary/v1";
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

    private static readonly IReadOnlyDictionary<string, string>
        ExpectedCapabilityByTask = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["file-handler-paths"] = "search.file",
            ["literal-archive-status"] = "search.text.literal",
            ["regex-handler-methods"] = "search.text.regex",
            ["syntax-attributed-classes"] =
                "search.syntax.attributed-class",
            ["syntax-catch-timeout"] = "search.syntax.catch",
            ["syntax-invocation-record"] = "search.syntax.invocation",
            ["syntax-object-creation-archive-client"] =
                "search.syntax.object-creation",
        };

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
        var priorSeries = await LoadPriorSeriesSummaryAsync(
            request.PriorSeries,
            cancellationToken);
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
            request,
            cancellationToken);
        await ValidateToolConfigurationAsync(
            candidateTools,
            AgentBenchmarkCondition.Candidate,
            request,
            cancellationToken);
        ValidateConditionExposure(request, baselineTools, candidateTools);

        await ValidateFilePinAsync(
            request.CodexExecutable,
            "Codex executable",
            cancellationToken);
        await ValidateFilePinAsync(
            request.DnxExecutable,
            "dnx executable",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Corpus.Artifact,
            "corpus",
            cancellationToken);
        await ValidateFilePinAsync(
            request.Product.Package,
            "dnaxi package",
            cancellationToken);
        await ValidateDirectoryPinAsync(
            request.Product.PackageSource,
            "dnaxi package source",
            cancellationToken);
        await ValidateDirectoryPinAsync(
            request.Product.Skill,
            "dnaxi repository skill",
            cancellationToken);
        await ValidateSeparatedProductArtifactsAsync(
            request.Product,
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
        await ValidatePromptInputExposureAsync(request, cancellationToken);

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
                || task.RequiredCapabilities.Count != 1
                || !ExpectedCapabilityByTask.TryGetValue(
                    task.Id,
                    out var expectedCapability)
                || !string.Equals(
                    task.RequiredCapabilities[0],
                    expectedCapability,
                    StringComparison.Ordinal)
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
                static entry => entry.Path).ToArray(),
            baselineTools.EnvironmentVariables,
            baselineTools.SkillDirectoryPath);
        var candidate = new CodexBenchmarkConditionExposure(
            AgentBenchmarkCondition.Candidate,
            request.Candidate.Instructions.Sha256,
            request.Candidate.ToolConfiguration.Sha256,
            candidateTools.ConfigurationOverrides,
            candidateTools.ExecutableSearchPathEntries.Select(
                static entry => entry.Path).ToArray(),
            candidateTools.EnvironmentVariables,
            candidateTools.SkillDirectoryPath);
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
                    },
                expectedDnxExecutablePath: request.DnxExecutable.Path));
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
                request.DnxExecutable.Path,
                request.DnxExecutable.Sha256,
                request.Product.PackageId,
                request.Product.PackageVersion,
                request.Product.Package.Path,
                request.Product.Package.Sha256,
                request.Product.PackageSource.Path,
                request.Product.PackageSource.Sha256,
                request.Product.Skill.Path,
                request.Product.Skill.Sha256,
                request.PriorSeries.Summary.Path,
                request.PriorSeries.Summary.Sha256,
                request.PriorSeries.RequestHash,
                request.PriorSeries.ReportHash,
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
            priorSeries,
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
            request.DnxExecutable,
            "dnx executable",
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
            "dnaxi package",
            cancellationToken);
        await ValidateDirectoryPinAsync(
            request.Product.PackageSource,
            "dnaxi package source",
            cancellationToken);
        await ValidateDirectoryPinAsync(
            request.Product.Skill,
            "dnaxi repository skill",
            cancellationToken);
        await ValidateFilePinAsync(
            request.PriorSeries.Summary,
            "retained 0.3.0 summary",
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
            || request.DnxExecutable is null
            || request.Settings is null
            || request.Corpus is null
            || request.Corpus.Artifact is null
            || request.Product is null
            || request.Product.Package is null
            || request.Product.PackageSource is null
            || request.Product.Skill is null
            || request.PriorSeries is null
            || request.PriorSeries.Summary is null
            || !AgentBenchmarkHash.IsHash(request.PriorSeries.RequestHash)
            || !AgentBenchmarkHash.IsHash(request.PriorSeries.ReportHash)
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
                "The request does not pin the exact manual dnx-first 0.4.0 Codex discovery series contract.");
        }

        ValidatePinShape(request.CodexExecutable, "Codex executable");
        ValidatePinShape(request.DnxExecutable, "dnx executable");
        ValidatePinShape(request.Settings, "settings");
        ValidatePinShape(request.Corpus.Artifact, "corpus");
        ValidatePinShape(request.Product.Package, "dnaxi package");
        ValidatePinShape(request.Product.PackageSource, "dnaxi package source");
        ValidatePinShape(request.Product.Skill, "dnaxi repository skill");
        ValidatePinShape(
            request.PriorSeries.Summary,
            "retained 0.3.0 summary");
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
                "The settings artifact does not pin the approved Codex 0.4.0 self-hosting execution profile.");
        }
    }

    private static async ValueTask ValidateToolConfigurationAsync(
        CodexDiscoveryToolConfiguration tools,
        AgentBenchmarkCondition condition,
        CodexDiscoveryBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                tools.Schema,
                ToolConfigurationSchema,
                StringComparison.Ordinal)
            || tools.ConfigurationOverrides is null
            || tools.ExecutableSearchPathEntries is null
            || tools.EnvironmentVariables is null)
        {
            throw new AgentBenchmarkException(
                $"The {condition} concrete-tool configuration is malformed.");
        }

        if (tools.ConfigurationOverrides.Count != 0)
        {
            throw new AgentBenchmarkException(
                $"The {condition} must not use Codex configuration to inject an Agent Skill.");
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
            if (tools.SkillDirectoryPath is not null
                || tools.EnvironmentVariables.Count != 0)
            {
                throw new AgentBenchmarkException(
                    "The baseline must disable skill exposure.");
            }
        }
        else if (!string.Equals(
                     tools.SkillDirectoryPath,
                     request.Product.Skill.Path,
                     StringComparison.Ordinal)
                 || tools.EnvironmentVariables.Count != 1
                 || !tools.EnvironmentVariables.TryGetValue(
                     PackageSourceEnvironmentVariable,
                     out var packageSource)
                 || !string.Equals(
                     packageSource,
                     request.Product.PackageSource.Path,
                     StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The candidate must configure only the repository skill and its pinned local feed; dnaxi is a CLI, not an MCP server.");
        }
    }

    private static void ValidateConditionExposure(
        CodexDiscoveryBenchmarkRequest request,
        CodexDiscoveryToolConfiguration baseline,
        CodexDiscoveryToolConfiguration candidate)
    {
        var packageName = Path.GetFileName(request.Product.Package.Path);
        var packageSource = Path.GetFullPath(
            request.Product.PackageSource.Path);
        var packageDirectory = Path.GetDirectoryName(
                Path.GetFullPath(request.Product.Package.Path))
            ?? string.Empty;
        var dnxDirectory = Path.GetDirectoryName(
                Path.GetFullPath(request.DnxExecutable.Path))
            ?? string.Empty;
        var skillFile = Path.Combine(request.Product.Skill.Path, "SKILL.md");
        var expectedPackageName =
            $"{PackageId}.{PackageVersion}.nupkg";
        if (!string.Equals(
                packageName,
                expectedPackageName,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                packageDirectory,
                packageSource,
                StringComparison.Ordinal)
            || !(string.Equals(
                     Path.GetFileName(request.DnxExecutable.Path),
                     "dnx",
                     StringComparison.Ordinal)
                 || string.Equals(
                     Path.GetFileName(request.DnxExecutable.Path),
                     "dnx.exe",
                     StringComparison.Ordinal))
            || !IsExecutableFile(request.DnxExecutable.Path)
            || !IsExecutableFile(request.CodexExecutable.Path)
            || !string.Equals(
                request.Candidate.Instructions.Path,
                skillFile,
                StringComparison.Ordinal)
            || baseline.ExecutableSearchPathEntries.Any(entry =>
                ContainsDnaxi(entry.Path))
            || candidate.ExecutableSearchPathEntries.Any(entry =>
                ContainsDnaxi(entry.Path))
            || baseline.ExecutableSearchPathEntries.Count == 0
            || !string.Equals(
                baseline.ExecutableSearchPathEntries[0].Path,
                dnxDirectory,
                StringComparison.Ordinal)
            || baseline.ExecutableSearchPathEntries.Skip(1).Any(entry =>
                ContainsDnx(entry.Path))
            || !candidate.ExecutableSearchPathEntries.Select(
                    static entry => entry.Path).SequenceEqual(
                    baseline.ExecutableSearchPathEntries.Select(
                        static entry => entry.Path),
                    StringComparer.Ordinal)
            || baseline.SkillDirectoryPath is not null
            || !string.Equals(
                candidate.SkillDirectoryPath,
                request.Product.Skill.Path,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "Both conditions must use the same dnaxi-free raw-tool path with pinned dnx, while the candidate adds only the repository skill through project-local discovery and the pinned local feed environment.");
        }
    }

    private static bool ContainsDnaxi(string directory) =>
        File.Exists(Path.Combine(directory, "dnaxi"))
        || File.Exists(Path.Combine(directory, "dnaxi.exe"));

    private static bool ContainsDnx(string directory) =>
        File.Exists(Path.Combine(directory, "dnx"))
        || File.Exists(Path.Combine(directory, "dnx.exe"));

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

    private static async ValueTask ValidateSeparatedProductArtifactsAsync(
        CodexDiscoveryProductPin product,
        CancellationToken cancellationToken)
    {
        var packagePath = Path.GetFullPath(product.Package.Path);
        var sourceFiles = Directory.EnumerateFiles(
                product.PackageSource.Path,
                "*",
                SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (sourceFiles.Length != 1
            || !string.Equals(
                sourceFiles[0],
                packagePath,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The pinned local feed must contain only the exact candidate dnaxi package.");
        }

        var skillFiles = Directory.EnumerateFiles(
                product.Skill.Path,
                "*",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Relative = Path.GetRelativePath(product.Skill.Path, path)
                    .Replace(Path.DirectorySeparatorChar, '/'),
            })
            .OrderBy(static file => file.Relative, StringComparer.Ordinal)
            .ToArray();
        using var packageStream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(
            packageStream,
            ZipArchiveMode.Read,
            leaveOpen: false);
        var packagedSkill = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Replace('\\', '/').StartsWith(
                "skills/",
                StringComparison.OrdinalIgnoreCase));
        if (packagedSkill is not null)
        {
            throw new AgentBenchmarkException(
                $"The candidate tool package must not carry Agent Skill entry '{packagedSkill.FullName}'.");
        }

        if (!skillFiles.Select(static file => file.Relative).SequenceEqual(
                [
                    "SKILL.md",
                    "references/codex.md",
                ],
                StringComparer.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The pinned repository skill must contain only SKILL.md and references/codex.md.");
        }

        var skillFile = Path.Combine(product.Skill.Path, "SKILL.md");
        var skillBytes = await File.ReadAllBytesAsync(
            skillFile,
            cancellationToken);
        var skill = await File.ReadAllTextAsync(
            skillFile,
            Encoding.UTF8,
            cancellationToken);
        var exactInvocation =
            $"dnx {PackageId}@{PackageVersion} --source \"${PackageSourceEnvironmentVariable}\" --verbosity quiet -- <command>";
        if (skillBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            || !skill.StartsWith(
                "---\nname: dotnet-axi\ndescription: ",
                StringComparison.Ordinal)
            || skill.Contains("<exact-version>", StringComparison.Ordinal)
            || !skill.Contains(exactInvocation, StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The repository skill must be BOM-free, discoverable, and expose the exact source-pinned candidate dnx invocation.");
        }
    }

    private static async ValueTask<CodexDiscoveryPriorSeriesIdentity>
        LoadPriorSeriesSummaryAsync(
        CodexDiscoveryPriorSeriesPin prior,
        CancellationToken cancellationToken)
    {
        await ValidateFilePinAsync(
            prior.Summary,
            "retained 0.3.0 summary",
            cancellationToken);
        try
        {
            await using var stream = File.OpenRead(prior.Summary.Path);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            var identity = new CodexDiscoveryPriorSeriesIdentity(
                root.GetProperty("schema").GetString() ?? string.Empty,
                root.GetProperty("requestHash").GetString() ?? string.Empty,
                root.GetProperty("reportHash").GetString() ?? string.Empty,
                root.GetProperty("evidenceStatus").GetString() ?? string.Empty,
                root.GetProperty("comparison").GetString() ?? string.Empty,
                root.GetProperty("expectedRunCount").GetInt32(),
                root.GetProperty("retainedRunCount").GetInt32());
            if (!string.Equals(
                    identity.Schema,
                    PriorSummarySchema,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.RequestHash,
                    prior.RequestHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.ReportHash,
                    prior.ReportHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.EvidenceStatus,
                    "failed",
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.Comparison,
                    "incomparable",
                    StringComparison.Ordinal)
                || identity.ExpectedRunCount != RunsPerTask
                    * ExpectedTaskIds.Length * 2
                || identity.RetainedRunCount != identity.ExpectedRunCount)
            {
                throw new AgentBenchmarkException(
                    "The prior-series pin does not identify the retained failed/incomparable 0.3.0 discovery result.");
            }

            return identity;
        }
        catch (Exception exception)
            when (exception is JsonException
                  or InvalidOperationException
                  or KeyNotFoundException
                  or FormatException)
        {
            throw new AgentBenchmarkException(
                "The retained 0.3.0 summary is malformed.",
                exception);
        }
    }

    private static async ValueTask ValidateCodexRuntimeAsync(
        CodexDiscoveryBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        var environment = CreateCodexProbeEnvironment(request);

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

    private static async ValueTask ValidatePromptInputExposureAsync(
        CodexDiscoveryBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        var preflightRoot = Directory.CreateTempSubdirectory(
            "dnaxi-codex-skill-preflight-").FullName;
        try
        {
            var baselineWorkspace = Directory.CreateDirectory(
                Path.Combine(preflightRoot, "baseline")).FullName;
            var candidateWorkspace = Directory.CreateDirectory(
                Path.Combine(preflightRoot, "candidate")).FullName;
            CodexAgentBenchmarkAdapter.CopySkillDirectory(
                request.Product.Skill.Path,
                Path.Combine(
                    candidateWorkspace,
                    ".agents",
                    "skills",
                    "dotnet-axi"),
                cancellationToken);

            var environment = CreateCodexProbeEnvironment(request);
            const string prompt =
                "Find C# files containing Archive pipeline ready.";
            var baseline = await RunCodexProbeAsync(
                request.CodexExecutable.Path,
                baselineWorkspace,
                ["-C", baselineWorkspace, "debug", "prompt-input", prompt],
                environment,
                cancellationToken);
            var candidate = await RunCodexProbeAsync(
                request.CodexExecutable.Path,
                candidateWorkspace,
                ["-C", candidateWorkspace, "debug", "prompt-input", prompt],
                environment,
                cancellationToken);
            const string skillMarker = "- dotnet-axi:";
            var candidateOutput = candidate.StandardOutput
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Replace('\\', '/');
            var candidateSkillFile = Path.Combine(
                    candidateWorkspace,
                    ".agents",
                    "skills",
                    "dotnet-axi",
                    "SKILL.md")
                .Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(baseline.StandardError)
                || !string.IsNullOrWhiteSpace(candidate.StandardError)
                || CountOccurrences(
                    baseline.StandardOutput,
                    skillMarker) != 0
                || CountOccurrences(candidateOutput, skillMarker) != 1
                || !candidateOutput.Contains(
                    candidateSkillFile,
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    "The Codex prompt-input preflight did not prove candidate-only project-local dotnet-axi skill discovery.");
            }
        }
        finally
        {
            Directory.Delete(preflightRoot, recursive: true);
        }
    }

    private static Dictionary<string, string> CreateCodexProbeEnvironment(
        CodexDiscoveryBenchmarkRequest request)
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

        return environment;
    }

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(
                   expected,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += expected.Length;
        }

        return count;
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
                new ProcessOutputLimits(1024 * 1024, 64 * 1024),
                TimeSpan.FromSeconds(10)),
            cancellationToken);
        if (result.Lifecycle is not ProcessLifecycle.Completed
            || result.Outcome is not ProcessRunOutcome.Completed
            || result.Exit?.ExitCode != 0
            || result.StandardOutput.LimitExceeded
            || result.StandardError.LimitExceeded)
        {
            throw new AgentBenchmarkException(
                "The pinned Codex executable failed its bounded local probe.");
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
    CodexDiscoveryArtifactPin PackageSource,
    CodexDiscoveryArtifactPin Skill);

internal sealed record CodexDiscoveryPriorSeriesPin(
    CodexDiscoveryArtifactPin Summary,
    string RequestHash,
    string ReportHash);

internal sealed record CodexDiscoveryPriorSeriesIdentity(
    string Schema,
    string RequestHash,
    string ReportHash,
    string EvidenceStatus,
    string Comparison,
    int ExpectedRunCount,
    int RetainedRunCount);

internal sealed record CodexDiscoveryConditionPin(
    CodexDiscoveryArtifactPin Instructions,
    CodexDiscoveryArtifactPin ToolConfiguration);

internal sealed record CodexDiscoveryBenchmarkRequest(
    string Schema,
    string SeriesId,
    CodexDiscoveryArtifactPin CodexExecutable,
    CodexDiscoveryArtifactPin DnxExecutable,
    string CodexHomePath,
    CodexDiscoveryArtifactPin Settings,
    CodexDiscoveryCorpusPin Corpus,
    CodexDiscoveryProductPin Product,
    CodexDiscoveryPriorSeriesPin PriorSeries,
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
    string? SkillDirectoryPath,
    IReadOnlyList<string> ConfigurationOverrides,
    IReadOnlyList<CodexDiscoveryArtifactPin> ExecutableSearchPathEntries,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

internal sealed record CodexDiscoveryRetainedPins(
    string CodexExecutablePath,
    string CodexExecutableHash,
    string AuthenticationHomePathHash,
    string AuthenticationMethod,
    string SettingsPath,
    string SettingsHash,
    string CorpusPath,
    string CorpusHash,
    string DnxExecutablePath,
    string DnxExecutableHash,
    string PackageId,
    string PackageVersion,
    string PackagePath,
    string PackageHash,
    string PackageSourcePath,
    string PackageSourceHash,
    string SkillPath,
    string SkillHash,
    string PriorSummaryPath,
    string PriorSummaryHash,
    string PriorRequestHash,
    string PriorReportHash,
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
    CodexDiscoveryPriorSeriesIdentity PriorSeries,
    AgentTaskCorpus Corpus,
    AgentBenchmarkConfiguration Configuration,
    CodexAgentBenchmarkAdapter Adapter,
    CodexDiscoverySeriesPreparation Preparation);
