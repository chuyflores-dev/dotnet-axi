using System.Text;

namespace DotNetAxi.Search;

internal static class TextDocumentEncodingDetector
{
    public static bool HasUnsupportedByteOrderMark(
        ReadOnlySpan<byte> bytes) =>
        HasPrefix(bytes, 0xff, 0xfe, 0x00, 0x00)
        || HasPrefix(bytes, 0x00, 0x00, 0xfe, 0xff);

    public static TextDocumentEncoding Detect(ReadOnlySpan<byte> prefix)
    {
        if (HasPrefix(prefix, 0xff, 0xfe))
        {
            return new TextDocumentEncoding(
                new UnicodeEncoding(false, true, true),
                "utf-16-le",
                PreambleLength: 2,
                DetectNullBytes: false);
        }

        if (HasPrefix(prefix, 0xfe, 0xff))
        {
            return new TextDocumentEncoding(
                new UnicodeEncoding(true, true, true),
                "utf-16-be",
                PreambleLength: 2,
                DetectNullBytes: false);
        }

        return new TextDocumentEncoding(
            new UTF8Encoding(false, true),
            "utf-8",
            HasPrefix(prefix, 0xef, 0xbb, 0xbf) ? 3 : 0,
            DetectNullBytes: true);
    }

    private static bool HasPrefix(
        ReadOnlySpan<byte> bytes,
        byte first,
        byte second) =>
        bytes.Length >= 2 && bytes[0] == first && bytes[1] == second;

    private static bool HasPrefix(
        ReadOnlySpan<byte> bytes,
        byte first,
        byte second,
        byte third) =>
        bytes.Length >= 3
        && bytes[0] == first
        && bytes[1] == second
        && bytes[2] == third;

    private static bool HasPrefix(
        ReadOnlySpan<byte> bytes,
        byte first,
        byte second,
        byte third,
        byte fourth) =>
        bytes.Length >= 4
        && bytes[0] == first
        && bytes[1] == second
        && bytes[2] == third
        && bytes[3] == fourth;
}

internal sealed record TextDocumentEncoding(
    Encoding Value,
    string Name,
    int PreambleLength,
    bool DetectNullBytes);
