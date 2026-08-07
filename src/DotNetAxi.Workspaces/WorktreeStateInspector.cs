using DotNetAxi.Contracts;

namespace DotNetAxi.Workspaces;

public sealed class WorktreeStateInspector
{
    private const int ProcessOutputLimit = 16 * 1024 * 1024;
    private static readonly TimeSpan DefaultProcessTimeout =
        TimeSpan.FromSeconds(30);

    private readonly string _gitExecutable;
    private readonly TimeSpan _processTimeout;
    private readonly IProcessRunner _processRunner;
    private readonly bool _resolveExecutable;
    private readonly bool _enforcePassiveBoundary;

    public WorktreeStateInspector(
        IProcessRunner processRunner,
        string gitExecutable = "git",
        TimeSpan? processTimeout = null)
        : this(
            gitExecutable,
            processTimeout,
            processRunner,
            resolveExecutable: true,
            enforcePassiveBoundary: false)
    {
    }

    public static WorktreeStateInspector CreatePassive(
        IProcessRunner processRunner) => new(
        "git",
        processTimeout: null,
        processRunner,
        resolveExecutable: true,
        enforcePassiveBoundary: true);

    internal WorktreeStateInspector(
        string gitExecutable,
        TimeSpan? processTimeout,
        IProcessRunner processRunner)
        : this(
            gitExecutable,
            processTimeout,
            processRunner,
            resolveExecutable: false,
            enforcePassiveBoundary: false)
    {
    }

