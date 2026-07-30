namespace DotNetAxi.Testing;

public sealed record FixtureSdkContext(
    string Version,
    string RollForward,
    bool AllowPrerelease);

public sealed record RepositoryFixtureIdentity(
    string Name,
    int Seed,
    FixtureSdkContext SelectedSdk);

public sealed record FixtureToolchainIdentity(
    string Framework,
    string RuntimeIdentifier,
    string ProcessArchitecture,
    string OperatingSystem,
    FixtureSdkContext SelectedSdk);
