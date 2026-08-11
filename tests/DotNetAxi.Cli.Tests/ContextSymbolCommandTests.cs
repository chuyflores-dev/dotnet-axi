using System.Text.RegularExpressions;
using DotNetAxi.Cli.Output;
using DotNetAxi.Contracts;

namespace DotNetAxi.Cli.Tests;

public sealed class ContextSymbolCommandTests
{
    [Fact]
    public async Task Composes_four_sections_once_with_shared_identity_and_snapshot()
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
                public string Value => "A😀";
            }
            """);
        var id = await workspace.SymbolIdAsync("Demo.Service");

        var result = await workspace.RunAsync(
            "context", "symbol", id, "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: context symbol", result.Output);
        Assert.Contains("budget_mode: full", result.Output);
        Assert.Contains("sections[4]:", result.Output);
        Assert.Contains($"target:\n  id: {id}", result.Output);
        Assert.Contains("- name: declaration", result.Output);
        Assert.Contains("- name: owner", result.Output);
        Assert.Contains("- name: document", result.Output);
        Assert.Contains("- name: outline", result.Output);
        Assert.Contains($"source_span_ref: {id}", result.Output);
        Assert.Contains($"root_declaration_ref: {id}", result.Output);
        Assert.Contains("projects[1]: App.csproj", result.Output);
        Assert.Contains("provenance: resolved-declaration-source", result.Output);
        Assert.Single(Regex.Matches(result.Output, "public sealed class Service"));
        Assert.Single(Regex.Matches(result.Output, "^snapshot:", RegexOptions.Multiline));
        Assert.DoesNotContain("retrieval_command:", result.Output);
        AssertSectionBodyMatchesReportedCount(result.Output, sectionCount: 4);
    }

    [Fact]
    public async Task Whole_section_budget_reports_counts_and_scope_preserving_recovery()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Symbols.cs",
            "namespace Demo; public sealed class Service { public string Value => \"😀\"; }");
        var id = await workspace.SymbolIdAsync("Demo.Service");

        var result = await workspace.RunAsync(
            "context", "symbol", id,
            "--project", "App.csproj",
            "--include", "declaration,owner,document,outline",
            "--max-chars", "0");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("sections: []", result.Output);
        Assert.Contains("included_characters: 0", result.Output);
        Assert.Contains("omitted_sections[4]: declaration,owner,document,outline", result.Output);
        Assert.Contains("approximate_tokens:", result.Output);
        Assert.Contains("minimum: 0", result.Output);
        Assert.Contains("maximum: 0", result.Output);
        Assert.Contains("retrieval_command:", result.Output);
        Assert.Contains("--project 'App.csproj'", result.Output);
        Assert.Contains("--include 'declaration,owner,document,outline'", result.Output);
        Assert.Contains("--max-chars", result.Output);
    }

    [Fact]
    public async Task Owner_section_preserves_multiple_projects_and_variants()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Workspace.slnx",
            "<Solution><Project Path=\"A.csproj\" /><Project Path=\"B.csproj\" /></Solution>");
        await workspace.WriteAsync("A.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync("B.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Symbols.cs",
            "namespace Demo; public sealed class Shared { }");
        var id = await workspace.SymbolIdAsync(
            "Demo.Shared", "--solution", "Workspace.slnx");

        var result = await workspace.RunAsync(
            "context", "symbol", id,
            "--solution", "Workspace.slnx",
            "--include", "owner",
            "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("project_count: 2", result.Output);
        Assert.Contains("projects[2]: A.csproj,B.csproj", result.Output);
        Assert.Contains("variant_count: 2", result.Output);
        Assert.Contains("variants[2]{project,meaning}:", result.Output);
        Assert.Contains($"target:\n  id: {id}", result.Output);
        Assert.Contains("file: Symbols.cs", result.Output);
        AssertSectionBodyMatchesReportedCount(result.Output, sectionCount: 1);
    }

    [Fact]
    public async Task Scoped_test_stale_recovery_preserves_scope_sections_and_budget()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Workspace.slnx",
            "<Solution><Project Path=\"tests/App.Tests.csproj\" /></Solution>");
        await workspace.WriteAsync(
            "tests/App.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "tests/Symbols.cs",
            "namespace Demo; public class Spec { public void Save(int value) { } }");
        var id = await workspace.SymbolIdAsync(
            "Save",
            "--solution", "Workspace.slnx",
            "--include-tests");
        var shown = await workspace.RunAsync(
            "context", "symbol", id,
            "--solution", "Workspace.slnx",
            "--include-tests",
            "--include", "declaration,owner",
            "--full");
        Assert.Equal(0, shown.ExitCode);
        Assert.Contains("include_tests: true", shown.Output);
        Assert.Contains("projects[1]: tests/App.Tests.csproj", shown.Output);

        await workspace.WriteAsync(
            "tests/Symbols.cs",
            "namespace Demo; public class Spec { public void Save(string value) { } }");
        var stale = await workspace.RunAsync(
            "context", "symbol", id,
            "--solution", "Workspace.slnx",
            "--include-tests",
            "--include", "declaration,owner",
            "--max-chars", "42");

        Assert.Equal(1, stale.ExitCode);
        Assert.Contains("code: evidence.stale_id", stale.Output);
        Assert.Contains("context_command", stale.Output);
        Assert.Contains("--solution 'Workspace.slnx'", stale.Output);
        Assert.Contains("--include-tests", stale.Output);
        Assert.Contains("--include 'declaration,owner'", stale.Output);
        Assert.Contains("--max-chars 42", stale.Output);
    }

    [Fact]
    public async Task Generated_identity_trajectory_preserves_explicit_eligibility()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Generated.g.cs",
            "namespace Demo; public sealed class GeneratedService { }");
        var id = await workspace.SymbolIdAsync(
            "Demo.GeneratedService", "--include-generated");

        var result = await workspace.RunAsync(
            "context", "symbol", id,
            "--include-generated",
            "--include", "declaration,document",
            "--max-chars", "0");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("include_generated: true", result.Output);
        Assert.Contains("omitted_sections[2]: declaration,document", result.Output);
        Assert.Contains("--include-generated", result.Output);
        Assert.Contains("--include 'declaration,document'", result.Output);
    }

    [Fact]
    public async Task Ambiguous_candidates_keep_context_continuations_and_scope()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        const string declaration =
            "namespace Demo; public partial class Widget { }";
        await workspace.WriteAsync("original/Widget.cs", declaration);
        var id = await workspace.SymbolIdAsync("Demo.Widget");
        await workspace.WriteAsync("moved/Widget1.cs", declaration);
        await workspace.WriteAsync("moved/Widget2.cs", declaration);
        File.Delete(Path.Combine(workspace.Root, "original", "Widget.cs"));

        var result = await workspace.RunAsync(
            "context", "symbol", id,
            "--project", "App.csproj",
            "--include", "owner,outline",
            "--full");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("code: evidence.ambiguous_id", result.Output);
        Assert.Contains("candidate_count: 2", result.Output);
        Assert.Contains("context_command", result.Output);
        Assert.Equal(2, Regex.Matches(
            result.Output,
            "--include 'owner,outline'").Count);
        Assert.True(Regex.Matches(
            result.Output,
            "--project 'App.csproj'").Count >= 2);
        Assert.True(Regex.Matches(result.Output, "--full").Count >= 2);
    }

    [Fact]
    public void Section_cost_uses_the_exact_toon_unicode_scalar_representation()
    {
        var value = new SymbolContextSectionPayload(
            "fixture",
            EvidenceResolution.Text,
            EvidenceConfidence.Verified,
            new { Text = "A😀B" });
        var section = ToonResultSerializer.CreateContextSectionForBudget(
            "document",
            2,
            value,
            hasPreviousSection: false);
        var expected = section.IncludedCharacters;

        var exact = ContextBudgeter.Apply(
            [section],
            ContextBudget.Resolve(1, explicitMaximumCharacters: expected),
            maximum => $"larger {maximum}",
            "full");
        var undersized = ContextBudgeter.Apply(
            [section],
            ContextBudget.Resolve(1, explicitMaximumCharacters: expected - 1),
            maximum => $"larger {maximum}",
            "full");

        Assert.Equal(expected, exact.IncludedCharacters);
        Assert.False(exact.Truncated);
        Assert.Empty(undersized.Sections);
        Assert.Equal($"larger {expected}", undersized.RetrievalCommand);
    }

    [Theory]
    [InlineData("callers", "capability.context_section_unavailable")]
    [InlineData("unknown", "usage.context_section")]
    [InlineData("declaration,,owner", "usage.context_section")]
    public async Task Invalid_or_unavailable_sections_return_structured_corrections(
        string section,
        string code)
    {
        var result = await RunInRepositoryAsync(
            "context", "symbol",
            "symbol/v2/V2lkZ2V0/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "--include", section);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"code: {code}", result.Output);
        Assert.Contains("declaration,owner,document,outline", result.Output);
    }

    [Fact]
    public async Task Context_parent_and_symbol_help_are_registered()
    {
        var parent = await RunInRepositoryAsync("context", "--help");
        var symbol = await RunInRepositoryAsync("context", "symbol", "--help");

        Assert.Equal(0, parent.ExitCode);
        Assert.Contains("topic: context", parent.Output);
        Assert.Contains("subcommands[1]", parent.Output);
        Assert.Equal(0, symbol.ExitCode);
        Assert.Contains("topic: context symbol", symbol.Output);
        Assert.Contains("--include", symbol.Output);
        Assert.Contains("--max-chars", symbol.Output);
        Assert.Contains("--full", symbol.Output);
    }

    private static Task<(int ExitCode, string Output)> RunInRepositoryAsync(
        params string[] arguments) =>
        TestWorkspace.ExecuteAsync(Directory.GetCurrentDirectory(), arguments);

    private static void AssertSectionBodyMatchesReportedCount(
        string output,
        int sectionCount)
    {
        var sectionsMarker = $"sections[{sectionCount}]:\n";
        var sectionStart = output.IndexOf(
            sectionsMarker,
            StringComparison.Ordinal) + sectionsMarker.Length;
        var sectionEnd = output.IndexOf(
            "\nincluded_characters:",
            sectionStart,
            StringComparison.Ordinal);
        var emittedSection = output[sectionStart..sectionEnd];
        var reported = Regex.Match(
            output[sectionEnd..],
            "^included_characters: (?<count>[0-9]+)",
            RegexOptions.Multiline);
        Assert.True(reported.Success, output);
        Assert.Equal(
            emittedSection.EnumerateRunes().Count(),
            int.Parse(
                reported.Groups["count"].Value,
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-context-symbol-tests",
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
            await File.WriteAllTextAsync(path, contents);
        }

        public async Task<string> SymbolIdAsync(
            string name,
            params string[] scopeArguments)
        {
            var arguments = new List<string> { "search", "symbol", name };
            arguments.AddRange(scopeArguments);
            arguments.AddRange(["--fields", "id", "--full"]);
            var search = await RunAsync(arguments.ToArray());
            var match = Regex.Match(search.Output, "symbol/v2/[A-Za-z0-9_/-]+");
            Assert.True(match.Success, search.Output);
            return match.Value;
        }

        public Task<(int ExitCode, string Output)> RunAsync(
            params string[] arguments) => ExecuteAsync(Root, arguments);

        public static async Task<(int ExitCode, string Output)> ExecuteAsync(
            string workingDirectory,
            params string[] arguments)
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
            foreach (var argument in arguments)
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

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
