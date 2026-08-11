using System.Text;
using System.Text.RegularExpressions;

namespace DotNetAxi.Cli.Tests;

public sealed class DocumentShowCommandTests
{
    [Fact]
    public async Task Small_document_reports_identity_ownership_encoding_and_outline_reference()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        const string contents =
            "namespace Demo;\npublic sealed class Document { }\n";
        await workspace.WriteAsync("Document.cs", contents);
        var search = await workspace.RunAsync(
            "search", "file", "Document.cs", "--fields", "id", "path");

        var result = await workspace.RunAsync(
            "show", "document", "./Document.cs");

        Assert.Equal(0, search.ExitCode);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("command: show document", result.Output);
        Assert.Contains("status: success", result.Output);
        Assert.Matches("snapshot: ws_[a-f0-9]{64}", result.Output);
        Assert.Matches("id: file/v1/[a-f0-9]{64}", result.Output);
        Assert.Equal(FileId(search.Output), FileId(result.Output));
        Assert.Contains("path: Document.cs", result.Output);
        Assert.Contains("external: false", result.Output);
        Assert.Contains("generated: false", result.Output);
        Assert.Contains("owning_project_count: 1", result.Output);
        Assert.Contains("owning_projects[1]: App.csproj", result.Output);
        Assert.Contains("encoding: utf-8", result.Output);
        Assert.Contains("byte_order_mark: false", result.Output);
        Assert.Contains(
            $"byte_count: {Encoding.UTF8.GetByteCount(contents)}",
            result.Output);
        Assert.Contains("public sealed class Document", result.Output);
        Assert.Contains(
            $"included_characters: {contents.EnumerateRunes().Count()}",
            result.Output);
        Assert.Contains("total_known: true", result.Output);
        Assert.Contains("omitted_characters: 0", result.Output);
        Assert.Contains("truncated: false", result.Output);
        Assert.DoesNotContain("retrieval_command:", result.Output);
        Assert.Contains(
            "outline_reference:\n  path: Document.cs\n  available: false",
            result.Output);
        Assert.DoesNotContain(" outline ", result.Output);
    }

    [Fact]
    public async Task Large_document_is_bounded_by_default_and_full_is_explicit()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var contents = new string('x', 1100) + "😀";
        await workspace.WriteAsync("Large.cs", contents);

        var bounded = await workspace.RunAsync(
            "show", "document", "Large.cs");

        Assert.Equal(0, bounded.ExitCode);
        Assert.Contains("included_characters: 1000", bounded.Output);
        Assert.Contains("total_characters: 1101", bounded.Output);
        Assert.Contains("omitted_characters: 101", bounded.Output);
        Assert.Contains("truncated: true", bounded.Output);
        Assert.Contains("retrieval_command:", bounded.Output);
        Assert.Contains("show document 'Large.cs' --full", bounded.Output);

        var full = await workspace.RunAsync(
            "show", "document", "Large.cs", "--full");

        Assert.Equal(0, full.ExitCode);
        Assert.Contains("included_characters: 1101", full.Output);
        Assert.Contains("total_characters: 1101", full.Output);
        Assert.Contains("omitted_characters: 0", full.Output);
        Assert.Contains("truncated: false", full.Output);
        Assert.DoesNotContain("retrieval_command:", full.Output);
        Assert.Contains("😀", full.Output);
    }

    [Theory]
    [MemberData(nameof(EncodedDocuments))]
    public async Task Encoded_documents_report_the_detected_encoding(
        string fileName,
        byte[] content,
        string expectedEncoding)
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteBytesAsync(fileName, content);

        var result = await workspace.RunAsync(
            "show", "document", fileName, "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"encoding: {expectedEncoding}", result.Output);
        Assert.Contains("byte_order_mark: true", result.Output);
        Assert.Contains($"byte_count: {content.Length}", result.Output);
        Assert.Contains("Hello 世界 👋", result.Output);
    }

    [Fact]
    public async Task Generated_document_requires_explicit_inclusion()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "Generated.g.cs",
            "namespace Demo; public sealed class Generated { }");

        var excluded = await workspace.RunAsync(
            "show", "document", "Generated.g.cs", "--max-chars", "20");

        Assert.Equal(1, excluded.ExitCode);
        Assert.Contains("status: failed", excluded.Output);
        Assert.Contains("code: document.generated_excluded", excluded.Output);
        Assert.Contains("dnx dnaxi@", excluded.Output);
        Assert.Contains(
            "--verbosity quiet -- show document 'Generated.g.cs' --include-generated",
            excluded.Output);
        Assert.Contains("--max-chars 20", excluded.Output);

        var included = await workspace.RunAsync(
            "show", "document", "Generated.g.cs", "--include-generated");

        Assert.Equal(0, included.ExitCode);
        Assert.Contains("generated: true", included.Output);
        Assert.Contains("public sealed class Generated", included.Output);
    }

    [Fact]
    public async Task Generated_header_in_returned_content_requires_explicit_inclusion()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync(
            "OrdinaryName.cs",
            "// <auto-generated/>\nnamespace Demo;");

        var excluded = await workspace.RunAsync(
            "show", "document", "OrdinaryName.cs");
        var included = await workspace.RunAsync(
            "show", "document", "OrdinaryName.cs", "--include-generated");

        Assert.Equal(1, excluded.ExitCode);
        Assert.Contains("code: document.generated_excluded", excluded.Output);
        Assert.Equal(0, included.ExitCode);
        Assert.Contains("generated: true", included.Output);
        Assert.Contains("<auto-generated/>", included.Output);
    }

    [Fact]
    public async Task Explicit_external_document_remains_external_and_unowned()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var externalPath = await workspace.WriteExternalAsync(
            "External.cs",
            "namespace External; public sealed class Visible { }");

        var result = await workspace.RunAsync(
            "show", "document", externalPath, "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"path: {externalPath}", result.Output);
        Assert.Contains("external: true", result.Output);
        Assert.Contains("owning_project_count: 0", result.Output);
        Assert.Contains("owning_projects: []", result.Output);
        Assert.Contains("public sealed class Visible", result.Output);
    }

    [Fact]
    public async Task Missing_document_returns_a_structured_failure()
    {
        using var workspace = new TestWorkspace();

        var result = await workspace.RunAsync(
            "show", "document", "Missing.cs");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("command: show document", result.Output);
        Assert.Contains("status: failed", result.Output);
        Assert.Contains("code: document.not_found", result.Output);
        Assert.Contains("Correct the path", result.Output);
    }

    [Fact]
    public async Task Changed_document_changes_snapshot_but_preserves_path_identity()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await workspace.WriteAsync("Changed.cs", "before");
        var before = await workspace.RunAsync(
            "show", "document", "Changed.cs", "--full");

        await workspace.WriteAsync("Changed.cs", "after");
        var after = await workspace.RunAsync(
            "show", "document", "Changed.cs", "--full");

        Assert.Equal(0, before.ExitCode);
        Assert.Equal(0, after.ExitCode);
        Assert.NotEqual(EvidenceId(before.Output), EvidenceId(after.Output));
        Assert.Equal(FileId(before.Output), FileId(after.Output));
        Assert.Contains("preview: before", before.Output);
        Assert.Contains("preview: after", after.Output);
    }

    [Fact]
    public async Task Malformed_source_is_returned_without_requiring_a_parse()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        const string malformed =
            "namespace Demo { public sealed class Broken {";
        await workspace.WriteAsync("Broken.cs", malformed);

        var result = await workspace.RunAsync(
            "show", "document", "Broken.cs", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: success", result.Output);
        Assert.Contains(malformed, result.Output);
        Assert.Contains("coverage: complete", result.Output);
    }

    [Theory]
    [MemberData(nameof(RejectedDocuments))]
    public async Task Rejected_document_content_returns_a_structured_failure(
        byte[] content,
        string errorCode,
        string message)
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteBytesAsync("Invalid.cs", content);

        var result = await workspace.RunAsync(
            "show", "document", "Invalid.cs");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("status: failed", result.Output);
        Assert.Contains($"code: {errorCode}", result.Output);
        Assert.Contains(message, result.Output);
    }

    [Theory]
    [InlineData("-1", false, "usage.max_chars")]
    [InlineData("20", true, "usage.document_budget")]
    public async Task Invalid_budgets_return_structured_usage_errors(
        string maximumCharacters,
        bool full,
        string errorCode)
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync("Document.cs", "text");
        var arguments = new List<string>
        {
            "show",
            "document",
            "Document.cs",
            "--max-chars",
            maximumCharacters,
        };
        if (full)
        {
            arguments.Add("--full");
        }

        var result = await workspace.RunAsync(arguments.ToArray());

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"code: {errorCode}", result.Output);
    }

    public static IEnumerable<object[]> EncodedDocuments()
    {
        const string text = "Hello 世界 👋";
        yield return
        [
            "Utf8Bom.cs",
            WithPreamble(new UTF8Encoding(true, true), text),
            "utf-8",
        ];
        yield return
        [
            "Utf16Le.cs",
            WithPreamble(new UnicodeEncoding(false, true, true), text),
            "utf-16-le",
        ];
        yield return
        [
            "Utf16Be.cs",
            WithPreamble(new UnicodeEncoding(true, true, true), text),
            "utf-16-be",
        ];
    }

    public static IEnumerable<object[]> RejectedDocuments()
    {
        yield return
        [
            new byte[] { 0xc3, 0x28 },
            "document.undecodable",
            "without data loss",
        ];
        yield return
        [
            new byte[] { 0x41, 0x00, 0x42 },
            "document.binary",
            "contains binary data",
        ];
        yield return
        [
            new byte[] { 0xff, 0xfe, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 },
            "document.encoding_unsupported",
            "unsupported encoding",
        ];
    }

    private static byte[] WithPreamble(Encoding encoding, string text) =>
        [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static string EvidenceId(string output) =>
        Regex.Match(
                output,
                @"^snapshot: (ws_[a-f0-9]{64})$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Groups[1]
            .Value;

    private static string FileId(string output) =>
        Regex.Match(
                output,
                @"file/v1/[a-f0-9]{64}",
                RegexOptions.CultureInvariant)
            .Value;

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-document-show-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ExternalRoot = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-document-show-external",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ExternalRoot);
        }

        public string Root { get; }

        private string ExternalRoot { get; }

        public Task WriteAsync(string relativePath, string contents) =>
            WriteBytesAsync(relativePath, Encoding.UTF8.GetBytes(contents));

        public async Task WriteBytesAsync(
            string relativePath,
            byte[] contents)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, contents);
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
