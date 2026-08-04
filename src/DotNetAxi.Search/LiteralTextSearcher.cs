using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public sealed class LiteralTextSearcher : ILiteralTextSearcher
{
    private readonly IWorkspacePathTraverser _traverser;

    public LiteralTextSearcher(IWorkspacePathTraverser traverser) =>
        _traverser = traverser ?? throw new ArgumentNullException(nameof(traverser));

    public TextSearchResult Search(TextSearchRequest request) =>
        SearchAsync(request).GetAwaiter().GetResult();

    public async Task<TextSearchResult> SearchAsync(
        TextSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = new List<TextSearchMatch>();
        var skipped = new List<TextSearchSkippedFile>();
        var observations = new List<TextSearchFileObservation>();
        var skippedFileTotal = 0;
        var skippedBinary = 0;
        var skippedUndecodable = 0;
        var skippedUnsupportedEncoding = 0;
        var skippedUnreadable = 0;
        var comparison = request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        using var observation = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(observation, "dotnet-axi/text-search-observation/v1");
        Append(observation, request.Query);
        Append(observation, request.CaseSensitive ? "sensitive" : "insensitive");
        var total = 0;
        foreach (var path in _traverser.Traverse(request.Traversal, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await ReadAsync(path.FullPath, cancellationToken).ConfigureAwait(false);
            Append(observation, path.RelativePath);
            Append(observation, read.Status.ToString());
            if (read.Bytes is not null)
            {
                Append(observation, read.Bytes);
            }

            if (read.Text is null)
            {
                var status = ToObservationStatus(read.Status);
                observations.Add(new TextSearchFileObservation(path.RelativePath, status));
                skippedFileTotal++;
                if (status is TextSearchFileStatus.Binary) skippedBinary++;
                if (status is TextSearchFileStatus.Undecodable) skippedUndecodable++;
                if (status is TextSearchFileStatus.UnsupportedEncoding) skippedUnsupportedEncoding++;
                if (status is TextSearchFileStatus.Unreadable) skippedUnreadable++;
                if (skipped.Count < request.SkippedDetailLimit)
                {
                    skipped.Add(new TextSearchSkippedFile(path.RelativePath, Reason(read.Status)));
                }
                continue;
            }

            var contentHash = Convert.ToHexStringLower(SHA256.HashData(read.Bytes!));
            for (var index = 0; (index = read.Text.IndexOf(request.Query, index, comparison)) >= 0; index += request.Query.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total++;
                if (matches.Count == request.Limit)
                {
                    observations.Add(new TextSearchFileObservation(path.RelativePath, TextSearchFileStatus.LimitReached));
                    return Result(matches, null, false, skipped, observations, skippedFileTotal, skippedBinary, skippedUndecodable, skippedUnsupportedEncoding, skippedUnreadable, observation);
                }

                matches.Add(CreateMatch(path, read.Text, index, request, contentHash));
            }
            observations.Add(new TextSearchFileObservation(path.RelativePath, TextSearchFileStatus.Analyzed));
        }

        return Result(matches, total, true, skipped, observations, skippedFileTotal, skippedBinary, skippedUndecodable, skippedUnsupportedEncoding, skippedUnreadable, observation);
    }

    private static TextSearchResult Result(
        IReadOnlyList<TextSearchMatch> matches, int? total, bool totalKnown,
        IReadOnlyList<TextSearchSkippedFile> skipped,
        IReadOnlyList<TextSearchFileObservation> observations,
        int skippedFileTotal,
        int skippedBinary,
        int skippedUndecodable,
        int skippedUnsupportedEncoding,
        int skippedUnreadable,
        IncrementalHash observation) =>
        new(matches, total, totalKnown, skippedBinary, skippedUndecodable,
            "ws_" + Convert.ToHexStringLower(observation.GetHashAndReset()),
            skipped,
            observations,
            skipTotalsKnown: totalKnown,
            skippedFileTotal: skippedFileTotal,
            skippedUnsupportedEncoding: skippedUnsupportedEncoding,
            skippedUnreadable: skippedUnreadable);

    private static TextSearchMatch CreateMatch(WorkspaceTraversalPath path, string text, int index, TextSearchRequest request, string contentHash)
    {
        var (line, lineStart) = Locate(text, index);
        var lineEnd = FindLineEnd(text, index);
        var column = index - lineStart + 1;
        var preview = ScalarPreview(text[lineStart..lineEnd], index - lineStart, request.PreviewLength);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/text-match/v1"); Append(hash, contentHash); Append(hash, path.RelativePath);
        Append(hash, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, request.Query); Append(hash, request.Query.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, request.CaseSensitive ? "sensitive" : "insensitive");
        return new TextSearchMatch("text/v1/" + Convert.ToHexStringLower(hash.GetHashAndReset()),
            new SourceLocation(path.RelativePath, line, column, path.IsExternal), preview);
    }

    private static string ScalarPreview(string line, int matchOffset, int maxScalars)
    {
        if (string.IsNullOrWhiteSpace(line)) return "…";
        if (line.EnumerateRunes().Count() <= maxScalars) return line;
        var start = Math.Min(Math.Max(0, matchOffset - maxScalars / 2), line.Length);
        while (start > 0 && char.IsLowSurrogate(line[start])) start--;
        var hasLeading = start > 0;
        var contentBudget = Math.Max(0, maxScalars - (hasLeading ? 1 : 0));
        var end = start; var count = 0;
        while (end < line.Length && count < contentBudget)
        {
            Rune.DecodeFromUtf16(line.AsSpan(end), out _, out var consumed); end += consumed; count++;
        }
        var hasTrailing = end < line.Length;
        if (hasTrailing && count > 0 && count + (hasLeading ? 2 : 1) > maxScalars)
        {
            end = PreviousRuneStart(line, end);
            hasTrailing = true;
        }

        if (hasLeading && hasTrailing && maxScalars == 1) return "…";
        var preview = (hasLeading ? "…" : string.Empty) + line[start..end] + (hasTrailing ? "…" : string.Empty);
        return string.IsNullOrWhiteSpace(preview) ? "…" : preview;
    }

    private static (int Line, int Start) Locate(string text, int index)
    {
        var line = 1;
        var start = 0;
        for (var cursor = 0; cursor < index; cursor++)
        {
            if (text[cursor] == '\r')
            {
                if (cursor + 1 < index && text[cursor + 1] == '\n') cursor++;
                line++; start = cursor + 1;
            }
            else if (text[cursor] == '\n')
            {
                line++; start = cursor + 1;
            }
        }

        return (line, start);
    }

    private static int FindLineEnd(string text, int index)
    {
        for (var cursor = index; cursor < text.Length; cursor++)
        {
            if (text[cursor] is '\r' or '\n') return cursor;
        }

        return text.Length;
    }

    private static int PreviousRuneStart(string value, int end) =>
        end > 1 && char.IsLowSurrogate(value[end - 1]) && char.IsHighSurrogate(value[end - 2])
            ? end - 2
            : end - 1;

    private static async Task<ReadResult> ReadAsync(string path, CancellationToken token)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            if (bytes.Length >= 4 && ((bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0) || (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff))) return new(null, bytes, Status.UnsupportedEncoding);
            Encoding encoding; var start = 0;
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) { encoding = new UnicodeEncoding(false, true, true); start = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) { encoding = new UnicodeEncoding(true, true, true); start = 2; }
            else { if (bytes.Contains((byte)0)) return new(null, bytes, Status.Binary); encoding = new UTF8Encoding(false, true); if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) start = 3; }
            try
            {
                return new(encoding.GetString(bytes, start, bytes.Length - start), bytes, Status.Success);
            }
            catch (DecoderFallbackException)
            {
                return new(null, bytes, Status.Undecodable);
            }
        }
        catch (IOException) { return new(null, null, Status.Unreadable); }
        catch (UnauthorizedAccessException) { return new(null, null, Status.Unreadable); }
    }

    private static string Reason(Status status) => status switch { Status.Binary => "binary", Status.UnsupportedEncoding => "unsupported_encoding", Status.Unreadable => "unreadable", _ => "undecodable" };
    private static TextSearchFileStatus ToObservationStatus(Status status) => status switch
    {
        Status.Binary => TextSearchFileStatus.Binary,
        Status.UnsupportedEncoding => TextSearchFileStatus.UnsupportedEncoding,
        Status.Unreadable => TextSearchFileStatus.Unreadable,
        _ => TextSearchFileStatus.Undecodable,
    };
    private static void Append(IncrementalHash hash, string text) => Append(hash, Encoding.UTF8.GetBytes(text));
    private static void Append(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }
    private enum Status { Success, Binary, Undecodable, UnsupportedEncoding, Unreadable }
    private sealed record ReadResult(string? Text, byte[]? Bytes, Status Status);
}
