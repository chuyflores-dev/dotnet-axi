using DotNetAxi.Contracts;

namespace DotNetAxi.Workspaces;

public sealed class ChangedScopeResolver
{
    private readonly WorktreeStateInspector _worktreeInspector;

    public ChangedScopeResolver(
        IProcessRunner processRunner,
        string gitExecutable = "git",
        TimeSpan? processTimeout = null)
        : this(new WorktreeStateInspector(
            processRunner,
            gitExecutable,
            processTimeout))
    {
    }

    public static ChangedScopeResolver CreatePassive(
        IProcessRunner processRunner) =>
        new(WorktreeStateInspector.CreatePassive(processRunner));

    internal ChangedScopeResolver(
        WorktreeStateInspector worktreeInspector)
    {
        ArgumentNullException.ThrowIfNull(worktreeInspector);
        _worktreeInspector = worktreeInspector;
    }

    public async Task<ChangedScopeResult> ResolveAsync(
        WorkspaceDiscoveryResult workspace,
        ChangedScopeRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        request ??= new ChangedScopeRequest();

        if (workspace.WorkspaceKind != WorkspaceKind.Git)
        {
            throw Error(
                ChangedScopeErrorKind.GitRequired,
                "workspace.git_required",
                "Changed scope requires a Git worktree.",
                "Run the command from a Git worktree or omit --changed.");
        }

        ValidateRequest(request);

        var inspection = await _worktreeInspector.InspectAsync(
            workspace,
            cancellationToken);
        var worktree = inspection.Outcome switch
        {
            WorktreeInspectionOutcome.Available =>
                inspection.State
                ?? throw InvalidOutput(),
            WorktreeInspectionOutcome.NotGit =>
                throw Error(
                    ChangedScopeErrorKind.GitRequired,
                    "workspace.git_required",
                    "Changed scope requires a Git worktree.",
                    "Run the command from a Git worktree or omit --changed."),
            _ => throw InspectionFailure(
                inspection.Failure
                ?? new WorktreeInspectionFailure(
                    WorktreeInspectionFailureKind.InvalidGitOutput)),
        };

        if (request.BaseReference is null)
        {
            var conflicts = ConflictedPaths(worktree);
            var paths = WorktreePaths(worktree);
            paths.ExceptWith(conflicts);
            return new ChangedScopeResult(
                ChangedScopeMode.Worktree,
                paths,
                conflicts,
                resolvedBaseCommit: null,
                worktree.Head.CommitId,
                mergeBaseCommit: null,
                includesWorktreeChanges: true);
        }

        var resolvedBase = await ResolveCommitAsync(
            workspace.RootPath,
            request.BaseReference,
            ChangedScopeErrorKind.InvalidBaseReference,
            "--base",
            cancellationToken);
        var resolvedHead = request.HeadReference is null
            ? worktree.Head.CommitId
                ?? throw Error(
                    ChangedScopeErrorKind.HeadUnavailable,
                    "workspace.git_head_unavailable",
                    "The current Git worktree does not have a committed HEAD.",
                    "Create the initial commit or provide --head with an existing commit reference.")
            : await ResolveCommitAsync(
                workspace.RootPath,
                request.HeadReference,
                ChangedScopeErrorKind.InvalidHeadReference,
                "--head",
                cancellationToken);
        var mergeBase = await ResolveMergeBaseAsync(
            workspace.RootPath,
            resolvedBase,
            resolvedHead,
            cancellationToken);
        var committedPaths = await ReadCommittedPathsAsync(
            workspace.RootPath,
            mergeBase,
            resolvedHead,
            cancellationToken);
        var conflictedPaths = ConflictedPaths(worktree);

        if (request.HeadReference is null)
        {
            committedPaths.UnionWith(WorktreePaths(worktree));
            committedPaths.ExceptWith(conflictedPaths);
            return new ChangedScopeResult(
                ChangedScopeMode.MergeBaseWithWorktree,
                committedPaths,
                conflictedPaths,
                resolvedBase,
                resolvedHead,
                mergeBase,
                includesWorktreeChanges: true);
        }

        conflictedPaths.IntersectWith(committedPaths);
        committedPaths.ExceptWith(conflictedPaths);
        return new ChangedScopeResult(
            ChangedScopeMode.CommittedThreeDot,
            committedPaths,
            conflictedPaths,
            resolvedBase,
            resolvedHead,
            mergeBase,
            includesWorktreeChanges: false);
    }

