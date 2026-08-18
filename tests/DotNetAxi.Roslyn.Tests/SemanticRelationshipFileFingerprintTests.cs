using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Roslyn;

namespace DotNetAxi.Roslyn.Tests;

public sealed class SemanticRelationshipFileFingerprintTests
{
    [Fact]
    public async Task Create_async_hashes_file_bytes_into_bounded_sha256_fingerprint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dnaxi-fingerprint-{Guid.NewGuid():N}.txt");
        var contents = Encoding.UTF8.GetBytes(new string('x', 4096));

        try
        {
            await File.WriteAllBytesAsync(path, contents);

            var fingerprint = await SemanticRelationshipFileFingerprint.CreateAsync(path);

            Assert.Equal(64, fingerprint.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(contents)), fingerprint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Create_async_reports_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dnaxi-missing-{Guid.NewGuid():N}.txt");

        var fingerprint = await SemanticRelationshipFileFingerprint.CreateAsync(path);

        Assert.Equal("missing", fingerprint);
    }
}
