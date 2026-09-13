namespace Broiler.Documents.Tests;

public sealed class MemoryInputStreamTests
{
    [Fact]
    public void Seeking_Beyond_End_Preserves_Position_And_Can_Return_To_Content()
    {
        using var input = DocumentInput.FromBytes(new byte[] { 10, 20, 30 });
        using Stream stream = input.OpenStream();
        Assert.Equal(8, stream.Seek(5, SeekOrigin.End));
        Assert.Equal(8, stream.Position);
        Assert.Equal(-1, stream.ReadByte());
        stream.Position = long.MaxValue;
        Assert.Equal(0, stream.Read(new byte[1]));
        Assert.Equal(long.MaxValue, stream.Position);
        Assert.Equal(1, stream.Seek(1, SeekOrigin.Begin));
        Assert.Equal(20, stream.ReadByte());
    }

    [Fact]
    public void Invalid_Seeks_Do_Not_Change_Position()
    {
        using var input = DocumentInput.FromBytes(new byte[] { 1, 2, 3 });
        using Stream stream = input.OpenStream();
        stream.Position = 2;
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
        Assert.Throws<IOException>(() => stream.Seek(-3, SeekOrigin.Current));
        Assert.Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)99));
        Assert.Throws<IOException>(() => stream.Seek(long.MaxValue, SeekOrigin.Current));
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void Disposing_One_Stream_Leaves_The_Input_And_Other_Streams_Usable()
    {
        using var input = DocumentInput.FromBytes(new byte[] { 42 });
        Stream closed = input.OpenStream();
        using Stream open = input.OpenStream();
        closed.Dispose();
        closed.Dispose();
        Assert.False(closed.CanRead);
        Assert.False(closed.CanSeek);
        Assert.False(closed.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => closed.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => closed.Seek(0, SeekOrigin.Begin));
        Assert.Throws<ObjectDisposedException>(() => closed.Length);
        Assert.Throws<ObjectDisposedException>(() => closed.Position);
        Assert.Throws<ObjectDisposedException>(() => closed.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => closed.Flush());
        Assert.Equal(42, open.ReadByte());
        Assert.Equal(42, input.Peek(1).Span[0]);
    }

    [Fact]
    public void Read_Validates_Array_Arguments_And_Uses_The_Memory_Slice()
    {
        byte[] bytes = [9, 1, 2, 9];
        using var input = DocumentInput.FromBytes(bytes.AsMemory(1, 2));
        using Stream stream = input.OpenStream();
        Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[1], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[1], 0, 2));
        Assert.Equal(0, stream.Position);
        bytes[1] = 7;
        Assert.Equal(7, stream.ReadByte());
        Assert.Equal(2, stream.ReadByte());
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public async Task Cancelled_Async_Read_Does_Not_Consume_Content()
    {
        using var input = DocumentInput.FromBytes(new byte[] { 7 });
        using Stream stream = input.OpenStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            int read = await stream.ReadAsync(new byte[1].AsMemory(), new CancellationToken(true));
            Assert.Equal(1, read);
        });
        Assert.Equal(0, stream.Position);
        Assert.Equal(7, stream.ReadByte());
    }
}
