using System.Diagnostics;
using System.Reflection;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Search;

namespace DotNetAxi.Cli;

internal enum CapabilityAvailability
{
    Present,
    Missing,
    Unverified,
}

internal enum CapabilityCompatibility
{
    Supported,
    Unsupported,
    Unverified,
}

internal enum CapabilityProbeOutcome
{
    TimedOut,
    Failed,
    PolicyDenied,
}

internal sealed record SelectedHostCapability(
    string? Path,
    CapabilityAvailability Availability);

internal sealed record VersionedCapability(
    string? Version,
    CapabilityAvailability Availability,
    CapabilityCompatibility? Compatibility,
    CapabilityProbeOutcome? Probe = null);

internal sealed record OptionalEngineCapability(
    string Name,
    string? Version,
    CapabilityAvailability Availability,
    CapabilityCompatibility? Compatibility);

internal sealed record CommandEngineCapability(
    string Command,
    string PreferredEngine,
    string SelectedEngine,
    string Degradation);

internal sealed record CapabilityReport(
    SelectedHostCapability SelectedHost,
    VersionedCapability Sdk,
    VersionedCapability MsBuild,
    VersionedCapability Roslyn,
    VersionedCapability Git,
    IReadOnlyList<OptionalEngineCapability> OptionalEngines,
    IReadOnlyList<CommandEngineCapability> CommandEngines);

internal interface ICapabilityReporter
{
    ValueTask<CapabilityReport> ReportAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}

internal enum ExternalCapability
{
    Git,
    Ripgrep,
}

internal sealed record ExternalVersionProbeResult(
    string? ExecutablePath,
    string? Version)
{
    public CapabilityAvailability Availability => ExecutablePath is null
        ? CapabilityAvailability.Missing
        : CapabilityAvailability.Present;
}

