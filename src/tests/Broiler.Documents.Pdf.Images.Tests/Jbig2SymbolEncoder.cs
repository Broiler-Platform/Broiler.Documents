using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// The arithmetic integer encoding procedure, so the decoder's has something to
/// be round-tripped against.
/// </summary>
/// <remarks>
/// It writes the same decision tree the decoder reads, which is the honest
/// statement of what the round trip proves: the two halves agree about where the
/// prefix ends and what to add to the magnitude. If both misread T.88 Annex A's
/// ranges the tests still pass, and the ranges are therefore written out as
/// literals in both files so a reviewer can compare them against the standard
/// rather than against each other.
/// </remarks>
internal sealed class Jbig2IntegerEncoder
{
    private readonly MqContexts _contexts = new(9);

    public void Encode(MqEncoder encoder, int value)
    {
        long magnitude = value < 0 ? -(long)value : value;
        Write(encoder, value < 0 ? 1 : 0, magnitude);
    }

    /// <summary>
    /// OOB: the sign bit set on a zero magnitude, which is how a height class or
    /// a strip says it has ended.
    /// </summary>
    public void EncodeOutOfBand(MqEncoder encoder) => Write(encoder, 1, 0, outOfBand: true);

    private void Write(MqEncoder encoder, int sign, long magnitude, bool outOfBand = false)
    {
        int prev = 1;

        void Bit(int bit)
        {
            encoder.Encode(_contexts, prev, bit);
            prev = prev < 256
                ? (prev << 1) | bit
                : ((((prev << 1) | bit) & 511) | 256);
        }

        void Magnitude(int bits, long offset)
        {
            long remainder = magnitude - offset;
            for (int i = bits - 1; i >= 0; i--)
                Bit((int)((remainder >> i) & 1));
        }

        Bit(sign);

        // A negative zero is OOB and nothing else, so an ordinary zero must be
        // written positive — which it is, since the sign came from the value.
        if (outOfBand || magnitude < 4)
        {
            Bit(0);
            Magnitude(2, 0);
        }
        else if (magnitude < 20)
        {
            Bit(1);
            Bit(0);
            Magnitude(4, 4);
        }
        else if (magnitude < 84)
        {
            Bit(1);
            Bit(1);
            Bit(0);
            Magnitude(6, 20);
        }
        else if (magnitude < 340)
        {
            Bit(1);
            Bit(1);
            Bit(1);
            Bit(0);
            Magnitude(8, 84);
        }
        else if (magnitude < 4436)
        {
            Bit(1);
            Bit(1);
            Bit(1);
            Bit(1);
            Bit(0);
            Magnitude(12, 340);
        }
        else
        {
            Bit(1);
            Bit(1);
            Bit(1);
            Bit(1);
            Bit(1);
            Magnitude(32, 4436);
        }
    }
}

/// <summary>The IAID procedure's encoding half: a fixed-width walk down the same tree.</summary>
internal sealed class Jbig2SymbolIdEncoder
{
    private readonly MqContexts _contexts;
    private readonly int _codeLength;

    public Jbig2SymbolIdEncoder(int codeLength)
    {
        _codeLength = codeLength;
        _contexts = new MqContexts(codeLength + 1);
    }

    public void Encode(MqEncoder encoder, int id)
    {
        int prev = 1;
        for (int i = _codeLength - 1; i >= 0; i--)
        {
            int bit = (id >> i) & 1;
            encoder.Encode(_contexts, prev, bit);
            prev = (prev << 1) | bit;
        }
    }
}

