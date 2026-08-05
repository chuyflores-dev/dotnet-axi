using DotNetAxi.Contracts;

namespace DotNetAxi.Structural;

public sealed record AstGrepVersion : IComparable<AstGrepVersion>
{
    public AstGrepVersion(
        int major,
        int minor,
        int patch,
        bool isPrerelease = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        Major = major;
        Minor = minor;
        Patch = patch;
        IsPrerelease = isPrerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public bool IsPrerelease { get; }

    public int CompareTo(AstGrepVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = Major.CompareTo(other.Major);
        if (comparison == 0)
        {
            comparison = Minor.CompareTo(other.Minor);
        }

        if (comparison == 0)
        {
            comparison = Patch.CompareTo(other.Patch);
        }

        if (comparison == 0)
        {
            comparison = other.IsPrerelease.CompareTo(IsPrerelease);
        }

        return comparison;
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-prerelease" : string.Empty);
}

public sealed record AstGrepAdapterOptions
{
    public static AstGrepVersion DefaultMinimumVersion { get; } = new(0, 45, 0);

    public static AstGrepVersion DefaultMaximumVersionExclusive { get; } = new(0, 46, 0);

    public AstGrepAdapterOptions(
        string? executablePath = null,
        AstGrepVersion? minimumVersion = null,
        AstGrepVersion? maximumVersionExclusive = null,
        TimeSpan? versionTimeout = null,
        TimeSpan? searchTimeout = null,
        int standardOutputLimit = 16 * 1024 * 1024,
        int standardErrorLimit = 64 * 1024)
    {
        if (executablePath is not null
            && !Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "An explicit AST-grep executable path must be fully qualified.",
                nameof(executablePath));
        }

        ExecutablePath = executablePath is null
            ? null
            : Path.GetFullPath(executablePath);
        MinimumVersion = minimumVersion ?? DefaultMinimumVersion;
        MaximumVersionExclusive = maximumVersionExclusive
            ?? DefaultMaximumVersionExclusive;
        if (MinimumVersion.CompareTo(MaximumVersionExclusive) >= 0)
        {
            throw new ArgumentException(
                "The supported AST-grep version range must be non-empty.",
                nameof(maximumVersionExclusive));
        }

        VersionTimeout = ValidateTimeout(
            versionTimeout ?? TimeSpan.FromSeconds(10),
            nameof(versionTimeout));
        SearchTimeout = ValidateTimeout(
            searchTimeout ?? TimeSpan.FromSeconds(30),
            nameof(searchTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(standardOutputLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(standardErrorLimit);
        StandardOutputLimit = standardOutputLimit;
        StandardErrorLimit = standardErrorLimit;
    }

    public string? ExecutablePath { get; }

    public AstGrepVersion MinimumVersion { get; }

    public AstGrepVersion MaximumVersionExclusive { get; }

    public TimeSpan VersionTimeout { get; }

    public TimeSpan SearchTimeout { get; }

    public int StandardOutputLimit { get; }

    public int StandardErrorLimit { get; }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero
            || timeout.TotalMilliseconds > uint.MaxValue - 1D)
        {
            throw new ArgumentOutOfRangeException(parameterName, timeout, "The timeout must be positive and bounded.");
        }

        return timeout;
    }
}

public sealed record AstGrepSearchRequest
{
    public AstGrepSearchRequest(
        string pattern,
        WorkspaceTraversalRequest traversal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (pattern.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An AST-grep pattern cannot contain a null character.",
                nameof(pattern));
        }

        Pattern = pattern;
        Traversal = traversal ?? throw new ArgumentNullException(nameof(traversal));
    }

    public string Pattern { get; }

    public WorkspaceTraversalRequest Traversal { get; }
}

public enum AstGrepCapabilityState
{
    Supported,
    Missing,
    Incompatible,
    Unavailable,
    Cancelled,
}

public enum AstGrepIssueKind
{
    Missing,
    IncompatibleVersion,
    MalformedVersion,
    MalformedOutput,
    ExecutionFailed,
    TimedOut,
    OutputLimitExceeded,
    Cancelled,
}

public sealed record AstGrepIssue
{
    public AstGrepIssue(
        AstGrepIssueKind kind,
        string code,
        string correction)
    {
        Kind = Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind));
        Code = RequiredText(code, nameof(code));
        Correction = RequiredText(correction, nameof(correction));
    }

    public AstGrepIssueKind Kind { get; }

    public string Code { get; }

    public string Correction { get; }

    private static string RequiredText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public sealed record AstGrepCapabilityResult
{
    public AstGrepCapabilityResult(
        AstGrepCapabilityState state,
        AstGrepVersion? version,
        AstGrepIssue? issue)
    {
        State = Enum.IsDefined(state)
            ? state
            : throw new ArgumentOutOfRangeException(nameof(state));
        if ((state is AstGrepCapabilityState.Supported) != (issue is null)
            || (state is AstGrepCapabilityState.Supported && version is null))
        {
            throw new ArgumentException(
                "A supported capability requires a version and no issue; every other state requires an issue.");
        }

        Version = version;
        Issue = issue;
    }

    public AstGrepCapabilityState State { get; }

    public AstGrepVersion? Version { get; }

    public AstGrepIssue? Issue { get; }

    public bool IsSupported => State is AstGrepCapabilityState.Supported;
}

public sealed record StructuralSourceRange
{
    public StructuralSourceRange(SourceLocation start, SourceLocation end)
    {
        Start = start ?? throw new ArgumentNullException(nameof(start));
        End = end ?? throw new ArgumentNullException(nameof(end));
        if (!Start.Path.Equals(End.Path, StringComparison.Ordinal)
            || Start.IsExternal != End.IsExternal
            || End.Line < Start.Line
            || (End.Line == Start.Line && End.Column < Start.Column))
        {
            throw new ArgumentException(
                "A structural source range must be ordered within one source path.",
                nameof(end));
        }
    }

    public SourceLocation Start { get; }

    public SourceLocation End { get; }
}

public sealed record StructuralCandidate
{
    public StructuralCandidate(
        string id,
        StructuralSourceRange range,
        string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(text);
        Id = id;
        Range = range ?? throw new ArgumentNullException(nameof(range));
        Text = text;
    }

    public string Id { get; }

    public StructuralSourceRange Range { get; }

    public string Text { get; }
}

public enum AstGrepAdapterOutcome
{
    Succeeded,
    CapabilityUnavailable,
    MalformedOutput,
    ExecutionFailed,
    TimedOut,
    OutputLimitExceeded,
    Cancelled,
}

public sealed record AstGrepSearchResult
{
    public AstGrepSearchResult(
        AstGrepAdapterOutcome outcome,
        AstGrepCapabilityResult capability,
        IEnumerable<StructuralCandidate>? candidates = null,
        AstGrepIssue? issue = null)
    {
        Outcome = Enum.IsDefined(outcome)
            ? outcome
            : throw new ArgumentOutOfRangeException(nameof(outcome));
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
        Candidates = Array.AsReadOnly((candidates ?? []).ToArray());
        if ((outcome is AstGrepAdapterOutcome.Succeeded) != (issue is null)
            || (outcome is not AstGrepAdapterOutcome.Succeeded && Candidates.Count != 0))
        {
            throw new ArgumentException(
                "Successful searches have candidates and no issue; failed searches have an issue and no candidates.");
        }

        Issue = issue;
    }

    public AstGrepAdapterOutcome Outcome { get; }

    public AstGrepCapabilityResult Capability { get; }

    public IReadOnlyList<StructuralCandidate> Candidates { get; }

    public AstGrepIssue? Issue { get; }
}
