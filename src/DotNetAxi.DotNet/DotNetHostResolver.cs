using DotNetAxi.Contracts;

namespace DotNetAxi.DotNet;

public sealed class DotNetHostResolver : IDotNetHostResolver
{
    private const int OutputLimit = 64 * 1024;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly IProcessRunner _runner;
    private readonly Func<string?> _pathValue;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _isExecutable;

    public DotNetHostResolver()
        : this(
            new ProcessRunner(),
            static () => Environment.GetEnvironmentVariable("PATH"),
            File.Exists,
            IsExecutable)
    {
    }

    public DotNetHostResolver(IProcessRunner runner)
        : this(
            runner,
            static () => Environment.GetEnvironmentVariable("PATH"),
            File.Exists,
            IsExecutable)
    {
    }

    internal DotNetHostResolver(
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

    public async ValueTask<DotNetHostResolution> ResolveAsync(
        DotNetHostResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = request.ExplicitHostPath is null
            ? ResolvePathHost(_pathValue(), request.WorkspaceRoot)
            : ResolveExplicitHost(
                request.ExplicitHostPath,
                request.WorkspaceRoot);
        if (executablePath is null)
        {
            return Failed(
                request.ExplicitHostPath is null
                    ? DotNetHostFailureReason.HostNotFound
                    : DotNetHostFailureReason.HostUnsupported,
                request.ExplicitHostPath is null
                    ? "dotnet.host_not_found"
                    : "dotnet.host_unsupported",
                request.ExplicitHostPath is null
                    ? "Install an official .NET SDK and add its dotnet executable to PATH, or select its absolute host path."
                    : "Select an executable official dotnet host path named dotnet (or dotnet.exe on Windows).");
        }

        var sdkInfo = await RunAsync(
                executablePath,
                request.WorkspaceRoot,
                ["--info"],
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfCancelled(sdkInfo, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (sdkInfo.Outcome is ProcessRunOutcome.StartFailed
            && sdkInfo.StartFailure is ProcessStartFailure.PolicyDenied)
        {
            return Failed(
                DotNetHostFailureReason.ProcessPolicyDenied,
                "sdk.probe_policy_denied",
                "Continue with built-in passive capabilities; do not retry the SDK probe from this passive operation.",
                executablePath);
        }

        if (sdkInfo.Outcome is ProcessRunOutcome.TimedOut)
        {
            return Failed(
                DotNetHostFailureReason.SdkProbeTimedOut,
                "sdk.probe_timed_out",
                "Retry the passive SDK probe, or select another trusted dotnet host.",
                executablePath);
        }

        if (sdkInfo.Outcome is not ProcessRunOutcome.Completed)
        {
            return Failed(
                DotNetHostFailureReason.SdkProbeFailed,
                "sdk.probe_failed",
                "Retry the passive SDK probe, or select another trusted dotnet host.",
                executablePath);
        }

        if (sdkInfo.Exit?.ExitCode != 0)
        {
            return Failed(
                DotNetHostFailureReason.SdkUnavailable,
                "sdk.selection_failed",
                "Install an SDK accepted by this workspace's global.json, or select a dotnet host that provides it.",
                executablePath);
        }

        var selected = ParseSelectedSdkInfo(sdkInfo.StandardOutput.Text);
        if (selected is null)
        {
            return Failed(
                DotNetHostFailureReason.SdkSelectionInvalid,
                "sdk.context_invalid",
                "Select an official dotnet host that reports a valid selected SDK version and absolute Base Path.",
                executablePath);
        }

        TryParseSdkVersion(selected.Version, out var sdkVersion);
        var msBuildPath = Path.Combine(selected.SdkPath, "Microsoft.Build.dll");
        var sdk = new SelectedDotNetSdk(
            selected.Version,
            selected.SdkPath,
            msBuildPath,
            ClassifyCompatibility(sdkVersion));
        var major = sdkVersion.Major;
        if (major < 8)
        {
            return Failed(
                DotNetHostFailureReason.SdkUnsupported,
                "sdk.selected_unsupported",
                "Select a dotnet host with an installed .NET 8, .NET 9, or .NET 10 SDK accepted by global.json.",
                executablePath,
                sdk);
        }

        if (!_fileExists(msBuildPath))
        {
            return Failed(
                DotNetHostFailureReason.MsBuildUnavailable,
                "msbuild.selected_instance_missing",
                "Repair or reinstall the selected .NET SDK so its MSBuild assemblies are available.",
                executablePath,
                sdk);
        }

        return new DotNetHostResolution(
            executablePath,
            sdk,
            null);
    }

    internal static string? ResolveHostPath(
        string? pathValue,
        bool isWindows,
        Func<string, bool> fileExists,
        Func<string, bool> isExecutable,
        string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(isExecutable);
        if (pathValue is null)
        {
            return null;
        }

        var executableName = isWindows ? "dotnet.exe" : "dotnet";
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
                var candidate = Path.GetFullPath(Path.Combine(directory, executableName));
                if (fileExists(candidate)
                    && (isWindows || isExecutable(candidate))
                    && (workspaceRoot is null
                        || IsExecutableOutsideWorkspace(
                            candidate,
                            workspaceRoot,
                            fileExists)))
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

    internal static SelectedSdkInfo? ParseSelectedSdkInfo(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string? version = null;
        string? basePath = null;
        var section = DotNetInfoSection.None;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Equals(".NET SDK:", StringComparison.Ordinal))
            {
                section = DotNetInfoSection.Sdk;
                continue;
            }

            if (trimmed.Equals("Runtime Environment:", StringComparison.Ordinal))
            {
                section = DotNetInfoSection.RuntimeEnvironment;
                continue;
            }

            if (trimmed.Length == 0)
            {
                section = DotNetInfoSection.None;
                continue;
            }

            if (section is DotNetInfoSection.Sdk
                && TryReadInfoValue(trimmed, "Version:", out var selectedVersion))
            {
                if (version is not null)
                {
                    return null;
                }

                version = selectedVersion;
            }
            else if (section is DotNetInfoSection.RuntimeEnvironment
                     && TryReadInfoValue(trimmed, "Base Path:", out var selectedBasePath))
            {
                if (basePath is not null)
                {
                    return null;
                }

                basePath = selectedBasePath;
            }
        }

        if (version is null
            || !TryParseSdkVersion(version, out _)
            || string.IsNullOrWhiteSpace(basePath)
            || !Path.IsPathFullyQualified(basePath))
        {
            return null;
        }

        try
        {
            return new SelectedSdkInfo(
                version,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath)));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or NotSupportedException
                  or PathTooLongException)
        {
            return null;
        }
    }

