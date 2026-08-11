using System.Text;
using System.Text.RegularExpressions;

namespace DotNetAxi.Cli.Tests;

public sealed class OutlineCommandTests
{
    [Fact]
    public async Task Document_outline_reports_identity_ownership_structure_and_locations()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Source.cs",
            "using System;\n"
            + "namespace Demo;\n"
            + "[Obsolete]\n"
            + "public class Outer\n"
            + "{\n"
            + "    int Field = 1;\n"
            + "    void Run() { Console.WriteLine(Field); }\n"
            + "    class Nested { }\n"
            + "}\n");

        var result = await workspace.RunAsync("outline", "Source.cs", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: outline", result.Output);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("resolution: syntax", result.Output);
        Assert.Contains("coverage: complete", result.Output);
        Assert.Contains("target_kind: document", result.Output);
        Assert.Matches("id: file/v1/[a-f0-9]{64}", result.Output);
        Assert.Contains("path: Source.cs", result.Output);
        Assert.Contains("owning_project_count: 1", result.Output);
        Assert.Contains("owning_projects[1]: App.csproj", result.Output);
        Assert.Contains("diagnostic_count: 0", result.Output);
        Assert.Contains("truncated: false", result.Output);
        Assert.Contains("kind: import", result.Output);
        Assert.Contains("kind: namespace", result.Output);
        Assert.Contains("name: Outer", result.Output);
        Assert.Contains("[Obsolete]", result.Output);
        Assert.Contains("signature: int Field;", result.Output);
        Assert.Contains("signature: void Run();", result.Output);
        Assert.DoesNotContain("Console.WriteLine", result.Output);
        Assert.True(
            result.Output.IndexOf("name: Outer", StringComparison.Ordinal)
            < result.Output.IndexOf("name: Field", StringComparison.Ordinal));
        Assert.True(
            result.Output.IndexOf("name: Field", StringComparison.Ordinal)
            < result.Output.IndexOf("name: Run", StringComparison.Ordinal));
        Assert.Contains("start: Source.cs,3,1,false", result.Output);
    }

    [Fact]
    public async Task Symbol_outline_contains_only_the_resolved_declaration_scope()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Source.cs",
            "using System;\n"
            + "namespace Demo;\n"
            + "class Other { }\n"
            + "class Selected { int Field; void Run() { } }\n");
        var search = await workspace.RunAsync(
            "search", "symbol", "Selected", "--fields", "id", "--full");
        var id = SymbolId(search.Output);

        var result = await workspace.RunAsync("outline", id, "--full");

        Assert.Equal(0, search.ExitCode);
        Assert.NotEmpty(id);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("target_kind: symbol", result.Output);
        Assert.Contains($"id: {id}", result.Output);
        Assert.Contains("name: Selected", result.Output);
        Assert.Contains("name: Field", result.Output);
        Assert.Contains("name: Run", result.Output);
        Assert.DoesNotContain("name: Other", result.Output);
        Assert.DoesNotContain("kind: import", result.Output);
        Assert.Contains("depth: 0", result.Output);
        Assert.Contains("depth: 1", result.Output);
    }

    [Fact]
    public async Task Outline_limit_reports_known_total_and_full_recovery()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Many.cs",
            "class Many { void A() { } void B() { } void C() { } }");

        var bounded = await workspace.RunAsync(
            "outline", "Many.cs", "--limit", "2");
        var full = await workspace.RunAsync(
            "outline", "Many.cs", "--full");

        Assert.Equal(0, bounded.ExitCode);
        Assert.Contains("count: 2", bounded.Output);
        Assert.Contains("total_known: true", bounded.Output);
        Assert.Contains("total: 4", bounded.Output);
        Assert.Contains("omitted: 2", bounded.Output);
        Assert.Contains("truncated: true", bounded.Output);
        Assert.Contains("outline 'Many.cs' --full", bounded.Output);
        Assert.Equal(0, full.ExitCode);
        Assert.Contains("count: 4", full.Output);
        Assert.Contains("omitted: 0", full.Output);
        Assert.Contains("truncated: false", full.Output);
        Assert.DoesNotContain("retrieval_command:", full.Output);
    }

    [Fact]
    public async Task Generated_source_requires_explicit_inclusion()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Generated.g.cs",
            "// <auto-generated/>\nclass Generated { }");

        var excluded = await workspace.RunAsync("outline", "Generated.g.cs");
        var included = await workspace.RunAsync(
            "outline", "Generated.g.cs", "--include-generated", "--full");

        Assert.Equal(1, excluded.ExitCode);
        Assert.Contains("code: document.generated_excluded", excluded.Output);
        Assert.Contains("--include-generated", excluded.Output);
        Assert.Equal(0, included.ExitCode);
        Assert.Contains("generated: true", included.Output);
        Assert.Contains("name: Generated", included.Output);
    }

    [Fact]
    public async Task Malformed_source_returns_diagnostics_and_recoverable_structure()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Broken.cs",
            "namespace Demo { class Broken { void Run( { }");

        var result = await workspace.RunAsync("outline", "Broken.cs", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches("diagnostic_count: [1-9][0-9]*", result.Output);
        Assert.Contains("name: Demo", result.Output);
        Assert.Contains("name: Broken", result.Output);
        Assert.Contains("name: Run", result.Output);
    }

    [Fact]
    public async Task Stale_symbol_recovery_preserves_generated_test_eligibility()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "tests/Generated.g.cs",
            "// <auto-generated/>\nclass Generated { }");
        var search = await workspace.RunAsync(
            "search", "symbol", "Generated",
            "--path", "tests",
            "--include-tests",
            "--include-generated",
            "--fields", "id",
            "--full");
        var staleId = SymbolId(search.Output);
        await workspace.WriteAsync(
            "tests/Generated.g.cs",
            "// <auto-generated/>\nclass Generated { void Updated() { } }");

        var result = await workspace.RunAsync(
            "outline", staleId,
            "--path", "tests",
            "--include-tests",
            "--include-generated",
            "--limit", "1");

        Assert.Equal(0, search.ExitCode);
        Assert.NotEmpty(staleId);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("code: evidence.stale_id", result.Output);
        Assert.Contains("count: 1", result.Output);
        Assert.Contains(
            "--path 'tests' --include-tests --include-generated",
            result.Output);
        Assert.Contains(
            "--fields 'id,signature,owning_projects,variant_count,variants'",
            result.Output);
    }

    [Theory]
    [InlineData("document-path", "usage.outline_scope")]
    [InlineData("negative-limit", "usage.limit")]
    [InlineData("full-and-limit", "usage.outline_limit")]
    [InlineData("invalid-symbol", "usage.symbol_id")]
    public async Task Invalid_options_return_structured_usage_errors(
        string scenario,
        string errorCode)
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("Source.cs", "class Source { }");
        var arguments = scenario switch
        {
            "document-path" => new[]
            {
                "outline", "Source.cs", "--path", "src",
            },
            "negative-limit" => new[]
            {
                "outline", "Source.cs", "--limit", "-1",
            },
            "full-and-limit" => new[]
            {
                "outline", "Source.cs", "--limit", "1", "--full",
            },
            "invalid-symbol" => new[]
            {
                "outline", "symbol/v1/invalid",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var result = await workspace.RunAsync(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"code: {errorCode}", result.Output);
    }

    private static string SymbolId(string output) =>
        Regex.Match(
                output,
                @"symbol/v2/[A-Za-z0-9_-]+/[a-f0-9]{64}/[a-f0-9]{64}",
                RegexOptions.CultureInvariant)
            .Value;

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-outline-command-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents, Encoding.UTF8);
        }

        public async Task<(int ExitCode, string Output)> RunAsync(
            params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                    ?? "dotnet",
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
