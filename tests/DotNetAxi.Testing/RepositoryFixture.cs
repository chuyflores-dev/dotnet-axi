using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DotNetAxi.Testing;

public sealed class RepositoryFixture : IAsyncDisposable
{
    private static readonly HashSet<string> AmbientGitVariables =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_COMMON_DIR",
        "GIT_INDEX_FILE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_NAMESPACE",
        "GIT_CEILING_DIRECTORIES",
        "GIT_DISCOVERY_ACROSS_FILESYSTEM",
        "GIT_CONFIG",
        "GIT_CONFIG_COUNT",
        "GIT_CONFIG_SYSTEM",
        "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_NOSYSTEM",
    };

    private readonly IFixtureDirectoryCleaner _cleaner;
    private readonly string _ownerId;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private bool _cleaned;

    internal RepositoryFixture(
        string rootPath,
        string workspacePath,
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
        string contentHash,
        IReadOnlyList<string> contentFiles,
        RepositoryFixtureIdentity identity,
        FixtureToolchainIdentity toolchain,
        RepositoryFixtureOptions options,
        IReadOnlyDictionary<string, string> environmentVariables,
        string ownerId,
        IFixtureDirectoryCleaner cleaner)
    {
        RootPath = rootPath;
        WorkspacePath = workspacePath;
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
        ContentHash = contentHash;
        ContentFiles = contentFiles;
        Identity = identity;
        Toolchain = toolchain;
        Options = options;
        EnvironmentVariables = environmentVariables;
        _ownerId = ownerId;
        _cleaner = cleaner;
    }

    public string RootPath { get; }

    public string WorkspacePath { get; }

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

    public string ContentHash { get; }

    public IReadOnlyList<string> ContentFiles { get; }

    public RepositoryFixtureIdentity Identity { get; }

    public FixtureToolchainIdentity Toolchain { get; }

    public RepositoryFixtureOptions Options { get; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    public ProcessStartInfo CreateProcessStartInfo(
        FixtureProcessKind kind,
        string fileName,
        params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        var requiredPermission = kind switch
        {
            FixtureProcessKind.Tooling =>
                FixtureExecutionPermissions.Tooling,
            FixtureProcessKind.Restore =>
                FixtureExecutionPermissions.Restore,
            FixtureProcessKind.RepositoryCode =>
                FixtureExecutionPermissions.RepositoryCode,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if ((Options.ExecutionPermissions & requiredPermission)
            != requiredPermission)
        {
            throw new InvalidOperationException(
                $"Fixture process kind '{kind}' requires explicit permission.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = WorkspacePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in startInfo.Environment.Keys
                     .Where(IsAmbientGitVariable)
                     .ToArray())
        {
            startInfo.Environment.Remove(variable);
        }

        foreach (var variable in EnvironmentVariables)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        return startInfo;
    }

    private static bool IsAmbientGitVariable(string variable) =>
        AmbientGitVariables.Contains(variable)
        || variable.StartsWith(
            "GIT_CONFIG_KEY_",
            StringComparison.OrdinalIgnoreCase)
        || variable.StartsWith(
            "GIT_CONFIG_VALUE_",
            StringComparison.OrdinalIgnoreCase);

    public ValueTask<string> ComputeContentHashAsync(
        CancellationToken cancellationToken = default) =>
        FixtureContentHasher.ComputeAsync(
            WorkspacePath,
            ContentFiles,
            cancellationToken);

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
