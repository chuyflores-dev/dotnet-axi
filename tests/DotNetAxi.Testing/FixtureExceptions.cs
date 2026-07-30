namespace DotNetAxi.Testing;

public sealed class FixtureManifestException : Exception
{
    public FixtureManifestException(string message)
        : base(message)
    {
    }

    public FixtureManifestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class FixtureCleanupException : IOException
{
    public FixtureCleanupException(string rootPath, Exception innerException)
        : base($"Could not clean fixture directory '{rootPath}'.", innerException)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }
}
