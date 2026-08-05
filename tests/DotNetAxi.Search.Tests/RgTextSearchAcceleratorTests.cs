using System.Text;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Search;

namespace DotNetAxi.Search.Tests;

public sealed class RgTextSearchAcceleratorTests
{
    [Fact]
    public async Task Compatible_present_rg_preserves_the_full_builtin_result()
    {
        var root = CreateDirectory();
        try
        {
            var first = CreateFile(root, "first.cs", "before\nNEEDLE");
            var noMatch = CreateFile(root, "no-match.cs", "nothing here");
            var utf16 = Path.Combine(root, "utf16.cs");
            await File.WriteAllTextAsync(
                utf16,
                "NEEDLE",
                new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: true));
            var binary = Path.Combine(root, "binary.dat");
            await File.WriteAllBytesAsync(binary, [0, 1, 2]);
            var invalid = Path.Combine(root, "invalid.txt");
            await File.WriteAllBytesAsync(invalid, [0xff]);
            var paths = new[]
            {
                Entry(first, "first.cs"),
                Entry(noMatch, "no-match.cs"),
                Entry(utf16, "utf16.cs"),
                Entry(binary, "binary.dat"),
                Entry(invalid, "invalid.txt"),
            };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner(
                Completed(0, "ripgrep 15.2.0 (rev verified)\n"),
                Completed(0, first + '\0' + utf16 + '\0'));

            var actual = await AcceleratedSearchAsync(
                root,
                paths,
                request,
                runner);

            AssertEquivalent(expected, actual);
            Assert.Equal(2, runner.Requests.Count);
            Assert.Equal(["--version"], runner.Requests[0].Arguments);
            Assert.Equal(
                [
                    "--no-config",
                    "--files-with-matches",
                    "--null",
                    "--fixed-strings",
                    "--case-sensitive",
                    "--encoding",
                    "auto",
                    "--no-messages",
                    "--",
                    "NEEDLE",
                    first,
                    noMatch,
                    utf16,
                    binary,
                    invalid,
                ],
                runner.Requests[1].Arguments);
            Assert.All(runner.Requests, item =>
                Assert.Equal(FakeExecutable(root), item.ExecutablePath));
            Assert.Equal(
                TextSearchFileStatus.Analyzed,
                actual.Observations.Single(item => item.Path == "no-match.cs").Status);

            var absentRunner = new RecordingProcessRunner();
            var absent = await new LiteralTextSearcher(
                    new Traverser(paths),
                    new RgTextSearchAccelerator(
                        absentRunner,
                        executablePath: null))
                .SearchAsync(request);
            var incompatibleRunner = new RecordingProcessRunner(
                Completed(0, "ripgrep 12.1.0\n"));
            var incompatible = await AcceleratedSearchAsync(
                root,
                paths,
                request,
                incompatibleRunner);

            AssertEquivalent(expected, absent);
            AssertEquivalent(expected, incompatible);
            Assert.Empty(absentRunner.Requests);
            Assert.Single(incompatibleRunner.Requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rg_no_match_preserves_observations_skips_and_known_totals()
    {
        var root = CreateDirectory();
        try
        {
            var text = CreateFile(root, "none.cs", "nothing here");
            var binary = Path.Combine(root, "binary.dat");
            await File.WriteAllBytesAsync(binary, [0, 1]);
            var paths = new[]
            {
                Entry(text, "none.cs"),
                Entry(binary, "binary.dat"),
            };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner(
                Completed(0, "ripgrep 15.2.0\n"),
                Completed(1, string.Empty));

            var actual = await AcceleratedSearchAsync(
                root,
                paths,
                request,
                runner);

            AssertEquivalent(expected, actual);
            Assert.Empty(actual.Matches);
            Assert.Equal(0, actual.Total);
            Assert.True(actual.TotalKnown);
            Assert.True(actual.SkipTotalsKnown);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Builtin_validation_catches_a_match_added_after_rg_observation()
    {
        var root = CreateDirectory();
        try
        {
            var path = CreateFile(root, "changed.cs", "nothing here");
            var paths = new[] { Entry(path, "changed.cs") };
            var request = Request(root, limit: 10);
            var runner = new MutatingNoMatchProcessRunner(path);
            var actual = await new LiteralTextSearcher(
                    new Traverser(paths),
                    new RgTextSearchAccelerator(runner, FakeExecutable(root)))
                .SearchAsync(request);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);

            AssertEquivalent(expected, actual);
            Assert.Single(actual.Matches);
            Assert.Equal(2, runner.Requests.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rg_candidates_preserve_limit_order_and_unknown_totals()
    {
        var root = CreateDirectory();
        try
        {
            var first = CreateFile(root, "first.cs", "NEEDLE\nNEEDLE");
            var second = CreateFile(root, "second.cs", "NEEDLE");
            var paths = new[]
            {
                Entry(first, "first.cs"),
                Entry(second, "second.cs"),
            };
            var request = Request(root, limit: 2);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner(
                Completed(0, "ripgrep 15.2.0\n"),
                Completed(0, first + '\0' + second + '\0'));

            var actual = await AcceleratedSearchAsync(
                root,
                paths,
                request,
                runner);

            AssertEquivalent(expected, actual);
            Assert.False(actual.TotalKnown);
            Assert.Null(actual.Total);
            Assert.Equal(
                TextSearchFileStatus.LimitReached,
                actual.Observations[^1].Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Absent_rg_falls_back_without_starting_a_process()
    {
        var root = CreateDirectory();
        try
        {
            var path = CreateFile(root, "match.cs", "NEEDLE");
            var paths = new[] { Entry(path, "match.cs") };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner();
            var accelerator = new RgTextSearchAccelerator(
                runner,
                executablePath: null);

            var actual = await new LiteralTextSearcher(
                    new Traverser(paths),
                    accelerator)
                .SearchAsync(request);

            AssertEquivalent(expected, actual);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Incompatible_rg_version_falls_back_after_detection()
    {
        var root = CreateDirectory();
        try
        {
            var path = CreateFile(root, "match.cs", "NEEDLE");
            var paths = new[] { Entry(path, "match.cs") };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner(
                Completed(0, "ripgrep 12.1.0\n"));

            var actual = await AcceleratedSearchAsync(
                root,
                paths,
                request,
                runner);

            AssertEquivalent(expected, actual);
            Assert.Single(runner.Requests);
            Assert.Equal(["--version"], runner.Requests[0].Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dependency_failure_and_raw_diagnostics_fall_back_silently()
    {
        var root = CreateDirectory();
        try
        {
            var path = CreateFile(root, "match.cs", "NEEDLE");
            var paths = new[] { Entry(path, "match.cs") };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner(
                Completed(0, "ripgrep 15.2.0\n"),
                Completed(2, string.Empty, "raw rg failure details"));

            var actual = await AcceleratedSearchAsync(
                root,
                paths,
                request,
                runner);

            AssertEquivalent(expected, actual);
            Assert.DoesNotContain(
                "raw rg failure details",
                actual.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Unsupported_case_and_regex_semantics_do_not_probe_rg()
    {
        var root = CreateDirectory();
        try
        {
            var path = CreateFile(root, "match.cs", "NEEDLE");
            var paths = new[] { Entry(path, "match.cs") };
            var runner = new RecordingProcessRunner();
            var accelerator = new RgTextSearchAccelerator(
                runner,
                FakeExecutable(root));

            var insensitive = await new LiteralTextSearcher(
                    new Traverser(paths),
                    accelerator)
                .SearchAsync(new TextSearchRequest(
                    "needle",
                    new WorkspaceTraversalRequest(root),
                    caseSensitive: false));
            var regex = await new RegexTextSearcher(
                    new Traverser(paths),
                    accelerator)
                .SearchAsync(new RegexTextSearchRequest(
                    "NEEDLE",
                    new WorkspaceTraversalRequest(root),
                    TimeSpan.FromSeconds(1),
                    caseSensitive: true));

            Assert.Single(insensitive.Matches);
            Assert.Single(regex.Matches);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Candidate_outside_the_current_batch_fails_closed()
    {
        var root = CreateDirectory();
        try
        {
            var paths = Enumerable.Range(0, 129)
                .Select(index =>
                {
                    var name = $"{index:D3}.cs";
                    var content = index == 0 ? "NEEDLE" : "nothing here";
                    return Entry(CreateFile(root, name, content), name);
                })
                .ToArray();
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner(
                Completed(0, "ripgrep 15.2.0\n"),
                Completed(0, paths[^1].FullPath + '\0'));

            var actual = await AcceleratedSearchAsync(
                root,
                paths,
                request,
                runner);

            AssertEquivalent(expected, actual);
            Assert.Single(actual.Matches);
            Assert.Equal(2, runner.Requests.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_during_detection_is_propagated()
    {
        var root = CreateDirectory();
        try
        {
            var path = CreateFile(root, "match.cs", "NEEDLE");
            var paths = new[] { Entry(path, "match.cs") };
            using var cancellation = new CancellationTokenSource();
            var runner = new CancellingProcessRunner(cancellation);
            var accelerator = new RgTextSearchAccelerator(
                runner,
                FakeExecutable(root));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new LiteralTextSearcher(new Traverser(paths), accelerator)
                    .SearchAsync(Request(root, limit: 10), cancellation.Token));

            Assert.Equal(1, runner.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Path_resolution_is_optional_and_platform_specific()
    {
        var missingDirectory = Path.Combine(
            Path.GetTempPath(),
            "dnaxi-rg-missing");
        var toolsDirectory = Path.Combine(
            Path.GetTempPath(),
            "dnaxi-rg-tools");
        var pathValue = string.Join(
            Path.PathSeparator,
            missingDirectory,
            toolsDirectory);
        var expected = Path.GetFullPath(
            Path.Combine(toolsDirectory, "rg"));

        var unix = RgTextSearchAccelerator.ResolveExecutablePath(
            pathValue,
            isWindows: false,
            path => path == expected,
            _ => true);
        var absent = RgTextSearchAccelerator.ResolveExecutablePath(
            pathValue,
            isWindows: false,
            _ => false,
            _ => true);

        Assert.Equal(expected, unix);
        Assert.Null(absent);
    }

    [Fact]
    public void Path_resolution_skips_relative_and_non_executable_entries()
    {
        var blockedDirectory = Path.Combine(
            Path.GetTempPath(),
            "dnaxi-rg-blocked");
        var usableDirectory = Path.Combine(
            Path.GetTempPath(),
            "dnaxi-rg-usable");
        var blocked = Path.GetFullPath(Path.Combine(blockedDirectory, "rg"));
        var usable = Path.GetFullPath(Path.Combine(usableDirectory, "rg"));
        var pathValue = string.Join(
            Path.PathSeparator,
            "tools",
            blockedDirectory,
            usableDirectory);

        var resolved = RgTextSearchAccelerator.ResolveExecutablePath(
            pathValue,
            isWindows: false,
            path => path == blocked || path == usable,
            path => path == usable);

        Assert.Equal(usable, resolved);
    }

    [Fact]
    public async Task Workspace_local_rg_is_never_probed_or_executed()
    {
        var root = CreateDirectory();
        try
        {
            var path = CreateFile(root, "match.cs", "NEEDLE");
            var paths = new[] { Entry(path, "match.cs") };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner();
            var accelerator = new RgTextSearchAccelerator(
                runner,
                Path.Combine(root, "tools", "rg"));

            var actual = await new LiteralTextSearcher(
                    new Traverser(paths),
                    accelerator)
                .SearchAsync(request);

            AssertEquivalent(expected, actual);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Multi_hop_symlink_to_workspace_rg_is_never_probed_or_executed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateDirectory();
        var aliasRoot = root + "-aliases";
        try
        {
            var path = CreateFile(root, "match.cs", "NEEDLE");
            var workspaceExecutable = CreateFile(root, "rg", "repository-controlled");
            Directory.CreateDirectory(aliasRoot);
            var secondHop = Path.Combine(aliasRoot, "second-hop");
            var firstHop = Path.Combine(aliasRoot, "first-hop");
            File.CreateSymbolicLink(secondHop, workspaceExecutable);
            File.CreateSymbolicLink(firstHop, secondHop);
            var paths = new[] { Entry(path, "match.cs") };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingProcessRunner();

            var actual = await new LiteralTextSearcher(
                    new Traverser(paths),
                    new RgTextSearchAccelerator(runner, firstHop))
                .SearchAsync(request);

            AssertEquivalent(expected, actual);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            File.Delete(Path.Combine(aliasRoot, "first-hop"));
            File.Delete(Path.Combine(aliasRoot, "second-hop"));
            Directory.Delete(aliasRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Installed_rg_smoke_preserves_the_full_builtin_result_when_available()
    {
        var executablePath = RgTextSearchAccelerator.ResolveExecutablePath();
        if (executablePath is null)
        {
            return;
        }

        var root = CreateDirectory();
        try
        {
            var utf8 = CreateFile(root, "utf8.cs", "before\nNEEDLE\n");
            var utf16 = Path.Combine(root, "utf16.cs");
            await File.WriteAllTextAsync(
                utf16,
                "NEEDLE",
                new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: true));
            var noMatch = CreateFile(root, "no-match.cs", "nothing here");
            var binary = Path.Combine(root, "binary.dat");
            await File.WriteAllBytesAsync(binary, [0, 1, 2]);
            var paths = new[]
            {
                Entry(utf8, "utf8.cs"),
                Entry(utf16, "utf16.cs"),
                Entry(noMatch, "no-match.cs"),
                Entry(binary, "binary.dat"),
            };
            var request = Request(root, limit: 10);
            var expected = await new LiteralTextSearcher(new Traverser(paths))
                .SearchAsync(request);
            var runner = new RecordingDelegatingProcessRunner(new ProcessRunner());

            var actual = await new LiteralTextSearcher(
                    new Traverser(paths),
                    new RgTextSearchAccelerator(runner, executablePath))
                .SearchAsync(request);

            AssertEquivalent(expected, actual);
            Assert.True(runner.Requests.Count >= 2);
            Assert.Equal(["--version"], runner.Requests[0].Request.Arguments);
            Assert.Equal(0, runner.Requests[1].ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<TextSearchResult> AcceleratedSearchAsync(
        string root,
        IReadOnlyList<WorkspaceTraversalPath> paths,
        TextSearchRequest request,
        RecordingProcessRunner runner) =>
        await new LiteralTextSearcher(
                new Traverser(paths),
                new RgTextSearchAccelerator(runner, FakeExecutable(root)))
            .SearchAsync(request);

    private static TextSearchRequest Request(string root, int limit) =>
        new(
            "NEEDLE",
            new WorkspaceTraversalRequest(root),
            caseSensitive: true,
            limit: limit,
            previewLength: 160,
            skippedDetailLimit: 50);

    private static void AssertEquivalent(
        TextSearchResult expected,
        TextSearchResult actual)
    {
        Assert.Equal(expected.Matches.ToArray(), actual.Matches.ToArray());
        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.TotalKnown, actual.TotalKnown);
        Assert.Equal(expected.SkippedBinary, actual.SkippedBinary);
        Assert.Equal(expected.SkippedUndecodable, actual.SkippedUndecodable);
        Assert.Equal(
            expected.SkippedUnsupportedEncoding,
            actual.SkippedUnsupportedEncoding);
        Assert.Equal(expected.SkippedUnreadable, actual.SkippedUnreadable);
        Assert.Equal(expected.Snapshot, actual.Snapshot);
        Assert.Equal(
            expected.SkippedFiles.ToArray(),
            actual.SkippedFiles.ToArray());
        Assert.Equal(
            expected.Observations.ToArray(),
            actual.Observations.ToArray());
        Assert.Equal(expected.Errors.ToArray(), actual.Errors.ToArray());
        Assert.Equal(expected.SkipTotalsKnown, actual.SkipTotalsKnown);
        Assert.Equal(expected.SkippedFileTotal, actual.SkippedFileTotal);
    }

    private static ProcessRunResult Completed(
        int exitCode,
        string standardOutput,
        string standardError = "") =>
        new(
            ProcessLifecycle.Completed,
            ProcessRunOutcome.Completed,
            ProcessStartFailure.None,
            new ProcessExitEvidence(exitCode, signal: null),
            new ProcessCapturedOutput(standardOutput, limitExceeded: false),
            new ProcessCapturedOutput(standardError, limitExceeded: false),
            TimeSpan.FromMilliseconds(1));

    private static ProcessRunResult Cancelled() =>
        new(
            ProcessLifecycle.NotStarted,
            ProcessRunOutcome.Cancelled,
            ProcessStartFailure.None,
            exit: null,
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            TimeSpan.Zero);

    private static WorkspaceTraversalPath Entry(
        string fullPath,
        string relativePath) =>
        new(fullPath, relativePath, isExternal: false);

    private static string CreateFile(string root, string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-rg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FakeExecutable(string root) =>
        Path.Combine(root + "-tools", OperatingSystem.IsWindows() ? "rg.exe" : "rg");

    private sealed class Traverser(IEnumerable<WorkspaceTraversalPath> paths)
        : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return paths.ToArray();
        }
    }

    private sealed class RecordingProcessRunner(
        params ProcessRunResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _results = new(results);

        public List<ProcessRunRequest> Requests { get; } = [];

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class CancellingProcessRunner(
        CancellationTokenSource cancellation) : IProcessRunner
    {
        public int Calls { get; private set; }

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellation.Cancel();
            return ValueTask.FromResult(Cancelled());
        }
    }

    private sealed class MutatingNoMatchProcessRunner(string path) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (Requests.Count == 1)
            {
                return ValueTask.FromResult(Completed(0, "ripgrep 15.2.0\n"));
            }

            File.WriteAllText(path, "NEEDLE");
            return ValueTask.FromResult(Completed(1, string.Empty));
        }
    }

    private sealed class RecordingDelegatingProcessRunner(IProcessRunner inner)
        : IProcessRunner
    {
        public List<(ProcessRunRequest Request, int? ExitCode)> Requests { get; } = [];

        public async ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            var result = await inner.RunAsync(request, cancellationToken);
            Requests.Add((request, result.Exit?.ExitCode));
            return result;
        }
    }
}
