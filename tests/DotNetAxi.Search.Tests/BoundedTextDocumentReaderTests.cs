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

    [Theory]
    [InlineData("one\ntwo\nthree", 2, 2, "two\n", 3)]
    [InlineData("one\r\ntwo\r\nthree", 2, 2, "two\r\n", 3)]
    [InlineData("one\ntwo\nthree", 3, 3, "three", 3)]
    public async Task Line_spans_are_one_based_inclusive_and_preserve_content(
        string contents,
        int startLine,
        int endLine,
        string expected,
        long expectedLineCount)
    {
        var path = await WriteAsync(
            "Lines.cs",
            Encoding.UTF8.GetBytes(contents));

        var result = await new BoundedTextDocumentReader().ReadAsync(
            path,
            maximumCharacters: null,
            headerCharacters: 32,
            startLine,
            endLine);

        Assert.Equal(TextDocumentReadStatus.Success, result.Status);
        Assert.Equal(expected, result.Preview);
        Assert.Equal(expected.EnumerateRunes().Count(), result.TotalCharacters);
        Assert.Equal(expected.EnumerateRunes().Count(), result.IncludedCharacters);
        Assert.Equal(expectedLineCount, result.TotalLines);
        Assert.Equal(startLine, result.ActualStartLine);
        Assert.Equal(endLine, result.ActualEndLine);
    }

    [Fact]
    public async Task Character_budget_applies_only_to_the_selected_span()
    {
        var path = await WriteAsync(
            "Unicode.cs",
            Encoding.UTF8.GetBytes("ignored\n😀界z\ntail"));

        var result = await new BoundedTextDocumentReader().ReadAsync(
            path,
            maximumCharacters: 2,
            headerCharacters: 32,
            startLine: 2,
            endLine: 2);

        Assert.Equal("😀界", result.Preview);
        Assert.Equal(2, result.IncludedCharacters);
        Assert.Equal(4, result.TotalCharacters);
        Assert.Equal(2, result.OmittedCharacters);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.TotalLines);
        Assert.Equal(2, result.ActualStartLine);
        Assert.Equal(2, result.ActualEndLine);
    }

    [Fact]
    public async Task Multi_line_truncation_reports_only_preview_line_coverage()
    {
        var path = await WriteAsync(
            "Truncated.cs",
            Encoding.UTF8.GetBytes("ignored\nline two\nline three\nline four"));

        var result = await new BoundedTextDocumentReader().ReadAsync(
            path,
            maximumCharacters: 12,
            headerCharacters: 32,
            startLine: 2,
            endLine: 4);

        Assert.Equal("line two\nlin", result.Preview);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.ActualStartLine);
        Assert.Equal(3, result.ActualEndLine);
    }

    [Fact]
    public async Task Zero_character_budget_has_no_actual_line_for_non_empty_span()
    {
        var path = await WriteAsync(
            "Zero.cs",
            Encoding.UTF8.GetBytes("one\ntwo"));

        var result = await new BoundedTextDocumentReader().ReadAsync(
            path,
            maximumCharacters: 0,
            headerCharacters: 32,
            startLine: 2,
            endLine: 2);

        Assert.Equal(string.Empty, result.Preview);
        Assert.True(result.Truncated);
        Assert.Null(result.ActualStartLine);
        Assert.Null(result.ActualEndLine);
    }

    [Fact]
    public async Task Empty_document_has_one_empty_line()
    {
        var path = await WriteAsync("Empty.cs", []);

        var result = await new BoundedTextDocumentReader().ReadAsync(
            path,
            maximumCharacters: 10,
            headerCharacters: 32,
            startLine: 1,
            endLine: 1);

        Assert.Equal(TextDocumentReadStatus.Success, result.Status);
        Assert.Equal(string.Empty, result.Preview);
        Assert.Equal(0, result.TotalCharacters);
        Assert.Equal(1, result.TotalLines);
        Assert.Equal(1, result.ActualStartLine);
        Assert.Equal(1, result.ActualEndLine);
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
