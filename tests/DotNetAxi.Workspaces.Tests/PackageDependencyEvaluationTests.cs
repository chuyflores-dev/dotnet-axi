using DotNetAxi.DotNet;
using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class PackageDependencyEvaluationTests
{
    [Fact]
    public async Task Evaluated_package_references_preserve_project_identity_and_version()
    {
        var fixtures = new RepositoryFixtureFactory();
        await using var fixture = await fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ProjectGraph",
                "coverage",
                "fixture.json"));
        await AddAssetsAsync(fixture.WorkspacePath);
        var workspace = new WorkspaceDiscoverer().Discover(fixture.WorkspacePath);
        var selection = new WorkspaceEntryPointSelector().Select(
            workspace,
            new WorkspaceSelectionRequest(project: "Unrestored"));

        var graph = new MsBuildProjectGraphEvaluator(new DotNetHostResolver())
            .Evaluate(workspace, selection);

        Assert.Equal(
            new PackageDependency(
                "src/Unrestored/Unrestored.csproj",
                "xunit",
                "2.9.3",
                "Debug",
                "net10.0"),
            Assert.Single(graph.PackageDependencies));
    }

    private static async Task AddAssetsAsync(string workspacePath)
    {
        var project = Path.Combine(
            workspacePath,
            "src",
            "Unrestored",
            "Unrestored.csproj");
        var assets = Path.Combine(
            Path.GetDirectoryName(project)!,
            "obj",
            "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        await File.WriteAllTextAsync(
            assets,
            """
            { "version": 3, "targets": { "net10.0": {} } }
            """);
    }
}
