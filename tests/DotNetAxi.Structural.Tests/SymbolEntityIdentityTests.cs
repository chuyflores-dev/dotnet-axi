using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.Structural.Tests;

public sealed class SymbolEntityIdentityTests
{
    [Fact]
    public async Task Entity_ids_are_versioned_and_distinguish_overloads_and_partial_declarations()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync(
            "Symbols.cs",
            """
            namespace Demo;
            partial class Widget { public void Run() { } public void Run(int value) { } }
            partial class Widget { }
            """);
        var searcher = Searcher([Path(source, "Symbols.cs")]);

        var overloads = await searcher.SearchAsync(Request(workspace.Root, "Run"));
        var partials = await searcher.SearchAsync(Request(workspace.Root, "Widget"));

        Assert.Equal(2, overloads.Matches.Select(match => match.Id).Distinct().Count());
        Assert.Equal(2, partials.Matches.Select(match => match.Id).Distinct().Count());
        Assert.All(
            overloads.Matches.Concat(partials.Matches),
            match => Assert.StartsWith("symbol/v2/", match.Id));
    }

    [Fact]
    public async Task Entity_id_survives_a_file_move_and_resolves_without_retained_state()
    {
        using var workspace = new TestWorkspace();
        var before = await workspace.WriteAsync(
            "before/Service.cs",
            "namespace Demo; class Service { public void Save() { } }");
        var firstSearcher = Searcher([Path(before, "before/Service.cs")]);
        var first = Assert.Single((await firstSearcher.SearchAsync(
            Request(workspace.Root, "Save"))).Matches);

        var stateDirectory = System.IO.Path.Combine(workspace.Root, ".dnaxi");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(stateDirectory, "discarded-state"),
            "not used by identity resolution");
        Directory.Delete(stateDirectory, recursive: true);

        var after = System.IO.Path.Combine(workspace.Root, "after", "Service.cs");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(after)!);
        File.Move(before, after);
        var paths = new[] { Path(after, "after/Service.cs") };
        var secondSearcher = Searcher(paths);
        var second = Assert.Single((await secondSearcher.SearchAsync(
            Request(workspace.Root, "Save"))).Matches);
        var resolver = new SymbolEntityResolver(
            new StubTraverser(paths),
            NoOwnershipResolver.Instance);

        var resolution = await resolver.ResolveAsync(
            first.Id,
            new WorkspaceTraversalRequest(workspace.Root));

        Assert.NotEqual(first.Id, second.Id);
        Assert.True(resolution.Resolved);
        var resolved = Assert.Single(resolution.Matches);
        Assert.Equal(second.Id, resolved.Id);
        Assert.Equal("after/Service.cs", resolved.Range.Start.Path);
        Assert.False(Directory.Exists(stateDirectory));
    }

    [Fact]
    public async Task Byte_identical_declarations_have_distinct_ids_and_moved_collisions_are_ambiguous()
    {
        using var workspace = new TestWorkspace();
        const string contents = "namespace Demo; partial class Widget { }";
        var first = await workspace.WriteAsync("a/Widget.cs", contents);
        var second = await workspace.WriteAsync("b/Widget.cs", contents);
        var initialPaths = new[]
        {
            Path(first, "a/Widget.cs"),
            Path(second, "b/Widget.cs"),
        };
        var initial = await Searcher(initialPaths).SearchAsync(
            Request(workspace.Root, "Widget"));

        Assert.Equal(2, initial.Matches.Select(match => match.Id).Distinct().Count());
        var firstId = initial.Matches.Single(match =>
            match.Range.Start.Path == "a/Widget.cs").Id;
        var exact = await new SymbolEntityResolver(
            new StubTraverser(initialPaths),
            NoOwnershipResolver.Instance).ResolveAsync(
                firstId,
                new WorkspaceTraversalRequest(workspace.Root));
        Assert.True(exact.Resolved);
        Assert.False(exact.Ambiguous);
        Assert.Equal("a/Widget.cs", Assert.Single(exact.Matches).Range.Start.Path);

        var moved = System.IO.Path.Combine(workspace.Root, "c", "Widget.cs");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(moved)!);
        File.Move(first, moved);
        var movedPaths = new[]
        {
            Path(second, "b/Widget.cs"),
            Path(moved, "c/Widget.cs"),
        };
        var ambiguous = await new SymbolEntityResolver(
            new StubTraverser(movedPaths),
            NoOwnershipResolver.Instance).ResolveAsync(
                firstId,
                new WorkspaceTraversalRequest(workspace.Root));

        Assert.False(ambiguous.Resolved);
        Assert.True(ambiguous.Ambiguous);
        Assert.Equal(2, ambiguous.Matches.Count);
    }

    [Fact]
    public async Task Changed_signature_and_overloads_return_stale_replacements_and_query()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync(
            "Service.cs",
            "namespace Demo; class Service { public void Save(int value) { } }");
        var paths = new[] { Path(source, "Service.cs") };
        var original = Assert.Single((await Searcher(paths).SearchAsync(
            Request(workspace.Root, "Save"))).Matches);
        await workspace.WriteAsync(
            "Service.cs",
            "namespace Demo; class Service { public void Save() { } public void Save(string value) { } }");

        var resolution = await Resolver(paths).ResolveAsync(
            original.Id,
            new WorkspaceTraversalRequest(workspace.Root));

        Assert.True(resolution.Stale);
        Assert.Equal("evidence.stale_id", resolution.ErrorCode);
        Assert.Empty(resolution.Matches);
        Assert.Equal(
            ["Save()", "Save(string)"],
            resolution.ReplacementCandidates.Select(
                static replacement => replacement.Signature));
        Assert.Equal(
            "dnaxi search symbol 'Save' --fields id signature owning_projects variant_count variants --full",
            resolution.Query);
    }

    [Theory]
    [InlineData("src/A/A.csproj", "Debug", "net8.0", "src/B/B.csproj", "Debug", "net8.0")]
    [InlineData("src/A/A.csproj", "Debug", "net8.0", "src/A/A.csproj", "Release", "net8.0")]
    [InlineData("src/A/A.csproj", "Debug", "net8.0", "src/A/A.csproj", "Debug", "net10.0")]
    public async Task Project_configuration_and_framework_changes_make_identity_stale(
        string beforeProject,
        string beforeConfiguration,
        string beforeFramework,
        string afterProject,
        string afterConfiguration,
        string afterFramework)
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync(
            "Shared.cs",
            "namespace Demo; class Shared { }");
        var paths = new[] { Path(source, "Shared.cs") };
        var beforeOwnership = new VariantOwnershipResolver(
            new FileCompilerVariant(
                beforeProject,
                beforeConfiguration,
                beforeFramework,
                "shared-context"));
        var original = Assert.Single((await Searcher(
            paths,
            beforeOwnership).SearchAsync(
                Request(workspace.Root, "Shared"))).Matches);
        var afterOwnership = new VariantOwnershipResolver(
            new FileCompilerVariant(
                afterProject,
                afterConfiguration,
                afterFramework,
                "shared-context"));

        var resolution = await new SymbolEntityResolver(
            new StubTraverser(paths),
            afterOwnership).ResolveAsync(
                original.Id,
                new WorkspaceTraversalRequest(workspace.Root));

        Assert.True(resolution.Stale);
        var replacement = Assert.Single(resolution.ReplacementCandidates);
        Assert.NotEqual(original.Id, replacement.Id);
        var variant = Assert.Single(replacement.Variants);
        Assert.Equal(afterProject, variant.Project);
        Assert.Equal(afterConfiguration, variant.Configuration);
        Assert.Equal(afterFramework, variant.Framework);
        Assert.Equal("unresolved", variant.Meaning);
    }

    [Fact]
    public async Task One_logical_declaration_exposes_distinct_framework_variants()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync(
            "Shared.cs",
            "namespace Demo; class Shared { }");
        var ownership = new VariantOwnershipResolver(
            new FileCompilerVariant(
                "src/App/App.csproj",
                "Debug",
                "net8.0",
                "net8-context"),
            new FileCompilerVariant(
                "src/App/App.csproj",
                "Debug",
                "net10.0",
                "net10-context"));

        var match = Assert.Single((await Searcher(
            [Path(source, "Shared.cs")],
            ownership).SearchAsync(
                Request(workspace.Root, "Shared"))).Matches);

        Assert.Equal(2, match.VariantCount);
        Assert.Equal(
            ["net10.0", "net8.0"],
            match.Variants.Select(static variant => variant.Framework));
        Assert.All(
            match.Variants,
            static variant => Assert.Equal("unresolved", variant.Meaning));
    }

    [Fact]
    public async Task Conditional_compilation_variants_do_not_claim_default_parse_meaning()
    {
        using var workspace = new TestWorkspace();
        var source = await workspace.WriteAsync(
            "Conditional.cs",
            """
            class Conditional
            {
            #if NET8_0
                void Save(int value) { }
            #else
                void Save(string value) { }
            #endif
            }
            """);
        var ownership = new VariantOwnershipResolver(
            new FileCompilerVariant(
                "App.csproj",
                configuration: null,
                framework: "net8.0",
                contextFingerprint: "shared-context"),
            new FileCompilerVariant(
                "App.csproj",
                configuration: null,
                framework: "net10.0",
                contextFingerprint: "shared-context"));

        var match = Assert.Single((await Searcher(
            [Path(source, "Conditional.cs")],
            ownership).SearchAsync(
                Request(workspace.Root, "Save"))).Matches);

        Assert.Equal("Save(string)", match.Signature);
        Assert.Equal(2, match.VariantCount);
        Assert.All(
            match.Variants,
            static variant => Assert.Equal("unresolved", variant.Meaning));
    }

    [Fact]
    public async Task Previous_identity_version_is_rejected_not_reclassified_stale()
    {
        using var workspace = new TestWorkspace();
        var resolver = Resolver([]);
        var legacyId = "symbol/v1/U2F2ZQ/"
                       + new string('a', 64)
                       + "/"
                       + new string('b', 64);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(
                    legacyId,
                    new WorkspaceTraversalRequest(workspace.Root))
                .AsTask());

        Assert.Contains("symbol/v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Primary_constructor_resolves_ids_emitted_before_signature_enrichment()
    {
        using var workspace = new TestWorkspace();
        const string contents = "record Widget(int Value);";
        var source = await workspace.WriteAsync("Symbols.cs", contents);
        var paths = new[] { Path(source, "Symbols.cs") };
        var legacyId = LegacyPrimaryConstructorId(
            "Widget",
            "record",
            "Widget",
            contents,
            "Symbols.cs");

        var resolution = await Resolver(paths).ResolveAsync(
            legacyId,
            new WorkspaceTraversalRequest(workspace.Root));

        Assert.True(resolution.Resolved);
        Assert.Equal("Widget(int)", Assert.Single(resolution.Matches).Signature);
    }

    private static string LegacyPrimaryConstructorId(
        string name,
        string kind,
        string fullyQualifiedName,
        string contents,
        string relativePath)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var stable = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(stable, "dotnet-axi/symbol-entity-stable/v2");
        Append(stable, kind);
        Append(stable, fullyQualifiedName);
        Append(stable, name);
        Append(stable, contentHash);
        Append(stable, "0");
        Append(stable, contents.Length.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        Append(stable, "0");

        using var location = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(location, "dotnet-axi/symbol-entity-location/v2");
        Append(location, relativePath);
        Append(location, "workspace");
        return "symbol/v2/"
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(name)).TrimEnd('=')
            + "/"
            + Convert.ToHexStringLower(stable.GetHashAndReset())
            + "/"
            + Convert.ToHexStringLower(location.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static SymbolDeclarationSearcher Searcher(
        IReadOnlyList<WorkspaceTraversalPath> paths,
        IFileOwnershipResolver? ownership = null) =>
        new(new StubTraverser(paths), ownership ?? NoOwnershipResolver.Instance);

    private static SymbolEntityResolver Resolver(
        IReadOnlyList<WorkspaceTraversalPath> paths) =>
        new(new StubTraverser(paths), NoOwnershipResolver.Instance);

    private static SymbolDeclarationSearchRequest Request(string root, string query) =>
        new(query, new WorkspaceTraversalRequest(root), includeTests: true);

    private static WorkspaceTraversalPath Path(string fullPath, string relativePath) =>
        new(fullPath, relativePath, isExternal: false);

    private sealed class StubTraverser(IReadOnlyList<WorkspaceTraversalPath> paths)
        : IWorkspacePathTraverser
    {
        public IReadOnlyList<WorkspaceTraversalPath> Traverse(
            WorkspaceTraversalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return paths;
        }
    }

    private sealed class NoOwnershipResolver : IFileOwnershipResolver
    {
        public static NoOwnershipResolver Instance { get; } = new();

        public IReadOnlyList<string> GetOwningProjects(WorkspaceTraversalPath path) => [];
    }

    private sealed class VariantOwnershipResolver(
        params FileCompilerVariant[] variants) : IFileOwnershipResolver
    {
        public IReadOnlyList<string> GetOwningProjects(
            WorkspaceTraversalPath path) =>
            variants.Select(static variant => variant.Project)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

        public IReadOnlyList<FileCompilerVariant> GetCompilerVariants(
            WorkspaceTraversalPath path) => variants;
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotnet-axi-entity-identity-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task<string> WriteAsync(string relativePath, string contents)
        {
            var path = System.IO.Path.Combine(Root, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
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
