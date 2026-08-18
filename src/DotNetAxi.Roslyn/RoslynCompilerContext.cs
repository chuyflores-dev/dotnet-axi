using System.Security.Cryptography;
using DotNetAxi.Contracts;
using DotNetAxi.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNetAxi.Roslyn;

internal enum RoslynCompilerContextPurpose
{
    Target,
    Relationship,
}

internal sealed record RoslynCompilerContextKey(
    string Project,
    string? Configuration,
    string? Framework,
    string ContextFingerprint)
{
    internal static RoslynCompilerContextKey From(FileCompilerVariant variant) =>
        new(
            variant.Project,
            variant.Configuration,
            variant.Framework,
            variant.ContextFingerprint);
}

internal sealed class RoslynCompilerContext : IDisposable
{
    private bool _ownsWorkspace;

    private RoslynCompilerContext(
        MSBuildWorkspace? workspace,
        Project? project,
        CSharpCompilation? compilation,
        IReadOnlyDictionary<string, SyntaxTree> trees,
        IReadOnlyDictionary<string, string> contentHashes,
        string? failureReason,
        IReadOnlyList<WorkspaceDiagnostic> workspaceDiagnostics,
        IReadOnlyList<Diagnostic> compilationErrors)
    {
        Workspace = workspace;
        Project = project;
        Compilation = compilation;
        Trees = trees;
        ContentHashes = contentHashes;
        FailureReason = failureReason;
        WorkspaceDiagnostics = workspaceDiagnostics;
        CompilationErrors = compilationErrors;
        _ownsWorkspace = workspace is not null;
    }

    internal MSBuildWorkspace? Workspace { get; }

    internal Project? Project { get; }

    internal CSharpCompilation? Compilation { get; }

    internal IReadOnlyDictionary<string, SyntaxTree> Trees { get; }

    internal IReadOnlyDictionary<string, string> ContentHashes { get; }

    internal string? FailureReason { get; }

    internal IReadOnlyList<WorkspaceDiagnostic> WorkspaceDiagnostics { get; }

    internal IReadOnlyList<Diagnostic> CompilationErrors { get; }

    internal string? DiagnosticReason(RoslynCompilerContextPurpose purpose) =>
        RoslynCompilerContextLoader.DiagnosticReason(
            purpose,
            WorkspaceDiagnostics,
            CompilationErrors);

    internal static RoslynCompilerContext Failed(string reason) =>
        new(
            workspace: null,
            project: null,
            compilation: null,
            new Dictionary<string, SyntaxTree>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            reason,
            Array.Empty<WorkspaceDiagnostic>(),
            Array.Empty<Diagnostic>());

    internal static RoslynCompilerContext Succeeded(
        MSBuildWorkspace workspace,
        Project project,
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, SyntaxTree> trees,
        IReadOnlyDictionary<string, string> contentHashes,
        IReadOnlyList<WorkspaceDiagnostic> workspaceDiagnostics,
        IReadOnlyList<Diagnostic> compilationErrors) =>
        new(
            workspace,
            project,
            compilation,
            trees,
            contentHashes,
            failureReason: null,
            workspaceDiagnostics,
            compilationErrors);

    internal void TransferOwnership() => _ownsWorkspace = false;

    public void Dispose()
    {
        if (_ownsWorkspace)
        {
            Workspace!.Dispose();
            _ownsWorkspace = false;
        }
    }
}

internal sealed class RoslynCompilerContextLoader
{
    private readonly Func<
        MSBuildWorkspace,
        string,
        CancellationToken,
        Task<Project>> _projectLoader;

    internal RoslynCompilerContextLoader(
        Func<MSBuildWorkspace, string, CancellationToken, Task<Project>> projectLoader)
    {
        _projectLoader = projectLoader
            ?? throw new ArgumentNullException(nameof(projectLoader));
    }

    internal static Task<Project> LoadProjectAsync(
        MSBuildWorkspace workspace,
        string projectPath,
        CancellationToken cancellationToken) =>
        workspace.OpenProjectAsync(
            projectPath,
            progress: null,
            cancellationToken);

    internal async ValueTask<RoslynCompilerContext> LoadAsync(
        string workspaceRoot,
        FileCompilerVariant variant,
        ProjectGraphEvaluationOptions evaluationOptions,
        RoslynCompilerContextPurpose purpose,
        CancellationToken cancellationToken)
    {
        var projectPath = Path.GetFullPath(
            variant.Project.Replace('/', Path.DirectorySeparatorChar),
            workspaceRoot);
        if (!IsWithin(workspaceRoot, projectPath))
        {
            return RoslynCompilerContext.Failed("project.path_escape");
        }

        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var property in evaluationOptions.Properties)
        {
            properties[property.Name] = property.Value;
        }

        properties["Configuration"] = variant.Configuration ?? "Debug";
        properties["DesignTimeBuild"] = "true";
        properties["BuildingInsideVisualStudio"] = "true";
        properties["BuildProjectReferences"] = "false";
        properties["SkipCompilerExecution"] = "true";
        properties["ProvideCommandLineArgs"] = "true";
        if (variant.Framework is not null)
        {
            properties["TargetFramework"] = variant.Framework;
        }

