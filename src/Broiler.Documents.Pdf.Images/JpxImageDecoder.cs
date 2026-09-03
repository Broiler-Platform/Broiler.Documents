using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Images;

/// <summary>What a decode produced, or why it did not.</summary>
internal sealed class JpxDecodeResult
{
    private JpxDecodeResult(byte[]? samples, int width, int height, int components, string? refusal)
    {
        Samples = samples;
        Width = width;
        Height = height;
        Components = components;
        Refusal = refusal;
    }

    public byte[]? Samples { get; }

    public int Width { get; }

    public int Height { get; }

    public int Components { get; }

    /// <summary>Null on success; otherwise a phrase naming the construct refused.</summary>
    public string? Refusal { get; }

    public static JpxDecodeResult Decoded(byte[] samples, int width, int height, int components) =>
        new(samples, width, height, components, null);

    public static JpxDecodeResult Refused(string refusal) => new(null, 0, 0, 0, refusal);
}

/// <summary>
/// Decodes a JPEG 2000 Part 1 codestream into 8-bit samples.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Scope is deliberately narrow and everything outside it is refused by
/// name.</strong> One tile, default precincts, the LRCP and RPCL progressions, no
/// packed packet headers, no region of interest, no progression-order changes.
/// Those are the shapes a PDF-embedded image usually has; the rest report what
/// they are instead of being decoded approximately, which is this codec's rule
/// everywhere else.
/// </para>
/// <para>
/// <strong>What is and is not evidenced.</strong> The wavelets and the component
/// transforms are tested by inversion and the tag tree by round trip against an
/// independently written encoder — those rest on evidence. The EBCOT context
/// tables and the packet-header field order rest on a reading of T.800 that
/// nothing here checks, because a JPEG 2000 encoder large enough to check them
/// would share the reading, the standard's test codestreams are official test
/// material, and no conforming file may be committed (IP-020).
/// </para>
/// <para>
/// The practical consequence: the first real image is the actual test, and until
/// one has been decoded successfully this should not be composed into any
/// application.
/// </para>
/// </remarks>
internal static class JpxImageDecoder
{
    private const int MarkerSoc = 0xFF4F;
    private const int MarkerSiz = 0xFF51;
    private const int MarkerCod = 0xFF52;
    private const int MarkerCoc = 0xFF53;
    private const int MarkerQcd = 0xFF5C;
    private const int MarkerQcc = 0xFF5D;
    private const int MarkerRgn = 0xFF5E;
    private const int MarkerPoc = 0xFF5F;
    private const int MarkerPpm = 0xFF60;
    private const int MarkerPpt = 0xFF61;
    private const int MarkerSot = 0xFF90;
    private const int MarkerSod = 0xFF93;
    private const int MarkerEoc = 0xFFD9;

