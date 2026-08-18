using System.Security.Cryptography;

namespace DotNetAxi.Roslyn;

internal static class SemanticRelationshipFileFingerprint
{
    internal static async ValueTask<string> CreateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return "missing";
        }

        try
        {
            var contents = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(SHA256.HashData(contents));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }
}
