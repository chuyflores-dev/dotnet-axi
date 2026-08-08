namespace DotNetAxi.Cli.Tests;

public sealed class FileSearchCommandTests
{
    [Theory]
    [InlineData("search", "file", "query", "--limit", "-1")]
    [InlineData("search", "file", "query", "--path", "")]
    [InlineData("search", "file", "query", "--extension", ".")]
    [InlineData("search", "file", "query", "--glob", "")]
    [InlineData("search", "file", "query", "--fields", "")]
    public async Task Invalid_file_requests_are_structured_usage_errors(
        params string[] args)
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(args);

        Assert.Equal(2, exit);
        Assert.Contains("command: search file\n", output.ToString());
        Assert.Contains("status: failed\n", output.ToString());
        Assert.Contains("usage.", output.ToString());
    }

    [Fact]
    public async Task Malformed_glob_is_a_structured_usage_error()
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(
            ["search", "file", "file", "--glob", "[!]"]);

        Assert.Equal(2, exit);
        Assert.Contains("code: usage.file_glob", output.ToString());
        Assert.DoesNotContain("internal.unhandled", output.ToString());
    }

    [Fact]
    public async Task Default_rows_are_ranked_and_use_the_documented_fields()
    {
        var workspace = CreateWorkspace();
        try
        {
            await WriteAsync(workspace, "Root.csproj", Project());
            await WriteAsync(workspace, "Widget", "plain");
            await WriteAsync(workspace, "src/Widget.cs", "source");
            await WriteAsync(workspace, "src/WidgetTests.cs", "source");
            await WriteAsync(workspace, "docs/MyWidget.md", "text");
            await WriteAsync(workspace, "src/widget/Other.cs", "source");

            var result = await RunAsync(
                workspace,
                "search",
                "file",
                "widget");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("status: success", result.Output);
            Assert.Contains("count: 5", result.Output);
            Assert.Contains(
                "files[5]{id,path,kind,owning_project_count}",
                result.Output);
            AssertBefore(result.Output, ",Widget,file,1", ",src/Widget.cs,source,1");
            AssertBefore(result.Output, ",src/Widget.cs,source,1", ",src/WidgetTests.cs,source,1");
            AssertBefore(result.Output, ",src/WidgetTests.cs,source,1", ",docs/MyWidget.md,file,1");
            AssertBefore(result.Output, ",docs/MyWidget.md,file,1", ",src/widget/Other.cs,source,1");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Case_extension_glob_path_and_generated_flags_compose()
    {
        var workspace = CreateWorkspace();
        try
        {
            await WriteAsync(workspace, "Root.csproj", Project());
            await WriteAsync(workspace, "src/Alpha.cs", "source");
            await WriteAsync(workspace, "src/alpha.md", "text");
            await WriteAsync(workspace, "src/Alpha.g.cs", "generated");
            await WriteAsync(workspace, "tests/AlphaTests.CS", "source");
            await WriteAsync(workspace, "other/Alpha.cs", "source");

            var sensitive = await RunAsync(
                workspace,
                "search",
                "file",
                "Alpha",
                "--case-sensitive",
                "--extension",
                "cs",
                "--glob",
                "src/**",
                "--glob",
                "tests/**",
                "--path",
                "src",
                "--path",
                "tests");
            var inclusive = await RunAsync(
                workspace,
                "search",
                "file",
                "alpha",
                "--extension",
                ".cs",
                "--extension",
                "md",
                "--glob",
                "src/**",
                "--glob",
                "tests/**",
                "--path",
                "src",
                "--path",
                "tests",
                "--include-generated");

            Assert.Equal(0, sensitive.ExitCode);
            Assert.Contains("count: 1", sensitive.Output);
            Assert.Contains("src/Alpha.cs", sensitive.Output);
            Assert.DoesNotContain("src/alpha.md", sensitive.Output);
            Assert.DoesNotContain("Alpha.g.cs", sensitive.Output);
            Assert.DoesNotContain("AlphaTests.CS", sensitive.Output);
            Assert.Equal(0, inclusive.ExitCode);
            Assert.Contains("count: 4", inclusive.Output);
            Assert.Contains("src/Alpha.cs", inclusive.Output);
            Assert.Contains("src/alpha.md", inclusive.Output);
            Assert.Contains("src/Alpha.g.cs", inclusive.Output);
            Assert.Contains("tests/AlphaTests.CS", inclusive.Output);
            Assert.DoesNotContain("other/Alpha.cs", inclusive.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Project_scope_and_multi_ownership_are_deterministic()
    {
        var workspace = CreateWorkspace();
        try
        {
            await WriteAsync(workspace, "Root.csproj", Project());
            await WriteAsync(workspace, "src/Nested/Nested.csproj", Project());
            await WriteAsync(workspace, "src/Nested/Shared.cs", "source");
            await WriteAsync(workspace, "other/Shared.cs", "source");

            var result = await RunAsync(
                workspace,
                "search",
                "file",
                "Shared",
                "--project",
                "src/Nested/Nested.csproj",
                "--fields",
                "owning_projects");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("count: 1", result.Output);
            Assert.Equal(1, Occurrences(result.Output, "src/Nested/Shared.cs"));
            Assert.Contains("owning_project_count: 2", result.Output);
            Assert.Contains("Root.csproj", result.Output);
            Assert.Contains("src/Nested/Nested.csproj", result.Output);
            Assert.DoesNotContain("other/Shared.cs", result.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Changed_scope_intersects_traversal_and_generated_policy()
    {
        var workspace = CreateWorkspace();
        try
        {
            await GitAsync(workspace, "init");
            await GitAsync(workspace, "config", "user.email", "files@example.test");
            await GitAsync(workspace, "config", "user.name", "File Search Test");
            await WriteAsync(workspace, "Root.csproj", Project());
            await WriteAsync(workspace, "Changed.cs", "before");
            await WriteAsync(workspace, "Unchanged.cs", "same");
            await WriteAsync(workspace, "Generated.g.cs", "before");
            await GitAsync(workspace, "add", ".");
            await GitAsync(workspace, "commit", "-m", "base");
            await WriteAsync(workspace, "Changed.cs", "after");
            await WriteAsync(workspace, "Generated.g.cs", "after");
            await WriteAsync(workspace, "New.cs", "new");

            var normal = await RunAsync(
                workspace,
                "search",
                "file",
                ".cs",
                "--changed");
            var generated = await RunAsync(
                workspace,
                "search",
                "file",
                ".cs",
                "--changed",
                "--include-generated");

            Assert.Equal(0, normal.ExitCode);
            Assert.Contains("count: 2", normal.Output);
            Assert.Contains("Changed.cs", normal.Output);
            Assert.Contains("New.cs", normal.Output);
            Assert.DoesNotContain("Unchanged.cs", normal.Output);
            Assert.DoesNotContain("Generated.g.cs", normal.Output);
            Assert.Equal(0, generated.ExitCode);
            Assert.Contains("count: 3", generated.Output);
            Assert.Contains("Generated.g.cs", generated.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Limit_has_a_selector_preserving_recovery_command_and_empty_is_success()
    {
        var workspace = CreateWorkspace();
        try
        {
            await WriteAsync(workspace, "Root.csproj", Project());
            await WriteAsync(workspace, "src/MatchA.cs", "source");
            await WriteAsync(workspace, "src/MatchB.cs", "source");

            var limited = await RunAsync(
                workspace,
                "search",
                "file",
                "Match",
                "--extension",
                "cs",
                "--path",
                "src",
                "--fields",
                "external",
                "--limit",
                "1");
            var empty = await RunAsync(
                workspace,
                "search",
                "file",
                "Missing");

            Assert.Equal(0, limited.ExitCode);
            Assert.Contains("count: 1", limited.Output);
            Assert.Contains("total: 2", limited.Output);
            Assert.Contains("omitted: 1", limited.Output);
            Assert.Contains("truncated: true", limited.Output);
            Assert.Contains(
                $"dnx dnaxi@{ToolVersion.Current} --verbosity quiet -- search file",
                limited.Output);
            Assert.Contains("--extension 'cs'", limited.Output);
            Assert.Contains("--path 'src'", limited.Output);
            Assert.Contains("--fields 'external'", limited.Output);
            Assert.Contains("--limit 2", limited.Output);
            Assert.Equal(0, empty.ExitCode);
            Assert.Contains("status: success", empty.Output);
            Assert.Contains("count: 0", empty.Output);
            Assert.Contains("total: 0", empty.Output);
            Assert.Contains("files: []", empty.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_fields_use_the_canonical_usage_error()
    {
        var workspace = CreateWorkspace();
        try
        {
            var result = await RunAsync(
                workspace,
                "search",
                "file",
                "file",
                "--fields",
                "unknown");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("code: usage.unknown_field", result.Output);
            Assert.Contains("valid_fields[6]", result.Output);
            Assert.Contains("owning_projects", result.Output);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string Project() =>
        "<Project Sdk=\"Microsoft.NET.Sdk\" />";

    private static async Task WriteAsync(
        string root,
        string relativePath,
        string content)
    {
        var path = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private static void AssertBefore(
        string value,
        string first,
        string second)
    {
        var firstIndex = value.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = value.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Missing expected value: {first}");
        Assert.True(secondIndex >= 0, $"Missing expected value: {second}");
        Assert.True(
            firstIndex < secondIndex,
            $"Expected `{first}` before `{second}`.\n{value}");
    }

    private static int Occurrences(string value, string expected)
    {
        var count = 0;
        for (var index = 0;
             (index = value.IndexOf(
                 expected,
                 index,
                 StringComparison.Ordinal)) >= 0;
             index += expected.Length)
        {
            count++;
        }

        return count;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string workingDirectory,
        params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Cli.Program).Assembly.Location);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(string.IsNullOrEmpty(error), error);
        return (process.ExitCode, output);
    }

    private static async Task GitAsync(
        string workingDirectory,
        params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("commit.gpgsign=false");
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', args)} failed.\n{output}\n{error}");
    }
}