    public static JpxDecodeResult Decode(ReadOnlySpan<byte> data, long maxSamples)
    {
        // A JP2 container wraps the codestream in boxes. Rather than walking them
        // a second time, find the codestream by its own opening pair: SOC is only
        // ever immediately followed by SIZ.
        int start = -1;
        for (int i = 0; i + 3 < data.Length; i++)
        {
            if (Read16(data, i) == MarkerSoc && Read16(data, i + 2) == MarkerSiz)
            {
                start = i;
                break;
            }
        }

        if (start < 0)
            return JpxDecodeResult.Refused("no codestream begins with SOC followed by SIZ");

        data = data[start..];
        var image = new Image();
        int position = 2;
        int tileDataStart = -1;
        int tileDataEnd = -1;

        while (position + 1 < data.Length)
        {
            int marker = Read16(data, position);
            position += 2;

            if (marker == MarkerEoc)
                break;

            if (marker == MarkerSod)
            {
                // Everything to the next marker segment is this tile-part's data.
                tileDataStart = position;
                tileDataEnd = data.Length;
                break;
            }

            if (position + 1 >= data.Length)
                return JpxDecodeResult.Refused("a marker segment runs past the end of the codestream");

            int length = Read16(data, position);
            if (length < 2 || position + length > data.Length)
                return JpxDecodeResult.Refused("a marker segment declares a length the codestream does not hold");

            ReadOnlySpan<byte> body = data.Slice(position + 2, length - 2);

            switch (marker)
            {
                case MarkerSiz:
                    if (ReadSiz(body, image) is string sizRefusal)
                        return JpxDecodeResult.Refused(sizRefusal);
                    break;

                case MarkerCod:
                    if (ReadCod(body, image) is string codRefusal)
                        return JpxDecodeResult.Refused(codRefusal);
                    break;

                case MarkerQcd:
                    ReadQcd(body, image);
                    break;

                case MarkerSot:
                    if (body.Length >= 2 && Read16(body, 0) != 0)
                        return JpxDecodeResult.Refused("the codestream carries more than one tile");
                    break;

                case MarkerCoc:
                case MarkerQcc:
                    return JpxDecodeResult.Refused("a per-component coding or quantization override");

                case MarkerRgn:
                    return JpxDecodeResult.Refused("a region of interest");

                case MarkerPoc:
                    return JpxDecodeResult.Refused("a progression order change");

                case MarkerPpm:
                case MarkerPpt:
                    return JpxDecodeResult.Refused("packed packet headers");
            }

            position += length;
        }

        if (tileDataStart < 0)
            return JpxDecodeResult.Refused("the codestream carries no tile data");
        if (image.Width <= 0 || image.Components == 0)
            return JpxDecodeResult.Refused("the codestream states no usable image size");
        if ((long)image.Width * image.Height * image.Components > maxSamples)
            return JpxDecodeResult.Refused("the image would exceed this stage's decoded-byte ceiling");

        return Reconstruct(image, data[tileDataStart..tileDataEnd], maxSamples);
    }

    // ---- marker segments --------------------------------------------------------

    private static string? ReadSiz(ReadOnlySpan<byte> body, Image image)
    {
        if (body.Length < 36)
            return "a truncated SIZ segment";

        int xsiz = (int)Read32(body, 2);
        int ysiz = (int)Read32(body, 6);
        int x0 = (int)Read32(body, 10);
        int y0 = (int)Read32(body, 14);
        int xtsiz = (int)Read32(body, 18);
        int ytsiz = (int)Read32(body, 22);

        image.Width = xsiz - x0;
        image.Height = ysiz - y0;
        image.Components = Read16(body, 34);

        if (image.Components is < 1 or > 4)
            return "a component count outside one to four";
        if (body.Length < 36 + (image.Components * 3))
            return "a SIZ segment shorter than its component list";

        // One tile covering the image is the supported shape.
        if (xtsiz < image.Width || ytsiz < image.Height)
            return "a codestream divided into multiple tiles";

        image.Depth = new int[image.Components];
        image.Signed = new bool[image.Components];

        for (int i = 0; i < image.Components; i++)
        {
            byte ssiz = body[36 + (i * 3)];
            image.Depth[i] = (ssiz & 0x7F) + 1;
            image.Signed[i] = (ssiz & 0x80) != 0;

            if (body[37 + (i * 3)] != 1 || body[38 + (i * 3)] != 1)
                return "a component with subsampling";
            if (image.Depth[i] > 16)
                return "a component deeper than sixteen bits";
        }

        return null;
    }

    private static string? ReadCod(ReadOnlySpan<byte> body, Image image)
    {
        if (body.Length < 10)
            return "a truncated COD segment";

        byte scod = body[0];
        if ((scod & 0x01) != 0)
            return "user-defined precincts";
        if ((scod & 0x02) != 0)
            return "SOP marker segments";
        if ((scod & 0x04) != 0)
            return "EPH marker segments";

        image.Progression = body[1];
        image.Layers = Read16(body, 2);
        image.MultipleComponentTransform = body[4] != 0;
        image.Levels = body[5];
        image.CodeBlockWidth = 1 << ((body[6] & 0x0F) + 2);
        image.CodeBlockHeight = 1 << ((body[7] & 0x0F) + 2);
        image.Reversible = body[9] == 1;

        if (image.Progression is not (0 or 2))
            return "a progression order other than LRCP or RPCL";
        if (body[8] != 0)
            return "code-block style options";
        if (image.Levels > 32)
            return "more decomposition levels than the format allows";

        return null;
    }

