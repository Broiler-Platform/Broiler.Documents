using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Images;

/// <summary>One segment of a JBIG2 embedded stream, located but not interpreted.</summary>
internal readonly record struct Jbig2Segment(
    uint Number,
    int Type,
    uint Page,
    int DataStart,
    int DataLength)
{
    /// <summary>The segment type as ITU-T T.88 names it.</summary>
    public string Describe() => Type switch
    {
        0 => "symbol dictionary",
        4 or 6 or 7 => "text region",
        16 => "pattern dictionary",
        20 or 22 or 23 => "halftone region",
        36 or 38 or 39 => "generic region",
        40 or 42 or 43 => "refinement region",
        48 => "page information",
        49 => "end of page",
        50 => "end of stripe",
        51 => "end of file",
        52 => "profiles",
        53 => "custom Huffman tables",
        62 => "extension",
        _ => string.Create(CultureInfo.InvariantCulture, $"segment type {Type}"),
    };

    /// <summary>True for the segment types that carry no image data to lose.</summary>
    public bool IsStructural => Type is 48 or 49 or 50 or 51 or 52 or 62;

    public bool IsGenericRegion => Type is 36 or 38 or 39;
}

/// <summary>A generic region's header, read without decoding its data.</summary>
internal readonly record struct Jbig2GenericRegion(
    int Width,
    int Height,
    int X,
    int Y,
    int CombinationOperator,
    bool UsesMmr,
    int Template,
    bool TypicalPrediction,
    int DataStart,
    int DataLength)
{
    /// <summary>
    /// The adaptive template pixels the header supplied, A1 first. Empty for an
    /// MMR region, which has none, and for a header that stated none.
    /// </summary>
    public (int X, int Y)[] Adaptive { get; init; } = [];
}

/// <summary>
/// Reads the segment structure of a JBIG2 embedded stream.
/// </summary>
/// <remarks>
/// <para>
/// PDF embeds JBIG2 in its sequential organisation: no file header, no random
/// access table, just segment headers each followed immediately by their data.
/// This walks that chain and says what is in it. It interprets one segment type —
/// a generic region — and only enough of that to find its bitmap.
/// </para>
/// <para>
/// The referred-to-segment field is the fiddly part and the reason this is worth
/// its own type: its size depends on a count that is itself encoded in one of two
/// forms, and the width of each reference depends on the number of the segment
/// doing the referring. Getting it wrong does not fail, it desynchronises the
/// walk, so it is bounded and checked rather than trusted.
/// </para>
/// </remarks>
internal static class Jbig2SegmentReader
{
    /// <summary>Segments read before a stream is refused as unreasonable.</summary>
    private const int MaxSegments = 4096;

    /// <summary>Marks a segment whose length the header does not state.</summary>
    private const uint UnknownLength = 0xFFFFFFFFu;

