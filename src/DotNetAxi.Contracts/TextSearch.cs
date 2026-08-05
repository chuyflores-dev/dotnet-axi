namespace DotNetAxi.Contracts;

/// <summary>Text-search inputs shared by the CLI and built-in engines.</summary>
public record TextSearchRequest
{
    public TextSearchRequest(
        string query,
        WorkspaceTraversalRequest traversal,
        bool caseSensitive = false,
        int limit = 100,
        int previewLength = 160,
        int skippedDetailLimit = 50)
    {
        Query = ContractGuards.RequiredText(query, nameof(query));
        Traversal = traversal ?? throw new ArgumentNullException(nameof(traversal));
        CaseSensitive = caseSensitive;
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (previewLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(previewLength));
        }

        if (skippedDetailLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skippedDetailLimit));
        }

        Limit = limit;
        PreviewLength = previewLength;
        SkippedDetailLimit = skippedDetailLimit;
    }

    public string Query { get; }
    public WorkspaceTraversalRequest Traversal { get; }
    public bool CaseSensitive { get; }
    public int Limit { get; }
    public int PreviewLength { get; }
    public int SkippedDetailLimit { get; }
}

/// <summary>Regular-expression text-search inputs with bounded per-file matching.</summary>
public sealed record RegexTextSearchRequest : TextSearchRequest
{
    private static readonly TimeSpan MaximumPerFileTimeout =
        TimeSpan.FromMilliseconds(int.MaxValue - 1D);

    public RegexTextSearchRequest(
        string query,
        WorkspaceTraversalRequest traversal,
        TimeSpan perFileTimeout,
        bool caseSensitive = false,
        int limit = 100,
        int previewLength = 160,
        int skippedDetailLimit = 50)
        : base(
            query,
            traversal,
            caseSensitive,
            limit,
            previewLength,
            skippedDetailLimit)
    {
        if (perFileTimeout <= TimeSpan.Zero
            || perFileTimeout > MaximumPerFileTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(perFileTimeout),
                perFileTimeout,
                "The regular-expression per-file timeout must be positive and bounded.");
        }

        PerFileTimeout = perFileTimeout;
    }

    public TimeSpan PerFileTimeout { get; }
}

public sealed record TextSearchMatch
{
    public TextSearchMatch(
        string id,
        SourceLocation location,
        string preview)
    {
        Id = ContractGuards.RequiredText(id, nameof(id));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        Preview = ContractGuards.RequiredText(preview, nameof(preview));
    }

    public string Id { get; }
    public SourceLocation Location { get; }
    public string Preview { get; }
}

public sealed record TextSearchResult
{
    public TextSearchResult(
        IEnumerable<TextSearchMatch> matches,
        int? total,
        bool totalKnown,
        int skippedBinary,
        int skippedUndecodable,
        string snapshot,
        IEnumerable<TextSearchSkippedFile>? skippedFiles = null,
        IEnumerable<TextSearchFileObservation>? observations = null,
        bool skipTotalsKnown = true,
        int? skippedFileTotal = null,
        int skippedUnsupportedEncoding = 0,
        int skippedUnreadable = 0,
        IEnumerable<TextSearchError>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(matches);
        Matches = ContractGuards.Copy(matches);
        if (total < 0 || (totalKnown && total is null))
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }

        if (skippedBinary < 0 || skippedUndecodable < 0 ||
            skippedUnsupportedEncoding < 0 || skippedUnreadable < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skippedBinary));
        }

        Total = total;
        TotalKnown = totalKnown;
        SkippedBinary = skippedBinary;
        SkippedUndecodable = skippedUndecodable;
        SkippedUnsupportedEncoding = skippedUnsupportedEncoding;
        SkippedUnreadable = skippedUnreadable;
        Snapshot = ContractGuards.RequiredText(snapshot, nameof(snapshot));
        SkippedFiles = ContractGuards.Copy(skippedFiles);
        Observations = ContractGuards.Copy(observations);
        Errors = ContractGuards.Copy(errors);
        SkipTotalsKnown = skipTotalsKnown;
        if (skippedFileTotal < SkippedFiles.Count ||
            (skipTotalsKnown && skippedFileTotal is null))
        {
            throw new ArgumentOutOfRangeException(nameof(skippedFileTotal));
        }

        SkippedFileTotal = skipTotalsKnown ? skippedFileTotal ?? SkippedFiles.Count : null;
    }

    public IReadOnlyList<TextSearchMatch> Matches { get; }
    public int? Total { get; }
    public bool TotalKnown { get; }
    public int SkippedBinary { get; }
    public int SkippedUndecodable { get; }
    public int SkippedUnsupportedEncoding { get; }
    public int SkippedUnreadable { get; }
    public string Snapshot { get; }
    public IReadOnlyList<TextSearchSkippedFile> SkippedFiles { get; }
    public IReadOnlyList<TextSearchFileObservation> Observations { get; }
    public IReadOnlyList<TextSearchError> Errors { get; }
    public bool SkipTotalsKnown { get; }
    public int? SkippedFileTotal { get; }
}

/// <summary>A safe, structured failure produced while evaluating a text query.</summary>
public sealed record TextSearchError
{
    public TextSearchError(
        TextSearchErrorKind kind,
        string query,
        string? path = null)
    {
        Kind = Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind));
        Query = ContractGuards.RequiredText(query, nameof(query));
        Path = path is null
            ? null
            : ContractGuards.RequiredText(path, nameof(path));

        if (kind is TextSearchErrorKind.InvalidRegularExpression
            && Path is not null)
        {
            throw new ArgumentException(
                "Invalid regular-expression outcomes do not identify a file.",
                nameof(path));
        }

        if (kind is TextSearchErrorKind.RegularExpressionTimeout
            && Path is null)
        {
            throw new ArgumentNullException(
                nameof(path),
                "Regular-expression timeout outcomes must identify a file.");
        }
    }

    public TextSearchErrorKind Kind { get; }
    public string Query { get; }
    public string? Path { get; }
}

public enum TextSearchErrorKind
{
    InvalidRegularExpression,
    RegularExpressionTimeout,
}

public sealed record TextSearchSkippedFile(string Path, string Reason)
{
    public string Path { get; } = ContractGuards.RequiredText(Path, nameof(Path));
    public string Reason { get; } = ContractGuards.RequiredText(Reason, nameof(Reason));
}

/// <summary>One explicitly observed file outcome from a text search scan.</summary>
public sealed record TextSearchFileObservation(string Path, TextSearchFileStatus Status)
{
    public string Path { get; } = ContractGuards.RequiredText(Path, nameof(Path));
    public TextSearchFileStatus Status { get; } = Enum.IsDefined(Status)
        ? Status
        : throw new ArgumentOutOfRangeException(nameof(Status));
}

public enum TextSearchFileStatus
{
    Analyzed,
    Binary,
    Undecodable,
    UnsupportedEncoding,
    Unreadable,
    RegularExpressionTimeout,
    LimitReached,
}

public interface ILiteralTextSearcher
{
    TextSearchResult Search(TextSearchRequest request);

    Task<TextSearchResult> SearchAsync(
        TextSearchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRegexTextSearcher
{
    TextSearchResult Search(RegexTextSearchRequest request);

    Task<TextSearchResult> SearchAsync(
        RegexTextSearchRequest request,
        CancellationToken cancellationToken = default);
}
