using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Media;
using Broiler.Media.Image;

namespace Broiler.Documents.Cli.Composition;

/// <summary>
/// Decodes the first image of a classic TIFF with chunky 8-bit RGB or RGBA strips,
/// uncompressed, LZW or PackBits, with or without horizontal differencing.
/// </summary>
internal sealed class TiffImageCodec() : ImageCodec(new MediaCodecDescriptor(
    new MediaCodecId("broiler.documents.image.tiff"), "TIFF RGB strips", MediaKind.Image,
    MediaCodecCapabilities.Decode, [new MediaFormatDescriptor("TIFF", ["image/tiff"], [".tif", ".tiff"])]))
{
    private const uint Uncompressed = 1, Lzw = 5, PackBits = 32773;

    public override ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> prefix = request.Prefix.Span;
        bool matches = prefix.StartsWith("II*\0"u8) ||
            prefix.StartsWith("MM\0*"u8);
        return ValueTask.FromResult(matches
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "TIFF", "image/tiff", 4)
            : MediaProbeResult.NoMatch(MediaKind.Image));
    }

    protected override ImageSequence DecodeCore(ReadOnlySpan<byte> data, ImageDecodeOptions options)
    {
        var directory = new Directory(data);
        uint width = directory.Scalar(256), height = directory.Scalar(257);
        long pixels = (long)width * height;
        if (width == 0 || height == 0 || pixels <= 0 || pixels > options.Limits.MaxImagePixels || pixels > int.MaxValue / 4)
            throw new FormatException("TIFF image dimensions exceed the pixel budget.");
        Entry depths = directory.Find(258);
        uint samples = directory.Scalar(277);
        uint compression = directory.Scalar(259, Uncompressed);
        uint predictor = directory.Scalar(317, 1);
        bool depthsAre8 = depths.Count == samples;
        for (uint i = 0; depthsAre8 && i < samples; i++)
            depthsAre8 = directory.Value(depths, i) == 8;
        if (compression is not (Uncompressed or Lzw or PackBits) || predictor is not (1 or 2) || directory.Scalar(262) != 2 ||
            directory.Scalar(274, 1) != 1 || samples is not (3 or 4) || directory.Scalar(284, 1) != 1 || !depthsAre8)
            throw new NotSupportedException("This TIFF requires a decoder for a different pixel or strip format.");
        // A fourth sample is alpha only where ExtraSamples says so: 1 associated, 2 unassociated.
        uint alpha = samples == 4 ? directory.Scalar(338) : 0;

        Entry offsets = directory.Find(273), lengths = directory.Find(279);
        uint rowsPerStrip = directory.Scalar(278, uint.MaxValue);
        if (rowsPerStrip == 0 || offsets.Count != (height - 1) / rowsPerStrip + 1 || lengths.Count != offsets.Count)
            throw new FormatException("TIFF strip table is inconsistent.");

        var rgba = new byte[(int)pixels * 4];
        int target = 0;
        uint remaining = height;
        int step = (int)samples, stride = (int)(width * samples);
        for (uint strip = 0; strip < offsets.Count; strip++)
        {
            uint rows = Math.Min(rowsPerStrip, remaining);
            int bytes = (int)(rows * stride);
            uint start = directory.Value(offsets, strip), length = directory.Value(lengths, strip);
            if (start > data.Length || length > data.Length - start)
                throw new FormatException("TIFF strip payload is truncated.");
            ReadOnlySpan<byte> payload = data.Slice((int)start, (int)length);
            byte[] decoded = compression switch
            {
                Lzw => DecodeLzw(payload, bytes),
                PackBits => DecodePackBits(payload, bytes),
                _ => payload.Length >= bytes ? payload[..bytes].ToArray() : [],
            };
            if (decoded.Length < bytes)
                throw new FormatException("TIFF strip payload is truncated.");
            if (predictor == 2)
            {
                // Horizontal differencing (TIFF 6.0, section 14): each sample is stored
                // as its difference from the same sample of the pixel to its left.
                for (int row = 0; row < rows; row++)
                    for (int at = row * stride + step; at < (row + 1) * stride; at++)
                        decoded[at] += decoded[at - step];
            }
            for (int source = 0; source < bytes; source += step)
            {
                byte a = alpha is 1 or 2 ? decoded[source + 3] : (byte)255;
                for (int c = 0; c < 3; c++)
                {
                    byte value = decoded[source + c];
                    rgba[target + c] = alpha == 1 && a is > 0 and < 255 ? (byte)Math.Min(255, value * 255 / a) : value;
                }
                rgba[target + 3] = a;
                target += 4;
            }
            remaining -= rows;
        }
        return ImageSequence.Static(new ImageBuffer((int)width, (int)height, rgba));
    }

    /// <summary>
    /// TIFF LZW (TIFF 6.0, section 13): most significant bit first, codes 9 to 12
    /// bits wide, the width growing one code before the table reaches it. Stops at
    /// the end code, at the end of the input, or once <paramref name="expected"/>
    /// bytes are out; a short result is returned short.
    /// </summary>
    internal static byte[] DecodeLzw(ReadOnlySpan<byte> input, int expected)
    {
        const int Clear = 256, End = 257, First = 258, MaxCodes = 4096;
        var output = new byte[expected];
        var prefix = new short[MaxCodes];
        var suffix = new byte[MaxCodes];
        var lead = new byte[MaxCodes];
        var lengths = new ushort[MaxCodes];
        for (int code = 0; code < 256; code++)
            (prefix[code], suffix[code], lead[code], lengths[code]) = (-1, (byte)code, (byte)code, 1);

        int written = 0, next = First, codeWidth = 9, previous = -1;
        int bitBuffer = 0, bitCount = 0, position = 0;
        while (written < expected)
        {
            while (bitCount < codeWidth && position < input.Length)
            {
                bitBuffer = (bitBuffer << 8) | input[position++];
                bitCount += 8;
            }
            if (bitCount < codeWidth)
                break;
            int code = (bitBuffer >> (bitCount - codeWidth)) & ((1 << codeWidth) - 1);
            bitCount -= codeWidth;
            if (code == End)
                break;
            if (code == Clear)
            {
                (next, codeWidth, previous) = (First, 9, -1);
                continue;
            }
            if (previous < 0)
            {
                if (code > 255)
                    break;
                output[written++] = (byte)code;
                previous = code;
                continue;
            }
            if (code > next || (code == next && next >= MaxCodes))
                break;
            if (next < MaxCodes)
            {
                // The new entry is the previous string and the first byte of this one;
                // for the code being defined by this very step that is the previous string's own.
                (prefix[next], suffix[next]) = ((short)previous, code == next ? lead[previous] : lead[code]);
                (lead[next], lengths[next]) = (lead[previous], (ushort)(lengths[previous] + 1));
                next++;
                if (next == (1 << codeWidth) - 1 && codeWidth < 12)
                    codeWidth++;
            }
            int end = written + lengths[code];
            for (int entry = code, at = end - 1; entry >= 0; entry = prefix[entry], at--)
                if (at < expected)
                    output[at] = suffix[entry];
            written = Math.Min(expected, end);
            previous = code;
        }
        return written == expected ? output : output[..written];
    }

    /// <summary>PackBits (TIFF 6.0, section 9): a count byte, then literal bytes or one byte repeated.</summary>
    internal static byte[] DecodePackBits(ReadOnlySpan<byte> input, int expected)
    {
        var output = new byte[expected];
        int written = 0, position = 0;
        while (written < expected && position < input.Length)
        {
            int count = (sbyte)input[position++];
            if (count >= 0)
            {
                int copy = Math.Min(Math.Min(count + 1, expected - written), input.Length - position);
                input.Slice(position, copy).CopyTo(output.AsSpan(written));
                (written, position) = (written + copy, position + count + 1);
            }
            else if (count != -128 && position < input.Length)
            {
                int repeat = Math.Min(1 - count, expected - written);
                output.AsSpan(written, repeat).Fill(input[position++]);
                written += repeat;
            }
        }
        return written == expected ? output : output[..written];
    }

    private readonly record struct Entry(uint Count, int Size, int Offset);

    private readonly ref struct Directory
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly bool _little;
        private readonly int _offset;
        private readonly int _count;

        public Directory(ReadOnlySpan<byte> data)
        {
            _data = data;
            _little = data.StartsWith("II"u8);
            if (data.Length < 8 || (!_little && !data.StartsWith("MM"u8)) || Read16(2) != 42)
                throw new FormatException("Invalid classic TIFF header.");
            uint offset = Read32(4);
            if (offset > data.Length - 2)
                throw new FormatException("TIFF image directory is truncated.");
            _offset = (int)offset + 2;
            _count = Read16((int)offset);
            if (_count > (data.Length - _offset) / 12)
                throw new FormatException("TIFF image directory is truncated.");
        }

        public Entry Find(int tag)
        {
            for (int index = 0; index < _count; index++)
            {
                int entry = _offset + index * 12;
                if (Read16(entry) != tag)
                    continue;
                int size = Read16(entry + 2) switch { 3 => 2, 4 => 4, _ => 0 };
                uint count = Read32(entry + 4);
                long bytes = (long)size * count;
                long offset = bytes <= 4 ? entry + 8 : Read32(entry + 8);
                if (size == 0 || bytes == 0 || offset + bytes > _data.Length)
                    throw new FormatException("Invalid TIFF tag data.");
                return new Entry(count, size, (int)offset);
            }
            return default;
        }

        public uint Scalar(int tag, uint fallback = 0)
        {
            Entry entry = Find(tag);
            return entry.Count == 1 ? Value(entry, 0) : fallback;
        }

        public uint Value(Entry entry, uint index)
        {
            int offset = entry.Offset + (int)index * entry.Size;
            return entry.Size == 2 ? Read16(offset) : Read32(offset);
        }

        private ushort Read16(int offset) => _little
            ? BinaryPrimitives.ReadUInt16LittleEndian(_data[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(_data[offset..]);
        private uint Read32(int offset) => _little
            ? BinaryPrimitives.ReadUInt32LittleEndian(_data[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(_data[offset..]);
    }
}
