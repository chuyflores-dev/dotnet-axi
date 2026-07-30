using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DotNetAxi.Testing;

internal static class FixtureContentHasher
{
    private static readonly byte[] FormatPrefix =
        "dotnet-axi/fixture-content/v1\n"u8.ToArray();

    public static string Compute(
        IEnumerable<FixtureMaterializedFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(FormatPrefix);

        foreach (var file in files.OrderBy(
                     static file => file.RelativePath,
                     StringComparer.Ordinal))
        {
            AppendField(hash, Encoding.UTF8.GetBytes(file.RelativePath));
            AppendField(hash, file.Content);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static async ValueTask<string> ComputeAsync(
        string workspacePath,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        var files = new List<FixtureMaterializedFile>();
        foreach (var relativePath in relativePaths.Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(
                workspacePath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            files.Add(
                new FixtureMaterializedFile(
                    relativePath,
                    await File.ReadAllBytesAsync(path, cancellationToken)));
        }

        return Compute(files);
    }

    private static void AppendField(IncrementalHash hash, byte[] value)
    {
        var length = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(length, value.LongLength);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}

internal sealed record FixtureMaterializedFile(
    string RelativePath,
    byte[] Content);