    private static void ValidateRequest(ChangedScopeRequest request)
    {
        if (request.BaseReference is null
            && request.HeadReference is not null)
        {
            throw Error(
                ChangedScopeErrorKind.HeadRequiresBase,
                "usage.changed_head_requires_base",
                "The --head option requires --base.",
                "Provide --base together with --head, or remove --head.");
        }

        if (request.BaseReference is not null
            && string.IsNullOrWhiteSpace(request.BaseReference))
        {
            throw InvalidReference(
                ChangedScopeErrorKind.InvalidBaseReference,
                "--base",
                request.BaseReference);
        }

        if (request.HeadReference is not null
            && string.IsNullOrWhiteSpace(request.HeadReference))
        {
            throw InvalidReference(
                ChangedScopeErrorKind.InvalidHeadReference,
                "--head",
                request.HeadReference);
        }
    }

    private async Task<string> ResolveCommitAsync(
        string workingDirectory,
        string reference,
        ChangedScopeErrorKind invalidKind,
        string option,
        CancellationToken cancellationToken)
    {
        var result = await _worktreeInspector.RunGitAsync(
            workingDirectory,
            [
                "rev-parse",
                "--verify",
                "--end-of-options",
                $"{reference}^{{commit}}",
            ],
            cancellationToken);
        if (result.Failure is null)
        {
            return ParseObjectId(result.StandardOutput!);
        }

        if (result.Failure.Kind
            == WorktreeInspectionFailureKind.GitProcessFailed)
        {
            throw InvalidReference(invalidKind, option, reference);
        }

        throw InspectionFailure(result.Failure);
    }

    private async Task<string> ResolveMergeBaseAsync(
        string workingDirectory,
        string resolvedBase,
        string resolvedHead,
        CancellationToken cancellationToken)
    {
        var result = await _worktreeInspector.RunGitAsync(
            workingDirectory,
            ["merge-base", resolvedBase, resolvedHead],
            cancellationToken);
        if (result.Failure is null)
        {
            return ParseObjectId(result.StandardOutput!);
        }

        if (result.Failure is
            {
                Kind: WorktreeInspectionFailureKind.GitProcessFailed,
                ExitCode: 1,
            })
        {
            throw Error(
                ChangedScopeErrorKind.NoMergeBase,
                "workspace.git_merge_base_unavailable",
                "The selected base and head do not have a merge base.",
                "Choose --base and --head references that share commit history.");
        }

        throw InspectionFailure(result.Failure);
    }

    private async Task<HashSet<string>> ReadCommittedPathsAsync(
        string workingDirectory,
        string mergeBase,
        string resolvedHead,
        CancellationToken cancellationToken)
    {
        var result = await _worktreeInspector.RunGitAsync(
            workingDirectory,
            [
                "diff",
                "--name-status",
                "-z",
                "--find-renames=50%",
                "--no-ext-diff",
                "--no-textconv",
                "--ignore-submodules=dirty",
                mergeBase,
                resolvedHead,
                "--",
            ],
            cancellationToken);
        if (result.Failure is not null)
        {
            throw InspectionFailure(result.Failure);
        }

        try
        {
            return ParseDiffPaths(result.StandardOutput!);
        }
        catch (InvalidGitOutputException)
        {
            throw InvalidOutput();
        }
    }

