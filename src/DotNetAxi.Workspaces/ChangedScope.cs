namespace DotNetAxi.Workspaces;

public enum ChangedScopeMode
{
    Worktree,
    MergeBaseWithWorktree,
    CommittedThreeDot,
}

public enum ChangedScopeErrorKind
{
    GitRequired,
    HeadRequiresBase,
    InvalidBaseReference,
    InvalidHeadReference,
    HeadUnavailable,
    NoMergeBase,
    GitExecutableNotFound,
    GitProcessStartFailed,
    GitProcessFailed,
    GitProcessTimedOut,
    GitFilterCommandsConfigured,
    InvalidGitOutput,
}

public sealed class ChangedScopeRequest
{
    public ChangedScopeRequest(
        string? baseReference = null,
        string? headReference = null)
    {
        BaseReference = baseReference;
        HeadReference = headReference;
    }

    public string? BaseReference { get; }

    public string? HeadReference { get; }
}

public sealed class ChangedScopeResult
{
    internal ChangedScopeResult(
        ChangedScopeMode mode,
        IEnumerable<string> changedPaths,
        IEnumerable<string> excludedConflictedPaths,
        string? resolvedBaseCommit,
        string? resolvedHeadCommit,
        string? mergeBaseCommit,
        bool includesWorktreeChanges)
    {
        Mode = mode;
        ChangedPaths = Copy(changedPaths);
        ExcludedConflictedPaths = Copy(excludedConflictedPaths);
        ResolvedBaseCommit = resolvedBaseCommit;
        ResolvedHeadCommit = resolvedHeadCommit;
        MergeBaseCommit = mergeBaseCommit;
        IncludesWorktreeChanges = includesWorktreeChanges;
    }

    public ChangedScopeMode Mode { get; }

    public IReadOnlyList<string> ChangedPaths { get; }

    public IReadOnlyList<string> ExcludedConflictedPaths { get; }

    public string? ResolvedBaseCommit { get; }

    public string? ResolvedHeadCommit { get; }

    public string? MergeBaseCommit { get; }

    public bool IncludesWorktreeChanges { get; }

    private static IReadOnlyList<string> Copy(IEnumerable<string> paths) =>
        Array.AsReadOnly(
            paths
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
}

public sealed class ChangedScopeResolutionException
    : InvalidOperationException
{
    internal ChangedScopeResolutionException(
        ChangedScopeErrorKind kind,
        string code,
        string message,
        string correction,
        string? reference = null,
        int? processExitCode = null)
        : base(message)
    {
        Kind = kind;
        Code = code;
        Correction = correction;
        Reference = reference;
        ProcessExitCode = processExitCode;
    }

    public ChangedScopeErrorKind Kind { get; }

    public string Code { get; }

    public string Correction { get; }

    public string? Reference { get; }

    public int? ProcessExitCode { get; }
}
