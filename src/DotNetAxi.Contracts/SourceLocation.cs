namespace DotNetAxi.Contracts;

public sealed record SourceLocation
{
    public SourceLocation(
        string path,
        int line,
        int column,
        bool isExternal = false)
    {
        Path = NormalizePath(path);
        if (!isExternal && IsLexicallyExternal(Path))
        {
            throw new ArgumentException(
                "An external source path must be labeled external.",
                nameof(isExternal));
        }

        Line = OneBased(line, nameof(line));
        Column = OneBased(column, nameof(column));
        IsExternal = isExternal;
    }

    public string Path { get; }

    public int Line { get; }

    public int Column { get; }

    public bool IsExternal { get; }

    public static SourceLocation FromZeroBasedUtf16(
        string path,
        int zeroBasedLine,
        int zeroBasedColumn,
        bool isExternal = false) =>
        new(
            path,
            ToOneBased(zeroBasedLine, nameof(zeroBasedLine)),
            ToOneBased(zeroBasedColumn, nameof(zeroBasedColumn)),
            isExternal);

    private static string NormalizePath(string path)
    {
        var value = ContractGuards.RequiredText(path, nameof(path))
            .Replace('\\', '/');
        if (value.StartsWith("/", StringComparison.Ordinal)
            || IsWindowsDriveQualified(value))
        {
            throw new ArgumentException(
                "Source locations require a workspace-relative path.",
                nameof(path));
        }

        var segments = new List<string>();
        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == ".."
                && segments.Count > 0
                && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException(
                "Source locations require a file path.",
                nameof(path));
        }

        return string.Join('/', segments);
    }

    private static bool IsWindowsDriveQualified(string path) =>
        path.Length >= 2
        && char.IsAsciiLetter(path[0])
        && path[1] == ':';

    private static bool IsLexicallyExternal(string path) =>
        path.Equals("..", StringComparison.Ordinal)
        || path.StartsWith("../", StringComparison.Ordinal);

    private static int OneBased(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Source lines and UTF-16 columns are one-based.");
        }

        return value;
    }

    private static int ToOneBased(int value, string parameterName)
    {
        if (value < 0 || value == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A zero-based source coordinate must fit after conversion to one-based form.");
        }

        return value + 1;
    }
}
