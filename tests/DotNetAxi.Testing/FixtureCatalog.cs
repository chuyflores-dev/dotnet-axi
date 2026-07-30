namespace DotNetAxi.Testing;

public enum FixtureBuildOutcome
{
    Success,
    Failure,
}

public sealed record FixtureBuildVerification(
    string Target,
    FixtureBuildOutcome ExpectedOutcome,
    IReadOnlyList<string> RequiredOutput);
