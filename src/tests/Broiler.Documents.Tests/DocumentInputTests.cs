namespace Broiler.Documents.Tests;

/// <summary>
/// The property that matters: a source can be probed and then read once, whether
/// or not it can seek, without losing the probed bytes and without buffering the
/// whole thing.
/// </summary>
public sealed class DocumentInputTests
{
    private static byte[] Bytes(string text) => System.Text.Encoding.ASCII.GetBytes(text);

    /// <summary>A stream that refuses to seek, like a pipe or a network body.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;

        public ForwardOnlyStream(byte[] data) => _inner = new MemoryStream(data);

        public int ReadCalls { get; private set; }

        public override bool CanRead => true;

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
            ReadCalls++;
            return _inner.Read(buffer, offset, count);
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void A_Non_Seekable_Source_Can_Be_Probed_And_Then_Read_In_Full()
    {
        using var source = new ForwardOnlyStream(Bytes("HEADER-then-the-body"));
        using DocumentInput input = DocumentInput.FromStream(source);

        Assert.Equal("HEADER", System.Text.Encoding.ASCII.GetString(input.Peek(6).Span));

        using Stream stream = input.OpenStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);

        // The probed prefix is replayed, not lost.
        Assert.Equal("HEADER-then-the-body", reader.ReadToEnd());
    }

    [Fact]
    public void Peeking_Twice_Returns_The_Same_Bytes_Without_Re_Reading()
    {
        using var source = new ForwardOnlyStream(Bytes("abcdefghij"));
        using DocumentInput input = DocumentInput.FromStream(source);

        ReadOnlyMemory<byte> first = input.Peek(4);
        int callsAfterFirst = source.ReadCalls;
        ReadOnlyMemory<byte> second = input.Peek(4);

        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal(callsAfterFirst, source.ReadCalls);
    }

    [Fact]
    public void A_Peek_Longer_Than_The_Source_Returns_What_Exists()
    {
        using var source = new ForwardOnlyStream(Bytes("short"));
        using DocumentInput input = DocumentInput.FromStream(source);

        Assert.Equal(5, input.Peek(4096).Length);
    }

    [Fact]
    public void A_Seekable_Source_Rewinds_Instead_Of_Buffering()
    {
        using var source = new MemoryStream(Bytes("HEADER-then-the-body"));
        using DocumentInput input = DocumentInput.FromStream(source);

        input.Peek(6);
        using Stream stream = input.OpenStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);

        Assert.Equal("HEADER-then-the-body", reader.ReadToEnd());
    }

    [Fact]
    public void Materialize_Refuses_A_Source_Past_Its_Ceiling()
    {
        using var source = new ForwardOnlyStream(new byte[4096]);
        using DocumentInput input = DocumentInput.FromStream(source);

        Assert.Throws<DocumentException>(() => input.Materialize(1024));
    }

    [Fact]
    public void A_Known_Length_Rejects_An_Oversized_Source_Before_Reading_It()
    {
        using var source = new MemoryStream(new byte[4096]);
        using DocumentInput input = DocumentInput.FromStream(source);

        Assert.Equal(4096, input.KnownLength);
        Assert.Throws<DocumentException>(() => input.Materialize(1024));

        // Nothing was consumed on the way to the refusal.
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void A_Non_Seekable_Source_Reports_No_Known_Length()
    {
        using var source = new ForwardOnlyStream(Bytes("body"));
        using DocumentInput input = DocumentInput.FromStream(source);

        Assert.Null(input.KnownLength);
        Assert.False(input.CanSeek);
    }

    [Fact]
    public void Materialize_Includes_The_Probed_Prefix()
    {
        using var source = new ForwardOnlyStream(Bytes("PREFIXbody"));
        using DocumentInput input = DocumentInput.FromStream(source);

        input.Peek(6);

        Assert.Equal("PREFIXbody", System.Text.Encoding.ASCII.GetString(input.Materialize(1024).Span));
    }

    [Fact]
    public async Task MaterializeAsync_Matches_The_Synchronous_Result()
    {
        using var source = new ForwardOnlyStream(Bytes("PREFIXbody"));
        using DocumentInput input = DocumentInput.FromStream(source);
        input.Peek(6);

        ReadOnlyMemory<byte> bytes = await input.MaterializeAsync(1024);

        Assert.Equal("PREFIXbody", System.Text.Encoding.ASCII.GetString(bytes.Span));
    }

    [Fact]
    public void The_Caller_Keeps_Its_Stream_By_Default()
    {
        var source = new MemoryStream(Bytes("body"));
        using (DocumentInput input = DocumentInput.FromStream(source))
        {
            using Stream stream = input.OpenStream();
            stream.ReadByte();
        }

        // Disposing the input, and the stream it handed out, left the caller's
        // stream usable.
        source.Position = 0;
        Assert.Equal((byte)'b', (byte)source.ReadByte());
        source.Dispose();
    }

    [Fact]
    public void Ownership_Can_Be_Transferred_Explicitly()
    {
        var source = new MemoryStream(Bytes("body"));
        using (DocumentInput.FromStream(source, leaveOpen: false))
        {
        }

        Assert.Throws<ObjectDisposedException>(() => source.ReadByte());
    }

    [Fact]
    public void A_Memory_Input_Neither_Copies_Nor_Consumes()
    {
        byte[] data = Bytes("in memory");
        using DocumentInput input = DocumentInput.FromBytes(data);

        Assert.Equal(data.Length, input.KnownLength);
        Assert.True(input.CanSeek);
        Assert.Equal("in ", System.Text.Encoding.ASCII.GetString(input.Peek(3).Span));
        Assert.Equal(data, input.Materialize(1024).ToArray());
        Assert.Equal(data, input.Materialize(1024).ToArray());
    }

    [Fact]
    public void Using_A_Disposed_Input_Throws_Rather_Than_Misbehaving()
    {
        DocumentInput input = DocumentInput.FromBytes(Bytes("x"));
        input.Dispose();

        Assert.Throws<ObjectDisposedException>(() => input.Peek(1));
        Assert.Throws<ObjectDisposedException>(() => input.OpenStream());
        Assert.Throws<ObjectDisposedException>(() => input.Materialize(16));
    }
}