    private static void ReadQcd(ReadOnlySpan<byte> body, Image image)
    {
        if (body.Length < 1)
            return;

        int style = body[0] & 0x1F;
        image.GuardBits = body[0] >> 5;
        image.QuantizationStyle = style;

        var exponents = new List<int>();
        var mantissas = new List<int>();

        if (style == 0)
        {
            for (int i = 1; i < body.Length; i++)
            {
                exponents.Add(body[i] >> 3);
                mantissas.Add(0);
            }
        }
        else
        {
            for (int i = 1; i + 1 < body.Length; i += 2)
            {
                int value = Read16(body, i);
                exponents.Add(value >> 11);
                mantissas.Add(value & 0x7FF);
            }
        }

        image.Exponents = [.. exponents];
        image.Mantissas = [.. mantissas];
    }

    // ---- reconstruction ---------------------------------------------------------

    private static JpxDecodeResult Reconstruct(Image image, ReadOnlySpan<byte> tileData, long maxSamples)
    {
        int width = image.Width;
        int height = image.Height;
        var planes = new float[image.Components][];

        // Every component shares the geometry, so the resolution and subband
        // layout is computed once and reused.
        List<Resolution> resolutions = BuildResolutions(image);
        var reader = new JpxBitReader(tileData.ToArray());
        var blocks = new Dictionary<(int Component, int Resolution, int Band, int Index), JpxCodeBlock>();
        var trees = new Dictionary<(int Component, int Resolution, int Band), (JpxTagTree Inclusion, JpxTagTree Planes)>();

        if (!ReadPackets(image, resolutions, reader, blocks, trees, tileData.Length))
            return JpxDecodeResult.Refused("a packet header this build could not follow");

        for (int c = 0; c < image.Components; c++)
        {
            var coefficients = new float[width * height];

            foreach (Resolution resolution in resolutions)
            {
                foreach (Band band in resolution.Bands)
                {
                    foreach ((int index, JpxCodeBlock block) in band.Blocks)
                    {
                        if (!blocks.TryGetValue((c, resolution.Level, band.Index, index), out JpxCodeBlock? state) ||
                            state.Contributions.Count == 0)
                        {
                            continue;
                        }

                        byte[] data = Concatenate(tileData, state.Contributions);
                        int planeCount = image.GuardBits + Exponent(image, resolution.Level, band.Index) - 1;

                        int[]? values = JpxBlockDecoder.Decode(
                            data, block.Width, block.Height, state.Passes,
                            state.MissingBitPlanes, Math.Max(1, planeCount), band.Subband);

                        if (values is null)
                            continue;

                        Place(coefficients, width, height, band, block, values, image, resolution.Level);
                    }
                }
            }

            // Inverse transform, coarsest level outward.
            for (int level = image.Levels; level >= 1; level--)
            {
                int lowWidth = CeilShift(width, level);
                int lowHeight = CeilShift(height, level);
                int fullWidth = CeilShift(width, level - 1);
                int fullHeight = CeilShift(height, level - 1);

                JpxWavelet.InverseLevel(coefficients, fullWidth, fullHeight, lowWidth, lowHeight, image.Reversible);
            }

            planes[c] = coefficients;
        }

        if (image.MultipleComponentTransform && image.Components >= 3)
        {
            if (image.Reversible)
                JpxComponentTransform.InverseReversible(planes[0], planes[1], planes[2]);
            else
                JpxComponentTransform.InverseIrreversible(planes[0], planes[1], planes[2]);
        }

        // Straight-alpha RGBA, which is what the composed image filters hand the
        // codec and what the model's projection understands.
        long required = (long)width * height * 4;
        if (required > maxSamples)
            return JpxDecodeResult.Refused("the image would exceed this stage's decoded-byte ceiling");

        bool colour = image.Components >= 3;
        var samples = new byte[required];

        for (int i = 0; i < width * height; i++)
        {
            byte r = Sample(planes[0], i, image.Depth[0]);
            byte g = colour ? Sample(planes[1], i, image.Depth[1]) : r;
            byte b = colour ? Sample(planes[2], i, image.Depth[2]) : r;

            int at = i * 4;
            samples[at] = r;
            samples[at + 1] = g;
            samples[at + 2] = b;
            samples[at + 3] = 255;
        }

        return JpxDecodeResult.Decoded(samples, width, height, colour ? 3 : 1);
    }