    private static HashSet<string> WorktreePaths(GitWorktreeState worktree)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in worktree.Entries)
        {
            if (entry.IsConflicted)
            {
                continue;
            }

            paths.Add(entry.Path);
            if (entry.IsRenamed && entry.OriginalPath is not null)
            {
                paths.Add(entry.OriginalPath);
            }
        }

        return paths;
    }

    private static HashSet<string> ConflictedPaths(
        GitWorktreeState worktree) =>
        worktree.ConflictedEntries
            .Select(static entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ParseDiffPaths(string output)
    {
        if (output.Length != 0 && output[^1] != '\0')
        {
            throw new InvalidGitOutputException();
        }

        var records = output.Split('\0');
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        while (index < records.Length)
        {
            var status = records[index++];
            if (status.Length == 0)
            {
                if (index != records.Length)
                {
                    throw new InvalidGitOutputException();
                }

                break;
            }

            if (status[0] is 'R' or 'C')
            {
                if (!IsSimilarityStatus(status)
                    || index + 1 >= records.Length)
                {
                    throw new InvalidGitOutputException();
                }

                var originalPath = ReadPath(records[index++]);
                var path = ReadPath(records[index++]);
                paths.Add(path);
                if (status[0] == 'R')
                {
                    paths.Add(originalPath);
                }

                continue;
            }

            if (!IsSinglePathStatus(status)
                || index >= records.Length)
            {
                throw new InvalidGitOutputException();
            }

            paths.Add(ReadPath(records[index++]));
        }

        return paths;
    }

    private static bool IsSimilarityStatus(string status) =>
        status.Length is >= 2 and <= 4
        && status[1..].All(static character => char.IsAsciiDigit(character))
        && int.TryParse(status[1..], out var score)
        && score is >= 0 and <= 100;

    private static bool IsSinglePathStatus(string status) =>
        status.Length == 1
        && status[0] is 'A' or 'B' or 'D' or 'M' or 'T' or 'U' or 'X';

    private static string ReadPath(string path) =>
        path.Length == 0
            ? throw new InvalidGitOutputException()
            : path;

    private static string ParseObjectId(string output)
    {
        string value;
        if (output.EndsWith("\r\n", StringComparison.Ordinal))
        {
            value = output[..^2];
        }
        else if (output.EndsWith('\n'))
        {
            value = output[..^1];
        }
        else
        {
            throw InvalidOutput();
        }

        if (!IsObjectId(value))
        {
            throw InvalidOutput();
        }

        return value;
    }

    private static bool IsObjectId(string value) =>
        value.Length is 40 or 64
        && value.All(static character => Uri.IsHexDigit(character));

    private static ChangedScopeResolutionException InvalidReference(
        ChangedScopeErrorKind kind,
        string option,
        string reference) =>
        Error(
            kind,
            "workspace.git_ref_invalid",
            $"The {option} reference does not resolve to a commit.",
            $"Provide {option} with an existing commit reference.",
            reference);

    private static ChangedScopeResolutionException InspectionFailure(
        WorktreeInspectionFailure failure) =>
        failure.Kind switch
        {
            WorktreeInspectionFailureKind.ProcessPolicyDenied => Error(
                ChangedScopeErrorKind.ProcessPolicyDenied,
                "operation.passive_process_denied",
                "Changed-scope resolution requires a Git process, which passive operation policy denied.",
                "Omit --changed and use workspace or path selectors; inspect Git changes separately when needed."),
            WorktreeInspectionFailureKind.GitExecutableNotFound => Error(
                ChangedScopeErrorKind.GitExecutableNotFound,
                "workspace.git_unavailable",
                "Git is not available for changed-scope resolution.",
                "Install Git or run the command in an environment where Git is available."),
            WorktreeInspectionFailureKind.GitProcessStartFailed => Error(
                ChangedScopeErrorKind.GitProcessStartFailed,
                "workspace.git_start_failed",
                "Git could not be started for changed-scope resolution.",
                "Verify that the configured Git executable can be started."),
            WorktreeInspectionFailureKind.GitProcessFailed => Error(
                ChangedScopeErrorKind.GitProcessFailed,
                "workspace.git_failed",
                "Git failed while resolving changed scope.",
                "Verify the repository state and the selected Git references.",
                processExitCode: failure.ExitCode),
            WorktreeInspectionFailureKind.GitProcessTimedOut => Error(
                ChangedScopeErrorKind.GitProcessTimedOut,
                "workspace.git_timed_out",
                "Git timed out while resolving changed scope.",
                "Retry after checking the repository and local Git configuration."),
            WorktreeInspectionFailureKind.GitFilterCommandsConfigured =>
                Error(
                    ChangedScopeErrorKind.GitFilterCommandsConfigured,
                    "workspace.git_filter_commands_configured",
                    "Changed scope cannot inspect a repository with clean or process filter commands configured.",
                    "Remove executable clean and process filters before resolving changed scope."),
            WorktreeInspectionFailureKind.InvalidGitOutput => InvalidOutput(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure.Kind,
                "The worktree inspection failure kind is not defined."),
        };

    private static ChangedScopeResolutionException InvalidOutput() =>
        Error(
            ChangedScopeErrorKind.InvalidGitOutput,
            "workspace.git_output_invalid",
            "Git returned invalid output while resolving changed scope.",
            "Verify the repository and use a supported Git version.");

    private static ChangedScopeResolutionException Error(
        ChangedScopeErrorKind kind,
        string code,
        string message,
        string correction,
        string? reference = null,
        int? processExitCode = null) =>
        new(
            kind,
            code,
            message,
            correction,
            reference,
            processExitCode);

    private sealed class InvalidGitOutputException : Exception
    {
    }
}
