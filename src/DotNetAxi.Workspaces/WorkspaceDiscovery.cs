using System.Collections.ObjectModel;

namespace DotNetAxi.Workspaces;

public enum WorkspaceKind
{
    Git,
    Configured,
    Solution,
    Project,
    Directory,
}

public enum WorkspaceSolutionKind
{
    Sln,
    Slnx,
}

public enum WorkspaceRootMarkerKind
{
    Configuration,
    SdkSelection,
    BuildProperties,
    BuildTargets,
    CentralPackageManagement,
}

public enum WorkspaceCapabilityKind
{
    SolutionFilter,
    FileBasedCSharpApplication,
    UnsupportedProject,
}

public enum WorkspaceCapabilitySupport
{
    ReportedOnly,
}

public sealed record WorkspaceSolution(
    string Path,
    WorkspaceSolutionKind Kind);

public sealed record WorkspaceProject(string Path);

public sealed record WorkspaceRootMarker(
    string Path,
    WorkspaceRootMarkerKind Kind);

public sealed record WorkspaceCapability(
    string Path,
    WorkspaceCapabilityKind Kind,
    WorkspaceCapabilitySupport Support);

public sealed class WorkspaceDiscoveryResult
{
    internal WorkspaceDiscoveryResult(
        string rootPath,
        string currentDirectory,
        WorkspaceKind workspaceKind,
        IEnumerable<WorkspaceSolution> solutions,
        IEnumerable<WorkspaceProject> projects,
        IEnumerable<WorkspaceRootMarker> rootMarkers,
        IEnumerable<WorkspaceCapability> capabilities)
    {
        RootPath = rootPath;
        CurrentDirectory = currentDirectory;
        WorkspaceKind = workspaceKind;
        Solutions = Copy(solutions);
        Projects = Copy(projects);
        RootMarkers = Copy(rootMarkers);
        Capabilities = Copy(capabilities);
    }

    public string RootPath { get; }

    public string CurrentDirectory { get; }

    public WorkspaceKind WorkspaceKind { get; }

    public IReadOnlyList<WorkspaceSolution> Solutions { get; }

    public IReadOnlyList<WorkspaceProject> Projects { get; }

    public IReadOnlyList<WorkspaceRootMarker> RootMarkers { get; }

    public IReadOnlyList<WorkspaceCapability> Capabilities { get; }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed class WorkspaceDiscoverer
{
    private static readonly IReadOnlyDictionary<string, WorkspaceRootMarkerKind>
        RootMarkerKinds =
            new ReadOnlyDictionary<string, WorkspaceRootMarkerKind>(
                new Dictionary<string, WorkspaceRootMarkerKind>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-axi.yml"] =
                        WorkspaceRootMarkerKind.Configuration,
                    ["global.json"] =
                        WorkspaceRootMarkerKind.SdkSelection,
                    ["Directory.Build.props"] =
                        WorkspaceRootMarkerKind.BuildProperties,
                    ["Directory.Build.targets"] =
                        WorkspaceRootMarkerKind.BuildTargets,
                    ["Directory.Packages.props"] =
                        WorkspaceRootMarkerKind.CentralPackageManagement,
                });

    public WorkspaceDiscoveryResult Discover(string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var currentPath = Path.GetFullPath(currentDirectory);
        if (!Directory.Exists(currentPath))
        {
            throw new DirectoryNotFoundException(
                $"Workspace discovery directory '{currentPath}' does not exist.");
        }

        var current = new DirectoryInfo(currentPath);
        var (root, kind) = FindWorkspaceRoot(current);
        return Catalog(root.FullName, currentPath, kind);
    }

    private static (DirectoryInfo Root, WorkspaceKind Kind) FindWorkspaceRoot(
        DirectoryInfo current)
    {
        var root = FindAncestor(current, IsGitRoot);
        if (root is not null)
        {
            return (root, WorkspaceKind.Git);
        }

        root = FindAncestor(
            current,
            directory => ContainsFile(directory, "dotnet-axi.yml"));
        if (root is not null)
        {
            return (root, WorkspaceKind.Configured);
        }

        root = FindAncestor(
            current,
            directory => ContainsFileWithExtension(
                directory,
                ".sln",
                ".slnx"));
        if (root is not null)
        {
            return (root, WorkspaceKind.Solution);
        }

        root = FindAncestor(
            current,
            directory => ContainsFileWithExtension(directory, ".csproj"));
        return root is null
            ? (current, WorkspaceKind.Directory)
            : (root, WorkspaceKind.Project);
    }

