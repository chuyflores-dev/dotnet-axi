using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public enum RgVersionCompatibility
{
    Supported,
    Unsupported,
    Unverified,
}

/// <summary>
/// Uses a compatible ripgrep installation to prefilter files for supported
/// literal text searches. The built-in engine remains responsible for reading
/// files, validating encodings, matching candidates, and producing results.
/// </summary>
public sealed class RgTextSearchAccelerator
{
    private const int MinimumSupportedMajorVersion = 13;
    private const int MaximumSupportedMajorVersion = 15;
    private const int MaximumBatchArgumentCharacters = 24 * 1024;
    private const int MaximumBatchPaths = 128;
    private const int OutputLimit = 256 * 1024;
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessRunner _processRunner;
    private readonly string? _executablePath;

    public RgTextSearchAccelerator(IProcessRunner processRunner)
        : this(processRunner, ResolveExecutablePath())
    {
    }

    internal RgTextSearchAccelerator(
        IProcessRunner processRunner,
        string? executablePath)
    {
        _processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
        _executablePath = executablePath;
    }

    internal async Task<IReadOnlySet<string>?> FindCandidatePathsAsync(
        TextSearchRequest request,
        ITextSearchMatcher matcher,
        IReadOnlyList<WorkspaceTraversalPath> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();

        if (_executablePath is null
            || !IsSupportedQuery(request, matcher)
            || paths.Count == 0
            || !IsExecutableOutsideWorkspace(
                _executablePath,
                request.Traversal.WorkspaceRoot)
            || !await IsCompatibleAsync(request.Traversal.WorkspaceRoot, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var knownPaths = new HashSet<string>(comparer);
        var orderedPaths = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(path.FullPath))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(path.FullPath);
            if (knownPaths.Add(fullPath))
            {
                orderedPaths.Add(fullPath);
            }
        }

        var candidates = new HashSet<string>(comparer);
        foreach (var batch in CreateBatches(orderedPaths))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (batch is null)
            {
                return null;
            }

            var result = await RunSearchAsync(
                    request.Traversal.WorkspaceRoot,
                    request.Query,
                    batch,
                    cancellationToken)
                .ConfigureAwait(false);
            ThrowIfCancelled(result, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadCandidates(
                    result,
                    new HashSet<string>(batch, comparer),
                    candidates))
            {
                return null;
            }
        }

        return candidates;
    }

    internal static string? ResolveExecutablePath(
        string? pathValue = null,
        bool? isWindows = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? isExecutable = null)
    {
        pathValue ??= Environment.GetEnvironmentVariable("PATH");
        if (pathValue is null)
        {
            return null;
        }

        var windows = isWindows ?? OperatingSystem.IsWindows();
        var exists = fileExists ?? File.Exists;
        var executable = isExecutable ?? IsExecutableFile;
        var executableName = windows ? "rg.exe" : "rg";
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

                var candidate = Path.GetFullPath(
                    Path.Combine(directory, executableName));
                if (exists(candidate) && (windows || executable(candidate)))
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

    private static bool IsSupportedQuery(
        TextSearchRequest request,
        ITextSearchMatcher matcher) =>
        !matcher.IsRegularExpression
        && request.CaseSensitive
        && request.Query.All(static character => character is >= ' ' and <= '~');

    private async Task<bool> IsCompatibleAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
                Request(
                    workingDirectory,
                    ["--version"],
                    DetectionTimeout,
                    standardOutputLimit: 4 * 1024),
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfCancelled(result, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Outcome is not ProcessRunOutcome.Completed
            || result.Exit?.ExitCode != 0
            || result.StandardOutput.LimitExceeded)
        {
            return false;
        }

        var firstLine = result.StandardOutput.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstLine is null
            || !firstLine.StartsWith("ripgrep ", StringComparison.Ordinal))
        {
            return false;
        }

        var versionText = firstLine["ripgrep ".Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return ClassifyVersion(versionText) is RgVersionCompatibility.Supported;
    }

    public static RgVersionCompatibility ClassifyVersion(string? versionText)
    {
        if (!Version.TryParse(versionText, out var version)
            || version is null)
        {
            return RgVersionCompatibility.Unverified;
        }

        if (version.Major < MinimumSupportedMajorVersion)
        {
            return RgVersionCompatibility.Unsupported;
        }

        return version.Major <= MaximumSupportedMajorVersion
            ? RgVersionCompatibility.Supported
            : RgVersionCompatibility.Unverified;
    }

    private async Task<ProcessRunResult> RunSearchAsync(
        string workingDirectory,
        string query,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--no-config",
            "--files-with-matches",
            "--null",
            "--fixed-strings",
            "--case-sensitive",
            "--encoding",
            "auto",
            "--no-messages",
            "--",
            query,
        };
        arguments.AddRange(paths);
        return await _processRunner.RunAsync(
                Request(
                    workingDirectory,
                    arguments,
                    SearchTimeout,
                    OutputLimit),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private ProcessRunRequest Request(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        int standardOutputLimit) =>
        new(
            _executablePath!,
            workingDirectory,
            arguments,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new ProcessOutputLimits(standardOutputLimit, OutputLimit),
            timeout);

    private static IEnumerable<IReadOnlyList<string>?> CreateBatches(
        IEnumerable<string> paths)
    {
        var batch = new List<string>();
        var characters = 0;
        foreach (var path in paths)
        {
            if (path.Length > MaximumBatchArgumentCharacters)
            {
                yield return null;
                yield break;
            }

            if (batch.Count == MaximumBatchPaths
                || characters + path.Length > MaximumBatchArgumentCharacters)
            {
                yield return batch.ToArray();
                batch.Clear();
                characters = 0;
            }

            batch.Add(path);
            characters += path.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch.ToArray();
        }
    }

    private static bool TryReadCandidates(
        ProcessRunResult result,
        IReadOnlySet<string> batchPaths,
        ISet<string> candidates)
    {
        if (result.Outcome is not ProcessRunOutcome.Completed
            || result.StandardOutput.LimitExceeded
            || result.Exit?.ExitCode is not (0 or 1))
        {
            return false;
        }

        var output = result.StandardOutput.Text;
        if (result.Exit.ExitCode == 1)
        {
            return output.Length == 0;
        }

        if (output.Length == 0 || output[^1] != '\0')
        {
            return false;
        }

        var observedCurrentBatchPath = false;
        foreach (var rawPath in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            string path;
            try
            {
                if (!Path.IsPathFullyQualified(rawPath))
                {
                    return false;
                }

                path = Path.GetFullPath(rawPath);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                      or NotSupportedException
                      or PathTooLongException)
            {
                return false;
            }

            if (!batchPaths.Contains(path))
            {
                return false;
            }

            candidates.Add(path);
            observedCurrentBatchPath = true;
        }

        return observedCurrentBatchPath;
    }

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
            "The ripgrep text-search accelerator was cancelled.",
            cancellationToken);
    }
}
