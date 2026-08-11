namespace DotNetAxi.Cli.Tests;

public sealed class AttributedClassSyntaxCommandTests
{
    [Fact]
    public async Task Class_search_handles_supported_shapes_and_false_candidates()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Attributes.cs",
            """
            [assembly: Authorize]

            [Authorize]
            class Direct { }

            [Security.Authorize]
            static class Qualified { }

            [AuthorizeAttribute, Obsolete]
            partial class Suffixed { }

            [type: global::Security.AuthorizeAttribute]
            class Targeted { }

            [Authorize, AuthorizeAttribute]
            class Multiple { }

            [authorize]
            class WrongCase { }

            [Other]
            class Other { }

            [Authorize]
            record RecordCandidate;

            [Authorize]
            struct StructCandidate { }
            """);
        await workspace.WriteAsync(
            "Malformed.cs",
            """
            [Authorize
            class Broken { }
            """);

        var result = await workspace.RunAsync(
            "search", "syntax", "class", "--attribute", "Authorize", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: search syntax class", result.Output);
        Assert.Contains("resolution: syntax", result.Output);
        Assert.DoesNotContain("confidence:", result.Output);
        Assert.Contains("count: 6", result.Output);
        Assert.Contains("total: 6", result.Output);
        Assert.Contains("Attributes.cs", result.Output);
        Assert.Contains("Malformed.cs", result.Output);
        Assert.Contains("matches[6]{file,line}:", result.Output);
        Assert.Contains("Attributes.cs,4", result.Output);
        Assert.DoesNotContain("syntax/v1/", result.Output);
    }

    [Fact]
    public async Task Class_search_respects_path_and_generated_scope()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "selected"));
        Directory.CreateDirectory(Path.Combine(workspace.Root, "other"));
        await workspace.WriteAsync(
            "selected/Selected.cs",
            "[Authorize] class Selected { }");
        await workspace.WriteAsync(
            "other/Other.cs",
            "[Authorize] class Other { }");
        await workspace.WriteAsync(
            "selected/Generated.g.cs",
            "[Authorize] class Generated { }");

        var normal = await workspace.RunAsync(
            "search", "syntax", "class", "--attribute", "Authorize",
            "--path", "selected", "--full");
        var generated = await workspace.RunAsync(
            "search", "syntax", "class", "--attribute", "Authorize",
            "--path", "selected", "--include-generated", "--full");

        Assert.Equal(0, normal.ExitCode);
        Assert.Contains("count: 1", normal.Output);
        Assert.Contains("selected/Selected.cs", normal.Output);
        Assert.DoesNotContain("other/Other.cs", normal.Output);
        Assert.DoesNotContain("Generated.g.cs", normal.Output);
        Assert.Equal(0, generated.ExitCode);
        Assert.Contains("count: 2", generated.Output);
        Assert.Contains("selected/Generated.g.cs", generated.Output);
    }

    [Fact]
    public async Task Class_search_bounds_output_and_preserves_the_query_in_retrieval()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Attributes.cs",
            "[Authorize] class First { } [Authorize] class Second { }");

        var bounded = await workspace.RunAsync(
            "search", "syntax", "class", "--attribute", "Authorize",
            "--limit", "1", "--fields", "id", "construct", "column");

        Assert.Equal(0, bounded.ExitCode);
        Assert.Contains("count: 1", bounded.Output);
        Assert.Contains("total_known: true", bounded.Output);
        Assert.Contains("total: 2", bounded.Output);
        Assert.Contains("omitted: 1", bounded.Output);
        Assert.Contains("truncated: true", bounded.Output);
        Assert.Contains("matches[1]{id,file,line,construct,column}:", bounded.Output);
        Assert.Contains("retrieval_command:", bounded.Output);
        Assert.Contains(
            $"dnx dnaxi@{ToolVersion.Current} --verbosity quiet -- search syntax class",
            bounded.Output);
        Assert.Contains("--attribute 'Authorize' --fields 'id,construct,column' --full", bounded.Output);
        Assert.DoesNotContain("--limit", bounded.Output);
    }

    [Fact]
    public async Task Empty_class_search_is_a_successful_structured_result()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("Empty.cs", "class C { }");

        var result = await workspace.RunAsync(
            "search", "syntax", "class", "--attribute", "Missing");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("count: 0", result.Output);
        Assert.Contains("total: 0", result.Output);
        Assert.Contains("matches: []", result.Output);
    }

    [Fact]
    public async Task Class_help_is_passive_and_documents_candidate_semantics()
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(
            ["search", "syntax", "class", "--help"]);

        Assert.Equal(0, exit);
        Assert.Contains("syntax candidates", output.ToString());
        Assert.Contains("optional Attribute suffix", output.ToString());
        Assert.Contains("--include-generated", output.ToString());
        Assert.Contains("--path", output.ToString());
    }

    [Theory]
    [InlineData("search", "syntax", "class")]
    [InlineData("search", "syntax", "class", "--attribute", "")]
    [InlineData("search", "syntax", "class", "--attribute", "Authorize", "--limit", "-1")]
    [InlineData("search", "syntax", "class", "--attribute", "Authorize", "--full", "--limit", "1")]
    [InlineData("search", "syntax", "class", "--attribute", "Authorize", "--path", "")]
    [InlineData("search", "syntax", "class", "--attribute", "Authorize", "--fields", "unknown")]
    public async Task Invalid_class_requests_are_structured_usage_errors(
        params string[] arguments)
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(arguments);

        Assert.Equal(2, exit);
        Assert.Contains("command: search syntax class", output.ToString());
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("usage.", output.ToString());
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-attributed-class-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        public async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(typeof(Cli.Program).Assembly.Location);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                string.IsNullOrEmpty(error),
                $"Expected empty stderr, got: {error}");
            return (process.ExitCode, output);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
