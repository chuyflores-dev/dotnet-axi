namespace DotNetAxi.Testing;

[Flags]
public enum FixtureExecutionPermissions
{
    None = 0,
    Tooling = 1,
    Restore = 2,
    RepositoryCode = 4,
}

[Flags]
public enum FixtureProcessKind
{
    None = 0,
    Tooling = 1,
    Restore = 2,
    RepositoryCode = 4,
}

public sealed record RepositoryFixtureOptions(
    FixtureExecutionPermissions ExecutionPermissions =
        FixtureExecutionPermissions.None);
