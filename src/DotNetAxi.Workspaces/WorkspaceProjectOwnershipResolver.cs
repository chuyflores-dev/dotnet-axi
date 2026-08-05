using DotNetAxi.Contracts;

namespace DotNetAxi.Workspaces;

/// <summary>
/// Maps traversed files to passive project-directory ownership candidates.
/// This does not evaluate project items or require compilation.
/// </summary>
public sealed class WorkspaceProjectOwnershipResolver : IFileOwnershipResolver
{
    private readonly IReadOnlyList<ProjectScope> _projects;

    public WorkspaceProjectOwnershipResolver(IEnumerable<string> projectPaths)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        _projects = Array.AsReadOnly(
            projectPaths
                .Select(NormalizeProjectPath)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(static path => new ProjectScope(
                    path,
                    DirectoryPath(path)))
                .ToArray());
    }

    public IReadOnlyList<string> GetOwningProjects(
        WorkspaceTraversalPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsExternal)
        {
            return [];
        }

        return Array.AsReadOnly(
            _projects
                .Where(project => Contains(
                    project.Directory,
                    path.RelativePath))
                .Select(static project => project.Path)
                .ToArray());
    }

    private static string NormalizeProjectPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (!normalized.EndsWith(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Project ownership requires normalized C# project paths.",
                nameof(path));
        }

        return normalized;
    }

    private static string DirectoryPath(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static bool Contains(string directory, string path) =>
        directory.Length == 0
        || path.StartsWith(directory + "/", StringComparison.Ordinal);

    private sealed record ProjectScope(string Path, string Directory);
}
