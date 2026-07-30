namespace DotNetAxi.Testing;

internal interface IFixtureDirectoryCleaner
{
    ValueTask DeleteAsync(string rootPath, string ownerId);
}

internal sealed class FixtureDirectoryCleaner : IFixtureDirectoryCleaner
{
    internal const string OwnerMarkerName = ".dotnet-axi-fixture-owner";

    public async ValueTask DeleteAsync(string rootPath, string ownerId)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        var markerPath = Path.Combine(rootPath, OwnerMarkerName);
        if (!File.Exists(markerPath)
            || !string.Equals(
                await File.ReadAllTextAsync(markerPath),
                ownerId,
                StringComparison.Ordinal))
        {
            throw new FixtureCleanupException(
                rootPath,
                new InvalidOperationException(
                    "The directory is not owned by this fixture instance."));
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                DeleteContents(rootPath, markerPath);
                File.SetAttributes(markerPath, FileAttributes.Normal);
                File.Delete(markerPath);
                Directory.Delete(rootPath);
                return;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt));
                }
            }
        }

        throw new FixtureCleanupException(
            rootPath,
            lastException
            ?? new IOException("Fixture cleanup failed without an exception."));
    }

    private static void DeleteContents(string directory, string markerPath)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (string.Equals(path, markerPath, StringComparison.Ordinal))
            {
                continue;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(path);
                    continue;
                }

                DeleteContents(path, markerPath);
                File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(path);
                continue;
            }

            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
