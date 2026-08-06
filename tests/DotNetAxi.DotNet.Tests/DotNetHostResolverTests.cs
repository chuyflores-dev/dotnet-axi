using DotNetAxi.Contracts;

namespace DotNetAxi.DotNet.Tests;

public sealed class DotNetHostResolverTests
{
    [Fact]
    public async Task Path_host_reports_its_executable_selected_sdk_and_supported_compatibility()
    {
        using var fixture = new HostFixture();
        var first = Path.Combine(fixture.ToolsRoot, "first");
        var second = Path.Combine(fixture.ToolsRoot, "second");
        var executable = Path.GetFullPath(Path.Combine(second, HostName));
        var sdkBase = Path.Combine(fixture.Root, "sdks");
        var sdkPath = Path.GetFullPath(Path.Combine(sdkBase, "10.0.302"));
        var resolver = fixture.Resolver(
            string.Join(Path.PathSeparator, first, second),
            existingPaths: [executable, Path.Combine(sdkPath, "Microsoft.Build.dll")],
            Completed(Info("10.0.302", sdkPath)));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.True(result.IsResolved);
        Assert.Equal(executable, result.ExecutablePath);
        Assert.Equal("10.0.302", result.Sdk?.Version);
        Assert.Equal(sdkPath, result.Sdk?.SdkPath);
        Assert.Equal(DotNetHostCompatibility.Supported, result.Sdk?.Compatibility);
    }

