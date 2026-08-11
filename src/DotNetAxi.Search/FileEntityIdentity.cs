using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public static class FileEntityIdentity
{
    public static string Create(WorkspaceTraversalPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Create(path.RelativePath, path.IsExternal);
    }

    public static string Create(string path, bool isExternal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/file-match/v1");
        Append(hash, new SourceLocation(path, 1, 1, isExternal).Path);
        Append(hash, isExternal ? "external" : "workspace");
        return "file/v1/" + Convert.ToHexStringLower(
            hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
