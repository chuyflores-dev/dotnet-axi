using DotNetAxi.Contracts;

namespace DotNetAxi.Search.Tests;

public sealed class FileEntityIdentityTests
{
    [Theory]
    [InlineData("src/App.cs", false)]
    [InlineData("../external/App.cs", true)]
    public void Path_overload_matches_traversal_identity(
        string relativePath,
        bool external)
    {
        var traversalPath = new WorkspaceTraversalPath(
            Path.GetFullPath("App.cs"),
            relativePath,
            external);

        Assert.Equal(
            FileEntityIdentity.Create(traversalPath),
            FileEntityIdentity.Create(relativePath, external));
    }
}
