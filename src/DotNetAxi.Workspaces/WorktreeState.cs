namespace DotNetAxi.Workspaces;

public enum WorktreeInspectionOutcome
{
    Available,
    NotGit,
    GitUnavailable,
    Failed,
}

public enum WorktreeInspectionFailureKind
{
    GitExecutableNotFound,
    GitProcessStartFailed,
    GitProcessFailed,
    GitProcessTimedOut,
    GitFilterCommandsConfigured,
    InvalidGitOutput,
}

public sealed record WorktreeInspectionFailure(
    WorktreeInspectionFailureKind Kind,
    int? ExitCode = null);

public enum GitHeadKind
{
    Branch,
    Detached,
    Unborn,
}

public sealed record GitHeadState(
    GitHeadKind Kind,
    string? BranchName,
    string? CommitId);

public enum GitPathTracking
{
    Tracked,
    Untracked,
}

public enum GitPathStatus
{
    None,
    Modified,
    TypeChanged,
    Added,
    Deleted,
    Renamed,
    Copied,
}

public enum GitConflictKind
{
    None,
    BothDeleted,
    AddedByUs,
    DeletedByThem,
    AddedByThem,
    DeletedByUs,
    BothAdded,
    BothModified,
}

public sealed record GitWorktreeEntry(
    string Path,
    string? OriginalPath,
    GitPathTracking Tracking,
    GitPathStatus IndexStatus,
    GitPathStatus WorktreeStatus,
    GitConflictKind Conflict)
{
    public bool IsStaged =>
        Conflict == GitConflictKind.None
        && Tracking == GitPathTracking.Tracked
        && IndexStatus != GitPathStatus.None;

    public bool IsUnstaged =>
        Conflict == GitConflictKind.None
        && Tracking == GitPathTracking.Tracked
        && WorktreeStatus != GitPathStatus.None;

    public bool IsUntracked => Tracking == GitPathTracking.Untracked;

    public bool IsRenamed =>
        IndexStatus == GitPathStatus.Renamed
        || WorktreeStatus == GitPathStatus.Renamed;

    public bool IsDeleted =>
        IndexStatus == GitPathStatus.Deleted
        || WorktreeStatus == GitPathStatus.Deleted;

    public bool IsConflicted => Conflict != GitConflictKind.None;
}

public sealed class GitWorktreeState
{
    internal GitWorktreeState(
        GitHeadState head,
        IEnumerable<string> trackedPaths,
        IEnumerable<GitWorktreeEntry> entries)
    {
        Head = head;
        TrackedPaths = Copy(trackedPaths);
        Entries = Copy(entries);
        StagedEntries = Copy(Entries.Where(static entry => entry.IsStaged));
        UnstagedEntries = Copy(
            Entries.Where(static entry => entry.IsUnstaged));
        UntrackedEntries = Copy(
            Entries.Where(static entry => entry.IsUntracked));
        RenamedEntries = Copy(
            Entries.Where(static entry => entry.IsRenamed));
        DeletedEntries = Copy(
            Entries.Where(static entry => entry.IsDeleted));
        ConflictedEntries = Copy(
            Entries.Where(static entry => entry.IsConflicted));
    }

    public GitHeadState Head { get; }

    public IReadOnlyList<string> TrackedPaths { get; }

    public IReadOnlyList<GitWorktreeEntry> Entries { get; }

    public IReadOnlyList<GitWorktreeEntry> StagedEntries { get; }

    public IReadOnlyList<GitWorktreeEntry> UnstagedEntries { get; }

    public IReadOnlyList<GitWorktreeEntry> UntrackedEntries { get; }

    public IReadOnlyList<GitWorktreeEntry> RenamedEntries { get; }

    public IReadOnlyList<GitWorktreeEntry> DeletedEntries { get; }

    public IReadOnlyList<GitWorktreeEntry> ConflictedEntries { get; }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed class WorktreeStateResult
{
    internal WorktreeStateResult(
        WorktreeInspectionOutcome outcome,
        GitWorktreeState? state,
        WorktreeInspectionFailure? failure)
    {
        Outcome = outcome;
        State = state;
        Failure = failure;
    }

    public WorktreeInspectionOutcome Outcome { get; }

    public GitWorktreeState? State { get; }

    public WorktreeInspectionFailure? Failure { get; }
}
