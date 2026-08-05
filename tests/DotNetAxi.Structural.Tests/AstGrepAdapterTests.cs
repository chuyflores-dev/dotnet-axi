using System.Text;
using System.Text.Json;
using DotNetAxi.Contracts;

namespace DotNetAxi.Structural.Tests;

public sealed class AstGrepAdapterTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task Supported_backend_uses_exact_bounded_arguments_and_converts_utf8_coordinates()
    {
        using var fixture = new SourceFixture("Unicode.cs", "😀Call();\n");
        var runner = new StubProcessRunner(
            Completed(0, "ast-grep 0.45.0\n"),
            Completed(
                0,
                MatchJson(
                    fixture.Path,
                    "Call()",
                    byteStart: 4,
                    byteEnd: 10,
                    line: 0,
                    startColumn: 4,
                    endColumn: 10)));
        var traverser = new StubTraverser(fixture.TraversalPath);
        var adapter = Adapter(fixture, traverser, runner);

        var result = await adapter.SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.Succeeded, result.Outcome);
        Assert.Equal(AstGrepCapabilityState.Supported, result.Capability.State);
        Assert.Equal(new AstGrepVersion(0, 45, 0), result.Capability.Version);
        var candidate = Assert.Single(result.Candidates);
        Assert.StartsWith("syntax/v1/", candidate.Id, StringComparison.Ordinal);
        Assert.Equal("Call()", candidate.Text);
        Assert.Equal("Unicode.cs", candidate.Range.Start.Path);
        Assert.Equal(1, candidate.Range.Start.Line);
        Assert.Equal(3, candidate.Range.Start.Column);
        Assert.Equal(1, candidate.Range.End.Line);
        Assert.Equal(9, candidate.Range.End.Column);

        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal(["--version"], runner.Requests[0].Arguments);
        Assert.Equal(
            [
                "run",
                "--pattern",
                "Call()",
                "--lang",
                "csharp",
                "--json=compact",
                "--color",
                "never",
                "--no-ignore",
                "hidden",
                "--no-ignore",
                "dot",
                "--no-ignore",
                "exclude",
                "--no-ignore",
                "global",
                "--no-ignore",
                "parent",
                "--no-ignore",
                "vcs",
                "--",
                fixture.Path,
            ],
            runner.Requests[1].Arguments);
        Assert.DoesNotContain("--rewrite", runner.Requests[1].Arguments);
        Assert.Equal("1", runner.Requests[1].Environment["NO_COLOR"]);
        Assert.Equal("dumb", runner.Requests[1].Environment["TERM"]);
        Assert.Same(fixture.Traversal, traverser.Requests.Single());
    }

    [Fact]
    public async Task Shared_traversal_is_filtered_to_csharp_and_bounded_to_one_path_per_process()
    {
        using var fixture = new SourceFixture("First.cs", "class First {}\n");
        var secondPath = System.IO.Path.Combine(fixture.Root, "Second.cs");
        var textPath = System.IO.Path.Combine(fixture.Root, "notes.txt");
        await File.WriteAllTextAsync(secondPath, "class Second {}\n");
        await File.WriteAllTextAsync(textPath, "not source\n");
        var paths = new[]
        {
            new WorkspaceTraversalPath(secondPath, "Second.cs", false),
            new WorkspaceTraversalPath(textPath, "notes.txt", false),
            fixture.TraversalPath,
        };
        var runner = new StubProcessRunner(
            Completed(0, "ast-grep 0.45.7"),
            Completed(1, "[]"),
            Completed(1, "[]"));
        var adapter = Adapter(fixture, new StubTraverser(paths), runner);

        var result = await adapter.SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.Succeeded, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal(fixture.Path, runner.Requests[1].Arguments[^1]);
        Assert.Equal(secondPath, runner.Requests[2].Arguments[^1]);
        Assert.All(
            runner.Requests.Skip(1),
            static request => Assert.Single(
                request.Arguments.SkipWhile(argument => argument != "--").Skip(1)));
    }

    [Fact]
    public async Task Missing_backend_is_an_actionable_capability_result_without_traversal()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var traverser = new StubTraverser(fixture.TraversalPath);
        var runner = new StubProcessRunner(StartFailed(ProcessStartFailure.ExecutableNotFound));
        var result = await Adapter(fixture, traverser, runner).SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.CapabilityUnavailable, result.Outcome);
        Assert.Equal(AstGrepCapabilityState.Missing, result.Capability.State);
        Assert.Equal(AstGrepIssueKind.Missing, result.Issue!.Kind);
        Assert.Equal("structural.ast_grep_missing", result.Issue.Code);
        Assert.Contains("Install", result.Issue.Correction, StringComparison.Ordinal);
        Assert.Empty(result.Candidates);
        Assert.Empty(traverser.Requests);
    }

    [Fact]
    public async Task Incompatible_backend_reports_sanitized_version_and_correction()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(Completed(0, "ast-grep 0.44.9")))
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.CapabilityUnavailable, result.Outcome);
        Assert.Equal(AstGrepCapabilityState.Incompatible, result.Capability.State);
        Assert.Equal(new AstGrepVersion(0, 44, 9), result.Capability.Version);
        Assert.Equal(AstGrepIssueKind.IncompatibleVersion, result.Issue!.Kind);
        Assert.Contains("0.45.0", result.Issue.Correction, StringComparison.Ordinal);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Malformed_version_is_an_incompatible_capability_result()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(Completed(0, "ast-grep development-build RAW")))
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.CapabilityUnavailable, result.Outcome);
        Assert.Equal(AstGrepCapabilityState.Incompatible, result.Capability.State);
        Assert.Null(result.Capability.Version);
        Assert.Equal(AstGrepIssueKind.MalformedVersion, result.Issue!.Kind);
        Assert.DoesNotContain("development-build", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Versions_follow_semver_precedence_and_parse_build_metadata_safely()
    {
        var prerelease = new AstGrepVersion(0, 45, 0, isPrerelease: true);
        var stable = new AstGrepVersion(0, 45, 0);

        Assert.True(prerelease.CompareTo(stable) < 0);
        Assert.True(AstGrepAdapter.TryParseVersion("ast-grep 0.45.7+official", out var parsed));
        Assert.Equal(new AstGrepVersion(0, 45, 7), parsed);
        Assert.True(AstGrepAdapter.TryParseVersion("ast-grep 0.45.7-rc.1", out var parsedPrerelease));
        Assert.True(parsedPrerelease.IsPrerelease);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[{\"text\":\"class\"}]")]
    public async Task Malformed_backend_output_is_a_typed_adapter_result(string output)
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        const string rawDiagnostic = "RAW-BACKEND-DIAGNOSTIC";
        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(
                    Completed(0, "ast-grep 0.45.0"),
                    Completed(0, output, rawDiagnostic)))
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.MalformedOutput, result.Outcome);
        Assert.Equal(AstGrepIssueKind.MalformedOutput, result.Issue!.Kind);
        Assert.Equal("structural.ast_grep_output_invalid", result.Issue.Code);
        Assert.DoesNotContain(rawDiagnostic, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(output, result.ToString(), StringComparison.Ordinal);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task No_match_exit_is_successful_empty_result()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(
                    Completed(0, "ast-grep 0.45.0"),
                    Completed(1, "[]", "No matches")))
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.Succeeded, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.Null(result.Issue);
    }

    [Fact]
    public async Task Cancellation_after_empty_backend_result_is_not_reported_as_success()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        using var cancellation = new CancellationTokenSource();
        var runner = new CancellingNoMatchRunner(cancellation);

        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                runner)
            .SearchAsync(Request(fixture), cancellation.Token);

        Assert.Equal(AstGrepAdapterOutcome.Cancelled, result.Outcome);
        Assert.Equal(AstGrepIssueKind.Cancelled, result.Issue!.Kind);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Cancelled_backend_is_typed_and_discards_partial_data()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        const string rawPartialJson = "[{\"text\":\"class\"}]";
        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(
                    Completed(0, "ast-grep 0.45.0"),
                    Cancelled(rawPartialJson, "cancelled raw diagnostic")))
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.Cancelled, result.Outcome);
        Assert.Equal(AstGrepIssueKind.Cancelled, result.Issue!.Kind);
        Assert.Empty(result.Candidates);
        Assert.DoesNotContain(rawPartialJson, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Match_outside_shared_traversal_scope_is_malformed()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var outside = System.IO.Path.Combine(fixture.Root, "Outside.cs");
        await File.WriteAllTextAsync(outside, "class Outside {}\n");
        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(
                    Completed(0, "ast-grep 0.45.0"),
                    Completed(0, MatchJson(outside, "class", 0, 5, 0, 0, 5))))
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.MalformedOutput, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Parent_relative_backend_path_cannot_alias_a_traversed_file()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var result = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(
                    Completed(0, "ast-grep 0.45.0"),
                    Completed(0, MatchJson("../Source.cs", "class", 0, 5, 0, 0, 5))))
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.MalformedOutput, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Relative_full_path_from_traversal_is_rejected_before_search_invocation()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var relative = new WorkspaceTraversalPath("relative.cs", "Source.cs", false);
        var runner = new StubProcessRunner(Completed(0, "ast-grep 0.45.0"));
        var result = await Adapter(
                fixture,
                new StubTraverser(relative),
                runner)
            .SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.ExecutionFailed, result.Outcome);
        Assert.Equal("structural.traversal_path_invalid", result.Issue!.Code);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Workspace_local_executable_is_rejected_before_version_probe()
    {
        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var runner = new StubProcessRunner(Completed(0, "ast-grep 0.45.0"));
        var adapter = new AstGrepAdapter(
            new StubTraverser(fixture.TraversalPath),
            runner,
            new AstGrepAdapterOptions(
                executablePath: System.IO.Path.Combine(
                    fixture.Root,
                    "tools",
                    "ast-grep")));

        var result = await adapter.SearchAsync(Request(fixture));

        Assert.Equal(AstGrepAdapterOutcome.CapabilityUnavailable, result.Outcome);
        Assert.Equal("structural.ast_grep_executable_unsafe", result.Issue!.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Multi_hop_symlink_to_workspace_executable_is_rejected_before_probe()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SourceFixture("Source.cs", "class Source {}\n");
        var workspaceTools = System.IO.Path.Combine(fixture.Root, "tools");
        Directory.CreateDirectory(workspaceTools);
        var workspaceExecutable = System.IO.Path.Combine(workspaceTools, "ast-grep");
        await File.WriteAllTextAsync(workspaceExecutable, "repository-controlled");
        var aliasRoot = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(fixture.Root)!,
            $"{System.IO.Path.GetFileName(fixture.Root)}-aliases");
        Directory.CreateDirectory(aliasRoot);
        var secondHop = System.IO.Path.Combine(aliasRoot, "second-hop");
        var firstHop = System.IO.Path.Combine(aliasRoot, "first-hop");
        File.CreateSymbolicLink(secondHop, workspaceExecutable);
        File.CreateSymbolicLink(firstHop, secondHop);

        try
        {
            var runner = new StubProcessRunner(Completed(0, "ast-grep 0.45.0"));
            var adapter = new AstGrepAdapter(
                new StubTraverser(fixture.TraversalPath),
                runner,
                new AstGrepAdapterOptions(executablePath: firstHop));

            var result = await adapter.SearchAsync(Request(fixture));

            Assert.Equal(AstGrepAdapterOutcome.CapabilityUnavailable, result.Outcome);
            Assert.Equal("structural.ast_grep_executable_unsafe", result.Issue!.Code);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            File.Delete(firstHop);
            File.Delete(secondHop);
            Directory.Delete(aliasRoot);
        }
    }

    [Fact]
    public void Windows_npm_launchers_resolve_to_the_official_native_executable()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"dnaxi-ast-grep-npm-{Guid.NewGuid():N}");
        var globalBin = System.IO.Path.Combine(root, "npm");
        var globalLauncher = System.IO.Path.Combine(globalBin, "ast-grep.cmd");
        var globalExecutable = System.IO.Path.Combine(
            globalBin,
            "node_modules",
            "@ast-grep",
            "cli",
            "ast-grep.exe");
        var localBin = System.IO.Path.Combine(root, "project", "node_modules", ".bin");
        var localLauncher = System.IO.Path.Combine(localBin, "ast-grep.cmd");
        var localExecutable = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            localBin,
            "..",
            "@ast-grep",
            "cli",
            "ast-grep.exe"));

        Assert.Equal(
            globalExecutable,
            AstGrepAdapter.ResolveExecutableOnPath(
                globalBin,
                isWindows: true,
                fileExists: path => path == globalLauncher || path == globalExecutable));
        Assert.Equal(
            localExecutable,
            AstGrepAdapter.ResolveExecutableOnPath(
                localBin,
                isWindows: true,
                fileExists: path => path == localLauncher || path == localExecutable));
    }

    [Fact]
    public void Path_resolution_skips_relative_and_non_executable_unix_entries()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"dnaxi-ast-grep-path-{Guid.NewGuid():N}");
        var shadowDirectory = System.IO.Path.Combine(root, "shadow");
        var executableDirectory = System.IO.Path.Combine(root, "executable");
        var shadow = System.IO.Path.Combine(shadowDirectory, "ast-grep");
        var executable = System.IO.Path.Combine(executableDirectory, "ast-grep");
        var pathValue = string.Join(
            System.IO.Path.PathSeparator,
            "relative-tools",
            shadowDirectory,
            executableDirectory);

        var resolved = AstGrepAdapter.ResolveExecutableOnPath(
            pathValue,
            isWindows: false,
            fileExists: path => path == shadow || path == executable,
            isExecutable: path => path == executable);

        Assert.Equal(executable, resolved);
    }

    [Fact]
    public async Task Candidate_identity_is_independent_of_ast_grep_pattern_syntax()
    {
        using var fixture = new SourceFixture("Source.cs", "Call();\n");
        var output = MatchJson(fixture.Path, "Call()", 0, 6, 0, 0, 6);
        var first = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(
                    Completed(0, "ast-grep 0.45.0"),
                    Completed(0, output)))
            .SearchAsync(Request(fixture, "Call()"));
        var second = await Adapter(
                fixture,
                new StubTraverser(fixture.TraversalPath),
                new StubProcessRunner(
                    Completed(0, "ast-grep 0.45.0"),
                    Completed(0, output)))
            .SearchAsync(Request(fixture, "$FUNC()"));

        Assert.Equal(
            Assert.Single(first.Candidates).Id,
            Assert.Single(second.Candidates).Id);
    }

    [Fact]
    public void Public_results_do_not_expose_backend_transport_fields()
    {
        var names = typeof(AstGrepSearchResult)
            .GetProperties()
            .Select(static property => property.Name)
            .Concat(typeof(StructuralCandidate).GetProperties().Select(static property => property.Name))
            .ToArray();

        Assert.DoesNotContain(names, static name => name.Contains("Json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, static name => name.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, static name => name.Contains("Exit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, static name => name.Contains("Replacement", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, static name => name.Contains("ByteOffset", StringComparison.OrdinalIgnoreCase));
    }

    private static AstGrepAdapter Adapter(
        SourceFixture fixture,
        IWorkspacePathTraverser traverser,
        IProcessRunner runner) =>
        new(
            traverser,
            runner,
            new AstGrepAdapterOptions(
                executablePath: System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(fixture.Root)!,
                    $"{System.IO.Path.GetFileName(fixture.Root)}-tool",
                    "ast-grep")));

    private static AstGrepSearchRequest Request(
        SourceFixture fixture,
        string pattern = "Call()") =>
        new(pattern, fixture.Traversal);

    private static string MatchJson(
        string file,
        string text,
        int byteStart,
        int byteEnd,
        int line,
        int startColumn,
        int endColumn) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                text,
                range = new
                {
                    byteOffset = new { start = byteStart, end = byteEnd },
                    start = new { line, column = startColumn },
                    end = new { line, column = endColumn },
                },
                file,
                lines = text,
                replacement = "RAW-REWRITE-MUST-NOT-ESCAPE",
            },
        });

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
            Duration);

    private static ProcessRunResult StartFailed(ProcessStartFailure failure) =>
        new(
            ProcessLifecycle.NotStarted,
            ProcessRunOutcome.StartFailed,
            failure,
            exit: null,
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            new ProcessCapturedOutput(string.Empty, limitExceeded: false),
            Duration);

    private static ProcessRunResult Cancelled(
        string standardOutput,
        string standardError) =>
        new(
            ProcessLifecycle.Terminated,
            ProcessRunOutcome.Cancelled,
            ProcessStartFailure.None,
            new ProcessExitEvidence(143, signal: null),
            new ProcessCapturedOutput(standardOutput, limitExceeded: false),
            new ProcessCapturedOutput(standardError, limitExceeded: false),
            Duration);

    private sealed class StubProcessRunner(params ProcessRunResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _results = new(results);

        public List<ProcessRunRequest> Requests { get; } = [];

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class CancellingNoMatchRunner(
        CancellationTokenSource cancellation) : IProcessRunner
    {
        private int _requestCount;

        public ValueTask<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            _requestCount++;
            if (_requestCount == 1)
            {
                return ValueTask.FromResult(Completed(0, "ast-grep 0.45.0"));
            }

            cancellation.Cancel();
            return ValueTask.FromResult(Completed(1, "[]"));
        }
    }

    private sealed class StubTraverser(params WorkspaceTraversalPath[] paths) : IWorkspacePathTraverser
    {
        public List<WorkspaceTraversalRequest> Requests { get; } = [];

        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return paths;
        }
    }

    private sealed class SourceFixture : IDisposable
    {
        public SourceFixture(string fileName, string content)
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dnaxi-ast-grep-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Path = System.IO.Path.Combine(Root, fileName);
            File.WriteAllText(Path, content, new UTF8Encoding(false));
            Traversal = new WorkspaceTraversalRequest(Root);
            TraversalPath = new WorkspaceTraversalPath(Path, fileName, false);
        }

        public string Root { get; }

        public string Path { get; }

        public WorkspaceTraversalRequest Traversal { get; }

        public WorkspaceTraversalPath TraversalPath { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
