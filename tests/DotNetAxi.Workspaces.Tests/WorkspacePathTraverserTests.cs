using DotNetAxi.Contracts;
using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class WorkspacePathTraverserTests
{
    [Fact]
    public void Pre_cancelled_traversal_does_not_enumerate_the_workspace()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _traverser.Traverse(new WorkspaceTraversalRequest(Directory.GetCurrentDirectory()), cancellation.Token));
    }

    [Fact]
    public void Active_cancellation_stops_directory_enumeration_before_sorting_and_materialization()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotnet-axi-traversal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "first.cs"), "class First { }");
            File.WriteAllText(Path.Combine(root, "second.cs"), "class Second { }");
            using var cancellation = new CancellationTokenSource();
            var observedEntries = 0;
            var traverser = new WorkspacePathTraverser(() =>
            {
                observedEntries++;
                cancellation.Cancel();
            });

            Assert.Throws<OperationCanceledException>(() => traverser.Traverse(new WorkspaceTraversalRequest(root), cancellation.Token));
            Assert.Equal(1, observedEntries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private readonly RepositoryFixtureFactory _fixtures = new();
    private readonly WorkspacePathTraverser _traverser = new();

    [Fact]
    public async Task Traversal_applies_only_workspace_git_rules_and_tool_owned_defaults()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        await File.WriteAllTextAsync(
            Path.Combine(fixture.RootPath, ".gitignore"),
            "ignored-by-parent.cs\n");
        var globalIgnoreDirectory = Path.Combine(
            fixture.HomePath,
            ".config",
            "git");
        Directory.CreateDirectory(globalIgnoreDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(globalIgnoreDirectory, "ignore"),
            "ignored-by-global.cs\n");
        var infoDirectory = Path.Combine(
            fixture.WorkspacePath,
            ".git",
            "info");
        Directory.CreateDirectory(infoDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(infoDirectory, "exclude"),
            "ignored-by-info.cs\n");

        var paths = Traverse(
            fixture.WorkspacePath,
            new TraversalConfiguration(
                exclusionPatterns: ["configured/**"],
                generatedPathPatterns: ["generated/PatternGenerated.cs"]));

        Assert.Equal(paths, paths.Order(StringComparer.Ordinal));
        Assert.Contains(".hidden/Visible.cs", paths);
        Assert.Contains("src/Included.cs", paths);
        Assert.Contains("explicit/Included.cs", paths);
        Assert.Contains("ignored-by-parent.cs", paths);
        Assert.Contains("ignored-by-global.cs", paths);
        Assert.Contains("ignored-by-rg.cs", paths);
        Assert.Contains("ignored-by-the-tool-yaml.cs", paths);
        Assert.DoesNotContain("ignored/ByGitIgnore.cs", paths);
        Assert.DoesNotContain("ignored-by-info.cs", paths);
        Assert.DoesNotContain("nested/Excluded.cs", paths);
        Assert.DoesNotContain("configured/Excluded.cs", paths);
        Assert.DoesNotContain("generated/HeaderGenerated.cs", paths);
        Assert.DoesNotContain("generated/Form1.Designer.cs", paths);
        Assert.DoesNotContain("generated/PatternGenerated.cs", paths);
        Assert.DoesNotContain("generated/Suffix.g.cs", paths);
        Assert.DoesNotContain("bin/Build.cs", paths);
        Assert.DoesNotContain("obj/Build.cs", paths);
        Assert.DoesNotContain(paths, static path => path.StartsWith(
            ".git/",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explicit_scope_narrows_paths_and_includes_build_output_only_in_scope()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());

        var paths = Traverse(
            fixture.WorkspacePath,
            explicitPaths: ["bin", "explicit/Included.cs"]);

        Assert.Equal(["bin/Build.cs", "explicit/Included.cs"], paths);
    }

    [Fact]
    public async Task Generated_configuration_default_includes_detected_source_without_including_build_output()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());

        var paths = Traverse(
            fixture.WorkspacePath,
            new TraversalConfiguration(
                generatedPathPatterns: ["generated/PatternGenerated.cs"],
                includeGeneratedByDefault: true));

        Assert.Contains("generated/HeaderGenerated.cs", paths);
        Assert.Contains("generated/PatternGenerated.cs", paths);
        Assert.Contains("generated/Suffix.g.cs", paths);
        Assert.DoesNotContain("bin/Build.cs", paths);
        Assert.DoesNotContain("obj/Build.cs", paths);
    }

    [Fact]
    public async Task Directory_links_are_not_followed()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var internalDirectoryLink = Path.Combine(
            fixture.WorkspacePath,
            "linked-internal");
        var externalDirectoryLink = Path.Combine(
            fixture.WorkspacePath,
            "linked-external");
        if (!TryCreateDirectorySymbolicLink(
                internalDirectoryLink,
                Path.Combine(fixture.WorkspacePath, "src"))
            || !TryCreateDirectorySymbolicLink(
                externalDirectoryLink,
                fixture.ExternalPath))
        {
            return;
        }

        var paths = Traverse(fixture.WorkspacePath);

        Assert.DoesNotContain(paths, static path => path.StartsWith(
            "linked-internal/",
            StringComparison.Ordinal));
        Assert.DoesNotContain(paths, static path => path.StartsWith(
            "linked-external/",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_links_are_evaluated_without_following_directory_links()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var internalFileLink = Path.Combine(fixture.WorkspacePath, "Alias.cs");
        var externalFileLink = Path.Combine(
            fixture.WorkspacePath,
            "ExternalAlias.cs");
        if (!TryCreateFileSymbolicLink(
                internalFileLink,
                Path.Combine(fixture.WorkspacePath, "src", "Included.cs"))
            || !TryCreateFileSymbolicLink(
                externalFileLink,
                Path.Combine(fixture.ExternalPath, "External.cs")))
        {
            return;
        }

        var paths = Traverse(fixture.WorkspacePath);

        Assert.Contains("Alias.cs", paths);
        Assert.DoesNotContain("ExternalAlias.cs", paths);
    }

    [Fact]
    public async Task External_explicit_file_and_directory_scopes_are_labeled_and_deduplicated()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var externalFile = Path.Combine(fixture.ExternalPath, "External.cs");

        var paths = _traverser.Traverse(new WorkspaceTraversalRequest(
                fixture.WorkspacePath,
                explicitPaths:
                [
                    "../external",
                    "../external/External.cs",
                    externalFile,
                ]));

        var path = Assert.Single(paths);
        Assert.Equal("../external/External.cs", path.RelativePath);
        Assert.Equal(Path.GetFullPath(externalFile), path.FullPath);
        Assert.True(path.IsExternal);
    }

    [Fact]
    public async Task Explicit_external_file_link_is_accepted()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var fileLink = Path.Combine(fixture.WorkspacePath, "ExternalAlias.cs");
        if (!TryCreateFileSymbolicLink(
                fileLink,
                Path.Combine(fixture.ExternalPath, "External.cs")))
        {
            return;
        }

        var filePaths = _traverser.Traverse(new WorkspaceTraversalRequest(
            fixture.WorkspacePath,
            explicitPaths: [fileLink]));
        var file = Assert.Single(filePaths);
        Assert.Equal("../external/External.cs", file.RelativePath);
        Assert.True(file.IsExternal);
    }

    [Fact]
    public async Task Explicit_external_directory_link_is_not_followed()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var directoryLink = Path.Combine(fixture.WorkspacePath, "linked-external");
        if (!TryCreateDirectorySymbolicLink(directoryLink, fixture.ExternalPath))
        {
            return;
        }

        var directoryPaths = _traverser.Traverse(new WorkspaceTraversalRequest(
            fixture.WorkspacePath,
            explicitPaths: [directoryLink]));
        Assert.Empty(directoryPaths);
    }

    [Fact]
    public async Task Explicit_file_through_directory_link_is_not_followed()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var directoryLink = Path.Combine(fixture.WorkspacePath, "linked-external");
        if (!TryCreateDirectorySymbolicLink(directoryLink, fixture.ExternalPath))
        {
            return;
        }

        var paths = _traverser.Traverse(new WorkspaceTraversalRequest(
            fixture.WorkspacePath,
            explicitPaths: [Path.Combine(directoryLink, "External.cs")]));

        Assert.Empty(paths);
    }

    [Fact]
    public async Task Explicit_file_link_uses_its_lexical_git_policy_path()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var fileLink = Path.Combine(fixture.WorkspacePath, "ExternalAlias.cs");
        if (!TryCreateFileSymbolicLink(
                fileLink,
                Path.Combine(fixture.ExternalPath, "External.cs")))
        {
            return;
        }

        await File.AppendAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".gitignore"),
            "ExternalAlias.cs\n");

        var paths = _traverser.Traverse(new WorkspaceTraversalRequest(
            fixture.WorkspacePath,
            explicitPaths: [fileLink]));

        Assert.Empty(paths);
    }

    [Fact]
    public async Task Linked_worktree_marker_uses_common_info_exclude_and_is_not_returned()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        var commonDirectory = Path.Combine(fixture.RootPath, "git-common");
        var worktreeGitDirectory = Path.Combine(
            commonDirectory,
            "worktrees",
            "fixture");
        var infoDirectory = Path.Combine(commonDirectory, "info");
        Directory.CreateDirectory(worktreeGitDirectory);
        Directory.CreateDirectory(infoDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(worktreeGitDirectory, "commondir"),
            "../..\n");
        await File.WriteAllTextAsync(
            Path.Combine(infoDirectory, "exclude"),
            "ignored-by-info.cs\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".git"),
            $"gitdir: {worktreeGitDirectory}\n");

        var paths = Traverse(fixture.WorkspacePath);

        Assert.DoesNotContain(".git", paths);
        Assert.DoesNotContain("ignored-by-info.cs", paths);
    }

    [Fact]
    public async Task Nested_negation_cannot_reinclude_a_file_beneath_an_ignored_parent()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "ignored", ".gitignore"),
            "!ByGitIgnore.cs\n");

        var paths = Traverse(fixture.WorkspacePath);

        Assert.DoesNotContain("ignored/ByGitIgnore.cs", paths);
    }

    [Fact]
    public async Task Root_and_nested_leading_slash_rules_remain_anchored()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".gitignore"),
            "/RootOnly.cs\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "RootOnly.cs"),
            "namespace Traversal;\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "src", "RootOnly.cs"),
            "namespace Traversal;\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "nested", ".gitignore"),
            "/NestedOnly.cs\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "nested", "NestedOnly.cs"),
            "namespace Traversal;\n");
        var deeperDirectory = Path.Combine(
            fixture.WorkspacePath,
            "nested",
            "deeper");
        Directory.CreateDirectory(deeperDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(deeperDirectory, "NestedOnly.cs"),
            "namespace Traversal;\n");

        var paths = Traverse(fixture.WorkspacePath);

        Assert.DoesNotContain("RootOnly.cs", paths);
        Assert.Contains("src/RootOnly.cs", paths);
        Assert.DoesNotContain("nested/NestedOnly.cs", paths);
        Assert.Contains("nested/deeper/NestedOnly.cs", paths);
    }

    [Fact]
    public async Task Escaped_leading_exclamation_matches_a_literal_file_name()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".gitignore"),
            "\\!Important.cs\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "!Important.cs"),
            "namespace Traversal;\n");

        var paths = Traverse(fixture.WorkspacePath);

        Assert.DoesNotContain("!Important.cs", paths);
    }

    [Fact]
    public async Task Git_rules_match_only_the_current_item()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".gitignore"),
            "*\n!*/\n!*.cs\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "src", "readme.txt"),
            "ignored\n");

        var paths = Traverse(fixture.WorkspacePath);

        Assert.Contains("src/Included.cs", paths);
        Assert.DoesNotContain("src/readme.txt", paths);
    }

    [Fact]
    public async Task Explicit_root_and_current_directory_relative_scopes_are_supported()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());

        var rootPaths = _traverser.Traverse(new WorkspaceTraversalRequest(
                fixture.WorkspacePath,
                explicitPaths: ["."],
                currentDirectory: fixture.WorkspacePath))
            .Select(static path => path.RelativePath)
            .ToArray();
        var absoluteRootPaths = _traverser.Traverse(new WorkspaceTraversalRequest(
                fixture.WorkspacePath,
                explicitPaths: [fixture.WorkspacePath]))
            .Select(static path => path.RelativePath)
            .ToArray();
        var nestedPaths = _traverser.Traverse(new WorkspaceTraversalRequest(
                fixture.WorkspacePath,
                explicitPaths: ["../explicit/Included.cs"],
                currentDirectory: Path.Combine(fixture.WorkspacePath, "src")))
            .Select(static path => path.RelativePath)
            .ToArray();

        Assert.Contains("bin/Build.cs", rootPaths);
        Assert.Equal(rootPaths, absoluteRootPaths);
        Assert.Equal(["explicit/Included.cs"], nestedPaths);
    }

    [Fact]
    public async Task External_ancestor_scope_includes_workspace_descendants_once()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());

        var paths = _traverser.Traverse(new WorkspaceTraversalRequest(
                fixture.WorkspacePath,
                explicitPaths: [".."],
                currentDirectory: fixture.WorkspacePath))
            .Select(static path => path.RelativePath)
            .ToArray();

        Assert.Contains("src/Included.cs", paths);
        Assert.Contains("../external/External.cs", paths);
        Assert.DoesNotContain("ignored/ByGitIgnore.cs", paths);
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Git_recursive_glob_positions_are_limited_to_documented_forms()
    {
        await using var fixture = await _fixtures.CreateAsync(ManifestPath());
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, ".gitignore"),
            "**/Leading.txt\ntrailing/**\nmiddle/**/Middle.txt\na/**b/c.txt\n");
        await WriteSourceAsync(fixture.WorkspacePath, "leading/Leading.txt");
        await WriteSourceAsync(fixture.WorkspacePath, "trailing/deep/Trailing.txt");
        await WriteSourceAsync(fixture.WorkspacePath, "middle/Middle.txt");
        await WriteSourceAsync(fixture.WorkspacePath, "middle/deep/Middle.txt");
        await WriteSourceAsync(fixture.WorkspacePath, "a/zzb/c.txt");
        await WriteSourceAsync(fixture.WorkspacePath, "a/x/b/c.txt");

        var paths = Traverse(fixture.WorkspacePath);

        Assert.DoesNotContain("leading/Leading.txt", paths);
        Assert.DoesNotContain("trailing/deep/Trailing.txt", paths);
        Assert.DoesNotContain("middle/Middle.txt", paths);
        Assert.DoesNotContain("middle/deep/Middle.txt", paths);
        Assert.DoesNotContain("a/zzb/c.txt", paths);
        Assert.Contains("a/x/b/c.txt", paths);
    }

    private string[] Traverse(
        string workspacePath,
        TraversalConfiguration? configuration = null,
        IEnumerable<string>? explicitPaths = null,
        bool? includeGenerated = null) =>
        _traverser.Traverse(new WorkspaceTraversalRequest(
                workspacePath,
                configuration,
                explicitPaths,
                includeGenerated))
            .Select(static path => path.RelativePath)
            .ToArray();

    private static string ManifestPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Traversal",
            "fixture.json");

    private static bool TryCreateDirectorySymbolicLink(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateFileSymbolicLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static async Task WriteSourceAsync(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "source\n");
    }
}
