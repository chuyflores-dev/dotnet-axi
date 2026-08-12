using DotNetAxi.Contracts;
using DotNetAxi.Roslyn;
using DotNetAxi.Structural;
using DotNetAxi.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNetAxi.Cli.Tests;

public sealed class SemanticSyntaxVerificationCommandTests
{
    [Fact]
    public async Task Verify_accepts_each_declared_syntax_verifier()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await workspace.WriteAsync(
            "Code.cs",
            """
            using System;
            sealed class MarkerAttribute : Attribute { }
            sealed class Widget { }
            [Marker]
            sealed class C
            {
                static void Target() { }
                static void Run()
                {
                    Target();
                    _ = new Widget();
                    try { } catch (Exception) { }
                }
            }
            """);
        string[][] cases =
        [
            ["invocation", "--name", "Target"],
            ["class", "--attribute", "Marker"],
            ["object-creation", "--type", "Widget"],
            ["catch", "--type", "Exception"],
        ];

        foreach (var syntaxCase in cases)
        {
            var result = await workspace.RunAsync(
                ["search", "syntax", .. syntaxCase, "--verify", "--full"]);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("status: success", result.Output);
            Assert.Contains("classification: executing", result.Output);
            Assert.Contains("resolution: semantic", result.Output);
            Assert.Contains("coverage: complete", result.Output);
            Assert.Contains("confidence: verified", result.Output);
            Assert.Contains("discovered: 1", result.Output);
            Assert.Contains("verified: 1", result.Output);
            Assert.Contains("rejected: 0", result.Output);
            Assert.Contains("unresolved: 0", result.Output);
            Assert.Contains("status: verified", result.Output);
            Assert.Contains("net10.0", result.Output);
        }
    }

    [Fact]
    public async Task Verify_preserves_partial_framework_coverage_and_reasons()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System.Private.CoreLib">
                  <HintPath>{System.Security.SecurityElement.Escape(typeof(object).Assembly.Location)}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);
        await workspace.WriteAsync(
            "Code.cs",
            """
            sealed class C
            {
            #if NET8_0
                static void Target() { }
            #endif
                static void Run() => Target();
            }
            """);

        var result = await workspace.RunAsync(
            "search", "syntax", "invocation", "--name", "Target",
            "--verify", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: partial", result.Output);
        Assert.Contains("resolution: semantic", result.Output);
        Assert.Contains("coverage: partial", result.Output);
        Assert.Contains("partial_reason:", result.Output);
        Assert.Contains("semantic.unresolved", result.Output);
        Assert.Contains("discovered: 1", result.Output);
        Assert.Contains("verified: 1", result.Output);
        Assert.Contains("rejected: 0", result.Output);
        Assert.Contains("unresolved: 0", result.Output);
        Assert.Contains("net10.0", result.Output);
        Assert.Contains("net8.0", result.Output);
        Assert.Contains("semantic.unresolved", result.Output);
        Assert.Contains("unresolved", result.Output);
        Assert.Contains("verified", result.Output);
    }

    [Fact]
    public async Task Verify_partial_output_retains_the_normalized_path_selector()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "loose/UnownedCandidate.cs",
            "sealed class C { static void Run() => MissingAudit(); }");

        var result = await workspace.RunAsync(
            "search", "syntax", "invocation", "--name", "MissingAudit",
            "--path", "./loose/../loose/UnownedCandidate.cs",
            "--verify", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: partial", result.Output);
        Assert.Contains("resolution: semantic", result.Output);
        Assert.Contains(
            "paths[1]: loose/UnownedCandidate.cs",
            result.Output);
        Assert.Contains("include_tests: false", result.Output);
        Assert.Contains("include_generated: false", result.Output);
        Assert.Contains("considered: 1", result.Output);
        Assert.Contains("partial_reason: ownership.not_found", result.Output);
    }

    [Fact]
    public async Task Verify_without_an_owner_is_partial_instead_of_inventing_meaning()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "Code.cs",
            "class C { static void Run() => Missing(); }");

        var result = await workspace.RunAsync(
            "search", "syntax", "invocation", "--name", "Missing",
            "--verify", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: partial", result.Output);
        Assert.Contains("unresolved: 1", result.Output);
        Assert.Contains("ownership.not_found", result.Output);
    }

    [Fact]
    public async Task Verify_owned_explicit_path_returns_semantic_evidence()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await workspace.WriteAsync(
            "Code.cs",
            """
            static class C
            {
                static void Target() { }
                static void Run() => Target();
            }
            """);

        var result = await workspace.RunAsync(
            "search", "syntax", "invocation", "--name", "Target",
            "--path", "Code.cs", "--verify", "--limit", "5");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("internal.unhandled", result.Output);
        Assert.Contains("status: success", result.Output);
        Assert.Contains("classification: executing", result.Output);
        Assert.Contains("resolution: semantic", result.Output);
        Assert.Contains("coverage: complete", result.Output);
        Assert.Contains("discovered: 1", result.Output);
        Assert.Contains("verified: 1", result.Output);
        Assert.Contains("Code.cs", result.Output);
        Assert.Contains("App.csproj", result.Output);
        Assert.Contains("net10.0", result.Output);
    }

    [Fact]
    public async Task Verify_project_load_timeout_returns_partial_scoped_evidence()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await workspace.WriteAsync(
            "Code.cs",
            """
            static class C
            {
                static void Target() { }
                static void Run() => Target();
            }
            """);

        var result = await ExecuteWithProjectLoaderAsync(
            workspace,
            static (_, _, _) => Task.FromException<Project>(
                new TimeoutException("Injected project-load timeout.")));
        var output = new StringWriter();
        var exitCode = await new CommandResponseWriter(output)
            .WriteAsync(result);

        Assert.Equal(0, exitCode);
        Assert.Equal(ResultStatus.Partial, result.Status);
        Assert.Equal(CoverageLevel.Partial, result.Evidence!.Coverage.Level);
        Assert.Equal("project.load_failed", result.Evidence.Coverage.PartialReason);
        Assert.Equal(["App.csproj"], result.Evidence.Scope.Projects);
        Assert.Equal(["net10.0"], result.Evidence.Scope.Frameworks);
        Assert.Contains(
            "variants[1]{configuration,framework,project,reason,status,symbol}:",
            output.ToString());
        Assert.Contains(
            "Debug,net10.0,App.csproj,project.load_failed,unresolved,null",
            output.ToString());
    }

    [Fact]
    public async Task Verify_project_load_cancellation_propagates()
    {
        using var workspace = new TestWorkspace();
        await workspace.WriteAsync(
            "App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await workspace.WriteAsync(
            "Code.cs",
            "static class C { static void Run() => Target(); }");
        var expected = new OperationCanceledException(
            "Injected project-load cancellation.");

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ExecuteWithProjectLoaderAsync(
                workspace,
                (_, _, _) => Task.FromException<Project>(expected)));

        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData("invocation", "--name", "Hit")]
    [InlineData("class", "--attribute", "Marker")]
    [InlineData("object-creation", "--type", "Widget")]
    [InlineData("catch", "--type", "Exception")]
    public async Task Help_declares_semantic_verification(
        string kind,
        string option,
        string value)
    {
        var output = new StringWriter();
        var host = CliApplication.Create(output, new StringWriter());

        var exit = await host.InvokeAsync(
            ["search", "syntax", kind, option, value, "--help"]);

        Assert.Equal(0, exit);
        Assert.Contains("--verify", output.ToString());
        Assert.Contains("compiler semantics", output.ToString());
        Assert.Contains("executes repository", output.ToString());
    }

    [Fact]
    public void Verify_changes_the_registered_operation_classification()
    {
        var host = CliApplication.Create(new StringWriter(), new StringWriter());

        var passive = host.Parse(
            ["search", "syntax", "invocation", "--name", "Hit"]);
        var executing = host.Parse(
            ["search", "syntax", "invocation", "--name", "Hit", "--verify"]);

        Assert.Equal(
            DotNetAxi.Contracts.OperationClassification.Passive,
            host.ResolvePolicy(passive).Classification);
        Assert.Equal(
            DotNetAxi.Contracts.OperationClassification.Executing,
            host.ResolvePolicy(executing).Classification);
    }

    private static async Task<ICommandResult> ExecuteWithProjectLoaderAsync(
        TestWorkspace workspace,
        Func<MSBuildWorkspace, string, CancellationToken, Task<Project>>
            projectLoader)
    {
        var discovery = new WorkspaceDiscoverer().Discover(workspace.Root);
        var projects = discovery.Projects
            .Select(static project => project.Path)
            .ToArray();
        var query = new InvocationSyntaxQuery("Target");
        var syntax = await new RoslynSyntaxEngine(new WorkspacePathTraverser())
            .QueryAsync(
                new RoslynSyntaxQueryRequest(
                    new WorkspaceTraversalRequest(workspace.Root)),
                query);
        var verifier = new RoslynSemanticCandidateVerifier(
            new WorkspaceProjectOwnershipResolver(workspace.Root, projects),
            projects,
            projectLoader);
        var fields = new OutputFieldSet<StructuralCandidate>(
            [
                new(
                    "file",
                    static candidate => candidate.Range.Start.Path,
                    includedByDefault: true),
            ])
            .Select();

        return await SemanticSyntaxVerificationCommand.ExecuteAsync(
            "search syntax invocation",
            discovery,
            syntax,
            query,
            fields,
            full: true,
            limit: 5,
            "dnaxi search syntax invocation --name Target --verify --full",
            paths: [],
            includeGenerated: false,
            verifier,
            CancellationToken.None);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-axi-semantic-cli-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public async Task WriteAsync(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        public async Task<(int ExitCode, string Output)> RunAsync(
            params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                    ?? "dotnet",
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(typeof(Cli.Program).Assembly.Location);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                string.IsNullOrEmpty(error),
                $"Expected empty stderr, got: {error}");
            return (process.ExitCode, output);
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
