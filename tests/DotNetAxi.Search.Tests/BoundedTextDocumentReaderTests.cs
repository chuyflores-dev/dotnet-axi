using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Search;

namespace DotNetAxi.Search.Tests;

public sealed class BoundedTextDocumentReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotnet-axi-bounded-reader-tests",
        Guid.NewGuid().ToString("N"));

    public BoundedTextDocumentReaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Bounded_read_uses_fixed_chunks_and_retains_only_limits()
    {
        const long length = 5 * 1024 * 1024;
        await using var stream = new RepeatingByteStream(length, (byte)'x');

        var result = await BoundedTextDocumentReader.ReadAsync(
            stream,
            maximumCharacters: 10,
            headerCharacters: 32,
            CancellationToken.None);

        Assert.Equal(TextDocumentReadStatus.Success, result.Status);
        Assert.Equal(new string('x', 10), result.Preview);
        Assert.Equal(10, result.IncludedCharacters);
        Assert.Equal(length, result.TotalCharacters);
        Assert.Equal(length - 10, result.OmittedCharacters);
        Assert.True(result.Truncated);
        Assert.Equal(32, result.Header!.Length);
        Assert.Equal(length, result.ByteCount);
        Assert.Equal(64 * 1024, stream.MaximumReadSize);
        Assert.Matches("^[a-f0-9]{64}$", result.ContentHash!);
    }

    [Fact]
    public void Bounded_allocations_do_not_scale_with_document_length()
    {
        _ = MeasureAllocatedBytes(128 * 1024);
        var oneMiB = MeasureAllocatedBytes(1024 * 1024);
        var thirtyTwoMiB = MeasureAllocatedBytes(32 * 1024 * 1024);

        Assert.True(
            thirtyTwoMiB <= oneMiB + 128 * 1024,
            $"Expected fixed-memory streaming, but allocations grew from "
            + $"{oneMiB:N0} to {thirtyTwoMiB:N0} bytes.");
    }

    [Fact]
    public async Task Multibyte_scalar_split_across_read_buffers_is_counted_once()
    {
        var contents = new string('x', 65_538) + "😀z";
        var bytes = Encoding.UTF8.GetBytes(contents);
        var path = await WriteAsync("Boundary.cs", bytes);

        var result = await new BoundedTextDocumentReader().ReadAsync(
            path,
            maximumCharacters: 4,
            headerCharacters: 4);

        Assert.Equal(TextDocumentReadStatus.Success, result.Status);
        Assert.Equal("xxxx", result.Preview);
        Assert.Equal(65_540, result.TotalCharacters);
        Assert.Equal(65_536, result.OmittedCharacters);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            result.ContentHash);
    }

    [Fact]
    public async Task Unbounded_read_retains_complete_unicode_text()
    {
        const string contents = "Hello 世界 👋";
        var encoding = new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: true,
            throwOnInvalidBytes: true);
        byte[] bytes =
        [
            .. encoding.GetPreamble(),
            .. encoding.GetBytes(contents),
        ];
        var path = await WriteAsync("Full.cs", bytes);

        var result = await new BoundedTextDocumentReader().ReadAsync(
            path,
            maximumCharacters: null,
            headerCharacters: 4096);

        Assert.Equal(TextDocumentReadStatus.Success, result.Status);
        Assert.Equal("utf-16-be", result.Encoding);
        Assert.True(result.HasByteOrderMark);
        Assert.Equal(contents, result.Preview);
        Assert.Equal(contents.EnumerateRunes().Count(), result.TotalCharacters);
        Assert.Equal(result.TotalCharacters, result.IncludedCharacters);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Pre_cancelled_read_does_not_return_partial_evidence()
    {
        var path = await WriteAsync(
            "Cancelled.cs",
            Encoding.UTF8.GetBytes(new string('x', 1024)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BoundedTextDocumentReader().ReadAsync(
                path,
                maximumCharacters: 10,
                headerCharacters: 32,
                cancellation.Token));
    }

    private async Task<string> WriteAsync(string name, byte[] contents)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, contents);
        return path;
    }

    private static long MeasureAllocatedBytes(long length)
    {
        using var stream = new RepeatingByteStream(length, (byte)'x');
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = BoundedTextDocumentReader.ReadAsync(
                stream,
                maximumCharacters: 10,
                headerCharacters: 32,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10, result.Preview!.Length);
        return allocated;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RepeatingByteStream(long length, byte value) : Stream
    {
        private long _position;

        public int MaximumReadSize { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaximumReadSize = Math.Max(MaximumReadSize, buffer.Length);
            var count = (int)Math.Min(buffer.Length, length - _position);
            buffer.Span[..count].Fill(value);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