/// <summary>
/// Builds a symbol dictionary segment body around a set of bitmaps.
/// </summary>
/// <remarks>
/// <para>
/// Test-only, like every encoder here. Nothing in this repository writes JBIG2,
/// and symbol-substitution encoding specifically is a Post-V1 decision of its own
/// because a lossy one can silently change the characters in a scanned document.
/// </para>
/// <para>
/// It sorts the symbols into height classes because the format requires it: a
/// class is coded as one height and a run of width differences, so symbols of
/// unequal height cannot share one. The order that sorting produces is the order
/// the decoder will hand back, and therefore the order a text region's
/// identifiers count through, so it is returned rather than left implicit.
/// </para>
/// </remarks>
internal static class Jbig2SymbolDictionaryEncoder
{
    internal static byte[] Encode(
        IReadOnlyList<Jbig2Bitmap> symbols,
        out Jbig2Bitmap[] order,
        int template = 0,
        int unexportedPrefix = 0)
    {
        (int X, int Y)[] adaptive = Nominal(template);

        // Height classes ascending, and widths ascending within a class: the
        // differences the format codes are then all non-negative, which is what
        // a real encoder arranges for and what keeps this one simple.
        Jbig2Bitmap[] sorted = [.. symbols.OrderBy(symbol => symbol.Height).ThenBy(symbol => symbol.Width)];
        order = sorted;

        var encoder = new MqEncoder();
        var generic = new MqContexts(Jbig2GenericDecoder.GenericContextBits);
        var deltaHeight = new Jbig2IntegerEncoder();
        var deltaWidth = new Jbig2IntegerEncoder();
        var exportRun = new Jbig2IntegerEncoder();

        int height = 0;
        int index = 0;

        while (index < sorted.Length)
        {
            int classHeight = sorted[index].Height;
            deltaHeight.Encode(encoder, classHeight - height);
            height = classHeight;

            int width = 0;
            while (index < sorted.Length && sorted[index].Height == classHeight)
            {
                Jbig2Bitmap symbol = sorted[index];
                deltaWidth.Encode(encoder, symbol.Width - width);
                width = symbol.Width;

                Jbig2GenericEncoder.Encode(
                    encoder, generic, symbol.Pixels, symbol.Width, symbol.Height, template, adaptive: adaptive);

                index++;
            }

            deltaWidth.EncodeOutOfBand(encoder);
        }

        // The export flags as two runs: a prefix that is not exported, then the
        // rest that is. One run of each is enough to exercise the alternation,
        // and a prefix greater than zero is what proves a decoder reads them at
        // all rather than assuming a dictionary exports everything it defines.
        exportRun.Encode(encoder, unexportedPrefix);
        exportRun.Encode(encoder, sorted.Length - unexportedPrefix);

        var body = new List<byte>();
        AddUInt16(body, template << 10);

        foreach ((int x, int y) in adaptive)
        {
            body.Add((byte)(sbyte)x);
            body.Add((byte)(sbyte)y);
        }

        AddUInt32(body, sorted.Length - unexportedPrefix);
        AddUInt32(body, sorted.Length);
        body.AddRange(encoder.Flush());

        return [.. body];
    }

    /// <summary>
    /// Builds a dictionary whose symbols are corrections of ones it was given,
    /// which is the form an encoder reaches for when a shape recurs slightly
    /// changed.
    /// </summary>
    internal static byte[] EncodeRefining(
        IReadOnlyList<Jbig2Bitmap> input,
        IReadOnlyList<Jbig2RefinedSymbol> symbols,
        out Jbig2Bitmap[] order,
        int refinementTemplate = 0)
    {
        (int X, int Y)[] adaptive = Nominal(0);
        (int X, int Y)[] refinementAdaptive = [(-1, -1), (-1, -1)];

        Jbig2RefinedSymbol[] sorted =
            [.. symbols.OrderBy(symbol => symbol.Bitmap.Height).ThenBy(symbol => symbol.Bitmap.Width)];

        order = [.. sorted.Select(symbol => symbol.Bitmap)];

        var encoder = new MqEncoder();
        var deltaHeight = new Jbig2IntegerEncoder();
        var deltaWidth = new Jbig2IntegerEncoder();
        var exportRun = new Jbig2IntegerEncoder();
        var instances = new Jbig2IntegerEncoder();
        var refinementX = new Jbig2IntegerEncoder();
        var refinementY = new Jbig2IntegerEncoder();
        var identifiers = new Jbig2SymbolIdEncoder(Jbig2TextRegion.CodeLength(input.Count + sorted.Length));
        var refinementContexts = new MqContexts(Jbig2RefinementDecoder.RefinementContextBits);

        int height = 0;
        int index = 0;

        while (index < sorted.Length)
        {
            int classHeight = sorted[index].Bitmap.Height;
            deltaHeight.Encode(encoder, classHeight - height);
            height = classHeight;

            int width = 0;
            while (index < sorted.Length && sorted[index].Bitmap.Height == classHeight)
            {
                Jbig2RefinedSymbol symbol = sorted[index];
                deltaWidth.Encode(encoder, symbol.Bitmap.Width - width);
                width = symbol.Bitmap.Width;

                // One instance: the aggregate form, where a symbol is built from
                // several, is refused by the decoder and not written here.
                instances.Encode(encoder, 1);
                identifiers.Encode(encoder, symbol.ReferenceId);
                refinementX.Encode(encoder, symbol.Dx);
                refinementY.Encode(encoder, symbol.Dy);

                Jbig2Bitmap reference = symbol.ReferenceId < input.Count
                    ? input[symbol.ReferenceId]
                    : order[symbol.ReferenceId - input.Count];

                Jbig2RefinementEncoder.Encode(
                    encoder, refinementContexts, symbol.Bitmap, refinementTemplate,
                    typicalPrediction: false, reference, symbol.Dx, symbol.Dy, refinementAdaptive);

                index++;
            }

            deltaWidth.EncodeOutOfBand(encoder);
        }

        // The runs count through the symbols this dictionary was given before the
        // ones it defined, so a dictionary built on another's symbols has to skip
        // them explicitly rather than start at its own.
        exportRun.Encode(encoder, input.Count);
        exportRun.Encode(encoder, sorted.Length);

        var body = new List<byte>();

        // SDREFAGG in bit 1, the refinement template in bit 12.
        AddUInt16(body, 0x02 | (refinementTemplate << 12));

        foreach ((int x, int y) in adaptive)
        {
            body.Add((byte)(sbyte)x);
            body.Add((byte)(sbyte)y);
        }

        if (refinementTemplate == 0)
        {
            foreach ((int x, int y) in refinementAdaptive)
            {
                body.Add((byte)(sbyte)x);
                body.Add((byte)(sbyte)y);
            }
        }

        AddUInt32(body, sorted.Length);
        AddUInt32(body, sorted.Length);
        body.AddRange(encoder.Flush());

        return [.. body];
    }

