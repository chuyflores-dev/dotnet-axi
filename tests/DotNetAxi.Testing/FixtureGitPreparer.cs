using System.Diagnostics;

namespace DotNetAxi.Testing;

internal static class FixtureGitPreparer
{
    private static readonly TimeSpan ProcessTimeout =
        TimeSpan.FromSeconds(30);

    public static async ValueTask PrepareAsync(
        RepositoryFixture fixture,
        FixtureGitPlan plan,
        CancellationToken cancellationToken)
    {
        var gitDirectory = Path.Combine(fixture.WorkspacePath, ".git");
        if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory))
        {
            throw new InvalidOperationException(
                "Fixture Git preparation can run only once.");
        }

        await RunGitAsync(
            fixture,
            ["init", "--quiet", "--initial-branch=main"],
            expectFailure: false,
            cancellationToken);
        await RunGitAsync(
            fixture,
            ["add", "--all"],
            expectFailure: false,
            cancellationToken);
        await RunGitAsync(
            fixture,
            ["commit", "--quiet", "--message", "fixture baseline"],
            expectFailure: false,
            cancellationToken);

        if (plan.Conflict is not null)
        {
            await PrepareConflictAsync(
                fixture,
                plan.Conflict,
                cancellationToken);
            return;
        }

        foreach (var change in plan.Changes)
        {
            await ApplyChangeAsync(
                fixture,
                change,
                cancellationToken);
        }
    }

    private static async ValueTask ApplyChangeAsync(
        RepositoryFixture fixture,
        FixtureGitChangePlan change,
        CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(fixture.WorkspacePath, change.Path);
        switch (change.Kind)
        {
            case FixtureGitChangeKind.Staged:
                await WriteAsync(path, change.Content, cancellationToken);
                await RunGitAsync(
                    fixture,
                    ["add", "--", change.Path],
                    expectFailure: false,
                    cancellationToken);
                break;
            case FixtureGitChangeKind.Unstaged:
            case FixtureGitChangeKind.Untracked:
                await WriteAsync(path, change.Content, cancellationToken);
                break;
            case FixtureGitChangeKind.Renamed:
                var newPath = change.NewPath
                    ?? throw new InvalidOperationException(
                        "A fixture Git rename requires a destination.");
                var destination = ResolveWorkspacePath(
                    fixture.WorkspacePath,
                    newPath);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destination)
                    ?? throw new InvalidOperationException(
                        "A fixture Git rename destination requires a parent."));
                File.Move(path, destination);
                await RunGitAsync(
                    fixture,
                    ["add", "--all", "--", change.Path, newPath],
                    expectFailure: false,
                    cancellationToken);
                break;
            case FixtureGitChangeKind.Deleted:
                File.Delete(path);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(change),
                    change.Kind,
                    "Unsupported fixture Git change.");
        }
    }

    private static async ValueTask PrepareConflictAsync(
        RepositoryFixture fixture,
        FixtureGitConflictPlan conflict,
        CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(
            fixture.WorkspacePath,
            conflict.Path);
        await RunGitAsync(
            fixture,
            ["checkout", "--quiet", "-b", "fixture-conflict"],
            expectFailure: false,
            cancellationToken);
        await WriteAsync(path, conflict.TheirsContent, cancellationToken);
        await RunGitAsync(
            fixture,
            ["add", "--", conflict.Path],
            expectFailure: false,
            cancellationToken);
        await RunGitAsync(
            fixture,
            ["commit", "--quiet", "--message", "fixture conflict branch"],
            expectFailure: false,
            cancellationToken);

        await RunGitAsync(
            fixture,
            ["checkout", "--quiet", "main"],
            expectFailure: false,
            cancellationToken);
        await WriteAsync(path, conflict.OursContent, cancellationToken);
        await RunGitAsync(
            fixture,
            ["add", "--", conflict.Path],
            expectFailure: false,
            cancellationToken);
        await RunGitAsync(
            fixture,
            ["commit", "--quiet", "--message", "fixture main branch"],
            expectFailure: false,
            cancellationToken);

        var merge = await RunGitAsync(
            fixture,
            ["merge", "--no-edit", "fixture-conflict"],
            expectFailure: true,
            cancellationToken);
        if (merge.ExitCode == 0)
        {
            throw new InvalidOperationException(
                $"Fixture Git conflict '{conflict.Path}' merged without a conflict.");
        }
    }

    private static async ValueTask WriteAsync(
        string path,
        byte[]? content,
        CancellationToken cancellationToken)
    {
        if (content is null)
        {
            throw new InvalidOperationException(
                "A fixture Git content change requires template bytes.");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "A fixture Git path requires a parent directory."));
        await File.WriteAllBytesAsync(path, content, cancellationToken);
    }

    private static string ResolveWorkspacePath(
        string workspacePath,
        string relativePath)
    {
        var root = Path.GetFullPath(workspacePath);
        var candidate = Path.GetFullPath(
            Path.Combine(
                root,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = root.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidOperationException(
                $"Fixture Git path '{relativePath}' escapes the workspace.");
        }

        return candidate;
    }

    private static async ValueTask<GitResult> RunGitAsync(
        RepositoryFixture fixture,
        IReadOnlyList<string> arguments,
        bool expectFailure,
        CancellationToken cancellationToken)
    {
        var startInfo = fixture.CreateProcessStartInfo(
            FixtureProcessKind.Tooling,
            "git",
            arguments.ToArray());
        startInfo.Environment["GIT_AUTHOR_DATE"] =
            "2000-01-01T00:00:00+00:00";
        startInfo.Environment["GIT_COMMITTER_DATE"] =
            "2000-01-01T00:00:00+00:00";
        using var process = new Process
        {
            StartInfo = startInfo,
        };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Fixture Git process did not start.");
        }

        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(
            cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Fixture Git command exceeded {ProcessTimeout.TotalSeconds} seconds.");
        }

        var result = new GitResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
        if (!expectFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"""
                 Fixture Git command failed with exit code {result.ExitCode}.

                 {result.StandardOutput}
                 {result.StandardError}
                 """);
        }

        return result;
    }

    private sealed record GitResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
