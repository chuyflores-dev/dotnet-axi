namespace DotNetAxi.Cli.Tests;

public sealed class SymbolSearchCommandTests
{
    [Fact]
    public async Task Symbol_search_returns_ranked_compact_declaration_rows()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Symbols.cs",
            """
            namespace Demo;
            public partial class Widget { public void Run() { } }
            public partial class Widget { }
            public class WidgetFactory { }
            """);

        var result = await workspace.RunAsync("search", "symbol", "Widget", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: search symbol", result.Output);
        Assert.Contains("resolution: syntax", result.Output);
        Assert.Contains("count: 3", result.Output);
        Assert.Contains("matches[3]{kind,name,file,line}:", result.Output);
        Assert.Contains("class,Widget,Symbols.cs,2", result.Output);
        Assert.DoesNotContain("symbol-candidate/v1/", result.Output);
    }

    [Fact]
    public async Task Symbol_search_supports_declared_filters_and_opt_in_fields()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "src/App/Service.cs",
            "namespace Product.App; public class Service { private void Save() { } }");
        await workspace.WriteAsync(
            "src/App/Service.g.cs",
            "namespace Product.App; public class GeneratedService { }");
        await workspace.WriteAsync(
            "tests/App.Tests/App.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "tests/App.Tests/ServiceTests.cs",
            "namespace Product.Tests; public class ServiceTests { public void Save() { } }");

        var production = await workspace.RunAsync(
            "search", "symbol", "Service",
            "--kind", "class",
            "--namespace", "Product.App",
            "--project", "src/App/App.csproj",
            "--accessibility", "public",
            "--fields", "namespace", "accessibility", "owning_projects", "test", "generated",
            "--full");
        var expanded = await workspace.RunAsync(
            "search", "symbol", "Service",
            "--include-tests", "--include-generated", "--full");

        Assert.Equal(0, production.ExitCode);
        Assert.Contains("count: 1", production.Output);
        Assert.Contains("Service", production.Output);
        Assert.DoesNotContain("GeneratedService", production.Output);
        Assert.DoesNotContain("ServiceTests", production.Output);
        Assert.Contains("namespace", production.Output);
        Assert.Contains("accessibility", production.Output);
        Assert.Contains("owning_projects", production.Output);
        Assert.Equal(0, expanded.ExitCode);
        Assert.Contains("count: 3", expanded.Output);
        Assert.Contains("GeneratedService", expanded.Output);
        Assert.Contains("ServiceTests", expanded.Output);
    }

    [Fact]
    public async Task Symbol_search_exposes_distinct_project_framework_variants()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup></Project>");
        await workspace.WriteAsync(
            "Shared.cs",
            "namespace Demo; public class Shared { }");

        var result = await workspace.RunAsync(
            "search", "symbol", "Shared",
            "--fields", "id", "variant_count", "variants",
            "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("variant_count: 2", result.Output);
        Assert.Contains("net10.0", result.Output);
        Assert.Contains("net8.0", result.Output);
        Assert.Contains("meaning", result.Output);
        Assert.Contains("unresolved", result.Output);
        Assert.DoesNotContain("symbol-variant/", result.Output);
    }

    [Fact]
    public async Task Symbol_search_bounds_results_and_emits_a_complete_retrieval_command()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync("Symbols.cs", "class MatchA { } class MatchB { }");

        var result = await workspace.RunAsync(
            "search", "symbol", "Match", "--limit", "1", "--fields", "id", "rank");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("count: 1", result.Output);
        Assert.Contains("total: 2", result.Output);
        Assert.Contains("omitted: 1", result.Output);
        Assert.Contains("truncated: true", result.Output);
        Assert.Contains("matches[1]{id,kind,name,file,line,rank}:", result.Output);
        Assert.Contains("retrieval_command:", result.Output);
        Assert.Contains("search symbol 'Match'", result.Output);
        Assert.Contains("--fields 'id' 'rank' --full", result.Output);
        Assert.DoesNotContain("--limit", result.Output);
    }

    [Fact]
    public async Task Empty_symbol_search_uses_the_compact_bounded_shape()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync("Symbols.cs", "class Present { }");

        var result = await workspace.RunAsync("search", "symbol", "Missing");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("count: 0", result.Output);
        Assert.Contains("total_known: true", result.Output);
        Assert.Contains("total: 0", result.Output);
        Assert.Contains("omitted: 0", result.Output);
        Assert.Contains("truncated: false", result.Output);
        Assert.Contains("matches: []", result.Output);
        Assert.DoesNotContain("query:", result.Output);
    }

    [Fact]
    public async Task Entity_id_is_stable_across_fresh_processes_state_deletion_and_file_move()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "before/Service.cs",
            "namespace Demo; public class Service { }");

        var first = await workspace.RunAsync(
            "search", "symbol", "Service", "--fields", "id", "--full");
        var firstId = EntityId(first.Output);
        var stateDirectory = Path.Combine(workspace.Root, ".dnaxi");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(stateDirectory, "discarded-state"),
            "identity must not read this");
        Directory.Delete(stateDirectory, recursive: true);
        var fresh = await workspace.RunAsync(
            "search", "symbol", "Service", "--fields", "id", "--full");
        Assert.Equal(firstId, EntityId(fresh.Output));
        Directory.CreateDirectory(Path.Combine(workspace.Root, "after"));
        File.Move(
            Path.Combine(workspace.Root, "before", "Service.cs"),
            Path.Combine(workspace.Root, "after", "Service.cs"));

        var resolution = await workspace.ResolveInFreshProcessAsync(firstId);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, fresh.ExitCode);
        Assert.Equal(0, resolution.ExitCode);
        Assert.Contains("resolved: true", resolution.Output);
        Assert.Contains("ambiguous: false", resolution.Output);
        Assert.Contains("after/Service.cs", resolution.Output);
        Assert.False(Directory.Exists(stateDirectory));
    }

    [Fact]
    public async Task Changed_entity_id_fails_stale_with_replacements_and_query()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await workspace.WriteAsync(
            "Service.cs",
            "namespace Demo; public class Service { public void Save(int value) { } }");
        var first = await workspace.RunAsync(
            "search", "symbol", "Save", "--fields", "id", "--full");
        var firstId = EntityId(first.Output);
        await workspace.WriteAsync(
            "Service.cs",
            "namespace Demo; public class Service { public void Save(string value) { } }");

        var resolution = await workspace.ResolveInFreshProcessAsync(firstId);

        Assert.Equal(1, resolution.ExitCode);
        Assert.Contains("stale: true", resolution.Output);
        Assert.Contains("error: evidence.stale_id", resolution.Output);
        Assert.Contains(
            "query: dnaxi search symbol 'Save' --fields id signature owning_projects variant_count variants --full",
            resolution.Output);
        Assert.Contains("replacement: Save(string)", resolution.Output);
    }

    [Fact]
    public async Task Changed_target_framework_fails_entity_id_stale()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        await workspace.WriteAsync(
            "Service.cs",
            "namespace Demo; public class Service { }");
        var first = await workspace.RunAsync(
            "search", "symbol", "Service", "--fields", "id", "--full");
        var firstId = EntityId(first.Output);
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var resolution = await workspace.ResolveInFreshProcessAsync(firstId);

        Assert.Equal(1, resolution.ExitCode);
        Assert.Contains("stale: true", resolution.Output);
        Assert.Contains("error: evidence.stale_id", resolution.Output);
        Assert.Contains("replacement: Service", resolution.Output);
    }

    private static string EntityId(string output)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            output,
            @"symbol/v2/[A-Za-z0-9_-]+/[a-f0-9]{64}/[a-f0-9]{64}",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Expected a versioned symbol entity ID in: {output}");
        return match.Value;
    }

    [Theory]
    [InlineData("search", "symbol")]
    [InlineData("search", "symbol", "")]
    [InlineData("search", "symbol", "Name", "--kind", "unknown")]
    [InlineData("search", "symbol", "Name", "--accessibility", "unknown")]
    [InlineData("search", "symbol", "Name", "--namespace", "")]
    [InlineData("search", "symbol", "Name", "--project", "")]
    [InlineData("search", "symbol", "Name", "--path", "")]
    [InlineData("search", "symbol", "Name", "--limit", "-1")]
    [InlineData("search", "symbol", "Name", "--full", "--limit", "1")]
    [InlineData("search", "symbol", "Name", "--fields", "unknown")]
    public async Task Invalid_symbol_requests_are_structured_usage_errors(
        params string[] arguments)
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(arguments);

        Assert.Equal(2, exit);
        Assert.Contains("command: search symbol", output.ToString());
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("usage.", output.ToString());
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-symbol-command-tests",
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
            Assert.True(string.IsNullOrEmpty(error), $"Expected empty stderr, got: {error}");
            return (process.ExitCode, output);
        }

        public async Task<(int ExitCode, string Output)> ResolveInFreshProcessAsync(
            string id)
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(TestApplicationPath());
            start.ArgumentList.Add("resolve-symbol");
            start.ArgumentList.Add(Root);
            start.ArgumentList.Add(id);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(string.IsNullOrEmpty(error), $"Expected empty stderr, got: {error}");
            return (process.ExitCode, output);
        }

        private static string TestApplicationPath()
        {
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            return Path.Combine(
                Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..")),
                "tests",
                "DotNetAxi.Cli.TestApp",
                "bin",
                configuration,
                "net10.0",
                "DotNetAxi.Cli.TestApp.dll");
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
