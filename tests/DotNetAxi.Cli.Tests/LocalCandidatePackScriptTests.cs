using System.Diagnostics;

namespace DotNetAxi.Cli.Tests;

public sealed class LocalCandidatePackScriptTests
{
    [Fact]
    public async Task Pack_script_keeps_a_persistent_lock_and_rejects_contention()
    {
        using var workspace = new TestWorkspace();
        var lockPath = Path.Combine(workspace.PackageRoot, ".dnaxi-pack.lock");
        await using (var heldLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            var result = await workspace.RunAsync("0.5.0-alpha.lock-test");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                "Another local dnaxi candidate pack is already running",
                result.Output,
                StringComparison.Ordinal);
            Assert.True(File.Exists(lockPath));
        }

        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task Pack_script_creates_verifier_compatible_version_directories()
    {
        using var workspace = new TestWorkspace();

        var first = await workspace.RunAsync("0.5.0-alpha.local.1");
        var second = await workspace.RunAsync("0.5.0-alpha.local.2");

        Assert.True(first.ExitCode == 0, first.Output);
        Assert.True(second.ExitCode == 0, second.Output);
        AssertPackagePair(workspace.PackageRoot, "0.5.0-alpha.local.1");
        AssertPackagePair(workspace.PackageRoot, "0.5.0-alpha.local.2");
        Assert.Empty(Directory.EnumerateFiles(
            workspace.PackageRoot,
            "*.nupkg",
            SearchOption.TopDirectoryOnly));
        Assert.True(File.Exists(Path.Combine(
            workspace.PackageRoot,
            ".dnaxi-pack.lock")));
        var arguments = await File.ReadAllLinesAsync(workspace.InvocationLog);
        Assert.Contains("--no-restore", arguments);
    }

    [Fact]
    public async Task Pack_script_rejects_an_existing_version_without_repacking()
    {
        using var workspace = new TestWorkspace();
        const string version = "0.5.0-alpha.local.existing";
        var first = await workspace.RunAsync(version);
        File.Delete(workspace.InvocationLog);

        var repeated = await workspace.RunAsync(version);

        Assert.True(first.ExitCode == 0, first.Output);
        Assert.NotEqual(0, repeated.ExitCode);
        Assert.Contains("already exists", repeated.Output, StringComparison.Ordinal);
        Assert.Contains(
            "choose a new candidate version",
            repeated.Output,
            StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.InvocationLog));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Pack_script_rejects_path_segment_versions_without_repacking(
        string version)
    {
        using var workspace = new TestWorkspace();

        var result = await workspace.RunAsync(version);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Version must be", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.InvocationLog));
    }

    [Fact]
    public async Task Pack_script_cleans_failed_staging_and_allows_retry()
    {
        using var workspace = new TestWorkspace();
        const string version = "0.5.0-alpha.local.retry";

        var failed = await workspace.RunAsync(version, failPack: true);

        Assert.NotEqual(0, failed.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(workspace.PackageRoot, version)));
        Assert.Empty(Directory.EnumerateDirectories(workspace.PackageRoot));

        var retried = await workspace.RunAsync(version);

        Assert.True(retried.ExitCode == 0, retried.Output);
        AssertPackagePair(workspace.PackageRoot, version);
    }

    private static void AssertPackagePair(string packageRoot, string version)
    {
        var versionDirectory = Path.Combine(packageRoot, version);
        var packages = Directory
            .EnumerateFiles(versionDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path) is ".nupkg" or ".snupkg")
            .Order(StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path)!)
            .ToArray();
        Assert.Equal(
            [
                $"dnaxi.{version}.nupkg",
                $"dnaxi.{version}.snupkg",
            ],
            packages);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root;
        private readonly string _fakeDotNetHost;

        public TestWorkspace()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                $"dnaxi-local-pack-{Guid.NewGuid():N}");
            PackageRoot = Path.Combine(_root, "packages");
            InvocationLog = Path.Combine(_root, "dotnet-arguments.txt");
            _fakeDotNetHost = Path.Combine(_root, "fake-dotnet.ps1");
            Directory.CreateDirectory(PackageRoot);
            File.WriteAllText(
                _fakeDotNetHost,
                """
                Set-StrictMode -Version Latest
                $ErrorActionPreference = "Stop"
                Set-Content -LiteralPath $env:DNAXI_PACK_TEST_LOG -Value $args
                $outputIndex = [Array]::IndexOf($args, "--output")
                if ($outputIndex -lt 0 -or $outputIndex + 1 -ge $args.Count) {
                    exit 41
                }
                $outputDirectory = $args[$outputIndex + 1]
                $versionArguments = @($args | Where-Object {
                    $_ -like "-p:DotNetAxiBuildVersion=*"
                })
                if ($versionArguments.Count -ne 1) {
                    exit 42
                }
                if ($env:DNAXI_PACK_TEST_FAIL -eq "1") {
                    exit 43
                }
                $version = $versionArguments[0].Substring(
                    "-p:DotNetAxiBuildVersion=".Length)
                [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
                New-Item -ItemType File -Path (
                    Join-Path $outputDirectory "dnaxi.$version.nupkg") | Out-Null
                New-Item -ItemType File -Path (
                    Join-Path $outputDirectory "dnaxi.$version.snupkg") | Out-Null
                """);
        }

        public string PackageRoot { get; }

        public string InvocationLog { get; }

        public async Task<ScriptResult> RunAsync(
            string version,
            bool failPack = false)
        {
            var start = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(Path.Combine(
                RepositoryRoot(),
                "eng",
                "pack-local-candidate.ps1"));
            start.ArgumentList.Add("-Version");
            start.ArgumentList.Add(version);
            start.ArgumentList.Add("-PackageRoot");
            start.ArgumentList.Add(PackageRoot);
            start.Environment["DOTNET_HOST_PATH"] = _fakeDotNetHost;
            start.Environment["DNAXI_PACK_TEST_LOG"] = InvocationLog;
            start.Environment["DNAXI_PACK_TEST_FAIL"] = failPack ? "1" : "0";

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start pwsh.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ScriptResult(
                process.ExitCode,
                await standardOutput + await standardError);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private static string RepositoryRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed record ScriptResult(int ExitCode, string Output);
}
