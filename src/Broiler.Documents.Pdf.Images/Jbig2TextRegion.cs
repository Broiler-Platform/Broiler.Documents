using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Documents.Pdf.Images;

/// <summary>What a text region segment produced, and where it goes on the page.</summary>
internal readonly record struct Jbig2TextRegionResult(
    Jbig2DecodeOutcome Outcome,
    Jbig2Bitmap? Bitmap,
    int X,
    int Y,
    int CombinationOperator,
    string? Message);

/// <summary>
/// Decodes a JBIG2 text region segment: where a dictionary's symbols are drawn.
/// </summary>
/// <remarks>
/// <para>
/// A text region is a list of placements — this symbol, at this position — and
/// nothing else. All of the picture is in the dictionary; all of the layout is
/// here. That division is why the format compresses scanned text so far, and it
/// is also why a text region alone is worthless: the identifiers it codes mean
/// only what the dictionaries it refers to say they mean.
/// </para>
/// <para>
/// <strong>Strips.</strong> Placements are not coded as independent coordinates.
/// The region is divided into horizontal strips, each instance's position coded
/// as a difference from the one before it along the strip and the strip's own
/// position as a difference from the previous strip. So the coordinates arrive as
/// a running sum, and every one of them is a small number, which is what makes
/// them cheap. It also means a single misread difference moves every symbol after
/// it rather than one.
/// </para>
/// <para>
/// <strong>The reference corner is the fiddly part.</strong> An instance's
/// coordinate names one corner of the symbol, which corner is a property of the
/// region, and the running S coordinate advances by the symbol's extent either
/// before or after placement depending on which corner it is. The four cases are
/// written out below rather than folded together, because a reviewer holding
/// T.88 6.4.5 should be able to read them off against its steps, and because
/// getting one wrong draws a legible page with the symbols in the wrong places.
/// </para>
/// <para>
/// <strong>What is decoded here.</strong> Arithmetic coding, without refinement.
/// A Huffman-coded text region is refused by name, so is one that refines the
/// symbols it places, and so is any composition operator other than OR — the one
/// case where drawing the symbols in any order gives the same answer.
/// </para>
/// </remarks>
internal static class Jbig2TextRegion
{
    /// <summary>Region segment information: size, position, and the external operator.</summary>
    private const int RegionInfoLength = 17;

    /// <summary>The coordinate range a running difference may reach before it is nonsense.</summary>
    private const int CoordinateCeiling = 1 << 24;

    public static Jbig2TextRegionResult Decode(
        ReadOnlyMemory<byte> body,
        IReadOnlyList<Jbig2Bitmap> symbols,
        long pixelBudget)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        ReadOnlySpan<byte> header = body.Span;
        if (header.Length < RegionInfoLength + 2)
            return Malformed("A JBIG2 text region is too short to describe a region.");

        long width = BinaryPrimitives.ReadUInt32BigEndian(header);
        long height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        long x = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        long y = BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
        int externalCombination = header[16] & 0x07;

        if (width is <= 0 or > (1 << 16) || height is <= 0 or > (1 << 16))
            return Malformed("A JBIG2 text region declares a size outside the supported range.");

        if (x > CoordinateCeiling || y > CoordinateCeiling)
            return Malformed("A JBIG2 text region declares a position outside the supported range.");

        int flags = BinaryPrimitives.ReadUInt16BigEndian(header[RegionInfoLength..]);
        bool huffman = (flags & 0x01) != 0;
        bool refine = (flags & 0x02) != 0;
        int logStrips = (flags >> 2) & 0x03;
        int corner = (flags >> 4) & 0x03;
        bool transposed = (flags & 0x40) != 0;
        int combination = (flags >> 7) & 0x03;
        byte defaultPixel = (byte)((flags >> 9) & 0x01);
        int refinementTemplate = (flags >> 15) & 0x01;

        // A five-bit two's-complement field: the offset added to every symbol
        // spacing in the region, which lets an encoder tune the common case to
        // zero and save a bit per instance.
        int spacingOffset = (flags >> 10) & 0x1F;
        if (spacingOffset > 15)
            spacingOffset -= 32;

        if (huffman)
            return Unsupported("a Huffman-coded text region");

        if (refine)
            return Unsupported("a text region that refines the symbols it places");

        if (combination != 0)
        {
            return Unsupported(string.Create(
                CultureInfo.InvariantCulture,
                $"a text region that draws its symbols with operator {combination} rather than OR"));
        }

        if (symbols.Count == 0)
            return Unsupported("a text region whose dictionaries export no symbol");

        if (symbols.Count > Jbig2Limits.MaxSymbols)
            return Malformed("A JBIG2 text region refers to more symbols than this reader will hold.");

        int cursor = RegionInfoLength + 2;

        // Read but unreachable while refinement is refused above, and skipped
        // here so the cursor stays honest if that refusal ever moves.
        if (refine && refinementTemplate == 0)
            cursor += 4;

        if (cursor + 4 > header.Length)
            return Malformed("A JBIG2 text region does not state how many symbol instances it holds.");

