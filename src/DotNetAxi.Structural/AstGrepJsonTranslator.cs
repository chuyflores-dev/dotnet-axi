using System.Text;
using System.Text.Json;
using DotNetAxi.Contracts;

namespace DotNetAxi.Structural;

internal static class AstGrepJsonTranslator
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async ValueTask<TranslationResult> TranslateAsync(
        string json,
        string workspaceRoot,
        WorkspaceTraversalPath expectedPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
            cancellationToken.ThrowIfCancellationRequested();
            if (document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                return TranslationResult.Invalid;
            }

            var elements = document.RootElement.EnumerateArray().ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            if (elements.Length == 0)
            {
                return new TranslationResult(true, []);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(
                    expectedPath.FullPath,
                    cancellationToken)
                .ConfigureAwait(false);
            var bomLength = bytes.Length >= 3
                && bytes[0] == 0xef
                && bytes[1] == 0xbb
                && bytes[2] == 0xbf
                    ? 3
                    : 0;
            _ = StrictUtf8.GetString(bytes, bomLength, bytes.Length - bomLength);
            var candidates = new List<StructuralCandidate>(elements.Length);
            foreach (var element in elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReadMatch(
                        element,
                        workspaceRoot,
                        expectedPath,
                        bytes,
                        bomLength,
                        out var candidate))
                {
                    return TranslationResult.Invalid;
                }

                candidates.Add(candidate);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new TranslationResult(true, candidates.AsReadOnly());
        }
        catch (Exception exception)
            when (exception is JsonException
                  or DecoderFallbackException
                  or IOException
                  or UnauthorizedAccessException)
        {
            return TranslationResult.Invalid;
        }
    }

    private static bool TryReadMatch(
        JsonElement element,
        string workspaceRoot,
        WorkspaceTraversalPath expectedPath,
        byte[] bytes,
        int bomLength,
        out StructuralCandidate candidate)
    {
        candidate = null!;
        if (element.ValueKind is not JsonValueKind.Object
            || !TryGetUniqueProperty(element, "text", out var textElement)
            || textElement.ValueKind is not JsonValueKind.String
            || !TryGetUniqueProperty(element, "file", out var fileElement)
            || fileElement.ValueKind is not JsonValueKind.String
            || !TryGetUniqueProperty(element, "range", out var rangeElement)
            || rangeElement.ValueKind is not JsonValueKind.Object
            || !TryGetUniqueProperty(rangeElement, "byteOffset", out var byteOffset)
            || byteOffset.ValueKind is not JsonValueKind.Object
            || !TryGetNonNegativeInt(byteOffset, "start", out var start)
            || !TryGetNonNegativeInt(byteOffset, "end", out var end)
            || end < start
            || !TryReadBackendPosition(rangeElement, "start")
            || !TryReadBackendPosition(rangeElement, "end"))
        {
            return false;
        }

        var reportedFile = fileElement.GetString();
        var matchedText = textElement.GetString();
        if (reportedFile is null
            || matchedText is null
            || !MatchesExpectedPath(
                reportedFile,
                workspaceRoot,
                expectedPath)
            || start < bomLength
            || end > bytes.Length)
        {
            return false;
        }

        string actualText;
        SourceLocation startLocation;
        SourceLocation endLocation;
        try
        {
            actualText = StrictUtf8.GetString(bytes, start, end - start);
            startLocation = LocationAt(
                bytes,
                bomLength,
                start,
                expectedPath);
            endLocation = LocationAt(
                bytes,
                bomLength,
                end,
                expectedPath);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!actualText.Equals(matchedText, StringComparison.Ordinal))
        {
            return false;
        }

        var range = new StructuralSourceRange(startLocation, endLocation);
        candidate = new StructuralCandidate(
            StructuralCandidateIdentity.Create(bytes, range, actualText),
            range,
            actualText);
        return true;
    }

    private static SourceLocation LocationAt(
        byte[] bytes,
        int bomLength,
        int byteOffset,
        WorkspaceTraversalPath path)
    {
        var prefix = StrictUtf8.GetString(
            bytes,
            bomLength,
            byteOffset - bomLength);
        var line = 1;
        var lineStart = 0;
        for (var index = 0; index < prefix.Length; index++)
        {
            if (prefix[index] == '\r')
            {
                if (index + 1 < prefix.Length && prefix[index + 1] == '\n')
                {
                    index++;
                }

                line++;
                lineStart = index + 1;
            }
            else if (prefix[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }

        return new SourceLocation(
            path.RelativePath,
            line,
            prefix.Length - lineStart + 1,
            path.IsExternal);
    }

    private static bool TryReadBackendPosition(
        JsonElement range,
        string name) =>
        TryGetUniqueProperty(range, name, out var position)
        && position.ValueKind is JsonValueKind.Object
        && TryGetNonNegativeInt(position, "line", out _)
        && TryGetNonNegativeInt(position, "column", out _);

    private static bool TryGetNonNegativeInt(
        JsonElement parent,
        string name,
        out int value)
    {
        value = 0;
        return TryGetUniqueProperty(parent, name, out var element)
            && element.ValueKind is JsonValueKind.Number
            && element.TryGetInt32(out value)
            && value >= 0;
    }

    private static bool TryGetUniqueProperty(
        JsonElement parent,
        string name,
        out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in parent.EnumerateObject())
        {
            if (!property.NameEquals(name))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            found = true;
            value = property.Value;
        }

        return found;
    }

    private static bool MatchesExpectedPath(
        string reportedPath,
        string workspaceRoot,
        WorkspaceTraversalPath expectedPath)
    {
        var normalizedReported = reportedPath.Replace('\\', '/');
        var normalizedExpected = expectedPath.RelativePath.Replace('\\', '/');
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (comparer.Equals(normalizedReported, normalizedExpected)
            || (normalizedReported.StartsWith("./", StringComparison.Ordinal)
                && comparer.Equals(normalizedReported[2..], normalizedExpected)))
        {
            return true;
        }

        try
        {
            var fullReported = Path.IsPathFullyQualified(reportedPath)
                ? Path.GetFullPath(reportedPath)
                : Path.GetFullPath(reportedPath, workspaceRoot);
            return comparer.Equals(
                Path.TrimEndingDirectorySeparator(fullReported),
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(expectedPath.FullPath)));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or NotSupportedException
                  or PathTooLongException)
        {
            return false;
        }
    }

    internal sealed record TranslationResult(
        bool IsValid,
        IReadOnlyList<StructuralCandidate> Candidates)
    {
        public static TranslationResult Invalid { get; } = new(false, []);
    }
}
