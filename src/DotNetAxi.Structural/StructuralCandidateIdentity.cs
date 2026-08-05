using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DotNetAxi.Structural;

internal static class StructuralCandidateIdentity
{
    public static string Create(
        string queryKind,
        string queryIdentity,
        string contentHash,
        string relativePath,
        bool isExternal,
        int spanStart,
        int spanLength,
        string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentNullException.ThrowIfNull(text);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/roslyn-syntax-candidate/v1");
        Append(hash, queryKind);
        Append(hash, queryIdentity);
        Append(hash, relativePath.Replace('\\', '/'));
        Append(hash, isExternal ? "external" : "workspace");
        Append(hash, contentHash);
        Append(hash, spanStart.ToString(CultureInfo.InvariantCulture));
        Append(hash, spanLength.ToString(CultureInfo.InvariantCulture));
        Append(hash, text);
        return "syntax/v1/" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static void Append(IncrementalHash hash, string value) =>
        Append(hash, Encoding.UTF8.GetBytes(value));

    public static void Append(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