    [Fact]
    public async Task Explicit_supported_host_is_used_instead_of_path()
    {
        using var fixture = new HostFixture();
        var pathHost = Path.Combine(fixture.ToolsRoot, "path", HostName);
        var explicitHost = Path.Combine(fixture.ToolsRoot, "selected", HostName);
        var sdkBase = Path.Combine(fixture.Root, "sdks");
        var sdkPath = Path.Combine(sdkBase, "9.0.308");
        var resolver = fixture.Resolver(
            Path.GetDirectoryName(pathHost),
            existingPaths: [pathHost, explicitHost, Path.Combine(sdkPath, "Microsoft.Build.dll")],
            Completed(Info("9.0.308", sdkPath)));

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root, explicitHost));

        Assert.True(result.IsResolved);
        Assert.Equal(Path.GetFullPath(explicitHost), result.ExecutablePath);
        Assert.Equal(DotNetHostCompatibility.Unverified, result.Sdk?.Compatibility);
        Assert.All(
            fixture.Runner.Requests,
            request => Assert.Equal(Path.GetFullPath(explicitHost), request.ExecutablePath));
    }

    [Fact]
    public async Task Global_json_selection_uses_the_host_selected_roll_forward_sdk()
    {
        using var fixture = new HostFixture(
            """
            {
              "sdk": {
                "version": "10.0.400",
                "rollForward": "latestFeature",
                "allowPrerelease": false
              }
            }
            """);
        var executable = fixture.HostPath;
        var sdkBase = Path.Combine(fixture.Root, "sdks");
        var sdkPath = Path.Combine(sdkBase, "10.0.401");
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [executable, Path.Combine(sdkPath, "Microsoft.Build.dll")],
            Completed(Info("10.0.401", sdkPath)));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.True(result.IsResolved);
        Assert.Equal("10.0.401", result.Sdk?.Version);
        Assert.Equal(DotNetHostCompatibility.Unverified, result.Sdk?.Compatibility);
        Assert.All(
            fixture.Runner.Requests,
            request => Assert.Equal(fixture.Root, request.WorkingDirectory));
        Assert.Equal(
            [["--info"]],
            fixture.Runner.Requests.Select(static request => request.Arguments));
        Assert.All(
            fixture.Runner.Requests,
            request => Assert.Equal("en-US", request.Environment["DOTNET_CLI_UI_LANGUAGE"]));
    }

    [Fact]
    public async Task Unavailable_global_json_sdk_has_an_actionable_structured_failure()
    {
        using var fixture = new HostFixture();
        var executable = fixture.HostPath;
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [executable],
            Completed("A compatible SDK is not installed.\n", exitCode: 145));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.False(result.IsResolved);
        Assert.Equal(executable, result.ExecutablePath);
        Assert.Equal(DotNetHostFailureReason.SdkUnavailable, result.Failure?.Reason);
        Assert.Equal("sdk.selection_failed", result.Failure?.Code);
        Assert.Contains("global.json", result.Failure?.Correction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prerelease_selected_by_global_json_is_retained_without_version_coercion()
    {
        using var fixture = new HostFixture(
            """
            {
              "sdk": {
                "version": "10.0.100-preview.3",
                "rollForward": "latestFeature",
                "allowPrerelease": true
              }
            }
            """);
        var executable = fixture.HostPath;
        var sdkBase = Path.Combine(fixture.Root, "sdks");
        var version = "10.0.100-preview.3";
        var sdkPath = Path.Combine(sdkBase, version);
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [executable, Path.Combine(sdkPath, "Microsoft.Build.dll")],
            Completed(Info(version, sdkPath)));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.True(result.IsResolved);
        Assert.Equal(version, result.Sdk?.Version);
        Assert.Equal(DotNetHostCompatibility.Unverified, result.Sdk?.Compatibility);
    }

    [Fact]
    public async Task Repo_local_sdk_path_from_global_json_is_the_selected_authority()
    {
        using var fixture = new HostFixture(
            """
            {
              "sdk": {
                "version": "10.0.302",
                "paths": [".dotnet"]
              }
            }
            """);
        var executable = fixture.HostPath;
        var sdkPath = Path.Combine(fixture.Root, ".dotnet", "sdk", "10.0.302");
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [executable, Path.Combine(sdkPath, "Microsoft.Build.dll")],
            Completed(Info("10.0.302", sdkPath)));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.True(result.IsResolved);
        Assert.Equal(Path.GetFullPath(sdkPath), result.Sdk?.SdkPath);
        Assert.Single(fixture.Runner.Requests);
    }

    [Fact]
    public async Task Duplicate_sdk_versions_honor_the_exact_base_path_selected_by_host()
    {
        using var fixture = new HostFixture();
        var executable = fixture.HostPath;
        var firstSdk = Path.Combine(fixture.Root, "first", "sdk", "10.0.302");
        var selectedSdk = Path.Combine(fixture.Root, "second", "sdk", "10.0.302");
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths:
            [
                executable,
                Path.Combine(firstSdk, "Microsoft.Build.dll"),
                Path.Combine(selectedSdk, "Microsoft.Build.dll"),
            ],
            Completed(Info("10.0.302", selectedSdk)));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.True(result.IsResolved);
        Assert.Equal(Path.GetFullPath(selectedSdk), result.Sdk?.SdkPath);
        Assert.NotEqual(Path.GetFullPath(firstSdk), result.Sdk?.SdkPath);
    }

    [Theory]
    [InlineData("10.0.300", DotNetHostCompatibility.Supported)]
    [InlineData("10.0.399", DotNetHostCompatibility.Supported)]
    [InlineData("10.0.400", DotNetHostCompatibility.Unverified)]
    [InlineData("9.0.308", DotNetHostCompatibility.Unverified)]
    [InlineData("10.0.302-preview.1", DotNetHostCompatibility.Unverified)]
    public void Compatibility_is_limited_to_the_tested_stable_feature_band(
        string version,
        DotNetHostCompatibility expected)
    {
        Assert.Equal(expected, DotNetHostResolver.ClassifyCompatibility(version));
    }

    [Fact]
    public async Task Cancelled_sdk_probe_throws_with_the_caller_token()
    {
        using var fixture = new HostFixture();
        using var cancellation = new CancellationTokenSource();
        var executable = fixture.HostPath;
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [executable],
            Cancelled());

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await resolver.ResolveAsync(
                new DotNetHostResolutionRequest(fixture.Root),
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Single(fixture.Runner.Requests);
    }

    [Fact]
    public async Task Selected_sdk_without_its_msbuild_assembly_is_a_structured_mismatch()
    {
        using var fixture = new HostFixture();
        var executable = fixture.HostPath;
        var sdkBase = Path.Combine(fixture.Root, "sdks");
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [executable],
            Completed(Info("10.0.302", Path.Combine(sdkBase, "10.0.302"))));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.False(result.IsResolved);
        Assert.Equal("10.0.302", result.Sdk?.Version);
        Assert.Equal(
            Path.Combine(sdkBase, "10.0.302", "Microsoft.Build.dll"),
            result.Sdk?.MsBuildPath);
        Assert.Equal(DotNetHostFailureReason.MsBuildUnavailable, result.Failure?.Reason);
        Assert.Equal("msbuild.selected_instance_missing", result.Failure?.Code);
        Assert.Contains("reinstall", result.Failure?.Correction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selected_pre_baseline_sdk_is_reported_as_unsupported()
    {
        using var fixture = new HostFixture();
        var executable = fixture.HostPath;
        var sdkBase = Path.Combine(fixture.Root, "sdks");
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [executable],
            Completed(Info("7.0.410", Path.Combine(sdkBase, "7.0.410"))));

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.False(result.IsResolved);
        Assert.Equal("7.0.410", result.Sdk?.Version);
        Assert.Equal(DotNetHostFailureReason.SdkUnsupported, result.Failure?.Reason);
        Assert.Equal("sdk.selected_unsupported", result.Failure?.Code);
    }

    [Fact]
    public async Task Unsupported_explicit_host_fails_without_starting_a_process()
    {
        using var fixture = new HostFixture();
        var unsupported = Path.Combine(fixture.ToolsRoot, "not-dotnet");
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            existingPaths: [unsupported]);

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root, unsupported));

        Assert.False(result.IsResolved);
        Assert.Equal(DotNetHostFailureReason.HostUnsupported, result.Failure?.Reason);
        Assert.Empty(fixture.Runner.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-tools")]
    public async Task Empty_and_relative_path_entries_never_start_a_host(
        string pathValue)
    {
        using var fixture = new HostFixture();
        var candidate = Path.GetFullPath(Path.Combine(pathValue, HostName));
        var resolver = fixture.Resolver(pathValue, [candidate]);

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root));

        Assert.Equal(DotNetHostFailureReason.HostNotFound, result.Failure?.Reason);
        Assert.Empty(fixture.Runner.Requests);
    }

    [Fact]
    public async Task Workspace_local_path_host_is_skipped_for_an_external_host()
    {
        using var fixture = new HostFixture();
        var workspaceTools = Path.Combine(fixture.Root, "tools");
        var workspaceHost = Path.Combine(workspaceTools, HostName);
        var sdkPath = Path.Combine(fixture.Root, "sdks", "10.0.302");
        var resolver = fixture.Resolver(
            string.Join(Path.PathSeparator, workspaceTools, fixture.ToolsRoot),
            [
                workspaceHost,
                fixture.HostPath,
                Path.Combine(sdkPath, "Microsoft.Build.dll"),
            ],
            Completed(Info("10.0.302", sdkPath)));

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root));

        Assert.True(result.IsResolved);
        Assert.Equal(fixture.HostPath, result.ExecutablePath);
        Assert.All(
            fixture.Runner.Requests,
            request => Assert.Equal(fixture.HostPath, request.ExecutablePath));
    }

    [Fact]
    public async Task Explicit_workspace_local_host_never_starts()
    {
        using var fixture = new HostFixture();
        var workspaceHost = Path.Combine(fixture.Root, HostName);
        var resolver = fixture.Resolver(fixture.ToolsRoot, [workspaceHost]);

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root, workspaceHost));

        Assert.Equal(DotNetHostFailureReason.HostUnsupported, result.Failure?.Reason);
        Assert.Empty(fixture.Runner.Requests);
    }

    [Fact]
    public async Task External_symlink_to_workspace_host_never_starts()
    {
        using var fixture = new HostFixture();
        var workspaceHost = Path.Combine(fixture.Root, HostName);
        await File.WriteAllTextAsync(workspaceHost, "repository-controlled");
        try
        {
            File.CreateSymbolicLink(fixture.HostPath, workspaceHost);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return;
        }

        var resolver = fixture.Resolver(fixture.ToolsRoot, [fixture.HostPath]);

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root));

        Assert.Equal(DotNetHostFailureReason.HostNotFound, result.Failure?.Reason);
        Assert.Empty(fixture.Runner.Requests);
    }

    [Fact]
    public async Task Timed_out_sdk_probe_is_distinct_from_an_unavailable_sdk()
    {
        using var fixture = new HostFixture();
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            [fixture.HostPath],
            TimedOut());

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root));

        Assert.Equal(fixture.HostPath, result.ExecutablePath);
        Assert.Equal(DotNetHostFailureReason.SdkProbeTimedOut, result.Failure?.Reason);
        Assert.Equal("sdk.probe_timed_out", result.Failure?.Code);
        Assert.Single(fixture.Runner.Requests);
    }

    [Fact]
    public async Task Failed_sdk_probe_is_distinct_from_an_unavailable_sdk()
    {
        using var fixture = new HostFixture();
        var resolver = fixture.Resolver(
            fixture.ToolsRoot,
            [fixture.HostPath],
            RuntimeFailed());

        var result = await resolver.ResolveAsync(
            new DotNetHostResolutionRequest(fixture.Root));

        Assert.Equal(DotNetHostFailureReason.SdkProbeFailed, result.Failure?.Reason);
        Assert.Equal("sdk.probe_failed", result.Failure?.Code);
        Assert.Single(fixture.Runner.Requests);
    }

    [Fact]
    public async Task Missing_path_host_has_an_actionable_structured_failure()
    {
        using var fixture = new HostFixture();
        var resolver = fixture.Resolver(Path.Combine(fixture.ToolsRoot, "empty"), []);

        var result = await resolver.ResolveAsync(new DotNetHostResolutionRequest(fixture.Root));

        Assert.False(result.IsResolved);
        Assert.Null(result.ExecutablePath);
        Assert.Equal(DotNetHostFailureReason.HostNotFound, result.Failure?.Reason);
        Assert.Equal("dotnet.host_not_found", result.Failure?.Code);
        Assert.Contains("PATH", result.Failure?.Correction, StringComparison.Ordinal);
        Assert.Empty(fixture.Runner.Requests);
    }

    private static string HostName => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    private static ProcessRunResult Completed(string output, int exitCode = 0) =>
        new(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.Completed,
            ProcessStartFailure.None,
            new ProcessExitEvidence(exitCode, null),
            new ProcessCapturedOutput(output, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            TimeSpan.Zero);

    private static ProcessRunResult Cancelled() =>
        new(
            ProcessLifecycle.NotStarted,
            ProcessRunOutcome.Cancelled,
            ProcessStartFailure.None,
            null,
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            TimeSpan.Zero);

    private static ProcessRunResult TimedOut() =>
        new(
            ProcessLifecycle.Terminated,
            ProcessRunOutcome.TimedOut,
            ProcessStartFailure.None,
            new ProcessExitEvidence(143, null),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            TimeSpan.Zero);

    private static ProcessRunResult RuntimeFailed() =>
        new(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.RuntimeFailed,
            ProcessStartFailure.None,
            new ProcessExitEvidence(1, null),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            TimeSpan.Zero);

    private static string Info(string version, string sdkPath) =>
        $"""
        .NET SDK:
         Version:           {version}
         Commit:            fixture

        Runtime Environment:
         OS Name:     Fixture OS
         Base Path:   {sdkPath}{Path.DirectorySeparatorChar}

        .NET SDKs installed:
          {version} [{Path.GetDirectoryName(sdkPath)}]
        """;

    private sealed class HostFixture : IDisposable
    {
        private readonly HashSet<string> _existingPaths = new(PathComparer());

        public HostFixture(string? globalJson = null)
        {
            Root = Path.Combine(Path.GetTempPath(), $"dnaxi-host-{Guid.NewGuid():N}");
            ToolsRoot = Root + "-tools";
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ToolsRoot);
            if (globalJson is not null)
            {
                File.WriteAllText(Path.Combine(Root, "global.json"), globalJson);
            }
        }

        public string Root { get; }

        public string ToolsRoot { get; }

        public string HostPath => Path.Combine(ToolsRoot, HostName);

        public StubProcessRunner Runner { get; } = new();

        public DotNetHostResolver Resolver(
            string? pathValue,
            IEnumerable<string> existingPaths,
            params ProcessRunResult[] results)
        {
            _existingPaths.Clear();
            foreach (var path in existingPaths)
            {
                _existingPaths.Add(Path.GetFullPath(path));
            }

            Runner.SetResults(results);
            return new DotNetHostResolver(
                Runner,
                () => pathValue,
                path => _existingPaths.Contains(Path.GetFullPath(path)),
                static _ => true);
        }

        public void Dispose()
        {
            Directory.Delete(ToolsRoot, recursive: true);
            Directory.Delete(Root, recursive: true);
        }

        private static StringComparer PathComparer() => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _results = new();

        public List<ProcessRunRequest> Requests { get; } = [];

        public void SetResults(IEnumerable<ProcessRunResult> results)
        {
            _results.Clear();
            Requests.Clear();
            foreach (var result in results)
            {
                _results.Enqueue(result);
            }
        }

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(_results.Dequeue());
        }
    }
}