        MSBuildWorkspace? workspace = null;
        try
        {
            var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
            workspace = MSBuildWorkspace.Create(properties);
            workspace.RegisterWorkspaceFailedHandler(args =>
                workspaceDiagnostics.Add(args.Diagnostic));
            workspace.LoadMetadataForReferencedProjects =
                purpose is RoslynCompilerContextPurpose.Relationship;

            var project = await _projectLoader(
                    workspace,
                    projectPath,
                    cancellationToken)
                .ConfigureAwait(false);
            var compilation = await project.GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false) as CSharpCompilation;
            if (compilation is null)
            {
                return RoslynCompilerContext.Failed(
                    "project.compilation_unavailable");
            }

            IReadOnlyDictionary<string, SyntaxTree> trees =
                new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
            IReadOnlyDictionary<string, string> contentHashes =
                new Dictionary<string, string>(StringComparer.Ordinal);

            if (purpose is RoslynCompilerContextPurpose.Target)
            {
                var loadedTrees =
                    new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
                var loadedHashes =
                    new Dictionary<string, string>(StringComparer.Ordinal);
                var pathResolver = new WorkspacePathResolver(
                    workspaceRoot,
                    workspaceRoot);

                foreach (var document in project.Documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (document.FilePath is null)
                    {
                        continue;
                    }

                    var tree = await document.GetSyntaxTreeAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (tree is null)
                    {
                        continue;
                    }

                    var relativePath = pathResolver
                        .NormalizeOutput(document.FilePath)
                        .Path;
                    loadedTrees[relativePath] = tree;
                    loadedHashes[relativePath] = Convert.ToHexStringLower(
                        SHA256.HashData(await File.ReadAllBytesAsync(
                                document.FilePath,
                                cancellationToken)
                            .ConfigureAwait(false)));
                }

                trees = loadedTrees;
                contentHashes = loadedHashes;
            }

            var compilationErrors = compilation.GetDiagnostics(cancellationToken)
                .Where(static diagnostic =>
                    diagnostic.Severity is DiagnosticSeverity.Error)
                .ToArray();
            var completedWorkspace = workspace;
            workspace = null;
            return RoslynCompilerContext.Succeeded(
                completedWorkspace,
                project,
                compilation,
                trees,
                contentHashes,
                Array.AsReadOnly(workspaceDiagnostics.ToArray()),
                Array.AsReadOnly(compilationErrors));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            return RoslynCompilerContext.Failed("project.load_failed");
        }
        finally
        {
            workspace?.Dispose();
        }
    }

    internal static string? DiagnosticReason(
        RoslynCompilerContextPurpose purpose,
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        IReadOnlyCollection<Diagnostic> compilationErrors) =>
        purpose switch
        {
            RoslynCompilerContextPurpose.Target => TargetDiagnosticReason(
                workspaceDiagnostics,
                compilationErrors),
            RoslynCompilerContextPurpose.Relationship => RelationshipDiagnosticReason(
                workspaceDiagnostics,
                compilationErrors),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };

    private static string? TargetDiagnosticReason(
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        IReadOnlyCollection<Diagnostic> compilationErrors)
    {
        var workspace = workspaceDiagnostics.ToArray();
        if (workspace.Any(static diagnostic =>
                IsMissingMetadata(diagnostic.Message)))
        {
            return "metadata.missing";
        }

        if (workspace.Any(static diagnostic =>
                diagnostic.Kind is WorkspaceDiagnosticKind.Failure))
        {
            return "project.load_failed";
        }

        if (compilationErrors.Any(static diagnostic =>
                diagnostic.Id is "CS0006" or "CS0012" or "CS0518"))
        {
            return "metadata.missing";
        }

        return compilationErrors.Count > 0
            ? "project.compilation_errors"
            : null;
    }

    private static string? RelationshipDiagnosticReason(
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        IReadOnlyCollection<Diagnostic> compilationErrors)
    {
        var workspace = workspaceDiagnostics.ToArray();
        if (workspace.Any(static diagnostic =>
                IsMissingMetadata(diagnostic.Message))
            || compilationErrors.Any(static diagnostic =>
                diagnostic.Id is "CS0006" or "CS0012" or "CS0518"))
        {
            return "metadata.missing";
        }

        if (workspace.Any(static diagnostic =>
                diagnostic.Kind is WorkspaceDiagnosticKind.Failure))
        {
            return "project.load_failed";
        }

        return compilationErrors.Count > 0
            ? "project.compilation_errors"
            : null;
    }

    private static bool IsMissingMetadata(string message) =>
        message.Contains("metadata", StringComparison.OrdinalIgnoreCase)
        || message.Contains("reference", StringComparison.OrdinalIgnoreCase)
        && (message.Contains("missing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unable", StringComparison.OrdinalIgnoreCase))
        || message.Contains(
            "project.assets.json",
            StringComparison.OrdinalIgnoreCase);

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
}
