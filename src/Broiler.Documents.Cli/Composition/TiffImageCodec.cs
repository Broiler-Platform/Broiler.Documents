using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Media;
using Broiler.Media.Image;

namespace Broiler.Documents.Cli.Composition;

/// <summary>Decodes the first image of a classic TIFF with uncompressed chunky RGB strips.</summary>
internal sealed class TiffImageCodec() : ImageCodec(new MediaCodecDescriptor(
    new MediaCodecId("broiler.documents.image.tiff"), "TIFF RGB strips", MediaKind.Image,
    MediaCodecCapabilities.Decode, [new MediaFormatDescriptor("TIFF", ["image/tiff"], [".tif", ".tiff"])]))
{
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
        if (directory.Scalar(259, 1) != 1 || directory.Scalar(262) != 2 ||
            directory.Scalar(274, 1) != 1 || directory.Scalar(277) != 3 || directory.Scalar(284, 1) != 1 ||
            depths.Count != 3 || directory.Value(depths, 0) != 8 || directory.Value(depths, 1) != 8 || directory.Value(depths, 2) != 8)
            throw new NotSupportedException("This TIFF requires a decoder for a different pixel or strip format.");

        Entry offsets = directory.Find(273), lengths = directory.Find(279);
        uint rowsPerStrip = directory.Scalar(278, uint.MaxValue);
        if (rowsPerStrip == 0 || offsets.Count != (height - 1) / rowsPerStrip + 1 || lengths.Count != offsets.Count)
            throw new FormatException("TIFF strip table is inconsistent.");

        var rgba = new byte[(int)pixels * 4];
        int target = 0;
        uint remaining = height;
        for (uint strip = 0; strip < offsets.Count; strip++)
        {
            uint rows = Math.Min(rowsPerStrip, remaining);
            int bytes = (int)(rows * width * 3);
            uint start = directory.Value(offsets, strip);
            if (directory.Value(lengths, strip) < bytes || start > data.Length || bytes > data.Length - start)
                throw new FormatException("TIFF strip payload is truncated.");
            ReadOnlySpan<byte> rgb = data.Slice((int)start, bytes);
            for (int source = 0; source < rgb.Length; source += 3)
            {
                rgb.Slice(source, 3).CopyTo(rgba.AsSpan(target, 3));
                rgba[target + 3] = 255;
                target += 4;
            }
            remaining -= rows;
        }
        return ImageSequence.Static(new ImageBuffer((int)width, (int)height, rgba));
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
