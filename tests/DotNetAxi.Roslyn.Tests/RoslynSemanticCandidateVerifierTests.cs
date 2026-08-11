using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn.Tests;

public sealed class RoslynSemanticCandidateVerifierTests
{
    [Fact]
    public async Task Verifies_each_declared_query_kind()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "App/Code.cs",
            """
            using System;

            sealed class MarkerAttribute : Attribute { }
            sealed class Widget { }

            [Marker]
            sealed class Marked
            {
                static void Target() { }

                static void Run()
                {
                    Target();
                    _ = new Widget();
                    try { }
                    catch (Exception) { }
                }
            }
            """);

        var cases = new ISemanticallyVerifiableSyntaxQuery[]
        {
            new InvocationSyntaxQuery("Target"),
            new AttributedClassSyntaxQuery("Marker"),
            new ObjectCreationSyntaxQuery("Widget"),
            new CatchClauseSyntaxQuery("Exception"),
        };

        foreach (var query in cases)
        {
            var result = await workspace.VerifyAsync(query);

            Assert.Equal(1, result.Discovered);
            Assert.Equal(1, result.Verified);
            Assert.Equal(0, result.Rejected);
            Assert.Equal(0, result.Unresolved);
            var candidate = Assert.Single(result.Candidates);
            Assert.Equal(SemanticCandidateStatus.Verified, candidate.Status);
            var variant = Assert.Single(candidate.Variants);
            Assert.Equal("App/App.csproj", variant.Project);
            Assert.Equal("net10.0", variant.Framework);
            Assert.Equal(SemanticCandidateStatus.Verified, variant.Status);
            Assert.NotNull(variant.Symbol);
            Assert.Null(variant.Reason);
        }
    }

    [Fact]
    public async Task Keeps_framework_outcomes_explicit_without_collapsing_meaning()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            MultiTargetProject());
        await workspace.WriteAsync(
            "App/Code.cs",
            """
            sealed class C
            {
            #if NET8_0
                static void Target() { }
            #endif
                static void Run() => Target();
            }
            """);

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        Assert.Equal(1, result.Discovered);
        Assert.Equal(1, result.Verified);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(0, result.Unresolved);
        var variants = Assert.Single(result.Candidates).Variants;
        Assert.Collection(
            variants,
            net10 =>
            {
                Assert.Equal("net10.0", net10.Framework);
                Assert.Equal(SemanticCandidateStatus.Unresolved, net10.Status);
                Assert.Equal("semantic.unresolved", net10.Reason);
            },
            net8 =>
            {
                Assert.Equal("net8.0", net8.Framework);
                Assert.Equal(SemanticCandidateStatus.Verified, net8.Status);
            });
    }

    [Fact]
    public async Task Reports_the_evaluated_framework_and_configuration()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Directory.Build.props",
            """
            <Project>
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            ExplicitFrameworkReference());
        await workspace.WriteAsync(
            "App/Code.cs",
            """
            sealed class C
            {
            #if NET10_0
                static void Target() { }
            #endif
                static void Run() => Target();
            }
            """);

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal("Debug", variant.Configuration);
        Assert.Equal("net10.0", variant.Framework);
        Assert.Equal(SemanticCandidateStatus.Verified, variant.Status);
    }

    [Fact]
    public async Task Expands_all_frameworks_declared_by_an_import()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Directory.Build.props",
            """
            <Project>
              <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
            </Project>
            """);
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            $"""
            {ExplicitFrameworkReference()}
            <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
              <Reference Include="Missing">
                <HintPath>missing/reference.dll</HintPath>
              </Reference>
            </ItemGroup>
            """);
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        Assert.Equal(
            ["net10.0", "net8.0"],
            Assert.Single(result.Candidates).Variants
                .Select(static variant => variant.Framework));
        Assert.Collection(
            Assert.Single(result.Candidates).Variants,
            net10 => Assert.Equal(SemanticCandidateStatus.Verified, net10.Status),
            net8 =>
            {
                Assert.Equal(SemanticCandidateStatus.Unresolved, net8.Status);
                Assert.Equal("metadata.missing", net8.Reason);
            });
    }

    [Fact]
    public async Task Does_not_remap_a_stale_candidate_to_replacement_source()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");
        var query = new InvocationSyntaxQuery("Target");
        var syntax = await workspace.QueryAsync(query);
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(1); }");

        var result = await workspace.VerifyAsync(syntax, query);

        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal(SemanticCandidateStatus.Unresolved, variant.Status);
        Assert.Equal("candidate.stale", variant.Reason);
    }

    [Fact]
    public async Task Verifies_an_inner_invocation_that_shares_the_outer_start()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "App/Code.cs",
            """
            static class C
            {
                static string RequiredText(string value) => value;
                static string Run(string value) => RequiredText(value).Replace('a', 'b');
            }
            """);

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("RequiredText"));

        Assert.Equal(1, result.Verified);
        Assert.Equal(
            SemanticCandidateStatus.Verified,
            Assert.Single(Assert.Single(result.Candidates).Variants).Status);
    }

    [Theory]
    [InlineData("int", "new int[1]")]
    [InlineData("int", "new int[1][]")]
    public async Task Verifies_predefined_and_jagged_array_element_names(
        string type,
        string expression)
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "App/Code.cs",
            $"sealed class C {{ object Value = {expression}; }}");

        var result = await workspace.VerifyAsync(
            new ObjectCreationSyntaxQuery(type));

        Assert.Equal(1, result.Verified);
        Assert.Equal(
            SemanticCandidateStatus.Verified,
            Assert.Single(Assert.Single(result.Candidates).Variants).Status);
    }

    [Fact]
    public async Task Evaluates_conditional_metadata_per_framework()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            MultiTargetProject(
                """
            <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
              <Reference Include="Missing"><HintPath>missing\\reference.dll</HintPath></Reference>
            </ItemGroup>
            """));
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        Assert.Collection(
            Assert.Single(result.Candidates).Variants,
            net10 =>
            {
                Assert.Equal("net10.0", net10.Framework);
                Assert.Equal(SemanticCandidateStatus.Verified, net10.Status);
            },
            net8 =>
            {
                Assert.Equal("net8.0", net8.Framework);
                Assert.Equal(SemanticCandidateStatus.Unresolved, net8.Status);
                Assert.Equal("metadata.missing", net8.Reason);
            });
    }

    [Fact]
    public async Task Broken_compilation_is_explicitly_unresolved()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); MissingType Value; }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal(SemanticCandidateStatus.Unresolved, variant.Status);
        Assert.Equal("project.compilation_errors", variant.Reason);
    }

    [Fact]
    public async Task Generated_tree_errors_are_explicitly_unresolved()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            """
            <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><Compile Include="obj/Broken.g.cs" /></ItemGroup>
            """);
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");
        await workspace.WriteAsync(
            "App/obj/Broken.g.cs",
            "sealed class Generated { MissingType Value; }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal(SemanticCandidateStatus.Unresolved, variant.Status);
        Assert.Equal("project.compilation_errors", variant.Reason);
    }

    [Fact]
    public async Task Verifies_linked_source_in_its_real_project_owner()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            """
            <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><Compile Include="../Shared/Code.cs" Link="Code.cs" /></ItemGroup>
            """);
        await workspace.WriteAsync(
            "Shared/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal("App/App.csproj", variant.Project);
        Assert.Equal(SemanticCandidateStatus.Verified, variant.Status);
    }

    [Fact]
    public async Task Verifies_linked_source_from_an_imported_wildcard()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Directory.Build.props",
            """
            <Project>
              <ItemGroup>
                <Compile Include="$(MSBuildThisFileDirectory)Shared/*.cs" Link="%(Filename)%(Extension)" />
              </ItemGroup>
            </Project>
            """);
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "Shared/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        AssertVerifiedByApp(result);
    }

    [Fact]
    public async Task Verifies_linked_source_from_a_property_expanded_include()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            """
            <PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
              <SharedRoot>../Shared</SharedRoot>
            </PropertyGroup>
            <ItemGroup><Compile Include="$(SharedRoot)/Code.cs" Link="Code.cs" /></ItemGroup>
            """);
        await workspace.WriteAsync(
            "Shared/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        AssertVerifiedByApp(result);
    }

    [Fact]
    public async Task Verifies_linked_source_from_a_wildcard_include()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            """
            <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><Compile Include="../Shared/*.cs" Link="%(Filename)%(Extension)" /></ItemGroup>
            """);
        await workspace.WriteAsync(
            "Shared/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        AssertVerifiedByApp(result);
    }

    [Fact]
    public async Task Keeps_successful_frameworks_when_another_framework_fails_evaluation()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            MultiTargetProject(
                """
                <Import Project="Missing.targets" Condition="'$(TargetFramework)' == 'net8.0'" />
                """));
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        Assert.Collection(
            Assert.Single(result.Candidates).Variants,
            net10 =>
            {
                Assert.Equal("App/App.csproj", net10.Project);
                Assert.Equal("net10.0", net10.Framework);
                Assert.Equal(SemanticCandidateStatus.Verified, net10.Status);
            },
            net8 =>
            {
                Assert.Equal("App/App.csproj", net8.Project);
                Assert.Equal("Debug", net8.Configuration);
                Assert.Equal("net8.0", net8.Framework);
                Assert.Equal(SemanticCandidateStatus.Unresolved, net8.Status);
                Assert.Equal("project.invalid", net8.Reason);
            });
    }

    [Fact]
    public async Task Verifies_an_explicit_external_linked_source()
    {
        using var workspace = new TestWorkspace();
        var externalPath = await workspace.WriteExternalAsync(
            "Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");
        var include = Path.GetRelativePath(
                Path.Combine(workspace.Root, "App"),
                externalPath)
            .Replace('\\', '/');
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            $"""
            <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><Compile Include="{include}" Link="Code.cs" /></ItemGroup>
            """);

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"),
            [externalPath]);

        AssertVerifiedByApp(result);
        Assert.True(Assert.Single(result.Candidates).Candidate.Range.Start.IsExternal);
    }

    [Fact]
    public async Task Missing_project_references_are_explicitly_unresolved()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            """
            <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><ProjectReference Include="Missing.csproj" /></ItemGroup>
            """);
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal(SemanticCandidateStatus.Unresolved, variant.Status);
        Assert.Equal("project.reference_not_found", variant.Reason);
    }

    [Fact]
    public async Task Stale_package_assets_are_explicitly_unresolved()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            """
            <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><PackageReference Include="Example" Version="1.0.0" /></ItemGroup>
            """);
        await workspace.WriteAsync(
            "App/obj/project.assets.json",
            "{\"version\":3,\"targets\":{\"net9.0\":{}}}");
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");

        var result = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Target"));

        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal(SemanticCandidateStatus.Unresolved, variant.Status);
        Assert.Equal("metadata.missing", variant.Reason);
    }

    [Fact]
    public async Task Semantic_snapshot_changes_with_the_compiler_context()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Directory.Build.props",
            "<Project><PropertyGroup><DefineConstants>FIRST</DefineConstants></PropertyGroup></Project>");
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "App/Code.cs",
            "sealed class C { static void Target() { } static void Run() => Target(); }");
        var query = new InvocationSyntaxQuery("Target");
        var syntax = await workspace.QueryAsync(query);

        var first = await workspace.VerifyAsync(syntax, query);
        await workspace.WriteAsync(
            "Directory.Build.props",
            "<Project><PropertyGroup><DefineConstants>SECOND</DefineConstants></PropertyGroup></Project>");
        var second = await workspace.VerifyAsync(syntax, query);

        Assert.NotEqual(syntax.Snapshot, first.Snapshot);
        Assert.NotEqual(first.Snapshot, second.Snapshot);
    }

    [Fact]
    public async Task Reports_multi_ownership_ambiguity_broken_projects_and_missing_metadata()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "First.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteProjectAsync(
            "Second.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "Shared.cs",
            """
            interface IA { }
            interface IB { }
            static class C
            {
                static void Pick(IA value) { }
                static void Pick(IB value) { }
                static void Run() => Pick(null);
            }
            """);

        var ambiguous = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Pick"));

        var candidate = Assert.Single(ambiguous.Candidates);
        Assert.Equal(SemanticCandidateStatus.Unresolved, candidate.Status);
        Assert.Equal(2, candidate.Variants.Count);
        Assert.All(candidate.Variants, variant =>
        {
            Assert.Equal(SemanticCandidateStatus.Unresolved, variant.Status);
            Assert.Equal("semantic.ambiguous", variant.Reason);
        });

        await workspace.WriteAsync("First.csproj", "<Project>");
        await workspace.WriteProjectAsync(
            "Second.csproj",
            """
            <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup>
              <Reference Include="Missing"><HintPath>missing.dll</HintPath></Reference>
            </ItemGroup>
            """);

        var incomplete = await workspace.VerifyAsync(
            new InvocationSyntaxQuery("Pick"));

        Assert.Equal(1, incomplete.Unresolved);
        var reasons = Assert.Single(incomplete.Candidates).Variants
            .Select(static variant => variant.Reason)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new string?[] { "metadata.missing", "project.invalid" },
            reasons);
    }

    [Fact]
    public async Task Rejects_a_resolved_target_typed_creation_of_another_type()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteProjectAsync(
            "App/App.csproj",
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>");
        await workspace.WriteAsync(
            "App/Code.cs",
            """
            sealed class Widget { }
            sealed class Other { }
            sealed class C { Other Value = new(); }
            """);

        var result = await workspace.VerifyAsync(
            new ObjectCreationSyntaxQuery("Widget"));

        Assert.Equal(1, result.Discovered);
        Assert.Equal(0, result.Verified);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(0, result.Unresolved);
        Assert.Equal(
            SemanticCandidateStatus.Rejected,
            Assert.Single(result.Candidates).Status);
    }

    private static void AssertVerifiedByApp(
        SemanticSyntaxVerificationResult result)
    {
        var variant = Assert.Single(Assert.Single(result.Candidates).Variants);
        Assert.Equal("App/App.csproj", variant.Project);
        Assert.Equal(SemanticCandidateStatus.Verified, variant.Status);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-roslyn-tests",
                Guid.NewGuid().ToString("N"));
            ExternalRoot = Root + "-external";
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ExternalRoot);
        }

        public string Root { get; }

        public string ExternalRoot { get; }

        public async Task WriteProjectAsync(string relativePath, string body) =>
            await WriteAsync(
                relativePath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\">{body}</Project>");

        public async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        public async Task<string> WriteExternalAsync(
            string relativePath,
            string contents)
        {
            var path = Path.Combine(
                ExternalRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
            return path;
        }

        public async Task<SemanticSyntaxVerificationResult> VerifyAsync(
            ISemanticallyVerifiableSyntaxQuery query,
            IEnumerable<string>? explicitPaths = null)
        {
            var syntax = await QueryAsync(query, explicitPaths);
            return await VerifyAsync(syntax, query);
        }

        public async Task<RoslynSyntaxQueryResult> QueryAsync(
            ISemanticallyVerifiableSyntaxQuery query,
            IEnumerable<string>? explicitPaths = null)
        {
            var traversal = new WorkspaceTraversalRequest(
                Root,
                explicitPaths: explicitPaths);
            return await new RoslynSyntaxEngine(new WorkspacePathTraverser())
                .QueryAsync(new RoslynSyntaxQueryRequest(traversal), query);
        }

        public async Task<SemanticSyntaxVerificationResult> VerifyAsync(
            RoslynSyntaxQueryResult syntax,
            ISemanticallyVerifiableSyntaxQuery query)
        {
            var projects = Directory.EnumerateFiles(
                    Root,
                    "*.csproj",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();
            return await new RoslynSemanticCandidateVerifier(
                    new WorkspaceProjectOwnershipResolver(Root, projects),
                    projects)
                .VerifyAsync(Root, syntax, query);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            if (Directory.Exists(ExternalRoot))
            {
                Directory.Delete(ExternalRoot, recursive: true);
            }
        }
    }

    private static string MultiTargetProject(string extra = "") =>
        $"""
        <PropertyGroup>
          <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
          <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
        </PropertyGroup>
        {ExplicitReferenceItemGroup()}
        {extra}
        """;

    private static string ExplicitFrameworkReference() =>
        $"""
        <PropertyGroup><DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences></PropertyGroup>
        {ExplicitReferenceItemGroup()}
        """;

    private static string ExplicitReferenceItemGroup() =>
        $"""
        <ItemGroup>
          <Reference Include="System.Private.CoreLib">
            <HintPath>{System.Security.SecurityElement.Escape(typeof(object).Assembly.Location)}</HintPath>
          </Reference>
        </ItemGroup>
        """;
}