    /// <summary>
    /// One coefficient as an eight-bit level. The DC level shift the encoder
    /// applied is undone here, and the result scaled from the component's own
    /// depth.
    /// </summary>
    private static byte Sample(float[] plane, int index, int depth)
    {
        int shift = 1 << (depth - 1);
        float scale = 255f / ((1 << depth) - 1);
        float value = (plane[index] + shift) * scale;
        return value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5f);
    }

    private static byte[] Concatenate(ReadOnlySpan<byte> tileData, List<JpxBlockContribution> contributions)
    {
        int total = 0;
        foreach (JpxBlockContribution contribution in contributions)
            total += contribution.Length;

        var data = new byte[total];
        int at = 0;
        foreach (JpxBlockContribution contribution in contributions)
        {
            if (contribution.Offset + contribution.Length > tileData.Length)
                break;

            tileData.Slice(contribution.Offset, contribution.Length).CopyTo(data.AsSpan(at));
            at += contribution.Length;
        }

        return data;
    }

    private static int Exponent(Image image, int level, int band)
    {
        int index = level == 0 ? 0 : (3 * (level - 1)) + band + 1;
        return image.Exponents.Length > index ? image.Exponents[index] : 8;
    }

    private static void Place(
        float[] coefficients,
        int width,
        int height,
        Band band,
        JpxCodeBlock block,
        int[] values,
        Image image,
        int level)
    {
        // Dequantize into the quadrant this subband occupies.
        float step = 1f;
        if (!image.Reversible)
        {
            int exponent = Exponent(image, level, band.Index);
            step = MathF.Pow(2, image.Depth[0] - exponent);
        }

        for (int y = 0; y < block.Height; y++)
        {
            int targetY = band.Y + block.Y + y;
            if (targetY >= height)
                break;

            for (int x = 0; x < block.Width; x++)
            {
                int targetX = band.X + block.X + x;
                if (targetX >= width)
                    break;

                coefficients[(targetY * width) + targetX] = values[(y * block.Width) + x] * step;
            }
        }
    }

    private static int CeilShift(int value, int shift) => (value + (1 << shift) - 1) >> shift;

    // ---- geometry and packets ---------------------------------------------------

    private static List<Resolution> BuildResolutions(Image image)
    {
        var resolutions = new List<Resolution>();

        for (int r = 0; r <= image.Levels; r++)
        {
            var bands = new List<Band>();
            int level = image.Levels - r;

            if (r == 0)
            {
                bands.Add(MakeBand(image, 0, JpxSubband.Ll, 0, 0, CeilShift(image.Width, image.Levels), CeilShift(image.Height, image.Levels)));
            }
            else
            {
                int lowWidth = CeilShift(image.Width, level + 1);
                int lowHeight = CeilShift(image.Height, level + 1);
                int fullWidth = CeilShift(image.Width, level);
                int fullHeight = CeilShift(image.Height, level);

                bands.Add(MakeBand(image, 0, JpxSubband.Hl, lowWidth, 0, fullWidth - lowWidth, lowHeight));
                bands.Add(MakeBand(image, 1, JpxSubband.Lh, 0, lowHeight, lowWidth, fullHeight - lowHeight));
                bands.Add(MakeBand(image, 2, JpxSubband.Hh, lowWidth, lowHeight, fullWidth - lowWidth, fullHeight - lowHeight));
            }

            resolutions.Add(new Resolution(r, bands));
        }

        return resolutions;
    }

    private static Band MakeBand(Image image, int index, JpxSubband subband, int x, int y, int width, int height)
    {
        var blocks = new List<(int, JpxCodeBlock)>();
        width = Math.Max(0, width);
        height = Math.Max(0, height);

        int across = width == 0 ? 0 : (width + image.CodeBlockWidth - 1) / image.CodeBlockWidth;
        int down = height == 0 ? 0 : (height + image.CodeBlockHeight - 1) / image.CodeBlockHeight;

        for (int by = 0; by < down; by++)
        {
            for (int bx = 0; bx < across; bx++)
            {
                blocks.Add(((by * across) + bx, new JpxCodeBlock
                {
                    X = bx * image.CodeBlockWidth,
                    Y = by * image.CodeBlockHeight,
                    Width = Math.Min(image.CodeBlockWidth, width - (bx * image.CodeBlockWidth)),
                    Height = Math.Min(image.CodeBlockHeight, height - (by * image.CodeBlockHeight)),
                }));
            }
        }

        return new Band(index, subband, x, y, width, height, across, down, blocks);
    }

    /// <summary>
    /// Walks the packets in progression order, reading each header and recording
    /// where each code-block's data sits.
    /// </summary>
    private static bool ReadPackets(
        Image image,
        List<Resolution> resolutions,
        JpxBitReader reader,
        Dictionary<(int, int, int, int), JpxCodeBlock> blocks,
        Dictionary<(int, int, int), (JpxTagTree Inclusion, JpxTagTree Planes)> trees,
        int dataLength)
    {
        // LRCP and RPCL differ only in which of layer and resolution is outermost;
        // with one tile and one precinct per resolution the rest collapses.
        var order = new List<(int Layer, int Resolution, int Component)>();
        if (image.Progression == 0)
        {
            for (int l = 0; l < image.Layers; l++)
                for (int r = 0; r < resolutions.Count; r++)
                    for (int c = 0; c < image.Components; c++)
                        order.Add((l, r, c));
        }
        else
        {
            for (int r = 0; r < resolutions.Count; r++)
                for (int l = 0; l < image.Layers; l++)
                    for (int c = 0; c < image.Components; c++)
                        order.Add((l, r, c));
        }

        foreach ((int layer, int r, int c) in order)
        {
            if (!reader.TryReadBit(out int present))
                return true;   // the codestream ended early; what was read stands

            if (present == 0)
            {
                reader.AlignToByte();
                continue;
            }

            Resolution resolution = resolutions[r];
            var lengths = new List<(JpxCodeBlock State, int Passes)>();

            foreach (Band band in resolution.Bands)
            {
                if (!trees.TryGetValue((c, r, band.Index), out (JpxTagTree Inclusion, JpxTagTree Planes) tree))
                {
                    tree = (new JpxTagTree(Math.Max(1, band.Across), Math.Max(1, band.Down)),
                            new JpxTagTree(Math.Max(1, band.Across), Math.Max(1, band.Down)));
                    trees[(c, r, band.Index)] = tree;
                }

                foreach ((int index, JpxCodeBlock block) in band.Blocks)
                {
                    var key = (c, r, band.Index, index);
                    if (!blocks.TryGetValue(key, out JpxCodeBlock? state))
                    {
                        state = new JpxCodeBlock { X = block.X, Y = block.Y, Width = block.Width, Height = block.Height };
                        blocks[key] = state;
                    }

                    int bx = index % Math.Max(1, band.Across);
                    int by = index / Math.Max(1, band.Across);

                    bool included;
                    if (state.Included)
                    {
                        if (!reader.TryReadBit(out int bit))
                            return true;
                        included = bit == 1;
                    }
                    else
                    {
                        if (!tree.Inclusion.TryDecode(reader, bx, by, layer + 1, out _))
                            return true;
                        included = tree.Inclusion.IsKnown(bx, by);
                    }

                    if (!included)
                        continue;

                    if (!state.Included)
                    {
                        // First inclusion also carries the zero bit-planes.
                        int threshold = 1;
                        while (!tree.Planes.IsKnown(bx, by))
                        {
                            if (!tree.Planes.TryDecode(reader, bx, by, threshold, out _))
                                return true;
                            if (tree.Planes.IsKnown(bx, by))
                                break;
                            threshold++;
                            if (threshold > 64)
                                return false;
                        }

                        tree.Planes.TryDecode(reader, bx, by, threshold, out int missing);
                        state.MissingBitPlanes = missing;
                        state.Included = true;
                    }

                    if (!TryReadPassCount(reader, out int passes))
                        return true;

                    // The length signalling grows by explicit signals rather than
                    // being fixed, which is what LBlock accumulates.
                    while (true)
                    {
                        if (!reader.TryReadBit(out int more))
                            return true;
                        if (more == 0)
                            break;
                        state.LengthBits++;
                    }

                    int bits = state.LengthBits + (int)Math.Floor(Math.Log2(Math.Max(1, passes)));
                    if (!reader.TryReadBits(bits, out int length))
                        return true;

                    lengths.Add((state, passes));
                    state.Contributions.Add(new JpxBlockContribution { Passes = passes, Length = length });
                    state.Passes += passes;
                }
            }

            reader.AlignToByte();

            // Body follows the header, in the order the header listed.
            int offset = reader.Position;
            foreach ((JpxCodeBlock state, int _) in lengths)
            {
                JpxBlockContribution contribution = state.Contributions[^1];
                contribution.Offset = offset;
                offset += contribution.Length;
                if (offset > dataLength)
                    return true;
            }

            if (!Skip(reader, offset))
                return true;
        }

        return true;
    }

    /// <summary>The pass-count code, T.800 Table B.4.</summary>
    private static bool TryReadPassCount(JpxBitReader reader, out int passes)
    {
        passes = 0;
        if (!reader.TryReadBit(out int b))
            return false;
        if (b == 0)
        {
            passes = 1;
            return true;
        }

        if (!reader.TryReadBit(out b))
            return false;
        if (b == 0)
        {
            passes = 2;
            return true;
        }

        if (!reader.TryReadBits(2, out int two))
            return false;
        if (two < 3)
        {
            passes = 3 + two;
            return true;
        }

        if (!reader.TryReadBits(5, out int five))
            return false;
        if (five < 31)
        {
            passes = 6 + five;
            return true;
        }

        if (!reader.TryReadBits(7, out int seven))
            return false;

        passes = 37 + seven;
        return true;
    }

    private static bool Skip(JpxBitReader reader, int target)
    {
        while (reader.Position < target)
        {
            if (!reader.TryReadBits(8, out _))
                return false;
        }

        return true;
    }

    private static int Read16(ReadOnlySpan<byte> data, int at) =>
        at + 1 < data.Length ? (data[at] << 8) | data[at + 1] : 0;

    private static uint Read32(ReadOnlySpan<byte> data, int at) =>
        at + 3 < data.Length
            ? ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3]
            : 0;

    private sealed class Image
    {
        public int Width;
        public int Height;
        public int Components;
        public int[] Depth = [];
        public bool[] Signed = [];
        public int Progression;
        public int Layers = 1;
        public int Levels;
        public int CodeBlockWidth = 64;
        public int CodeBlockHeight = 64;
        public bool Reversible = true;
        public bool MultipleComponentTransform;
        public int GuardBits = 2;
        public int QuantizationStyle;
        public int[] Exponents = [];
        public int[] Mantissas = [];
    }

    private sealed record Resolution(int Level, List<Band> Bands);

    private sealed record Band(
        int Index,
        JpxSubband Subband,
        int X,
        int Y,
        int Width,
        int Height,
        int Across,
        int Down,
        List<(int Index, JpxCodeBlock Block)> Blocks);
}
