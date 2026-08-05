using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

internal static class BuiltInTextSearchEngine
{
    public static async Task<TextSearchResult> SearchAsync(
        IWorkspacePathTraverser traverser,
        TextSearchRequest request,
        ITextSearchMatcher matcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traverser);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(matcher);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = new List<TextSearchMatch>();
        var skipped = new List<TextSearchSkippedFile>();
        var observations = new List<TextSearchFileObservation>();
        var errors = new List<TextSearchError>();
        var skippedFileTotal = 0;
        var skippedBinary = 0;
        var skippedUndecodable = 0;
        var skippedUnsupportedEncoding = 0;
        var skippedUnreadable = 0;
        var total = 0;
        var totalKnown = true;

        using var observation = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        InitializeObservation(observation, request, matcher);

        foreach (var path in traverser.Traverse(request.Traversal, cancellationToken))
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
            try
            {
                var remainingCapacity = request.Limit - matches.Count;
                var matchObservationLimit = remainingCapacity == int.MaxValue
                    ? int.MaxValue
                    : remainingCapacity + 1;
                foreach (var span in matcher.FindMatches(
                             read.Text,
                             matchObservationLimit,
                             cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total++;
                    if (matches.Count == request.Limit)
                    {
                        observations.Add(new TextSearchFileObservation(
                            path.RelativePath,
                            TextSearchFileStatus.LimitReached));
                        return Result(
                            matches,
                            total: null,
                            totalKnown: false,
                            skipped,
                            observations,
                            skippedFileTotal,
                            skippedBinary,
                            skippedUndecodable,
                            skippedUnsupportedEncoding,
                            skippedUnreadable,
                            skipTotalsKnown: false,
                            errors,
                            observation);
                    }

                    matches.Add(CreateMatch(
                        path,
                        read.Text,
                        span,
                        request,
                        contentHash,
                        matcher.IsRegularExpression));
                }

                observations.Add(new TextSearchFileObservation(
                    path.RelativePath,
                    TextSearchFileStatus.Analyzed));
            }
            catch (RegexTextSearchFileTimeoutException)
                when (matcher.IsRegularExpression)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalKnown = false;
                Append(observation, "regular_expression_timeout");
                observations.Add(new TextSearchFileObservation(
                    path.RelativePath,
                    TextSearchFileStatus.RegularExpressionTimeout));
                errors.Add(new TextSearchError(
                    TextSearchErrorKind.RegularExpressionTimeout,
                    request.Query,
                    path.RelativePath));
            }
        }

        return Result(
            matches,
            totalKnown ? total : null,
            totalKnown,
            skipped,
            observations,
            skippedFileTotal,
            skippedBinary,
            skippedUndecodable,
            skippedUnsupportedEncoding,
            skippedUnreadable,
            skipTotalsKnown: true,
            errors,
            observation);
    }

    public static TextSearchResult InvalidRegularExpression(
        RegexTextSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var observation = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        InitializeObservation(
            observation,
            request,
            new InvalidRegexMatcher(request.PerFileTimeout));
        Append(observation, "invalid_regular_expression");

        return Result(
            matches: [],
            total: null,
            totalKnown: false,
            skipped: [],
            observations: [],
            skippedFileTotal: 0,
            skippedBinary: 0,
            skippedUndecodable: 0,
            skippedUnsupportedEncoding: 0,
            skippedUnreadable: 0,
            skipTotalsKnown: false,
            errors:
            [
                new TextSearchError(
                    TextSearchErrorKind.InvalidRegularExpression,
                    request.Query),
            ],
            observation);
    }

    private static void InitializeObservation(
        IncrementalHash observation,
        TextSearchRequest request,
        ITextSearchMatcher matcher)
    {
        Append(observation, "dotnet-axi/text-search-observation/v1");
        Append(observation, request.Query);
        Append(observation, request.CaseSensitive ? "sensitive" : "insensitive");
        if (matcher.IsRegularExpression)
        {
            Append(observation, "regular_expression");
            Append(
                observation,
                matcher.PerFileTimeout!.Value.Ticks.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static TextSearchResult Result(
        IReadOnlyList<TextSearchMatch> matches,
        int? total,
        bool totalKnown,
        IReadOnlyList<TextSearchSkippedFile> skipped,
        IReadOnlyList<TextSearchFileObservation> observations,
        int skippedFileTotal,
        int skippedBinary,
        int skippedUndecodable,
        int skippedUnsupportedEncoding,
        int skippedUnreadable,
        bool skipTotalsKnown,
        IReadOnlyList<TextSearchError> errors,
        IncrementalHash observation) =>
        new(
            matches,
            total,
            totalKnown,
            skippedBinary,
            skippedUndecodable,
            "ws_" + Convert.ToHexStringLower(observation.GetHashAndReset()),
            skipped,
            observations,
            skipTotalsKnown,
            skippedFileTotal,
            skippedUnsupportedEncoding,
            skippedUnreadable,
            errors);

    private static TextSearchMatch CreateMatch(
        WorkspaceTraversalPath path,
        string text,
        TextSearchSpan span,
        TextSearchRequest request,
        string contentHash,
        bool isRegularExpression)
    {
        var (line, lineStart) = Locate(text, span.Index);
        var lineEnd = FindLineEnd(text, span.Index);
        var column = span.Index - lineStart + 1;
        var preview = ScalarPreview(
            text[lineStart..lineEnd],
            span.Index - lineStart,
            request.PreviewLength);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/text-match/v1");
        Append(hash, contentHash);
        Append(hash, path.RelativePath);
        Append(hash, span.Index.ToString(CultureInfo.InvariantCulture));
        Append(hash, request.Query);
        Append(hash, span.Length.ToString(CultureInfo.InvariantCulture));
        Append(hash, request.CaseSensitive ? "sensitive" : "insensitive");
        if (isRegularExpression)
        {
            Append(hash, "regular_expression");
        }

        return new TextSearchMatch(
            "text/v1/" + Convert.ToHexStringLower(hash.GetHashAndReset()),
            new SourceLocation(path.RelativePath, line, column, path.IsExternal),
            preview);
    }

    private static string ScalarPreview(
        string line,
        int matchOffset,
        int maxScalars)
    {
        if (string.IsNullOrWhiteSpace(line)) return "…";
        if (line.EnumerateRunes().Count() <= maxScalars) return line;
        var start = Math.Min(Math.Max(0, matchOffset - maxScalars / 2), line.Length);
        while (start > 0
               && start < line.Length
               && char.IsLowSurrogate(line[start]))
        {
            start--;
        }

        var hasLeading = start > 0;
        var contentBudget = Math.Max(0, maxScalars - (hasLeading ? 1 : 0));
        var end = start;
        var count = 0;
        while (end < line.Length && count < contentBudget)
        {
            Rune.DecodeFromUtf16(line.AsSpan(end), out _, out var consumed);
            end += consumed;
            count++;
        }

        var hasTrailing = end < line.Length;
        if (hasTrailing
            && count > 0
            && count + (hasLeading ? 2 : 1) > maxScalars)
        {
            end = PreviousRuneStart(line, end);
            hasTrailing = true;
        }

        if (hasLeading && hasTrailing && maxScalars == 1) return "…";
        var preview = (hasLeading ? "…" : string.Empty)
            + line[start..end]
            + (hasTrailing ? "…" : string.Empty);
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
                line++;
                start = cursor + 1;
            }
            else if (text[cursor] == '\n')
            {
                line++;
                start = cursor + 1;
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
        end > 1
        && char.IsLowSurrogate(value[end - 1])
        && char.IsHighSurrogate(value[end - 2])
            ? end - 2
            : end - 1;

    private static async Task<ReadResult> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes.Length >= 4
                && ((bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0)
                    || (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff)))
            {
                return new(null, bytes, ReadStatus.UnsupportedEncoding);
            }

            Encoding encoding;
            var start = 0;
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            {
                encoding = new UnicodeEncoding(false, true, true);
                start = 2;
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            {
                encoding = new UnicodeEncoding(true, true, true);
                start = 2;
            }
            else
            {
                if (bytes.Contains((byte)0))
                {
                    return new(null, bytes, ReadStatus.Binary);
                }

                encoding = new UTF8Encoding(false, true);
                if (bytes.Length >= 3
                    && bytes[0] == 0xef
                    && bytes[1] == 0xbb
                    && bytes[2] == 0xbf)
                {
                    start = 3;
                }
            }

            try
            {
                return new(
                    encoding.GetString(bytes, start, bytes.Length - start),
                    bytes,
                    ReadStatus.Success);
            }
            catch (DecoderFallbackException)
            {
                return new(null, bytes, ReadStatus.Undecodable);
            }
        }
        catch (IOException)
        {
            return new(null, null, ReadStatus.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return new(null, null, ReadStatus.Unreadable);
        }
    }

    private static string Reason(ReadStatus status) => status switch
    {
        ReadStatus.Binary => "binary",
        ReadStatus.UnsupportedEncoding => "unsupported_encoding",
        ReadStatus.Unreadable => "unreadable",
        _ => "undecodable",
    };

    private static TextSearchFileStatus ToObservationStatus(ReadStatus status) =>
        status switch
        {
            ReadStatus.Binary => TextSearchFileStatus.Binary,
            ReadStatus.UnsupportedEncoding => TextSearchFileStatus.UnsupportedEncoding,
            ReadStatus.Unreadable => TextSearchFileStatus.Unreadable,
            _ => TextSearchFileStatus.Undecodable,
        };

    private static void Append(IncrementalHash hash, string text) =>
        Append(hash, Encoding.UTF8.GetBytes(text));

    private static void Append(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private enum ReadStatus
    {
        Success,
        Binary,
        Undecodable,
        UnsupportedEncoding,
        Unreadable,
    }

    private sealed record ReadResult(
        string? Text,
        byte[]? Bytes,
        ReadStatus Status);

    private sealed class InvalidRegexMatcher(TimeSpan perFileTimeout)
        : ITextSearchMatcher
    {
        public bool IsRegularExpression => true;
        public TimeSpan? PerFileTimeout => perFileTimeout;

        public IEnumerable<TextSearchSpan> FindMatches(
            string text,
            int maximumMatches,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "An invalid regular expression cannot scan text.");
    }
}

internal interface ITextSearchMatcher
{
    bool IsRegularExpression { get; }
    TimeSpan? PerFileTimeout { get; }

    IEnumerable<TextSearchSpan> FindMatches(
        string text,
        int maximumMatches,
        CancellationToken cancellationToken);
}

internal readonly record struct TextSearchSpan(int Index, int Length);

internal sealed class LiteralTextMatcher(StringComparison comparison, string query)
    : ITextSearchMatcher
{
    public bool IsRegularExpression => false;
    public TimeSpan? PerFileTimeout => null;

    public IEnumerable<TextSearchSpan> FindMatches(
        string text,
        int maximumMatches,
        CancellationToken cancellationToken)
    {
        var observed = 0;
        for (var index = 0;
             (index = text.IndexOf(query, index, comparison)) >= 0;
             index += query.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new TextSearchSpan(index, query.Length);
            observed++;
            if (observed == maximumMatches)
            {
                yield break;
            }
        }
    }
}

internal sealed class RegexTextMatcher(
    string pattern,
    RegexOptions options,
    TimeSpan perFileTimeout) : ITextSearchMatcher
{
    public bool IsRegularExpression => true;
    public TimeSpan? PerFileTimeout => perFileTimeout;

    public IEnumerable<TextSearchSpan> FindMatches(
        string text,
        int maximumMatches,
        CancellationToken cancellationToken)
    {
        var matches = new List<TextSearchSpan>(Math.Min(maximumMatches, 256));
        try
        {
            var regex = new Regex(pattern, options, perFileTimeout);
            foreach (var match in regex.EnumerateMatches(text.AsSpan()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                matches.Add(new TextSearchSpan(match.Index, match.Length));
                if (matches.Count == maximumMatches)
                {
                    break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RegexTextSearchFileTimeoutException();
        }

        return matches;
    }
}

internal sealed class RegexTextSearchFileTimeoutException : Exception;