internal interface IExternalVersionProbe
{
    ValueTask<ExternalVersionProbeResult> ProbeAsync(
        ExternalCapability capability,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

internal sealed record AssemblyVersionProbeResult(
    CapabilityAvailability Availability,
    string? Version);

internal interface IAssemblyVersionProbe
{
    AssemblyVersionProbeResult Probe(string path);
}

internal sealed class CapabilityReporter : ICapabilityReporter
{
    private const int MinimumSupportedGitMajor = 2;
    private const int MinimumSupportedGitMinor = 11;
    private const int MaximumVerifiedGitMajor = 2;

    private readonly IDotNetHostResolver _hostResolver;
    private readonly IExternalVersionProbe _externalProbe;
    private readonly IAssemblyVersionProbe _assemblyProbe;

    internal CapabilityReporter(
        IDotNetHostResolver hostResolver,
        IExternalVersionProbe externalProbe,
        IAssemblyVersionProbe assemblyProbe)
    {
        _hostResolver = hostResolver
            ?? throw new ArgumentNullException(nameof(hostResolver));
        _externalProbe = externalProbe
            ?? throw new ArgumentNullException(nameof(externalProbe));
        _assemblyProbe = assemblyProbe
            ?? throw new ArgumentNullException(nameof(assemblyProbe));
    }

    public static ICapabilityReporter CreateDefault() => new CapabilityReporter(
        DotNetHostResolver.CreatePassive(),
        new ExternalVersionProbe(new ProcessRunner()),
        new AssemblyVersionProbe());

    public async ValueTask<CapabilityReport> ReportAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException(
                "The capability workspace root must be fully qualified.",
                nameof(workspaceRoot));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var hostTask = _hostResolver.ResolveAsync(
                new DotNetHostResolutionRequest(workspaceRoot),
                cancellationToken)
            .AsTask();
        var gitTask = _externalProbe.ProbeAsync(
                ExternalCapability.Git,
                workspaceRoot,
                cancellationToken)
            .AsTask();
        var ripgrepTask = _externalProbe.ProbeAsync(
                ExternalCapability.Ripgrep,
                workspaceRoot,
                cancellationToken)
            .AsTask();
        await Task.WhenAll(hostTask, gitTask, ripgrepTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var host = await hostTask.ConfigureAwait(false);
        var git = await gitTask.ConfigureAwait(false);
        var ripgrep = await ripgrepTask.ConfigureAwait(false);
        var sdk = CreateSdkCapability(host);
        var sdkCompatibility = sdk.Compatibility;
        var probeOutcome = HostProbeOutcome(host.Failure?.Reason);
        var msBuild = probeOutcome is null
            ? CreateSelectedAssemblyCapability(
                host.Sdk?.MsBuildPath,
                sdkCompatibility,
                host.Failure?.Reason is DotNetHostFailureReason.MsBuildUnavailable)
            : UnverifiedVersionedCapability(probeOutcome.Value);
        var roslyn = probeOutcome is null
            ? CreateSelectedAssemblyCapability(
                host.Sdk is null
                    ? null
                    : Path.Combine(
                        host.Sdk.SdkPath,
                        "Roslyn",
                        "bincore",
                        "Microsoft.CodeAnalysis.dll"),
                sdkCompatibility,
                forceMissing: false)
            : UnverifiedVersionedCapability(probeOutcome.Value);
        var ripgrepCompatibility = ClassifyRipgrep(ripgrep.Version);

        return new CapabilityReport(
            new SelectedHostCapability(
                host.ExecutablePath,
                host.ExecutablePath is null
                    ? CapabilityAvailability.Missing
                    : CapabilityAvailability.Present),
            sdk,
            msBuild,
            roslyn,
            new VersionedCapability(
                git.Version,
                git.Availability,
                CompatibilityWhenPresent(
                    git.Availability,
                    ClassifyGit(git.Version))),
            [
                new OptionalEngineCapability(
                    "rg",
                    ripgrep.Version,
                    ripgrep.Availability,
                    CompatibilityWhenPresent(
                        ripgrep.Availability,
                        ripgrepCompatibility)),
            ],
            [
                new CommandEngineCapability(
                    "search text",
                    "rg",
                    ripgrepCompatibility is CapabilityCompatibility.Supported
                        && ripgrep.Availability is CapabilityAvailability.Present
                            ? "rg"
                            : "built_in",
                    "built_in"),
            ]);
    }

    internal static CapabilityCompatibility ClassifyGit(string? version)
    {
        if (!TryParseVersion(version, out var parsed))
        {
            return CapabilityCompatibility.Unverified;
        }

        if (parsed.Major < MinimumSupportedGitMajor
            || (parsed.Major == MinimumSupportedGitMajor
                && parsed.Minor < MinimumSupportedGitMinor))
        {
            return CapabilityCompatibility.Unsupported;
        }

        return parsed.Major == MaximumVerifiedGitMajor
            ? CapabilityCompatibility.Supported
            : CapabilityCompatibility.Unverified;
    }

    internal static CapabilityCompatibility ClassifyRipgrep(string? version)
    {
        return RgTextSearchAccelerator.ClassifyVersion(version) switch
        {
            RgVersionCompatibility.Supported => CapabilityCompatibility.Supported,
            RgVersionCompatibility.Unsupported => CapabilityCompatibility.Unsupported,
            RgVersionCompatibility.Unverified => CapabilityCompatibility.Unverified,
            _ => throw new ArgumentOutOfRangeException(nameof(version)),
        };
    }

    private VersionedCapability CreateSelectedAssemblyCapability(
        string? path,
        CapabilityCompatibility? selectedSdkCompatibility,
        bool forceMissing)
    {
        if (path is null || forceMissing)
        {
            return MissingVersionedCapability();
        }

        var probe = _assemblyProbe.Probe(path);
        if (probe.Availability is CapabilityAvailability.Missing)
        {
            return MissingVersionedCapability();
        }

        return new VersionedCapability(
            probe.Version,
            CapabilityAvailability.Present,
            probe.Version is null
                ? CapabilityCompatibility.Unverified
                : selectedSdkCompatibility
                    ?? CapabilityCompatibility.Unverified);
    }

    private static VersionedCapability CreateSdkCapability(
        DotNetHostResolution host)
    {
        if (host.Sdk is not null)
        {
            var compatibility = host.Failure?.Reason is
                DotNetHostFailureReason.SdkUnsupported
                    ? CapabilityCompatibility.Unsupported
                    : host.Sdk.Compatibility switch
                    {
                        DotNetHostCompatibility.Supported =>
                            CapabilityCompatibility.Supported,
                        DotNetHostCompatibility.Unverified =>
                            CapabilityCompatibility.Unverified,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(host),
                            host.Sdk.Compatibility,
                            "The selected SDK compatibility is not defined."),
                    };
            return new VersionedCapability(
                host.Sdk.Version,
                CapabilityAvailability.Present,
                compatibility);
        }

        if (host.Failure?.Reason is DotNetHostFailureReason.SdkSelectionInvalid)
        {
            return new VersionedCapability(
                Version: null,
                CapabilityAvailability.Present,
                CapabilityCompatibility.Unverified);
        }

        var probeOutcome = HostProbeOutcome(host.Failure?.Reason);
        if (probeOutcome is not null)
        {
            return UnverifiedVersionedCapability(probeOutcome.Value);
        }

        return MissingVersionedCapability();
    }

    private static CapabilityProbeOutcome? HostProbeOutcome(
        DotNetHostFailureReason? reason) => reason switch
        {
            DotNetHostFailureReason.SdkProbeTimedOut =>
                CapabilityProbeOutcome.TimedOut,
            DotNetHostFailureReason.SdkProbeFailed =>
                CapabilityProbeOutcome.Failed,
            DotNetHostFailureReason.ProcessPolicyDenied =>
                CapabilityProbeOutcome.PolicyDenied,
            _ => null,
        };

    private static VersionedCapability UnverifiedVersionedCapability(
        CapabilityProbeOutcome probe) => new(
            Version: null,
            CapabilityAvailability.Unverified,
            CapabilityCompatibility.Unverified,
            probe);

    private static VersionedCapability MissingVersionedCapability() => new(
        Version: null,
        CapabilityAvailability.Missing,
        Compatibility: null);

    private static CapabilityCompatibility? CompatibilityWhenPresent(
        CapabilityAvailability availability,
        CapabilityCompatibility compatibility) =>
        availability is CapabilityAvailability.Present
            ? compatibility
            : null;

    private static bool TryParseVersion(
        string? value,
        out Version version)
    {
        version = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value;
        const string windowsSuffix = ".windows.";
        var suffixIndex = value.IndexOf(windowsSuffix, StringComparison.Ordinal);
        if (suffixIndex >= 0)
        {
            var suffix = value[(suffixIndex + windowsSuffix.Length)..];
            if (suffix.Length == 0
                || !suffix.All(char.IsAsciiDigit)
                || value.IndexOf(
                    windowsSuffix,
                    suffixIndex + windowsSuffix.Length,
                    StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            normalized = value[..suffixIndex];
        }

        if (!Version.TryParse(normalized, out var parsed)
            || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }
}

internal sealed class AssemblyVersionProbe : IAssemblyVersionProbe
{
    public AssemblyVersionProbeResult Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new AssemblyVersionProbeResult(
                CapabilityAvailability.Missing,
                Version: null);
        }

        try
        {
            var productVersion = FileVersionInfo
                .GetVersionInfo(path)
                .ProductVersion;
            return new AssemblyVersionProbeResult(
                CapabilityAvailability.Present,
                NormalizeProductVersion(productVersion)
                    ?? AssemblyName.GetAssemblyName(path).Version?.ToString());
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or BadImageFormatException
                or FileLoadException
                or FileNotFoundException)
        {
            return new AssemblyVersionProbeResult(
                CapabilityAvailability.Present,
                Version: null);
        }
    }

    private static string? NormalizeProductVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return version.Split('+', 2)[0];
    }
}

internal sealed class ExternalVersionProbe : IExternalVersionProbe
{
    private const int OutputLimit = 4 * 1024;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly IProcessRunner _runner;
    private readonly Func<string?> _pathValue;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _isExecutable;

