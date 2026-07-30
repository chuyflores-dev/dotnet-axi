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
        var markerPath = Path.Combine(rootPath, OwnerMarkerName);
        if (!TryGetAttributes(rootPath, out _))
        {
            return;
        }

        await ValidateOwnedRootAsync(rootPath, markerPath, ownerId);

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await ValidateOwnedRootAsync(rootPath, markerPath, ownerId);
                DeleteContents(rootPath, markerPath);
                await ValidateOwnedRootAsync(rootPath, markerPath, ownerId);
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

    private static async ValueTask ValidateOwnedRootAsync(
        string rootPath,
        string markerPath,
        string ownerId)
    {
        if (!TryGetAttributes(rootPath, out var rootAttributes)
            || (rootAttributes & FileAttributes.Directory) == 0
            || IsReparsePoint(rootAttributes))
        {
            throw OwnershipError(
                rootPath,
                "The fixture root is missing, is not a directory, or was replaced by a symbolic link.");
        }

        if (!TryGetAttributes(markerPath, out var markerAttributes)
            || (markerAttributes & FileAttributes.Directory) != 0
            || IsReparsePoint(markerAttributes))
        {
            throw OwnershipError(
                rootPath,
                "The fixture ownership marker is missing, is not a regular file, or was replaced by a symbolic link.");
        }

        string actualOwner;
        try
        {
            actualOwner = await File.ReadAllTextAsync(markerPath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new FixtureCleanupException(rootPath, exception);
        }

        if (!TryGetAttributes(rootPath, out rootAttributes)
            || (rootAttributes & FileAttributes.Directory) == 0
            || IsReparsePoint(rootAttributes)
            || !TryGetAttributes(markerPath, out markerAttributes)
            || (markerAttributes & FileAttributes.Directory) != 0
            || IsReparsePoint(markerAttributes)
            || !string.Equals(actualOwner, ownerId, StringComparison.Ordinal))
        {
            throw OwnershipError(
                rootPath,
                "The directory is not owned by this fixture instance or its identity changed during cleanup.");
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

    private static bool IsReparsePoint(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) != 0;

    private static FixtureCleanupException OwnershipError(
        string rootPath,
        string message) =>
        new(
            rootPath,
            new InvalidOperationException(message));

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
                if (IsReparsePoint(attributes))
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