    internal static DotNetHostCompatibility ClassifyCompatibility(string version) =>
        TryParseSdkVersion(version, out var parsed)
            ? ClassifyCompatibility(parsed)
            : DotNetHostCompatibility.Unverified;

    private async ValueTask<ProcessRunResult> RunAsync(
        string executablePath,
        string workspaceRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        await _runner.RunAsync(
                new ProcessRunRequest(
                    executablePath,
                    workspaceRoot,
                    arguments,
                    ChildProcessEnvironment.DotNetDefaults,
                    new ProcessOutputLimits(OutputLimit, OutputLimit),
                    CommandTimeout,
                    ProcessEnvironmentPolicy.InheritParent),
                cancellationToken)
            .ConfigureAwait(false);

    private string? ResolvePathHost(
        string? pathValue,
        string workspaceRoot) => ResolveHostPath(
        pathValue,
        OperatingSystem.IsWindows(),
        _fileExists,
        _isExecutable,
        workspaceRoot);

    private string? ResolveExplicitHost(
        string explicitHostPath,
        string workspaceRoot)
    {
        var expectedName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        return Path.GetFileName(explicitHostPath).Equals(
                   expectedName,
                   OperatingSystem.IsWindows()
                       ? StringComparison.OrdinalIgnoreCase
                       : StringComparison.Ordinal)
               && _fileExists(explicitHostPath)
               && (OperatingSystem.IsWindows() || _isExecutable(explicitHostPath))
               && IsExecutableOutsideWorkspace(
                   explicitHostPath,
                   workspaceRoot,
                   _fileExists)
            ? explicitHostPath
            : null;
    }

    private static bool IsExecutableOutsideWorkspace(
        string executablePath,
        string workspaceRoot,
        Func<string, bool> fileExists)
    {
        try
        {
            var fullExecutablePath = Path.GetFullPath(executablePath);
            var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
            if (IsWithin(fullWorkspaceRoot, fullExecutablePath))
            {
                return false;
            }

            if (!File.Exists(fullExecutablePath) && fileExists(fullExecutablePath))
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

    private static DotNetHostResolution Failed(
        DotNetHostFailureReason reason,
        string code,
        string correction,
        string? executablePath = null,
        SelectedDotNetSdk? sdk = null) =>
        new(executablePath, sdk, new DotNetHostFailure(reason, code, correction));

    private static void ThrowIfCancelled(
        ProcessRunResult result,
        CancellationToken cancellationToken)
    {
        if (result.Outcome is not ProcessRunOutcome.Cancelled)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(
            "The dotnet host resolution was cancelled.",
            cancellationToken);
    }

    private static bool TryReadInfoValue(
        string line,
        string label,
        out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(label, StringComparison.Ordinal))
        {
            return false;
        }

        value = line[label.Length..].Trim();
        return value.Length > 0;
    }

    private static bool TryParseSdkVersion(
        string version,
        out ParsedSdkVersion parsed)
    {
        parsed = default;
        var prereleaseSeparator = version.IndexOf('-');
        var stablePart = prereleaseSeparator < 0
            ? version
            : version[..prereleaseSeparator];
        var components = stablePart.Split('.');
        if (components.Length != 3
            || !int.TryParse(components[0], out var major)
            || !int.TryParse(components[1], out var minor)
            || !int.TryParse(components[2], out var patch)
            || major < 1
            || minor < 0
            || patch < 0
            || (prereleaseSeparator >= 0
                && prereleaseSeparator == version.Length - 1))
        {
            return false;
        }

        parsed = new ParsedSdkVersion(
            major,
            minor,
            patch,
            prereleaseSeparator >= 0);
        return true;
    }

    private static DotNetHostCompatibility ClassifyCompatibility(
        ParsedSdkVersion version) =>
        version is
        {
            Major: 10,
            Minor: 0,
            Patch: >= 300 and <= 399,
            IsPrerelease: false,
        }
            ? DotNetHostCompatibility.Supported
            : DotNetHostCompatibility.Unverified;

    private static bool IsExecutable(string path) => OperatingSystem.IsWindows()
        || PosixProcessAuthority.CanExecute(path);

    internal sealed record SelectedSdkInfo(string Version, string SdkPath);

    private readonly record struct ParsedSdkVersion(
        int Major,
        int Minor,
        int Patch,
        bool IsPrerelease);

    private enum DotNetInfoSection
    {
        None,
        Sdk,
        RuntimeEnvironment,
    }
}
