using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
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
        _projects = CreateProjectScopes(workspaceRoot: null, projectPaths);
    }

    public WorkspaceProjectOwnershipResolver(
        string workspaceRoot,
        IEnumerable<string> projectPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _projects = CreateProjectScopes(
            Path.GetFullPath(workspaceRoot),
            projectPaths);
    }

    private static IReadOnlyList<ProjectScope> CreateProjectScopes(
        string? workspaceRoot,
        IEnumerable<string> projectPaths)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        return Array.AsReadOnly(
            projectPaths
                .Select(NormalizeProjectPath)
                .Distinct(WorkspacePathIdentity.Comparer)
                .Order(StringComparer.Ordinal)
                .Select(path => new ProjectScope(
                    path,
                    DirectoryPath(path),
                    ProjectVariants(workspaceRoot, path),
                    LinkedSources(workspaceRoot, path)))
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
                .Where(project => Contains(project.Directory, path.RelativePath)
                    || project.LinkedSources.Contains(path.RelativePath))
                .Select(static project => project.Path)
                .ToArray());
    }

    public IReadOnlyList<FileCompilerVariant> GetCompilerVariants(
        WorkspaceTraversalPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsExternal)
        {
            return [];
        }

        return Array.AsReadOnly(
            _projects
                .Where(project => Contains(project.Directory, path.RelativePath)
                    || project.LinkedSources.Contains(path.RelativePath))
                .SelectMany(static project => project.Variants)
                .OrderBy(static variant => variant.Project, StringComparer.Ordinal)
                .ThenBy(static variant => variant.Configuration, StringComparer.Ordinal)
                .ThenBy(static variant => variant.Framework, StringComparer.Ordinal)
                .ThenBy(
                    static variant => variant.ContextFingerprint,
                    StringComparer.Ordinal)
                .ToArray());
    }

    private static string NormalizeProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Project ownership requires a nonblank project path.",
                "projectPaths");
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2
                && char.IsAsciiLetter(normalized[0])
                && normalized[1] == ':')
            || segments.Any(static segment => segment is "." or "..")
            || !normalized.EndsWith(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Project ownership requires normalized workspace-relative C# project paths.",
                "projectPaths");
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
        || path.StartsWith(
            directory + "/",
            WorkspacePathIdentity.Comparison);

    private static IReadOnlyList<FileCompilerVariant> ProjectVariants(
        string? workspaceRoot,
        string project)
    {
        if (workspaceRoot is null)
        {
            return [new FileCompilerVariant(
                project,
                configuration: null,
                framework: null,
                contextFingerprint: project)];
        }

        var projectPath = Path.Combine(
            workspaceRoot,
            project.Replace('/', Path.DirectorySeparatorChar));
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(projectPath);
        }
        catch (IOException)
        {
            return [new FileCompilerVariant(
                project,
                configuration: null,
                framework: null,
                contextFingerprint: project)];
        }
        catch (UnauthorizedAccessException)
        {
            return [new FileCompilerVariant(
                project,
                configuration: null,
                framework: null,
                contextFingerprint: project)];
        }

        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var properties = ReadVariantProperties(bytes);
        var configurations = Expand(properties.Configurations);
        var frameworks = Expand(properties.Frameworks);
        return Array.AsReadOnly(
            configurations
                .SelectMany(configuration => frameworks.Select(framework =>
                    new FileCompilerVariant(
                        project,
                        configuration,
                        framework,
                        fingerprint)))
                .ToArray());
    }

    private static IReadOnlySet<string> LinkedSources(
        string? workspaceRoot,
        string project)
    {
        if (workspaceRoot is null)
        {
            return new HashSet<string>(WorkspacePathIdentity.Comparer);
        }

        var projectPath = Path.GetFullPath(
            project.Replace('/', Path.DirectorySeparatorChar),
            workspaceRoot);
        try
        {
            using var stream = File.OpenRead(projectPath);
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                });
            var document = XDocument.Load(reader, LoadOptions.None);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            return document.Descendants()
                .Where(static element => element.Name.LocalName.Equals(
                    "Compile",
                    StringComparison.OrdinalIgnoreCase))
                .Select(static element => element.Attribute("Include")?.Value.Trim())
                .OfType<string>()
                .Where(static include => include.Length > 0
                    && !IsUnevaluated(include)
                    && include.IndexOfAny(['*', '?']) < 0)
                .Select(include => Path.GetFullPath(
                    include
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar),
                    projectDirectory))
                .Where(path => IsWithin(workspaceRoot, path))
                .Select(path => Path.GetRelativePath(workspaceRoot, path)
                    .Replace('\\', '/'))
                .ToHashSet(WorkspacePathIdentity.Comparer);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or XmlException
            or ArgumentException)
        {
            return new HashSet<string>(WorkspacePathIdentity.Comparer);
        }
    }

    private static IReadOnlyList<string?> Expand(
        PassivePropertyValues properties)
    {
        var values = properties.Literals.Cast<string?>().ToList();
        if (values.Count == 0 || properties.HasUnevaluatedValue)
        {
            values.Add(null);
        }

        return Array.AsReadOnly(values.ToArray());
    }

    private static ProjectVariantProperties ReadVariantProperties(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                });
            var document = XDocument.Load(reader, LoadOptions.None);
            return new ProjectVariantProperties(
                PropertyValues(document, "Configurations"),
                PropertyValues(
                    document,
                    "TargetFrameworks",
                    "TargetFramework"));
        }
        catch (XmlException)
        {
            return new ProjectVariantProperties(
                new PassivePropertyValues([], HasUnevaluatedValue: true),
                new PassivePropertyValues([], HasUnevaluatedValue: true));
        }
    }

    private static PassivePropertyValues PropertyValues(
        XDocument document,
        params string[] names)
    {
        var segments = document.Descendants()
            .Where(element => names.Contains(
                element.Name.LocalName,
                StringComparer.OrdinalIgnoreCase))
            .SelectMany(static element => element.Value.Split(';'))
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .ToArray();
        var hasUnevaluatedValue = segments.Any(IsUnevaluated);
        return new PassivePropertyValues(
            Array.AsReadOnly(segments
                .Where(value => !IsUnevaluated(value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()),
            hasUnevaluatedValue);
    }

    private static bool IsUnevaluated(string value) =>
        value.Contains("$(", StringComparison.Ordinal)
        || value.Contains("@(", StringComparison.Ordinal)
        || value.Contains("%(", StringComparison.Ordinal);

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathFullyQualified(relative)
            && relative != ".."
            && !relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            && !relative.StartsWith(
                ".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    private sealed record ProjectScope(
        string Path,
        string Directory,
        IReadOnlyList<FileCompilerVariant> Variants,
        IReadOnlySet<string> LinkedSources);

    private sealed record ProjectVariantProperties(
        PassivePropertyValues Configurations,
        PassivePropertyValues Frameworks);

    private sealed record PassivePropertyValues(
        IReadOnlyList<string> Literals,
        bool HasUnevaluatedValue);
}
