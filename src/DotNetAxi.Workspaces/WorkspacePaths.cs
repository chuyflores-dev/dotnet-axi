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
    private readonly string _workspaceRoot;
    private readonly string _currentDirectory;
    private readonly string _resolvedWorkspaceRoot;

    public WorkspacePathResolver(
        string workspaceRoot,
        string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _currentDirectory = Path.GetFullPath(currentDirectory);
        _resolvedWorkspaceRoot = ResolveEntryLinkTarget(_workspaceRoot);
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
        var fullPath = Path.IsPathRooted(nativePath)
            ? Path.GetFullPath(nativePath)
            : Path.GetFullPath(nativePath, _currentDirectory);
        var resolution = Resolve(fullPath);
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

        var fullPath = OutputFullPath(path);
        return Resolve(fullPath);
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

    private WorkspacePathResolution Resolve(string fullPath)
    {
        var lexical = RelativeIdentity(_workspaceRoot, fullPath);
        if (lexical.IsExternal)
        {
            var canonicalLexical = RelativeIdentity(
                _resolvedWorkspaceRoot,
                fullPath);
            if (!canonicalLexical.IsExternal)
            {
                lexical = canonicalLexical;
            }
        }

        var resolvedFullPath = lexical.IsExternal
            ? fullPath
            : ResolveContainedLinkTargets(
                _resolvedWorkspaceRoot,
                lexical.Path);
        var resolved = RelativeIdentity(
            _resolvedWorkspaceRoot,
            resolvedFullPath);
        var escapesThroughSymbolicLink =
            !lexical.IsExternal && resolved.IsExternal;
        var outputPath = escapesThroughSymbolicLink
            ? resolved.Path
            : lexical.Path;

        return new WorkspacePathResolution(
            fullPath,
            outputPath,
            lexical.IsExternal || resolved.IsExternal,
            escapesThroughSymbolicLink);
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

    private string OutputFullPath(string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(ToNativeInputPath(path), _workspaceRoot);

    private static string ResolveEntryLinkTarget(string path)
    {
        var fullPath = Path.GetFullPath(path);
        FileSystemInfo? entry = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : File.Exists(fullPath)
                ? new FileInfo(fullPath)
                : null;
        if (entry is null || entry.LinkTarget is null)
        {
            return fullPath;
        }

        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName
            ?? fullPath;
    }

    private static string ResolveContainedLinkTargets(
        string rootPath,
        string relativePath)
    {
        var current = rootPath;
        var segments = ToNativeInputPath(relativePath).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is ".")
            {
                continue;
            }

            var candidate = Path.Combine(current, segment);
            current = ResolveEntryLinkTarget(candidate);
        }

        return Path.GetFullPath(current);
    }
}
