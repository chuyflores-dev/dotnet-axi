using DotNetAxi.Contracts;

namespace DotNetAxi.Workspaces.Tests;

public sealed class WorkspaceProjectOwnershipResolverTests
{
    [Fact]
    public void Nested_projects_produce_sorted_multi_ownership_once()
    {
        var resolver = new WorkspaceProjectOwnershipResolver(
        [
            "src/Nested/Nested.csproj",
            "Root.csproj",
            "src/Nested/Nested.csproj",
        ]);

        var owners = resolver.GetOwningProjects(new WorkspaceTraversalPath(
            "/unused/Shared.cs",
            "src/Nested/Shared.cs",
            isExternal: false));

        Assert.Equal(
            ["Root.csproj", "src/Nested/Nested.csproj"],
            owners);
    }

    [Fact]
    public void Sibling_and_external_paths_are_not_owned()
    {
        var resolver = new WorkspaceProjectOwnershipResolver(
            ["src/App/App.csproj"]);

        var sibling = resolver.GetOwningProjects(new WorkspaceTraversalPath(
            "/unused/Sibling.cs",
            "src/Application/Sibling.cs",
            isExternal: false));
        var external = resolver.GetOwningProjects(new WorkspaceTraversalPath(
            "/external/External.cs",
            "../external/External.cs",
            isExternal: true));

        Assert.Empty(sibling);
        Assert.Empty(external);
    }

    [Fact]
    public async Task Project_variants_expose_configuration_framework_and_content_identity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-owner-variant-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "App"));
        try
        {
            var projectPath = Path.Combine(root, "src", "App", "App.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks><Configurations>Debug;Release</Configurations></PropertyGroup></Project>");
            var path = new WorkspaceTraversalPath(
                Path.Combine(root, "src", "App", "Shared.cs"),
                "src/App/Shared.cs",
                isExternal: false);
            var before = new WorkspaceProjectOwnershipResolver(
                root,
                ["src/App/App.csproj"]).GetCompilerVariants(path);

            Assert.Equal(4, before.Count);
            Assert.Equal(
                [
                    ("Debug", "net10.0"),
                    ("Debug", "net8.0"),
                    ("Release", "net10.0"),
                    ("Release", "net8.0"),
                ],
                before.Select(static variant =>
                    (variant.Configuration!, variant.Framework!)));
            Assert.All(before, static variant =>
                Assert.Matches("^[a-f0-9]{64}$", variant.ContextFingerprint));

            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
            var after = new WorkspaceProjectOwnershipResolver(
                root,
                ["src/App/App.csproj"]).GetCompilerVariants(path);

            var changed = Assert.Single(after);
            Assert.Null(changed.Configuration);
            Assert.Equal("net9.0", changed.Framework);
            Assert.NotEqual(
                before[0].ContextFingerprint,
                changed.ContextFingerprint);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Variant_discovery_rejects_project_paths_outside_the_workspace()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new WorkspaceProjectOwnershipResolver(
                Path.GetTempPath(),
                ["../Outside.csproj"]));

        Assert.Equal("projectPaths", exception.ParamName);
    }

    [Fact]
    public async Task Unevaluated_framework_segments_remain_explicitly_unknown()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-owner-unknown-variant-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net8.0;$(AdditionalTargetFrameworks)</TargetFrameworks></PropertyGroup></Project>");
            var variants = new WorkspaceProjectOwnershipResolver(
                root,
                ["App.csproj"]).GetCompilerVariants(
                    new WorkspaceTraversalPath(
                        Path.Combine(root, "Shared.cs"),
                        "Shared.cs",
                        isExternal: false));

            Assert.Equal(2, variants.Count);
            Assert.Contains(variants, static variant =>
                variant.Framework is null);
            Assert.Contains(variants, static variant =>
                variant.Framework == "net8.0");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
