using System.Xml;
using System.Xml.Linq;

namespace DotNetAxi.Workspaces;

public static class PassiveSolutionProjectReader
{
    public static IReadOnlyList<string> ReadProjectPaths(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var fullSolutionPath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetDirectoryName(fullSolutionPath)!;
        var extension = Path.GetExtension(fullSolutionPath).ToLowerInvariant();
        IEnumerable<string> paths = extension switch
        {
            ".slnx" => ReadSlnx(fullSolutionPath),
            ".sln" => ReadSln(fullSolutionPath),
            _ => throw new ArgumentException(
                "Passive solution membership requires a .sln or .slnx path.",
                nameof(solutionPath)),
        };

        return Array.AsReadOnly(paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(
                path
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar),
                solutionDirectory))
            .Where(static path => Path.GetExtension(path).EndsWith(
                "proj",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(WorkspacePathIdentity.Comparer)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    private static IEnumerable<string> ReadSlnx(string solutionPath)
    {
        using var stream = File.OpenRead(solutionPath);
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
        return XDocument.Load(reader, LoadOptions.None)
            .Descendants()
            .Where(static element => element.Name.LocalName.Equals(
                "Project",
                StringComparison.Ordinal))
            .Select(static element => element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "Path",
                    StringComparison.OrdinalIgnoreCase))?.Value)
            .OfType<string>()
            .ToArray();
    }

    private static IEnumerable<string> ReadSln(string solutionPath) =>
        File.ReadLines(solutionPath)
            .Where(static line => line.StartsWith(
                "Project(\"",
                StringComparison.Ordinal))
            .Select(static line => line.Split('"'))
            .Where(static fields => fields.Length >= 6)
            .Select(static fields => fields[5])
            .ToArray();

}