    internal WorktreeStateInspector(
        string gitExecutable,
        TimeSpan? processTimeout,
        IProcessRunner processRunner,
        bool resolveExecutable,
        bool enforcePassiveBoundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        ArgumentNullException.ThrowIfNull(processRunner);
        if (processTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTimeout));
        }

        _gitExecutable = gitExecutable;
        _processTimeout = processTimeout ?? DefaultProcessTimeout;
        _processRunner = processRunner;
        _resolveExecutable = resolveExecutable;
        _enforcePassiveBoundary = enforcePassiveBoundary;
    }

    public async Task<WorktreeStateResult> InspectAsync(
        WorkspaceDiscoveryResult workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.WorkspaceKind != WorkspaceKind.Git)
        {
            return new WorktreeStateResult(
                WorktreeInspectionOutcome.NotGit,
                state: null,
                failure: null);
        }

        var filters = await RunGitAsync(
            workspace.RootPath,
            [
                "config",
                "--includes",
                "--null",
                "--name-only",
                "--get-regexp",
                @"^filter\..*\.(clean|process)$",
            ],
            cancellationToken,
            additionalSuccessExitCode: 1);
        if (filters.Failure is not null)
        {
            return Failed(filters.Failure);
        }

        if (filters.ExitCode == 0)
        {
            return Failed(
                new WorktreeInspectionFailure(
                    WorktreeInspectionFailureKind
                        .GitFilterCommandsConfigured));
        }

        if (filters.StandardOutput!.Length != 0)
        {
            return Failed(
                new WorktreeInspectionFailure(
                    WorktreeInspectionFailureKind.InvalidGitOutput));
        }

        var status = await RunGitAsync(
            workspace.RootPath,
            [
                "status",
                "--porcelain=v2",
                "--branch",
                "-z",
                "--untracked-files=all",
                "--renames",
                "--ignore-submodules=dirty",
            ],
            cancellationToken);
        if (status.Failure is not null)
        {
            return Failed(status.Failure);
        }

        var tracked = await RunGitAsync(
            workspace.RootPath,
            ["ls-files", "--cached", "-z"],
            cancellationToken);
        if (tracked.Failure is not null)
        {
            return Failed(tracked.Failure);
        }

        try
        {
            var parsedStatus = ParseStatus(status.StandardOutput!);
            var trackedPaths = ParseTrackedPaths(tracked.StandardOutput!);
            return new WorktreeStateResult(
                WorktreeInspectionOutcome.Available,
                new GitWorktreeState(
                    parsedStatus.Head,
                    trackedPaths,
                    parsedStatus.Entries),
                failure: null);
        }
        catch (InvalidGitOutputException)
        {
            return Failed(
                new WorktreeInspectionFailure(
                    WorktreeInspectionFailureKind.InvalidGitOutput));
        }
    }

    private static WorktreeStateResult Failed(
        WorktreeInspectionFailure failure) =>
        new(
            failure.Kind ==
                WorktreeInspectionFailureKind.GitExecutableNotFound
                ? WorktreeInspectionOutcome.GitUnavailable
                : WorktreeInspectionOutcome.Failed,
            state: null,
            failure);

    internal async Task<GitProcessResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        int? additionalSuccessExitCode = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_enforcePassiveBoundary
            && !SafePassiveGitBoundary.IsAllowedArguments(arguments))
        {
            return GitProcessResult.PolicyDenied();
        }

        PassiveGitExecutableResolution executable;
        if (_resolveExecutable)
        {
            executable = !_enforcePassiveBoundary
                && Path.IsPathFullyQualified(_gitExecutable)
                    ? new PassiveGitExecutableResolution(
                        PassiveGitExecutableTrust.Trusted,
                        Path.GetFullPath(_gitExecutable))
                    : SafePassiveGitBoundary.ResolveExecutable(
                        _gitExecutable,
                        workingDirectory);
        }
        else
        {
            var path = Path.IsPathFullyQualified(_gitExecutable)
                ? Path.GetFullPath(_gitExecutable)
                : Path.GetFullPath(
                    Path.Combine(workingDirectory, _gitExecutable));
            executable = new PassiveGitExecutableResolution(
                PassiveGitExecutableTrust.Trusted,
                path);
        }

        if (executable.Trust is PassiveGitExecutableTrust.WorkspaceControlled)
        {
            return GitProcessResult.PolicyDenied();
        }

        if (executable.Trust is PassiveGitExecutableTrust.Missing)
        {
            return GitProcessResult.ExecutableNotFound();
        }

        var processArguments = new List<string>(arguments.Count + 12)
        {
            "--no-optional-locks",
            "--no-pager",
            "--literal-pathspecs",
            $"--work-tree={workingDirectory}",
            "-c",
            "core.fsmonitor=false",
            "-c",
            "core.untrackedCache=false",
            "-c",
            "submodule.recurse=false",
        };
        processArguments.AddRange(arguments);
        var environment = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["GIT_ATTR_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] =
                OperatingSystem.IsWindows() ? "NUL" : "/dev/null",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_NO_LAZY_FETCH"] = "1",
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
            ["LC_ALL"] = "C",
        };
        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                executable.Path!,
                workingDirectory,
                processArguments,
                environment,
                new ProcessOutputLimits(
                    ProcessOutputLimit,
                    ProcessOutputLimit),
                _processTimeout,
                ProcessEnvironmentPolicy.Isolated),
            cancellationToken);
        if (result.Outcome is ProcessRunOutcome.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(
                "Git worktree inspection was cancelled.",
                cancellationToken);
        }

        if (result.Outcome is ProcessRunOutcome.StartFailed)
        {
            return result.StartFailure switch
            {
                ProcessStartFailure.ExecutableNotFound =>
                    GitProcessResult.ExecutableNotFound(),
                ProcessStartFailure.PolicyDenied =>
                    GitProcessResult.PolicyDenied(),
                _ => GitProcessResult.StartFailure(),
            };
        }

        if (result.Outcome is ProcessRunOutcome.TimedOut)
        {
            return GitProcessResult.TimedOut();
        }

        if (result.Outcome is not ProcessRunOutcome.Completed
            || result.Lifecycle is not ProcessLifecycle.Completed
            || result.Exit?.ExitCode is not { } exitCode
            || result.StandardOutput.Text.Contains(
                '\uFFFD',
                StringComparison.Ordinal))
        {
            return GitProcessResult.InvalidOutput();
        }

        return exitCode == 0 || exitCode == additionalSuccessExitCode
            ? GitProcessResult.Success(result.StandardOutput.Text, exitCode)
            : GitProcessResult.ProcessFailure(exitCode);
    }

    private static ParsedStatus ParseStatus(string output)
    {
        RequireNullTerminated(output);
        var records = output.Split('\0');
        string? branchHead = null;
        string? branchOid = null;
        var entries = new List<GitWorktreeEntry>();
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (record.Length == 0)
            {
                continue;
            }

            if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                if (branchHead is not null)
                {
                    throw new InvalidGitOutputException();
                }

                branchHead = ReadHeaderValue(record, "# branch.head ");
                continue;
            }

            if (record.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                if (branchOid is not null)
                {
                    throw new InvalidGitOutputException();
                }

                branchOid = ReadHeaderValue(record, "# branch.oid ");
                continue;
            }

            if (record.StartsWith(
                    "# branch.upstream ",
                    StringComparison.Ordinal)
                || record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                continue;
            }

            if (record.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            switch (record[0])
            {
                case '1':
                    entries.Add(ParseOrdinaryEntry(record));
                    break;
                case '2':
                    if (++index >= records.Length
                        || records[index].Length == 0)
                    {
                        throw new InvalidGitOutputException();
                    }

                    entries.Add(ParseRenameEntry(record, records[index]));
                    break;
                case 'u':
                    entries.Add(ParseConflictEntry(record));
                    break;
                case '?':
                    entries.Add(ParseUntrackedEntry(record));
                    break;
                default:
                    throw new InvalidGitOutputException();
            }
        }

        var head = ParseHead(branchHead, branchOid);
        return new ParsedStatus(
            head,
            entries
                .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
                .ThenBy(
                    static entry => entry.OriginalPath,
                    StringComparer.Ordinal)
                .ToArray());
    }

    private static string[] ParseTrackedPaths(string output)
    {
        RequireNullTerminated(output);
        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static GitHeadState ParseHead(
        string? branchHead,
        string? branchOid)
    {
        if (branchHead is null || branchOid is null)
        {
            throw new InvalidGitOutputException();
        }

        if (branchOid.Equals("(initial)", StringComparison.Ordinal))
        {
            if (branchHead.Equals("(detached)", StringComparison.Ordinal))
            {
                throw new InvalidGitOutputException();
            }

            return new GitHeadState(
                GitHeadKind.Unborn,
                branchHead,
                CommitId: null);
        }

        if (!IsObjectId(branchOid))
        {
            throw new InvalidGitOutputException();
        }

        return branchHead.Equals("(detached)", StringComparison.Ordinal)
            ? new GitHeadState(
                GitHeadKind.Detached,
                BranchName: null,
                branchOid)
            : new GitHeadState(
                GitHeadKind.Branch,
                branchHead,
                branchOid);
    }

    private static GitWorktreeEntry ParseOrdinaryEntry(string record)
    {
        var fields = record.Split(' ', 9, StringSplitOptions.None);
        if (fields.Length != 9 || fields[0] != "1")
        {
            throw new InvalidGitOutputException();
        }

        var (indexStatus, worktreeStatus) = ParseStatuses(fields[1]);
        return TrackedEntry(
            fields[8],
            originalPath: null,
            indexStatus,
            worktreeStatus,
            GitConflictKind.None);
    }

    private static GitWorktreeEntry ParseRenameEntry(
        string record,
        string originalPath)
    {
        var fields = record.Split(' ', 10, StringSplitOptions.None);
        if (fields.Length != 10 || fields[0] != "2")
        {
            throw new InvalidGitOutputException();
        }

        var (indexStatus, worktreeStatus) = ParseStatuses(fields[1]);
        if (indexStatus is not (GitPathStatus.Renamed or GitPathStatus.Copied)
            && worktreeStatus is not (
                GitPathStatus.Renamed or GitPathStatus.Copied))
        {
            throw new InvalidGitOutputException();
        }

        return TrackedEntry(
            fields[9],
            originalPath,
            indexStatus,
            worktreeStatus,
            GitConflictKind.None);
    }

    private static GitWorktreeEntry ParseConflictEntry(string record)
    {
        var fields = record.Split(' ', 11, StringSplitOptions.None);
        if (fields.Length != 11 || fields[0] != "u")
        {
            throw new InvalidGitOutputException();
        }

        var conflict = fields[1] switch
        {
            "DD" => GitConflictKind.BothDeleted,
            "AU" => GitConflictKind.AddedByUs,
            "UD" => GitConflictKind.DeletedByThem,
            "UA" => GitConflictKind.AddedByThem,
            "DU" => GitConflictKind.DeletedByUs,
            "AA" => GitConflictKind.BothAdded,
            "UU" => GitConflictKind.BothModified,
            _ => throw new InvalidGitOutputException(),
        };
        return TrackedEntry(
            fields[10],
            originalPath: null,
            GitPathStatus.None,
            GitPathStatus.None,
            conflict);
    }

    private static GitWorktreeEntry ParseUntrackedEntry(string record)
    {
        if (!record.StartsWith("? ", StringComparison.Ordinal)
            || record.Length == 2)
        {
            throw new InvalidGitOutputException();
        }

        return new GitWorktreeEntry(
            record[2..],
            OriginalPath: null,
            GitPathTracking.Untracked,
            GitPathStatus.None,
            GitPathStatus.None,
            GitConflictKind.None);
    }

    private static GitWorktreeEntry TrackedEntry(
        string path,
        string? originalPath,
        GitPathStatus indexStatus,
        GitPathStatus worktreeStatus,
        GitConflictKind conflict)
    {
        if (path.Length == 0 || originalPath is { Length: 0 })
        {
            throw new InvalidGitOutputException();
        }

        return new GitWorktreeEntry(
            path,
            originalPath,
            GitPathTracking.Tracked,
            indexStatus,
            worktreeStatus,
            conflict);
    }

    private static (GitPathStatus Index, GitPathStatus Worktree)
        ParseStatuses(string value)
    {
        if (value.Length != 2)
        {
            throw new InvalidGitOutputException();
        }

        return (ParseStatus(value[0]), ParseStatus(value[1]));
    }

    private static GitPathStatus ParseStatus(char value) => value switch
    {
        '.' => GitPathStatus.None,
        'M' => GitPathStatus.Modified,
        'T' => GitPathStatus.TypeChanged,
        'A' => GitPathStatus.Added,
        'D' => GitPathStatus.Deleted,
        'R' => GitPathStatus.Renamed,
        'C' => GitPathStatus.Copied,
        _ => throw new InvalidGitOutputException(),
    };

    private static string ReadHeaderValue(string record, string prefix)
    {
        var value = record[prefix.Length..];
        return value.Length == 0
            ? throw new InvalidGitOutputException()
            : value;
    }

    private static void RequireNullTerminated(string output)
    {
        if (output.Length != 0 && output[^1] != '\0')
        {
            throw new InvalidGitOutputException();
        }
    }

    private static bool IsObjectId(string value) =>
        value.Length is 40 or 64
        && value.All(static character => Uri.IsHexDigit(character));

    private sealed record ParsedStatus(
        GitHeadState Head,
        IReadOnlyList<GitWorktreeEntry> Entries);

    internal sealed record GitProcessResult(
        string? StandardOutput,
        int? ExitCode,
        WorktreeInspectionFailure? Failure)
    {
        public static GitProcessResult Success(string output, int exitCode) =>
            new(output, exitCode, Failure: null);

        public static GitProcessResult ExecutableNotFound() =>
            FailureResult(
                WorktreeInspectionFailureKind.GitExecutableNotFound);

        public static GitProcessResult PolicyDenied() =>
            FailureResult(
                WorktreeInspectionFailureKind.ProcessPolicyDenied);

        public static GitProcessResult StartFailure() =>
            FailureResult(
                WorktreeInspectionFailureKind.GitProcessStartFailed);

        public static GitProcessResult ProcessFailure(int exitCode) =>
            new(
                StandardOutput: null,
                ExitCode: exitCode,
                new WorktreeInspectionFailure(
                    WorktreeInspectionFailureKind.GitProcessFailed,
                    exitCode));

        public static GitProcessResult TimedOut() =>
            FailureResult(
                WorktreeInspectionFailureKind.GitProcessTimedOut);

        public static GitProcessResult InvalidOutput() =>
            FailureResult(
                WorktreeInspectionFailureKind.InvalidGitOutput);

        private static GitProcessResult FailureResult(
            WorktreeInspectionFailureKind kind) =>
            new(
                StandardOutput: null,
                ExitCode: null,
                new WorktreeInspectionFailure(kind));
    }

    private sealed class InvalidGitOutputException : Exception
    {
    }
}

