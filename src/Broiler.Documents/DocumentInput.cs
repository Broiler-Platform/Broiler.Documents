using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Documents;

/// <summary>
/// A document source that can be probed and then read once, without losing the
/// probed bytes and without copying the whole source twice.
/// </summary>
/// <remarks>
/// <para>
/// Selecting a codec means reading a prefix; reading the document means reading
/// that same prefix again. On a seekable stream that is a rewind. On a network
/// stream, a pipe, or a browser upload it is not, and the naive fix — buffer the
/// entire source before probing — turns a bounded read into an unbounded one.
/// <see cref="DocumentInput"/> holds the probed prefix and replays it ahead of
/// the remaining source, so a non-seekable source can be probed and read exactly
/// once (PDF roadmap §6.1).
/// </para>
/// <para>
/// <b>Ownership is explicit.</b> An input created over a caller's stream does not
/// close it unless the caller says so. Disposing an input never disposes a buffer
/// the caller still owns.
/// </para>
/// <para>
/// <b>There is no ambient spooling.</b> Materialization is memory-only and always
/// bounded by an explicit ceiling; nothing here opens a temporary file, consults
/// a temp directory, or writes to disk. That keeps the type usable in a
/// memory-only WebAssembly host, and it keeps document bytes from landing in a
/// location with no agreed retention or privacy policy. A host that wants
/// spooling supplies it above this type, with its own directory, quota,
/// permission, cleanup, and crash-recovery policy.
/// </para>
/// </remarks>
public abstract class DocumentInput : IDisposable
{
    private bool _disposed;

    /// <summary>The source length when it is known without consuming the source.</summary>
    /// <remarks>
    /// Null means "not known cheaply", not "empty". A caller enforcing a size
    /// limit must still bound its read; a known length only lets it reject early.
    /// </remarks>
    public abstract long? KnownLength { get; }

    /// <summary>True when the underlying source can be re-read without buffering.</summary>
    public abstract bool CanSeek { get; }