        long declaredInstances = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(cursor, 4));
        cursor += 4;

        if (declaredInstances > Jbig2Limits.MaxInstances)
            return Malformed("A JBIG2 text region declares more symbol instances than this reader will place.");

        // Both extents are bounded above by 2^16, so their product can exceed an
        // int even when the caller's budget would allow it.
        if (width * height > Math.Min(pixelBudget, int.MaxValue))
            return new Jbig2TextRegionResult(Jbig2DecodeOutcome.TooLarge, null, 0, 0, 0, null);

        var region = Jbig2Bitmap.Blank((int)width, (int)height, defaultPixel);
        var decoder = new MqDecoder(body[cursor..]);
        var deltaT = new Jbig2IntegerDecoder();
        var firstS = new Jbig2IntegerDecoder();
        var deltaS = new Jbig2IntegerDecoder();
        var instanceT = new Jbig2IntegerDecoder();
        var identifiers = new Jbig2SymbolIdDecoder(CodeLength(symbols.Count));

        int strips = 1 << logStrips;

        // The first strip's T coordinate is coded as a negative offset, which is
        // the one place the format states a coordinate rather than a difference.
        if (deltaT.Decode(decoder, out int initial) != Jbig2IntegerOutcome.Value)
            return Malformed("A JBIG2 text region states no position for its first strip.");

        long stripT = -(long)initial * strips;
        long currentFirstS = 0;
        long placed = 0;

        while (placed < declaredInstances)
        {
            if (deltaT.Decode(decoder, out int stripDelta) != Jbig2IntegerOutcome.Value)
                return Malformed("A JBIG2 text region states no position for a strip.");

            stripT += (long)stripDelta * strips;

            if (firstS.Decode(decoder, out int firstDelta) != Jbig2IntegerOutcome.Value)
                return Malformed("A JBIG2 text region states no position for a strip's first symbol.");

            currentFirstS += firstDelta;
            long currentS = currentFirstS;

            if (Math.Abs(stripT) > CoordinateCeiling || Math.Abs(currentS) > CoordinateCeiling)
                return Malformed("A JBIG2 text region places a symbol outside any plausible page.");

            while (true)
            {
                if (placed >= declaredInstances)
                    return Malformed("A JBIG2 text region places more symbol instances than it declared.");

                int instanceOffset = 0;
                if (strips > 1 && instanceT.Decode(decoder, out instanceOffset) != Jbig2IntegerOutcome.Value)
                    return Malformed("A JBIG2 text region states no offset for a symbol within its strip.");

                long t = stripT + instanceOffset;

                int id = identifiers.Decode(decoder);
                if (id < 0 || id >= symbols.Count)
                    return Malformed("A JBIG2 text region places a symbol its dictionaries do not define.");

                Jbig2Bitmap symbol = symbols[id];
                bool rightCorner = corner >= 2;
                bool bottomCorner = (corner & 1) == 0;

                // T.88 6.4.5 steps (vi) and (vii): for a corner on the trailing
                // edge, the running coordinate advances to it before the symbol is
                // placed, so that the corner and not the leading edge lands on it.
                if (!transposed && rightCorner)
                    currentS += symbol.Width - 1;
                else if (transposed && bottomCorner)
                    currentS += symbol.Height - 1;

                if (Math.Abs(currentS) > CoordinateCeiling || Math.Abs(t) > CoordinateCeiling)
                    return Malformed("A JBIG2 text region places a symbol outside any plausible page.");

                // Step (ix). S runs along the region's width when the region is
                // not transposed and down its height when it is; T is the other
                // axis. Which corner the pair names decides the shift.
                long left = transposed
                    ? (rightCorner ? t - symbol.Width + 1 : t)
                    : (rightCorner ? currentS - symbol.Width + 1 : currentS);

                long top = transposed
                    ? (bottomCorner ? currentS - symbol.Height + 1 : currentS)
                    : (bottomCorner ? t - symbol.Height + 1 : t);

                Draw(region, symbol, left, top);

                // Steps (x) and (xi): a leading-edge corner advances afterwards
                // instead, so that either way the next symbol starts past this one.
                if (!transposed && !rightCorner)
                    currentS += symbol.Width - 1;
                else if (transposed && !bottomCorner)
                    currentS += symbol.Height - 1;

                placed++;

                Jbig2IntegerOutcome outcome = deltaS.Decode(decoder, out int spacing);
                if (outcome == Jbig2IntegerOutcome.OutOfBand)
                    break;

                if (outcome != Jbig2IntegerOutcome.Value)
                    return Malformed("A JBIG2 text region states a symbol spacing it cannot hold.");

                currentS += (long)spacing + spacingOffset;
            }
        }

        return new Jbig2TextRegionResult(
            Jbig2DecodeOutcome.Decoded, region, (int)x, (int)y, externalCombination, null);
    }

    /// <summary>
    /// The number of bits a symbol identifier is coded in: enough to count the
    /// symbols available, as T.88 defines it.
    /// </summary>
    /// <remarks>
    /// A region with one symbol therefore reads no bits at all for its
    /// identifiers, which is the standard's own arithmetic and not an edge case
    /// worked around: with one symbol there is nothing to say.
    /// </remarks>
    internal static int CodeLength(int symbolCount)
    {
        int bits = 0;
        while ((1 << bits) < symbolCount)
            bits++;

        return bits;
    }

    /// <summary>Draws a symbol onto the region with OR, clipped to the region.</summary>
    private static void Draw(Jbig2Bitmap region, Jbig2Bitmap symbol, long left, long top)
    {
        for (int row = 0; row < symbol.Height; row++)
        {
            long y = top + row;
            if (y < 0 || y >= region.Height)
                continue;

            int target = (int)y * region.Width;
            int source = row * symbol.Width;

            for (int column = 0; column < symbol.Width; column++)
            {
                long x = left + column;
                if (x < 0 || x >= region.Width)
                    continue;

                if (symbol.Pixels[source + column] != 0)
                    region.Pixels[target + (int)x] = 1;
            }
        }
    }

    private static Jbig2TextRegionResult Malformed(string message) =>
        new(Jbig2DecodeOutcome.Malformed, null, 0, 0, 0, message);

    private static Jbig2TextRegionResult Unsupported(string construct) =>
        new(Jbig2DecodeOutcome.Unsupported, null, 0, 0, 0, construct);
}
