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
            match => Assert.StartsWith("symbol/v1/", match.Id));
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

    private static SymbolDeclarationSearcher Searcher(
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