    /// <summary>
    /// Returns up to <paramref name="byteCount"/> leading bytes, buffering them so
    /// a later <see cref="OpenStream"/> or <see cref="Materialize"/> still sees
    /// them. Repeated calls are cheap and return the same bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Peek(int byteCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        return byteCount == 0 ? ReadOnlyMemory<byte>.Empty : PeekCore(byteCount);
    }

    /// <summary>
    /// Opens the whole source from its first byte, including any bytes already
    /// handed out by <see cref="Peek"/>. The returned stream is owned by the
    /// caller and disposing it does not dispose this input.
    /// </summary>
    public Stream OpenStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return OpenStreamCore();
    }

    /// <summary>
    /// Reads the whole source into memory, refusing to exceed
    /// <paramref name="maxBytes"/>.
    /// </summary>
    /// <exception cref="DocumentException">
    /// The source is longer than <paramref name="maxBytes"/>. The ceiling is
    /// enforced while reading, so an oversized source is refused rather than
    /// buffered and then measured.
    /// </exception>
    public ReadOnlyMemory<byte> Materialize(long maxBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        if (KnownLength is { } length && length > maxBytes)
            throw TooLarge(maxBytes);

        return MaterializeCore(maxBytes);
    }

    /// <summary>The asynchronous form of <see cref="Materialize"/>.</summary>
    public ValueTask<ReadOnlyMemory<byte>> MaterializeAsync(long maxBytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        if (KnownLength is { } length && length > maxBytes)
            throw TooLarge(maxBytes);

        return MaterializeAsyncCore(maxBytes, cancellationToken);
    }

    protected abstract ReadOnlyMemory<byte> PeekCore(int byteCount);

    protected abstract Stream OpenStreamCore();

    protected abstract ReadOnlyMemory<byte> MaterializeCore(long maxBytes);

    protected abstract ValueTask<ReadOnlyMemory<byte>> MaterializeAsyncCore(long maxBytes, CancellationToken cancellationToken);

    /// <summary>An input over bytes the caller already holds. Nothing is copied.</summary>
    public static DocumentInput FromBytes(ReadOnlyMemory<byte> bytes) => new MemoryDocumentInput(bytes);

    /// <summary>
    /// An input over a stream. <paramref name="leaveOpen"/> defaults to true: the
    /// caller opened the stream and keeps it.
    /// </summary>
    public static DocumentInput FromStream(Stream source, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("A document input needs a readable stream.", nameof(source));
        return new StreamDocumentInput(source, leaveOpen);
    }

    internal static DocumentException TooLarge(long maxBytes) =>
        new($"The document source is larger than the {maxBytes}-byte limit for this read.");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    // ---- implementations ------------------------------------------------------

    private sealed class MemoryDocumentInput : DocumentInput
    {
        private readonly ReadOnlyMemory<byte> _bytes;

        public MemoryDocumentInput(ReadOnlyMemory<byte> bytes) => _bytes = bytes;

        public override long? KnownLength => _bytes.Length;

        public override bool CanSeek => true;

        protected override ReadOnlyMemory<byte> PeekCore(int byteCount) =>
            _bytes[..Math.Min(byteCount, _bytes.Length)];

        protected override Stream OpenStreamCore() => new ReadOnlyMemoryStream(_bytes);

        protected override ReadOnlyMemory<byte> MaterializeCore(long maxBytes) =>
            _bytes.Length > maxBytes ? throw TooLarge(maxBytes) : _bytes;

        protected override ValueTask<ReadOnlyMemory<byte>> MaterializeAsyncCore(long maxBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(MaterializeCore(maxBytes));
        }
    }

    private sealed class StreamDocumentInput : DocumentInput
    {
        private readonly Stream _source;
        private readonly bool _leaveOpen;
        private byte[] _prefix = [];
        private int _prefixLength;
        private int _prefixConsumed;

        public StreamDocumentInput(Stream source, bool leaveOpen)
        {
            _source = source;
            _leaveOpen = leaveOpen;
        }

        public override long? KnownLength
        {
            get
            {
                if (!_source.CanSeek)
                    return null;
                try
                {
                    return _source.Length - _source.Position + (_prefixLength - _prefixConsumed);
                }
                catch (NotSupportedException)
                {
                    return null;
                }
            }
        }

        public override bool CanSeek => _source.CanSeek;

        protected override ReadOnlyMemory<byte> PeekCore(int byteCount)
        {
            EnsureBuffered(byteCount);
            return _prefix.AsMemory(_prefixConsumed, Math.Min(byteCount, _prefixLength - _prefixConsumed));
        }

        // Buffers forward until the prefix holds `byteCount` unconsumed bytes or
        // the source ends. Only the probe prefix is ever held this way.
        private void EnsureBuffered(int byteCount)
        {
            int available = _prefixLength - _prefixConsumed;
            if (available >= byteCount)
                return;

            int needed = byteCount - available;
            if (_prefix.Length < _prefixLength + needed)
                Array.Resize(ref _prefix, _prefixLength + needed);

            while (needed > 0)
            {
                int read = _source.Read(_prefix, _prefixLength, needed);
                if (read == 0)
                    break;
                _prefixLength += read;
                needed -= read;
            }
        }

        protected override Stream OpenStreamCore()
        {
            ReadOnlyMemory<byte> replay = _prefix.AsMemory(_prefixConsumed, _prefixLength - _prefixConsumed);
            _prefixConsumed = _prefixLength;

            if (_source.CanSeek && replay.Length > 0)
            {
                // A seekable source needs no replay buffer: rewinding past the
                // probed bytes is cheaper and keeps one stream in play.
                _source.Position -= replay.Length;
                replay = ReadOnlyMemory<byte>.Empty;
            }

            return replay.Length == 0
                ? new NonClosingStream(_source)
                : new PrefixedStream(replay, _source);
        }

        protected override ReadOnlyMemory<byte> MaterializeCore(long maxBytes)
        {
            using Stream stream = OpenStreamCore();
            var buffer = new MemoryStream();
            byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (true)
                {
                    int read = stream.Read(chunk, 0, chunk.Length);
                    if (read == 0)
                        break;
                    if (buffer.Length + read > maxBytes)
                        throw TooLarge(maxBytes);
                    buffer.Write(chunk, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }

            return buffer.ToArray();
        }

        protected override async ValueTask<ReadOnlyMemory<byte>> MaterializeAsyncCore(long maxBytes, CancellationToken cancellationToken)
        {
            using Stream stream = OpenStreamCore();
            var buffer = new MemoryStream();
            byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    if (buffer.Length + read > maxBytes)
                        throw TooLarge(maxBytes);
                    await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }

            return buffer.ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
                _source.Dispose();
        }
    }

    /// <summary>A read-only stream over memory the caller owns.</summary>
    private sealed class ReadOnlyMemoryStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private int _position;

        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> bytes) => _bytes = bytes;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _bytes.Length;

        public override long Position
        {
            get => _position;
            set => _position = (int)Math.Clamp(value, 0, _bytes.Length);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int available = Math.Min(buffer.Length, _bytes.Length - _position);
            if (available <= 0)
                return 0;
            _bytes.Span.Slice(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _bytes.Length + offset,
            };
            Position = target;
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>The buffered probe prefix followed by the remaining source.</summary>
    private sealed class PrefixedStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _prefix;
        private readonly Stream _source;
        private int _prefixPosition;

        public PrefixedStream(ReadOnlyMemory<byte> prefix, Stream source)
        {
            _prefix = prefix;
            _source = source;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_prefixPosition < _prefix.Length)
            {
                int available = Math.Min(buffer.Length, _prefix.Length - _prefixPosition);
                _prefix.Span.Slice(_prefixPosition, available).CopyTo(buffer);
                _prefixPosition += available;
                return available;
            }

            return _source.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefixPosition < _prefix.Length)
            {
                int available = Math.Min(buffer.Length, _prefix.Length - _prefixPosition);
                _prefix.Slice(_prefixPosition, available).CopyTo(buffer);
                _prefixPosition += available;
                return available;
            }

            return await _source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Wraps a stream so the caller's stream survives the reader disposing it.</summary>
    private sealed class NonClosingStream : Stream
    {
        private readonly Stream _inner;

        public NonClosingStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
