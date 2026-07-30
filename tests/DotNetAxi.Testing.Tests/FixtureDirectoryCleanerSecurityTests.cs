namespace DotNetAxi.Testing.Tests;

public sealed class FixtureDirectoryCleanerSecurityTests
{
    [Fact]
    public async Task Root_symbolic_link_substitution_is_rejected()
    {
        var testRoot = CreateTestDirectory();
        var fixtureRoot = Path.Combine(testRoot, "fixture");
        var originalRoot = Path.Combine(testRoot, "fixture-original");
        var victimRoot = Path.Combine(testRoot, "victim");
        var ownerId = Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(fixtureRoot);
            await WriteOwnerMarkerAsync(fixtureRoot, ownerId);
            await File.WriteAllTextAsync(
                Path.Combine(fixtureRoot, "fixture.txt"),
                "fixture");
            Directory.CreateDirectory(victimRoot);
            await WriteOwnerMarkerAsync(victimRoot, ownerId);
            var victimFile = Path.Combine(victimRoot, "preserve.txt");
            await File.WriteAllTextAsync(victimFile, "preserve");

            Directory.Move(fixtureRoot, originalRoot);
            if (!TryCreateDirectorySymbolicLink(fixtureRoot, victimRoot))
            {
                return;
            }

            var cleaner = new FixtureDirectoryCleaner();

            var exception = await Assert.ThrowsAsync<FixtureCleanupException>(
                () => cleaner.DeleteAsync(fixtureRoot, ownerId).AsTask());

            Assert.Equal(fixtureRoot, exception.RootPath);
            Assert.True(File.Exists(victimFile));
            Assert.Equal(
                ownerId,
                await File.ReadAllTextAsync(
                    Path.Combine(
                        victimRoot,
                        FixtureDirectoryCleaner.OwnerMarkerName)));
        }
        finally
        {
            DeleteSymbolicLinkIfPresent(fixtureRoot);
            DeleteDirectoryIfPresent(originalRoot);
            DeleteDirectoryIfPresent(victimRoot);
            DeleteDirectoryIfPresent(testRoot);
        }
    }

    [Fact]
    public async Task Ownership_marker_symbolic_link_substitution_is_rejected()
    {
        var testRoot = CreateTestDirectory();
        var fixtureRoot = Path.Combine(testRoot, "fixture");
        var externalMarker = Path.Combine(testRoot, "external-owner");
        var ownerId = Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(fixtureRoot);
            var fixtureFile = Path.Combine(fixtureRoot, "preserve.txt");
            await File.WriteAllTextAsync(fixtureFile, "preserve");
            await File.WriteAllTextAsync(externalMarker, ownerId);
            var markerPath = Path.Combine(
                fixtureRoot,
                FixtureDirectoryCleaner.OwnerMarkerName);
            if (!TryCreateFileSymbolicLink(markerPath, externalMarker))
            {
                return;
            }

            var cleaner = new FixtureDirectoryCleaner();

            var exception = await Assert.ThrowsAsync<FixtureCleanupException>(
                () => cleaner.DeleteAsync(fixtureRoot, ownerId).AsTask());

            Assert.Equal(fixtureRoot, exception.RootPath);
            Assert.True(File.Exists(fixtureFile));
            Assert.Equal(ownerId, await File.ReadAllTextAsync(externalMarker));
        }
        finally
        {
            DeleteDirectoryIfPresent(fixtureRoot);
            if (File.Exists(externalMarker))
            {
                File.Delete(externalMarker);
            }

            DeleteDirectoryIfPresent(testRoot);
        }
    }

    private static async ValueTask WriteOwnerMarkerAsync(
        string directory,
        string ownerId) =>
        await File.WriteAllTextAsync(
            Path.Combine(
                directory,
                FixtureDirectoryCleaner.OwnerMarkerName),
            ownerId);

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

    private static bool TryCreateFileSymbolicLink(
        string path,
        string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
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

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-axi-cleaner-security-tests",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteSymbolicLinkIfPresent(string path)
    {
        if (TryGetAttributes(path, out var attributes)
            && (attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path);
        }
    }

    private static bool TryGetAttributes(
        string path,
        out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception)
            when (exception is FileNotFoundException
                or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
