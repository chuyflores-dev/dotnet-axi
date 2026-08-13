using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;

namespace DotNetAxi.Testing;

internal static partial class CodexDiscoveryBenchmarkPreparation
{
    internal const string RequestSchema =
        "dotnet-axi/codex-discovery-request/v5";
    internal const string PreparationSchema =
        "dotnet-axi/codex-discovery-preparation/v6";
    internal const string SettingsSchema =
        "dotnet-axi/codex-discovery-settings/v1";
    internal const string ToolConfigurationSchema =
        "dotnet-axi/codex-discovery-tool-configuration/v3";
    internal const string CodexCliVersion = "codex-cli 0.146.0";
    internal const string ModelId = "gpt-5.6-sol";
    internal const string LunaModelId = "gpt-5.6-luna";
    internal const string ReasoningSetting = "low";
    internal const string Sandbox = "read-only";
    internal const string PermissionProfile = "never";
    internal const string NetworkPolicy = "disabled";
    internal const string AuthenticationMethod = "chatgpt";
    internal const string ProductMilestone = "0.5.0";
    internal const string CorpusId = "symbol-context";
    internal const string CorpusVersion = "1.0.4";
    internal const string PackageId = "dnaxi";
    internal const string PackageVersion = "0.5.0";
    internal const string ProductSchema = "dotnet-axi/v1";
    internal const string HarnessVersion = "2.11.2";
    internal const string IsolationProtocol =
        "codex-controlled-workspace/v1";
    internal const string PackageSourceEnvironmentVariable =
        "DNAXI_LOCAL_FEED";
    internal const string ExactCandidateInvocation =
        "dnx " + PackageId + "@" + PackageVersion + " --source \"$"
        + PackageSourceEnvironmentVariable
        + "\" --verbosity quiet -- <command>";
    internal const string BoundedSkillReaderCommand = "sed";
    internal const int BoundedSkillReaderMaximumLines = 110;
    internal const int CodexLocalProbeTimeoutSeconds = 30;
    internal const string PriorSummarySchema =
        "dotnet-axi/codex-discovery-summary/v3";
    internal const string PriorRequestHash =
        "cdd5e913c38e5e7b9dd2f36c841690b0e5dbebd4bef8863f94b6ac1ab803aac8";
    internal const string PriorReportHash =
        "69ca8480a5a9c9f9a07956d4a3755dd7619b5e37ca895d592543a64bb6ed7653";
    private const string PriorHistoricalSummaryHash =
        "30fb6de32eadbdb0fb3ff51cae5a268e26fb7f8a281697a4cc1b9eb74950a986";
    private const string PriorHistoricalRequestHash =
        "2e0d5ebcb3549c7a5c5a451fe5106f4f6e156a6627011824327509b05c32f893";
    private const string PriorHistoricalReportHash =
        "417649ab59ccb352cf1389705f2b51f2ab406b3886ea86033efb24779851b77f";
    internal const int RunsPerTask = 5;
    internal const int PriorExpectedRunCount = 70;

    private static readonly string[] ExpectedTaskIds =
    [
        "test-symbol-explicit-scope",
        "symbol-owner-framework-variants",
        "fresh-symbol-identity-show",
        "stale-symbol-correction",
        "ambiguous-symbol-correction",
        "syntax-candidate-partial-verification",
        "bounded-symbol-show",
        "document-exact-line-span",
        "symbol-outline",
        "context-whole-section-truncation",
    ];

    private static readonly string[] ExpectedBaselineTaskIds =
        ExpectedTaskIds;

    private static readonly string[] ExpectedCapabilities =
    [
        "context.symbol",
        "outline.syntax",
        "search.symbol.declaration",
        "search.syntax.verify",
        "show.document",
        "show.symbol.identity",
    ];

    private static readonly string[] ExpectedPriorTaskIds =
    [
        "file-handler-paths",
        "literal-archive-status",
        "regex-handler-methods",
        "syntax-attributed-classes",
        "syntax-catch-timeout",
        "syntax-invocation-record",
        "syntax-object-creation-archive-client",
    ];

