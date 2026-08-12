using DotNetAxi.Contracts;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal static class SyntaxCommandScope
{
    public static EvidenceScope Create(
        WorkspaceDiscoveryResult workspace,
        IReadOnlyList<string> paths,
        bool includeGenerated,
        string analyzedPortion,
        IEnumerable<string>? projects = null,
        IEnumerable<string>? frameworks = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzedPortion);

        var resolver = new WorkspacePathResolver(
            workspace.RootPath,
            workspace.CurrentDirectory);
        var normalizedPaths = paths
            .Select(path => resolver
                .ResolveInput(path, WorkspacePathScope.Explicit)
                .Path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new EvidenceScope(
            workspace.RootPath,
            analyzedPortion,
            projects: projects,
            frameworks: frameworks,
            eligibility: new EvidenceEligibility(
                IncludeTests: false,
                IncludeGenerated: includeGenerated),
            paths: normalizedPaths);
    }
}
