using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.Structural.Tests;

public sealed class SourceOutlineTests
{
    [Fact]
    public void Document_outline_is_source_ordered_and_omits_implementation_bodies()
    {
        const string source =
            "global using System;\n"
            + "using Text = System.Text;\n"
            + "[assembly: CLSCompliant(true)]\n"
            + "namespace Demo;\n"
            + "[Serializable]\n"
            + "public partial class Outer<T> : Base where T : class\n"
            + "{\n"
            + "    [Obsolete(\"old\")]\n"
            + "    public int Value { get { return 1; } private set { } }\n"
            + "    public void Run(string input) { Console.WriteLine(input); }\n"
            + "    public sealed class Nested { }\n"
            + "}\n";
        var outliner = new RoslynSourceOutliner();

        var first = outliner.OutlineDocument(
            "Source.cs",
            isExternal: false,
            source,
            Hash(source));
        var repeated = outliner.OutlineDocument(
            "Source.cs",
            isExternal: false,
            source,
            Hash(source));

        Assert.Equal(
            [
                "import",
                "import",
                "attribute",
                "namespace",
                "class",
                "property",
                "method",
                "class",
            ],
            first.Items.Select(static item => item.Kind));
        Assert.Equal([0, 0, 0, 0, 1, 2, 2, 2],
            first.Items.Select(static item => item.Depth));
        Assert.Equal(
            ["System", "Text", "assembly", "Demo", "Outer", "Value", "Run", "Nested"],
            first.Items.Select(static item => item.Name));
        Assert.Equal(
            ["[Serializable]"],
            first.Items.Single(static item => item.Name == "Outer").Attributes);
        Assert.Equal(
            ["[Obsolete(\"old\")]"],
            first.Items.Single(static item => item.Name == "Value").Attributes);
        Assert.Contains(
            "public partial class Outer<T> : Base where T : class",
            first.Items.Single(static item => item.Name == "Outer").Signature);
        Assert.Equal(
            "public int Value { get; private set; }",
            first.Items.Single(static item => item.Name == "Value").Signature);
        Assert.Equal(
            "public void Run(string input);",
            first.Items.Single(static item => item.Name == "Run").Signature);
        Assert.DoesNotContain(
            first.Items,
            static item => item.Signature.Contains("return 1", StringComparison.Ordinal)
                || item.Signature.Contains("Console.WriteLine", StringComparison.Ordinal));
        Assert.Equal(
            new SourceLocation("Source.cs", 5, 1),
            first.Items.Single(static item => item.Name == "Outer").Range.Start);
        Assert.All(first.Items, static item => Assert.StartsWith("syntax/v1/", item.Id));
        Assert.Equal(
            first.Items.Select(static item => item.Id),
            repeated.Items.Select(static item => item.Id));
        Assert.Equal(8, first.TotalCount);
        Assert.Equal(0, first.DiagnosticCount);
    }

    [Fact]
    public void Signatures_preserve_member_shape_without_executable_initializers()
    {
        const string source =
            "class Container\n"
            + "{\n"
            + "    public static Container Instance { get; } = new();\n"
            + "    public bool HasValue => Compute();\n"
            + "    public int this[int index] => index;\n"
            + "    public Container() : this(Create()) { }\n"
            + "    public Container(int value) { }\n"
            + "}\n"
            + "class Derived(int value) : Base(Create(value)) { }\n"
            + "unsafe struct BufferHolder\n"
            + "{\n"
            + "    public fixed int Buffer[8];\n"
            + "}\n";

        var result = new RoslynSourceOutliner().OutlineDocument(
            "Members.cs",
            isExternal: false,
            source,
            Hash(source));

        Assert.Equal(
            "public static Container Instance { get; }",
            result.Items.Single(static item => item.Name == "Instance").Signature);
        Assert.Equal(
            "public bool HasValue { get; }",
            result.Items.Single(static item => item.Name == "HasValue").Signature);
        Assert.Equal(
            "public int this[int index] { get; }",
            result.Items.Single(static item => item.Kind == "indexer").Signature);
        Assert.Equal(
            "public Container();",
            result.Items.First(static item => item.Kind == "constructor").Signature);
        Assert.DoesNotContain(
            "Create",
            result.Items.Single(static item => item.Name == "Derived").Signature,
            StringComparison.Ordinal);
        Assert.Contains(
            ": Base",
            result.Items.Single(static item => item.Name == "Derived").Signature,
            StringComparison.Ordinal);
        Assert.Equal(
            "public fixed int Buffer[8];",
            result.Items.Single(static item => item.Name == "Buffer").Signature);
    }

