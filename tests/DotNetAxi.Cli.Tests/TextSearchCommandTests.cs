namespace DotNetAxi.Cli.Tests;

public sealed class TextSearchCommandTests
{
    [Fact]
    public async Task Successful_default_result_omits_implied_and_empty_diagnostics()
    {
        var workspace = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "Sample.cs"),
                "class Sample { // needle\n}");

            var result = await RunAsync(
                workspace,
                "search", "text", "needle", "--full");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("status: success", result.Output);
            Assert.Contains("count: 1", result.Output);
            Assert.Contains("items[1]{file,line,preview}", result.Output);
            Assert.DoesNotContain("text/v1/", result.Output);
            Assert.DoesNotContain("confidence:", result.Output);
            Assert.DoesNotContain("skipped:", result.Output);
            Assert.DoesNotContain("changed_coverage:", result.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Regex_search_uses_dotnet_patterns_and_command_case_mode()
    {
        var workspace = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "pattern.cs"),
                "before\nFOOFOO");
            const string query = @"(?<word>foo)\k<word>";

            var insensitive = await RunAsync(
                workspace,
                "search", "text", query, "--regex", "--full");
            var sensitive = await RunAsync(
                workspace,
                "search", "text", query, "--regex", "--case-sensitive", "--full");

            Assert.Equal(0, insensitive.ExitCode);
            Assert.Contains("pattern.cs", insensitive.Output);
            Assert.Contains("FOOFOO", insensitive.Output);
            Assert.Contains("total: 1", insensitive.Output);
            Assert.Equal(0, sensitive.ExitCode);
            Assert.Contains("total: 0", sensitive.Output);
            Assert.DoesNotContain("pattern.cs", sensitive.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_regex_is_a_structured_query_error_without_exception_details()
    {
        var workspace = CreateWorkspace();
        try
        {
            var result = await RunAsync(
                workspace,
                "search", "text", "[invalid", "--regex");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("status: failed", result.Output);
            Assert.Contains("code: search.regex_invalid", result.Output);
            Assert.Contains("query `[invalid`", result.Output);
            Assert.DoesNotContain("internal.unhandled", result.Output);
            Assert.DoesNotContain("RegexParseException", result.Output);
            Assert.DoesNotContain("System.Text.RegularExpressions", result.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Regex_timeout_is_structured_and_scanning_continues_in_the_cli()
    {
        var workspace = CreateWorkspace();
        try
        {
            var catastrophicInput = new string('a', 100_000) + "!";
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "a-catastrophic.txt"),
                catastrophicInput);
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "z-later.txt"),
                "aaaa");
            const string query = "^(a+)+$";

            var result = await RunAsync(
                workspace,
                "search", "text", query, "--regex", "--full");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("status: partial", result.Output);
            Assert.Contains("code: search.regex_timeout", result.Output);
            Assert.Contains($"query `{query}`", result.Output);
            Assert.Contains("a-catastrophic.txt", result.Output);
            Assert.Contains("z-later.txt", result.Output);
            Assert.Contains(
                "Narrow the expression or file scope and run the search again.",
                result.Output);
            Assert.DoesNotContain(new string('a', 128), result.Output);
            Assert.DoesNotContain("RegexMatchTimeoutException", result.Output);
            Assert.DoesNotContain("System.Text.RegularExpressions", result.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Text_help_documents_regex_semantics_and_remains_passive()
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(["search", "text", "--help"]);

        Assert.Equal(0, exit);
        Assert.Contains("regular-expression text", output.ToString());
        Assert.Contains(".NET regular-expression language", output.ToString());
        Assert.Contains("--regex", output.ToString());
        Assert.Contains(
            "Compact: --fields id,external. Repeated: --fields id --fields external. Multi-value: --fields id external.",
            output.ToString());
    }

    [Theory]
    [InlineData("search", "text", "needle", "--full", "--limit", "100")]
    [InlineData("search", "text", "needle", "--path", "")]
    [InlineData("search", "text", "needle", "--fields", "")]
    public async Task Invalid_text_requests_are_structured_usage_errors_before_workspace_work(params string[] args)
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(args);

        Assert.Equal(2, exit);
        Assert.Contains("command: search text\n", output.ToString());
        Assert.Contains("status: failed\n", output.ToString());
        Assert.Contains("usage.", output.ToString());
    }

    [Fact]
    public async Task Unknown_fields_use_the_canonical_usage_error_with_all_valid_fields()
    {
        var workspace = CreateWorkspace();
        try
        {
            var result = await RunAsync(workspace, "search", "text", "needle", "--fields", "unknown");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("code: usage.unknown_field", result.Output);
            Assert.Contains("valid_fields[7]", result.Output);
            Assert.Contains("skip_details", result.Output);
            Assert.Contains("column", result.Output);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    [Fact]
    public async Task Bounded_result_exposes_a_selector_preserving_full_recovery_command_and_opt_in_skip_details()
    {
        var workspace = CreateWorkspace();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "one.cs"), "needle");
            await File.WriteAllTextAsync(Path.Combine(workspace, "two.cs"), "needle");
            await File.WriteAllBytesAsync(Path.Combine(workspace, "bad.bin"), [0, 1]);

            var bounded = await RunAsync(workspace, "search", "text", "needle", "--limit", "1", "--fields", " id, column,skip_details ");
            var full = await RunAsync(workspace, "search", "text", "needle", "--full", "--fields", "id", "column", "skip_details");

            Assert.Equal(0, bounded.ExitCode);
            Assert.Contains("status: partial", bounded.Output);
            Assert.Contains("retrieval_command:", bounded.Output);
            Assert.Contains(
                $"dnx dnaxi@{ToolVersion.Current} --verbosity quiet -- search text",
                bounded.Output);
            Assert.Contains("--fields 'id,column,skip_details' --full", bounded.Output);
            Assert.DoesNotContain("--limit", bounded.Output);
            Assert.Contains("text/v1/", bounded.Output);
            Assert.Contains("details:", bounded.Output);
            Assert.Contains("totals_known: false", bounded.Output);
            Assert.Equal(0, full.ExitCode);
            Assert.Contains("status: success", full.Output);
            Assert.Contains("totals_known: true", full.Output);
            Assert.Contains("total: 2", full.Output);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    [Fact]
    public async Task Project_selector_means_project_directory_scope_before_compilation()
    {
        var workspace = CreateWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(workspace, "selected"));
            Directory.CreateDirectory(Path.Combine(workspace, "other"));
            await File.WriteAllTextAsync(Path.Combine(workspace, "selected", "Selected.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            await File.WriteAllTextAsync(Path.Combine(workspace, "selected", "Hit.cs"), "needle");
            await File.WriteAllTextAsync(Path.Combine(workspace, "other", "Miss.cs"), "needle");

            var result = await RunAsync(workspace, "search", "text", "needle", "--project", "selected/Selected.csproj", "--full");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("selected/Hit.cs", result.Output);
            Assert.DoesNotContain("other/Miss.cs", result.Output);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    [Fact]
    public async Task Skip_details_are_bounded_normally_and_full_returns_every_detail_with_exclusive_reasons()
    {
        var workspace = CreateWorkspace();
        try
        {
            for (var index = 0; index < 51; index++)
            {
                await File.WriteAllBytesAsync(Path.Combine(workspace, $"invalid-{index:D2}.txt"), [0xff]);
            }

            await File.WriteAllBytesAsync(Path.Combine(workspace, "utf32.txt"), [0xff, 0xfe, 0, 0, 65, 0, 0, 0]);
            var bounded = await RunAsync(workspace, "search", "text", "missing", "--fields", "skip_details");
            var full = await RunAsync(workspace, "search", "text", "missing", "--fields", "skip_details", "--full");

            Assert.Contains("count: 50", bounded.Output);
            Assert.Contains("total: 52", bounded.Output);
            Assert.Contains("truncated: true", bounded.Output);
            Assert.Contains("count: 52", full.Output);
            Assert.Contains("total: 52", full.Output);
            Assert.Contains("truncated: false", full.Output);
            Assert.Contains("undecodable: 51", full.Output);
            Assert.Contains("unsupported_encoding: 1", full.Output);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    [Fact]
    public async Task Changed_scope_reports_conflicts_and_traversal_exclusions_as_explicit_coverage_observations()
    {
        var workspace = CreateWorkspace();
        try
        {
            await GitAsync(workspace, "init");
            await GitAsync(workspace, "config", "user.email", "text@example.test");
            await GitAsync(workspace, "config", "user.name", "Text Test");
            await File.WriteAllTextAsync(Path.Combine(workspace, ".gitignore"), "ignored.txt\n");
            await File.WriteAllTextAsync(Path.Combine(workspace, "ignored.txt"), "needle base");
            await File.WriteAllTextAsync(Path.Combine(workspace, "Generated.g.cs"), "needle generated base");
            await File.WriteAllTextAsync(Path.Combine(workspace, "conflict.cs"), "needle base");
            await GitAsync(workspace, "add", ".");
            await GitAsync(workspace, "commit", "-m", "base");
            var branch = await GitOutputAsync(workspace, "branch", "--show-current");
            await GitAsync(workspace, "checkout", "-b", "other");
            await File.WriteAllTextAsync(Path.Combine(workspace, "conflict.cs"), "needle theirs");
            await GitAsync(workspace, "commit", "-am", "theirs");
            await GitAsync(workspace, "checkout", branch.Trim());
            await File.WriteAllTextAsync(Path.Combine(workspace, "conflict.cs"), "needle ours");
            await GitAsync(workspace, "commit", "-am", "ours");
            await GitAsync(workspace, expectedExitCode: 1, "merge", "other");
            await File.WriteAllTextAsync(Path.Combine(workspace, "ignored.txt"), "needle changed");
            await File.WriteAllTextAsync(Path.Combine(workspace, "Generated.g.cs"), "needle generated changed");

            var result = await RunAsync(workspace, "search", "text", "needle", "--changed", "--full");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("conflicted", result.Output);
            Assert.Contains("generated", result.Output);
            Assert.Contains("changed_coverage", result.Output);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotnet-axi-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string workingDirectory, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Cli.Program).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(string.IsNullOrEmpty(error), error);
        return (process.ExitCode, output);
    }

    private static async Task GitAsync(string workingDirectory, params string[] args) =>
        _ = await GitOutputAsync(workingDirectory, 0, args);

    private static async Task GitAsync(string workingDirectory, int expectedExitCode, params string[] args) =>
        _ = await GitOutputAsync(workingDirectory, expectedExitCode, args);

    private static async Task<string> GitOutputAsync(string workingDirectory, params string[] args) =>
        await GitOutputAsync(workingDirectory, 0, args);

    private static async Task<string> GitOutputAsync(string workingDirectory, int expectedExitCode, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo { FileName = "git", WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("commit.gpgsign=false");
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(expectedExitCode, process.ExitCode);
        return output;
    }
}
