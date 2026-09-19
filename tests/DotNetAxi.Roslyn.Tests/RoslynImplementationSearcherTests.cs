using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn.Tests;

public sealed class RoslynImplementationSearcherTests
{
    [Fact]
    public async Task Finds_concrete_interface_implementations_and_preserves_identity_and_variants()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync();

        var result = await workspace.FindAsync(
            "Demo.IService",
            ImplementationSearchScopeMode.Complete);

        Assert.True(result.TargetResolved);
        Assert.Equal(CoverageLevel.Partial, result.Coverage.Level);
        Assert.All(
            result.Matches,
            match => Assert.Equal("T:Demo.IService", match.TargetIdentity));
        Assert.Contains(
            result.Matches,
            match => IsServiceOwner(match.Owner)
                && match.Framework == "net8.0");
        Assert.Contains(
            result.Matches,
            match => IsServiceOwner(match.Owner)
                && match.Framework == "net10.0");
        Assert.Contains(
            result.Variants,
            variant => variant.Project == "Consumer/Consumer.csproj"
                && variant.Framework == "net8.0"
                && variant.Status is ImplementationSearchVariantStatus.Analyzed);
        Assert.Contains(
            result.Variants,
            variant => variant.Project == "Consumer/Consumer.csproj"
                && variant.Framework == "net10.0"
                && variant.Status is ImplementationSearchVariantStatus.Analyzed);
    }

    [Fact]
    public async Task Default_scope_is_partial_and_complete_adds_remaining_framework_variants()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync();

        var partial = await workspace.FindAsync(
            "Demo.IService",
            ImplementationSearchScopeMode.Default);
        var complete = await workspace.FindAsync(
            "Demo.IService",
            ImplementationSearchScopeMode.Complete);

        Assert.Equal(CoverageLevel.Partial, partial.Coverage.Level);
        Assert.Equal(CoverageLevel.Partial, complete.Coverage.Level);
        Assert.True(partial.Matches.Count < complete.Matches.Count);
        Assert.Contains(
            partial.Variants,
            variant => variant.Project == "Consumer/Consumer.csproj"
                && variant.Status is ImplementationSearchVariantStatus.Remaining);
        Assert.DoesNotContain(
            complete.Variants,
            variant => variant.Project == "Consumer/Consumer.csproj"
                && variant.Status is ImplementationSearchVariantStatus.Remaining);
    }

    [Fact]
    public async Task Preserves_abstract_member_variants_and_identity()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync();

        var result = await workspace.FindAsync(
            "Demo.WorkerBase.Execute",
            ImplementationSearchScopeMode.Complete);

        Assert.True(result.TargetResolved);
        Assert.Equal(CoverageLevel.Partial, result.Coverage.Level);
        Assert.All(
            result.Matches,
            static match => Assert.Equal(
                "M:Demo.WorkerBase.Execute(System.String)~System.String",
                match.TargetIdentity));
        Assert.Equal(2, result.Matches.Count);
        Assert.All(
            result.Matches,
            static match => Assert.Equal("ConcreteWorker", match.Owner));
        Assert.Equal(
            ["net10.0", "net8.0"],
            result.Matches
                .Select(static match => match.Framework)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Reports_coverage_failures_for_unloadable_dependents()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync(includeBroken: true);

        var result = await workspace.FindAsync(
            "Demo.IService",
            ImplementationSearchScopeMode.Complete);

        Assert.Equal(CoverageLevel.Partial, result.Coverage.Level);
        Assert.True((result.Coverage.Failed ?? 0) >= 1);
        Assert.Contains(
            result.Variants,
            variant => variant.Project == "Broken/Broken.csproj"
                && variant.Status is ImplementationSearchVariantStatus.Failed);
    }

    [Fact]
    public async Task Finds_transitive_generic_nested_and_interface_derived_types_with_paths()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync();

        var classes = await workspace.FindDerivedAsync("Demo.Root", DerivedTypeSearchScopeMode.Complete);
        Assert.Contains(classes.Matches, match =>
            match.DerivedIdentity == "T:Demo.Middle`1"
            && match.InheritancePath.SequenceEqual(["T:Demo.Root", "T:Demo.Middle`1"]));
        Assert.Contains(classes.Matches, match =>
            match.DerivedIdentity == "T:Demo.Leaf.Nested"
            && match.InheritancePath.Count >= 3
            && match.InheritancePath[0] == "T:Demo.Root"
            && match.InheritancePath[^1] == "T:Demo.Leaf.Nested");

        var interfaces = await workspace.FindDerivedAsync("Demo.IMarker", DerivedTypeSearchScopeMode.Complete);
        Assert.Contains(interfaces.Matches, match =>
            match.DerivedIdentity == "T:Demo.IChildMarker"
            && match.InheritancePath.SequenceEqual(["T:Demo.IMarker", "T:Demo.IChildMarker"]));
        Assert.Contains(interfaces.Matches, match =>
            match.DerivedIdentity == "T:Demo.InterfaceLeaf"
            && match.InheritancePath.SequenceEqual(
                ["T:Demo.IMarker", "T:Demo.IChildMarker", "T:Demo.InterfaceLeaf"]));
    }

    [Fact]
    public async Task Derived_search_discloses_remaining_and_broken_descendant_scope()
    {
        using var completeWorkspace = await ImplementationWorkspace.CreateAsync();
        var partial = await completeWorkspace.FindDerivedAsync("Demo.Root", DerivedTypeSearchScopeMode.Default);
        var complete = await completeWorkspace.FindDerivedAsync("Demo.Root", DerivedTypeSearchScopeMode.Complete);
        Assert.Contains(partial.Variants, variant =>
            variant.Project == "Consumer/Consumer.csproj"
            && variant.Status is ImplementationSearchVariantStatus.Remaining);
        Assert.DoesNotContain(complete.Variants, variant =>
            variant.Status is ImplementationSearchVariantStatus.Remaining);

        using var brokenWorkspace = await ImplementationWorkspace.CreateAsync(includeBroken: true);
        var broken = await brokenWorkspace.FindDerivedAsync("Demo.Root", DerivedTypeSearchScopeMode.Complete);
        Assert.Equal(CoverageLevel.Partial, broken.Coverage.Level);
        Assert.Contains(broken.Variants, variant =>
            variant.Project == "Broken/Broken.csproj"
            && variant.Status is ImplementationSearchVariantStatus.Failed);
    }

    [Fact]
    public async Task Finds_transitive_overrides_and_excludes_hidden_members()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync(includeBroken: true);
        var result = await workspace.FindOverridesAsync("Demo.OverrideBase.Run", OverrideSearchScopeMode.Complete);

        Assert.Contains(result.Matches, match => match.OverrideIdentity == "M:Demo.OverrideMid.Run" && match.OverridePath.SequenceEqual(["M:Demo.OverrideBase.Run", "M:Demo.OverrideMid.Run"]));
        Assert.Contains(result.Matches, match => match.OverrideIdentity == "M:Demo.OverrideLeaf.Run" && match.OverridePath.SequenceEqual(["M:Demo.OverrideBase.Run", "M:Demo.OverrideMid.Run", "M:Demo.OverrideLeaf.Run"]));
        Assert.DoesNotContain(result.Matches, match => match.OverrideIdentity == "M:Demo.HiddenRun.Run");
        Assert.Contains(result.Variants, variant => variant.Project == "Broken/Broken.csproj" && variant.Status is ImplementationSearchVariantStatus.Failed);
    }

    [Fact]
    public async Task Finds_overrides_from_an_intermediate_member_and_for_properties_events_and_generics()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync();

        var intermediate = await workspace.FindOverridesAsync("Demo.OverrideMid.Run", OverrideSearchScopeMode.Complete);
        Assert.Contains(intermediate.Matches, match =>
            match.OverrideIdentity == "M:Demo.OverrideLeaf.Run"
            && match.OverridePath.SequenceEqual(["M:Demo.OverrideMid.Run", "M:Demo.OverrideLeaf.Run"]));

        var abstractMethod = await workspace.FindOverridesAsync("Demo.WorkerBase.Execute", OverrideSearchScopeMode.Complete);
        Assert.Contains(abstractMethod.Matches, match =>
            match.OverrideIdentity == "M:Demo.ConcreteWorker.Execute(System.String)");

        var property = await workspace.FindOverridesAsync("Demo.MemberBase.Value", OverrideSearchScopeMode.Complete);
        Assert.Contains(property.Matches, match =>
            match.OverrideIdentity == "P:Demo.MemberLeaf.Value"
            && match.OverridePath.SequenceEqual(["P:Demo.MemberBase.Value", "P:Demo.MemberLeaf.Value"]));

        var @event = await workspace.FindOverridesAsync("Demo.MemberBase.Changed", OverrideSearchScopeMode.Complete);
        Assert.Contains(@event.Matches, match =>
            match.OverrideIdentity == "E:Demo.MemberLeaf.Changed"
            && match.OverridePath.SequenceEqual(["E:Demo.MemberBase.Changed", "E:Demo.MemberLeaf.Changed"]));

        var generic = await workspace.FindOverridesAsync("Demo.GenericBase.Transform", OverrideSearchScopeMode.Complete);
        Assert.Contains(generic.Matches, match =>
            match.OverrideIdentity == "M:Demo.GenericLeaf.Transform(System.Int32)");
    }

    [Fact]
    public async Task Caller_search_uses_exact_overload_binding_dispatch_scope_and_broken_callers()
    {
        using var workspace = await ImplementationWorkspace.CreateAsync(includeBroken: true);

        var overloadId = (await workspace.SearchSymbolsAsync("Demo.CallerTarget.Overload"))
            .Matches.Single(match => match.Signature.Contains("int", StringComparison.OrdinalIgnoreCase)).Id;
        var overload = await workspace.FindCallersAsync(overloadId, CallerSearchScopeMode.Complete);
        Assert.Equal(2, overload.Matches.Count(match =>
            match.ContainingSymbol == "M:Demo.CallerConsumer.Direct(Demo.CallerTarget)"));

        var virtualCall = await workspace.FindCallersAsync(
            "Demo.CallerTarget.Virtual", CallerSearchScopeMode.Complete);
        Assert.Contains(virtualCall.Matches, match =>
            match.ContainingSymbol == "M:Demo.CallerConsumer.Direct(Demo.CallerTarget)"
            && match.Relationship == "possible_dispatch"
            && match.Confidence == "possible");
        Assert.Equal(2, virtualCall.Matches.Count(match =>
            match.ContainingSymbol == "M:Demo.CallerConsumer.Direct(Demo.CallerTarget)"));
        Assert.Contains(virtualCall.Variants, variant =>
            variant.Project == "Broken/Broken.csproj"
            && variant.Status is CallerSearchVariantStatus.Failed);

        var partial = await workspace.FindCallersAsync(
            "Demo.CallerTarget.Virtual", CallerSearchScopeMode.Default);
        Assert.Contains(partial.Variants, variant =>
            variant.Project == "Consumer/Consumer.csproj"
            && variant.Status is CallerSearchVariantStatus.Remaining);
        Assert.DoesNotContain(partial.Variants, variant =>
            variant.Project == "Unrelated/Unrelated.csproj");
        Assert.DoesNotContain(virtualCall.Variants, variant =>
            variant.Status is CallerSearchVariantStatus.Remaining);
        Assert.Equal(["net10.0", "net8.0"], virtualCall.Matches
            .Select(static match => match.Framework)
            .Distinct()
            .Order(StringComparer.Ordinal));

        var dispatch = await workspace.FindCallersAsync(
            "Demo.ICallerContract.Contract", CallerSearchScopeMode.Complete);
        Assert.Contains(dispatch.Matches, match =>
            match.Relationship == "possible_dispatch"
            && match.Confidence == "possible"
            && match.ContainingSymbol == "M:Demo.CallerConsumer.Interface(Demo.ICallerContract)");
    }

    private static bool IsServiceOwner(string? owner) =>
        owner is "ServiceA" or "Demo.ServiceA" or "ServiceB" or "Demo.ServiceB";

    private sealed class ImplementationWorkspace : IDisposable
    {
        private ImplementationWorkspace()
            => Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-implementation-tests",
                Guid.NewGuid().ToString("N"));

        public string Root { get; }

        public static async Task<ImplementationWorkspace> CreateAsync(
            bool includeBroken = false)
        {
            var workspace = new ImplementationWorkspace();
            Directory.CreateDirectory(workspace.Root);
            try
            {
                await workspace.WriteProjectAsync(
                    "Contracts/Contracts.csproj",
                    WorkspaceProjectBody(targetFrameworks: "net8.0;net10.0"));
                await workspace.WriteAsync(
                    "Contracts/Types.cs",
                    """
                    namespace Demo;
                    public interface IService { void Execute(int value); }
                    public abstract class WorkerBase { public abstract string Execute(string value); }
                    public class Root { }
                    public class Middle<T> : Root { }
                    public class Leaf : Middle<int> { public class Nested : Leaf { } }
                    public interface IMarker { }
                    public interface IChildMarker : IMarker { }
                    public class InterfaceLeaf : IChildMarker { }
                    public abstract class OverrideBase { public virtual void Run() { } }
                    public class OverrideMid : OverrideBase { public override void Run() { } }
                    public sealed class OverrideLeaf : OverrideMid { public sealed override void Run() { } }
                    public class HiddenRun : OverrideBase { public new void Run() { } }
                    public abstract class MemberBase
                    {
                        public virtual int Value { get; set; }
                        public virtual event System.EventHandler? Changed;
                    }
                    public class MemberLeaf : MemberBase
                    {
                        public override int Value { get; set; }
                        public override event System.EventHandler? Changed { add { } remove { } }
                    }
                    public class GenericBase<T> { public virtual T Transform(T value) => value; }
                    public class GenericLeaf : GenericBase<int> { public override int Transform(int value) => value; }
                    public interface ICallerContract { void Contract(); }
                    public class CallerTarget
                    {
                        public void Overload(int value) { }
                        public void Overload(string value) { }
                        public virtual void Virtual() { }
                    }
                    """);
                await workspace.WriteProjectAsync(
                    "Consumer/Consumer.csproj",
                    WorkspaceProjectBody(projectReference: "../Contracts/Contracts.csproj"));
                await workspace.WriteAsync(
                    "Consumer/Consumer.cs",
                    """
                    namespace Demo;
                    public sealed class ServiceA : IService { public void Execute(int value) { } }
                    public sealed class ServiceB : IService { void IService.Execute(int value) => throw new System.NotImplementedException(); }
                    public sealed class ConcreteWorker : WorkerBase { public override string Execute(string value) => value; }
                    public sealed class CallerConsumer
                    {
                        public void Direct(CallerTarget target) { target.Overload(1); target.Overload("text"); target.Virtual(); }
                        public void Interface(ICallerContract target) => target.Contract();
                    }
                    """);
                await workspace.WriteProjectAsync(
                    "Unrelated/Unrelated.csproj",
                    WorkspaceProjectBody());
                await workspace.WriteAsync(
                    "Unrelated/Unrelated.cs",
                    "namespace Demo; public sealed class Unrelated { }");
                await workspace.WriteAsync(
                    "Workspace.slnx",
                    includeBroken
                        ? """
                          <Solution>
                            <Project Path="Contracts/Contracts.csproj" />
                            <Project Path="Consumer/Consumer.csproj" />
                            <Project Path="Unrelated/Unrelated.csproj" />
                            <Project Path="Broken/Broken.csproj" />
                          </Solution>
                          """
                        : """
                          <Solution>
                            <Project Path="Contracts/Contracts.csproj" />
                            <Project Path="Consumer/Consumer.csproj" />
                            <Project Path="Unrelated/Unrelated.csproj" />
                          </Solution>
                          """);

                if (includeBroken)
                {
                    await workspace.WriteProjectAsync(
                        "Broken/Broken.csproj",
                        WorkspaceProjectBody(
                            projectReference: "../Contracts/Contracts.csproj",
                            includeMissingReference: true));
                    await workspace.WriteAsync(
                        "Broken/Broken.cs",
                        """
                        namespace Broken;
                        public sealed class Broken : Demo.Root, Demo.IService
                        {
                            public void Execute(int value) { }
                            public Missing.Dependency.Widget? BrokenWidget { get; set; }
                        }
                        """);
                }

                return workspace;
            }
            catch
            {
                workspace.Dispose();
                throw;
            }
        }

        public async Task<RoslynImplementationSearchResult> FindAsync(
            string target,
            ImplementationSearchScopeMode scopeMode)
        {
            var context = Context();
            return await new RoslynImplementationSearcher(
                    new WorkspacePathTraverser(),
                    context.Ownership,
                    context.Projects)
                .FindAsync(
                    target,
                    context.Discovery,
                    context.Selection,
                    context.Traversal,
                    context.Scope,
                    scopeMode,
                    new ProjectGraphEvaluationOptions());
        }

        public async Task<RoslynDerivedTypeSearchResult> FindDerivedAsync(
            string target,
            DerivedTypeSearchScopeMode scopeMode)
        {
            var context = Context();
            return await new RoslynDerivedTypeSearcher(
                    new WorkspacePathTraverser(), context.Ownership, context.Projects)
                .FindAsync(target, context.Discovery, context.Selection, context.Traversal,
                    context.Scope, scopeMode, new ProjectGraphEvaluationOptions());
        }

        public async Task<RoslynOverrideSearchResult> FindOverridesAsync(string target, OverrideSearchScopeMode scopeMode)
        {
            var context = Context();
            return await new RoslynOverrideSearcher(new WorkspacePathTraverser(), context.Ownership, context.Projects)
                .FindAsync(target, context.Discovery, context.Selection, context.Traversal, context.Scope, scopeMode, new ProjectGraphEvaluationOptions());
        }

        public async Task<RoslynCallerSearchResult> FindCallersAsync(string target, CallerSearchScopeMode scopeMode)
        {
            var context = Context();
            return await new RoslynCallerSearcher(new WorkspacePathTraverser(), context.Ownership, context.Projects)
                .FindAsync(target, context.Discovery, context.Selection, context.Traversal, context.Scope, scopeMode, new ProjectGraphEvaluationOptions());
        }

        public async Task<SymbolDeclarationSearchResult> SearchSymbolsAsync(string query)
        {
            var context = Context();
            return await new SymbolDeclarationSearcher(new WorkspacePathTraverser(), context.Ownership)
                .SearchAsync(new SymbolDeclarationSearchRequest(query, context.Traversal, includeTests: false, scope: context.Scope));
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

        private static string WorkspaceProjectBody(
            string? projectReference = null,
            bool includeMissingReference = false,
            string targetFrameworks = "net8.0;net10.0")
        {
        var projectReferenceItem = projectReference is null
            ? string.Empty
            : $"""
                  <ProjectReference Include="{projectReference}" />
                  """;
        var missingReferenceItem = includeMissingReference
            ? """
                  <Reference Include="Missing.Dependency">
                    <HintPath>missing/Dependency.dll</HintPath>
                  </Reference>
                  """
                : string.Empty;
            return $"""
                <PropertyGroup>
                  <TargetFrameworks>{targetFrameworks}</TargetFrameworks>
                  <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
                </PropertyGroup>
                <ItemGroup>
                  <Reference Include="System.Private.CoreLib">
                    <HintPath>{System.Security.SecurityElement.Escape(typeof(object).Assembly.Location)}</HintPath>
                  </Reference>
                  {projectReferenceItem}
                  {missingReferenceItem}
                </ItemGroup>
                """;
        }

        public async Task WriteProjectAsync(
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
}
