using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Workspaces;

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
}
