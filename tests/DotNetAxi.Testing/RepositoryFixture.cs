using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DotNetAxi.Testing;

public sealed class RepositoryFixture : IAsyncDisposable
{
    private static readonly HashSet<string> AllowedAmbientVariables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ALL_PROXY",
            "COMSPEC",
            "CommonProgramFiles",
            "CommonProgramFiles(x86)",
            "CommonProgramW6432",
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "LANG",
            "LANGUAGE",
            "LC_ALL",
            "LC_CTYPE",
            "NO_PROXY",
            "NUMBER_OF_PROCESSORS",
            "OS",
            "PATH",
            "PATHEXT",
            "PROCESSOR_ARCHITECTURE",
            "PROCESSOR_ARCHITEW6432",
            "ProgramFiles",
            "ProgramFiles(x86)",
            "ProgramW6432",
            "SSL_CERT_DIR",
            "SSL_CERT_FILE",
            "SystemRoot",
            "WINDIR",
        };

    private readonly IFixtureDirectoryCleaner _cleaner;
    private readonly Lazy<string> _dotNetHostPath =
        new(ResolveDotNetHostPath, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly string _ownerId;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private bool _cleaned;

    internal RepositoryFixture(
        string rootPath,
        string workspacePath,
        string externalPath,
        string statePath,
        string metadataPath,
        string gitConfigPath,
        string homePath,
        string cachePath,
        string artifactsPath,
        string tempPath,
        string dotNetHomePath,
        string nuGetPackagesPath,
        string nuGetHttpCachePath,
        string nuGetConfigPath,
        string contentHash,
        IReadOnlyList<string> contentFiles,
        string? externalContentHash,
        IReadOnlyList<string> externalContentFiles,
        RepositoryFixtureIdentity identity,
        IReadOnlyList<string> capabilities,
        FixtureBuildVerification? buildVerification,
        string? testRunner,
        FixtureScenario? scenario,
        FixtureToolchainIdentity toolchain,
        RepositoryFixtureOptions options,
        IReadOnlyDictionary<string, string> environmentVariables,
        FixtureGitPlan? gitPlan,
        string ownerId,
        IFixtureDirectoryCleaner cleaner)
    {
        RootPath = rootPath;
        WorkspacePath = workspacePath;
        ExternalPath = externalPath;
        StatePath = statePath;
        MetadataPath = metadataPath;
        GitConfigPath = gitConfigPath;
        HomePath = homePath;
        CachePath = cachePath;
        ArtifactsPath = artifactsPath;
        TempPath = tempPath;
        DotNetHomePath = dotNetHomePath;
        NuGetPackagesPath = nuGetPackagesPath;
        NuGetHttpCachePath = nuGetHttpCachePath;
        NuGetConfigPath = nuGetConfigPath;
        ContentHash = contentHash;
        ContentFiles = contentFiles;
        ExternalContentHash = externalContentHash;
        ExternalContentFiles = externalContentFiles;
        Identity = identity;
        Capabilities = capabilities;
        BuildVerification = buildVerification;
        TestRunner = testRunner;
        Scenario = scenario;
        Toolchain = toolchain;
        Options = options;
        EnvironmentVariables = environmentVariables;
        GitPlan = gitPlan;
        _ownerId = ownerId;
        _cleaner = cleaner;
    }

    public string RootPath { get; }

    public string WorkspacePath { get; }

    public string ExternalPath { get; }

    public string StatePath { get; }

    public string MetadataPath { get; }

    public string GitConfigPath { get; }

    public string HomePath { get; }

    public string CachePath { get; }

    public string ArtifactsPath { get; }

    public string TempPath { get; }

    public string DotNetHomePath { get; }

    public string NuGetPackagesPath { get; }

    public string NuGetHttpCachePath { get; }

    public string NuGetConfigPath { get; }

    public string DotNetHostPath => _dotNetHostPath.Value;

    public string ContentHash { get; }

    public IReadOnlyList<string> ContentFiles { get; }

    public string? ExternalContentHash { get; }

    public IReadOnlyList<string> ExternalContentFiles { get; }

    public RepositoryFixtureIdentity Identity { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public FixtureBuildVerification? BuildVerification { get; }

    public string? TestRunner { get; }

    public FixtureScenario? Scenario { get; }

    public bool RequiresGitPreparation => GitPlan is not null;

    public FixtureToolchainIdentity Toolchain { get; }

    public RepositoryFixtureOptions Options { get; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    internal FixtureGitPlan? GitPlan { get; }

    public ProcessStartInfo CreateProcessStartInfo(
        FixtureProcessKind kind,
        string fileName,
        params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        const FixtureProcessKind supportedKinds =
            FixtureProcessKind.Tooling
            | FixtureProcessKind.Restore
            | FixtureProcessKind.RepositoryCode;
        if (kind == FixtureProcessKind.None
            || (kind & ~supportedKinds) != FixtureProcessKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var requiredPermissions =
            (FixtureExecutionPermissions)(int)kind;
        if ((Options.ExecutionPermissions & requiredPermissions)
            != requiredPermissions)
        {
            throw new InvalidOperationException(
                $"Fixture process kinds '{kind}' require explicit permission.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = IsDotNetCommand(fileName)
                ? ResolveDotNetCommand(fileName)
                : fileName,
            WorkingDirectory = WorkspacePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in AddFixtureArguments(fileName, arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var allowedAmbientEnvironment = startInfo.Environment
            .Where(static variable =>
                AllowedAmbientVariables.Contains(variable.Key))
            .ToArray();
        startInfo.Environment.Clear();
        foreach (var variable in allowedAmbientEnvironment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        foreach (var variable in EnvironmentVariables)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        return startInfo;
    }

    private IEnumerable<string> AddFixtureArguments(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments)
        {
            yield return argument;
        }

        if (!IsDotNetCommand(fileName) || arguments.Count == 0)
        {
            yield break;
        }

        if (string.Equals(
                arguments[0],
                "restore",
                StringComparison.OrdinalIgnoreCase))
        {
            yield return "--configfile";
            yield return NuGetConfigPath;
        }
        else if (string.Equals(
                     arguments[0],
                     "build",
                     StringComparison.OrdinalIgnoreCase))
        {
            yield return $"-p:RestoreConfigFile={NuGetConfigPath}";
        }
    }

    private static bool IsDotNetCommand(string fileName)
    {
        var commandName = Path.GetFileName(fileName);
        return string.Equals(
                   commandName,
                   "dotnet",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   commandName,
                   "dotnet.exe",
                   StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveDotNetCommand(string fileName)
    {
        if (string.IsNullOrEmpty(Path.GetDirectoryName(fileName)))
        {
            return DotNetHostPath;
        }

        var fullPath = Path.GetFullPath(fileName);
        if (!IsExecutableFile(fullPath))
        {
            throw new FileNotFoundException(
                $"The configured dotnet host '{fullPath}' is not executable.",
                fullPath);
        }

        return fullPath;
    }

    private static string ResolveDotNetHostPath()
    {
        var configuredPath = Environment
            .GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?.Trim()
            .Trim('"');
        if (!string.IsNullOrWhiteSpace(configuredPath)
            && Path.IsPathFullyQualified(configuredPath)
            && IsExecutableFile(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var executableName = OperatingSystem.IsWindows()
            ? "dotnet.exe"
            : "dotnet";
        var searchPath = Environment.GetEnvironmentVariable("PATH");
        foreach (var directory in (searchPath ?? string.Empty)
                     .Split(Path.PathSeparator))
        {
            var candidateDirectory = directory.Trim().Trim('"');
            if (candidateDirectory.Length == 0)
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(
                    Path.Combine(candidateDirectory, executableName));
            }
            catch (Exception exception)
                when (exception is ArgumentException
                      or NotSupportedException
                      or PathTooLongException)
            {
                continue;
            }

            if (IsExecutableFile(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "The dotnet host was not found through DOTNET_HOST_PATH or PATH.");
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var mode = File.GetUnixFileMode(path);
        const UnixFileMode executable =
            UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute;
        return (mode & executable) != 0;
    }

    public ValueTask<string> ComputeContentHashAsync(
        CancellationToken cancellationToken = default) =>
        FixtureContentHasher.ComputeAsync(
            WorkspacePath,
            ContentFiles,
            cancellationToken);

    public ValueTask PrepareGitAsync(
        CancellationToken cancellationToken = default)
    {
        if (GitPlan is null)
        {
            throw new InvalidOperationException(
                "Fixture manifest does not declare Git preparation.");
        }

        return FixtureGitPreparer.PrepareAsync(
            this,
            GitPlan,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _cleanupGate.WaitAsync();
        try
        {
            if (_cleaned)
            {
                return;
            }

            try
            {
                await _cleaner.DeleteAsync(RootPath, _ownerId);
            }
            catch (FixtureCleanupException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new FixtureCleanupException(RootPath, exception);
            }

            _cleaned = true;
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    internal static IReadOnlyDictionary<string, string> ReadOnlyEnvironment(
        IDictionary<string, string> values) =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                values,
                StringComparer.Ordinal));
}
