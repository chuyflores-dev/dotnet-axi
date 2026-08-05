using System.Text;

namespace DotNetAxi.Testing;

internal enum PortableRelativePathError
{
    None,
    Required,
    NotPortable,
    InvalidSegment,
    NotNormalized,
    InvalidSegmentEnding,
    WindowsDeviceName,
}

internal static class PortableRelativePath
{
    private static readonly HashSet<string> WindowsDeviceNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "CLOCK$",
        };

    public static bool TryNormalize(
        string? value,
        bool normalizeBackslashes,
        out string normalized) =>
        TryNormalize(
            value,
            normalizeBackslashes,
            out normalized,
            out _);

    public static bool TryNormalize(
        string? value,
        bool normalizeBackslashes,
        out string normalized,
        out PortableRelativePathError error)
    {
        normalized = string.Empty;
        error = PortableRelativePathError.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = PortableRelativePathError.Required;
            return false;
        }

        if (!normalizeBackslashes && value.Contains('\\'))
        {
            error = PortableRelativePathError.NotPortable;
            return false;
        }

        var candidate = normalizeBackslashes
            ? value.Replace('\\', '/')
            : value;
        if (candidate.StartsWith("/", StringComparison.Ordinal)
            || candidate.Contains(':')
            || candidate.Any(static character =>
                char.IsControl(character)
                || character is '*' or '?' or '"' or '<' or '>' or '|'))
        {
            error = PortableRelativePathError.NotPortable;
            return false;
        }

        var segments = candidate.Split('/');
        if (segments.Any(static segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."))
        {
            error = PortableRelativePathError.InvalidSegment;
            return false;
        }

        foreach (var segment in segments)
        {
            if (!segment.IsNormalized(NormalizationForm.FormC))
            {
                error = PortableRelativePathError.NotNormalized;
                return false;
            }

            if (segment[^1] is ' ' or '.')
            {
                error = PortableRelativePathError.InvalidSegmentEnding;
                return false;
            }

            if (IsWindowsDeviceName(segment))
            {
                error = PortableRelativePathError.WindowsDeviceName;
                return false;
            }
        }

        normalized = string.Join('/', segments);
        return true;
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        var extensionSeparator = segment.IndexOf('.');
        var baseName = extensionSeparator < 0
            ? segment
            : segment[..extensionSeparator];
        if (WindowsDeviceNames.Contains(baseName))
        {
            return true;
        }

        return baseName.Length == 4
            && baseName[3] is (>= '1' and <= '9')
                or '\u00b9'
                or '\u00b2'
                or '\u00b3'
            && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith(
                    "LPT",
                    StringComparison.OrdinalIgnoreCase));
    }
}
