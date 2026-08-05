using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DotNetAxi.Structural;

internal static class StructuralCandidateIdentity
{
    public static string Create(
        byte[] source,
        StructuralSourceRange range,
        string text)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(text);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/structural-candidate/v1");
        Append(hash, range.Start.Path.Replace('\\', '/'));
        Append(hash, range.Start.IsExternal ? "external" : "workspace");
        Append(hash, Convert.ToHexStringLower(SHA256.HashData(source)));
        Append(hash, range.Start.Line.ToString(CultureInfo.InvariantCulture));
        Append(hash, range.Start.Column.ToString(CultureInfo.InvariantCulture));
        Append(hash, range.End.Line.ToString(CultureInfo.InvariantCulture));
        Append(hash, range.End.Column.ToString(CultureInfo.InvariantCulture));
        Append(hash, text);
        return "syntax/v1/" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
