using System.Security.Cryptography;
using System.Text;

namespace DotNetAxi.Workspaces;

public enum WorkspacePathScope
{
    Workspace,
    Explicit,
}

public enum WorkspacePathScopeViolation
{
    ExternalPath,
    SymbolicLinkEscape,
}

public sealed class WorkspacePathScopeException : InvalidOperationException
{
    internal WorkspacePathScopeException(
        WorkspacePathScopeViolation violation,
        string path)
        : base(CreateMessage(violation, path))
    {
        Violation = violation;
        Path = path;
        Code = violation switch
        {
            WorkspacePathScopeViolation.ExternalPath =>
                "workspace.path_external",
            WorkspacePathScopeViolation.SymbolicLinkEscape =>
                "workspace.path_link_escape",
            _ => throw new ArgumentOutOfRangeException(
                nameof(violation),
                violation,
                "The workspace path scope violation is not defined."),
        };
    }

    public WorkspacePathScopeViolation Violation { get; }

    public string Path { get; }

    public string Code { get; }

    private static string CreateMessage(
        WorkspacePathScopeViolation violation,
        string path) =>
        violation switch
        {
            WorkspacePathScopeViolation.ExternalPath =>
                $"Path '{path}' is external and requires explicit path scope.",
            WorkspacePathScopeViolation.SymbolicLinkEscape =>
                $"Path '{path}' escapes the workspace through a symbolic link and requires explicit path scope.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(violation),
                violation,
                "The workspace path scope violation is not defined."),
        };
}

public sealed record WorkspacePathResolution
{
    internal WorkspacePathResolution(
        string fullPath,
        string path,
        bool isExternal,
        bool escapesThroughSymbolicLink)
    {
        FullPath = fullPath;
        Path = path;
        IsExternal = isExternal;
        EscapesThroughSymbolicLink = escapesThroughSymbolicLink;
    }

    public string FullPath { get; }

    public string Path { get; }

    public bool IsExternal { get; }

    public bool EscapesThroughSymbolicLink { get; }
}

public sealed class WorkspacePathResolver
{
    private const int MaximumLinkExpansions = 64;

    private readonly string _workspaceRoot;
    private readonly string _currentDirectory;
    private readonly string _resolvedWorkspaceRoot;
    private readonly string _resolvedCurrentDirectory;
    private readonly IReadOnlySet<string> _workspaceRootLinks;
    private readonly IReadOnlySet<string> _currentDirectoryLinks;

    public WorkspacePathResolver(
        string workspaceRoot,
        string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var processDirectory = Path.GetFullPath(
            Directory.GetCurrentDirectory());
        var resolvedProcessDirectory = ResolvePhysicalPath(
            processDirectory,
            processDirectory,
            []);
        var nativeWorkspaceRoot = ToNativeInputPath(workspaceRoot);
        var nativeCurrentDirectory = ToNativeInputPath(currentDirectory);
        _workspaceRoot = Path.GetFullPath(
            nativeWorkspaceRoot,
            processDirectory);
        _currentDirectory = Path.GetFullPath(
            nativeCurrentDirectory,
            processDirectory);

        var resolvedWorkspaceRoot = ResolvePhysicalPath(
            nativeWorkspaceRoot,
            resolvedProcessDirectory.Path,
            resolvedProcessDirectory.FollowedLinks);
        _resolvedWorkspaceRoot = resolvedWorkspaceRoot.Path;
        _workspaceRootLinks = resolvedWorkspaceRoot.FollowedLinks;

        var resolvedCurrentDirectory = ResolvePhysicalPath(
            nativeCurrentDirectory,
            resolvedProcessDirectory.Path,
            resolvedProcessDirectory.FollowedLinks);
        _resolvedCurrentDirectory = resolvedCurrentDirectory.Path;
        _currentDirectoryLinks = resolvedCurrentDirectory.FollowedLinks;
    }

