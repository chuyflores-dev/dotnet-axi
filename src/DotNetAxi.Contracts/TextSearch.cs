namespace DotNetAxi.Contracts;

/// <summary>Literal text-search inputs shared by the CLI and built-in engine.</summary>
public sealed record TextSearchRequest
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
        int skippedUnreadable = 0)
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
    public bool SkipTotalsKnown { get; }
    public int? SkippedFileTotal { get; }
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
    LimitReached,
}

public interface ILiteralTextSearcher
{
    TextSearchResult Search(TextSearchRequest request);

    Task<TextSearchResult> SearchAsync(
        TextSearchRequest request,
        CancellationToken cancellationToken = default);
}