    public static bool TryRead(ReadOnlySpan<byte> data, out List<Jbig2Segment> segments, out string? error)
    {
        segments = [];
        int position = 0;

        while (position < data.Length)
        {
            if (segments.Count >= MaxSegments)
            {
                error = "The JBIG2 stream declares more segments than this reader will walk.";
                return false;
            }

            if (position + 11 > data.Length)
            {
                // A trailing fragment too short to be a header ends the walk
                // rather than failing it: what was read is still usable.
                break;
            }

            uint number = ReadUInt32(data, position);
            byte flags = data[position + 4];
            int type = flags & 0x3F;
            bool longPage = (flags & 0x40) != 0;
            int cursor = position + 5;

            byte referredFlags = data[cursor];
            long referredCount = referredFlags >> 5;
            if (referredCount == 7)
            {
                if (cursor + 4 > data.Length)
                {
                    error = "A JBIG2 segment header declares a referred-to count that does not fit the stream.";
                    return false;
                }

                referredCount = ReadUInt32(data, cursor) & 0x1FFFFFFF;
                cursor += 4;

                long retainBytes = (referredCount + 8) / 8;
                if (referredCount > MaxSegments || cursor + retainBytes > data.Length)
                {
                    error = "A JBIG2 segment header declares more referred-to segments than the stream holds.";
                    return false;
                }

                cursor += (int)retainBytes;
            }
            else
            {
                cursor += 1;
            }

            int referenceSize = number <= 256 ? 1 : number <= 65536 ? 2 : 4;
            long referencesLength = referredCount * referenceSize;
            if (cursor + referencesLength + (longPage ? 4 : 1) + 4 > data.Length)
            {
                error = "A JBIG2 segment header runs past the end of the stream.";
                return false;
            }

            cursor += (int)referencesLength;

            uint page = longPage ? ReadUInt32(data, cursor) : data[cursor];
            cursor += longPage ? 4 : 1;

            uint length = ReadUInt32(data, cursor);
            cursor += 4;

            if (length == UnknownLength)
            {
                error = "A JBIG2 segment declares an unknown data length, which this reader does not resolve.";
                return false;
            }

            if (cursor + length > data.Length)
            {
                error = "A JBIG2 segment declares a data length that does not fit the stream.";
                return false;
            }

            segments.Add(new Jbig2Segment(number, type, page, cursor, (int)length));
            position = cursor + (int)length;
        }

        if (segments.Count == 0)
        {
            error = "The stream carries no JBIG2 segments.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Reads a generic region segment's header, up to its bitmap data.</summary>
    public static bool TryReadGenericRegion(
        ReadOnlySpan<byte> data,
        in Jbig2Segment segment,
        out Jbig2GenericRegion region,
        out string? error)
    {
        region = default;

        // Region segment information is seventeen bytes, then one byte of
        // generic-region flags.
        if (segment.DataLength < 18)
        {
            error = "A JBIG2 generic region segment is too short to describe a region.";
            return false;
        }

        ReadOnlySpan<byte> body = data.Slice(segment.DataStart, segment.DataLength);
        long width = ReadUInt32(body, 0);
        long height = ReadUInt32(body, 4);
        long x = ReadUInt32(body, 8);
        long y = ReadUInt32(body, 12);
        int combination = body[16] & 0x07;

        byte flags = body[17];
        (int X, int Y)[] adaptivePixels = [];
        bool mmr = (flags & 0x01) != 0;
        int template = (flags >> 1) & 0x03;
        bool typicalPrediction = (flags & 0x08) != 0;

        int cursor = 18;
        if (!mmr)
        {
            // Arithmetic coding carries the adaptive template pixels: four pairs
            // for template 0, one for the rest.
            int count = template == 0 ? 4 : 1;
            if (cursor + (count * 2) > body.Length)
            {
                error = "A JBIG2 generic region declares template pixels the segment does not hold.";
                return false;
            }

            // Signed bytes: an adaptive pixel may sit left of or above the one
            // being decoded, which is most of the point of moving it.
            adaptivePixels = new (int, int)[count];
            for (int i = 0; i < count; i++)
            {
                adaptivePixels[i] = ((sbyte)body[cursor], (sbyte)body[cursor + 1]);
                cursor += 2;
            }
        }

        if (width is <= 0 or > (1 << 16) || height is <= 0 or > (1 << 16))
        {
            error = "A JBIG2 generic region declares a size outside the supported range.";
            return false;
        }

        region = new Jbig2GenericRegion(
            (int)width, (int)height, (int)Math.Min(x, int.MaxValue), (int)Math.Min(y, int.MaxValue),
            combination, mmr, template, typicalPrediction,
            segment.DataStart + cursor, segment.DataLength - cursor)
        {
            Adaptive = adaptivePixels,
        };
        error = null;
        return true;
    }

    /// <summary>Reads the page information segment's declared size.</summary>
    public static bool TryReadPageSize(ReadOnlySpan<byte> data, in Jbig2Segment segment, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (segment.DataLength < 8)
            return false;

        ReadOnlySpan<byte> body = data.Slice(segment.DataStart, segment.DataLength);
        long declaredWidth = ReadUInt32(body, 0);
        long declaredHeight = ReadUInt32(body, 4);

        if (declaredWidth is <= 0 or > (1 << 16))
            return false;

        width = (int)declaredWidth;

        // An unknown page height is legal and resolved from the end-of-stripe
        // segments this reader does not interpret, so it is left for the caller
        // to take from the regions instead.
        height = declaredHeight is > 0 and <= (1 << 16) ? (int)declaredHeight : 0;
        return true;
    }

    /// <summary>An inventory of what a stream contains, for the diagnostic.</summary>
    public static string Describe(List<Jbig2Segment> segments)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Jbig2Segment segment in segments)
        {
            string name = segment.Describe();
            counts.TryGetValue(name, out int seen);
            counts[name] = seen + 1;
        }

        var names = new List<string>(counts.Keys);
        names.Sort(StringComparer.Ordinal);

        var parts = new List<string>(names.Count);
        foreach (string name in names)
        {
            int count = counts[name];
            parts.Add(count == 1
                ? string.Create(CultureInfo.InvariantCulture, $"1 {name}")
                : string.Create(CultureInfo.InvariantCulture, $"{count} {name} segments"));
        }

        return string.Join(", ", parts);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
}
