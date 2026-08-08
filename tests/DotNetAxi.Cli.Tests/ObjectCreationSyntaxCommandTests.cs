namespace DotNetAxi.Cli.Tests;

public sealed class ObjectCreationSyntaxCommandTests
{
    [Fact]
    public async Task Object_creation_search_handles_supported_and_unresolved_shapes()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Creations.cs",
            """
            class C
            {
                HttpClient First = new HttpClient();
                HttpClient<int> Generic = new HttpClient<int>();
                HttpClient Qualified = new System.Net.Http.HttpClient();
                HttpClient AliasQualified = new global::HttpClient();
                HttpClient[] Array = new HttpClient[3];
                HttpClient TargetTyped = new();
                Other AlsoUnresolved = new /* still target typed */ ();
                Other Other = new Other();
                int[] ImplicitArray = new[] { 1 };
                object Anonymous = new { Name = "anonymous" };
                object Factory = HttpClient.Create();
            }
            """);
        await workspace.WriteAsync(
            "Malformed.cs",
            "class Broken { object M() => new HttpClient(");

        var result = await workspace.RunAsync(
            "search", "syntax", "object-creation", "--type", "HttpClient", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: search syntax object-creation", result.Output);
        Assert.Contains("resolution: syntax", result.Output);
        Assert.Contains("confidence: candidate", result.Output);
        Assert.Contains("count: 8", result.Output);
        Assert.Contains("total: 8", result.Output);
        Assert.Contains("Creations.cs", result.Output);
        Assert.Contains("Malformed.cs", result.Output);
        Assert.Contains(
            "matches[8]{id,file,line,construct,type_match}:",
            result.Output);
        Assert.Contains(",object-creation,exact", result.Output);
        Assert.Contains(",object-creation,unresolved", result.Output);
    }

    [Fact]
    public async Task Object_creation_search_respects_path_and_generated_scope()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "selected/Selected.cs",
            "class A { object Value = new Widget(); }");
        await workspace.WriteAsync(
            "other/Other.cs",
            "class B { object Value = new Widget(); }");
        await workspace.WriteAsync(
            "selected/Generated.g.cs",
            "class G { object Value = new Widget(); }");

        var normal = await workspace.RunAsync(
            "search", "syntax", "object-creation", "--type", "Widget",
            "--path", "selected", "--full");
        var generated = await workspace.RunAsync(
            "search", "syntax", "object-creation", "--type", "Widget",
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
    public async Task Object_creation_search_bounds_before_projection_and_preserves_query()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Creations.cs",
            "class C { object A = new Widget(); object B = new Widget(); }");

        var bounded = await workspace.RunAsync(
            "search", "syntax", "object-creation", "--type", "Widget",
            "--limit", "1", "--fields", "column");

        Assert.Equal(0, bounded.ExitCode);
        Assert.Contains("count: 1", bounded.Output);
        Assert.Contains("total_known: true", bounded.Output);
        Assert.Contains("total: 2", bounded.Output);
        Assert.Contains("omitted: 1", bounded.Output);
        Assert.Contains("truncated: true", bounded.Output);
        Assert.Contains(
            "matches[1]{id,file,line,construct,type_match,column}:",
            bounded.Output);
        Assert.Contains("retrieval_command:", bounded.Output);
        Assert.Contains(
            $"dnx dnaxi@{ToolVersion.Current} --verbosity quiet -- search syntax object-creation",
            bounded.Output);
        Assert.Contains("--type 'Widget' --fields 'column' --full", bounded.Output);
        Assert.DoesNotContain("--limit", bounded.Output);
    }

    [Fact]
    public async Task Empty_object_creation_search_is_a_successful_structured_result()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("Empty.cs", "class C { }");

        var result = await workspace.RunAsync(
            "search", "syntax", "object-creation", "--type", "Missing");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("count: 0", result.Output);
        Assert.Contains("total: 0", result.Output);
        Assert.Contains("matches: []", result.Output);
    }

    [Fact]
    public async Task Object_creation_help_is_passive_and_documents_unresolved_candidates()
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(
            ["search", "syntax", "object-creation", "--help"]);

        Assert.Equal(0, exit);
        Assert.Contains("terminal type name", output.ToString());
        Assert.Contains("target-typed new()", output.ToString());
        Assert.Contains("unresolved syntax candidate", output.ToString());
        Assert.Contains("--include-generated", output.ToString());
        Assert.Contains("--path", output.ToString());
    }

    [Theory]
    [InlineData("search", "syntax", "object-creation")]
    [InlineData("search", "syntax", "object-creation", "--type", "")]
    [InlineData("search", "syntax", "object-creation", "--type", "Widget", "--limit", "-1")]
    [InlineData("search", "syntax", "object-creation", "--type", "Widget", "--full", "--limit", "1")]
    [InlineData("search", "syntax", "object-creation", "--type", "Widget", "--path", "")]
    [InlineData("search", "syntax", "object-creation", "--type", "Widget", "--fields", "unknown")]
    public async Task Invalid_object_creation_requests_are_structured_usage_errors(
        params string[] arguments)
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(arguments);

        Assert.Equal(2, exit);
        Assert.Contains("command: search syntax object-creation", output.ToString());
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("usage.", output.ToString());
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-object-creation-cli-tests",
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
