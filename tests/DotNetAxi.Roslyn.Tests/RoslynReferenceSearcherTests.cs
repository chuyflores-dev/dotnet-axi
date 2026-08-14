using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNetAxi.Roslyn.Tests;

public sealed class RoslynReferenceSearcherTests
{
    [Fact]
    public async Task Finds_only_the_selected_overload_across_frameworks()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();
        var overload = Assert.Single(
            (await workspace.SearchAsync("Demo.Service.Run")).Matches,
            match => match.Signature.Contains("int", StringComparison.Ordinal));

        var result = await workspace.FindAsync(
            overload.Id,
            ReferenceSearchScopeMode.Complete);

        Assert.True(result.TargetResolved);
        Assert.Equal(CoverageLevel.Partial, result.Coverage.Level);
        Assert.Equal(2, result.Matches.Count(match =>
            match.Start.Path == "Direct/Calls.cs"));
        Assert.Equal(
            ["net10.0", "net8.0"],
            result.Matches
                .Where(match => match.Start.Path == "Direct/Calls.cs")
                .Select(static match => match.Framework)
                .Order(StringComparer.Ordinal));
        Assert.All(result.Matches, match =>
            Assert.Equal("M:Demo.Service.Run(System.Int32)", match.TargetIdentity));
        Assert.DoesNotContain(result.Matches, match => match.Start.Line == 8);
    }

    [Fact]
    public async Task Matches_an_independent_Roslyn_reference_oracle_and_alias_metadata()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();
        var overload = Assert.Single(
            (await workspace.SearchAsync("Demo.Service.Run")).Matches,
            match => match.Signature.Contains("int", StringComparison.Ordinal));

        var result = await workspace.FindAsync(
            overload.Id,
            ReferenceSearchScopeMode.Complete,
            new ProjectGraphEvaluationOptions(framework: "net10.0"));
        var expected = await IndependentDirectReferencesAsync(workspace.Root);
        var actual = result.Matches
            .Where(static match => match.Project == "Direct/Direct.csproj")
            .Select(static match => (
                match.Start.Path,
                match.Start.Line,
                match.Start.Column))
            .OrderBy(static match => match.Path, StringComparer.Ordinal)
            .ThenBy(static match => match.Line)
            .ThenBy(static match => match.Column)
            .ToArray();

        Assert.Equal(expected, actual);

        var typeResult = await workspace.FindAsync(
            "Demo.Service",
            ReferenceSearchScopeMode.Complete,
            new ProjectGraphEvaluationOptions(framework: "net10.0"));
        Assert.Contains(typeResult.Matches, static match =>
            match.Project == "Direct/Direct.csproj"
            && match.Alias == "Alias");
    }

    [Fact]
    public async Task Does_not_substitute_a_target_from_another_framework()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();

        var result = await workspace.FindAsync(
            "Demo.NetTenOnly",
            ReferenceSearchScopeMode.Complete);

        Assert.Contains(result.Variants, static variant =>
            variant.Framework == "net8.0"
            && variant.Status is ReferenceSearchVariantStatus.Failed
            && variant.Reason == "semantic.target_not_in_framework");
        Assert.DoesNotContain(result.Matches, static match =>
            match.Framework == "net8.0");
    }

    [Fact]
    public async Task Maps_a_merged_namespace_to_the_selected_target_assembly()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();
        var target = Assert.Single(
            (await workspace.SearchAsync("Demo")).Matches,
            static match => match.Kind == "namespace"
                && match.Range.Start.Path == "Core/Namespace.cs");

        var result = await workspace.FindAsync(
            target.Id,
            ReferenceSearchScopeMode.Complete,
            new ProjectGraphEvaluationOptions(framework: "net10.0"));

        Assert.True(
            result.Matches.Any(static match =>
                match.Project == "Direct/Direct.csproj"
                && match.Start.Path == "Direct/NamespaceUse.cs"),
            string.Join(", ", result.Variants.Select(static variant =>
                $"{variant.Project}:{variant.Framework}:{variant.Status}:{variant.Reason}"))
            + " | matches: "
            + string.Join(", ", result.Matches.Select(static match =>
                $"{match.Project}:{match.Start.Path}:{match.Start.Line}")));
    }

    [Fact]
    public async Task Maps_escaped_namespace_identifiers_by_value_text()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();
        var target = Assert.Single(
            (await workspace.SearchAsync("Outer.@class")).Matches,
            static match => match.Kind == "namespace"
                && match.Range.Start.Path == "Core/EscapedNamespace.cs");

        var result = await workspace.FindAsync(
            target.Id,
            ReferenceSearchScopeMode.Complete,
            new ProjectGraphEvaluationOptions(framework: "net10.0"));

        Assert.Contains(result.Matches, static match =>
            match.Project == "Direct/Direct.csproj"
            && match.Start.Path == "Direct/EscapedNamespaceUse.cs");
    }

    [Fact]
    public async Task Snapshot_changes_when_a_reverse_project_import_changes()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();
        await workspace.WriteAsync(
            "Direct/Directory.Build.props",
            "<Project><PropertyGroup><DefineConstants>FIRST</DefineConstants></PropertyGroup></Project>");
        var first = await workspace.FindAsync("Demo.Service");
        var repeated = await workspace.FindAsync("Demo.Service");

        Assert.Equal(first.Snapshot, repeated.Snapshot);

        await workspace.WriteAsync(
            "Direct/Directory.Build.props",
            "<Project><PropertyGroup><DefineConstants>SECOND</DefineConstants></PropertyGroup></Project>");
        var second = await workspace.FindAsync("Demo.Service");

        Assert.NotEqual(first.Snapshot, second.Snapshot);
    }

    [Fact]
    public async Task Default_scope_is_verified_partial_and_complete_adds_transitive_projects()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();

        var partial = await workspace.FindAsync("Demo.Service");
        var complete = await workspace.FindAsync(
            "Demo.Service",
            ReferenceSearchScopeMode.Complete);

        Assert.Equal(CoverageLevel.Partial, partial.Coverage.Level);
        Assert.Contains(partial.Variants, variant =>
            variant.Project == "Transitive/Transitive.csproj"
            && variant.Status is ReferenceSearchVariantStatus.Remaining);
        Assert.DoesNotContain(partial.Matches, match =>
            match.Project == "Transitive/Transitive.csproj");
        Assert.Contains(complete.Variants, variant =>
            variant.Project == "Transitive/Transitive.csproj"
            && variant.Status is not ReferenceSearchVariantStatus.Remaining);
        Assert.DoesNotContain(complete.Variants, variant =>
            variant.Status is ReferenceSearchVariantStatus.Remaining);
    }

    [Fact]
    public async Task Preserves_linked_reference_owners_and_framework_variants()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();

        var result = await workspace.FindAsync(
            "Demo.Service",
            ReferenceSearchScopeMode.Complete);

        var linked = result.Matches
            .Where(static match => match.Start.Path == "Shared/Linked.cs")
            .ToArray();
        Assert.Equal(3, linked.Length);
        Assert.Equal(
            [
                ("Another/Another.csproj", "net10.0"),
                ("Direct/Direct.csproj", "net10.0"),
                ("Direct/Direct.csproj", "net8.0"),
            ],
            linked.Select(static match => (match.Project, match.Framework)));
        Assert.All(linked, match =>
            Assert.StartsWith("reference/v1/", match.Id));
    }

    [Fact]
    public async Task Names_broken_projects_and_keeps_verified_matches_partial()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();

        var result = await workspace.FindAsync(
            "Demo.Service",
            ReferenceSearchScopeMode.Complete);

        var broken = Assert.Single(result.Variants, variant =>
            variant.Project == "Broken/Broken.csproj");
        Assert.Equal(ReferenceSearchVariantStatus.Failed, broken.Status);
        Assert.Equal("project.compilation_errors", broken.Reason);
        Assert.Contains(
            "compiler errors",
            broken.Correction,
            StringComparison.Ordinal);
        Assert.Equal(CoverageLevel.Partial, result.Coverage.Level);
        Assert.True(result.Coverage.Failed >= 1);
        Assert.NotEmpty(result.Matches);
        Assert.All(result.Matches, match =>
            Assert.NotEqual("Broken/Broken.csproj", match.Project));
    }

    [Fact]
    public async Task Returns_target_corrections_before_reference_results()
    {
        using var workspace = await ReferenceWorkspace.CreateAsync();

        var result = await workspace.FindAsync("Demo.Missing");

        Assert.False(result.TargetResolved);
        Assert.Equal(
            SemanticTargetResolutionStatus.NotFound,
            result.TargetStatus);
        Assert.Equal("semantic.target_not_found", result.ErrorCode);
        Assert.Contains("dnaxi search symbol", result.Correction);
        Assert.Equal(CoverageLevel.NotApplicable, result.Coverage.Level);
        Assert.Empty(result.Matches);
        Assert.Empty(result.Variants);
    }

    private sealed class ReferenceWorkspace : IDisposable
    {
        private ReferenceWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-reference-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public static async Task<ReferenceWorkspace> CreateAsync()
        {
            var workspace = new ReferenceWorkspace();
            try
            {
                await workspace.WriteProjectAsync(
                    "Core/Core.csproj",
                    workspace.ProjectBody(excludeNetTenForNet8: true));
                await workspace.WriteAsync(
                    "Core/Service.cs",
                    "namespace Demo; public sealed class Service { public void Run(int value) { } public void Run(string value) { } }");
                await workspace.WriteAsync(
                    "Core/NetTenOnly.cs",
                    "namespace Demo; public sealed class NetTenOnly { }");
                await workspace.WriteAsync(
                    "Core/Namespace.cs",
                    "namespace Demo { }");
                await workspace.WriteAsync(
                    "Core/EscapedNamespace.cs",
                    "namespace Outer.@class { }");

                await workspace.WriteProjectAsync(
                    "Direct/Direct.csproj",
                    workspace.ProjectBody(
                        "../Core/Core.csproj",
                        "../Shared/Linked.cs"));
                await workspace.WriteAsync(
                    "Direct/Calls.cs",
                    """
                    using Alias = Demo.Service;
                    namespace Direct;
                    public sealed class Calls
                    {
                        public void Call(Alias service)
                        {
                            service.Run(1);
                            service.Run("text");
                        }
                    }
                    """);
                await workspace.WriteAsync(
                    "Direct/NamespaceUse.cs",
                    "namespace Demo; public sealed class DirectNamespaceMember { }");
                await workspace.WriteAsync(
                    "Direct/EscapedNamespaceUse.cs",
                    "namespace Outer.@class; public sealed class EscapedNamespaceMember { }");

                await workspace.WriteProjectAsync(
                    "Another/Another.csproj",
                    workspace.ProjectBody(
                        "../Core/Core.csproj",
                        "../Shared/Linked.cs",
                        frameworks: "net10.0"));
                await workspace.WriteAsync(
                    "Shared/Linked.cs",
                    "namespace Shared; public sealed class Linked { private Demo.Service? _service; }");

                await workspace.WriteProjectAsync(
                    "Transitive/Transitive.csproj",
                    workspace.ProjectBody("../Direct/Direct.csproj"));
                await workspace.WriteAsync(
                    "Transitive/Use.cs",
                    "namespace Transitive; public sealed class Use { private Demo.Service? _service; }");

                await workspace.WriteProjectAsync(
                    "Broken/Broken.csproj",
                    workspace.ProjectBody(
                        "../Core/Core.csproj",
                        missingReference: true,
                        frameworks: "net10.0"));
                await workspace.WriteAsync(
                    "Broken/Use.cs",
                    "namespace Broken; public sealed class Use { private Demo.Service? _service; private Missing.Dependency.Widget? _missing; }");

                await workspace.WriteAsync(
                    "Workspace.slnx",
                    """
                    <Solution>
                      <Project Path="Core/Core.csproj" />
                      <Project Path="Direct/Direct.csproj" />
                      <Project Path="Another/Another.csproj" />
                      <Project Path="Transitive/Transitive.csproj" />
                      <Project Path="Broken/Broken.csproj" />
                    </Solution>
                    """);
                return workspace;
            }
            catch
            {
                workspace.Dispose();
                throw;
            }
        }

        public async Task<SymbolDeclarationSearchResult> SearchAsync(
            string query)
        {
            var context = Context();
            return await new SymbolDeclarationSearcher(
                    new WorkspacePathTraverser(),
                    context.Ownership)
                .SearchAsync(new SymbolDeclarationSearchRequest(
                    query,
                    context.Traversal,
                    includeTests: false,
                    scope: context.Scope));
        }

        public async Task<RoslynReferenceSearchResult> FindAsync(
            string target,
            ReferenceSearchScopeMode mode = ReferenceSearchScopeMode.Default,
            ProjectGraphEvaluationOptions? evaluationOptions = null)
        {
            var context = Context();
            return await new RoslynReferenceSearcher(
                    new WorkspacePathTraverser(),
                    context.Ownership,
                    context.Projects)
                .FindAsync(
                    target,
                    context.Discovery,
                    context.Selection,
                    context.Traversal,
                    context.Scope,
                    mode,
                    evaluationOptions ?? new ProjectGraphEvaluationOptions());
        }

        private TestContext Context()
        {
            var discovery = new WorkspaceDiscoverer().Discover(Root);
            var selection = new WorkspaceEntryPointSelector().Select(
                discovery,
                new WorkspaceSelectionRequest(solution: "Workspace.slnx"));
            var projects = discovery.Projects
                .Select(static project => project.Path)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var traversal = new WorkspaceTraversalRequest(
                Root,
                includeGenerated: false);
            var scope = new SymbolDeclarationScope(
                selection.Path,
                projects,
                paths: null,
                includeTests: false,
                includeGenerated: false);
            return new TestContext(
                discovery,
                selection,
                traversal,
                scope,
                new WorkspaceProjectOwnershipResolver(Root, projects),
                Array.AsReadOnly(projects));
        }

        private string ProjectBody(
            string? projectReference = null,
            string? linkedFile = null,
            bool missingReference = false,
            string frameworks = "net8.0;net10.0",
            bool excludeNetTenForNet8 = false)
        {
            var frameworkProperty = frameworks.Contains(';', StringComparison.Ordinal)
                ? $"<TargetFrameworks>{frameworks}</TargetFrameworks>"
                : $"<TargetFramework>{frameworks}</TargetFramework>";
            var projectItem = projectReference is null
                ? string.Empty
                : $"<ProjectReference Include=\"{projectReference}\" />";
            var linkedItem = linkedFile is null
                ? string.Empty
                : $"<Compile Include=\"{linkedFile}\" Link=\"Linked.cs\" />";
            var missingItem = missingReference
                ? "<Reference Include=\"Missing.Dependency\"><HintPath>missing/Dependency.dll</HintPath></Reference>"
                : string.Empty;
            var conditionalItem = excludeNetTenForNet8
                ? "<Compile Remove=\"NetTenOnly.cs\" Condition=\"'$(TargetFramework)' == 'net8.0'\" />"
                : string.Empty;
            return $"""
                <PropertyGroup>
                  {frameworkProperty}
                  <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
                </PropertyGroup>
                <ItemGroup>
                  <Reference Include="System.Private.CoreLib">
                    <HintPath>{System.Security.SecurityElement.Escape(typeof(object).Assembly.Location)}</HintPath>
                  </Reference>
                  {projectItem}
                  {linkedItem}
                  {missingItem}
                  {conditionalItem}
                </ItemGroup>
                """;
        }

        private async Task WriteProjectAsync(
            string relativePath,
            string body) =>
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

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private sealed record TestContext(
            WorkspaceDiscoveryResult Discovery,
            WorkspaceSelection Selection,
            WorkspaceTraversalRequest Traversal,
            SymbolDeclarationScope Scope,
            WorkspaceProjectOwnershipResolver Ownership,
            IReadOnlyList<string> Projects);
    }

    private static async Task<(string Path, int Line, int Column)[]>
        IndependentDirectReferencesAsync(string root)
    {
        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = "Debug",
            ["TargetFramework"] = "net10.0",
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["BuildProjectReferences"] = "false",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
        };
        using var workspace = MSBuildWorkspace.Create(properties);
        workspace.LoadMetadataForReferencedProjects = true;
        var project = await workspace.OpenProjectAsync(
            Path.Combine(root, "Direct", "Direct.csproj"));
        var compilation = Assert.IsType<CSharpCompilation>(
            await project.GetCompilationAsync());
        var service = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("Demo.Service"));
        var target = Assert.Single(
            service.GetMembers("Run").OfType<IMethodSymbol>(),
            static method => method.Parameters.Single().Type.SpecialType
                is SpecialType.System_Int32);
        var references = await SymbolFinder.FindReferencesAsync(
            target,
            project.Solution,
            System.Collections.Immutable.ImmutableHashSet.Create(
                project.Documents.ToArray()));
        return references
            .SelectMany(static reference => reference.Locations)
            .Where(static location => location.Location.IsInSource)
            .Select(location =>
            {
                var span = location.Location.GetLineSpan().Span.Start;
                return (
                    Path.GetRelativePath(root, location.Document.FilePath!)
                        .Replace('\\', '/'),
                    span.Line + 1,
                    span.Character + 1);
            })
            .OrderBy(static match => match.Item1, StringComparer.Ordinal)
            .ThenBy(static match => match.Item2)
            .ThenBy(static match => match.Item3)
            .ToArray();
    }
}