    public ExternalVersionProbe(IProcessRunner runner)
        : this(
            runner,
            static () => Environment.GetEnvironmentVariable("PATH"),
            File.Exists,
            IsExecutable)
    {
    }

    internal ExternalVersionProbe(
        IProcessRunner runner,
        Func<string?> pathValue,
        Func<string, bool> fileExists,
        Func<string, bool> isExecutable)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _pathValue = pathValue ?? throw new ArgumentNullException(nameof(pathValue));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _isExecutable = isExecutable ?? throw new ArgumentNullException(nameof(isExecutable));
    }

    public async ValueTask<ExternalVersionProbeResult> ProbeAsync(
        ExternalCapability capability,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (!Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(nameof(capability));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var command = capability switch
        {
            ExternalCapability.Git => "git",
            ExternalCapability.Ripgrep => "rg",
            _ => throw new ArgumentOutOfRangeException(nameof(capability)),
        };
        var executablePath = ResolveExecutablePath(
            command,
            _pathValue(),
            OperatingSystem.IsWindows(),
            _fileExists,
            _isExecutable);
        if (executablePath is null)
        {
            return new ExternalVersionProbeResult(null, null);
        }

        if (!IsExecutableOutsideWorkspace(executablePath, workingDirectory))
        {
            return new ExternalVersionProbeResult(executablePath, null);
        }

        var result = await _runner.RunAsync(
                new ProcessRunRequest(
                    executablePath,
                    workingDirectory,
                    ["--version"],
                    ProbeEnvironment(capability),
                    new ProcessOutputLimits(OutputLimit, OutputLimit),
                    ProbeTimeout,
                    ProcessEnvironmentPolicy.Isolated),
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfCancelled(result, capability, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var version = result.Outcome is ProcessRunOutcome.Completed
            && result.Exit?.ExitCode == 0
            && !result.StandardOutput.LimitExceeded
                ? ParseVersion(capability, result.StandardOutput.Text)
                : null;
        return new ExternalVersionProbeResult(executablePath, version);
    }

    internal static string? ResolveExecutablePath(
        string command,
        string? pathValue,
        bool isWindows,
        Func<string, bool> fileExists,
        Func<string, bool> isExecutable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(isExecutable);
        if (pathValue is null)
        {
            return null;
        }

        var executableName = isWindows ? $"{command}.exe" : command;
        foreach (var rawDirectory in pathValue.Split(Path.PathSeparator))
        {
            var directory = isWindows
                ? rawDirectory.Trim().Trim('"')
                : rawDirectory;
            if (string.IsNullOrWhiteSpace(directory)
                || !Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            try
            {
                var candidate = Path.GetFullPath(
                    Path.Combine(directory, executableName));
                if (fileExists(candidate)
                    && (isWindows || isExecutable(candidate)))
                {
                    return candidate;
                }
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException
                    or IOException
                    or UnauthorizedAccessException)
            {
                // Continue to the next PATH entry.
            }
        }

        return null;
    }

    internal static string? ParseVersion(
        ExternalCapability capability,
        string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var firstLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstLine is null)
        {
            return null;
        }

        var prefix = capability switch
        {
            ExternalCapability.Git => "git version ",
            ExternalCapability.Ripgrep => "ripgrep ",
            _ => throw new ArgumentOutOfRangeException(nameof(capability)),
        };
        if (!firstLine.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return firstLine[prefix.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    private static bool IsExecutableOutsideWorkspace(
        string executablePath,
        string workspaceRoot)
    {
        try
        {
            var fullExecutablePath = Path.GetFullPath(executablePath);
            var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
            if (IsWithin(fullWorkspaceRoot, fullExecutablePath))
            {
                return false;
            }

            if (!File.Exists(fullExecutablePath))
            {
                return true;
            }

            return !IsWithin(
                ResolvePhysicalPath(fullWorkspaceRoot),
                ResolvePhysicalPath(fullExecutablePath));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Equals(".", StringComparison.Ordinal)
            || (!Path.IsPathFullyQualified(relative)
                && !relative.Equals("..", StringComparison.Ordinal)
                && !relative.StartsWith("../", StringComparison.Ordinal)
                && !relative.StartsWith("..\\", StringComparison.Ordinal));
    }

    private static string ResolvePhysicalPath(string path)
    {
        var currentPath = Path.GetFullPath(path);
        var visited = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
        {
            currentPath,
        };
        for (var pass = 0; pass < 64; pass++)
        {
            var resolved = ResolvePhysicalPathPass(currentPath, out var changed);
            if (!changed)
            {
                return resolved;
            }

            if (!visited.Add(resolved))
            {
                throw new IOException(
                    "A symbolic-link cycle prevents physical path resolution.");
            }

            currentPath = resolved;
        }

        throw new IOException(
            "The symbolic-link chain is too deep to resolve safely.");
    }

    private static string ResolvePhysicalPathPass(
        string path,
        out bool changed)
    {
        changed = false;
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException(
                "A fully qualified path requires a root.",
                nameof(path));
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            var target = entry.ResolveLinkTarget(returnFinalTarget: false);
            if (target is not null)
            {
                current = target.FullName;
                changed = true;
            }
        }

        return Path.GetFullPath(current);
    }

    private static IReadOnlyDictionary<string, string> ProbeEnvironment(
        ExternalCapability capability)
    {
        if (capability is ExternalCapability.Ripgrep)
        {
            return ChildProcessEnvironment.RipgrepDefaults;
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LC_ALL"] = "C",
        };
        if (capability is ExternalCapability.Git)
        {
            environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows()
                ? "NUL"
                : "/dev/null";
            environment["GIT_CONFIG_NOSYSTEM"] = "1";
            environment["GIT_TERMINAL_PROMPT"] = "0";
        }

        return environment;
    }

    private static void ThrowIfCancelled(
        ProcessRunResult result,
        ExternalCapability capability,
        CancellationToken cancellationToken)
    {
        if (result.Outcome is not ProcessRunOutcome.Cancelled)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(
            $"The {capability} version probe was cancelled.",
            cancellationToken);
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode execute = UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute;
            return (mode & execute) != 0;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
