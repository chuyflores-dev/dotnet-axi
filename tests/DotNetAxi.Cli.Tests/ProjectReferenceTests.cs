using System.Xml.Linq;

namespace DotNetAxi.Cli.Tests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void Source_projects_follow_the_documented_dependency_direction()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var projects = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var contractsProject = Path.Combine(
            sourceRoot,
            "DotNetAxi.Contracts",
            "DotNetAxi.Contracts.csproj");
        var cliProject = Path.Combine(
            sourceRoot,
            "DotNetAxi.Cli",
            "DotNetAxi.Cli.csproj");

        Assert.Empty(ReadProjectReferences(contractsProject));

        var expectedCliReferences = projects
            .Where(path => !PathEquals(path, cliProject))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedCliReferences, ReadProjectReferences(cliProject));

        foreach (var project in projects.Where(path =>
                     !PathEquals(path, contractsProject) &&
                     !PathEquals(path, cliProject)))
        {
            Assert.Equal([contractsProject], ReadProjectReferences(project));
        }
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project has no directory: {projectPath}");
        var document = XDocument.Load(projectPath);

        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!, projectDirectory))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-axi.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find dotnet-axi.slnx above {AppContext.BaseDirectory}.");
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
