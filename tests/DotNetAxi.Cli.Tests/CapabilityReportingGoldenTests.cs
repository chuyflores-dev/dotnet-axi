using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class CapabilityReportingGoldenTests
{
    private readonly ToonResultSerializer _serializer = new();

    [Theory]
    [InlineData("supported")]
    [InlineData("missing")]
    [InlineData("unsupported")]
    [InlineData("unverified")]
    [InlineData("timed-out")]
    [InlineData("policy-denied")]
    public async Task Controlled_version_probes_match_the_golden_contract(
        string scenario)
    {
        var fixture = ProbeFixture.Create(scenario);
        var reporter = new CapabilityReporter(
            new StubHostResolver(fixture.Host),
            new StubExternalVersionProbe(fixture.Git, fixture.Ripgrep),
            new StubAssemblyVersionProbe(fixture.Assemblies));

        var capabilities = await reporter.ReportAsync(
            Path.GetFullPath("controlled-workspace"));
        var document = _serializer.Serialize(
            VersionResult.Create("1.2.3-test", capabilities));

        Assert.Equal(ReadFixture($"capabilities-{scenario}.toon"), document);
    }

    [Theory]
    [InlineData("1.10.0", "unsupported")]
    [InlineData("2.11.0", "supported")]
    [InlineData("2.50.1.windows.1", "supported")]
    [InlineData("2.50.1garbage", "unverified")]
    [InlineData("2.50.1.windows.1garbage", "unverified")]
    [InlineData("3.0.0", "unverified")]
    [InlineData("vendor-build", "unverified")]
    public void Git_compatibility_preserves_unsupported_and_unverified_versions(
        string version,
        string expected) =>
        Assert.Equal(
            expected,
            CapabilityReporter.ClassifyGit(version)
                .ToString()
                .ToLowerInvariant());

    [Theory]
    [InlineData("12.1.0", "unsupported")]
    [InlineData("13.0.0", "supported")]
    [InlineData("15.2.0", "supported")]
    [InlineData("15.2oops", "unverified")]
    [InlineData("16.0.0", "unverified")]
    [InlineData("vendor-build", "unverified")]
    public void Ripgrep_compatibility_matches_the_acceleration_boundary(
        string version,
        string expected) =>
        Assert.Equal(
            expected,
            CapabilityReporter.ClassifyRipgrep(version)
                .ToString()
                .ToLowerInvariant());

    private static string ReadFixture(string name) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            .TrimEnd('\r', '\n');

    private sealed record ProbeFixture(
        DotNetHostResolution Host,
        ExternalVersionProbeResult Git,
        ExternalVersionProbeResult Ripgrep,
        IReadOnlyDictionary<string, AssemblyVersionProbeResult> Assemblies)
    {
        public static ProbeFixture Create(string scenario)
        {
            const string dotnet = "/tools/dotnet";
            const string git = "/tools/git";
            const string ripgrep = "/tools/rg";
            return scenario switch
            {
                "supported" => CreateSelected(
                    dotnet,
                    "10.0.302",
                    DotNetHostCompatibility.Supported,
                    hostFailure: null,
                    msBuildVersion: "18.6.3",
                    roslynVersion: "5.0.0",
                    new ExternalVersionProbeResult(git, "2.50.1"),
                    new ExternalVersionProbeResult(ripgrep, "15.2.0")),
                "missing" => new ProbeFixture(
                    new DotNetHostResolution(
                        null,
                        null,
                        Failure(
                            DotNetHostFailureReason.HostNotFound,
                            "dotnet.host_not_found")),
                    new ExternalVersionProbeResult(null, null),
                    new ExternalVersionProbeResult(null, null),
                    new Dictionary<string, AssemblyVersionProbeResult>()),
                "unsupported" => CreateSelected(
                    dotnet,
                    "7.0.410",
                    DotNetHostCompatibility.Unverified,
                    Failure(
                        DotNetHostFailureReason.SdkUnsupported,
                        "sdk.selected_unsupported"),
                    "17.7.0",
                    "4.7.0",
                    new ExternalVersionProbeResult(git, "1.10.0"),
                    new ExternalVersionProbeResult(ripgrep, "12.1.0")),
                "unverified" => CreateSelected(
                    dotnet,
                    "11.0.100",
                    DotNetHostCompatibility.Unverified,
                    hostFailure: null,
                    msBuildVersion: "19.0.0",
                    roslynVersion: "6.0.0",
                    new ExternalVersionProbeResult(git, "3.0.0"),
                    new ExternalVersionProbeResult(ripgrep, "16.0.0")),
                "timed-out" => new ProbeFixture(
                    new DotNetHostResolution(
                        dotnet,
                        null,
                        Failure(
                            DotNetHostFailureReason.SdkProbeTimedOut,
                            "sdk.probe_timed_out")),
                    new ExternalVersionProbeResult(git, "2.50.1"),
                    new ExternalVersionProbeResult(ripgrep, "15.2.0"),
                    new Dictionary<string, AssemblyVersionProbeResult>()),
                "policy-denied" => new ProbeFixture(
                    new DotNetHostResolution(
                        dotnet,
                        null,
                        Failure(
                            DotNetHostFailureReason.ProcessPolicyDenied,
                            "sdk.probe_policy_denied")),
                    new ExternalVersionProbeResult(git, "2.50.1"),
                    new ExternalVersionProbeResult(ripgrep, "15.2.0"),
                    new Dictionary<string, AssemblyVersionProbeResult>()),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
        }

        private static ProbeFixture CreateSelected(
            string dotnet,
            string sdkVersion,
            DotNetHostCompatibility compatibility,
            DotNetHostFailure? hostFailure,
            string msBuildVersion,
            string roslynVersion,
            ExternalVersionProbeResult git,
            ExternalVersionProbeResult ripgrep)
        {
            var sdkPath = Path.Combine("/sdks", sdkVersion);
            var msBuildPath = Path.Combine(sdkPath, "Microsoft.Build.dll");
            var roslynPath = Path.Combine(
                sdkPath,
                "Roslyn",
                "bincore",
                "Microsoft.CodeAnalysis.dll");
            var sdk = new SelectedDotNetSdk(
                sdkVersion,
                sdkPath,
                msBuildPath,
                compatibility);
            return new ProbeFixture(
                new DotNetHostResolution(dotnet, sdk, hostFailure),
                git,
                ripgrep,
                new Dictionary<string, AssemblyVersionProbeResult>(
                    StringComparer.Ordinal)
                {
                    [msBuildPath] = new(
                        CapabilityAvailability.Present,
                        msBuildVersion),
                    [roslynPath] = new(
                        CapabilityAvailability.Present,
                        roslynVersion),
                });
        }

        private static DotNetHostFailure Failure(
            DotNetHostFailureReason reason,
            string code) => new(reason, code, "controlled correction");
    }

    private sealed class StubHostResolver(DotNetHostResolution result)
        : IDotNetHostResolver
    {
        public ValueTask<DotNetHostResolution> ResolveAsync(
            DotNetHostResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubExternalVersionProbe(
        ExternalVersionProbeResult git,
        ExternalVersionProbeResult ripgrep) : IExternalVersionProbe
    {
        public ValueTask<ExternalVersionProbeResult> ProbeAsync(
            ExternalCapability capability,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(capability switch
            {
                ExternalCapability.Git => git,
                ExternalCapability.Ripgrep => ripgrep,
                _ => throw new ArgumentOutOfRangeException(nameof(capability)),
            });
        }
    }

    private sealed class StubAssemblyVersionProbe(
        IReadOnlyDictionary<string, AssemblyVersionProbeResult> results)
        : IAssemblyVersionProbe
    {
        public AssemblyVersionProbeResult Probe(string path) => results[path];
    }
}
