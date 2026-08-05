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
}
