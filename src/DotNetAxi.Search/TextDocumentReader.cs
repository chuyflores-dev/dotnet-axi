using System.Text;

namespace DotNetAxi.Search;

public enum TextDocumentReadStatus
{
    Success,
    Binary,
    Undecodable,
    UnsupportedEncoding,
    Unreadable,
}

public sealed class TextDocumentReadResult
{
    private readonly byte[] _content;

    internal TextDocumentReadResult(
        string? text,
        byte[]? content,
        TextDocumentReadStatus status,
        string? encoding,
        bool hasByteOrderMark)
    {
        Text = text;
        _content = content ?? [];
        ContentAvailable = content is not null;
        Status = status;
        Encoding = encoding;
        HasByteOrderMark = hasByteOrderMark;
    }

    public string? Text { get; }

    public ReadOnlyMemory<byte> Content => _content;

    public bool ContentAvailable { get; }

    public TextDocumentReadStatus Status { get; }

    public string? Encoding { get; }

    public bool HasByteOrderMark { get; }
}

public sealed class TextDocumentReader
{
    public async Task<TextDocumentReadResult> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken)
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

        if (HasUtf32ByteOrderMark(bytes))
        {
            return Result(
                TextDocumentReadStatus.UnsupportedEncoding,
                content: bytes);
        }

        Encoding encoding;
        string encodingName;
        var hasByteOrderMark = false;
        var start = 0;
        if (HasPrefix(bytes, 0xff, 0xfe))
        {
            encoding = new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true,
                throwOnInvalidBytes: true);
            encodingName = "utf-16-le";
            hasByteOrderMark = true;
            start = 2;
        }
        else if (HasPrefix(bytes, 0xfe, 0xff))
        {
            encoding = new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: true,
                throwOnInvalidBytes: true);
            encodingName = "utf-16-be";
            hasByteOrderMark = true;
            start = 2;
        }
        else
        {
            if (bytes.Contains((byte)0))
            {
                return Result(
                    TextDocumentReadStatus.Binary,
                    content: bytes);
            }

            encoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            encodingName = "utf-8";
            if (HasPrefix(bytes, 0xef, 0xbb, 0xbf))
            {
                hasByteOrderMark = true;
                start = 3;
            }
        }

        try
        {
            return Result(
                TextDocumentReadStatus.Success,
                encoding.GetString(bytes, start, bytes.Length - start),
                bytes,
                encodingName,
                hasByteOrderMark);
        }
        catch (DecoderFallbackException)
        {
            return Result(
                TextDocumentReadStatus.Undecodable,
                content: bytes);
        }
    }

    private static bool HasUtf32ByteOrderMark(byte[] bytes) =>
        HasPrefix(bytes, 0xff, 0xfe, 0x00, 0x00)
        || HasPrefix(bytes, 0x00, 0x00, 0xfe, 0xff);

    private static bool HasPrefix(byte[] bytes, params byte[] prefix) =>
        bytes.AsSpan().StartsWith(prefix);

    private static TextDocumentReadResult Result(
        TextDocumentReadStatus status,
        string? text = null,
        byte[]? content = null,
        string? encoding = null,
        bool hasByteOrderMark = false) =>
        new(
            text,
            content,
            status,
            encoding,
            hasByteOrderMark);
}