    [Fact]
    public void Item_limit_retains_only_the_requested_prefix_and_reports_total()
    {
        const string source =
            "class First { }\n"
            + "class Second { }\n"
            + "class Third { }\n";

        var result = new RoslynSourceOutliner().OutlineDocument(
            "Limited.cs",
            isExternal: false,
            source,
            Hash(source),
            maxItems: 1);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal("First", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void Block_namespaces_top_level_code_and_malformed_syntax_remain_visible()
    {
        const string source =
            "using System;\n"
            + "Console.WriteLine(\"hello\");\n"
            + "namespace Block\n"
            + "{\n"
            + "    partial class Outer\n"
            + "    {\n"
            + "        class Inner { }\n"
            + "        void Broken( {\n"
            + "    }\n"
            + "}\n";

        var result = new RoslynSourceOutliner().OutlineDocument(
            "Broken.cs",
            isExternal: false,
            source,
            Hash(source));

        Assert.True(result.DiagnosticCount > 0);
        Assert.Contains(result.Items, static item =>
            item.Kind == "top-level-statement"
            && item.Signature == "expression-statement");
        Assert.Contains(result.Items, static item =>
            item.Kind == "namespace" && item.Name == "Block" && item.Depth == 0);
        Assert.Contains(result.Items, static item =>
            item.Kind == "class" && item.Name == "Outer" && item.Depth == 1);
        Assert.Contains(result.Items, static item =>
            item.Kind == "class" && item.Name == "Inner" && item.Depth == 2);
        Assert.Contains(result.Items, static item =>
            item.Kind == "method" && item.Name == "Broken" && item.Depth == 2);
    }

    [Fact]
    public async Task Symbol_outline_uses_the_resolved_declaration_span_only()
    {
        using var workspace = new TestWorkspace();
        const string source =
            "using System;\n"
            + "namespace Demo;\n"
            + "class Other { }\n"
            + "partial class Selected\n"
            + "{\n"
            + "    int Field = 1;\n"
            + "    void Method() { }\n"
            + "}\n";
        var fullPath = await workspace.WriteAsync("Source.cs", source);
        var path = new WorkspaceTraversalPath(
            fullPath,
            "Source.cs",
            isExternal: false);
        var result = await new SymbolDeclarationSearcher(
                new StubTraverser([path]),
                new StubOwnership())
            .SearchAsync(
                new SymbolDeclarationSearchRequest(
                    "Selected",
                    new WorkspaceTraversalRequest(workspace.Root)));
        var match = Assert.Single(result.Matches);

        var outline = new RoslynSourceOutliner().OutlineSymbol(match);

        Assert.Equal(["class", "field", "method"],
            outline.Items.Select(static item => item.Kind));
        Assert.Equal(["Selected", "Field", "Method"],
            outline.Items.Select(static item => item.Name));
        Assert.Equal([0, 1, 1],
            outline.Items.Select(static item => item.Depth));
        Assert.DoesNotContain(outline.Items, static item => item.Name == "Other");
        Assert.DoesNotContain(outline.Items, static item => item.Kind == "import");
        Assert.Equal("int Field;",
            outline.Items.Single(static item => item.Name == "Field").Signature);
    }

    [Fact]
    public void Unicode_locations_use_one_based_utf16_columns()
    {
        const string source = "var emoji = \"😀\"; class After { }";

        var result = new RoslynSourceOutliner().OutlineDocument(
            "Unicode.cs",
            isExternal: false,
            source,
            Hash(source));

        Assert.Equal(
            new SourceLocation("Unicode.cs", 1, 19),
            result.Items.Single(static item => item.Name == "After").Range.Start);
    }

    private static string Hash(string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private sealed class StubTraverser(
        IReadOnlyList<WorkspaceTraversalPath> paths) : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return paths;
        }
    }

    private sealed class StubOwnership : IFileOwnershipResolver
    {
        public IReadOnlyList<string> GetOwningProjects(
            WorkspaceTraversalPath path) => ["App.csproj"];
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-outline-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task<string> WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath);
            await File.WriteAllTextAsync(path, contents);
            return path;
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
