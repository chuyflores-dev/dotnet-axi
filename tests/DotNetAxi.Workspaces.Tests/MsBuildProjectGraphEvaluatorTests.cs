using DotNetAxi.Contracts;
using DotNetAxi.DotNet;
using DotNetAxi.Testing;

namespace DotNetAxi.Workspaces.Tests;

public sealed class MsBuildProjectGraphEvaluatorTests
{
    private readonly WorkspaceDiscoverer _discoverer = new();
    private readonly WorkspaceEntryPointSelector _selector = new();
    private readonly MsBuildProjectGraphEvaluator _evaluator =
        new(new DotNetHostResolver());
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
    public async Task Solution_framework_validation_honors_member_configuration_mapping()
    {
        await using var fixture = await ProjectGraphFixtureAsync("coverage");
        var projectDirectory = Path.Combine(
            fixture.WorkspacePath,
            "src",
            "Mapped");
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Mapped.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' != 'Debug'">
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "Mapped.sln"),
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Mapped", "src\Mapped\Mapped.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Global
                GlobalSection(SolutionConfigurationPlatforms) = preSolution
                    Release|Any CPU = Release|Any CPU
                EndGlobalSection
                GlobalSection(ProjectConfigurationPlatforms) = postSolution
                    {11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Debug|Any CPU
                    {11111111-1111-1111-1111-111111111111}.Release|Any CPU.Build.0 = Debug|Any CPU
                EndGlobalSection
            EndGlobal
            """);
        await AddAssetsAsync(fixture.WorkspacePath);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Mapped.sln"));
        var platform = new MsBuildProperty("Platform", "Any CPU");

        var accepted = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(
                configuration: "Release",
                framework: "net10.0",
                properties: [platform]));
        var error = Assert.Throws<ProjectGraphUsageException>(() =>
            _evaluator.Evaluate(
                discovery,
                selection,
                new ProjectGraphEvaluationOptions(
                    configuration: "Release",
                    framework: "net9.0",
                    properties: [platform])));

        Assert.Equal(
            "Debug",
            Assert.Single(accepted.Projects).Configuration);
        var declaration = Assert.Single(error.Declarations);
        Assert.Equal("src/Mapped/Mapped.csproj", declaration.Project);
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
        var sdkVersion = Version.Parse(Assert.IsType<string>(graph.Runtime?.SdkVersion));
        Assert.Equal(10, sdkVersion.Major);
        Assert.Equal(0, sdkVersion.Minor);
        Assert.InRange(sdkVersion.Build, 302, 399);
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
    public async Task Explicit_framework_preserves_solution_member_symlink_escape_failure()
    {
        await using var fixture = await EvaluationFixtureAsync();
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

        await File.WriteAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "Escape.slnx"),
            """
            <Solution>
              <Project Path="src/App/external-link/External.csproj" />
            </Solution>
            """);
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(solution: "Escape.slnx"));

        var graph = _evaluator.Evaluate(
            discovery,
            selection,
            new ProjectGraphEvaluationOptions(framework: "net10.0"));

        Assert.Equal(ProjectGraphCompleteness.Failed, graph.Completeness);
        Assert.Contains(
            graph.Failures,
            static failure =>
                failure.Reason
                    is ProjectEvaluationFailureReason.WorkspacePathEscape
                && failure.AuthorityCode == "workspace.project_link_escape");
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
            new DotNetHostResolver(),
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

    [Theory]
    [InlineData("8.0.408")]
    [InlineData("9.0.308")]
    [InlineData("10.0.302")]
    public void Sdk_selector_uses_the_exact_host_selected_sdk_context(
        string selectedVersion)
    {
        var sdkBase = Path.Combine(Path.GetTempPath(), "dotnet-sdk inventory");
        var sdkPath = Path.GetFullPath(Path.Combine(sdkBase, selectedVersion));
        var resolver = new StubDotNetHostResolver(new DotNetHostResolution(
            Path.Combine(sdkBase, "dotnet"),
            new SelectedDotNetSdk(
                selectedVersion,
                sdkPath,
                Path.Combine(sdkPath, "Microsoft.Build.dll"),
                DotNetHostCompatibility.Supported),
            null));
        var selector = new DotNetSdkSelector(resolver);
        var workspace = Path.Combine(Path.GetTempPath(), "sdk-selection-workspace");

        var selection = selector.Select(workspace, CancellationToken.None);

        Assert.True(selection.IsSelected);
        Assert.Equal(selectedVersion, selection.Sdk?.Version);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(sdkBase, selectedVersion)),
            selection.Sdk?.SdkPath);
        Assert.Equal(workspace, resolver.Request?.WorkspaceRoot);
    }

    [Fact]
    public async Task Sdk_resolution_cancellation_propagates_through_graph_with_the_caller_token()
    {
        await using var fixture = await EvaluationFixtureAsync();
        var discovery = _discoverer.Discover(fixture.WorkspacePath);
        var selection = _selector.Select(
            discovery,
            new WorkspaceSelectionRequest(project: "App"));
        using var cancellation = new CancellationTokenSource();
        var resolver = new CancellingDotNetHostResolver();
        var authority = new MsBuildRuntimeAuthority(
            new DotNetSdkSelector(resolver),
            new StubMsBuildRuntimeRegistrar(
                isRegistered: false,
                loadedMsBuildPath: null));
        var evaluator = new MsBuildProjectGraphEvaluator(authority);

        var exception = Assert.ThrowsAny<OperationCanceledException>(() =>
            evaluator.Evaluate(
                discovery,
                selection,
                cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(cancellation.Token, resolver.CancellationToken);
    }

    [Fact]
    public void Mismatched_msbuild_assembly_is_a_typed_authority_failure()
    {
        var sdkPath = Path.Combine(Path.GetTempPath(), "selected-sdk", "10.0.302");
        var resolver = new StubDotNetHostResolver(new DotNetHostResolution(
            Path.Combine(Path.GetTempPath(), "dotnet"),
            new SelectedDotNetSdk(
                "10.0.302",
                sdkPath,
                typeof(MsBuildProjectGraphEvaluatorTests).Assembly.Location,
                DotNetHostCompatibility.Supported),
            null));
        var authority = new MsBuildRuntimeAuthority(
            new DotNetSdkSelector(resolver),
            new StubMsBuildRuntimeRegistrar(
                isRegistered: false,
                loadedMsBuildPath: null));

        var result = authority.ResolveAndRegister(
            Path.GetTempPath(),
            CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(
            new ProjectEvaluationFailure(
                ProjectEvaluationFailureReason.MsBuildIncompatible,
                "msbuild.contract_mismatch"),
            result.Failure);
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

    private static string PropertyValue(
        EvaluatedProjectGraph graph,
        string name) =>
        Assert.Single(
            graph.GlobalProperties,
            property => property.Name.Equals(
                name,
                StringComparison.OrdinalIgnoreCase)).Value;

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

    private sealed class StubDotNetHostResolver(DotNetHostResolution result)
        : IDotNetHostResolver
    {
        public DotNetHostResolutionRequest? Request { get; private set; }

        public ValueTask<DotNetHostResolution> ResolveAsync(
            DotNetHostResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancellingDotNetHostResolver : IDotNetHostResolver
    {
        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<DotNetHostResolution> ResolveAsync(
            DotNetHostResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            return ValueTask.FromException<DotNetHostResolution>(
                new OperationCanceledException(
                    "Fixture cancellation.",
                    cancellationToken));
        }
    }

}
