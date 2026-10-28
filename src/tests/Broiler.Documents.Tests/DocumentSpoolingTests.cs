using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Broiler.Documents.Tests;

/// <summary>
/// Verifies the bounded spooling capabilities of <see cref="DocumentInput"/> under a host-supplied
/// <see cref="DocumentSpoolPolicy"/>: directory enforcement, quota protection, memory thresholds,
/// seekable replaying, multiple open streams, cleanup on dispose, and crash recovery.
/// </summary>
public sealed class DocumentSpoolingTests : IDisposable
{
    private readonly string _spoolDir;

    public DocumentSpoolingTests()
    {
        _spoolDir = Path.Combine(Path.GetTempPath(), "broiler-spool-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_spoolDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_spoolDir))
                Directory.Delete(_spoolDir, recursive: true);
        }
        catch { }
    }

    private static byte[] GenerateBytes(int count)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    /// <summary>A forward-only, non-seekable stream simulating a pipe or network socket.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public int TotalBytesRead { get; private set; }
        public bool IsDisposed { get; private set; }

        public override bool CanRead => !IsDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            int read = _inner.Read(buffer, offset, count);
            TotalBytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            TotalBytesRead += read;
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void Policy_Requires_Explicit_Existing_Directory_And_Positive_Quota()
    {
        Assert.Throws<ArgumentException>(() => new DocumentSpoolPolicy(""));
        Assert.Throws<ArgumentException>(() => new DocumentSpoolPolicy("   "));
        Assert.Throws<DirectoryNotFoundException>(() =>
            new DocumentSpoolPolicy(Path.Combine(_spoolDir, "non-existent-subfolder")));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DocumentSpoolPolicy(_spoolDir, maxSpoolBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DocumentSpoolPolicy(_spoolDir, maxSpoolBytes: -100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: -1));

        var policy = new DocumentSpoolPolicy(_spoolDir, maxSpoolBytes: 1024 * 1024, memoryThresholdBytes: 4096);
        Assert.Equal(_spoolDir, policy.DirectoryPath);
        Assert.Equal(1024 * 1024, policy.MaxSpoolBytes);
        Assert.Equal(4096, policy.MemoryThresholdBytes);
        Assert.Equal(DocumentSpoolPolicy.DefaultFilePrefix, policy.FilePrefix);
        Assert.True(policy.DeleteOnDispose);
    }