internal enum PassiveGitExecutableTrust
{
    Trusted,
    Missing,
    WorkspaceControlled,
}

internal sealed record PassiveGitExecutableResolution(
    PassiveGitExecutableTrust Trust,
    string? Path);

internal static class SafePassiveGitBoundary
{
    internal static PassiveGitExecutableResolution ResolveExecutable(
        string executable,
        string workspaceRoot,
        string? pathValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var windows = OperatingSystem.IsWindows();
        var expectedName = windows ? "git.exe" : "git";
        string? candidate = null;

        if (Path.IsPathFullyQualified(executable))
        {
            if (!Path.GetFileName(executable).Equals(
                    expectedName,
                    windows
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return Denied();
            }

            candidate = Path.GetFullPath(executable);
            if (!File.Exists(candidate)
                || (!windows && !IsExecutableFile(candidate)))
            {
                return Missing();
            }
        }
        else
        {
            if (!executable.Equals(
                    expectedName,
                    windows
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return Denied();
            }

            pathValue ??= Environment.GetEnvironmentVariable("PATH");
            if (pathValue is null)
            {
                return Missing();
            }

            foreach (var rawDirectory in pathValue.Split(Path.PathSeparator))
            {
                var directory = windows
                    ? rawDirectory.Trim().Trim('"')
                    : rawDirectory;
                if (string.IsNullOrWhiteSpace(directory)
                    || !Path.IsPathFullyQualified(directory))
                {
                    continue;
                }

                try
                {
                    var path = Path.GetFullPath(
                        Path.Combine(directory, expectedName));
                    if (File.Exists(path)
                        && (windows || IsExecutableFile(path)))
                    {
                        candidate = path;
                        break;
                    }
                }
                catch (Exception exception)
                    when (exception is ArgumentException
                        or NotSupportedException
                        or PathTooLongException
                        or IOException
                        or UnauthorizedAccessException)
                {
                    // Continue to the next absolute PATH entry.
                }
            }

            if (candidate is null)
            {
                return Missing();
            }
        }

        try
        {
            var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
            if (IsWithin(fullWorkspaceRoot, candidate)
                || IsWithin(
                    ResolvePhysicalPath(fullWorkspaceRoot),
                    ResolvePhysicalPath(candidate)))
            {
                return Denied();
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException
                or UnauthorizedAccessException)
        {
            return Denied();
        }

        return new PassiveGitExecutableResolution(
            PassiveGitExecutableTrust.Trusted,
            candidate);
    }

    internal static bool IsAllowedArguments(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.SequenceEqual(
                [
                    "config",
                    "--includes",
                    "--null",
                    "--name-only",
                    "--get-regexp",
                    @"^filter\..*\.(clean|process)$",
                ])
            || arguments.SequenceEqual(
                [
                    "status",
                    "--porcelain=v2",
                    "--branch",
                    "-z",
                    "--untracked-files=all",
                    "--renames",
                    "--ignore-submodules=dirty",
                ])
            || arguments.SequenceEqual(
                ["ls-files", "--cached", "-z"]))
        {
            return true;
        }

        if (arguments.Count == 4
            && arguments[0].Equals("rev-parse", StringComparison.Ordinal)
            && arguments[1].Equals("--verify", StringComparison.Ordinal)
            && arguments[2].Equals("--end-of-options", StringComparison.Ordinal)
            && arguments[3].Length is > 9 and <= 4096
            && arguments[3].EndsWith("^{commit}", StringComparison.Ordinal))
        {
            return true;
        }

        if (arguments.Count == 3
            && arguments[0].Equals("merge-base", StringComparison.Ordinal)
            && IsObjectId(arguments[1])
            && IsObjectId(arguments[2]))
        {
            return true;
        }

        return arguments.Count == 10
            && arguments[0].Equals("diff", StringComparison.Ordinal)
            && arguments[1].Equals("--name-status", StringComparison.Ordinal)
            && arguments[2].Equals("-z", StringComparison.Ordinal)
            && arguments[3].Equals("--find-renames=50%", StringComparison.Ordinal)
            && arguments[4].Equals("--no-ext-diff", StringComparison.Ordinal)
            && arguments[5].Equals("--no-textconv", StringComparison.Ordinal)
            && arguments[6].Equals("--ignore-submodules=dirty", StringComparison.Ordinal)
            && IsObjectId(arguments[7])
            && IsObjectId(arguments[8])
            && arguments[9].Equals("--", StringComparison.Ordinal);
    }

    private static bool IsObjectId(string value) =>
        value.Length is 40 or 64
        && value.All(static character => Uri.IsHexDigit(character));

    private static PassiveGitExecutableResolution Missing() => new(
        PassiveGitExecutableTrust.Missing,
        Path: null);

    private static PassiveGitExecutableResolution Denied() => new(
        PassiveGitExecutableTrust.WorkspaceControlled,
        Path: null);

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

    private static bool IsExecutableFile(string path)
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
