namespace DotNetAxi.Contracts;

/// <summary>
/// Validated repository settings that affect source traversal. Configuration
/// discovery and parsing are deliberately outside this contract.
/// </summary>
public sealed record TraversalConfiguration
{
    public TraversalConfiguration(
        IEnumerable<string>? exclusionPatterns = null,
        IEnumerable<string>? generatedPathPatterns = null,
        bool includeGeneratedByDefault = false)
    {
        ExclusionPatterns = ContractGuards.CopyText(
            exclusionPatterns,
            nameof(exclusionPatterns));
        GeneratedPathPatterns = ContractGuards.CopyText(
            generatedPathPatterns,
            nameof(generatedPathPatterns));
        IncludeGeneratedByDefault = includeGeneratedByDefault;
    }

    public static TraversalConfiguration Empty { get; } = new();

    /// <summary>
    /// Workspace-relative glob patterns already validated by the configuration
    /// boundary.
    /// </summary>
    public IReadOnlyList<string> ExclusionPatterns { get; }

    /// <summary>
    /// Additional workspace-relative glob patterns that identify generated
    /// source in addition to the tool defaults.
    /// </summary>
    public IReadOnlyList<string> GeneratedPathPatterns { get; }

    public bool IncludeGeneratedByDefault { get; }
}

/// <summary>
/// A request to enumerate the single path set shared by file, text, and
/// structural search engines.
/// </summary>
public sealed record WorkspaceTraversalRequest
{
    public WorkspaceTraversalRequest(
        string workspaceRoot,
        TraversalConfiguration? configuration = null,
        IEnumerable<string>? explicitPaths = null,
        bool? includeGenerated = null,
        string? currentDirectory = null)
    {
        WorkspaceRoot = ContractGuards.RequiredText(
            workspaceRoot,
            nameof(workspaceRoot));
        Configuration = configuration ?? TraversalConfiguration.Empty;
        ExplicitPaths = ContractGuards.CopyText(
            explicitPaths,
            nameof(explicitPaths));
        IncludeGenerated = includeGenerated;
        CurrentDirectory = currentDirectory is null
            ? WorkspaceRoot
            : ContractGuards.RequiredText(
                currentDirectory,
                nameof(currentDirectory));
    }

    public string WorkspaceRoot { get; }

    public TraversalConfiguration Configuration { get; }

    /// <summary>
    /// Explicit file or directory scopes. Relative paths resolve from
    /// <see cref="CurrentDirectory"/>; fully qualified and external paths are
    /// also accepted. They narrow the candidate set and permit build output
    /// within that explicit scope.
    /// </summary>
    public IReadOnlyList<string> ExplicitPaths { get; }

    /// <summary>
    /// An operation-level override for
    /// <see cref="TraversalConfiguration.IncludeGeneratedByDefault"/>.
    /// </summary>
    public bool? IncludeGenerated { get; }

    /// <summary>
    /// Directory from which relative explicit paths resolve. It defaults to
    /// <see cref="WorkspaceRoot"/>.
    /// </summary>
    public string CurrentDirectory { get; }
}

/// <summary>
/// An eligible file identified by its absolute filesystem path and normalized
/// workspace-relative identity. External identities remain relative and carry
/// an explicit marker.
/// </summary>
public sealed record WorkspaceTraversalPath
{
    public WorkspaceTraversalPath(
        string fullPath,
        string relativePath,
        bool isExternal)
    {
        FullPath = ContractGuards.RequiredText(fullPath, nameof(fullPath));
        RelativePath = ContractGuards.RequiredText(
            relativePath,
            nameof(relativePath));
        IsExternal = isExternal;
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public bool IsExternal { get; }
}

/// <summary>
/// Produces the deterministic, tool-owned traversal set consumed by optional
/// file, text, and structural engines.
/// </summary>
public interface IWorkspacePathTraverser
{
    IReadOnlyList<WorkspaceTraversalPath> Traverse(
        WorkspaceTraversalRequest request,
        CancellationToken cancellationToken = default);
}
