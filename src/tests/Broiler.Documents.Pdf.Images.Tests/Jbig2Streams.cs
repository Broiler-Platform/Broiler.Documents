using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Builds JBIG2 embedded streams segment by segment, for the tests that need
/// one.
/// </summary>
/// <remarks>
/// No JBIG2 file is committed anywhere in this repository: IP-020 would want one
/// registered with its provenance first, and the standard's own test sequences
/// are official test material. Assembling the segments by hand is therefore the
/// only way to have a stream at all, and it has the compensating virtue that
/// every test states exactly which structure it is about.
/// </remarks>
internal static class Jbig2Streams
{
    /// <summary>A page information segment, the given segments, and an end of page.</summary>
    internal static byte[] Page(int width, int height, params byte[][] segments)
    {
        var info = new List<byte>();
        AddUInt32(info, width);
        AddUInt32(info, height);
        AddUInt32(info, 0);             // x resolution
        AddUInt32(info, 0);             // y resolution
        info.Add(0);                    // flags
        AddUInt16(info, 0);             // striping

        var all = new List<byte[]> { Segment(number: 0, type: 48, [.. info]) };
        all.AddRange(segments);
        all.Add(Segment(number: 9999, type: 49, []));
        return Segments([.. all]);
    }

    internal static byte[] Segments(params byte[][] segments)
    {
        var bytes = new List<byte>();
        foreach (byte[] segment in segments)
            bytes.AddRange(segment);
        return bytes.ToArray();
    }

    /// <summary>One segment header in the sequential organisation, plus its data.</summary>
    /// <param name="referred">
    /// The segments this one refers to. A text region names the dictionaries
    /// holding its symbols this way, and the numbers are one byte each while the
    /// referring segment's own number is 256 or below.
    /// </param>
    internal static byte[] Segment(uint number, int type, byte[] data, uint[]? referred = null)
    {
        referred ??= [];

        var bytes = new List<byte>();
        AddUInt32(bytes, number);
        bytes.Add((byte)type);          // flags: type, one-byte page association
        bytes.Add((byte)(referred.Length << 5));

        foreach (uint reference in referred)
            bytes.Add((byte)reference);

        bytes.Add(1);                   // page association
        AddUInt32(bytes, data.Length);
        bytes.AddRange(data);
        return bytes.ToArray();
    }

    /// <summary>The filter's output, back into pixels.</summary>
    internal static bool[][] Unpack(byte[] packed, int columns, int rows)
    {
        int stride = (columns + 7) / 8;
        var image = new bool[rows][];

        for (int y = 0; y < rows; y++)
        {
            image[y] = new bool[columns];
            for (int x = 0; x < columns; x++)
            {
                // The filter emits PDF's convention, where zero is black.
                int bit = (packed[(y * stride) + (x >> 3)] >> (7 - (x & 7))) & 1;
                image[y][x] = bit == 0;
            }
        }

        return image;
    }

    internal static void AddUInt16(List<byte> target, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        target.AddRange(bytes.ToArray());
    }

    internal static void AddUInt32(List<byte> target, long value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        target.AddRange(bytes.ToArray());
    }
}
