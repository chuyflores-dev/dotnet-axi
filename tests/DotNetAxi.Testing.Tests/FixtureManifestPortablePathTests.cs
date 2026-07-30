using System.Text.Json;

namespace DotNetAxi.Testing.Tests;

public sealed class FixtureManifestPortablePathTests
{
    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("src/PrN.config")]
    [InlineData("AUX")]
    [InlineData("nul.json")]
    [InlineData("CLOCK$")]
    [InlineData("clock$.log")]
    [InlineData("COM1.cs")]
    [InlineData("src/com9")]
    [InlineData("COM¹.cs")]
    [InlineData("src/com²")]
    [InlineData("LPT1.txt")]
    [InlineData("src/lPt9.log")]
    [InlineData("LPT³.log")]
    [InlineData("src/file.")]
    [InlineData("src/file ")]
    [InlineData("src/cafe\u0301.cs")]
    public async Task Non_portable_destination_path_is_rejected(string path)
    {
        var testRoot = CreateTestDirectory();

        try
        {
            var manifestPath = await WriteManifestAsync(testRoot, path);

            await Assert.ThrowsAsync<FixtureManifestException>(
                () => FixtureManifestLoader
                    .LoadAsync(manifestPath, CancellationToken.None)
                    .AsTask());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("COM0.cs")]
    [InlineData("COM10.cs")]
    [InlineData("LPT0.txt")]
    [InlineData("LPT10.txt")]
    [InlineData("CONSOLE.cs")]
    [InlineData("src/café.cs")]
    public async Task Similar_portable_destination_path_is_accepted(string path)
    {
        var testRoot = CreateTestDirectory();

        try
        {
            var manifestPath = await WriteManifestAsync(testRoot, path);

            var plan = await FixtureManifestLoader.LoadAsync(
                manifestPath,
                CancellationToken.None);

            Assert.Contains(
                plan.Files,
                file => string.Equals(
                    file.RelativePath,
                    path,
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async ValueTask<string> WriteManifestAsync(
        string testRoot,
        string destinationPath)
    {
        var templatePath = Path.Combine(testRoot, "template.txt");
        await File.WriteAllTextAsync(templatePath, "fixture");
        var manifestPath = Path.Combine(testRoot, "fixture.json");
        var document = new
        {
            schema = "dotnet-axi/fixture/v1",
            name = "portable-path-test",
            seed = 1,
            sdk = new
            {
                version = "10.0.302",
                rollForward = "latestPatch",
                allowPrerelease = false,
            },
            files = new[]
            {
                new
                {
                    path = destinationPath,
                    template = "template.txt",
                    expandTokens = false,
                },
            },
        };
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(document));
        return manifestPath;
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-manifest-path-tests",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
