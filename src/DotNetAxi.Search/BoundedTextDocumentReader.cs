using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace DotNetAxi.Search;

public sealed class BoundedTextDocumentReadResult
{
    internal BoundedTextDocumentReadResult(
        TextDocumentReadStatus status,
        string? encoding = null,
        bool hasByteOrderMark = false,
        long byteCount = 0,
        string? preview = null,
        long includedCharacters = 0,
        long totalCharacters = 0,
        long totalLines = 1,
        long? actualStartLine = null,
        long? actualEndLine = null,
        string? header = null,
        string? contentHash = null)
    {
        Status = status;
        Encoding = encoding;
        HasByteOrderMark = hasByteOrderMark;
        ByteCount = byteCount;
        Preview = preview;
        IncludedCharacters = includedCharacters;
        TotalCharacters = totalCharacters;
        TotalLines = totalLines;
        ActualStartLine = actualStartLine;
        ActualEndLine = actualEndLine;
        Header = header;
        ContentHash = contentHash;
    }

    public TextDocumentReadStatus Status { get; }

    public string? Encoding { get; }

    public bool HasByteOrderMark { get; }

    public long ByteCount { get; }

    public string? Preview { get; }

    public long IncludedCharacters { get; }

    public long TotalCharacters { get; }

    public long TotalLines { get; }

    public long? ActualStartLine { get; }

    public long? ActualEndLine { get; }

    public long OmittedCharacters =>
        TotalCharacters - IncludedCharacters;

    public bool Truncated => OmittedCharacters > 0;

    public string? Header { get; }

    public string? ContentHash { get; }
}

public sealed class BoundedTextDocumentReader
{
    private const int BufferSize = 64 * 1024;

    public async Task<BoundedTextDocumentReadResult> ReadAsync(
        string path,
        int? maximumCharacters,
        int headerCharacters,
        CancellationToken cancellationToken = default)
        => await ReadAsync(
                path,
                maximumCharacters,
                headerCharacters,
                startLine: null,
                endLine: null,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<BoundedTextDocumentReadResult> ReadAsync(
        string path,
        int? maximumCharacters,
        int headerCharacters,
        int? startLine,
        int? endLine,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                maximumCharacters,
                "The character limit cannot be negative.");
        }

