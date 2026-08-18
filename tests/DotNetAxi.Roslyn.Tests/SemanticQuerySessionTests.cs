using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNetAxi.Roslyn.Tests;

public sealed class SemanticQuerySessionTests
{
    [Fact]
    public void Project_graph_is_evaluated_once()
    {
        var graphEvaluations = 0;
        var session = new SemanticQuerySession(
            new ProjectGraphEvaluationOptions(),
            (_, _, _, _) =>
            {
                graphEvaluations++;
                return null!;
            },
            (_, _, _, _) => throw new InvalidOperationException());

        Assert.Null(session.GetProjectGraph(null!, null!, CancellationToken.None));
        Assert.Null(session.GetProjectGraph(null!, null!, CancellationToken.None));
        Assert.Equal(1, graphEvaluations);
    }

    [Fact]
    public void Compiler_variants_are_resolved_only_for_missing_projects()
    {
        var requests = new List<IReadOnlyList<string>>();
        var root = Path.GetFullPath(Path.GetTempPath());
        var session = new SemanticQuerySession(
            new ProjectGraphEvaluationOptions(),
            (_, _, _, _) => throw new InvalidOperationException(),
            (_, projects, _, _) =>
            {
                var requested = projects.ToArray();
                requests.Add(Array.AsReadOnly(requested));
                return new CompilerVariantResolution(
                    new MsBuildRuntimeIdentity("10.0.100", "18.0"),
                    FailureReason: null,
                    Array.AsReadOnly(requested
                        .Select(project => new EvaluatedCompilerVariant(
                            new FileCompilerVariant(
                                project,
                                configuration: null,
                                framework: null,
                                contextFingerprint: project),
                            FailureReason: null,
                            new HashSet<string>(StringComparer.Ordinal)))
                        .ToArray()));
            });

        var owners = session.ResolveCompilerVariants(
            root,
            ["B/B.csproj"],
            CancellationToken.None);
        var relationship = session.ResolveCompilerVariants(
            root,
            ["C/C.csproj", "B/B.csproj", "A/A.csproj"],
            CancellationToken.None);

        Assert.Single(owners.Variants);
        Assert.Equal(3, relationship.Variants.Count);
        Assert.Equal(["B/B.csproj"], requests[0]);
        Assert.Equal(["A/A.csproj", "C/C.csproj"], requests[1]);
    }

    [Fact]
    public void Compiler_variant_caches_are_isolated_by_authoritative_root()
    {
        var roots = new List<string>();
        var session = new SemanticQuerySession(
            new ProjectGraphEvaluationOptions(),
            (_, _, _, _) => throw new InvalidOperationException(),
            (root, projects, _, _) =>
            {
                roots.Add(root);
                return new CompilerVariantResolution(
                    new MsBuildRuntimeIdentity("10.0.100", "18.0"),
                    FailureReason: null,
                    Array.AsReadOnly(projects
                        .Select(project => new EvaluatedCompilerVariant(
                            new FileCompilerVariant(
                                project,
                                configuration: null,
                                framework: null,
                                contextFingerprint: project),
                            FailureReason: null,
                            new HashSet<string>(StringComparer.Ordinal)))
                        .ToArray()));
            });
        var firstRoot = Path.Combine(Path.GetTempPath(), "dnaxi-root-a");
        var secondRoot = Path.Combine(Path.GetTempPath(), "dnaxi-root-b");

        _ = session.ResolveCompilerVariants(
            firstRoot,
            ["App.csproj"],
            CancellationToken.None);
        _ = session.ResolveCompilerVariants(
            secondRoot,
            ["App.csproj"],
            CancellationToken.None);
        _ = session.ResolveCompilerVariants(
            firstRoot,
            ["App.csproj"],
            CancellationToken.None);

        Assert.Equal(
            [Path.GetFullPath(firstRoot), Path.GetFullPath(secondRoot)],
            roots);
    }

    [Fact]
    public async Task Target_context_is_reused_for_relationship_with_first_load_mode()
    {
        using var projectWorkspace = new AdhocWorkspace();
        var project = projectWorkspace.AddProject("App", LanguageNames.CSharp);
        var loads = 0;
        bool? loadMetadataForReferencedProjects = null;
        using var session = CreateSession((workspace, _, _) =>
        {
            loads++;
            loadMetadataForReferencedProjects =
                workspace.LoadMetadataForReferencedProjects;
            return Task.FromResult(project);
        });
        var variant = Variant("App.csproj");

        var target = await session.GetCompilerContextAsync(
            Path.GetTempPath(),
            variant,
            RoslynCompilerContextPurpose.Target,
            CancellationToken.None);
        var relationship = await session.GetCompilerContextAsync(
            Path.GetTempPath(),
            variant,
            RoslynCompilerContextPurpose.Relationship,
            CancellationToken.None);

        Assert.Same(target, relationship);
        Assert.Equal(1, loads);
        Assert.False(loadMetadataForReferencedProjects);
    }

