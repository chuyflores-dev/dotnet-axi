using System.Diagnostics;
using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class MsBuildProjectGraphEvaluatorTests
{
    private readonly WorkspaceDiscoverer _discoverer = new();
    private readonly WorkspaceEntryPointSelector _selector = new();
    private readonly MsBuildProjectGraphEvaluator _evaluator = new();
    private readonly RepositoryFixtureFactory _fixtures = new();

    [Theory]
    [InlineData("Supported", "net10.0")]
    [InlineData("Multi", "net9.0")]
    public async Task Explicit_declared_framework_is_accepted_for_single_and_multi_target_projects(
        string project,
        string framework)
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: project));

        var graph = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(framework: framework));

        Assert.Equal(ProjectGraphCompleteness.Complete, graph.Completeness);
        Assert.Equal(
            framework,
            Assert.Single(graph.Projects).Framework);
    }

    [Theory]
    [InlineData("Supported", "net9.0", "net10.0")]
    [InlineData("Multi", "net8.0", "net9.0;net10.0")]
    public async Task Explicit_undeclared_framework_is_a_stable_typed_usage_error(
        string project,
        string framework,
        string declaredFrameworks)
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: project));

        var error = Assert.Throws<ProjectGraphUsageException>(() =>
            _evaluator.Evaluate(
                discovery,
                selection,
                new ProjectGraphEvaluationOptions(framework: framework)));

        Assert.Equal(
            ProjectGraphUsageErrorKind.FrameworkNotDeclared,
            error.Kind);
        Assert.Equal("usage.framework_not_declared", error.Code);
        Assert.Equal(framework, error.Framework);
        var declaration = Assert.Single(error.Declarations);
        Assert.EndsWith($"/{project}/{project}.csproj", declaration.Project);
        Assert.Equal(
            declaredFrameworks.Split(';'),
            declaration.Frameworks);
        Assert.Contains(
            "--framework",
            error.Correction,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_framework_is_rejected_when_the_configuration_declares_none()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await File.WriteAllTextAsync(
            Path.Combine(
                fixture.WorkspacePath,
                "src",
                "Supported",
                "Supported.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "Supported"));

        var error = Assert.Throws<ProjectGraphUsageException>(() =>
            _evaluator.Evaluate(
                discovery,
                selection,
                new ProjectGraphEvaluationOptions(
                    configuration: "Release",
                    framework: "net10.0")));

        var declaration = Assert.Single(error.Declarations);
        Assert.Equal("src/Supported/Supported.csproj", declaration.Project);
        Assert.Empty(declaration.Frameworks);
        Assert.Contains("(none)", error.Correction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Solution_framework_validation_ignores_reference_specific_overrides()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "Coverage.slnx"),
            """
            <Solution>
              <Project Path="src/Multi/Multi.csproj" />
              <Project Path="src/Supported/Supported.csproj" />
            </Solution>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(
                fixture.WorkspacePath,
                "src",
                "Multi",
                "Multi.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference
                  Include="../Supported/Supported.csproj"
                  SetTargetFramework="TargetFramework=net9.0" />
              </ItemGroup>
            </Project>
            """);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Coverage.slnx"));

        var error = Assert.Throws<ProjectGraphUsageException>(() =>
            _evaluator.Evaluate(
                discovery,
                selection,
                new ProjectGraphEvaluationOptions(framework: "net9.0")));

        var declaration = Assert.Single(error.Declarations);
        Assert.Equal("src/Supported/Supported.csproj", declaration.Project);
        Assert.Equal(["net10.0"], declaration.Frameworks);
    }

    [Fact]
    public async Task Selected_project_honors_configuration_framework_and_properties()
    {
        await using var fixture = await EvaluationFixtureAsync();
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "App"));

        var graph = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(
                configuration: "Debug",
                framework: "net9.0",
                properties:
                [
                    new MsBuildProperty("Configuration", "Release"),
                    new MsBuildProperty("TargetFramework", "net10.0"),
                    new MsBuildProperty("Flavor", "conditional"),
                    new MsBuildProperty("IncludeCentralReference", "false"),
                    new MsBuildProperty("RepeatedProperty", "first"),
                    new MsBuildProperty("RepeatedProperty", "second"),
                ]));

        Assert.Equal(ProjectGraphCompleteness.Complete, graph.Completeness);
        Assert.Equal("10.0.302", graph.Runtime?.SdkVersion);
        Assert.StartsWith("18.6.", graph.Runtime?.MsBuildVersion);
        Assert.Empty(graph.Failures);
        Assert.Equal(WorkspaceSelectionSource.ExplicitProject, graph.Selection.Source);
        Assert.Equal(
            [
                "src/App/App.csproj",
                "src/Conditional/Conditional.csproj",
                "src/DebugOnly/DebugOnly.csproj",
                "src/NetNine/NetNine.csproj",
                "src/Repeated/Repeated.csproj",
            ],
            graph.Projects.Select(static project => project.Path));
        Assert.All(
            graph.Projects,
            static project =>
            {
                Assert.Equal("Debug", project.Configuration);
                Assert.Equal(EvaluatedProjectState.Evaluated, project.State);
            });
        Assert.Equal(
            "net9.0",
            Assert.Single(
                graph.Projects,
                static project => project.Path == "src/App/App.csproj").Framework);
        Assert.Equal(
            [
                new ProjectDependency(
                    "src/App/App.csproj",
                    "src/Conditional/Conditional.csproj"),
                new ProjectDependency(
                    "src/App/App.csproj",
                    "src/DebugOnly/DebugOnly.csproj"),
                new ProjectDependency(
                    "src/App/App.csproj",
                    "src/NetNine/NetNine.csproj"),
                new ProjectDependency(
                    "src/App/App.csproj",
                    "src/Repeated/Repeated.csproj"),
            ],
            graph.Dependencies);
        Assert.Equal(
            "Debug",
            PropertyValue(graph, "Configuration"));
        Assert.Equal(
            "net9.0",
            PropertyValue(graph, "TargetFramework"));
        Assert.Equal(
            "second",
            PropertyValue(graph, "RepeatedProperty"));
    }

    [Fact]
    public async Task Central_import_controls_an_evaluated_reference_and_global_property_wins()
    {
        await using var fixture = await EvaluationFixtureAsync();
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "App"));

        var imported = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(
                configuration: "Release",
                framework: "net10.0"));
        var overridden = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(
                configuration: "Release",
                framework: "net10.0",
                properties:
                [new MsBuildProperty("IncludeCentralReference", "false")]));

        Assert.Contains(
            new ProjectDependency(
                "src/App/App.csproj",
                "src/Central/Central.csproj"),
            imported.Dependencies);
        Assert.DoesNotContain(
            overridden.Dependencies,
            static dependency =>
                dependency.Dependency == "src/Central/Central.csproj");
    }

    [Fact]
    public async Task Selected_solution_includes_unreferenced_solution_projects_deterministically()
    {
        await using var fixture = await EvaluationFixtureAsync();
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Graph.slnx"));

        var first = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(
                configuration: "Release",
                framework: "net10.0"));
        var second = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(
                configuration: "Release",
                framework: "net10.0"));

        Assert.Equal(ProjectGraphCompleteness.Complete, first.Completeness);
        Assert.Equal(WorkspaceSelectionSource.ExplicitSolution, first.Selection.Source);
        Assert.Equal(
            [
                "src/App/App.csproj",
                "src/Central/Central.csproj",
                "src/Conditional/Conditional.csproj",
                "src/DebugOnly/DebugOnly.csproj",
                "src/NetNine/NetNine.csproj",
                "src/Repeated/Repeated.csproj",
                "src/SolutionOnly/SolutionOnly.csproj",
            ],
            first.Projects.Select(static project => project.Path));
        Assert.Contains(
            first.Projects,
            static project => project.Path == "src/SolutionOnly/SolutionOnly.csproj");
        Assert.Equal(
            first.Projects.Select(static project => project.Path),
            second.Projects.Select(static project => project.Path));
        Assert.Equal(first.Dependencies, second.Dependencies);
    }

    [Fact]
    public async Task Circular_graph_preserves_evaluated_projects_and_dependencies()
    {
        await using var fixture = await CatalogFixtureAsync("project-cycle");
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(
                configuration: "Debug",
                framework: "net10.0"));

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        Assert.Equal(
            ["src/A/A.csproj", "src/B/B.csproj"],
            graph.Projects.Select(static project => project.Path));
        Assert.All(
            graph.Projects,
            static project => Assert.Contains(
                project.Failures,
                static failure =>
                    failure.Reason
                    == ProjectEvaluationFailureReason.CircularDependency));
        Assert.Equal(
            [
                new ProjectDependency(
                    "src/A/A.csproj",
                    "src/B/B.csproj"),
                new ProjectDependency(
                    "src/B/B.csproj",
                    "src/A/A.csproj"),
            ],
            graph.Dependencies);
        Assert.Contains(
            graph.Failures,
            static failure =>
                failure.Reason
                == ProjectEvaluationFailureReason.CircularDependency);
    }

    [Fact]
    public async Task Circular_failure_is_limited_to_actual_cycle_participants()
    {
        await using var fixture = await ProjectGraphFixtureAsync("cycle-scope");
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        Assert.Equal(
            ["src/A/A.csproj", "src/B/B.csproj"],
            graph.Projects
                .Where(static project => project.Failures.Any(
                    static failure =>
                        failure.Reason
                        == ProjectEvaluationFailureReason.CircularDependency))
                .Select(static project => project.Path));
        Assert.DoesNotContain(
            Assert.Single(
                graph.Projects,
                static project => project.Path == "src/Root/Root.csproj").Failures,
            static failure =>
                failure.Reason
                == ProjectEvaluationFailureReason.CircularDependency);
        Assert.DoesNotContain(
            Assert.Single(
                graph.Projects,
                static project =>
                    project.Path == "src/Unrelated/Unrelated.csproj").Failures,
            static failure =>
                failure.Reason
                == ProjectEvaluationFailureReason.CircularDependency);
    }

    [Fact]
    public async Task Missing_assets_prevent_a_complete_graph_without_restore()
    {
        await using var fixture = await CatalogFixtureAsync("missing-assets");
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        var project = Assert.Single(graph.Projects);
        Assert.Equal("MissingAssets.csproj", project.Path);
        Assert.Equal(EvaluatedProjectState.Incomplete, project.State);
        Assert.Contains(
            project.Failures,
            static failure =>
                failure.Reason == ProjectEvaluationFailureReason.MissingAssets);
        Assert.False(Directory.Exists(
            Path.Combine(fixture.WorkspacePath, "obj")));
    }

    [Fact]
    public async Task Assets_without_the_exact_evaluated_framework_are_missing_assets()
    {
        await using var fixture = await CatalogFixtureAsync("missing-assets");
        var projectPath = Path.Combine(
            fixture.WorkspacePath,
            "MissingAssets.csproj");
        var assets = Assets("net9.0");
        await AddAssetsForProjectAsync(projectPath, assets);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        var project = Assert.Single(graph.Projects);
        Assert.Equal(EvaluatedProjectState.Incomplete, project.State);
        Assert.Equal(
            ProjectEvaluationFailureReason.MissingAssets,
            Assert.Single(project.Failures).Reason);
        Assert.Equal(
            assets,
            await File.ReadAllTextAsync(Path.Combine(
                fixture.WorkspacePath,
                "obj",
                "project.assets.json")));
    }

    [Theory]
    [InlineData("net10.0/linux-x64", ProjectGraphCompleteness.Complete)]
    [InlineData("net10.0", ProjectGraphCompleteness.Partial)]
    public async Task Assets_require_the_exact_evaluated_framework_and_runtime_target(
        string assetsTarget,
        ProjectGraphCompleteness expectedCompleteness)
    {
        await using var fixture = await CatalogFixtureAsync("missing-assets");
        var projectPath = Path.Combine(
            fixture.WorkspacePath,
            "MissingAssets.csproj");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
              </PropertyGroup>
            </Project>
            """);
        var assets = Assets(assetsTarget);
        await AddAssetsForProjectAsync(projectPath, assets);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(expectedCompleteness, graph.Completeness);
        var project = Assert.Single(graph.Projects);
        if (expectedCompleteness is ProjectGraphCompleteness.Complete)
        {
            Assert.Equal(EvaluatedProjectState.Evaluated, project.State);
            Assert.Empty(project.Failures);
        }
        else
        {
            Assert.Equal(EvaluatedProjectState.Incomplete, project.State);
            Assert.Equal(
                ProjectEvaluationFailureReason.MissingAssets,
                Assert.Single(project.Failures).Reason);
        }

        Assert.Equal(
            assets,
            await File.ReadAllTextAsync(Path.Combine(
                fixture.WorkspacePath,
                "obj",
                "project.assets.json")));
    }

    [Fact]
    public async Task Malformed_assets_have_a_stable_typed_failure_without_writes()
    {
        await using var fixture = await CatalogFixtureAsync("missing-assets");
        var projectPath = Path.Combine(
            fixture.WorkspacePath,
            "MissingAssets.csproj");
        const string malformedAssets = "{ invalid";
        await AddAssetsForProjectAsync(projectPath, malformedAssets);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        var failure = Assert.Single(Assert.Single(graph.Projects).Failures);
        Assert.Equal(
            ProjectEvaluationFailureReason.InvalidAssetsFile,
            failure.Reason);
        Assert.Equal("assets.invalid", failure.AuthorityCode);
        Assert.Equal(
            malformedAssets,
            await File.ReadAllTextAsync(Path.Combine(
                fixture.WorkspacePath,
                "obj",
                "project.assets.json")));
    }

    [Fact]
    public async Task Evaluation_failure_remains_a_typed_failed_project()
    {
        await using var fixture = await ProjectGraphFixtureAsync(
            "evaluation-failure");
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        Assert.Equal(
            ["src/Broken/Broken.csproj", "src/Good/Good.csproj"],
            graph.Projects.Select(static project => project.Path));
        var brokenProject = Assert.Single(
            graph.Projects,
            static project => project.Path == "src/Broken/Broken.csproj");
        Assert.Equal(EvaluatedProjectState.Failed, brokenProject.State);
        var failure = Assert.Single(brokenProject.Failures);
        Assert.Equal(
            ProjectEvaluationFailureReason.InvalidProjectFile,
            failure.Reason);
        Assert.Equal("MSB4025", failure.AuthorityCode);
        Assert.Single(
            graph.Projects,
            static project => project.Path == "src/Good/Good.csproj");
        Assert.Contains(
            graph.Failures,
            static graphFailure =>
                graphFailure.Reason
                == ProjectEvaluationFailureReason.InvalidProjectFile);
    }

    [Fact]
    public async Task Malformed_graph_metadata_preserves_a_graph_level_reason()
    {
        await using var fixture = await ProjectGraphFixtureAsync(
            "malformed-graph-metadata");
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "App"));

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        Assert.NotEmpty(graph.Projects);
        Assert.NotEmpty(graph.Failures);
        Assert.Contains(
            graph.Failures,
            static failure =>
                failure.Reason
                is ProjectEvaluationFailureReason.InvalidProjectFile
                or ProjectEvaluationFailureReason.EvaluationFailed);
    }

    [Fact]
    public async Task Directory_symlink_escape_is_rejected_before_external_evaluation()
    {
        await using var fixture = await EvaluationFixtureAsync();
        await AddAssetsAsync(fixture.WorkspacePath);
        var externalProject = Path.Combine(
            fixture.ExternalPath,
            "External.csproj");
        await WriteSimpleProjectAsync(externalProject);
        var linkPath = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App",
            "external-link");
        if (!TryCreateDirectorySymbolicLink(linkPath, fixture.ExternalPath))
        {
            return;
        }

        await WriteProjectWithReferenceAsync(
            Path.Combine(
                fixture.WorkspacePath,
                "src",
                "App",
                "App.csproj"),
            "external-link/External.csproj");
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "App"));

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        var escaped = Assert.Single(
            graph.Projects,
            static project => project.Path == "../external/External.csproj");
        Assert.True(escaped.IsExternal);
        Assert.Equal(EvaluatedProjectState.Failed, escaped.State);
        Assert.Contains(
            escaped.Failures,
            static failure =>
                failure.Reason
                == ProjectEvaluationFailureReason.WorkspacePathEscape);
        Assert.Contains(
            graph.Failures,
            static failure =>
                failure.AuthorityCode == "workspace.project_link_escape");
    }

    [Fact]
    public async Task External_project_paths_are_relative_and_relocation_stable()
    {
        var first = await EvaluateExternalReferenceAsync();
        var second = await EvaluateExternalReferenceAsync();

        Assert.Equal(first, second);
        Assert.Contains("../external/External.csproj", first);
        Assert.DoesNotContain(first, Path.IsPathRooted);
    }

    [Fact]
    public async Task Unavailable_solution_retains_known_members_without_inventing_solution_project()
    {
        await using var fixture = await EvaluationFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Graph.slnx"));
        var failure = new ProjectEvaluationFailure(
            ProjectEvaluationFailureReason.MsBuildUnavailable,
            "sdk.selection_failed");
        var evaluator = new MsBuildProjectGraphEvaluator(
            new StubRuntimeAuthority(failure));

        var graph = evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Failed, graph.Completeness);
        Assert.Equal(
            [
                "src/App/App.csproj",
                "src/Central/Central.csproj",
                "src/Conditional/Conditional.csproj",
                "src/DebugOnly/DebugOnly.csproj",
                "src/NetNine/NetNine.csproj",
                "src/Repeated/Repeated.csproj",
                "src/SolutionOnly/SolutionOnly.csproj",
            ],
            graph.Projects.Select(static project => project.Path));
        Assert.All(
            graph.Projects,
            project =>
            {
                Assert.Equal(EvaluatedProjectState.Failed, project.State);
                Assert.Contains(failure, project.Failures);
            });
        Assert.DoesNotContain(
            graph.Projects,
            static project => project.Path.EndsWith(
                ".slnx",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(failure, Assert.Single(graph.Failures));
        Assert.Null(graph.Runtime);
    }

    [Fact]
    public async Task Early_slnx_failure_retains_known_members_without_inventing_solution_project()
    {
        await using var fixture = await EvaluationFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Graph.slnx"));
        var evaluator = new MsBuildProjectGraphEvaluator(
            new MsBuildRuntimeAuthority(),
            static _ => throw new IOException("Rejected solution."));

        var graph = evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Failed, graph.Completeness);
        Assert.NotNull(graph.Runtime);
        Assert.Equal(
            [
                "src/App/App.csproj",
                "src/Central/Central.csproj",
                "src/Conditional/Conditional.csproj",
                "src/DebugOnly/DebugOnly.csproj",
                "src/NetNine/NetNine.csproj",
                "src/Repeated/Repeated.csproj",
                "src/SolutionOnly/SolutionOnly.csproj",
            ],
            graph.Projects.Select(static project => project.Path));
        var failure = Assert.Single(graph.Failures);
        Assert.Equal(
            ProjectEvaluationFailureReason.EvaluationFailed,
            failure.Reason);
        Assert.All(
            graph.Projects,
            project =>
            {
                Assert.Equal(EvaluatedProjectState.Incomplete, project.State);
                Assert.Equal(failure, Assert.Single(project.Failures));
            });
        Assert.DoesNotContain(
            graph.Projects,
            static project => project.Path.EndsWith(
                ".slnx",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cross_volume_external_paths_use_nonrooted_hashed_root_identities()
    {
        var driveC = WorkspacePathResolver.CrossVolumeExternalIdentity(
            @"C:\",
            @"src\App\App.csproj");
        var driveD = WorkspacePathResolver.CrossVolumeExternalIdentity(
            @"D:\",
            @"src\App\App.csproj");
        var unc = WorkspacePathResolver.CrossVolumeExternalIdentity(
            @"\\server\share\",
            @"src\App\App.csproj");

        Assert.True(driveC.IsExternal);
        Assert.True(driveD.IsExternal);
        Assert.True(unc.IsExternal);
        Assert.False(Path.IsPathRooted(driveC.Path));
        Assert.False(Path.IsPathRooted(driveD.Path));
        Assert.False(Path.IsPathRooted(unc.Path));
        Assert.StartsWith("../.external-volume/", driveC.Path);
        Assert.EndsWith("/src/App/App.csproj", driveC.Path);
        Assert.DoesNotContain('\\', driveC.Path);
        Assert.DoesNotContain("C:", driveC.Path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "server",
            unc.Path,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(driveC.Path, driveD.Path);
        Assert.NotEqual(driveC.Path, unc.Path);
        Assert.Equal(
            driveC.Path,
            WorkspacePathResolver.CrossVolumeExternalIdentity(
                "c:/",
                "src/App/App.csproj").Path);
    }

    [Fact]
    public void Cycle_participants_are_computed_before_project_path_aggregation()
    {
        ProjectInstanceGraphNode[] nodes =
        [
            new("A-net8", "A.csproj"),
            new("B-net8", "B.csproj"),
            new("B-net9", "B.csproj"),
            new("C-net8", "C.csproj"),
            new("C-net9", "C.csproj"),
        ];
        ProjectInstanceGraphEdge[] edges =
        [
            new("A-net8", "B-net8"),
            new("B-net8", "A-net8"),
            new("B-net9", "C-net9"),
            new("C-net8", "A-net8"),
        ];

        var participants = MsBuildProjectGraphEvaluator.CycleParticipantPaths(
            nodes,
            edges);

        Assert.Equal(
            ["A.csproj", "B.csproj"],
            participants.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("C.csproj", participants);
    }

    [Fact]
    public void Project_instance_identity_uses_platform_path_and_msbuild_property_semantics()
    {
        var upperPath = Path.Combine(
            Path.GetTempPath(),
            "ProjectIdentity",
            "Source",
            "App.csproj");
        var lowerPath = Path.Combine(
            Path.GetTempPath(),
            "ProjectIdentity",
            "source",
            "App.csproj");
        var properties = new Dictionary<string, string>
        {
            ["Configuration"] = "CaseSensitiveValue",
            ["Platform"] = "AnyCPU",
        };
        var equivalentProperties = new Dictionary<string, string>
        {
            ["platform"] = "AnyCPU",
            ["configuration"] = "CaseSensitiveValue",
        };
        var changedValueProperties = new Dictionary<string, string>(
            equivalentProperties)
        {
            ["configuration"] = "casesensitivevalue",
        };

        var unixUpper = MsBuildProjectGraphEvaluator.ProjectInstanceIdentity(
            upperPath,
            properties,
            isWindows: false);
        var unixLower = MsBuildProjectGraphEvaluator.ProjectInstanceIdentity(
            lowerPath,
            equivalentProperties,
            isWindows: false);
        var windowsUpper = MsBuildProjectGraphEvaluator.ProjectInstanceIdentity(
            upperPath,
            properties,
            isWindows: true);
        var windowsLower = MsBuildProjectGraphEvaluator.ProjectInstanceIdentity(
            lowerPath,
            equivalentProperties,
            isWindows: true);

        Assert.NotEqual(unixUpper, unixLower);
        Assert.Equal(windowsUpper, windowsLower);
        Assert.Equal(
            unixUpper,
            MsBuildProjectGraphEvaluator.ProjectInstanceIdentity(
                upperPath,
                equivalentProperties,
                isWindows: false));
        Assert.NotEqual(
            unixUpper,
            MsBuildProjectGraphEvaluator.ProjectInstanceIdentity(
                upperPath,
                changedValueProperties,
                isWindows: false));
    }

    [Fact]
    public async Task Multi_instance_cycle_does_not_attribute_variant_only_path_cycle()
    {
        await using var fixture = await EvaluationFixtureAsync();
        var variantDirectory = Path.Combine(fixture.WorkspacePath, "variants");
        Directory.CreateDirectory(variantDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(variantDirectory, "Root.csproj"),
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="A.csproj" AdditionalProperties="Variant=net8" />
                <ProjectReference Include="B.csproj" AdditionalProperties="Variant=net9" />
                <ProjectReference Include="C.csproj" AdditionalProperties="Variant=net8" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(variantDirectory, "A.csproj"),
            """
            <Project>
              <ItemGroup Condition="'$(Variant)' == 'net8'">
                <ProjectReference Include="B.csproj" AdditionalProperties="Variant=net8" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(variantDirectory, "B.csproj"),
            """
            <Project>
              <ItemGroup Condition="'$(Variant)' == 'net8'">
                <ProjectReference Include="A.csproj" AdditionalProperties="Variant=net8" />
              </ItemGroup>
              <ItemGroup Condition="'$(Variant)' == 'net9'">
                <ProjectReference Include="C.csproj" AdditionalProperties="Variant=net9" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(variantDirectory, "C.csproj"),
            """
            <Project>
              <ItemGroup Condition="'$(Variant)' == 'net8'">
                <ProjectReference Include="A.csproj" AdditionalProperties="Variant=net8" />
              </ItemGroup>
            </Project>
            """);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "Root"));

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Partial, graph.Completeness);
        Assert.Equal(
            ["variants/A.csproj", "variants/B.csproj"],
            graph.Projects
                .Where(static project => project.Failures.Any(
                    static failure => failure.Reason
                        is ProjectEvaluationFailureReason.CircularDependency))
                .Select(static project => project.Path));
        Assert.DoesNotContain(
            Assert.Single(
                graph.Projects,
                static project => project.Path == "variants/C.csproj").Failures,
            static failure => failure.Reason
                is ProjectEvaluationFailureReason.CircularDependency);
        Assert.DoesNotContain(
            Assert.Single(
                graph.Projects,
                static project => project.Path == "variants/Root.csproj").Failures,
            static failure => failure.Reason
                is ProjectEvaluationFailureReason.CircularDependency);
    }

    [Fact]
    public async Task Host_runner_cancellation_kills_the_entire_process_tree_and_preserves_token()
    {
        var process = new StubHostProcess(
            TextReader.Null,
            TextReader.Null,
            exitImmediately: false);
        var factory = new StubHostProcessFactory(process);
        var runner = HostRunner(factory, timeout: TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        var run = Task.Run(() => runner.Run(
            Path.GetTempPath(),
            ["--version"],
            cancellation.Token));
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await run);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(process.KillCalled);
        Assert.True(process.EntireProcessTree);
        Assert.False(process.ParentAlive);
        Assert.False(process.ChildAlive);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task Host_runner_timeout_bounds_post_exit_drains_and_kills_surviving_children()
    {
        var blockingOutput = new CancellationAwareTextReader();
        var process = new StubHostProcess(
            blockingOutput,
            TextReader.Null,
            exitImmediately: true);
        var factory = new StubHostProcessFactory(process);
        var runner = HostRunner(factory, timeout: TimeSpan.Zero);

        var result = runner.Run(
            Path.GetTempPath(),
            ["--list-sdks"],
            CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
        await blockingOutput.CancellationObserved.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.True(process.KillCalled);
        Assert.True(process.EntireProcessTree);
        Assert.False(process.ChildAlive);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task Host_runner_bounds_uncooperative_process_and_read_operations()
    {
        var process = new UncooperativeHostProcess();
        var factory = new StubHostProcessFactory(process);
        var runner = HostRunner(factory, timeout: TimeSpan.Zero);

        var run = Task.Run(() => runner.Run(
            Path.GetTempPath(),
            ["--version"],
            CancellationToken.None));
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(-1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
        Assert.True(process.KillCalled);
        Assert.True(process.EntireProcessTree);
        Assert.True(process.Disposed);

        process.FailPendingOperations();
        Assert.True(process.ExitOperation.IsFaulted);
        Assert.True(process.Output.Operation.IsFaulted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Host_runner_caps_output_and_kills_the_entire_process_tree_on_overflow(
        bool overflowStandardError)
    {
        const int outputLimit = 32;
        var output = new TrackingTextReader(new string('x', 4096));
        var process = new StubHostProcess(
            overflowStandardError ? TextReader.Null : output,
            overflowStandardError ? output : TextReader.Null,
            exitImmediately: false);
        var factory = new StubHostProcessFactory(process);
        var runner = HostRunner(
            factory,
            timeout: TimeSpan.FromMinutes(1),
            outputLimit);

        var result = runner.Run(
            Path.GetTempPath(),
            ["--version"],
            CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
        Assert.InRange(output.CharactersRead, 1, outputLimit + 1);
        Assert.True(process.KillCalled);
        Assert.True(process.EntireProcessTree);
        Assert.False(process.ParentAlive);
        Assert.False(process.ChildAlive);
        Assert.True(process.Disposed);
    }

    [Fact]
    public void Posix_path_resolution_skips_non_executable_dotnet_candidate()
    {
        var firstDirectory = Path.Combine(Path.GetTempPath(), "first-dotnet");
        var secondDirectory = Path.Combine(Path.GetTempPath(), "second-dotnet");
        var firstCandidate = Path.GetFullPath(
            Path.Combine(firstDirectory, "dotnet"));
        var secondCandidate = Path.GetFullPath(
            Path.Combine(secondDirectory, "dotnet"));
        var pathValue = string.Join(
            Path.PathSeparator,
            firstDirectory,
            secondDirectory);

        var resolved = PathDotNetHostRunner.ResolveHostPath(
            pathValue,
            isWindows: false,
            candidate => candidate.Equals(firstCandidate, StringComparison.Ordinal)
                         || candidate.Equals(
                             secondCandidate,
                             StringComparison.Ordinal),
            candidate => candidate.Equals(
                secondCandidate,
                StringComparison.Ordinal));

        Assert.Equal(secondCandidate, resolved);
    }

    [Fact]
    public void Posix_path_resolution_honors_an_empty_current_directory_component()
    {
        var currentDirectoryCandidate = Path.GetFullPath("dotnet");
        var fallbackDirectory = Path.Combine(
            Path.GetTempPath(),
            "fallback-dotnet");
        var fallbackCandidate = Path.Combine(fallbackDirectory, "dotnet");

        var resolved = PathDotNetHostRunner.ResolveHostPath(
            string.Concat(Path.PathSeparator, fallbackDirectory),
            isWindows: false,
            candidate => candidate.Equals(
                             currentDirectoryCandidate,
                             StringComparison.Ordinal)
                         || candidate.Equals(
                             fallbackCandidate,
                             StringComparison.Ordinal),
            static _ => true);

        Assert.Equal(currentDirectoryCandidate, resolved);
    }

    [Theory]
    [InlineData(" sdk with spaces ")]
    [InlineData("\"quoted-sdk\"")]
    public void Posix_path_resolution_preserves_component_text(string directory)
    {
        var expected = Path.GetFullPath(Path.Combine(directory, "dotnet"));

        var resolved = PathDotNetHostRunner.ResolveHostPath(
            directory,
            isWindows: false,
            candidate => candidate.Equals(expected, StringComparison.Ordinal),
            static _ => true);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Posix_executable_authority_uses_the_callers_permission_class()
    {
        if (OperatingSystem.IsWindows()
            || PosixProcessAuthority.UsesSuperUserExecutionSemantics)
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-executable-{Guid.NewGuid():N}");
        var firstDirectory = Path.Combine(root, "first");
        var secondDirectory = Path.Combine(root, "second");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        var firstCandidate = Path.Combine(firstDirectory, "dotnet");
        var secondCandidate = Path.Combine(secondDirectory, "dotnet");

        try
        {
            File.WriteAllText(firstCandidate, "not executable by its owner");
            File.WriteAllText(secondCandidate, "executable by its owner");
            File.SetUnixFileMode(
                firstCandidate,
                UnixFileMode.OtherExecute);
            File.SetUnixFileMode(
                secondCandidate,
                UnixFileMode.UserExecute);

            Assert.False(PosixProcessAuthority.CanExecute(firstCandidate));
            Assert.True(PosixProcessAuthority.CanExecute(secondCandidate));

            var resolved = PathDotNetHostRunner.ResolveHostPath(
                string.Join(
                    Path.PathSeparator,
                    firstDirectory,
                    secondDirectory),
                isWindows: false,
                File.Exists,
                PosixProcessAuthority.CanExecute);

            Assert.Equal(secondCandidate, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Posix_contained_host_preserves_cwd_arguments_and_output()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-contained-success-{Guid.NewGuid():N}",
            "working directory");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "working-directory.marker"),
            string.Empty);
        var script = WriteExecutableScript(
            root,
            "host",
            """
            #!/bin/sh
            test -f working-directory.marker || exit 13
            printf 'cwd-ok|%s\n' "$1"
            printf 'host-error\n' >&2
            """);

        try
        {
            var runner = new PathDotNetHostRunner(
                new SystemDotNetHostProcessFactory(),
                () => script,
                TimeSpan.FromSeconds(5),
                4096);

            var result = runner.Run(
                root,
                ["argument with spaces"],
                CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                "cwd-ok|argument with spaces\n",
                result.StandardOutput);
            Assert.Equal("host-error\n", result.StandardError);
        }
        finally
        {
            Directory.Delete(
                Directory.GetParent(root)!.FullName,
                recursive: true);
        }
    }

    [Fact]
    public async Task Posix_completion_terminates_the_owned_group_before_reaping()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"dnaxi-contained-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var childPidPath = Path.Combine(root, "child.pid");
        var script = WriteExecutableScript(
            root,
            "host",
            """
            #!/bin/sh
            sleep 30 &
            child=$!
            printf '%s' "$child" > "$1"
            printf 'leader-finished\n'
            exit 0
            """);
        var childPid = 0;

        try
        {
            var runner = new PathDotNetHostRunner(
                new SystemDotNetHostProcessFactory(),
                () => script,
                TimeSpan.FromMilliseconds(500),
                4096);
            var stopwatch = Stopwatch.StartNew();

            var result = runner.Run(
                root,
                [childPidPath],
                CancellationToken.None);

            stopwatch.Stop();
            Assert.Equal(0, result.ExitCode);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"Contained host took {stopwatch.Elapsed}.");
            Assert.True(File.Exists(childPidPath));
            childPid = int.Parse(
                await File.ReadAllTextAsync(childPidPath),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(
                await WaitForProcessExitAsync(
                    childPid,
                    TimeSpan.FromSeconds(5)),
                $"Child process {childPid} survived group termination.");
        }
        finally
        {
            TerminateProcessIfRunning(childPid);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Posix_group_lifetime_never_signals_after_the_leader_is_reaped()
    {
        var events = new List<string>();
        var lifetime = new PosixOwnedProcessGroup(
            123,
            _ => events.Add("wait-without-reaping"),
            _ =>
            {
                events.Add("reap");
                return 7;
            },
            _ => events.Add("signal"));

        var exitCode = lifetime.WaitForExitAndContainDescendants();
        lifetime.Terminate();

        Assert.Equal(7, exitCode);
        Assert.Equal(
            ["wait-without-reaping", "signal", "reap"],
            events);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Posix_file_actions_never_close_standard_descriptors(
        int fileDescriptor,
        bool shouldClose)
    {
        Assert.Equal(
            shouldClose,
            PosixProcessAuthority.ShouldCloseChildFileDescriptor(fileDescriptor));
    }

    [Theory]
    [InlineData("8.0.408")]
    [InlineData("9.0.308")]
    [InlineData("10.0.302")]
    public void Sdk_selector_uses_host_selected_version_and_exact_inventory_instance(
        string selectedVersion)
    {
        var sdkBase = Path.Combine(Path.GetTempPath(), "dotnet-sdk inventory");
        var runner = new StubDotNetHostRunner(
            new DotNetHostResult(0, $"{selectedVersion}\n", string.Empty),
            new DotNetHostResult(
                0,
                $"8.0.408 [{sdkBase}]\n9.0.308 [{sdkBase}]\n10.0.302 [{sdkBase}]\n",
                string.Empty));
        var selector = new DotNetSdkSelector(runner);
        var workspace = Path.Combine(Path.GetTempPath(), "sdk-selection-workspace");

        var selection = selector.Select(workspace, CancellationToken.None);

        Assert.True(selection.IsSelected);
        Assert.Equal(selectedVersion, selection.Sdk?.Version);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(sdkBase, selectedVersion)),
            selection.Sdk?.SdkPath);
        Assert.Equal(
            ["--version", "--list-sdks"],
            runner.Calls.Select(static call => Assert.Single(call.Arguments)));
        Assert.All(
            runner.Calls,
            call => Assert.Equal(workspace, call.WorkingDirectory));
    }

    [Fact]
    public void Registered_different_sdk_is_a_compatibility_mismatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "msbuild-registration");
        var selected = Path.Combine(root, "9.0.308");

        Assert.Equal(
            MsBuildRegistrationDecision.Mismatch,
            MsBuildRuntimeAuthority.RegistrationDecision(
                selected,
                isRegistered: true,
                loadedMsBuildPath: null,
                registeredMsBuildPath: Path.Combine(root, "10.0.302")));
        Assert.Equal(
            MsBuildRegistrationDecision.UseRegistered,
            MsBuildRuntimeAuthority.RegistrationDecision(
                selected,
                isRegistered: true,
                loadedMsBuildPath: null,
                registeredMsBuildPath: selected));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preloaded_unregistered_msbuild_never_attempts_registration(
        bool loadedPathMatchesSelection)
    {
        var root = Path.Combine(Path.GetTempPath(), "preloaded-msbuild");
        var selected = Path.Combine(root, "10.0.302");
        var loaded = loadedPathMatchesSelection
            ? selected
            : Path.Combine(root, "9.0.308");
        var registrar = new StubMsBuildRuntimeRegistrar(
            isRegistered: false,
            loaded);

        var result = MsBuildRuntimeAuthority.ResolveRegistration(
            new MsBuildRuntimeIdentity("10.0.302", "18.6.0"),
            selected,
            registrar,
            registrar.LoadedMsBuildPath());

        Assert.False(result.IsAvailable);
        Assert.Equal(
            new ProjectEvaluationFailure(
                ProjectEvaluationFailureReason.MsBuildIncompatible,
                "msbuild.registration_mismatch"),
            result.Failure);
        Assert.Equal(0, registrar.RegistrationCalls);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("two words\\", "\"two words\\\\\"")]
    public void Windows_arguments_are_quoted_for_create_process(
        string value,
        string expected)
    {
        Assert.Equal(expected, WindowsJobProcess.QuoteWindowsArgument(value));
    }

    [Fact]
    public async Task Compatibility_failure_is_structured_and_does_not_evaluate()
    {
        await using var fixture = await EvaluationFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "App"));
        var failure = new ProjectEvaluationFailure(
            ProjectEvaluationFailureReason.MsBuildIncompatible,
            "msbuild.registration_mismatch");
        var evaluator = new MsBuildProjectGraphEvaluator(
            new StubRuntimeAuthority(failure));

        var graph = evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Failed, graph.Completeness);
        Assert.Equal(failure, Assert.Single(graph.Failures));
        var project = Assert.Single(graph.Projects);
        Assert.Equal("src/App/App.csproj", project.Path);
        Assert.Equal(failure, Assert.Single(project.Failures));
    }

    [Fact]
    public async Task Discovery_and_selection_do_not_evaluate_the_project()
    {
        await using var fixture = await CatalogFixtureAsync("broken-project");

        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(discovery);

        Assert.Equal("Broken.csproj", selection.Path);
        Assert.Equal(WorkspaceEntryPointKind.Project, selection.Kind);
    }

    private async ValueTask<RepositoryFixture> EvaluationFixtureAsync() =>
        await ProjectGraphFixtureAsync("evaluation");

    private async ValueTask<RepositoryFixture> ProjectGraphFixtureAsync(
        string fixtureName) =>
        await _fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ProjectGraph",
                fixtureName,
                "fixture.json"));

    private async ValueTask<RepositoryFixture> CatalogFixtureAsync(
        string fixtureName) =>
        await _fixtures.CreateAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Catalog",
                fixtureName,
                "fixture.json"));

    private async Task<string[]> EvaluateExternalReferenceAsync()
    {
        await using var fixture = await EvaluationFixtureAsync();
        await AddAssetsAsync(fixture.WorkspacePath);
        var externalProject = Path.Combine(
            fixture.ExternalPath,
            "External.csproj");
        await WriteSimpleProjectAsync(externalProject);
        await AddAssetsForProjectAsync(externalProject);
        var appProject = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "App",
            "App.csproj");
        await WriteProjectWithReferenceAsync(
            appProject,
            Path.GetRelativePath(Path.GetDirectoryName(appProject)!, externalProject));
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "App"));

        var graph = _evaluator.Evaluate(discovery, selection);

        Assert.Equal(ProjectGraphCompleteness.Complete, graph.Completeness);
        Assert.True(Assert.Single(
            graph.Projects,
            static project =>
                project.Path == "../external/External.csproj").IsExternal);
        return graph.Projects.Select(static project => project.Path).ToArray();
    }

    private static async Task AddAssetsAsync(string workspacePath)
    {
        foreach (var projectPath in Directory.EnumerateFiles(
                     workspacePath,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var assetsDirectory = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                "obj");
            Directory.CreateDirectory(assetsDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(assetsDirectory, "project.assets.json"),
                Assets(
                    "net8.0",
                    "net9.0",
                    "net10.0",
                    "net10.0-windows"));
        }
    }

    private static async Task AddAssetsForProjectAsync(
        string projectPath,
        string? contents = null)
    {
        var assetsDirectory = Path.Combine(
            Path.GetDirectoryName(projectPath)!,
            "obj");
        Directory.CreateDirectory(assetsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(assetsDirectory, "project.assets.json"),
            contents ?? Assets(
                "net8.0",
                "net9.0",
                "net10.0",
                "net10.0-windows"));
    }

    private static string Assets(params string[] targets) =>
        $"{{\"version\":3,\"targets\":{{{string.Join(',', targets.Select(
            static target => $"\"{target}\":{{}}"))}}}}}";

    private static async Task WriteSimpleProjectAsync(string projectPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
    }

    private static async Task WriteProjectWithReferenceAsync(
        string projectPath,
        string referencePath)
    {
        await File.WriteAllTextAsync(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="{referencePath}" />
              </ItemGroup>
            </Project>
            """);
    }

    private static bool TryCreateDirectorySymbolicLink(
        string path,
        string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static string WriteExecutableScript(
        string directory,
        string fileName,
        string content)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute);
        return path;
    }

    private static async Task<bool> WaitForProcessExitAsync(
        int processId,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        return false;
    }

    private static void TerminateProcessIfRunning(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or InvalidOperationException
                  or System.ComponentModel.Win32Exception)
        {
            // The child already exited or cannot be observed further.
        }
    }

    private static string PropertyValue(
        EvaluatedProjectGraph graph,
        string name) =>
        Assert.Single(
            graph.GlobalProperties,
            property => property.Name.Equals(
                name,
                StringComparison.OrdinalIgnoreCase)).Value;

    private static PathDotNetHostRunner HostRunner(
        IDotNetHostProcessFactory processFactory,
        TimeSpan timeout,
        int outputLimit = 64 * 1024) =>
        new(
            processFactory,
            static () => "test-dotnet",
            timeout,
            outputLimit);

    private sealed class StubRuntimeAuthority(ProjectEvaluationFailure failure)
        : IMsBuildRuntimeAuthority
    {
        public MsBuildAuthorityResult ResolveAndRegister(
            string workspaceRoot,
            CancellationToken cancellationToken) =>
            new(null, failure);
    }

    private sealed class StubMsBuildRuntimeRegistrar(
        bool isRegistered,
        string? loadedMsBuildPath) : IMsBuildRuntimeRegistrar
    {
        public bool IsRegistered { get; } = isRegistered;

        public int RegistrationCalls { get; private set; }

        public string? LoadedMsBuildPath() => loadedMsBuildPath;

        public void RegisterMsBuildPath(string path)
        {
            RegistrationCalls++;
        }
    }

    private sealed class StubDotNetHostRunner(
        params DotNetHostResult[] results) : IDotNetHostRunner
    {
        private readonly Queue<DotNetHostResult> _results = new(results);

        public List<DotNetHostCall> Calls { get; } = [];

        public DotNetHostResult Run(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add(new DotNetHostCall(workingDirectory, [.. arguments]));
            return _results.Dequeue();
        }
    }

    private sealed record DotNetHostCall(
        string WorkingDirectory,
        IReadOnlyList<string> Arguments);

    private sealed class StubHostProcessFactory(IDotNetHostProcess process)
        : IDotNetHostProcessFactory
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public IDotNetHostProcess Start(ProcessStartInfo startInfo)
        {
            Started.TrySetResult();
            return process;
        }
    }

    private sealed class StubHostProcess(
        TextReader standardOutput,
        TextReader standardError,
        bool exitImmediately) : IDotNetHostProcess
    {
        private readonly TaskCompletionSource _exit = CompletedExit(
            exitImmediately);

        public TextReader StandardOutput { get; } = standardOutput;

        public TextReader StandardError { get; } = standardError;

        public int ExitCode => 0;

        public bool ParentAlive { get; private set; } = !exitImmediately;

        public bool ChildAlive { get; private set; } = true;

        public bool KillCalled { get; private set; }

        public bool EntireProcessTree { get; private set; }

        public bool Disposed { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _exit.Task.WaitAsync(cancellationToken);

        public void TerminateTree()
        {
            KillCalled = true;
            EntireProcessTree = true;
            ParentAlive = false;
            ChildAlive = false;

            _exit.TrySetResult();
        }

        public void Dispose()
        {
            Disposed = true;
        }

        private static TaskCompletionSource CompletedExit(bool completed)
        {
            var exit = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (completed)
            {
                exit.SetResult();
            }

            return exit;
        }
    }

    private sealed class UncooperativeHostProcess : IDotNetHostProcess
    {
        private readonly TaskCompletionSource _exit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public UncooperativeTextReader Output { get; } = new();

        public TextReader StandardOutput => Output;

        public TextReader StandardError => TextReader.Null;

        public int ExitCode => 0;

        public bool KillCalled { get; private set; }

        public bool EntireProcessTree { get; private set; }

        public bool Disposed { get; private set; }

        public Task ExitOperation => _exit.Task;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _exit.Task;

        public void TerminateTree()
        {
            KillCalled = true;
            EntireProcessTree = true;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        public void FailPendingOperations()
        {
            _exit.TrySetException(new IOException("Late exit failure."));
            Output.Fail();
        }
    }

    private sealed class UncooperativeTextReader : TextReader
    {
        private readonly TaskCompletionSource<int> _read = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> Operation => _read.Task;

        public override ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default) =>
            new(_read.Task);

        public void Fail() =>
            _read.TrySetException(new IOException("Late read failure."));
    }

    private sealed class TrackingTextReader(string value) : TextReader
    {
        private int _offset;

        public int CharactersRead { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, value.Length - _offset);
            if (count == 0)
            {
                return ValueTask.FromResult(0);
            }

            value.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            CharactersRead += count;
            return ValueTask.FromResult(count);
        }
    }

    private sealed class CancellationAwareTextReader : TextReader
    {
        private readonly TaskCompletionSource _cancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CancellationObserved => _cancellationObserved.Task;

        public override async ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
            catch (OperationCanceledException)
            {
                _cancellationObserved.TrySetResult();
                throw;
            }
        }
    }
}
