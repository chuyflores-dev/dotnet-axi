using DotNetAxi.Contracts;

namespace DotNetAxi.Contracts.Tests;

public sealed class CommandResultTests
{
    [Fact]
    public void Success_expresses_the_normal_envelope()
    {
        var payload = new CountPayload(0);

        var result = CommandResult<CountPayload>.Success(
            "search symbol",
            payload,
            suggestions:
            [
                new ResultSuggestion(
                    "dnaxi",
                    ["search", "file", "*.cs"]),
            ]);

        Assert.Equal(OutputSchema.Current, result.Schema);
        Assert.Equal("search symbol", result.Command);
        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Same(payload, result.Payload);
        Assert.Null(result.Evidence);
        Assert.Empty(result.Errors);
        Assert.Single(result.Suggestions);
    }

    [Fact]
    public void Partial_expresses_the_complete_evidence_envelope()
    {
        var evidence = CreateEvidence(
            new EvidenceCoverage(
                CoverageLevel.Partial,
                considered: 8,
                analyzed: 6,
                remaining: 2,
                partialReason: "Two projects require restore."));

        var result = CommandResult<CountPayload>.Partial(
            "search callers",
            new CountPayload(2),
            evidence,
            errors:
            [
                new ResultError(
                    "analysis.incomplete",
                    "Two projects were not analyzed.",
                    "Run `dnaxi restore`, then repeat with `--complete`.")
            ]);

        Assert.Equal(ResultStatus.Partial, result.Status);
        Assert.Same(evidence, result.Evidence);
        Assert.Equal("ws_123", evidence.Snapshot);
        Assert.Equal(EvidenceResolution.Semantic, evidence.Resolution);
        Assert.Equal(EvidenceConfidence.Verified, evidence.Confidence);
        Assert.Equal(CoverageLevel.Partial, evidence.Coverage.Level);
        Assert.Equal(2, evidence.Coverage.Remaining);
        Assert.Equal("Two projects require restore.", evidence.Coverage.PartialReason);
        Assert.Equal(
            ["src/Api/Api.csproj", "src/Core/Core.csproj"],
            evidence.Scope.Projects);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Failed_requires_and_expresses_errors()
    {
        var error = new ResultError(
            "workspace.not_found",
            "No .NET workspace was found.",
            "Run the command inside a repository containing a solution or project.");

        var result = CommandResult<CountPayload>.Failed(
            "search symbol",
            [error]);

        Assert.Equal(ResultStatus.Failed, result.Status);
        Assert.Null(result.Payload);
        Assert.Same(error, Assert.Single(result.Errors));
    }

    [Fact]
    public void Cancelled_is_a_distinct_result_status()
    {
        var result = CommandResult<CountPayload>.Cancelled(
            "validate",
            errors:
            [
                new ResultError(
                    "operation.cancelled",
                    "Validation was cancelled.",
                    "Run the command again when ready.")
            ]);

        Assert.Equal(ResultStatus.Cancelled, result.Status);
        Assert.Null(result.Payload);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Complete_evidence_without_scope_is_rejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new Evidence(
                "ws_123",
                EvidenceResolution.Syntax,
                new EvidenceCoverage(CoverageLevel.Complete),
                EvidenceConfidence.Verified,
                scope: null!));

        Assert.Equal("scope", exception.ParamName);
    }

    [Fact]
    public void Partial_coverage_without_reason_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new EvidenceCoverage(CoverageLevel.Partial));

        Assert.Equal("partialReason", exception.ParamName);
    }

    [Fact]
    public void Complete_coverage_with_remaining_targets_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new EvidenceCoverage(
                CoverageLevel.Complete,
                considered: 2,
                analyzed: 1,
                remaining: 1));

        Assert.Equal("level", exception.ParamName);
    }

    [Fact]
    public void Coverage_partitions_cannot_exceed_considered_targets()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new EvidenceCoverage(
                CoverageLevel.Partial,
                considered: 1,
                analyzed: 2,
                partialReason: "One target was unavailable."));

        Assert.Equal("considered", exception.ParamName);
    }

    [Fact]
    public void Fully_declared_coverage_counts_must_partition_considered_targets()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new EvidenceCoverage(
                CoverageLevel.Partial,
                considered: 3,
                analyzed: 1,
                remaining: 1,
                excluded: 0,
                failed: 0,
                partialReason: "One target was unavailable."));

        Assert.Equal("considered", exception.ParamName);
    }

    [Fact]
    public void Undefined_evidence_enums_are_rejected_at_construction()
    {
        Assert.Equal(
            "resolution",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Evidence(
                    "ws_123",
                    (EvidenceResolution)int.MaxValue,
                    new EvidenceCoverage(CoverageLevel.Complete),
                    EvidenceConfidence.Verified,
                    Scope()))
                .ParamName);
        Assert.Equal(
            "confidence",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Evidence(
                    "ws_123",
                    EvidenceResolution.Semantic,
                    new EvidenceCoverage(CoverageLevel.Complete),
                    (EvidenceConfidence)int.MaxValue,
                    Scope()))
                .ParamName);
        Assert.Equal(
            "level",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EvidenceCoverage(
                    (CoverageLevel)int.MaxValue))
                .ParamName);
    }

    [Fact]
    public void Failed_result_without_errors_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CommandResult<CountPayload>.Failed(
                "validate",
                errors: []));

        Assert.Equal("errors", exception.ParamName);
    }

    private static Evidence CreateEvidence(EvidenceCoverage coverage) =>
        new(
            "ws_123",
            EvidenceResolution.Semantic,
            coverage,
            EvidenceConfidence.Verified,
            new EvidenceScope(
                "/work/repository",
                "Selected project graph",
                solution: "Repository.slnx",
                projects:
                [
                    "src/Core/Core.csproj",
                    "src/Api/Api.csproj",
                ],
                frameworks: ["net10.0"],
                configuration: "Debug"));

    private static EvidenceScope Scope() =>
        new("/work/repository", "Selected project graph");

    private sealed record CountPayload(int Count);
}