    private static WorkspaceDiscoveryResult Catalog(
        string rootPath,
        string currentDirectory,
        WorkspaceKind workspaceKind)
    {
        var files = EnumerateWorkspaceFiles(rootPath).ToArray();
        var pathResolver = new WorkspacePathResolver(
            rootPath,
            currentDirectory);
        var projectDirectories = files
            .Where(static file => file.Extension.Equals(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
            .Select(static file => file.DirectoryName!)
            .Distinct(PathComparer())
            .ToArray();
        var solutions = new List<WorkspaceSolution>();
        var projects = new List<WorkspaceProject>();
        var capabilities = new List<WorkspaceCapability>();

        foreach (var file in files)
        {
            var relativePath = pathResolver.NormalizeContainedOutput(
                file.FullName);
            var extension = file.Extension;
            if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                solutions.Add(
                    new WorkspaceSolution(
                        relativePath,
                        WorkspaceSolutionKind.Sln));
            }
            else if (extension.Equals(
                         ".slnx",
                         StringComparison.OrdinalIgnoreCase))
            {
                solutions.Add(
                    new WorkspaceSolution(
                        relativePath,
                        WorkspaceSolutionKind.Slnx));
            }
            else if (extension.Equals(
                         ".csproj",
                         StringComparison.OrdinalIgnoreCase))
            {
                projects.Add(new WorkspaceProject(relativePath));
            }
            else if (extension.Equals(
                         ".slnf",
                         StringComparison.OrdinalIgnoreCase))
            {
                capabilities.Add(
                    ReportedCapability(
                        relativePath,
                        WorkspaceCapabilityKind.SolutionFilter));
            }
            else if (IsUnsupportedProject(extension))
            {
                capabilities.Add(
                    ReportedCapability(
                        relativePath,
                        WorkspaceCapabilityKind.UnsupportedProject));
            }
            else if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                     && !projectDirectories.Any(
                         projectDirectory => IsWithinDirectory(
                             file.FullName,
                             projectDirectory)))
            {
                capabilities.Add(
                    ReportedCapability(
                        relativePath,
                        WorkspaceCapabilityKind.FileBasedCSharpApplication));
            }
        }

        var rootMarkers = files
            .Where(file => string.Equals(
                file.DirectoryName,
                rootPath,
                PathComparison()))
            .Where(file => RootMarkerKinds.ContainsKey(file.Name))
            .Select(file => new WorkspaceRootMarker(
                pathResolver.NormalizeContainedOutput(file.FullName),
                RootMarkerKinds[file.Name]))
            .OrderBy(static marker => marker.Path, StringComparer.Ordinal);

        return new WorkspaceDiscoveryResult(
            rootPath,
            currentDirectory,
            workspaceKind,
            solutions.OrderBy(
                static solution => solution.Path,
                StringComparer.Ordinal),
            projects.OrderBy(
                static project => project.Path,
                StringComparer.Ordinal),
            rootMarkers,
            capabilities.OrderBy(
                static capability => capability.Path,
                StringComparer.Ordinal));
    }

    private static IEnumerable<FileInfo> EnumerateWorkspaceFiles(
        string rootPath)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(rootPath));

        while (pending.TryPop(out var directory))
        {
            foreach (var entry in directory.GetFileSystemInfos())
            {
                if (entry is FileInfo file)
                {
                    if (IsCatalogableFile(file, rootPath))
                    {
                        yield return file;
                    }

                    continue;
                }

                if (entry is not DirectoryInfo child
                    || child.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || (child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static bool IsCatalogableFile(FileInfo file, string rootPath)
    {
        if ((file.Attributes & FileAttributes.ReparsePoint) == 0
            || file.LinkTarget is null)
        {
            return true;
        }

        FileSystemInfo? target;
        try
        {
            target = file.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (IOException)
        {
            return false;
        }

        return target is FileInfo targetFile
            && targetFile.Exists
            && !new WorkspacePathResolver(rootPath, rootPath)
                .NormalizeOutput(file.FullName)
                .IsExternal;
    }

    private static DirectoryInfo? FindAncestor(
        DirectoryInfo current,
        Func<DirectoryInfo, bool> predicate)
    {
        for (var directory = current;
             directory is not null;
             directory = directory.Parent)
        {
            if (predicate(directory))
            {
                return directory;
            }
        }

        return null;
    }

    private static bool IsGitRoot(DirectoryInfo directory)
    {
        var markerPath = Path.Combine(directory.FullName, ".git");
        if (!TryGetAttributes(markerPath, out var attributes)
            || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return IsGitDirectory(markerPath);
        }

        var gitFile = File.ReadLines(markerPath).FirstOrDefault();
        const string gitDirectoryPrefix = "gitdir:";
        if (gitFile is null
            || !gitFile.StartsWith(
                gitDirectoryPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var gitDirectoryValue = gitFile[gitDirectoryPrefix.Length..].Trim();
        if (gitDirectoryValue.Length == 0)
        {
            return false;
        }

        string gitDirectory;
        try
        {
            gitDirectory = Path.GetFullPath(
                gitDirectoryValue,
                directory.FullName);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }

        return IsGitDirectory(gitDirectory);
    }

    private static bool IsGitDirectory(string path) =>
        Directory.Exists(path)
        && IsValidGitHead(Path.Combine(path, "HEAD"))
        && (Directory.Exists(Path.Combine(path, "objects"))
            || File.Exists(Path.Combine(path, "commondir")));

    private static bool IsValidGitHead(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var value = File.ReadAllText(path).Trim();
        if (value.StartsWith("ref: refs/", StringComparison.Ordinal))
        {
            return value.Length > "ref: refs/".Length;
        }

        return value.Length is 40 or 64
            && value.All(static character => Uri.IsHexDigit(character));
    }

    private static bool TryGetAttributes(
        string path,
        out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception)
            when (exception is FileNotFoundException
                or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static bool ContainsFile(
        DirectoryInfo directory,
        string fileName) =>
        directory
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Any(file =>
                file.Name.Equals(
                    fileName,
                    StringComparison.OrdinalIgnoreCase)
                && IsCatalogableFile(file, directory.FullName));

    private static bool ContainsFileWithExtension(
        DirectoryInfo directory,
        params string[] extensions) =>
        directory
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Any(file =>
                extensions.Contains(
                    file.Extension,
                    StringComparer.OrdinalIgnoreCase)
                && IsCatalogableFile(file, directory.FullName));

    private static bool IsUnsupportedProject(string extension) =>
        extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
        && !extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    private static WorkspaceCapability ReportedCapability(
        string path,
        WorkspaceCapabilityKind kind) =>
        new(path, kind, WorkspaceCapabilitySupport.ReportedOnly);

    private static bool IsWithinDirectory(string path, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return !Path.IsPathFullyQualified(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
