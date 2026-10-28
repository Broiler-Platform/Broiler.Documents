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
/// <b>There is no ambient spooling.</b> By default, materialization is memory-only and
/// bounded by an explicit ceiling; nothing here opens a temporary file, consults an
/// ambient temp directory, or writes to disk without authorization. A host that wants
/// spooling supplies an explicit <see cref="DocumentSpoolPolicy"/> via
/// <see cref="SpoolFromStream"/> with its own directory, quota, permission, cleanup,
/// and crash-recovery policy.
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
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        if (KnownLength is { } length && length > maxBytes)
            throw TooLarge(maxBytes);

        return MaterializeCore(maxBytes);
    }

    /// <summary>The asynchronous form of <see cref="Materialize"/>.</summary>
    public ValueTask<ReadOnlyMemory<byte>> MaterializeAsync(long maxBytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

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

    /// <summary>
    /// Creates a spooling document input over <paramref name="source"/> governed by the host-supplied
    /// <paramref name="policy"/>.
    /// </summary>
    /// <param name="source">The source stream to read and spool.</param>
    /// <param name="policy">The host-supplied spool policy defining directory, quota, and cleanup.</param>
    /// <param name="leaveOpen">When true, disposing this input does not dispose <paramref name="source"/>.</param>
    public static DocumentInput SpoolFromStream(Stream source, DocumentSpoolPolicy policy, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(policy);
        if (!source.CanRead)
            throw new ArgumentException("A document input needs a readable stream.", nameof(source));
        return new SpoolingDocumentInput(source, policy, leaveOpen);
    }

    /// <summary>
    /// Creates a spooling document input over <paramref name="source"/> governed by the host-supplied
    /// <paramref name="policy"/>. Alias for <see cref="SpoolFromStream"/>.
    /// </summary>
    /// <param name="source">The source stream to read and spool.</param>
    /// <param name="policy">The host-supplied spool policy defining directory, quota, and cleanup.</param>
    /// <param name="leaveOpen">When true, disposing this input does not dispose <paramref name="source"/>.</param>
    public static DocumentInput FromStream(Stream source, DocumentSpoolPolicy policy, bool leaveOpen = true) =>
        SpoolFromStream(source, policy, leaveOpen);

    /// <summary>
    /// Asynchronously spools <paramref name="source"/> according to <paramref name="policy"/>, returning
    /// a seekable, spooled document input.
    /// </summary>
    /// <param name="source">The source stream to read and spool.</param>
    /// <param name="policy">The host-supplied spool policy defining directory, quota, and cleanup.</param>
    /// <param name="leaveOpen">When true, disposing this input does not dispose <paramref name="source"/>.</param>
    /// <param name="cancellationToken">Cancellation token to abort spooling.</param>
    public static async Task<DocumentInput> SpoolFromStreamAsync(
        Stream source,
        DocumentSpoolPolicy policy,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(policy);
        if (!source.CanRead)
            throw new ArgumentException("A document input needs a readable stream.", nameof(source));

        var input = new SpoolingDocumentInput(source, policy, leaveOpen);
        await input.EnsureSpooledAsync(cancellationToken).ConfigureAwait(false);
        return input;
    }

    internal static DocumentException TooLarge(long maxBytes) =>
        new($"The document source is larger than the {maxBytes}-byte limit for this read.");

    /// <summary>Releases resources held by the document input.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    private sealed class MemoryDocumentInput(ReadOnlyMemory<byte> bytes) : DocumentInput
    {
        public override long? KnownLength => bytes.Length;

        public override bool CanSeek => true;

        protected override ReadOnlyMemory<byte> PeekCore(int byteCount) =>
            bytes[..Math.Min(byteCount, bytes.Length)];

        protected override Stream OpenStreamCore() => new ReadOnlyMemoryStream(bytes);

        protected override ReadOnlyMemory<byte> MaterializeCore(long maxBytes) =>
            bytes.Length > maxBytes ? throw TooLarge(maxBytes) : bytes;

        protected override ValueTask<ReadOnlyMemory<byte>> MaterializeAsyncCore(long maxBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(MaterializeCore(maxBytes));
        }
    }

    private sealed class StreamDocumentInput(Stream source, bool leaveOpen) : DocumentInput
    {
        private byte[] _prefix = [];
        private int _prefixLength;
        private int _prefixConsumed;

        public override long? KnownLength
        {
            get
            {
                if (!source.CanSeek)
                    return null;
                try
                {
                    return source.Length - source.Position + (_prefixLength - _prefixConsumed);
                }
                catch (NotSupportedException)
                {
                    return null;
                }
            }
        }

        public override bool CanSeek => source.CanSeek;

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
                int read = source.Read(_prefix, _prefixLength, needed);
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

            if (source.CanSeek && replay.Length > 0)
            {
                // A seekable source needs no replay buffer: rewinding past the
                // probed bytes is cheaper and keeps one stream in play.
                source.Position -= replay.Length;
                replay = ReadOnlyMemory<byte>.Empty;
            }

            return replay.Length == 0
                ? new NonClosingStream(source)
                : new PrefixedStream(replay, source);
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
            if (disposing && !leaveOpen)
                source.Dispose();
        }
    }

    /// <summary>A read-only stream over memory the caller owns.</summary>
    private sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> bytes) : Stream
    {
        private long _position;
        private bool _disposed;

        public override bool CanRead => !_disposed;

        public override bool CanSeek => !_disposed;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return bytes.Length;
            }
        }

        public override long Position
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _position;
            }
            set
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_position >= bytes.Length)
                return 0;
            int available = Math.Min(buffer.Length, bytes.Length - (int)_position);
            bytes.Span.Slice((int)_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long start = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => bytes.Length,
                _ => throw new ArgumentException("Invalid seek origin.", nameof(origin)),
            };
            long target;
            try
            {
                target = checked(start + offset);
            }
            catch (OverflowException ex)
            {
                throw new IOException("The requested stream position is too large.", ex);
            }
            if (target < 0)
                throw new IOException("Cannot seek before the beginning of the stream.");
            return _position = target;
        }

        public override void Flush() => ObjectDisposedException.ThrowIf(_disposed, this);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>The buffered probe prefix followed by the remaining source.</summary>
    private sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream source) : Stream
    {
        private int _prefixPosition;

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
            if (_prefixPosition < prefix.Length)
            {
                int available = Math.Min(buffer.Length, prefix.Length - _prefixPosition);
                prefix.Span.Slice(_prefixPosition, available).CopyTo(buffer);
                _prefixPosition += available;
                return available;
            }

            return source.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefixPosition < prefix.Length)
            {
                int available = Math.Min(buffer.Length, prefix.Length - _prefixPosition);
                prefix.Slice(_prefixPosition, available).CopyTo(buffer);
                _prefixPosition += available;
                return available;
            }

            return await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Wraps a stream so the caller's stream survives the reader disposing it.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class SpoolingDocumentInput(Stream source, DocumentSpoolPolicy policy, bool leaveOpen) : DocumentInput
    {
        private byte[] _prefix = [];
        private int _prefixLength;
        private int _prefixConsumed;

        private bool _isSpooled;
        private ReadOnlyMemory<byte>? _memoryBytes;
        private string? _spoolFilePath;
        private long _spooledLength;
        private readonly object _syncLock = new();

        public override long? KnownLength
        {
            get
            {
                if (_isSpooled)
                    return _spooledLength;

                if (source.CanSeek)
                {
                    try
                    {
                        return source.Length - source.Position + (_prefixLength - _prefixConsumed);
                    }
                    catch (NotSupportedException) { }
                }

                return null;
            }
        }

        public override bool CanSeek => true;

        protected override ReadOnlyMemory<byte> PeekCore(int byteCount)
        {
            lock (_syncLock)
            {
                if (_isSpooled)
                {
                    if (_memoryBytes is { } mem)
                        return mem[..Math.Min(byteCount, mem.Length)];

                    if (_prefixLength >= byteCount)
                        return _prefix.AsMemory(0, Math.Min(byteCount, _prefixLength));

                    using var fs = new FileStream(_spoolFilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    int toRead = (int)Math.Min(byteCount, fs.Length);
                    byte[] buf = new byte[toRead];
                    int read = fs.Read(buf, 0, toRead);
                    return buf.AsMemory(0, read);
                }

                EnsurePrefixBuffered(byteCount);
                return _prefix.AsMemory(_prefixConsumed, Math.Min(byteCount, _prefixLength - _prefixConsumed));
            }
        }

        private void EnsurePrefixBuffered(int byteCount)
        {
            int available = _prefixLength - _prefixConsumed;
            if (available >= byteCount)
                return;

            int needed = byteCount - available;
            if (_prefix.Length < _prefixLength + needed)
                Array.Resize(ref _prefix, _prefixLength + needed);

            while (needed > 0)
            {
                int read = source.Read(_prefix, _prefixLength, needed);
                if (read == 0)
                    break;
                _prefixLength += read;
                needed -= read;
            }
        }

        internal void EnsureSpooled()
        {
            lock (_syncLock)
            {
                if (_isSpooled)
                    return;

                int unconsumedPrefix = _prefixLength - _prefixConsumed;
                var memoryBuffer = new MemoryStream();
                if (unconsumedPrefix > 0)
                {
                    memoryBuffer.Write(_prefix, _prefixConsumed, unconsumedPrefix);
                    _prefixConsumed = _prefixLength;
                }

                long totalBytes = memoryBuffer.Length;
                if (totalBytes > policy.MaxSpoolBytes)
                    throw new DocumentLimitExceededException($"The spooled document exceeded the quota of {policy.MaxSpoolBytes} bytes.");

                byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
                string? tempFile = null;
                FileStream? diskStream = null;

                try
                {
                    if (policy.MemoryThresholdBytes == 0)
                    {
                        tempFile = CreateSpoolFilePath(policy);
                        diskStream = CreateSpoolFileStream(tempFile);
                        if (memoryBuffer.Length > 0)
                        {
                            memoryBuffer.Position = 0;
                            memoryBuffer.CopyTo(diskStream);
                            memoryBuffer.SetLength(0);
                        }
                    }

                    while (true)
                    {
                        if (diskStream is null)
                        {
                            if (totalBytes <= policy.MemoryThresholdBytes)
                            {
                                int toRead = (int)Math.Min(chunk.Length, (long)policy.MemoryThresholdBytes - totalBytes + 1);
                                if (toRead <= 0) toRead = chunk.Length;
                                int read = source.Read(chunk, 0, toRead);
                                if (read == 0)
                                {
                                    _memoryBytes = memoryBuffer.ToArray();
                                    _spooledLength = _memoryBytes.Value.Length;
                                    _isSpooled = true;
                                    return;
                                }

                                totalBytes += read;
                                if (totalBytes > policy.MaxSpoolBytes)
                                    throw new DocumentLimitExceededException($"The spooled document exceeded the quota of {policy.MaxSpoolBytes} bytes.");

                                memoryBuffer.Write(chunk, 0, read);
                                if (totalBytes > policy.MemoryThresholdBytes)
                                {
                                    tempFile = CreateSpoolFilePath(policy);
                                    diskStream = CreateSpoolFileStream(tempFile);
                                    memoryBuffer.Position = 0;
                                    memoryBuffer.CopyTo(diskStream);
                                    memoryBuffer.SetLength(0);
                                }
                            }
                            else
                            {
                                tempFile = CreateSpoolFilePath(policy);
                                diskStream = CreateSpoolFileStream(tempFile);
                                memoryBuffer.Position = 0;
                                memoryBuffer.CopyTo(diskStream);
                                memoryBuffer.SetLength(0);
                            }
                        }
                        else
                        {
                            int read = source.Read(chunk, 0, chunk.Length);
                            if (read == 0)
                                break;

                            totalBytes += read;
                            if (totalBytes > policy.MaxSpoolBytes)
                                throw new DocumentLimitExceededException($"The spooled document exceeded the quota of {policy.MaxSpoolBytes} bytes.");

                            diskStream.Write(chunk, 0, read);
                        }
                    }

                    if (diskStream is not null)
                    {
                        diskStream.Flush();
                        diskStream.Dispose();
                        diskStream = null;

                        _spoolFilePath = tempFile;
                        _spooledLength = totalBytes;
                        _isSpooled = true;
                    }
                    else
                    {
                        _memoryBytes = memoryBuffer.ToArray();
                        _spooledLength = _memoryBytes.Value.Length;
                        _isSpooled = true;
                    }
                }
                catch
                {
                    diskStream?.Dispose();
                    if (tempFile is not null)
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                    throw;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(chunk);
                    memoryBuffer.Dispose();
                }
            }
        }

        internal async Task EnsureSpooledAsync(CancellationToken cancellationToken)
        {
            if (_isSpooled)
                return;

            int unconsumedPrefix = _prefixLength - _prefixConsumed;
            var memoryBuffer = new MemoryStream();
            if (unconsumedPrefix > 0)
            {
                memoryBuffer.Write(_prefix, _prefixConsumed, unconsumedPrefix);
                _prefixConsumed = _prefixLength;
            }

            long totalBytes = memoryBuffer.Length;
            if (totalBytes > policy.MaxSpoolBytes)
                throw new DocumentLimitExceededException($"The spooled document exceeded the quota of {policy.MaxSpoolBytes} bytes.");

            byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
            string? tempFile = null;
            FileStream? diskStream = null;

            try
            {
                if (policy.MemoryThresholdBytes == 0)
                {
                    tempFile = CreateSpoolFilePath(policy);
                    diskStream = CreateSpoolFileStream(tempFile);
                    if (memoryBuffer.Length > 0)
                    {
                        memoryBuffer.Position = 0;
                        await memoryBuffer.CopyToAsync(diskStream, cancellationToken).ConfigureAwait(false);
                        memoryBuffer.SetLength(0);
                    }
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (diskStream is null)
                    {
                        if (totalBytes <= policy.MemoryThresholdBytes)
                        {
                            int toRead = (int)Math.Min(chunk.Length, (long)policy.MemoryThresholdBytes - totalBytes + 1);
                            if (toRead <= 0) toRead = chunk.Length;
                            int read = await source.ReadAsync(chunk.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                            if (read == 0)
                            {
                                _memoryBytes = memoryBuffer.ToArray();
                                _spooledLength = _memoryBytes.Value.Length;
                                _isSpooled = true;
                                return;
                            }

                            totalBytes += read;
                            if (totalBytes > policy.MaxSpoolBytes)
                                throw new DocumentLimitExceededException($"The spooled document exceeded the quota of {policy.MaxSpoolBytes} bytes.");

                            memoryBuffer.Write(chunk, 0, read);
                            if (totalBytes > policy.MemoryThresholdBytes)
                            {
                                tempFile = CreateSpoolFilePath(policy);
                                diskStream = CreateSpoolFileStream(tempFile);
                                memoryBuffer.Position = 0;
                                await memoryBuffer.CopyToAsync(diskStream, cancellationToken).ConfigureAwait(false);
                                memoryBuffer.SetLength(0);
                            }
                        }
                        else
                        {
                            tempFile = CreateSpoolFilePath(policy);
                            diskStream = CreateSpoolFileStream(tempFile);
                            memoryBuffer.Position = 0;
                            await memoryBuffer.CopyToAsync(diskStream, cancellationToken).ConfigureAwait(false);
                            memoryBuffer.SetLength(0);
                        }
                    }
                    else
                    {
                        int read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                            break;

                        totalBytes += read;
                        if (totalBytes > policy.MaxSpoolBytes)
                            throw new DocumentLimitExceededException($"The spooled document exceeded the quota of {policy.MaxSpoolBytes} bytes.");

                        await diskStream.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                }

                if (diskStream is not null)
                {
                    await diskStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    await diskStream.DisposeAsync().ConfigureAwait(false);
                    diskStream = null;

                    _spoolFilePath = tempFile;
                    _spooledLength = totalBytes;
                    _isSpooled = true;
                }
                else
                {
                    _memoryBytes = memoryBuffer.ToArray();
                    _spooledLength = _memoryBytes.Value.Length;
                    _isSpooled = true;
                }
            }
            catch
            {
                if (diskStream is not null)
                    await diskStream.DisposeAsync().ConfigureAwait(false);
                if (tempFile is not null)
                {
                    try { File.Delete(tempFile); } catch { }
                }
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
                memoryBuffer.Dispose();
            }
        }

        private static string CreateSpoolFilePath(DocumentSpoolPolicy policy)
        {
            string fileName = $"{policy.FilePrefix}{Environment.ProcessId}-{Guid.NewGuid():N}.spool";
            return Path.Combine(policy.DirectoryPath, fileName);
        }

        private static FileStream CreateSpoolFileStream(string filePath)
        {
            var stream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                FileOptions.Asynchronous);

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Temporary);
                }
                catch { }
            }
            else
            {
                try
                {
                    File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch { }
            }

            return stream;
        }

        protected override Stream OpenStreamCore()
        {
            EnsureSpooled();

            if (_memoryBytes is { } mem)
                return new ReadOnlyMemoryStream(mem);

            return new FileStream(
                _spoolFilePath!,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920);
        }

        protected override ReadOnlyMemory<byte> MaterializeCore(long maxBytes)
        {
            EnsureSpooled();

            if (_spooledLength > maxBytes)
                throw TooLarge(maxBytes);

            if (_memoryBytes is { } mem)
                return mem;

            return File.ReadAllBytes(_spoolFilePath!);
        }

        protected override async ValueTask<ReadOnlyMemory<byte>> MaterializeAsyncCore(long maxBytes, CancellationToken cancellationToken)
        {
            await EnsureSpooledAsync(cancellationToken).ConfigureAwait(false);

            if (_spooledLength > maxBytes)
                throw TooLarge(maxBytes);

            if (_memoryBytes is { } mem)
                return mem;

            return await File.ReadAllBytesAsync(_spoolFilePath!, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_spoolFilePath is not null && policy.DeleteOnDispose)
                {
                    try { File.Delete(_spoolFilePath); } catch { }
                    _spoolFilePath = null;
                }

                if (!leaveOpen)
                    source.Dispose();
            }
        }
    }

}
