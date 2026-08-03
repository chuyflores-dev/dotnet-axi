using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DotNetAxi.Workspaces;

public sealed class WorktreeStateInspector
{
    private static readonly TimeSpan DefaultProcessTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _gitExecutable;
    private readonly TimeSpan _processTimeout;
    private readonly IWorktreeGitProcessFactory _processFactory;

    public WorktreeStateInspector(
        string gitExecutable = "git",
        TimeSpan? processTimeout = null)
        : this(
            gitExecutable,
            processTimeout,
            SystemWorktreeGitProcessFactory.Instance)
    {
    }

    internal WorktreeStateInspector(
        string gitExecutable,
        TimeSpan? processTimeout,
        IWorktreeGitProcessFactory processFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        ArgumentNullException.ThrowIfNull(processFactory);
        if (processTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTimeout));
        }

        _gitExecutable = gitExecutable;
        _processTimeout = processTimeout ?? DefaultProcessTimeout;
        _processFactory = processFactory;
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
        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = StrictUtf8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
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
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in startInfo.Environment.Keys
                     .Where(static name => name.StartsWith(
                         "GIT_",
                         StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            startInfo.Environment.Remove(variable);
        }

        startInfo.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] =
            OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.Environment["LC_ALL"] = "C";

        using var process = _processFactory.Create(startInfo);
        try
        {
            if (!process.Start())
            {
                return GitProcessResult.StartFailure();
            }
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode is 2 or 3)
        {
            return GitProcessResult.ExecutableNotFound();
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or InvalidOperationException)
        {
            return GitProcessResult.StartFailure();
        }

        process.CloseStandardInput();
        using var timeout = new CancellationTokenSource(_processTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var standardOutput = process.ReadStandardOutputToEndAsync(
            operation.Token);
        var standardError = process.ReadStandardErrorToEndAsync(
            operation.Token);
        var processExit = process.WaitForExitAsync(operation.Token);
        var completion = Task.WhenAll(
            processExit,
            standardOutput,
            standardError);
        var cancellationSignal = Task.Delay(
            Timeout.InfiniteTimeSpan,
            operation.Token);
        try
        {
            if (await Task.WhenAny(completion, cancellationSignal)
                != completion)
            {
                throw new OperationCanceledException(operation.Token);
            }

            await completion;
        }
        catch (OperationCanceledException)
            when (operation.IsCancellationRequested)
        {
            TryTerminate(process);
            Observe(completion);
            cancellationToken.ThrowIfCancellationRequested();
            return GitProcessResult.TimedOut();
        }
        catch (DecoderFallbackException)
        {
            return GitProcessResult.InvalidOutput();
        }

        var exitCode = process.ExitCode;
        return exitCode == 0 || exitCode == additionalSuccessExitCode
            ? GitProcessResult.Success(standardOutput.Result, exitCode)
            : GitProcessResult.ProcessFailure(exitCode);
    }

    private static void TryTerminate(IWorktreeGitProcess process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or Win32Exception)
        {
        }
    }

    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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

internal interface IWorktreeGitProcessFactory
{
    IWorktreeGitProcess Create(ProcessStartInfo startInfo);
}

internal interface IWorktreeGitProcess : IDisposable
{
    bool HasExited { get; }

    int ExitCode { get; }

    bool Start();

    void CloseStandardInput();

    Task<string> ReadStandardOutputToEndAsync(
        CancellationToken cancellationToken);

    Task<string> ReadStandardErrorToEndAsync(
        CancellationToken cancellationToken);

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill(bool entireProcessTree);
}

internal sealed class SystemWorktreeGitProcessFactory
    : IWorktreeGitProcessFactory
{
    public static SystemWorktreeGitProcessFactory Instance { get; } = new();

    private SystemWorktreeGitProcessFactory()
    {
    }

    public IWorktreeGitProcess Create(ProcessStartInfo startInfo) =>
        new SystemWorktreeGitProcess(startInfo);
}

internal sealed class SystemWorktreeGitProcess : IWorktreeGitProcess
{
    private readonly Process _process;

    public SystemWorktreeGitProcess(ProcessStartInfo startInfo)
    {
        _process = new Process
        {
            StartInfo = startInfo,
        };
    }

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public bool Start() => _process.Start();

    public void CloseStandardInput() => _process.StandardInput.Close();

    public Task<string> ReadStandardOutputToEndAsync(
        CancellationToken cancellationToken) =>
        _process.StandardOutput.ReadToEndAsync(cancellationToken);

    public Task<string> ReadStandardErrorToEndAsync(
        CancellationToken cancellationToken) =>
        _process.StandardError.ReadToEndAsync(cancellationToken);

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Kill(bool entireProcessTree) =>
        _process.Kill(entireProcessTree);

    public void Dispose() => _process.Dispose();
}
