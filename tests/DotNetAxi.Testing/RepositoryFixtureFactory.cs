using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DotNetAxi.Testing;

public sealed class RepositoryFixtureFactory
{
    private static readonly UTF8Encoding Utf8WithoutBom =
        new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions MetadataJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    private readonly IFixtureDirectoryCleaner _cleaner;

    public RepositoryFixtureFactory(string? baseDirectory = null)
        : this(baseDirectory, new FixtureDirectoryCleaner())
    {
    }

    internal RepositoryFixtureFactory(
        string? baseDirectory,
        IFixtureDirectoryCleaner cleaner)
    {
        ArgumentNullException.ThrowIfNull(cleaner);
        BaseDirectory = Path.GetFullPath(
            baseDirectory
            ?? Path.Combine(Path.GetTempPath(), "dotnet-axi-fixtures"));
        Directory.CreateDirectory(BaseDirectory);
        _cleaner = cleaner;
    }

    public string BaseDirectory { get; }

    public async ValueTask<RepositoryFixture> CreateAsync(
        string manifestPath,
        RepositoryFixtureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await FixtureManifestLoader.LoadAsync(
            manifestPath,
            cancellationToken);
        options ??= new RepositoryFixtureOptions();

        var ownerId = Guid.NewGuid().ToString("N");
        var rootPath = Path.Combine(
            BaseDirectory,
            $"{plan.Identity.Name}-{Environment.ProcessId}-{ownerId}");
        var workspacePath = Path.Combine(rootPath, "workspace");
        var statePath = Path.Combine(rootPath, "state");
        var metadataPath = Path.Combine(statePath, "fixture.json");
        var gitConfigPath = Path.Combine(statePath, "gitconfig");
        var homePath = Path.Combine(statePath, "home");
        var cachePath = Path.Combine(statePath, "cache");
        var artifactsPath = Path.Combine(statePath, "artifacts");
        var tempPath = Path.Combine(statePath, "temp");
        var dotNetHomePath = Path.Combine(statePath, "dotnet-home");
        var nuGetPackagesPath = Path.Combine(
            statePath,
            "nuget",
            "packages");
        var nuGetHttpCachePath = Path.Combine(
            statePath,
            "nuget",
            "http-cache");
        var nuGetConfigPath = Path.Combine(
            statePath,
            "nuget",
            "NuGet.Config");
        var nuGetPluginsCachePath = Path.Combine(
            statePath,
            "nuget",
            "plugins-cache");

        try
        {
            Directory.CreateDirectory(rootPath);
            await File.WriteAllTextAsync(
                Path.Combine(
                    rootPath,
                    FixtureDirectoryCleaner.OwnerMarkerName),
                ownerId,
                Utf8WithoutBom,
                cancellationToken);

            foreach (var path in new[]
                     {
                         workspacePath,
                         statePath,
                         homePath,
                         cachePath,
                         artifactsPath,
                         tempPath,
                         dotNetHomePath,
                         nuGetPackagesPath,
                         nuGetHttpCachePath,
                         nuGetPluginsCachePath,
                     })
            {
                Directory.CreateDirectory(path);
            }

            await File.WriteAllTextAsync(
                gitConfigPath,
                """
                [user]
                    name = dotnet-axi fixture
                    email = fixture@dotnet-axi.invalid
                [init]
                    defaultBranch = main
                [commit]
                    gpgSign = false

                """.ReplaceLineEndings("\n"),
                Utf8WithoutBom,
                cancellationToken);
            await File.WriteAllTextAsync(
                nuGetConfigPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                  </packageSources>
                  <disabledPackageSources>
                    <clear />
                  </disabledPackageSources>
                </configuration>

                """.ReplaceLineEndings("\n"),
                Utf8WithoutBom,
                cancellationToken);

            foreach (var file in plan.Files)
            {
                var destinationPath = Path.Combine(
                    workspacePath,
                    file.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidOperationException(
                        "Fixture destination must have a parent directory."));
                await File.WriteAllBytesAsync(
                    destinationPath,
                    file.Content,
                    cancellationToken);
            }

            var contentFiles = Array.AsReadOnly(
                plan.Files
                    .Select(static file => file.RelativePath)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            var expectedContentHash = FixtureContentHasher.Compute(plan.Files);
            var actualContentHash = await FixtureContentHasher.ComputeAsync(
                workspacePath,
                contentFiles,
                cancellationToken);
            if (!string.Equals(
                    expectedContentHash,
                    actualContentHash,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "Materialized fixture content does not match its source plan.");
            }

            var toolchain = new FixtureToolchainIdentity(
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.RuntimeIdentifier,
                RuntimeInformation.ProcessArchitecture
                    .ToString()
                    .ToLowerInvariant(),
                RuntimeInformation.OSDescription,
                plan.Identity.SelectedSdk);
            await WriteMetadataAsync(
                metadataPath,
                plan.Identity,
                plan.Capabilities,
                plan.BuildVerification,
                plan.TestRunner,
                toolchain,
                actualContentHash,
                cancellationToken);
            var environmentVariables = CreateEnvironment(
                gitConfigPath,
                homePath,
                cachePath,
                artifactsPath,
                tempPath,
                dotNetHomePath,
                nuGetPackagesPath,
                nuGetHttpCachePath,
                nuGetConfigPath,
                nuGetPluginsCachePath);

            return new RepositoryFixture(
                rootPath,
                workspacePath,
                statePath,
                metadataPath,
                gitConfigPath,
                homePath,
                cachePath,
                artifactsPath,
                tempPath,
                dotNetHomePath,
                nuGetPackagesPath,
                nuGetHttpCachePath,
                nuGetConfigPath,
                actualContentHash,
                contentFiles,
                plan.Identity,
                plan.Capabilities,
                plan.BuildVerification,
                plan.TestRunner,
                toolchain,
                options,
                environmentVariables,
                ownerId,
                _cleaner);
        }
        catch (Exception creationException)
        {
            try
            {
                await _cleaner.DeleteAsync(rootPath, ownerId);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Fixture creation and cleanup both failed.",
                    creationException,
                    cleanupException);
            }

            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> CreateEnvironment(
        string gitConfigPath,
        string homePath,
        string cachePath,
        string artifactsPath,
        string tempPath,
        string dotNetHomePath,
        string nuGetPackagesPath,
        string nuGetHttpCachePath,
        string nuGetConfigPath,
        string nuGetPluginsCachePath)
    {
        var environment = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["HOME"] = homePath,
            ["USERPROFILE"] = homePath,
            ["APPDATA"] = Path.Combine(homePath, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(homePath, "AppData", "Local"),
            ["XDG_CONFIG_HOME"] = Path.Combine(homePath, ".config"),
            ["XDG_CACHE_HOME"] = cachePath,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = gitConfigPath,
            ["GIT_ATTR_NOSYSTEM"] = "1",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
            ["DOTNET_CLI_HOME"] = dotNetHomePath,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_UI_LANGUAGE"] = "en-US",
            ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["NUGET_PACKAGES"] = nuGetPackagesPath,
            ["NUGET_HTTP_CACHE_PATH"] = nuGetHttpCachePath,
            ["NUGET_PLUGINS_CACHE_PATH"] = nuGetPluginsCachePath,
            ["RestoreConfigFile"] = nuGetConfigPath,
            ["TMPDIR"] = tempPath,
            ["TMP"] = tempPath,
            ["TEMP"] = tempPath,
            ["DOTNET_AXI_ARTIFACTS"] = artifactsPath,
        };
        Directory.CreateDirectory(environment["XDG_CONFIG_HOME"]);
        Directory.CreateDirectory(environment["APPDATA"]);
        Directory.CreateDirectory(environment["LOCALAPPDATA"]);

        return RepositoryFixture.ReadOnlyEnvironment(environment);
    }

    private static async ValueTask WriteMetadataAsync(
        string metadataPath,
        RepositoryFixtureIdentity identity,
        IReadOnlyList<string> capabilities,
        FixtureBuildVerification? buildVerification,
        string? testRunner,
        FixtureToolchainIdentity toolchain,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                schema = "dotnet-axi/fixture-instance/v1",
                identity,
                capabilities,
                build = buildVerification,
                testRunner,
                contentHash,
                toolchain,
            },
            MetadataJsonOptions);
        await File.WriteAllTextAsync(
            metadataPath,
            json.ReplaceLineEndings("\n") + "\n",
            Utf8WithoutBom,
            cancellationToken);
    }
}
