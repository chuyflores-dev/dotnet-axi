namespace DotNetAxi.Contracts;

/// <summary>Path-only file-search inputs shared by the CLI and built-in engine.</summary>
public sealed record FileSearchRequest
{
    public FileSearchRequest(
        string query,
        WorkspaceTraversalRequest traversal,
        bool caseSensitive = false,
        IEnumerable<string>? extensions = null,
        IEnumerable<string>? globs = null,
        int limit = 100)
    {
        Query = ContractGuards.RequiredText(query, nameof(query));
        Traversal = traversal ?? throw new ArgumentNullException(nameof(traversal));
        CaseSensitive = caseSensitive;
        Extensions = Array.AsReadOnly(
            ContractGuards.CopyText(extensions, nameof(extensions))
                .Select(NormalizeExtension)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        Globs = Array.AsReadOnly(
            ContractGuards.CopyText(globs, nameof(globs))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        Limit = limit;
    }

    public string Query { get; }

    public WorkspaceTraversalRequest Traversal { get; }

    public bool CaseSensitive { get; }

    public IReadOnlyList<string> Extensions { get; }

    public IReadOnlyList<string> Globs { get; }

    public int Limit { get; }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.TrimStart('.');
        if (normalized.Length == 0
            || normalized.Contains('/')
            || normalized.Contains('\\'))
        {
            throw new ArgumentException(
                "File extensions must be names without path separators.",
                nameof(extension));
        }

        return normalized;
    }
}

public sealed record FileSearchMatch
{
    public FileSearchMatch(
        string id,
        string path,
        string kind,
        bool isExternal,
        IEnumerable<string>? owningProjects = null)
    {
        Id = ContractGuards.RequiredText(id, nameof(id));
        Path = NormalizePath(path);
        Kind = ContractGuards.RequiredText(kind, nameof(kind));
        IsExternal = isExternal;
        if (!isExternal && IsLexicallyExternal(Path))
        {
            throw new ArgumentException(
                "An external file-search path must be labeled external.",
                nameof(isExternal));
        }

        OwningProjects = Array.AsReadOnly(
            ContractGuards.CopyText(owningProjects, nameof(owningProjects))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public string Id { get; }

    public string Path { get; }

    public string Kind { get; }

    public bool IsExternal { get; }

    public IReadOnlyList<string> OwningProjects { get; }

    public int OwningProjectCount => OwningProjects.Count;

    private static string NormalizePath(string path)
    {
        var normalized = ContractGuards.RequiredText(path, nameof(path))
            .Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || HasWindowsDriveDesignator(normalized))
        {
            throw new ArgumentException(
                "File-search matches require a workspace-relative path.",
                nameof(path));
        }

        return normalized;
    }

    private static bool HasWindowsDriveDesignator(string path) =>
        path.Length >= 2
        && char.IsAsciiLetter(path[0])
        && path[1] == ':';

    private static bool IsLexicallyExternal(string path) =>
        path.Equals("..", StringComparison.Ordinal)
        || path.StartsWith("../", StringComparison.Ordinal);
}

public sealed record FileSearchResult
{
    public FileSearchResult(
        IEnumerable<FileSearchMatch> matches,
        int total,
        string snapshot)
    {
        Matches = ContractGuards.Copy(matches);
        if (total < Matches.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }

        Total = total;
        Snapshot = ContractGuards.RequiredText(snapshot, nameof(snapshot));
    }

    public IReadOnlyList<FileSearchMatch> Matches { get; }

    public int Total { get; }

    public string Snapshot { get; }
}

/// <summary>Returns deterministic project ownership candidates for one path.</summary>
public interface IFileOwnershipResolver
{
    IReadOnlyList<string> GetOwningProjects(WorkspaceTraversalPath path);

    IReadOnlyList<FileCompilerVariant> GetCompilerVariants(
        WorkspaceTraversalPath path) =>
        Array.AsReadOnly(
            GetOwningProjects(path)
                .Select(static project => new FileCompilerVariant(
                    project,
                    configuration: null,
                    framework: null,
                    contextFingerprint: project))
                .ToArray());
}

/// <summary>
/// Identifies one compiler context in which a traversed source file can be
/// included. The context fingerprint is opaque input used only to prevent
/// identity reuse after relevant project context changes.
/// </summary>
public sealed record FileCompilerVariant
{
    public FileCompilerVariant(
        string project,
        string? configuration,
        string? framework,
        string contextFingerprint)
    {
        Project = ContractGuards.RequiredText(project, nameof(project))
            .Replace('\\', '/');
        Configuration = ContractGuards.OptionalText(
            configuration,
            nameof(configuration));
        Framework = ContractGuards.OptionalText(framework, nameof(framework));
        ContextFingerprint = ContractGuards.RequiredText(
            contextFingerprint,
            nameof(contextFingerprint));
    }

    public string Project { get; }

    public string? Configuration { get; }

    public string? Framework { get; }

    public string ContextFingerprint { get; }
}

public interface IFileSearcher
{
    FileSearchResult Search(
        FileSearchRequest request,
        CancellationToken cancellationToken = default);
}