    private static readonly IReadOnlyDictionary<string, string>
        ExpectedCapabilityByTask = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test-symbol-explicit-scope"] = "search.symbol.declaration",
            ["symbol-owner-framework-variants"] =
                "search.symbol.declaration",
            ["fresh-symbol-identity-show"] = "show.symbol.identity",
            ["stale-symbol-correction"] = "show.symbol.identity",
            ["ambiguous-symbol-correction"] = "show.symbol.identity",
            ["syntax-candidate-partial-verification"] =
                "search.syntax.verify",
            ["bounded-symbol-show"] = "show.symbol.identity",
            ["document-exact-line-span"] = "show.document",
            ["symbol-outline"] = "outline.syntax",
            ["context-whole-section-truncation"] = "context.symbol",
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
        await ValidateBoundedSkillReaderAsync(
            request,
            baselineTools,
            cancellationToken);
        await ValidateBaselineRawToolsAsync(
            request,
            baselineTools,
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
                "The pinned symbol-context corpus is invalid.",
                exception);
        }

        if (!string.Equals(corpus.Id, CorpusId, StringComparison.Ordinal)
            || !string.Equals(
                corpus.Version,
                CorpusVersion,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The request does not select the controlled symbol-context corpus identity.");
        }

        var applicable = corpus.SelectApplicableTasks(
            ProductMilestone,
            request.Corpus.AvailableCapabilities);
        if (!applicable.Select(static task => task.Id).SequenceEqual(
                ExpectedTaskIds,
                StringComparer.Ordinal)
            || !applicable.Where(static task => task.Applicability.Baseline)
                .Select(static task => task.Id)
                .SequenceEqual(ExpectedBaselineTaskIds, StringComparer.Ordinal)
            || applicable.Any(static task =>
                !task.Applicability.Candidate
                || task.RequiredCapabilities.Count != 1
                || !ExpectedCapabilityByTask.TryGetValue(
                    task.Id,
                    out var expectedCapability)
                || !string.Equals(
                    task.RequiredCapabilities[0],
                    expectedCapability,
                    StringComparison.Ordinal)
                || !string.Equals(
                    task.SuccessOracle.Normalizer,
                    "ordinal-sequence/v1",
                    StringComparison.Ordinal)
                || !HasExactPermittedToolPolicy(task)
                || !HasExactFactResponseContract(task)))
        {
            throw new AgentBenchmarkException(
                "The request does not select the exact ten candidate and four baseline 0.5.0 symbol-context tasks with approved oracle response contracts.");
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
        var dotNetInstallationRoot = GetDotNetInstallationRoot();
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
                expectedDnxExecutablePath: request.DnxExecutable.Path,
                dotNetInstallationRoot: dotNetInstallationRoot));
        var corpusDirectory = Path.GetDirectoryName(
                request.Corpus.Artifact.Path)
            ?? throw new AgentBenchmarkException(
                "The symbol-context corpus must have a parent directory.");
        var candidateProbeManifest = Path.GetFullPath(Path.Combine(
            corpusDirectory,
            selectedCorpus.Tasks[0].Repository.FixtureManifest.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        await using (var candidateProbeFixture =
                     await new RepositoryFixtureFactory().CreateAsync(
                         candidateProbeManifest,
                         cancellationToken: cancellationToken))
        {
            var candidateProbeInput = CreateProbeInput(
                request,
                settings.ModelId,
                selectedCorpus.Tasks[0],
                candidateProbeFixture,
                AgentBenchmarkCondition.Candidate);
            await adapter.PrepareWorkspaceAsync(
                candidateProbeInput,
                cancellationToken);
            await ValidateCandidateExecutionAsync(
                request,
                candidateProbeFixture,
                adapter,
                candidateProbeInput,
                cancellationToken);
        }

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
                settings.ModelId,
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
            applicable.Sum(static task =>
                task.Execution.TimeoutSeconds
                * ((task.Applicability.Baseline ? 1 : 0)
                   + (task.Applicability.Candidate ? 1 : 0)))
            * RunsPerTask);
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
            new CodexDiscoveryIsolationPreparation(
                IsolationProtocol,
                FreshWorkspacePerRun: true,
                CommandEvidenceBoundaryEnforced: true,
                SharedAuthenticationHomeDenied: true,
                NetworkDisabled: true),
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
            "retained 0.4.0 summary",
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
                "The request does not pin the exact manual dnx-first 0.5.0 Codex symbol-context series contract.");
        }

        if (!string.Equals(
                request.HarnessVersion,
                HarnessVersion,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The request does not pin the approved harness identity for corrected skill activation reconciliation.");
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
            "retained 0.4.0 summary");
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
            || settings.ModelId is not (ModelId or LunaModelId)
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
                "The settings artifact does not pin the approved Codex 0.5.0 symbol-context execution profile.");
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
        ContainsCommand(directory, "dnaxi");

    private static bool ContainsDnx(string directory) =>
        ContainsCommand(directory, "dnx");

    private static bool ContainsCommand(string directory, string command)
    {
        if (File.Exists(Path.Combine(directory, command)))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")
            ?? ".COM;.EXE;.BAT;.CMD";
        return pathExtensions.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Any(extension => File.Exists(Path.Combine(
                directory,
                $"{command}{extension}")));
    }

    private static async ValueTask ValidateBoundedSkillReaderAsync(
        CodexDiscoveryBenchmarkRequest request,
        CodexDiscoveryToolConfiguration tools,
        CancellationToken cancellationToken)
    {
        var rawToolDirectory = tools.ExecutableSearchPathEntries[0].Path;
        var readerPath = ResolveExecutableCommand(
            rawToolDirectory,
            BoundedSkillReaderCommand);
        if (readerPath is null)
        {
            throw new AgentBenchmarkException(
                "The shared sealed raw-tool path must contain the pinned bounded skill reader 'sed'.");
        }

        var skillPath = Path.Combine(request.Product.Skill.Path, "SKILL.md");
        var result = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                readerPath,
                request.Product.Skill.Path,
                [
                    "-n",
                    $"1,{BoundedSkillReaderMaximumLines}p",
                    skillPath,
                ],
                CreateCodexProbeEnvironment(request),
                new ProcessOutputLimits(16 * 1024, 4 * 1024),
                TimeSpan.FromSeconds(10)),
            cancellationToken);
        var expected = await File.ReadAllTextAsync(
            skillPath,
            cancellationToken);
        if (result.Lifecycle is not ProcessLifecycle.Completed
            || result.Outcome is not ProcessRunOutcome.Completed
            || result.Exit?.ExitCode != 0
            || result.StandardOutput.LimitExceeded
            || result.StandardError.LimitExceeded
            || !string.Equals(
                result.StandardOutput.Text,
                expected,
                StringComparison.Ordinal)
            || !string.IsNullOrEmpty(result.StandardError.Text))
        {
            throw new AgentBenchmarkException(
                "The pinned bounded skill reader could not load the complete portable SKILL.md before paid execution.");
        }
    }

    private static async ValueTask ValidateBaselineRawToolsAsync(
        CodexDiscoveryBenchmarkRequest request,
        CodexDiscoveryToolConfiguration tools,
        CancellationToken cancellationToken)
    {
        var rawToolDirectory = tools.ExecutableSearchPathEntries[0].Path;
        var dotnetPath = ResolveExecutableCommand(rawToolDirectory, "dotnet");
        var searchPath = ResolveExecutableCommand(rawToolDirectory, "rg");
        if (dotnetPath is null || searchPath is null)
        {
            throw new AgentBenchmarkException(
                "The shared sealed raw-tool path must contain executable raw 'dotnet' and 'rg' commands.");
        }

        var corpusDirectory = Path.GetDirectoryName(request.Corpus.Artifact.Path)
                              ?? throw new AgentBenchmarkException(
                                  "The pinned corpus must have a parent directory.");
        var manifestPath = Path.Combine(corpusDirectory, "fixture.json");
        await using var fixture = await new RepositoryFixtureFactory()
            .CreateAsync(manifestPath, cancellationToken: cancellationToken);

        const string expectedSourceLine =
            "6:line six is the bounded preview target";
        var searchResult = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                searchPath,
                fixture.WorkspacePath,
                [
                    "-n",
                    "-F",
                    "line six is the bounded preview target",
                    "docs/Runbook.txt",
                ],
                fixture.EnvironmentVariables,
                new ProcessOutputLimits(4 * 1024, 4 * 1024),
                TimeSpan.FromSeconds(10)),
            cancellationToken);
        if (!IsSuccessfulProbe(searchResult)
            || !string.Equals(
                searchResult.StandardOutput.Text.Trim(),
                expectedSourceLine,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The pinned baseline source-search command cannot find an exact line in the materialized benchmark fixture.");
        }

        const string expectedCandidateLine =
            "5:    public static void Run() => MissingAudit();";
        var candidateSearchResult = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                searchPath,
                fixture.WorkspacePath,
                [
                    "-n",
                    "-F",
                    "MissingAudit()",
                    "loose/UnownedCandidate.cs",
                ],
                fixture.EnvironmentVariables,
                new ProcessOutputLimits(4 * 1024, 4 * 1024),
                TimeSpan.FromSeconds(10)),
            cancellationToken);
        if (!IsSuccessfulProbe(candidateSearchResult)
            || !string.Equals(
                candidateSearchResult.StandardOutput.Text.Trim(),
                expectedCandidateLine,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The pinned baseline source-search command cannot locate the semantic-verification candidate in the materialized benchmark fixture.");
        }

        var commonArgumentsResult = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                searchPath,
                fixture.WorkspacePath,
                [
                    "--hidden",
                    "--glob", "!**/bin/**",
                    "--glob", "!**/obj/**",
                    "--files",
                    "-g", "Workspace.slnx",
                    "-g", "*.cs",
                    "-g", "*.csproj",
                ],
                fixture.EnvironmentVariables,
                new ProcessOutputLimits(16 * 1024, 4 * 1024),
                TimeSpan.FromSeconds(10)),
            cancellationToken);
        var discoveredFiles = commonArgumentsResult.StandardOutput.Text.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Select(static path => path.Replace('\\', '/'))
            .ToArray();
        string[] expectedProjectPaths =
        [
            "src/Alternate/Alternate.csproj",
            "src/Core/Core.csproj",
            "src/Worker/Worker.csproj",
            "tests/Core.Tests/Core.Tests.csproj",
        ];
        if (!IsSuccessfulProbe(commonArgumentsResult)
            || !discoveredFiles.Contains(
                "Workspace.slnx",
                StringComparer.Ordinal)
            || !discoveredFiles.Contains(
                "src/Core/LedgerService.cs",
                StringComparer.Ordinal)
            || !discoveredFiles.Contains(
                "tests/Core.Tests/Core.Tests.csproj",
                StringComparer.Ordinal)
            || expectedProjectPaths.Any(projectPath =>
                !discoveredFiles.Contains(projectPath, StringComparer.Ordinal)))
        {
            throw new AgentBenchmarkException(
                "The pinned baseline rg command does not support the common Codex source-search arguments used by the benchmark condition.");
        }

        var dotnetResult = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                dotnetPath,
                fixture.WorkspacePath,
                [
                    "msbuild",
                    "src/Core/Core.csproj",
                    "-nologo",
                    "-getProperty:TargetFrameworks",
                ],
                fixture.EnvironmentVariables,
                new ProcessOutputLimits(4 * 1024, 4 * 1024),
                TimeSpan.FromSeconds(30)),
            cancellationToken);
        if (!IsSuccessfulProbe(dotnetResult)
            || !dotnetResult.StandardOutput.Text.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Contains("net8.0;net10.0", StringComparer.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The pinned baseline dotnet command cannot evaluate the benchmark fixture's target frameworks through MSBuild.");
        }

        var candidatePath = Path.GetFullPath(
            Path.Combine(
                fixture.WorkspacePath,
                "loose",
                "UnownedCandidate.cs"));
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        foreach (var projectPath in expectedProjectPaths)
        {
            var compileItemsResult = await new ProcessRunner().RunAsync(
                new ProcessRunRequest(
                    dotnetPath,
                    fixture.WorkspacePath,
                    [
                        "msbuild",
                        projectPath,
                        "-nologo",
                        "-getItem:Compile",
                    ],
                    fixture.EnvironmentVariables,
                    new ProcessOutputLimits(64 * 1024, 4 * 1024),
                    TimeSpan.FromSeconds(30)),
                cancellationToken);
            if (!IsSuccessfulProbe(compileItemsResult)
                || !TryReadCompileItemFullPaths(
                    compileItemsResult.StandardOutput.Text,
                    out var compileItemPaths))
            {
                throw new AgentBenchmarkException(
                    "The pinned baseline dotnet command cannot evaluate repository project ownership through ordinary MSBuild Compile-item queries.");
            }

            if (compileItemPaths.Contains(candidatePath, pathComparer))
            {
                throw new AgentBenchmarkException(
                    "The materialized semantic-verification candidate unexpectedly belongs to an evaluated repository project.");
            }
        }
    }

    private static bool TryReadCompileItemFullPaths(
        string output,
        out string[] paths)
    {
        paths = [];
        try
        {
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("Items", out var items)
                || !items.TryGetProperty("Compile", out var compileItems)
                || compileItems.ValueKind is not JsonValueKind.Array)
            {
                return false;
            }

            var values = new List<string>();
            foreach (var item in compileItems.EnumerateArray())
            {
                if (!item.TryGetProperty("FullPath", out var fullPath)
                    || fullPath.ValueKind is not JsonValueKind.String
                    || string.IsNullOrWhiteSpace(fullPath.GetString()))
                {
                    return false;
                }

                values.Add(Path.GetFullPath(fullPath.GetString()!));
            }

            paths = values.ToArray();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSuccessfulProbe(ProcessRunResult result) =>
        result.Lifecycle is ProcessLifecycle.Completed
        && result.Outcome is ProcessRunOutcome.Completed
        && result.Exit?.ExitCode == 0
        && !result.StandardOutput.LimitExceeded
        && !result.StandardError.LimitExceeded;

    internal static bool HasExactFactResponseContract(AgentTaskDefinition task)
    {
        const string evidenceMarker =
            "Replace every angle-bracket description with the corresponding evidence value.";
        const string responseMarker =
            "with each shown literal prefix followed by one space and no extra text:";
        var responseMarkerIndex = task.Prompt.IndexOf(
            responseMarker,
            StringComparison.Ordinal);
        if (!task.Prompt.Contains(evidenceMarker, StringComparison.Ordinal)
            || responseMarkerIndex < 0)
        {
            return false;
        }

        var contract = task.Prompt[
            (responseMarkerIndex + responseMarker.Length)..].Trim();
        if (!contract.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        var declarations = Regex.Matches(contract, "`(?<declaration>[^`]*)`")
            .Select(static match => match.Groups["declaration"].Value)
            .ToArray();
        string[]? approvedDeclarations = task.Id switch
        {
            "test-symbol-explicit-scope" =>
            [
                "declaration: <repo-relative-path>:<1-based-line>",
                "owner: <repo-relative-csproj-path>",
                "signature: <unqualified-declaration-name>",
            ],
            "symbol-owner-framework-variants" =>
            [
                "declaration: <repo-relative-path>:<1-based-line>",
                "framework: <target-framework-moniker>",
                "framework: <target-framework-moniker>",
                "owner: <repo-relative-csproj-path>",
                "variant-count: <base-10-integer>",
            ],
            "fresh-symbol-identity-show" =>
            [
                "body: <single-return-statement>",
                "declaration: <repo-relative-path>:<1-based-line>",
                "signature: <unqualified-method-signature>",
            ],
            "stale-symbol-correction" =>
            [
                "correction-target: <unqualified-name-without-parameters>",
                "replacement: <unqualified-signature>",
                "status: <normalized-status>",
            ],
            "ambiguous-symbol-correction" =>
            [
                "candidate-count: <base-10-integer>",
                "selected: <selection-or-none>",
                "status: <normalized-status>",
            ],
            "syntax-candidate-partial-verification" =>
            [
                "candidate: <repo-relative-path>:<1-based-line>",
                "compiler-verification: <availability-value>",
                "ownership: <presence-value>",
            ],
            "bounded-symbol-show" =>
            [
                "declaration: <repo-relative-path>:<1-based-line>",
                "retrieval: <availability-value>",
                "signature: <unqualified-declaration-name>",
                "truncated: <lowercase-boolean>",
            ],
            "document-exact-line-span" =>
            [
                "actual-span: <start-line>-<end-line>",
                "line-5: <exact-line-text>",
                "line-6: <exact-line-text>",
            ],
            "symbol-outline" =>
            [
                "1: depth <base-10-integer> <signature>",
                "2: depth <base-10-integer> <signature>",
                "3: depth <base-10-integer> <signature>",
            ],
            "context-whole-section-truncation" =>
            [
                "included-characters: <base-10-integer>",
                "omitted: <comma-separated-section-names>",
                "retrieval: <availability-value>",
                "truncated: <lowercase-boolean>",
            ],
            _ => null,
        };
        string[] requiredDirectives = task.Id switch
        {
            "symbol-owner-framework-variants" =>
            ["Sort repeated framework values using ordinal string order."],
            "fresh-symbol-identity-show" =>
            ["For body, return only the single return statement without braces or escaped newlines."],
            "stale-symbol-correction" =>
            ["Translate error code evidence.stale_id to normalized status stale."],
            "ambiguous-symbol-correction" =>
            ["Use selected value none when no declaration is resolved, and translate error code evidence.ambiguous_id to normalized status ambiguous."],
            "syntax-candidate-partial-verification" =>
            ["Map the evaluated evidence to canonical values: ownership is present when at least one evaluated C# project includes the file as a Compile item and absent otherwise; compiler-verification is available when at least one owning project can run compiler-backed verification for the candidate and unavailable otherwise."],
            "bounded-symbol-show" =>
            ["Use retrieval value available when a nonblank retrieval_command is present and unavailable otherwise; use a lowercase boolean for truncated."],
            "document-exact-line-span" =>
            ["Preserve each retrieved line value exactly."],
            "symbol-outline" =>
            ["Preserve structured outline order and format each value as the literal word depth, one space, the integer depth, one space, and the signature."],
            "context-whole-section-truncation" =>
            [
                "For omitted, join omitted_sections in reported order with commas and no spaces.",
                "Use retrieval value available when a nonblank retrieval_command is present and unavailable otherwise; use a lowercase boolean for truncated.",
            ],
            _ => [],
        };
        string[] disallowedSemanticVocabulary =
        [
            "coverage",
            "partial_reason",
            "ownership.not_found",
            "unresolved",
        ];
        if (approvedDeclarations is null
            || !declarations.SequenceEqual(
                approvedDeclarations,
                StringComparer.Ordinal)
            || requiredDirectives.Any(directive =>
                !task.Prompt.Contains(directive, StringComparison.Ordinal))
            || task.Id == "syntax-candidate-partial-verification"
            && disallowedSemanticVocabulary.Any(value =>
                task.Prompt.Contains(value, StringComparison.Ordinal)
                || task.SuccessOracle.ExpectedFacts.Any(fact =>
                    fact.Contains(value, StringComparison.Ordinal)))
            || declarations.Length != task.SuccessOracle.ExpectedFacts.Count)
        {
            return false;
        }

        var exactContract = string.Join(
            ", ",
            declarations.Select(static declaration => $"`{declaration}`"))
            + ".";
        if (!string.Equals(contract, exactContract, StringComparison.Ordinal))
        {
            return false;
        }

        if (task.Id == "syntax-candidate-partial-verification")
        {
            const string approvedSemanticInstructions =
                "Locate the MissingAudit invocation candidate in loose/UnownedCandidate.cs and determine its repository project ownership and compiler-verification availability. Map the evaluated evidence to canonical values: ownership is present when at least one evaluated C# project includes the file as a Compile item and absent otherwise; compiler-verification is available when at least one owning project can run compiler-backed verification for the candidate and unavailable otherwise.";
            var exactPrompt = approvedSemanticInstructions
                + " " + evidenceMarker
                + " Return exactly these three newline-delimited facts in this order, "
                + responseMarker
                + " " + exactContract;
            if (!string.Equals(
                task.Prompt,
                exactPrompt,
                StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var index = 0; index < declarations.Length; index++)
        {
            var expectedFact = task.SuccessOracle.ExpectedFacts[index];
            if (task.Prompt.Contains(expectedFact, StringComparison.Ordinal))
            {
                return false;
            }

            var separator = expectedFact.IndexOf(':');
            var declaration = declarations[index];
            var prefix = expectedFact[..(separator + 1)];
            if (!declaration.StartsWith($"{prefix} ", StringComparison.Ordinal))
            {
                return false;
            }

            var grammar = declaration[(prefix.Length + 1)..];
            var placeholders = Regex.Matches(
                grammar,
                "<(?<name>[a-z0-9][a-z0-9-]*)>");
            if (placeholders.Count == 0
                || placeholders.Any(static placeholder =>
                    string.IsNullOrWhiteSpace(
                        placeholder.Groups["name"].Value))
                || grammar.Count(static character => character == '<')
                    != placeholders.Count
                || grammar.Count(static character => character == '>')
                    != placeholders.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExactPermittedToolPolicy(
        AgentTaskDefinition task)
    {
        string[] expected = task.Id switch
        {
            "syntax-candidate-partial-verification" =>
            [
                "dotnet-sdk",
                "repository-execution",
                "repository-read",
                "source-search",
            ],
            _ => ["repository-read", "source-search"],
        };
        return task.Execution.PermittedTools.SequenceEqual(
            expected,
            StringComparer.Ordinal);
    }

    private static string? ResolveExecutableCommand(
        string directory,
        string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            var path = Path.Combine(directory, command);
            return IsExecutableFile(path) ? path : null;
        }

        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")
            ?? ".COM;.EXE;.BAT;.CMD";
        foreach (var extension in pathExtensions.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var path = Path.Combine(directory, $"{command}{extension}");
            if (IsExecutableFile(path))
            {
                return path;
            }
        }

        return null;
    }

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
        ValidateCandidateToolPackage(packagePath);

        if (!skillFiles.Select(static file => file.Relative).SequenceEqual(
                [
                    "SKILL.md",
                ],
                StringComparer.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The pinned repository skill must contain only SKILL.md.");
        }

        var skillFile = Path.Combine(product.Skill.Path, "SKILL.md");
        var skillBytes = await File.ReadAllBytesAsync(
            skillFile,
            cancellationToken);
        var skill = await File.ReadAllTextAsync(
            skillFile,
            Encoding.UTF8,
            cancellationToken);
        var invocationVersions = DnaxiInvocationVersionRegex()
            .Matches(skill)
            .Select(match => match.Groups["version"].Value)
            .ToArray();
        if (skillBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            || !skill.StartsWith(
                "---\nname: dotnet-axi\ndescription: ",
                StringComparison.Ordinal)
            || skill.Contains("<exact-version>", StringComparison.Ordinal)
            || invocationVersions.Length == 0
            || invocationVersions.Any(version => !string.Equals(
                version,
                PackageVersion,
                StringComparison.Ordinal))
            || !skill.Contains(
                ExactCandidateInvocation,
                StringComparison.Ordinal))
        {
            throw new AgentBenchmarkException(
                "The repository skill must be BOM-free, discoverable, and expose the exact source-pinned candidate dnx invocation.");
        }
    }

    private static void ValidateCandidateToolPackage(string packagePath)
    {
        try
        {
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

            var nuspecEntries = archive.Entries.Where(entry =>
                    entry.FullName.EndsWith(
                        ".nuspec",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (nuspecEntries.Length != 1)
            {
                throw new AgentBenchmarkException(
                    "The candidate tool package must contain exactly one nuspec.");
            }

            using var nuspecStream = nuspecEntries[0].Open();
            var nuspec = XDocument.Load(nuspecStream, LoadOptions.None);
            var metadata = nuspec.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "metadata");
            var id = metadata?.Elements().SingleOrDefault(element =>
                element.Name.LocalName == "id")?.Value;
            var version = metadata?.Elements().SingleOrDefault(element =>
                element.Name.LocalName == "version")?.Value;
            var packageTypes = metadata?.Descendants().Where(element =>
                    element.Name.LocalName == "packageType")
                .Select(element => (string?)element.Attribute("name"))
                .ToArray() ?? [];
            if (!string.Equals(id, PackageId, StringComparison.Ordinal)
                || !string.Equals(
                    version,
                    PackageVersion,
                    StringComparison.Ordinal)
                || !packageTypes.Contains(
                    "DotnetTool",
                    StringComparer.Ordinal))
            {
                throw new AgentBenchmarkException(
                    "The pinned package must identify the exact dnaxi 0.5.0 .NET tool candidate.");
            }

            foreach (var entryName in new[]
                     {
                         "tools/net10.0/any/DotnetToolSettings.xml",
                         "tools/net10.0/any/dnaxi.dll",
                         "tools/net10.0/any/dnaxi.deps.json",
                         "tools/net10.0/any/dnaxi.runtimeconfig.json",
                     })
            {
                if (archive.GetEntry(entryName) is null)
                {
                    throw new AgentBenchmarkException(
                        $"The candidate tool package is missing required entry '{entryName}'.");
                }
            }

            using var settingsStream = archive.GetEntry(
                    "tools/net10.0/any/DotnetToolSettings.xml")!
                .Open();
            var settings = XDocument.Load(settingsStream, LoadOptions.None);
            var command = settings.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "Command");
            if (!string.Equals(
                    (string?)command?.Attribute("Name"),
                    PackageId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    (string?)command?.Attribute("EntryPoint"),
                    "dnaxi.dll",
                    StringComparison.Ordinal)
                || !string.Equals(
                    (string?)command?.Attribute("Runner"),
                    "dotnet",
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    "The candidate tool package does not expose the expected dnaxi command.");
            }
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or InvalidOperationException
                  or XmlException)
        {
            throw new AgentBenchmarkException(
                "The pinned candidate package is not a valid NuGet tool archive.",
                exception);
        }
    }

    private static async ValueTask<CodexDiscoveryPriorSeriesIdentity>
        LoadPriorSeriesSummaryAsync(
        CodexDiscoveryPriorSeriesPin prior,
        CancellationToken cancellationToken)
    {
        await ValidateFilePinAsync(
            prior.Summary,
            "retained 0.4.0 summary",
            cancellationToken);
        try
        {
            await using var stream = File.OpenRead(prior.Summary.Path);
            var summary = await JsonSerializer.DeserializeAsync<
                    CodexDiscoveryPriorSeriesSummaryV3>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                ?? throw new AgentBenchmarkException(
                    "The retained 0.4.0 summary is empty.");
            var identity = new CodexDiscoveryPriorSeriesIdentity(
                summary.Schema ?? string.Empty,
                summary.RequestHash ?? string.Empty,
                summary.ReportHash ?? string.Empty,
                summary.EvidenceStatus ?? string.Empty,
                summary.Comparison ?? string.Empty,
                summary.ExpectedRunCount,
                summary.RetainedRunCount);
            if (!string.Equals(
                    identity.Schema,
                    PriorSummarySchema,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.RequestHash,
                    prior.RequestHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.RequestHash,
                    PriorRequestHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.ReportHash,
                    prior.ReportHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.ReportHash,
                    PriorReportHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.EvidenceStatus,
                    "complete",
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.Comparison,
                    "no-improvement",
                    StringComparison.Ordinal)
                || identity.ExpectedRunCount != PriorExpectedRunCount
                || identity.RetainedRunCount != identity.ExpectedRunCount
                || !IsCanonicalPriorCondition(
                    summary.Baseline,
                    AgentBenchmarkCondition.Baseline)
                || !IsCanonicalPriorCondition(
                    summary.Candidate,
                    AgentBenchmarkCondition.Candidate)
                || summary.Thresholds is null
                || summary.Thresholds.SafetyCriticalRegressions != 0
                || summary.Thresholds.AggregateSuccessDeltaPercentagePoints
                    != 0m
                || summary.Thresholds.MedianTokenChangePercent
                    != 7.5511508951406649616368286400m
                || summary.Thresholds.MedianToolCallChangePercent != 0m
                || summary.Thresholds.SuccessRegression
                || summary.Thresholds.TokenRegression
                || summary.Thresholds.ToolCallRegression
                || summary.Thresholds.ImprovementClaimSupported
                || summary.RouteActivations is null
                || !summary.RouteActivations.Select(static route => route.TaskId)
                    .SequenceEqual(ExpectedPriorTaskIds, StringComparer.Ordinal)
                || summary.RouteActivations.Any(static route =>
                    route.CandidateRunCount != RunsPerTask
                    || route.ActivatedRunCount
                    != ExpectedPriorActivationCount(route.TaskId)
                    || route.SuccessfulActivatedRunCount
                    != ExpectedPriorActivationCount(route.TaskId))
                || summary.PriorSeries is null
                || summary.PriorSeries.Comparable
                || !string.Equals(
                    summary.PriorSeries.SummaryHash,
                    PriorHistoricalSummaryHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    summary.PriorSeries.SummarySchema,
                    "dotnet-axi/codex-discovery-summary/v1",
                    StringComparison.Ordinal)
                || !string.Equals(
                    summary.PriorSeries.RequestHash,
                    PriorHistoricalRequestHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    summary.PriorSeries.ReportHash,
                    PriorHistoricalReportHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    summary.PriorSeries.EvidenceStatus,
                    "failed",
                    StringComparison.Ordinal)
                || !string.Equals(
                    summary.PriorSeries.Comparison,
                    "incomparable",
                    StringComparison.Ordinal)
                || summary.Reasons is null
                || !summary.Reasons.SequenceEqual(
                    [
                        "Complete comparable evidence does not satisfy either a documented regression threshold or the improvement threshold.",
                    ],
                    StringComparer.Ordinal))
            {
                throw new AgentBenchmarkException(
                    "The prior-series pin does not identify the immutable retained 0.4.0 discovery result.");
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
                "The retained 0.4.0 summary is malformed.",
                exception);
        }
    }

    private static bool IsCanonicalPriorCondition(
        CodexDiscoveryConditionMetrics? metrics,
        AgentBenchmarkCondition condition)
    {
        if (metrics is null
            || metrics.Condition != condition
            || metrics.RunCount != 35
            || metrics.CompletedCount != 35
            || metrics.SuccessCount != 35
            || metrics.SafeCount != 35
            || metrics.TimedOutCount != 0
            || metrics.SuccessRatePercent != 100m
            || metrics.MedianToolCalls != 3m
            || metrics.MedianTurns != 1m)
        {
            return false;
        }

        return condition is AgentBenchmarkCondition.Baseline
            ? metrics.DnxActivatedRunCount == 0
              && metrics.SuccessfulDnxActivatedRunCount == 0
              && metrics.DnxInvocationCount == 0
              && metrics.SuccessfulDnxInvocationCount == 0
              && metrics.MedianTotalTokens == 46_920m
              && metrics.MedianDurationMilliseconds == 21_101.7472m
            : metrics.DnxActivatedRunCount == 34
              && metrics.SuccessfulDnxActivatedRunCount == 34
              && metrics.DnxInvocationCount == 35
              && metrics.SuccessfulDnxInvocationCount == 35
              && metrics.MedianTotalTokens == 50_463m
              && metrics.MedianDurationMilliseconds == 21_530.5514m;
    }

    private static int ExpectedPriorActivationCount(string taskId) =>
        taskId == "regex-handler-methods" ? 4 : 5;

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

    private static string GetDotNetInstallationRoot()
    {
        var runtimeDirectory = new DirectoryInfo(
            RuntimeEnvironment.GetRuntimeDirectory());
        var installationRoot = runtimeDirectory.Parent?.Parent?.Parent;
        if (installationRoot is null
            || !Directory.Exists(installationRoot.FullName))
        {
            throw new AgentBenchmarkException(
                "The benchmark harness cannot locate its .NET installation.");
        }

        return installationRoot.FullName;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static async ValueTask ValidateCandidateExecutionAsync(
        CodexDiscoveryBenchmarkRequest request,
        RepositoryFixture fixture,
        CodexAgentBenchmarkAdapter adapter,
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken)
    {
        var codexHome = Directory.CreateDirectory(Path.Combine(
            fixture.StatePath,
            "codex-preflight-home")).FullName;
        var materializedStartInfo = adapter.CreateStartInfo(input);
        var environment = materializedStartInfo.Environment.ToDictionary(
            static variable => variable.Key,
            static variable => variable.Value ?? string.Empty,
            StringComparer.Ordinal);
        environment["CODEX_HOME"] = codexHome;
        var materializedDnx = adapter.GetMaterializedExecutablePath(
            input,
            request.DnxExecutable.Path);
        var materializedFeed = environment[PackageSourceEnvironmentVariable];
        var workspaceBaseline =
            await AgentBenchmarkWorkspaceHasher.CaptureBaselineAsync(
                fixture.WorkspacePath,
                fixture.ContentFiles,
                TimeSpan.FromSeconds(10),
                cancellationToken);
        var result = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                request.CodexExecutable.Path,
                fixture.WorkspacePath,
                [
                    "sandbox",
                    "--permission-profile",
                    CodexAgentBenchmarkAdapter.RuntimePermissionProfileName,
                    "--config",
                    CodexAgentBenchmarkAdapter
                        .CreateRuntimePermissionProfileConfig(
                            fixture.WorkspacePath,
                            fixture.StatePath,
                            CodexAgentBenchmarkAdapter
                                .GetMaterializedArtifactRoot(
                            fixture.WorkspacePath),
                            codexHome,
                            adapter.DotNetInstallationRoot,
                            Sandbox),
                    "--cd",
                    fixture.WorkspacePath,
                    "--",
                    materializedDnx,
                    $"{request.Product.PackageId}@{request.Product.PackageVersion}",
                    "--source",
                    materializedFeed,
                    "--verbosity",
                    "quiet",
                    "--",
                    "--version",
                ],
                environment,
                new ProcessOutputLimits(1024 * 1024, 64 * 1024),
                TimeSpan.FromSeconds(60)),
            cancellationToken);
        var workspaceInspection =
            await AgentBenchmarkWorkspaceHasher.InspectAsync(
                fixture.WorkspacePath,
                workspaceBaseline,
                TimeSpan.FromSeconds(10),
                CancellationToken.None);
        if (result.Lifecycle is not ProcessLifecycle.Completed
            || result.Outcome is not ProcessRunOutcome.Completed
            || result.Exit?.ExitCode != 0
            || result.StandardOutput.LimitExceeded
            || result.StandardError.LimitExceeded
            || !workspaceInspection.Complete
            || !workspaceInspection.MatchesBaseline
            || !IsExpectedCandidateVersionOutput(
                result.StandardOutput.Text,
                request.Product.PackageVersion))
        {
            throw new AgentBenchmarkException(
                "The exact source-pinned dnaxi candidate failed its bounded local execution preflight; no paid benchmark run may start.");
        }
    }

    private static AgentBenchmarkAdapterInput CreateProbeInput(
        CodexDiscoveryBenchmarkRequest request,
        string modelId,
        AgentTaskDefinition task,
        RepositoryFixture fixture,
        AgentBenchmarkCondition condition)
    {
        var conditionPin = condition == AgentBenchmarkCondition.Baseline
            ? request.Baseline
            : request.Candidate;
        return new AgentBenchmarkAdapterInput(
            $"{request.SeriesId}/isolation-{condition.ToString().ToLowerInvariant()}",
            1,
            0,
            1,
            condition,
            task,
            fixture.WorkspacePath,
            fixture.EnvironmentVariables,
            new AgentBenchmarkExecutionSettings(
                CodexCliVersion,
                modelId,
                ReasoningSetting,
                request.Settings.Sha256,
                Sandbox,
                PermissionProfile,
                NetworkPolicy),
            AgentBenchmarkHash.Compute(task.Prompt),
            conditionPin.Instructions.Sha256,
            conditionPin.ToolConfiguration.Sha256);
    }

    private static bool IsExpectedCandidateVersionOutput(
        string output,
        string packageVersion)
    {
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        string[] expectedHeader =
        [
            $"schema: {ProductSchema}",
            "command: version",
            "status: success",
            "tool: dotnet-axi",
            $"tool_version: {packageVersion}",
            $"output_schema: {ProductSchema}",
            "capabilities:",
        ];
        if (lines.Length <= expectedHeader.Length
            || !lines.Take(expectedHeader.Length).SequenceEqual(
                expectedHeader,
                StringComparer.Ordinal))
        {
            return false;
        }

        var lineIndex = expectedHeader.Length;
        return ValidateToonMapping(lines, ref lineIndex, indent: 2)
            && lineIndex == lines.Length;
    }

    private static bool ValidateToonMapping(
        IReadOnlyList<string> lines,
        ref int lineIndex,
        int indent)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var entryCount = 0;
        while (lineIndex < lines.Count)
        {
            var line = lines[lineIndex];
            var actualIndent = CountLeadingSpaces(line);
            if (actualIndent < indent)
            {
                break;
            }

            if (actualIndent != indent)
            {
                return false;
            }

            var content = line[indent..];
            var table = ToonTableHeaderRegex().Match(content);
            if (table.Success)
            {
                if (!keys.Add(table.Groups["key"].Value)
                    || !int.TryParse(
                        table.Groups["rows"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var rowCount))
                {
                    return false;
                }

                var fieldCount = table.Groups["fields"].Value.Count(
                    static character => character == ',') + 1;
                lineIndex++;
                for (var row = 0; row < rowCount; row++)
                {
                    if (lineIndex >= lines.Count
                        || CountLeadingSpaces(lines[lineIndex]) != indent + 2
                        || !HasExpectedToonFieldCount(
                            lines[lineIndex][(indent + 2)..],
                            fieldCount))
                    {
                        return false;
                    }

                    lineIndex++;
                }

                entryCount++;
                continue;
            }

            var separator = content.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0
                || !ToonKeyRegex().IsMatch(content[..separator])
                || !keys.Add(content[..separator]))
            {
                return false;
            }

            entryCount++;
            lineIndex++;
            if (separator != content.Length - 1)
            {
                continue;
            }

            if (!ValidateToonMapping(lines, ref lineIndex, indent + 2))
            {
                return false;
            }
        }

        return entryCount > 0;
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static bool HasExpectedToonFieldCount(
        string row,
        int expectedFieldCount)
    {
        var fieldCount = 1;
        var inQuotes = false;
        var escaped = false;
        foreach (var character in row)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inQuotes && character == '\\')
            {
                escaped = true;
            }
            else if (character == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && character == ',')
            {
                fieldCount++;
            }
        }

        return row.Length > 0
            && !inQuotes
            && !escaped
            && fieldCount == expectedFieldCount;
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
            var candidateOutput = Regex.Unescape(candidate.StandardOutput)
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
                    StringComparison.Ordinal)
                || !candidateOutput.Contains(
                    ExactCandidateInvocation,
                    StringComparison.Ordinal))
            {
                throw new AgentBenchmarkException(
                    "The Codex prompt-input preflight did not prove candidate-only project-local dotnet-axi skill discovery with the exact source-pinned invocation.");
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
                TimeSpan.FromSeconds(CodexLocalProbeTimeoutSeconds)),
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

    [GeneratedRegex(
        "\\bdnx[ \\t]+dnaxi@(?<version>[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DnaxiInvocationVersionRegex();

    [GeneratedRegex(
        "^[a-z][a-z0-9_]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ToonKeyRegex();

    [GeneratedRegex(
        "^(?<key>[a-z][a-z0-9_]*)\\[(?<rows>[0-9]+)\\]\\{(?<fields>[a-z][a-z0-9_]*(?:,[a-z][a-z0-9_]*)*)\\}:$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ToonTableHeaderRegex();
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

internal sealed record CodexDiscoveryPriorSeriesSummaryV3(
    string? Schema,
    string? RequestHash,
    string? ReportHash,
    string? EvidenceStatus,
    string? Comparison,
    int ExpectedRunCount,
    int RetainedRunCount,
    CodexDiscoveryConditionMetrics? Baseline,
    CodexDiscoveryConditionMetrics? Candidate,
    CodexDiscoveryThresholdEvaluation? Thresholds,
    IReadOnlyList<CodexDiscoveryPriorRouteActivationV3>? RouteActivations,
    CodexDiscoveryHistoricalComparison? PriorSeries,
    IReadOnlyList<string>? Reasons);

internal sealed record CodexDiscoveryPriorRouteActivationV3(
    string TaskId,
    int CandidateRunCount,
    int ActivatedRunCount,
    int SuccessfulActivatedRunCount);

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

internal sealed record CodexDiscoveryIsolationPreparation(
    string Protocol,
    bool FreshWorkspacePerRun,
    bool CommandEvidenceBoundaryEnforced,
    bool SharedAuthenticationHomeDenied,
    bool NetworkDisabled);

internal sealed record CodexDiscoverySeriesPreparation(
    string Schema,
    string RequestHash,
    CodexDiscoveryRetainedPins Pins,
    AgentBenchmarkSeriesManifest Manifest,
    IReadOnlyList<AgentBenchmarkScheduledRun> Schedule,
    CodexDiscoveryIsolationPreparation Isolation,
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