    [Fact]
    public void Small_Source_Under_Memory_Threshold_Stays_In_Memory_Without_Disk_File()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Hello, memory-only spooling!");
        var policy = new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: 1024);

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        Assert.True(input.CanSeek);

        using Stream stream = input.OpenStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        Assert.Equal(payload, ms.ToArray());
        Assert.Equal(payload.Length, input.KnownLength);

        // Assert no spool files were written to disk
        string[] files = Directory.GetFiles(_spoolDir, "*.spool");
        Assert.Empty(files);
    }

    [Fact]
    public void Large_Source_Exceeding_Memory_Threshold_Spools_To_Disk_File()
    {
        byte[] payload = GenerateBytes(8192); // 8 KB
        var policy = new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: 1024); // 1 KB threshold

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        using Stream stream = input.OpenStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        Assert.Equal(payload, ms.ToArray());
        Assert.Equal(payload.Length, input.KnownLength);

        // Verify a spool file exists on disk
        string[] files = Directory.GetFiles(_spoolDir, "*.spool");
        Assert.Single(files);
        Assert.Equal(payload.Length, new FileInfo(files[0]).Length);
    }

    [Fact]
    public void Spooled_Input_Allows_Seeking_And_Reports_Known_Length()
    {
        byte[] payload = GenerateBytes(16384);
        var policy = new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: 512);

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        using Stream stream = input.OpenStream();

        Assert.True(stream.CanSeek);
        Assert.Equal(payload.Length, stream.Length);

        // Seek forward
        stream.Seek(100, SeekOrigin.Begin);
        Assert.Equal(100, stream.Position);
        Assert.Equal(payload[100], (byte)stream.ReadByte());

        // Seek from current
        stream.Seek(50, SeekOrigin.Current);
        Assert.Equal(151, stream.Position);
        Assert.Equal(payload[151], (byte)stream.ReadByte());

        // Seek from end
        stream.Seek(-10, SeekOrigin.End);
        Assert.Equal(payload.Length - 10, stream.Position);
        Assert.Equal(payload[^10], (byte)stream.ReadByte());
    }

    [Fact]
    public void Spooled_Input_Supports_Multiple_Independent_OpenStream_Instances()
    {
        byte[] payload = GenerateBytes(10000);
        var policy = new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: 100);

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        using Stream stream1 = input.OpenStream();
        using Stream stream2 = input.OpenStream();

        stream1.Seek(500, SeekOrigin.Begin);
        stream2.Seek(1000, SeekOrigin.Begin);

        Assert.Equal(500, stream1.Position);
        Assert.Equal(1000, stream2.Position);

        byte[] b1 = new byte[100];
        byte[] b2 = new byte[100];

        int read1 = stream1.Read(b1, 0, 100);
        int read2 = stream2.Read(b2, 0, 100);

        Assert.Equal(100, read1);
        Assert.Equal(100, read2);
        Assert.Equal(payload.AsSpan(500, 100).ToArray(), b1);
        Assert.Equal(payload.AsSpan(1000, 100).ToArray(), b2);
    }

    [Fact]
    public void Quota_Exceeded_Throws_LimitExceededException_And_Cleans_Up_Partial_File()
    {
        byte[] payload = GenerateBytes(20000);
        var policy = new DocumentSpoolPolicy(_spoolDir, maxSpoolBytes: 5000, memoryThresholdBytes: 500);

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        var ex = Assert.Throws<DocumentLimitExceededException>(() => input.OpenStream());
        Assert.Contains("5000", ex.Message);

        // Verify partial file was cleaned up immediately
        string[] files = Directory.GetFiles(_spoolDir, "*.spool");
        Assert.Empty(files);
    }

    [Fact]
    public void Peek_Before_OpenStream_Only_Buffers_Probe_Prefix()
    {
        byte[] payload = GenerateBytes(10000);
        var policy = new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: 500);

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        // Peeking 32 bytes should only consume leading bytes from source
        ReadOnlyMemory<byte> peek = input.Peek(32);
        Assert.Equal(32, peek.Length);
        Assert.Equal(payload.AsSpan(0, 32).ToArray(), peek.ToArray());

        // We only requested 32 bytes, so full stream should not be consumed yet
        Assert.True(source.TotalBytesRead < payload.Length);
        Assert.Empty(Directory.GetFiles(_spoolDir, "*.spool"));

        // Now open stream and verify the full payload matches seamlessly
        using Stream stream = input.OpenStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public void Dispose_Cleans_Up_Spool_File_On_Disk()
    {
        byte[] payload = GenerateBytes(10000);
        var policy = new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: 500);

        using var source = new ForwardOnlyStream(payload);
        DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        using (Stream stream = input.OpenStream())
        {
            Assert.NotEmpty(Directory.GetFiles(_spoolDir, "*.spool"));
        }

        input.Dispose();

        // The spool file must be gone
        Assert.Empty(Directory.GetFiles(_spoolDir, "*.spool"));
    }

    [Fact]
    public void LeaveOpen_Honors_Caller_Stream_Ownership()
    {
        byte[] payload = GenerateBytes(200);
        var policy = new DocumentSpoolPolicy(_spoolDir);

        // Default: leaveOpen == true
        var source1 = new ForwardOnlyStream(payload);
        using (DocumentInput input = DocumentInput.SpoolFromStream(source1, policy))
        {
            using Stream s = input.OpenStream();
        }
        Assert.False(source1.IsDisposed);
        source1.Dispose();

        // Explicit leaveOpen == false
        var source2 = new ForwardOnlyStream(payload);
        using (DocumentInput input = DocumentInput.SpoolFromStream(source2, policy, leaveOpen: false))
        {
            using Stream s = input.OpenStream();
        }
        Assert.True(source2.IsDisposed);
    }

    [Fact]
    public void CleanOrphanedFiles_Purges_Stale_Files_And_Leaves_Recent_Files()
    {
        var policy = new DocumentSpoolPolicy(_spoolDir);

        string staleFile = Path.Combine(_spoolDir, $"{DocumentSpoolPolicy.DefaultFilePrefix}stale.spool");
        string freshFile = Path.Combine(_spoolDir, $"{DocumentSpoolPolicy.DefaultFilePrefix}fresh.spool");

        File.WriteAllText(staleFile, "stale content");
        File.WriteAllText(freshFile, "fresh content");

        // Backdate stale file to 3 hours ago
        File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(freshFile, DateTime.UtcNow);

        int cleaned = policy.CleanOrphanedFiles(TimeSpan.FromHours(1));

        Assert.Equal(1, cleaned);
        Assert.False(File.Exists(staleFile));
        Assert.True(File.Exists(freshFile));

        File.Delete(freshFile);
    }

    [Fact]
    public async Task SpoolFromStreamAsync_Spools_Asynchronously()
    {
        byte[] payload = GenerateBytes(12000);
        var policy = new DocumentSpoolPolicy(_spoolDir, memoryThresholdBytes: 1000);

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = await DocumentInput.SpoolFromStreamAsync(source, policy);

        Assert.True(input.CanSeek);
        Assert.Equal(payload.Length, input.KnownLength);

        ReadOnlyMemory<byte> materialized = await input.MaterializeAsync(20000);
        Assert.Equal(payload, materialized.ToArray());
    }

    [Fact]
    public void Materialize_Throws_When_Exceeding_Ceiling()
    {
        byte[] payload = GenerateBytes(5000);
        var policy = new DocumentSpoolPolicy(_spoolDir);

        using var source = new ForwardOnlyStream(payload);
        using DocumentInput input = DocumentInput.SpoolFromStream(source, policy);

        Assert.Throws<DocumentException>(() => input.Materialize(2000));
    }
}
