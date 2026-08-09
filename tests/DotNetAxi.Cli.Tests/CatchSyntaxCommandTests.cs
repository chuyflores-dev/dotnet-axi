namespace DotNetAxi.Cli.Tests;

public sealed class CatchSyntaxCommandTests
{
    [Fact]
    public async Task Catch_search_handles_typed_untyped_filtered_and_malformed_shapes()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Catches.cs",
            """
            class C
            {
                void M()
                {
                    try { } catch { }
                    try { } catch (Exception) { }
                    try { } catch (System.Exception ex) when (ex is not null) { Handle(); }
                    try { } catch (IOException) { }
                }
            }
            """);
        await workspace.WriteAsync(
            "Malformed.cs",
            "class Broken { void M() { try { } catch (Exception ex) {");

        var all = await workspace.RunAsync(
            "search", "syntax", "catch", "--full");
        var typed = await workspace.RunAsync(
            "search", "syntax", "catch", "--type", "Exception", "--full");

        Assert.Equal(0, all.ExitCode);
        Assert.Contains("command: search syntax catch", all.Output);
        Assert.Contains("resolution: syntax", all.Output);
        Assert.DoesNotContain("confidence:", all.Output);
        Assert.Contains("count: 5", all.Output);
        Assert.Contains("matches[5]{file,line}:", all.Output);
        Assert.DoesNotContain("syntax/v1/", all.Output);
        Assert.Equal(0, typed.ExitCode);
        Assert.Contains("count: 3", typed.Output);
        Assert.Contains("total: 3", typed.Output);
        Assert.Contains("Catches.cs", typed.Output);
        Assert.Contains("Malformed.cs", typed.Output);
    }

    [Fact]
    public async Task Empty_filter_counts_parsed_statements_and_combines_with_type()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Catches.cs",
            """
            class C
            {
                void M()
                {
                    try { } catch { }
                    try { } catch (Exception) { /* comment-only */ }
                    try { } catch (Exception ex) when (ex is not null) { }
                    try { } catch (Exception) { ; }
                    try { } catch (Exception) { Handle(); }
                }
            }
            """);

        var empty = await workspace.RunAsync(
            "search", "syntax", "catch", "--empty", "--full");
        var typedEmpty = await workspace.RunAsync(
            "search", "syntax", "catch", "--type", "Exception", "--empty", "--full");

        Assert.Equal(0, empty.ExitCode);
        Assert.Contains("count: 3", empty.Output);
        Assert.Equal(0, typedEmpty.ExitCode);
        Assert.Contains("count: 2", typedEmpty.Output);
    }

    [Fact]
    public async Task Catch_search_respects_path_and_generated_scope()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "selected/Selected.cs",
            "class A { void M() { try { } catch (Exception) { } } }");
        await workspace.WriteAsync(
            "other/Other.cs",
            "class B { void M() { try { } catch (Exception) { } } }");
        await workspace.WriteAsync(
            "selected/Generated.g.cs",
            "class G { void M() { try { } catch (Exception) { } } }");

        var normal = await workspace.RunAsync(
            "search", "syntax", "catch", "--type", "Exception",
            "--path", "selected", "--full");
        var generated = await workspace.RunAsync(
            "search", "syntax", "catch", "--type", "Exception",
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
    public async Task Catch_search_bounds_before_projection_and_preserves_filters()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Catches.cs",
            "class C { void M() { try { } catch (Exception) { } try { } catch (Exception) { } } }");

        var bounded = await workspace.RunAsync(
            "search", "syntax", "catch", "--type", "Exception", "--empty",
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
            $"dnx dnaxi@{ToolVersion.Current} --verbosity quiet -- search syntax catch",
            bounded.Output);
        Assert.Contains(
            "--type 'Exception' --empty --fields 'id' 'construct' 'column' --full",
            bounded.Output);
        Assert.DoesNotContain("--limit", bounded.Output);
    }

    [Fact]
    public async Task Empty_catch_search_is_a_successful_structured_result()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Empty.cs",
            "class C { void M() { try { } catch (IOException) { Handle(); } } }");

        var result = await workspace.RunAsync(
            "search", "syntax", "catch", "--type", "Exception");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("count: 0", result.Output);
        Assert.Contains("total: 0", result.Output);
        Assert.Contains("matches: []", result.Output);
    }

    [Fact]
    public async Task Catch_help_is_passive_and_documents_filter_semantics()
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(["search", "syntax", "catch", "--help"]);

        Assert.Equal(0, exit);
        Assert.Contains("untyped catches", output.ToString());
        Assert.Contains("no parsed statements", output.ToString());
        Assert.Contains("--type", output.ToString());
        Assert.Contains("--empty", output.ToString());
        Assert.Contains("--include-generated", output.ToString());
        Assert.Contains("--path", output.ToString());
    }

    [Theory]
    [InlineData("search", "syntax", "catch", "--type", "")]
    [InlineData("search", "syntax", "catch", "--limit", "-1")]
    [InlineData("search", "syntax", "catch", "--full", "--limit", "1")]
    [InlineData("search", "syntax", "catch", "--path", "")]
    [InlineData("search", "syntax", "catch", "--fields", "unknown")]
    public async Task Invalid_catch_requests_are_structured_usage_errors(
        params string[] arguments)
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(arguments);

        Assert.Equal(2, exit);
        Assert.Contains("command: search syntax catch", output.ToString());
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("usage.", output.ToString());
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-catch-cli-tests",
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