    [Fact]
    public async Task Failed_context_is_cached()
    {
        var loads = 0;
        using var session = CreateSession((_, _, _) =>
        {
            loads++;
            throw new UnauthorizedAccessException();
        });
        var variant = Variant("App.csproj");

        var first = await session.GetCompilerContextAsync(
            Path.GetTempPath(),
            variant,
            RoslynCompilerContextPurpose.Target,
            CancellationToken.None);
        var second = await session.GetCompilerContextAsync(
            Path.GetTempPath(),
            variant,
            RoslynCompilerContextPurpose.Relationship,
            CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal("project.load_failed", first.FailureReason);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task Cancelled_context_load_is_not_cached()
    {
        var loads = 0;
        using var session = CreateSession((_, _, cancellationToken) =>
        {
            loads++;
            throw new OperationCanceledException(cancellationToken);
        });
        var variant = Variant("App.csproj");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.GetCompilerContextAsync(
                    Path.GetTempPath(),
                    variant,
                    RoslynCompilerContextPurpose.Target,
                    CancellationToken.None)
                .AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.GetCompilerContextAsync(
                    Path.GetTempPath(),
                    variant,
                    RoslynCompilerContextPurpose.Target,
                    CancellationToken.None)
                .AsTask());

        Assert.Equal(2, loads);
    }

    [Fact]
    public void Compiler_context_diagnostic_reason_preserves_caller_priorities()
    {
        var workspaceDiagnostics = new[]
        {
            new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Failure, "workspace failed")
        };
        var compilationErrors = new[]
        {
            Diagnostic.Create(
                new DiagnosticDescriptor(
                    "CS0012",
                    "Missing reference",
                    "Missing reference",
                    "Compiler",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                Location.None)
        };

        Assert.Equal(
            "project.load_failed",
            RoslynCompilerContextLoader.DiagnosticReason(
                RoslynCompilerContextPurpose.Target,
                workspaceDiagnostics,
                compilationErrors));
        Assert.Equal(
            "metadata.missing",
            RoslynCompilerContextLoader.DiagnosticReason(
                RoslynCompilerContextPurpose.Relationship,
                workspaceDiagnostics,
                compilationErrors));
    }

    [Fact]
    public async Task Context_caches_are_isolated_by_authoritative_root()
    {
        using var projectWorkspace = new AdhocWorkspace();
        var project = projectWorkspace.AddProject("App", LanguageNames.CSharp);
        var loads = 0;
        using var session = CreateSession((_, _, _) =>
        {
            loads++;
            return Task.FromResult(project);
        });
        var variant = Variant("App.csproj");
        var firstRoot = Path.Combine(Path.GetTempPath(), "dnaxi-context-root-a");
        var secondRoot = Path.Combine(Path.GetTempPath(), "dnaxi-context-root-b");

        _ = await session.GetCompilerContextAsync(
            firstRoot,
            variant,
            RoslynCompilerContextPurpose.Target,
            CancellationToken.None);
        _ = await session.GetCompilerContextAsync(
            secondRoot,
            variant,
            RoslynCompilerContextPurpose.Target,
            CancellationToken.None);
        _ = await session.GetCompilerContextAsync(
            firstRoot,
            variant,
            RoslynCompilerContextPurpose.Target,
            CancellationToken.None);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Disposed_session_rejects_context_access_and_disposal_is_idempotent()
    {
        var session = CreateSession((_, _, _) =>
            throw new InvalidOperationException());
        session.Dispose();
        session.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.GetCompilerContextAsync(
                    Path.GetTempPath(),
                    Variant("App.csproj"),
                    RoslynCompilerContextPurpose.Target,
                    CancellationToken.None)
                .AsTask());
    }

    private static SemanticQuerySession CreateSession(
        Func<MSBuildWorkspace, string, CancellationToken, Task<Project>>
            projectLoader) =>
        new(
            new ProjectGraphEvaluationOptions(),
            (_, _, _, _) => throw new InvalidOperationException(),
            (_, _, _, _) => throw new InvalidOperationException(),
            projectLoader);

    private static FileCompilerVariant Variant(string project) =>
        new(
            project,
            configuration: null,
            framework: null,
            contextFingerprint: project);
}