        if (headerCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(headerCharacters),
                headerCharacters,
                "The header character limit cannot be negative.");
        }

        ValidateLineSpan(startLine, endLine);

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ReadAsync(
                    stream,
                    maximumCharacters,
                    headerCharacters,
                    startLine,
                    endLine,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            return Result(TextDocumentReadStatus.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return Result(TextDocumentReadStatus.Unreadable);
        }
    }

    internal static async Task<BoundedTextDocumentReadResult> ReadAsync(
        Stream stream,
        int? maximumCharacters,
        int headerCharacters,
        CancellationToken cancellationToken)
        => await ReadAsync(
                stream,
                maximumCharacters,
                headerCharacters,
                startLine: null,
                endLine: null,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<BoundedTextDocumentReadResult> ReadAsync(
        Stream stream,
        int? maximumCharacters,
        int headerCharacters,
        int? startLine,
        int? endLine,
        CancellationToken cancellationToken)
    {
        ValidateLineSpan(startLine, endLine);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var prefix = new byte[4];
        var prefixCount = await ReadPrefixAsync(
                stream,
                prefix,
                cancellationToken)
            .ConfigureAwait(false);
        hash.AppendData(prefix.AsSpan(0, prefixCount));
        long byteCount = prefixCount;

        if (TextDocumentEncodingDetector.HasUnsupportedByteOrderMark(
                prefix.AsSpan(0, prefixCount)))
        {
            return Result(TextDocumentReadStatus.UnsupportedEncoding);
        }

        var encoding = TextDocumentEncodingDetector.Detect(
            prefix.AsSpan(0, prefixCount));
        var decoder = encoding.Value.GetDecoder();
        var accumulator = new TextAccumulator(
            maximumCharacters,
            headerCharacters,
            startLine,
            endLine);
        var initialContent = prefix.AsSpan(
            encoding.PreambleLength,
            prefixCount - encoding.PreambleLength);
        var undecodable = false;
        if (encoding.DetectNullBytes && initialContent.Contains((byte)0))
        {
            return Result(TextDocumentReadStatus.Binary);
        }

        var byteBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var characterBuffer = ArrayPool<char>.Shared.Rent(BufferSize);
        try
        {
            undecodable = !TryDecode(
                decoder,
                initialContent,
                flush: false,
                characterBuffer,
                accumulator);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = await stream.ReadAsync(
                        byteBuffer.AsMemory(0, BufferSize),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                byteCount += count;
                var bytes = byteBuffer.AsSpan(0, count);
                hash.AppendData(bytes);
                if (encoding.DetectNullBytes && bytes.Contains((byte)0))
                {
                    return Result(TextDocumentReadStatus.Binary);
                }

                if (!undecodable)
                {
                    undecodable = !TryDecode(
                        decoder,
                        bytes,
                        flush: false,
                        characterBuffer,
                        accumulator);
                }
            }

            if (undecodable
                || !TryDecode(
                    decoder,
                    [],
                    flush: true,
                    characterBuffer,
                    accumulator)
                || !accumulator.TryComplete())
            {
                return Result(TextDocumentReadStatus.Undecodable);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new BoundedTextDocumentReadResult(
                TextDocumentReadStatus.Success,
                encoding.Name,
                encoding.PreambleLength > 0,
                byteCount,
                accumulator.Preview,
                accumulator.IncludedCharacters,
                accumulator.TotalCharacters,
                accumulator.TotalLines,
                accumulator.ActualStartLine,
                accumulator.ActualEndLine,
                accumulator.Header,
                Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer, clearArray: true);
            ArrayPool<char>.Shared.Return(characterBuffer, clearArray: true);
        }
    }

    private static async Task<int> ReadPrefixAsync(
        Stream stream,
        byte[] prefix,
        CancellationToken cancellationToken)
    {
        var count = 0;
        while (count < prefix.Length)
        {
            var read = await stream.ReadAsync(
                    prefix.AsMemory(count, prefix.Length - count),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return count;
    }

    private static bool TryDecode(
        Decoder decoder,
        ReadOnlySpan<byte> bytes,
        bool flush,
        char[] characterBuffer,
        TextAccumulator accumulator)
    {
        try
        {
            do
            {
                decoder.Convert(
                    bytes,
                    characterBuffer,
                    flush,
                    out var bytesUsed,
                    out var charactersUsed,
                    out var completed);
                accumulator.Append(
                    characterBuffer.AsSpan(0, charactersUsed));
                bytes = bytes[bytesUsed..];
                if (completed)
                {
                    return true;
                }

                if (bytesUsed == 0 && charactersUsed == 0)
                {
                    throw new InvalidOperationException(
                        "The text decoder made no forward progress.");
                }
            }
            while (!bytes.IsEmpty || flush);

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static BoundedTextDocumentReadResult Result(
        TextDocumentReadStatus status) => new(status);

    private static void ValidateLineSpan(int? startLine, int? endLine)
    {
        if (startLine is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startLine),
                startLine,
                "The start line must be positive.");
        }

        if (endLine is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endLine),
                endLine,
                "The end line must be positive.");
        }

        if (startLine > endLine)
        {
            throw new ArgumentException(
                "The start line cannot be greater than the end line.",
                nameof(startLine));
        }
    }

    private sealed class TextAccumulator(
        int? maximumCharacters,
        int headerCharacters,
        int? startLine,
        int? endLine)
    {
        private readonly StringBuilder _preview = new();
        private readonly StringBuilder _header = new();
        private char? _pendingHighSurrogate;
        private bool _endsWithLineFeed;

        public string Preview => _preview.ToString();

        public long IncludedCharacters { get; private set; }

        public long TotalCharacters { get; private set; }

        public long TotalLines { get; private set; } = 1;

        public long? ActualStartLine { get; private set; }

        public long? ActualEndLine { get; private set; }

        public string Header => _header.ToString();

        public void Append(ReadOnlySpan<char> characters)
        {
            var headerRemaining = headerCharacters - _header.Length;
            if (headerRemaining > 0)
            {
                _header.Append(characters[..Math.Min(
                    headerRemaining,
                    characters.Length)]);
            }

            if (_pendingHighSurrogate is { } highSurrogate)
            {
                if (characters.IsEmpty)
                {
                    return;
                }

                Span<char> pair = stackalloc char[2];
                pair[0] = highSurrogate;
                pair[1] = characters[0];
                if (Rune.DecodeFromUtf16(
                        pair,
                        out var pendingRune,
                        out var pendingConsumed)
                    is not OperationStatus.Done
                    || pendingConsumed != pair.Length)
                {
                    throw new DecoderFallbackException(
                        "Decoded text contains invalid UTF-16.");
                }

                _pendingHighSurrogate = null;
                Append(pendingRune);
                characters = characters[1..];
            }

            while (!characters.IsEmpty)
            {
                var status = Rune.DecodeFromUtf16(
                    characters,
                    out var rune,
                    out var consumed);
                if (status is OperationStatus.NeedMoreData
                    && characters.Length == 1
                    && char.IsHighSurrogate(characters[0]))
                {
                    _pendingHighSurrogate = characters[0];
                    return;
                }

                if (status is not OperationStatus.Done)
                {
                    throw new DecoderFallbackException(
                        "Decoded text contains invalid UTF-16.");
                }

                Append(rune);
                characters = characters[consumed..];
            }
        }

        public bool TryComplete()
        {
            if (_pendingHighSurrogate is not null)
            {
                return false;
            }

            if (TotalCharacters == 0
                && (startLine ?? 1) <= TotalLines)
            {
                ActualStartLine = startLine ?? 1;
                ActualEndLine = ActualStartLine;
            }
            else if (_endsWithLineFeed
                && TotalCharacters == IncludedCharacters
                && (startLine ?? 1) <= TotalLines
                && (endLine ?? TotalLines) >= TotalLines)
            {
                ActualStartLine ??= TotalLines;
                ActualEndLine = TotalLines;
            }

            return true;
        }

        private void Append(Rune rune)
        {
            _endsWithLineFeed = rune.Value == '\n';
            var selected = TotalLines >= (startLine ?? 1)
                && (endLine is null || TotalLines <= endLine.Value);
            if (selected)
            {
                if (maximumCharacters is null
                    || IncludedCharacters < maximumCharacters.Value)
                {
                    _preview.Append(rune);
                    IncludedCharacters++;
                    ActualStartLine ??= TotalLines;
                    ActualEndLine = TotalLines;
                }

                TotalCharacters++;
            }

            if (rune.Value == '\n')
            {
                TotalLines++;
            }
        }
    }
}
