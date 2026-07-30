namespace DotNetAxi.Contracts;

public enum EvidenceResolution
{
    Text,
    Syntax,
    Semantic,
}

public enum CoverageLevel
{
    NotApplicable,
    Partial,
    Complete,
}

public enum EvidenceConfidence
{
    Candidate,
    Verified,
    Heuristic,
    Unknown,
}

public sealed record Evidence
{
    public Evidence(
        string snapshot,
        EvidenceResolution resolution,
        EvidenceCoverage coverage,
        EvidenceConfidence confidence,
        EvidenceScope scope)
    {
        Snapshot = ContractGuards.RequiredText(snapshot, nameof(snapshot));
        Resolution = resolution;
        Coverage = coverage
            ?? throw new ArgumentNullException(nameof(coverage));
        Confidence = confidence;
        Scope = scope
            ?? throw new ArgumentNullException(
                nameof(scope),
                "Evidence, including complete coverage, requires a declared scope.");
    }

    public string Snapshot { get; }

    public EvidenceResolution Resolution { get; }

    public EvidenceCoverage Coverage { get; }

    public EvidenceConfidence Confidence { get; }

    public EvidenceScope Scope { get; }
}

public sealed record EvidenceScope
{
    public EvidenceScope(
        string workspaceRoot,
        string analyzedPortion,
        string? solution = null,
        IEnumerable<string>? projects = null,
        IEnumerable<string>? frameworks = null,
        string? configuration = null)
    {
        WorkspaceRoot = ContractGuards.RequiredText(
            workspaceRoot,
            nameof(workspaceRoot));
        AnalyzedPortion = ContractGuards.RequiredText(
            analyzedPortion,
            nameof(analyzedPortion));
        Solution = ContractGuards.OptionalText(solution, nameof(solution));
        Projects = ContractGuards.CopyText(projects, nameof(projects));
        Frameworks = ContractGuards.CopyText(frameworks, nameof(frameworks));
        Configuration = ContractGuards.OptionalText(
            configuration,
            nameof(configuration));
    }

    public string WorkspaceRoot { get; }

    public string AnalyzedPortion { get; }

    public string? Solution { get; }

    public IReadOnlyList<string> Projects { get; }

    public IReadOnlyList<string> Frameworks { get; }

    public string? Configuration { get; }
}

public sealed record EvidenceCoverage
{
    public EvidenceCoverage(
        CoverageLevel level,
        int? considered = null,
        int? analyzed = null,
        int? remaining = null,
        int? excluded = null,
        int? failed = null,
        string? partialReason = null)
    {
        Level = level;
        Considered = NonNegative(considered, nameof(considered));
        Analyzed = NonNegative(analyzed, nameof(analyzed));
        Remaining = NonNegative(remaining, nameof(remaining));
        Excluded = NonNegative(excluded, nameof(excluded));
        Failed = NonNegative(failed, nameof(failed));
        PartialReason = ContractGuards.OptionalText(
            partialReason,
            nameof(partialReason));

        if (level is CoverageLevel.Partial && PartialReason is null)
        {
            throw new ArgumentException(
                "Partial coverage requires a reason.",
                nameof(partialReason));
        }

        if (level is not CoverageLevel.Partial && PartialReason is not null)
        {
            throw new ArgumentException(
                "A partial reason is valid only for partial coverage.",
                nameof(partialReason));
        }

        if (level is CoverageLevel.Complete &&
            (Remaining is > 0 || Failed is > 0))
        {
            throw new ArgumentException(
                "Complete coverage cannot include remaining or failed targets.",
                nameof(level));
        }

        if (level is CoverageLevel.NotApplicable &&
            (Considered is not null ||
             Analyzed is not null ||
             Remaining is not null ||
             Excluded is not null ||
             Failed is not null))
        {
            throw new ArgumentException(
                "Not-applicable coverage cannot include target counts.",
                nameof(level));
        }
    }

    public CoverageLevel Level { get; }

    public int? Considered { get; }

    public int? Analyzed { get; }

    public int? Remaining { get; }

    public int? Excluded { get; }

    public int? Failed { get; }

    public string? PartialReason { get; }

    private static int? NonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Coverage counts cannot be negative.");
        }

        return value;
    }
}