    private static (int X, int Y)[] Nominal(int template) => template switch
    {
        0 => [(3, -1), (-3, -1), (2, -2), (-2, -2)],
        1 => [(3, -1)],
        _ => [(2, -1)],
    };

    private static void AddUInt16(List<byte> target, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        target.AddRange(bytes.ToArray());
    }

    private static void AddUInt32(List<byte> target, long value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        target.AddRange(bytes.ToArray());
    }
}

/// <summary>One placement a text region codes: a symbol, at a coded position.</summary>
/// <remarks>
/// S and T are the format's own coordinates rather than pixel positions. T is
/// the strip's axis and S the axis along it, and which corner of the symbol they
/// name is a property of the region — so the same instance draws in four
/// different places under the four reference corners, which is exactly what the
/// corner tests are for.
/// </remarks>
internal readonly record struct Jbig2Instance(int Id, int S, int T, Jbig2InstanceRefinement? Refine = null);

/// <summary>
/// A correction applied to one instance before it is drawn: the bitmap it should
/// become, and where the dictionary symbol sits under it.
/// </summary>
/// <remarks>
/// The size difference is not stated here because it is implied — the encoder
/// takes it from the two bitmaps, which is also how the decoder recovers the
/// refined size from the difference it reads.
/// </remarks>
internal sealed record Jbig2InstanceRefinement(Jbig2Bitmap Bitmap, int OffsetX = 0, int OffsetY = 0);

/// <summary>A dictionary symbol defined as a correction of another.</summary>
internal readonly record struct Jbig2RefinedSymbol(Jbig2Bitmap Bitmap, int ReferenceId, int Dx = 0, int Dy = 0);

