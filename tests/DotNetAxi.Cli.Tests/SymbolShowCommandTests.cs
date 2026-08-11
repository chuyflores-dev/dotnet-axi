namespace DotNetAxi.Cli.Tests;

public sealed class SymbolShowCommandTests
{
    [Fact]
    public async Task Shows_bounded_member_detail_and_cheap_overload_summary()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Symbols.cs",
            """
            namespace Demo;
            public sealed class Service
            {
                /// <summary>Saves a value.</summary>
                public string Save(string value)
                {
                    return value + "abcdefghijklmnopqrstuvwxyz0123456789";
                }

                public string Save(int value) => value.ToString();
            }
            """);
        var search = await workspace.RunAsync(
            "search", "symbol", "Save", "--fields", "id", "signature", "--full");
        var ids = EntityIds(search.Output);
        Assert.Equal(2, ids.Count);
        var id = ids[0];

        var result = await workspace.RunAsync(
            "show", "symbol", id, "--max-chars", "32");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: show symbol", result.Output);
        Assert.Contains("status: success", result.Output);
        Assert.Contains($"id: {id}", result.Output);
        Assert.Contains("signature: Save(string)", result.Output);
        Assert.Contains("containing_type: Demo.Service", result.Output);
        Assert.Contains("projects[1]: App.csproj", result.Output);
        Assert.Contains("file: Symbols.cs", result.Output);
        Assert.Contains("Saves a value", result.Output);
        Assert.Contains("body:", result.Output);
        Assert.Contains("truncated: true", result.Output);
        Assert.Contains("retrieval_command:", result.Output);
        Assert.Contains("--max-chars", result.Output);
        Assert.Contains("overload_count: 2", result.Output);
        Assert.Contains("parameter_count: 1", result.Output);
    }

    [Fact]
    public async Task Shows_type_detail_and_available_syntax_relationship_counts()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Symbols.cs",
            """
            namespace Demo;
            [System.Obsolete]
            public sealed class Service : System.IDisposable
            {
                public int Value { get; set; }
                public void Run() { }
                public void Dispose() { }
            }
            """);
        var search = await workspace.RunAsync(
            "search", "symbol", "Demo.Service", "--fields", "id", "--full");

        var result = await workspace.RunAsync(
            "show", "symbol", Assert.Single(EntityIds(search.Output)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("kind: class", result.Output);
        Assert.Contains("fully_qualified_name: Demo.Service", result.Output);
        Assert.Contains("attribute_count: 1", result.Output);
        Assert.Contains("base_type_count: 1", result.Output);
        Assert.Contains("member_count: 3", result.Output);
    }

    [Fact]
    public async Task Bodyless_declarations_keep_an_explicit_empty_body_preview()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Symbols.cs",
            """
            namespace Demo;
            public interface IService
            {
                /// <summary>Runs the service.</summary>
                void Run();
            }
            """);
        var search = await workspace.RunAsync(
            "search", "symbol", "Run", "--fields", "id", "--full");

        var result = await workspace.RunAsync(
            "show", "symbol", Assert.Single(EntityIds(search.Output)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Runs the service", result.Output);
        Assert.Contains(
            "body:\n  preview: \"\"\n  included_characters: 0\n  total_characters: 0\n  omitted_characters: 0\n  truncated: false",
            result.Output);
    }

    [Fact]
    public async Task Stale_ids_return_replacements_and_the_existing_search_correction()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Symbols.cs",
            "namespace Demo; public class Service { public void Save(int value) { } }");
        var search = await workspace.RunAsync(
            "search", "symbol", "Save", "--fields", "id", "--full");
        var id = Assert.Single(EntityIds(search.Output));
        await workspace.WriteAsync(
            "Symbols.cs",
            "namespace Demo; public class Service { public void Save(string value) { } }");

        var result = await workspace.RunAsync("show", "symbol", id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("status: failed", result.Output);
        Assert.Contains("code: evidence.stale_id", result.Output);
        Assert.Contains("query: dnaxi search symbol 'Save'", result.Output);
        Assert.Contains("Save(string)", result.Output);
    }

    [Fact]
    public async Task Moved_identity_collisions_remain_ambiguous_with_candidates()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        const string declaration = "namespace Demo; public partial class Widget { }";
        await workspace.WriteAsync("a/Widget.cs", declaration);
        var search = await workspace.RunAsync(
            "search", "symbol", "Demo.Widget", "--fields", "id", "--full");
        var id = Assert.Single(EntityIds(search.Output));
        await workspace.WriteAsync("b/Widget.cs", declaration);
        await workspace.WriteAsync("c/Widget.cs", declaration);
        File.Delete(Path.Combine(workspace.Root, "a", "Widget.cs"));

        var result = await workspace.RunAsync("show", "symbol", id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("code: evidence.ambiguous_id", result.Output);
        Assert.Contains("candidate_count: 2", result.Output);
        Assert.Contains("b/Widget.cs", result.Output);
        Assert.Contains("c/Widget.cs", result.Output);
        Assert.Contains("query: dnaxi search symbol 'Widget'", result.Output);
    }

    [Fact]
    public async Task Explicit_external_search_scope_can_be_reused_to_show_its_symbol()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var externalPath = await workspace.WriteExternalAsync(
            "External.cs",
            "namespace External; public class Visible { public string Run() => \"abcdefghijklmnopqrstuvwxyz\"; }");
        var search = await workspace.RunAsync(
            "search", "symbol", "Run", "--path", externalPath,
            "--fields", "id", "--full");

        var result = await workspace.RunAsync(
            "show", "symbol", Assert.Single(EntityIds(search.Output)),
            "--path", externalPath, "--max-chars", "2");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("external: true", result.Output);
        Assert.Contains("containing_type: External.Visible", result.Output);
        Assert.Contains("retrieval_command:", result.Output);
        Assert.Contains($"--path '{externalPath}'", result.Output);

        await workspace.WriteExternalAsync(
            "External.cs",
            "namespace External; public class Visible { public string Run(int value) => value.ToString(); }");
        var stale = await workspace.RunAsync(
            "show", "symbol", Assert.Single(EntityIds(search.Output)),
            "--path", externalPath);

        Assert.Equal(1, stale.ExitCode);
        Assert.Contains("code: evidence.stale_id", stale.Output);
        Assert.Contains($"--path '{externalPath}'", stale.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("symbol/v1/V2lkZ2V0/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public async Task Unsupported_symbol_ids_return_a_structured_usage_correction(
        string id)
    {
        using var workspace = new TestWorkspace();

        var result = await workspace.RunAsync("show", "symbol", id);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("code: usage.symbol_id", result.Output);
        Assert.Contains("search symbol", result.Output);
    }

    private static IReadOnlyList<string> EntityIds(string output) =>
        System.Text.RegularExpressions.Regex.Matches(
                output,
                @"symbol/v2/[A-Za-z0-9_-]+/[a-f0-9]{64}/[a-f0-9]{64}",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Select(static match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-symbol-show-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ExternalRoot = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-symbol-show-external",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ExternalRoot);
        }

        public string Root { get; }

        private string ExternalRoot { get; }

        public async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        public async Task<string> WriteExternalAsync(
            string relativePath,
            string contents)
        {
            var path = Path.Combine(ExternalRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
            return Path.GetRelativePath(Root, path).Replace('\\', '/');
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


            if (Directory.Exists(ExternalRoot))
            {
                Directory.Delete(ExternalRoot, recursive: true);
            }
        }
    }
}
