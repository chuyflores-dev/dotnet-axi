namespace DotNetAxi.Testing.Tests;

public sealed class RepositoryFixtureFactoryTests
{
    [Fact]
    public async Task Equivalent_manifests_have_stable_content_and_sdk_identity()
    {
        var baseDirectory = CreateTestBaseDirectory();
        try
        {
            var factory = new RepositoryFixtureFactory(baseDirectory);
            string firstRoot;
            string secondRoot;

            await using (var first = await factory.CreateAsync(ManifestPath()))
            await using (var second = await factory.CreateAsync(ManifestPath()))
            {
                firstRoot = first.RootPath;
                secondRoot = second.RootPath;

                Assert.NotEqual(first.RootPath, second.RootPath);
                Assert.Equal(first.ContentHash, second.ContentHash);
                Assert.Equal(
                    first.ContentHash,
                    await first.ComputeContentHashAsync());
                Assert.Equal(
                    [
                        "README.md",
                        "global.json",
                        "src/App/App.csproj",
                        "src/App/Program.cs",
                    ],
                    first.ContentFiles);
                Assert.Equal("factory-basic", first.Identity.Name);
                Assert.Equal(1729, first.Identity.Seed);
                Assert.Equal("10.0.302", first.Identity.SelectedSdk.Version);
                Assert.Equal(
                    first.Identity.SelectedSdk,
                    first.Toolchain.SelectedSdk);

                var readme = await File.ReadAllTextAsync(
                    Path.Combine(first.WorkspacePath, "README.md"));
                Assert.Contains("factory-basic", readme);
                Assert.Contains("1729", readme);
                Assert.Contains("10.0.302", readme);

                var globalJson = await File.ReadAllTextAsync(
                    Path.Combine(first.WorkspacePath, "global.json"));
                Assert.Contains("\"version\": \"10.0.302\"", globalJson);
                Assert.True(File.Exists(first.MetadataPath));
            }

            Assert.False(Directory.Exists(firstRoot));
            Assert.False(Directory.Exists(secondRoot));
        }
        finally
        {
            DeleteTestBaseDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task Concurrent_instances_isolate_all_mutable_state()
    {
        var baseDirectory = CreateTestBaseDirectory();
        try
        {
            var factory = new RepositoryFixtureFactory(baseDirectory);
            var processGitConfig =
                Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
            var fixtures = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => factory
                        .CreateAsync(ManifestPath())
                        .AsTask()));

            try
            {
                Assert.Single(
                    fixtures
                        .Select(static fixture => fixture.ContentHash)
                        .Distinct(StringComparer.Ordinal));
                Assert.Equal(
                    fixtures.Length,
                    fixtures
                        .Select(static fixture => fixture.RootPath)
                        .Distinct(PathComparer())
                        .Count());
                Assert.Equal(
                    fixtures.Length,
                    fixtures
                        .Select(static fixture => fixture.GitConfigPath)
                        .Distinct(PathComparer())
                        .Count());
                Assert.Equal(
                    fixtures.Length,
                    fixtures
                        .Select(static fixture => fixture.CachePath)
                        .Distinct(PathComparer())
                        .Count());
                Assert.Equal(
                    fixtures.Length,
                    fixtures
                        .Select(static fixture => fixture.ArtifactsPath)
                        .Distinct(PathComparer())
                        .Count());

                foreach (var fixture in fixtures)
                {
                    Assert.Equal(
                        fixture.GitConfigPath,
                        fixture.EnvironmentVariables["GIT_CONFIG_GLOBAL"]);
                    Assert.Equal(
                        fixture.DotNetHomePath,
                        fixture.EnvironmentVariables["DOTNET_CLI_HOME"]);
                    Assert.Equal(
                        fixture.NuGetPackagesPath,
                        fixture.EnvironmentVariables["NUGET_PACKAGES"]);
                    Assert.Equal(
                        fixture.TempPath,
                        fixture.EnvironmentVariables["TMPDIR"]);
                    Assert.StartsWith(
                        fixture.RootPath,
                        fixture.WorkspacePath,
                        PathComparison());
                }

                await File.WriteAllTextAsync(
                    Path.Combine(fixtures[0].ArtifactsPath, "result.txt"),
                    "isolated");
                Assert.DoesNotContain(
                    fixtures.Skip(1),
                    fixture => File.Exists(
                        Path.Combine(fixture.ArtifactsPath, "result.txt")));
                Assert.Equal(
                    processGitConfig,
                    Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL"));
            }
            finally
            {
                await Task.WhenAll(
                    fixtures.Select(static fixture =>
                        fixture.DisposeAsync().AsTask()));
            }

            Assert.All(
                fixtures,
                static fixture =>
                    Assert.False(Directory.Exists(fixture.RootPath)));
        }
        finally
        {
            DeleteTestBaseDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task Process_execution_requires_explicit_classification_permission()
    {
        var baseDirectory = CreateTestBaseDirectory();
        try
        {
            var factory = new RepositoryFixtureFactory(baseDirectory);
            await using var passive = await factory.CreateAsync(ManifestPath());

            Assert.Throws<InvalidOperationException>(
                () => passive.CreateProcessStartInfo(
                    FixtureProcessKind.Tooling,
                    "git",
                    "status"));
            Assert.Throws<InvalidOperationException>(
                () => passive.CreateProcessStartInfo(
                    FixtureProcessKind.Restore,
                    "dotnet",
                    "restore"));
            Assert.Throws<InvalidOperationException>(
                () => passive.CreateProcessStartInfo(
                    FixtureProcessKind.RepositoryCode,
                    "dotnet",
                    "test"));
            Assert.Throws<InvalidOperationException>(
                () => passive.CreateProcessStartInfo(
                    FixtureProcessKind.Restore
                    | FixtureProcessKind.RepositoryCode,
                    "dotnet",
                    "build"));

            await using var restoreOnly = await factory.CreateAsync(
                ManifestPath(),
                new RepositoryFixtureOptions(
                    FixtureExecutionPermissions.Restore));
            Assert.Throws<InvalidOperationException>(
                () => restoreOnly.CreateProcessStartInfo(
                    FixtureProcessKind.Restore
                    | FixtureProcessKind.RepositoryCode,
                    "dotnet",
                    "build"));

            await using var repositoryCodeOnly = await factory.CreateAsync(
                ManifestPath(),
                new RepositoryFixtureOptions(
                    FixtureExecutionPermissions.RepositoryCode));
            Assert.Throws<InvalidOperationException>(
                () => repositoryCodeOnly.CreateProcessStartInfo(
                    FixtureProcessKind.Restore
                    | FixtureProcessKind.RepositoryCode,
                    "dotnet",
                    "build"));

            var permissions =
                FixtureExecutionPermissions.Tooling
                | FixtureExecutionPermissions.Restore
                | FixtureExecutionPermissions.RepositoryCode;
            await using var executing = await factory.CreateAsync(
                ManifestPath(),
                new RepositoryFixtureOptions(permissions));
            var startInfo = executing.CreateProcessStartInfo(
                FixtureProcessKind.Restore,
                "dotnet",
                "restore",
                "--locked-mode");

            Assert.Equal(executing.WorkspacePath, startInfo.WorkingDirectory);
            Assert.Equal(
                ["restore", "--locked-mode"],
                startInfo.ArgumentList);
            Assert.Equal(
                executing.GitConfigPath,
                startInfo.Environment["GIT_CONFIG_GLOBAL"]);
            Assert.Equal(
                executing.NuGetHttpCachePath,
                startInfo.Environment["NUGET_HTTP_CACHE_PATH"]);
            Assert.True(startInfo.RedirectStandardOutput);
            Assert.True(startInfo.RedirectStandardError);
            Assert.False(startInfo.UseShellExecute);

            var buildStartInfo = executing.CreateProcessStartInfo(
                FixtureProcessKind.Restore
                | FixtureProcessKind.RepositoryCode,
                "dotnet",
                "build");
            Assert.Equal(["build"], buildStartInfo.ArgumentList);
        }
        finally
        {
            DeleteTestBaseDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task Build_target_casing_must_match_materialized_path()
    {
        var baseDirectory = CreateTestBaseDirectory();
        var manifestDirectory = Path.Combine(baseDirectory, "manifest");
        Directory.CreateDirectory(manifestDirectory);
        var manifestPath = Path.Combine(manifestDirectory, "fixture.json");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(manifestDirectory, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                manifestPath,
                """
                {
                  "schema": "dotnet-axi/fixture/v1",
                  "name": "target-case",
                  "seed": 1,
                  "sdk": {
                    "version": "10.0.302",
                    "rollForward": "latestPatch",
                    "allowPrerelease": false
                  },
                  "build": {
                    "target": "src/app/App.csproj",
                    "expectedOutcome": "success"
                  },
                  "files": [
                    {
                      "path": "src/App/App.csproj",
                      "template": "App.csproj",
                      "expandTokens": false
                    }
                  ]
                }
                """);
            var factory = new RepositoryFixtureFactory(
                Path.Combine(baseDirectory, "instances"));

            var exception = await Assert.ThrowsAsync<FixtureManifestException>(
                () => factory.CreateAsync(manifestPath).AsTask());

            Assert.Contains("casing", exception.Message);
            Assert.Empty(
                Directory.EnumerateFileSystemEntries(factory.BaseDirectory));
        }
        finally
        {
            DeleteTestBaseDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task Cleanup_failure_is_visible_and_can_be_retried()
    {
        var baseDirectory = CreateTestBaseDirectory();
        try
        {
            var cleaner = new FailOnceDirectoryCleaner(
                new FixtureDirectoryCleaner());
            var factory = new RepositoryFixtureFactory(
                baseDirectory,
                cleaner);
            var fixture = await factory.CreateAsync(ManifestPath());

            var exception = await Assert.ThrowsAsync<FixtureCleanupException>(
                () => fixture.DisposeAsync().AsTask());

            Assert.Equal(fixture.RootPath, exception.RootPath);
            Assert.True(Directory.Exists(fixture.RootPath));

            await fixture.DisposeAsync();

            Assert.False(Directory.Exists(fixture.RootPath));
            Assert.Equal(2, cleaner.CallCount);
        }
        finally
        {
            DeleteTestBaseDirectory(baseDirectory);
        }
    }

    [Fact]
    public async Task Manifest_path_escape_is_rejected_before_materialization()
    {
        var baseDirectory = CreateTestBaseDirectory();
        var manifestDirectory = Path.Combine(baseDirectory, "manifest");
        Directory.CreateDirectory(manifestDirectory);
        var manifestPath = Path.Combine(manifestDirectory, "fixture.json");
        try
        {
            await File.WriteAllTextAsync(
                manifestPath,
                """
                {
                  "schema": "dotnet-axi/fixture/v1",
                  "name": "escape",
                  "seed": 1,
                  "sdk": {
                    "version": "10.0.302",
                    "rollForward": "latestPatch",
                    "allowPrerelease": false
                  },
                  "files": [
                    {
                      "path": "../outside.txt",
                      "template": "template.txt",
                      "expandTokens": false
                    }
                  ]
                }
                """);
            var factory = new RepositoryFixtureFactory(
                Path.Combine(baseDirectory, "instances"));

            var exception = await Assert.ThrowsAsync<FixtureManifestException>(
                () => factory.CreateAsync(manifestPath).AsTask());

            Assert.Contains("cannot contain", exception.Message);
            Assert.Empty(
                Directory.EnumerateFileSystemEntries(factory.BaseDirectory));
        }
        finally
        {
            DeleteTestBaseDirectory(baseDirectory);
        }
    }

    private static string ManifestPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Factory",
            "basic",
            "fixture.json");

    private static string CreateTestBaseDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-testing-tests",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestBaseDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed class FailOnceDirectoryCleaner(
        IFixtureDirectoryCleaner inner)
        : IFixtureDirectoryCleaner
    {
        private int _callCount;

        public int CallCount => _callCount;

        public ValueTask DeleteAsync(string rootPath, string ownerId)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                throw new IOException("Simulated cleanup failure.");
            }

            return inner.DeleteAsync(rootPath, ownerId);
        }
    }
}
