using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNetAxi.Roslyn.Tests;

public sealed class RoslynSemanticTargetResolverTests
{
    [Fact]
    public async Task Resolves_partial_type_once_across_framework_variants()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            MultiTargetProject());
        await workspace.WriteAsync(
            "App/First.cs",
            "namespace Demo; public partial class Service { }");
        await workspace.WriteAsync(
            "App/Second.cs",
            "namespace Demo; public partial class Service { }");

        using var result = await workspace.ResolveAsync("Demo.Service");

        Assert.Equal(SemanticTargetResolutionStatus.Resolved, result.Status);
        Assert.Equal(2, result.Declarations.Count);
        Assert.Collection(
            result.Variants,
            net10 => AssertResolvedType(net10, "net10.0", declarationCount: 2),
            net8 => AssertResolvedType(net8, "net8.0", declarationCount: 2));
        Assert.False(result.HasPartialCoverage);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.Correction);
    }

    [Fact]
    public async Task Keeps_overloads_ambiguous_and_an_entity_id_resolves_aliases()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            MultiTargetProject());
        await workspace.WriteAsync(
            "App/Code.cs",
            """
            #if NET8_0
            using Count = System.Int32;
            #else
            using Count = System.Int64;
            #endif

            namespace Demo;

            public sealed class Service
            {
                public void Run(Count value) { }
                public void Run(string value) { }
            }
            """);

        using (var ambiguous = await workspace.ResolveAsync("Demo.Service.Run"))
        {
            Assert.Equal(
                SemanticTargetResolutionStatus.Ambiguous,
                ambiguous.Status);
            Assert.Equal("semantic.target_ambiguous", ambiguous.ErrorCode);
            Assert.Equal(2, ambiguous.Candidates.Count);
            Assert.All(ambiguous.Candidates, candidate =>
                Assert.StartsWith("symbol/v2/", candidate.Id));
            Assert.Contains("candidate ID", ambiguous.Correction);
            Assert.Empty(ambiguous.Variants);
        }

        var overloads = (await workspace.SearchAsync("Demo.Service.Run")).Matches;
        var aliasOverload = Assert.Single(overloads, match =>
            match.Signature.Contains("Count", StringComparison.Ordinal));

        using var resolved = await workspace.ResolveAsync(aliasOverload.Id);

        Assert.Equal(SemanticTargetResolutionStatus.Resolved, resolved.Status);
        Assert.Collection(
            resolved.Variants,
            net10 => AssertResolvedMethod(
                net10,
                "net10.0",
                "M:Demo.Service.Run(System.Int64)"),
            net8 => AssertResolvedMethod(
                net8,
                "net8.0",
                "M:Demo.Service.Run(System.Int32)"));
    }

    [Fact]
    public async Task Resolves_a_linked_declaration_in_each_owning_project()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "One/One.csproj",
            LinkedProject("../Shared/Linked.cs"));
        await workspace.WriteProjectAsync(
            "Two/Two.csproj",
            LinkedProject("../Shared/Linked.cs"));
        await workspace.WriteAsync(
            "Shared/Linked.cs",
            "namespace Shared; public sealed class Linked { }");

        using var result = await workspace.ResolveAsync("Shared.Linked");

        Assert.Equal(SemanticTargetResolutionStatus.Resolved, result.Status);
        Assert.Collection(
            result.Variants,
            one => Assert.Equal("One/One.csproj", one.ProjectPath),
            two => Assert.Equal("Two/Two.csproj", two.ProjectPath));
        Assert.All(result.Variants, variant =>
        {
            Assert.Equal(SemanticTargetVariantStatus.Resolved, variant.Status);
            Assert.Equal("T:Shared.Linked", variant.Identity);
        });
    }

    [Fact]
    public async Task Returns_structured_corrections_before_semantic_traversal()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            SingleTargetProject());
        await workspace.WriteAsync(
            "App/Code.cs",
            "namespace Demo; public sealed class Existing { }");

        using (var missing = await workspace.ResolveAsync("Missing"))
        {
            Assert.Equal(SemanticTargetResolutionStatus.NotFound, missing.Status);
            Assert.Equal("semantic.target_not_found", missing.ErrorCode);
            Assert.Contains("dnaxi search symbol", missing.Correction);
            Assert.Empty(missing.Variants);
        }

        using (var unsupported = await workspace.ResolveAsync("file/v1/example"))
        {
            Assert.Equal(
                SemanticTargetResolutionStatus.Unsupported,
                unsupported.Status);
            Assert.Equal("semantic.target_unsupported", unsupported.ErrorCode);
            Assert.Contains("symbol/v2", unsupported.Correction);
            Assert.Null(unsupported.Snapshot);
        }

        var oldId = Assert.Single(
            (await workspace.SearchAsync("Demo.Existing")).Matches).Id;
        await workspace.WriteAsync(
            "App/Code.cs",
            "namespace Demo; public sealed class Existing { } // changed");

        using var stale = await workspace.ResolveAsync(oldId);

        Assert.Equal(SemanticTargetResolutionStatus.Stale, stale.Status);
        Assert.Equal("evidence.stale_id", stale.ErrorCode);
        Assert.Single(stale.Candidates);
        Assert.Contains("dnaxi search symbol", stale.Correction);
        Assert.Empty(stale.Variants);
    }

    [Fact]
    public async Task Bounds_ambiguous_candidates_and_reports_omissions()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            SingleTargetProject());
        await workspace.WriteAsync(
            "App/Code.cs",
            string.Join(
                Environment.NewLine,
                Enumerable.Range(0, 25).Select(index =>
                    $"namespace N{index} {{ public sealed class Common {{ }} }}")));

        using var result = await workspace.ResolveAsync("Common");

        Assert.Equal(SemanticTargetResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(RoslynSemanticTargetResolver.DefaultCandidateLimit, result.Candidates.Count);
        Assert.Equal(25, result.CandidateTotal);
        Assert.Equal(5, result.CandidateOmitted);
        Assert.True(result.CandidatesTruncated);
        Assert.Contains("dnaxi search symbol", result.Correction);
    }

    [Fact]
    public async Task Does_not_rebind_when_source_changes_before_project_loading()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            SingleTargetProject());
        await workspace.WriteAsync(
            "App/Code.cs",
            "namespace Demo; public sealed class Existing { }");

        using var result = await workspace.ResolveAsync(
            "Demo.Existing",
            async (msbuild, projectPath, cancellationToken) =>
            {
                await workspace.WriteAsync(
                    "App/Code.cs",
                    "namespace Demo; public sealed class Replaced { }");
                return await RoslynSemanticTargetResolver.LoadProjectAsync(
                    msbuild,
                    projectPath,
                    cancellationToken);
            });

        Assert.Equal(SemanticTargetResolutionStatus.Unresolved, result.Status);
        var variant = Assert.Single(result.Variants);
        Assert.Equal(SemanticTargetVariantStatus.Unresolved, variant.Status);
        Assert.Equal("candidate.stale", variant.Reason);
        Assert.Null(variant.Symbol);
    }

    private static void AssertResolvedType(
        SemanticTargetVariant variant,
        string framework,
        int declarationCount)
    {
        Assert.Equal(framework, variant.Framework);
        Assert.Equal(SemanticTargetVariantStatus.Resolved, variant.Status);
        Assert.Equal("T:Demo.Service", variant.Identity);
        var symbol = Assert.IsAssignableFrom<INamedTypeSymbol>(variant.Symbol);
        Assert.Equal(declarationCount, symbol.DeclaringSyntaxReferences.Length);
    }

    private static void AssertResolvedMethod(
        SemanticTargetVariant variant,
        string framework,
        string identity)
    {
        Assert.Equal(framework, variant.Framework);
        Assert.Equal(SemanticTargetVariantStatus.Resolved, variant.Status);
        Assert.Equal(identity, variant.Identity);
        Assert.IsAssignableFrom<IMethodSymbol>(variant.Symbol);
        Assert.NotNull(variant.Project);
        Assert.NotNull(variant.Compilation);
    }

    private static string MultiTargetProject() =>
        $"""
        <PropertyGroup>
          <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
          <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
        </PropertyGroup>
        {ExplicitReferenceItemGroup()}
        """;

    private static string SingleTargetProject() =>
        $"""
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
          <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
        </PropertyGroup>
        {ExplicitReferenceItemGroup()}
        """;

    private static string LinkedProject(string include) =>
        $"""
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
          <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
        </PropertyGroup>
        {ExplicitReferenceItemGroup()}
        <ItemGroup>
          <Compile Include="{include}" Link="Linked.cs" />
        </ItemGroup>
        """;

    private static string ExplicitReferenceItemGroup() =>
        $"""
        <ItemGroup>
          <Reference Include="System.Private.CoreLib">
            <HintPath>{System.Security.SecurityElement.Escape(typeof(object).Assembly.Location)}</HintPath>
          </Reference>
        </ItemGroup>
        """;

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-target-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task WriteProjectAsync(string relativePath, string body) =>
            await WriteAsync(
                relativePath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\">{body}</Project>");

        public async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        public async Task<SymbolDeclarationSearchResult> SearchAsync(
            string target)
        {
            var (traversal, scope, ownership, _) = Context();
            return await new SymbolDeclarationSearcher(
                    new WorkspacePathTraverser(),
                    ownership)
                .SearchAsync(new SymbolDeclarationSearchRequest(
                    target,
                    traversal,
                    includeTests: false,
                    scope: scope));
        }

        public async Task<SemanticTargetResolution> ResolveAsync(
            string target,
            Func<MSBuildWorkspace, string, CancellationToken, Task<Project>>?
                projectLoader = null)
        {
            var (traversal, scope, ownership, projects) = Context();
            var resolver = projectLoader is null
                ? new RoslynSemanticTargetResolver(
                    new WorkspacePathTraverser(),
                    ownership,
                    projects)
                : new RoslynSemanticTargetResolver(
                    new WorkspacePathTraverser(),
                    ownership,
                    projects,
                    projectLoader);
            return await resolver
                .ResolveAsync(target, traversal, scope);
        }

        private (
            WorkspaceTraversalRequest Traversal,
            SymbolDeclarationScope Scope,
            WorkspaceProjectOwnershipResolver Ownership,
            IReadOnlyList<string> Projects) Context()
        {
            var projects = Directory.EnumerateFiles(
                    Root,
                    "*.csproj",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var traversal = new WorkspaceTraversalRequest(
                Root,
                includeGenerated: false);
            var scope = new SymbolDeclarationScope(
                solution: null,
                projects,
                paths: null,
                includeTests: false,
                includeGenerated: false);
            return (
                traversal,
                scope,
                new WorkspaceProjectOwnershipResolver(Root, projects),
                Array.AsReadOnly(projects));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
