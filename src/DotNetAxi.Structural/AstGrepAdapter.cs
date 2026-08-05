using DotNetAxi.Contracts;

namespace DotNetAxi.Structural;

public sealed class AstGrepAdapter
{
    private const int VersionOutputLimit = 4 * 1024;
    private static readonly IReadOnlyDictionary<string, string> ProcessEnvironment =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NO_COLOR"] = "1",
            ["TERM"] = "dumb",
        };
    private static readonly string[] DisabledIgnoreKinds =
    [
        "hidden",
        "dot",
        "exclude",
        "global",
        "parent",
        "vcs",
    ];

    private readonly IWorkspacePathTraverser _traverser;
    private readonly IProcessRunner _processRunner;
    private readonly AstGrepAdapterOptions _options;
    private readonly Func<string?> _pathValue;

    public AstGrepAdapter(
        IWorkspacePathTraverser traverser,
        IProcessRunner processRunner,
        AstGrepAdapterOptions? options = null)
        : this(
            traverser,
            processRunner,
            options ?? new AstGrepAdapterOptions(),
            static () => Environment.GetEnvironmentVariable("PATH"))
    {
    }

    internal AstGrepAdapter(
        IWorkspacePathTraverser traverser,
        IProcessRunner processRunner,
        AstGrepAdapterOptions options,
        Func<string?> pathValue)
    {
        _traverser = traverser ?? throw new ArgumentNullException(nameof(traverser));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pathValue = pathValue ?? throw new ArgumentNullException(nameof(pathValue));
    }

    public ValueTask<AstGrepCapabilityResult> CheckCapabilityAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(CancelledCapability());
        }

        var executablePath = ResolveExecutablePath();
        return CheckCapabilityAsync(
            workspaceRoot,
            executablePath,
            cancellationToken);
    }

    private async ValueTask<AstGrepCapabilityResult> CheckCapabilityAsync(
        string workspaceRoot,
        string? executablePath,
        CancellationToken cancellationToken)
    {
        if (executablePath is null)
        {
            return Capability(
                AstGrepCapabilityState.Missing,
                AstGrepIssueKind.Missing,
                "structural.ast_grep_missing",
                "Install ast-grep 0.45.x and add its ast-grep executable to PATH, or select its absolute executable path.");
        }

        if (!IsExecutableOutsideWorkspace(executablePath, workspaceRoot))
        {
            return Capability(
                AstGrepCapabilityState.Unavailable,
                AstGrepIssueKind.ExecutionFailed,
                "structural.ast_grep_executable_unsafe",
                "Install AST-grep outside the selected workspace and select that external executable.");
        }

        ProcessRunResult process;
        try
        {
            process = await _processRunner.RunAsync(
                    new ProcessRunRequest(
                        executablePath,
                        Path.GetFullPath(workspaceRoot),
                        ["--version"],
                        ProcessEnvironment,
                        new ProcessOutputLimits(
                            VersionOutputLimit,
                            VersionOutputLimit),
                        _options.VersionTimeout),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CancelledCapability();
        }

        if (process.Outcome is ProcessRunOutcome.Cancelled)
        {
            return CancelledCapability();
        }

        if (process.Outcome is ProcessRunOutcome.StartFailed
            && process.StartFailure is ProcessStartFailure.ExecutableNotFound)
        {
            return Capability(
                AstGrepCapabilityState.Missing,
                AstGrepIssueKind.Missing,
                "structural.ast_grep_missing",
                "Install ast-grep 0.45.x and select an executable path that exists on this platform.");
        }

        if (process.Outcome is ProcessRunOutcome.TimedOut)
        {
            return Capability(
                AstGrepCapabilityState.Unavailable,
                AstGrepIssueKind.TimedOut,
                "structural.ast_grep_probe_timed_out",
                "Retry the capability check or replace the AST-grep executable if its version command does not complete promptly.");
        }

        if (process.Outcome is ProcessRunOutcome.OutputLimitExceeded)
        {
            return Capability(
                AstGrepCapabilityState.Unavailable,
                AstGrepIssueKind.OutputLimitExceeded,
                "structural.ast_grep_probe_output_exceeded",
                "Use an official AST-grep executable whose version command emits a concise version line.");
        }

        if (process.Outcome is not ProcessRunOutcome.Completed
            || process.Exit?.ExitCode != 0)
        {
            return Capability(
                AstGrepCapabilityState.Unavailable,
                AstGrepIssueKind.ExecutionFailed,
                "structural.ast_grep_probe_failed",
                "Verify the selected executable is a runnable official AST-grep installation and retry.");
        }

        if (!TryParseVersion(process.StandardOutput.Text, out var version))
        {
            return Capability(
                AstGrepCapabilityState.Incompatible,
                AstGrepIssueKind.MalformedVersion,
                "structural.ast_grep_version_invalid",
                "Install a stable AST-grep 0.45.x release whose version command reports 'ast-grep MAJOR.MINOR.PATCH'.");
        }

        if (version.IsPrerelease
            || version.CompareTo(_options.MinimumVersion) < 0
            || version.CompareTo(_options.MaximumVersionExclusive) >= 0)
        {
            return new AstGrepCapabilityResult(
                AstGrepCapabilityState.Incompatible,
                version,
                new AstGrepIssue(
                    AstGrepIssueKind.IncompatibleVersion,
                    "structural.ast_grep_version_unsupported",
                    $"Install a stable AST-grep version from {_options.MinimumVersion} up to but not including {_options.MaximumVersionExclusive}."));
        }

        return new AstGrepCapabilityResult(
            AstGrepCapabilityState.Supported,
            version,
            issue: null);
    }

    public async ValueTask<AstGrepSearchResult> SearchAsync(
        AstGrepSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var executablePath = ResolveExecutablePath();
        var capability = await CheckCapabilityAsync(
                request.Traversal.WorkspaceRoot,
                executablePath,
                cancellationToken)
            .ConfigureAwait(false);
        if (!capability.IsSupported)
        {
            return new AstGrepSearchResult(
                capability.State is AstGrepCapabilityState.Cancelled
                    ? AstGrepAdapterOutcome.Cancelled
                    : AstGrepAdapterOutcome.CapabilityUnavailable,
                capability,
                issue: capability.Issue);
        }

        var selectedExecutablePath = executablePath!;

        IReadOnlyList<WorkspaceTraversalPath> traversed;
        try
        {
            traversed = _traverser.Traverse(
                request.Traversal,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failed(
                AstGrepAdapterOutcome.Cancelled,
                capability,
                AstGrepIssueKind.Cancelled,
                "structural.ast_grep_cancelled",
                "Retry the structural search when cancellation is no longer requested.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledSearch(capability);
        }

        var paths = traversed
            .Where(static path => Path.GetExtension(path.RelativePath).Equals(
                ".cs",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path.RelativePath, StringComparer.Ordinal)
            .ThenBy(static path => path.FullPath, StringComparer.Ordinal)
            .GroupBy(static path => path.RelativePath, StringComparer.Ordinal)
            .Select(static pathsWithIdentity => pathsWithIdentity.First())
            .ToArray();
        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledSearch(capability);
        }

        if (paths.Any(static path => !Path.IsPathFullyQualified(path.FullPath)))
        {
            return Failed(
                AstGrepAdapterOutcome.ExecutionFailed,
                capability,
                AstGrepIssueKind.ExecutionFailed,
                "structural.traversal_path_invalid",
                "Re-run workspace discovery so structural traversal returns fully qualified source paths.");
        }

        var candidates = new List<StructuralCandidate>();
        var workspaceRoot = Path.GetFullPath(request.Traversal.WorkspaceRoot);
        foreach (var path in paths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Failed(
                    AstGrepAdapterOutcome.Cancelled,
                    capability,
                    AstGrepIssueKind.Cancelled,
                    "structural.ast_grep_cancelled",
                    "Retry the structural search when cancellation is no longer requested.");
            }

            ProcessRunResult process;
            try
            {
                process = await _processRunner.RunAsync(
                        new ProcessRunRequest(
                            selectedExecutablePath,
                            workspaceRoot,
                            SearchArguments(request.Pattern, path.FullPath),
                            ProcessEnvironment,
                            new ProcessOutputLimits(
                                _options.StandardOutputLimit,
                                _options.StandardErrorLimit),
                            _options.SearchTimeout),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failed(
                    AstGrepAdapterOutcome.Cancelled,
                    capability,
                    AstGrepIssueKind.Cancelled,
                    "structural.ast_grep_cancelled",
                    "Retry the structural search when cancellation is no longer requested.");
            }

            var processFailure = ProcessFailure(process, capability);
            if (processFailure is not null)
            {
                return processFailure;
            }

            var exitCode = process.Exit?.ExitCode;
            if (exitCode is not (0 or 1))
            {
                return Failed(
                    AstGrepAdapterOutcome.ExecutionFailed,
                    capability,
                    AstGrepIssueKind.ExecutionFailed,
                    "structural.ast_grep_search_failed",
                    "Validate the structural pattern and retry with a supported AST-grep 0.45.x executable.");
            }

            AstGrepJsonTranslator.TranslationResult translation;
            try
            {
                translation = await AstGrepJsonTranslator.TranslateAsync(
                        process.StandardOutput.Text,
                        workspaceRoot,
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failed(
                    AstGrepAdapterOutcome.Cancelled,
                    capability,
                    AstGrepIssueKind.Cancelled,
                    "structural.ast_grep_cancelled",
                    "Retry the structural search when cancellation is no longer requested.");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledSearch(capability);
            }

            if (!translation.IsValid
                || (exitCode == 1 && translation.Candidates.Count != 0)
                || (exitCode == 0 && translation.Candidates.Count == 0))
            {
                return Failed(
                    AstGrepAdapterOutcome.MalformedOutput,
                    capability,
                    AstGrepIssueKind.MalformedOutput,
                    "structural.ast_grep_output_invalid",
                    "Retry with a supported AST-grep 0.45.x executable and an unchanged UTF-8 source file.");
            }

            candidates.AddRange(translation.Candidates);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledSearch(capability);
        }

        var ordered = candidates
            .OrderBy(static candidate => candidate.Range.Start.Path, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Range.Start.Line)
            .ThenBy(static candidate => candidate.Range.Start.Column)
            .ThenBy(static candidate => candidate.Range.End.Line)
            .ThenBy(static candidate => candidate.Range.End.Column)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledSearch(capability);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in ordered)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledSearch(capability);
            }

            if (!ids.Add(candidate.Id))
            {
                return Failed(
                    AstGrepAdapterOutcome.MalformedOutput,
                    capability,
                    AstGrepIssueKind.MalformedOutput,
                    "structural.ast_grep_output_invalid",
                    "Retry with a supported AST-grep executable that emits each structural candidate once.");
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledSearch(capability);
        }

        return new AstGrepSearchResult(
            AstGrepAdapterOutcome.Succeeded,
            capability,
            ordered);
    }

    internal static IReadOnlyList<string> SearchArguments(
        string pattern,
        string path)
    {
        var arguments = new List<string>
        {
            "run",
            "--pattern",
            pattern,
            "--lang",
            "csharp",
            "--json=compact",
            "--color",
            "never",
        };
        foreach (var ignoreKind in DisabledIgnoreKinds)
        {
            arguments.Add("--no-ignore");
            arguments.Add(ignoreKind);
        }

        arguments.Add("--");
        arguments.Add(path);
        return arguments.AsReadOnly();
    }

    internal static bool TryParseVersion(
        string output,
        out AstGrepVersion version)
    {
        version = null!;
        var value = output.Trim();
        const string prefix = "ast-grep ";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var versionText = value[prefix.Length..];
        var prereleaseSeparator = versionText.IndexOf('-');
        var buildSeparator = versionText.IndexOf('+');
        var suffix = prereleaseSeparator < 0
            ? buildSeparator
            : buildSeparator < 0
                ? prereleaseSeparator
                : Math.Min(prereleaseSeparator, buildSeparator);
        var stableText = suffix < 0 ? versionText : versionText[..suffix];
        var components = stableText.Split('.');
        if (components.Length != 3
            || !TryParseVersionComponent(components[0], out var major)
            || !TryParseVersionComponent(components[1], out var minor)
            || !TryParseVersionComponent(components[2], out var patch)
            || components.Any(static component => component.Length == 0)
            || (suffix >= 0 && suffix == versionText.Length - 1))
        {
            return false;
        }

        version = new AstGrepVersion(
            major,
            minor,
            patch,
            isPrerelease: prereleaseSeparator >= 0
                && (buildSeparator < 0 || prereleaseSeparator < buildSeparator));
        return true;
    }

    private static bool TryParseVersionComponent(string value, out int component)
    {
        component = 0;
        return value.Length > 0
            && (value.Length == 1 || value[0] != '0')
            && int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out component);
    }

    private AstGrepSearchResult? ProcessFailure(
        ProcessRunResult process,
        AstGrepCapabilityResult capability) =>
        process.Outcome switch
        {
            ProcessRunOutcome.Cancelled => Failed(
                AstGrepAdapterOutcome.Cancelled,
                capability,
                AstGrepIssueKind.Cancelled,
                "structural.ast_grep_cancelled",
                "Retry the structural search when cancellation is no longer requested."),
            ProcessRunOutcome.TimedOut => Failed(
                AstGrepAdapterOutcome.TimedOut,
                capability,
                AstGrepIssueKind.TimedOut,
                "structural.ast_grep_timed_out",
                "Narrow the shared traversal scope or retry with a responsive supported AST-grep executable."),
            ProcessRunOutcome.OutputLimitExceeded => Failed(
                AstGrepAdapterOutcome.OutputLimitExceeded,
                capability,
                AstGrepIssueKind.OutputLimitExceeded,
                "structural.ast_grep_output_exceeded",
                "Narrow the structural search scope or raise the configured bounded adapter output limit."),
            ProcessRunOutcome.Completed => null,
            _ => Failed(
                AstGrepAdapterOutcome.ExecutionFailed,
                capability,
                AstGrepIssueKind.ExecutionFailed,
                "structural.ast_grep_search_failed",
                "Verify the selected AST-grep executable is runnable and retry the structural search."),
        };

    private string? ResolveExecutablePath()
    {
        if (_options.ExecutablePath is not null)
        {
            return _options.ExecutablePath;
        }

        return ResolveExecutableOnPath(_pathValue());
    }

    internal static string? ResolveExecutableOnPath(
        string? pathValue,
        bool? isWindows = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? isExecutable = null)
    {
        if (pathValue is null)
        {
            return null;
        }

        var windows = isWindows ?? OperatingSystem.IsWindows();
        var exists = fileExists ?? File.Exists;
        var executable = isExecutable ?? IsExecutableFile;
        foreach (var rawDirectory in pathValue.Split(Path.PathSeparator))
        {
            var directory = windows
                ? rawDirectory.Trim().Trim('"')
                : rawDirectory;
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            try
            {
                if (!Path.IsPathFullyQualified(directory))
                {
                    continue;
                }

                var candidate = Path.GetFullPath(Path.Combine(
                    directory,
                    windows ? "ast-grep.exe" : "ast-grep"));
                if (exists(candidate) && (windows || executable(candidate)))
                {
                    return candidate;
                }

                if (!windows
                    || !exists(Path.GetFullPath(Path.Combine(directory, "ast-grep.cmd"))))
                {
                    continue;
                }

                if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))
                        .Equals(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    var localPackageExecutable = Path.GetFullPath(Path.Combine(
                        directory,
                        "..",
                        "@ast-grep",
                        "cli",
                        "ast-grep.exe"));
                    if (exists(localPackageExecutable))
                    {
                        return localPackageExecutable;
                    }
                }

                var globalPackageExecutable = Path.GetFullPath(Path.Combine(
                    directory,
                    "node_modules",
                    "@ast-grep",
                    "cli",
                    "ast-grep.exe"));
                if (exists(globalPackageExecutable))
                {
                    return globalPackageExecutable;
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
                throw new IOException("A symbolic-link cycle prevents physical path resolution.");
            }

            currentPath = resolved;
        }

        throw new IOException("The symbolic-link chain is too deep to resolve safely.");
    }

    private static string ResolvePhysicalPathPass(
        string path,
        out bool changed)
    {
        changed = false;
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("A fully qualified path requires a root.", nameof(path));
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

    private static bool IsExecutableFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
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

    private static AstGrepCapabilityResult CancelledCapability() => Capability(
        AstGrepCapabilityState.Cancelled,
        AstGrepIssueKind.Cancelled,
        "structural.ast_grep_cancelled",
        "Retry the AST-grep capability check when cancellation is no longer requested.");

    private static AstGrepSearchResult CancelledSearch(
        AstGrepCapabilityResult capability) =>
        Failed(
            AstGrepAdapterOutcome.Cancelled,
            capability,
            AstGrepIssueKind.Cancelled,
            "structural.ast_grep_cancelled",
            "Retry the structural search when cancellation is no longer requested.");

    private static AstGrepCapabilityResult Capability(
        AstGrepCapabilityState state,
        AstGrepIssueKind issueKind,
        string code,
        string correction) =>
        new(state, version: null, new AstGrepIssue(issueKind, code, correction));

    private static AstGrepSearchResult Failed(
        AstGrepAdapterOutcome outcome,
        AstGrepCapabilityResult capability,
        AstGrepIssueKind issueKind,
        string code,
        string correction) =>
        new(
            outcome,
            capability,
            issue: new AstGrepIssue(issueKind, code, correction));
}