/// <summary>Builds a text region segment body from a list of placements.</summary>
internal static class Jbig2TextRegionEncoder
{
    internal static byte[] Encode(
        int width,
        int height,
        IReadOnlyList<Jbig2Bitmap> symbols,
        IReadOnlyList<Jbig2Instance> instances,
        int x = 0,
        int y = 0,
        int corner = 1,
        bool transposed = false,
        int logStrips = 0,
        int spacingOffset = 0,
        bool refine = false,
        int refinementTemplate = 0)
    {
        // A region that carries a correction must declare that it refines, so the
        // flag is taken from the instances as well as from the caller: a test
        // asking for the flag without a correction is exercising the other case.
        refine |= instances.Any(instance => instance.Refine is not null);

        (int X, int Y)[] refinementAdaptive = [(-1, -1), (-1, -1)];
        var refinementFlag = new Jbig2IntegerEncoder();
        var refinementWidth = new Jbig2IntegerEncoder();
        var refinementHeight = new Jbig2IntegerEncoder();
        var refinementX = new Jbig2IntegerEncoder();
        var refinementY = new Jbig2IntegerEncoder();
        var refinementContexts = new MqContexts(Jbig2RefinementDecoder.RefinementContextBits);

        int strips = 1 << logStrips;
        var encoder = new MqEncoder();
        var deltaT = new Jbig2IntegerEncoder();
        var firstS = new Jbig2IntegerEncoder();
        var deltaS = new Jbig2IntegerEncoder();
        var instanceT = new Jbig2IntegerEncoder();
        var identifiers = new Jbig2SymbolIdEncoder(Jbig2TextRegion.CodeLength(symbols.Count));

        // The first strip's position is coded as a negative offset from zero, so
        // an encoder that starts the running coordinate at zero states zero.
        deltaT.Encode(encoder, 0);

        var strip = instances
            .GroupBy(instance => instance.T / strips * strips)
            .OrderBy(group => group.Key);

        int previousBase = 0;
        int previousFirst = 0;

        foreach (var group in strip)
        {
            deltaT.Encode(encoder, (group.Key - previousBase) / strips);
            previousBase = group.Key;

            Jbig2Instance[] placements = [.. group];
            firstS.Encode(encoder, placements[0].S - previousFirst);
            previousFirst = placements[0].S;

            int currentS = placements[0].S;

            for (int i = 0; i < placements.Length; i++)
            {
                if (i > 0)
                {
                    deltaS.Encode(encoder, placements[i].S - currentS - spacingOffset);
                    currentS = placements[i].S;
                }

                if (strips > 1)
                    instanceT.Encode(encoder, placements[i].T - group.Key);

                identifiers.Encode(encoder, placements[i].Id);

                Jbig2Bitmap symbol = symbols[placements[i].Id];
                Jbig2InstanceRefinement? correction = placements[i].Refine;

                if (refine)
                {
                    refinementFlag.Encode(encoder, correction is null ? 0 : 1);

                    if (correction is not null)
                    {
                        int deltaWidth = correction.Bitmap.Width - symbol.Width;
                        int deltaHeight = correction.Bitmap.Height - symbol.Height;

                        refinementWidth.Encode(encoder, deltaWidth);
                        refinementHeight.Encode(encoder, deltaHeight);
                        refinementX.Encode(encoder, correction.OffsetX);
                        refinementY.Encode(encoder, correction.OffsetY);

                        Jbig2RefinementEncoder.Encode(
                            encoder,
                            refinementContexts,
                            correction.Bitmap,
                            refinementTemplate,
                            typicalPrediction: false,
                            symbol,
                            (deltaWidth >> 1) + correction.OffsetX,
                            (deltaHeight >> 1) + correction.OffsetY,
                            refinementAdaptive);

                        symbol = correction.Bitmap;
                    }
                }

                // The decoder advances the running coordinate by the symbol's
                // extent either before placing it or after, depending on which
                // corner the position names — but never both, and never by a
                // different amount. So the encoder's running total does not
                // branch on the corner even though the placement does. What it
                // does take from the corrected symbol is the extent itself.
                currentS += (transposed ? symbol.Height : symbol.Width) - 1;
            }

            deltaS.EncodeOutOfBand(encoder);
        }

        int flags = logStrips << 2;
        flags |= corner << 4;
        flags |= transposed ? 0x40 : 0;
        flags |= (spacingOffset & 0x1F) << 10;
        flags |= refine ? 0x02 : 0;
        flags |= refinementTemplate << 15;

        var body = new List<byte>();
        AddUInt32(body, width);
        AddUInt32(body, height);
        AddUInt32(body, x);
        AddUInt32(body, y);
        body.Add(0);                    // external combination operator: OR
        AddUInt16(body, flags);

        if (refine && refinementTemplate == 0)
        {
            foreach ((int ax, int ay) in refinementAdaptive)
            {
                body.Add((byte)(sbyte)ax);
                body.Add((byte)(sbyte)ay);
            }
        }

        AddUInt32(body, instances.Count);
        body.AddRange(encoder.Flush());

        return [.. body];
    }

    private static void AddUInt16(List<byte> target, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        target.AddRange(bytes.ToArray());
    }

    private static void AddUInt32(List<byte> target, long value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        target.AddRange(bytes.ToArray());
    }
}
