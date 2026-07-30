using System.Diagnostics;

namespace DotNetAxi.Testing.Tests;

public sealed class FixtureCatalogTests
{
    private static readonly string[] RequiredCapabilities =
    [
        "analyzer:diagnostic",
        "framework:multi-targeting",
        "framework:net10.0",
        "generator:source",
        "language:csharp",
        "project-reference:cycle",
        "project:project-reference",
        "project:sdk-style",
        "solution:sln",
        "solution:slnx",
        "source:conditional-compilation",
        "source:generated",
        "source:linked-file",
        "test-runner:mtp",
        "test-runner:vstest",
        "workspace:multi-project",
        "workspace:single-project",
    ];

    public static TheoryData<string> CatalogManifests()
    {
        var manifests = new TheoryData<string>();
        foreach (var path in Directory
                     .EnumerateFiles(
                         CatalogRoot(),
                         "fixture.json",
                         SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            manifests.Add(path);
        }

        return manifests;
    }

    [Fact]
    public async Task Catalog_declares_every_required_capability()
    {
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var factory = new RepositoryFixtureFactory();

        foreach (var manifestPath in CatalogManifestPaths())
        {
            await using var fixture = await factory.CreateAsync(manifestPath);

            Assert.NotEmpty(fixture.Capabilities);
            Assert.NotNull(fixture.BuildVerification);
            Assert.Equal("10.0.302", fixture.Identity.SelectedSdk.Version);
            capabilities.UnionWith(fixture.Capabilities);
        }

        Assert.Empty(
            RequiredCapabilities.Except(
                capabilities,
                StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CatalogManifests))]
    public async Task Fixture_build_matches_manifest(string manifestPath)
    {
        var options = new RepositoryFixtureOptions(
            FixtureExecutionPermissions.Restore
            | FixtureExecutionPermissions.RepositoryCode);
        var factory = new RepositoryFixtureFactory();
        await using var fixture = await factory.CreateAsync(
            manifestPath,
            options);
        var verification = fixture.BuildVerification
            ?? throw new InvalidOperationException(
                "Catalog fixture does not declare build verification.");
        var startInfo = fixture.CreateProcessStartInfo(
            FixtureProcessKind.Restore
            | FixtureProcessKind.RepositoryCode,
            fixture.DotNetHostPath,
            "build",
            verification.Target,
            "--configuration",
            "Release",
            "--verbosity",
            "minimal",
            "--nologo",
            "--disable-build-servers",
            "--artifacts-path",
            Path.Combine(fixture.ArtifactsPath, "build"));
        using var process = new Process
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start(), "The fixture build process did not start.");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new TimeoutException(
                $"Fixture '{fixture.Identity.Name}' build exceeded three minutes.");
        }

        var output = string.Join(
            Environment.NewLine,
            await standardOutput,
            await standardError);
        var actualOutcome = process.ExitCode == 0
            ? FixtureBuildOutcome.Success
            : FixtureBuildOutcome.Failure;

        Assert.True(
            actualOutcome == verification.ExpectedOutcome,
            $"""
             Fixture '{fixture.Identity.Name}' expected build outcome
             '{verification.ExpectedOutcome}' but exited {process.ExitCode}.

             {output}
             """);
        foreach (var requiredOutput in verification.RequiredOutput)
        {
            Assert.Contains(
                requiredOutput,
                output,
                StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string> CatalogManifestPaths() =>
        Directory
            .EnumerateFiles(
                CatalogRoot(),
                "fixture.json",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string CatalogRoot() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Catalog");
}