    public WorkspacePathResolution ResolveInput(
        string path,
        WorkspacePathScope scope = WorkspacePathScope.Workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope,
                "The workspace path scope is not defined.");
        }

        var nativePath = ToNativeInputPath(path);
        var fullPath = Path.GetFullPath(nativePath, _currentDirectory);
        var resolvedPath = ResolvePhysicalPath(
            nativePath,
            _resolvedCurrentDirectory,
            _currentDirectoryLinks);
        var resolution = Resolve(fullPath, resolvedPath);
        if (scope is WorkspacePathScope.Workspace
            && resolution.IsExternal)
        {
            throw new WorkspacePathScopeException(
                resolution.EscapesThroughSymbolicLink
                    ? WorkspacePathScopeViolation.SymbolicLinkEscape
                    : WorkspacePathScopeViolation.ExternalPath,
                resolution.Path);
        }

        return resolution;
    }

    public WorkspacePathResolution NormalizeOutput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var nativePath = ToNativeInputPath(path);
        var fullPath = OutputFullPath(nativePath);
        var resolvedPath = ResolvePhysicalPath(
            nativePath,
            _resolvedWorkspaceRoot,
            _workspaceRootLinks);
        return Resolve(fullPath, resolvedPath);
    }

    public string ToInputPath(string workspaceRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRelativePath);

        var nativePath = ToNativeInputPath(workspaceRelativePath);
        if (Path.IsPathRooted(nativePath))
        {
            throw new ArgumentException(
                "The path must be relative to the workspace.",
                nameof(workspaceRelativePath));
        }

        var physicalWorkspacePath = Path.GetFullPath(
            nativePath,
            _resolvedWorkspaceRoot);
        if (RelativeIdentity(
                _resolvedWorkspaceRoot,
                physicalWorkspacePath).IsExternal)
        {
            throw new ArgumentException(
                "The path must be contained by the workspace.",
                nameof(workspaceRelativePath));
        }

        return NormalizeNativeSeparators(Path.GetRelativePath(
            _resolvedCurrentDirectory,
            physicalWorkspacePath));
    }

    internal string NormalizeContainedOutput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var identity = RelativeIdentity(_workspaceRoot, OutputFullPath(path));
        if (identity.IsExternal)
        {
            throw new ArgumentException(
                "The path is not contained by the workspace.",
                nameof(path));
        }

        return identity.Path;
    }

    internal static (string Path, bool IsExternal)
        CrossVolumeExternalIdentity(
            string volumeRoot,
            string volumeRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        ArgumentNullException.ThrowIfNull(volumeRelativePath);

        var canonicalRoot = volumeRoot
            .Replace('\\', '/')
            .TrimEnd('/')
            .ToUpperInvariant();
        var rootHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRoot)));
        var normalizedRelativePath = volumeRelativePath
            .Replace('\\', '/')
            .TrimStart('/');
        return (
            $"../.external-volume/{rootHash}/{normalizedRelativePath}",
            true);
    }

    internal static string NormalizeNativeSeparators(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private WorkspacePathResolution Resolve(
        string fullPath,
        PhysicalPathResolution physicalPath)
    {
        var lexical = RelativeIdentity(_workspaceRoot, fullPath);
        var resolved = RelativeIdentity(
            _resolvedWorkspaceRoot,
            physicalPath.Path);
        if (lexical.IsExternal
            && !resolved.IsExternal
            && UsesOnlyWorkspaceAliases(physicalPath.FollowedLinks))
        {
            lexical = resolved;
        }

        var escapesThroughSymbolicLink =
            !lexical.IsExternal && resolved.IsExternal;
        var outputPath = escapesThroughSymbolicLink
            ? resolved.Path
            : lexical.Path;
        var outputFullPath = escapesThroughSymbolicLink
            ? physicalPath.Path
            : fullPath;

        return new WorkspacePathResolution(
            outputFullPath,
            outputPath,
            lexical.IsExternal || resolved.IsExternal,
            escapesThroughSymbolicLink);
    }

    private bool UsesOnlyWorkspaceAliases(
        IEnumerable<string> followedLinks)
    {
        foreach (var link in followedLinks)
        {
            if (_workspaceRootLinks.Contains(link)
                || !RelativeIdentity(_resolvedWorkspaceRoot, link).IsExternal)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static (string Path, bool IsExternal) RelativeIdentity(
        string rootPath,
        string fullPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (Path.IsPathRooted(relativePath))
        {
            var volumeRoot = Path.GetPathRoot(fullPath)!;
            return CrossVolumeExternalIdentity(
                volumeRoot,
                fullPath[volumeRoot.Length..]);
        }

        var normalizedPath = NormalizeNativeSeparators(relativePath);
        return (normalizedPath, IsExternal(normalizedPath));
    }

    private static bool IsExternal(string relativePath) =>
        relativePath.Equals("..", StringComparison.Ordinal)
        || relativePath.StartsWith("../", StringComparison.Ordinal);

    private static string ToNativeInputPath(string path) =>
        path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private string OutputFullPath(string nativePath) =>
        Path.GetFullPath(nativePath, _workspaceRoot);

    private static PhysicalPathResolution ResolvePhysicalPath(
        string path,
        string basePath,
        IEnumerable<string> baseLinks)
    {
        var nativePath = ToNativeInputPath(path);
        var followedLinks = new HashSet<string>(PathComparer());
        if (!Path.IsPathRooted(nativePath))
        {
            followedLinks.UnionWith(baseLinks);
        }

        var state = new PhysicalPathState(followedLinks);
        var resolvedPath = ResolvePhysicalPath(
            nativePath,
            Path.GetFullPath(basePath),
            state);
        return new PhysicalPathResolution(
            resolvedPath,
            new HashSet<string>(state.FollowedLinks, PathComparer()));
    }

    private static string ResolvePhysicalPath(
        string path,
        string basePath,
        PhysicalPathState state)
    {
        var (currentPath, remainder) = ResolutionStart(path, basePath);
        var segments = remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is ".")
            {
                continue;
            }

            if (segment is "..")
            {
                currentPath = Directory.GetParent(currentPath)?.FullName
                    ?? Path.GetPathRoot(currentPath)!;
                continue;
            }

            var candidate = Path.Combine(currentPath, segment);
            var linkTarget = ReadLinkTarget(candidate);
            if (linkTarget is null)
            {
                currentPath = candidate;
                continue;
            }

            state.LinkExpansions++;
            if (state.LinkExpansions > MaximumLinkExpansions)
            {
                throw new IOException(
                    $"Path '{path}' contains too many symbolic-link expansions.");
            }

            state.FollowedLinks.Add(Path.GetFullPath(candidate));
            currentPath = ResolvePhysicalPath(
                ToNativeInputPath(linkTarget),
                currentPath,
                state);
        }

        return Path.GetFullPath(currentPath);
    }

    private static (string CurrentPath, string Remainder) ResolutionStart(
        string path,
        string basePath)
    {
        if (Path.IsPathFullyQualified(path))
        {
            var root = Path.GetPathRoot(path)!;
            return (Path.GetFullPath(root), path[root.Length..]);
        }

        if (!Path.IsPathRooted(path))
        {
            return (basePath, path);
        }

        var pathRoot = Path.GetPathRoot(path)!;
        if (OperatingSystem.IsWindows()
            && pathRoot.EndsWith(':'))
        {
            var baseRoot = Path.GetPathRoot(basePath)!
                .TrimEnd(Path.DirectorySeparatorChar);
            var driveBase = pathRoot.Equals(
                baseRoot,
                StringComparison.OrdinalIgnoreCase)
                    ? basePath
                    : Path.GetFullPath($"{pathRoot}.");
            return (driveBase, path[pathRoot.Length..]);
        }

        return (
            Path.GetPathRoot(basePath)!,
            path[pathRoot.Length..]);
    }

    private static string? ReadLinkTarget(string path)
    {
        var fileTarget = new FileInfo(path).LinkTarget;
        return fileTarget ?? new DirectoryInfo(path).LinkTarget;
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record PhysicalPathResolution(
        string Path,
        IReadOnlySet<string> FollowedLinks);

    private sealed class PhysicalPathState(
        HashSet<string> followedLinks)
    {
        public HashSet<string> FollowedLinks { get; } = followedLinks;

        public int LinkExpansions { get; set; }
    }
}
