namespace DotNetAxi.Testing;

public enum FixtureBuildOutcome
{
    Success,
    Failure,
}

public enum FixtureCoverageExpectation
{
    Complete,
    Partial,
    None,
}

public sealed record FixtureBuildVerification(
    string Target,
    FixtureBuildOutcome ExpectedOutcome,
    IReadOnlyList<string> RequiredOutput);

public sealed record FixtureScenario(
    string State,
    bool IntentionalFailure,
    FixtureCoverageExpectation ExpectedCoverage,
    IReadOnlyList<string> RemainingCoverage,
    string Reason);

internal enum FixtureGitChangeKind
{
    Staged,
    Unstaged,
    Untracked,
    Renamed,
    Deleted,
}

internal sealed record FixtureGitChangePlan(
    FixtureGitChangeKind Kind,
    string Path,
    string? NewPath,
    byte[]? Content);

internal sealed record FixtureGitConflictPlan(
    string Path,
    byte[] OursContent,
    byte[] TheirsContent);

internal sealed record FixtureGitPlan(
    IReadOnlyList<FixtureGitChangePlan> Changes,
    FixtureGitConflictPlan? Conflict);
