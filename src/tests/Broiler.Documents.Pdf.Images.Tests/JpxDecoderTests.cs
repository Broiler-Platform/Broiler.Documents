namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers the JPEG 2000 decoder, and is explicit about which parts it can prove.
/// </summary>
/// <remarks>
/// <para>
/// The tests divide into two kinds, and the division is the useful thing here.
/// </para>
/// <para>
/// <strong>Tested by inversion, which is evidence.</strong> The wavelets and the
/// component transforms are invertible by construction. A forward transform
/// written here and undone by the decoder's inverse is not two readings of the
/// same sentence agreeing with each other: if the lifting steps were wrong, an
/// independently written forward step would not undo them. The tag tree is close
/// to the same footing — its encoder is short enough to be an independent
/// reading, and the values it round-trips are integers rather than a bitstream.
/// </para>
/// <para>
/// <strong>Not tested at all.</strong> EBCOT tier-1's context tables and the
/// packet-header field order. Nothing here reaches them, and nothing available
/// inside the repository's rules could: an encoder able to exercise them is as
/// large as the decoder and would share its reading, the standard's test
/// codestreams are official test material, and no conforming file may be
/// committed (IP-020). What is asserted about them is only that malformed and
/// out-of-scope input is refused by name rather than decoded into a guess.
/// </para>
/// </remarks>
public sealed class JpxDecoderTests
{
    // ---- tested by inversion ----------------------------------------------------

    [Theory]
    [InlineData(8, 8)]
    [InlineData(1, 16)]
    [InlineData(31, 7)]
    [InlineData(64, 64)]
    public void The_Reversible_Wavelet_Inverts_Exactly(int width, int height)
    {
        // 5/3 is integer-exact, so this is equality rather than tolerance. A
        // lifting step written wrongly here fails against an independently
        // written forward transform.
        float[] original = Ramp(width, height);
        float[] working = (float[])original.Clone();

        ForwardReversible(working, width, height);
        JpxWavelet.InverseLevel(working, width, height, (width + 1) / 2, (height + 1) / 2, reversible: true);

        for (int i = 0; i < original.Length; i++)
            Assert.Equal(original[i], working[i], 3);
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(33, 9)]
    public void The_Irreversible_Wavelet_Inverts_Within_Tolerance(int width, int height)
    {
        float[] original = Ramp(width, height);
        float[] working = (float[])original.Clone();

        ForwardIrreversible(working, width, height);
        JpxWavelet.InverseLevel(working, width, height, (width + 1) / 2, (height + 1) / 2, reversible: false);

        for (int i = 0; i < original.Length; i++)
            Assert.Equal(original[i], working[i], 1);
    }

    [Fact]
    public void The_Reversible_Colour_Transform_Inverts_Exactly()
    {
        float[] r = [0, 17, 255, 128, 64];
        float[] g = [0, 200, 12, 128, 33];
        float[] b = [0, 99, 47, 128, 210];

        float[] y = new float[5];
        float[] u = new float[5];
        float[] v = new float[5];

        // The forward RCT, written here from its own definition.
        for (int i = 0; i < 5; i++)
        {
            y[i] = MathF.Floor((r[i] + (2 * g[i]) + b[i]) / 4);
            u[i] = b[i] - g[i];
            v[i] = r[i] - g[i];
        }

        JpxComponentTransform.InverseReversible(y, u, v);

        Assert.Equal(r, y);
        Assert.Equal(g, u);
        Assert.Equal(b, v);
    }

    [Fact]
    public void The_Irreversible_Colour_Transform_Inverts_Within_Tolerance()
    {
        float[] r = [10, 200, 90];
        float[] g = [20, 40, 180];
        float[] b = [30, 130, 70];

        float[] y = new float[3];
        float[] cb = new float[3];
        float[] cr = new float[3];

        for (int i = 0; i < 3; i++)
        {
            y[i] = (0.299f * r[i]) + (0.587f * g[i]) + (0.114f * b[i]);
            cb[i] = (-0.16875f * r[i]) - (0.33126f * g[i]) + (0.5f * b[i]);
            cr[i] = (0.5f * r[i]) - (0.41869f * g[i]) - (0.08131f * b[i]);
        }

        JpxComponentTransform.InverseIrreversible(y, cb, cr);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(r[i], y[i], 2);
            Assert.Equal(g[i], cb[i], 2);
            Assert.Equal(b[i], cr[i], 2);
        }
    }

    [Fact]
    public void A_Tag_Tree_Round_Trips_Its_Values()
    {
        // Values chosen so the tree has real internal structure rather than a
        // single level: a flat tree would pass whatever the descent did.
        int[,] values = { { 1, 3, 2, 0 }, { 0, 2, 4, 1 } };
        byte[] encoded = TagTreeEncoder.Encode(values, 4, 2);

        var tree = new JpxTagTree(4, 2);
        var reader = new JpxBitReader(encoded);

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                // Raise the threshold until the leaf resolves, which is how a
                // packet header interrogates the tree.
                int threshold = 0;
                int value = 0;
                while (!tree.IsKnown(x, y) && threshold <= 8)
                {
                    threshold++;
                    Assert.True(tree.TryDecode(reader, x, y, threshold, out value));
                }

                Assert.True(tree.IsKnown(x, y));
                Assert.Equal(values[y, x], value);
            }
        }
    }

    // ---- refusals, which is all that is asserted about the rest ------------------

    [Theory]
    [InlineData("no codestream begins with SOC")]
    public void Data_That_Is_Not_A_Codestream_Is_Refused(string expected)
    {
        JpxDecodeResult result = JpxImageDecoder.Decode(new byte[64], 1 << 20);

        Assert.Null(result.Samples);
        Assert.Contains(expected, result.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Truncated_Codestream_Is_Refused_Rather_Than_Decoded_Partly()
    {
        byte[] codestream = [0xFF, 0x4F, 0xFF, 0x51, 0x00, 0x08];

        JpxDecodeResult result = JpxImageDecoder.Decode(codestream, 1 << 20);

        Assert.Null(result.Samples);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public void An_Out_Of_Scope_Construct_Is_Refused_By_Name()
    {
        // A region of interest. The decoder does not attempt it and says which
        // construct stopped it, rather than decoding the rest and being wrong
        // quietly — the same rule the image normalizer follows.
        byte[] codestream = [.. Soc(), .. Siz(16, 16, 1), .. Marker(0xFF5E, [0x00])];

        JpxDecodeResult result = JpxImageDecoder.Decode(codestream, 1 << 20);

        Assert.Null(result.Samples);
        Assert.Contains("region of interest", result.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Multi_Tile_Codestream_Is_Refused_By_Name()
    {
        byte[] codestream = [.. Soc(), .. Siz(64, 64, 1, tileWidth: 32, tileHeight: 32)];

        JpxDecodeResult result = JpxImageDecoder.Decode(codestream, 1 << 20);

        Assert.Contains("multiple tiles", result.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Subsampled_Component_Is_Refused_By_Name()
    {
        byte[] codestream = [.. Soc(), .. Siz(16, 16, 1, subsampling: 2)];

        JpxDecodeResult result = JpxImageDecoder.Decode(codestream, 1 << 20);

        Assert.Contains("subsampling", result.Refusal!, StringComparison.Ordinal);
    }

    // ---- fixtures ---------------------------------------------------------------

    private static float[] Ramp(int width, int height)
    {
        var data = new float[width * height];
        for (int i = 0; i < data.Length; i++)
            data[i] = ((i * 37) % 211) - 105;
        return data;
    }

    /// <summary>The forward 5/3, written here so the inverse has something to undo.</summary>
    private static void ForwardReversible(float[] data, int width, int height)
    {
        Forward1D(data, width, height, reversible: true);
    }

    private static void ForwardIrreversible(float[] data, int width, int height)
    {
        Forward1D(data, width, height, reversible: false);
    }

    private static void Forward1D(float[] data, int width, int height, bool reversible)
    {
        // Columns then rows, the inverse of the decoder's order.
        var column = new float[height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                column[y] = data[(y * width) + x];

            ForwardLine(column, height, reversible);
            Deinterleave(column, height, data, x, width);
        }

        var row = new float[width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                row[x] = data[(y * width) + x];

            ForwardLine(row, width, reversible);
            Deinterleave(row, width, data, y * width, 1);
        }
    }

    private static void ForwardLine(float[] line, int length, bool reversible)
    {
        if (length == 1)
        {
            if (!reversible)
                line[0] *= 1.230174104914001f;
            return;
        }

        if (reversible)
        {
            for (int i = 1; i < length; i += 2)
                line[i] -= MathF.Floor((At(line, length, i - 1) + At(line, length, i + 1)) / 2);
            for (int i = 0; i < length; i += 2)
                line[i] += MathF.Floor((At(line, length, i - 1) + At(line, length, i + 1) + 2) / 4);
            return;
        }

        const float alpha = -1.586134342059924f;
        const float beta = -0.052980118572961f;
        const float gamma = 0.882911075530934f;
        const float delta = 0.443506852043971f;
        const float kappa = 1.230174104914001f;

        for (int i = 1; i < length; i += 2)
            line[i] += alpha * (At(line, length, i - 1) + At(line, length, i + 1));
        for (int i = 0; i < length; i += 2)
            line[i] += beta * (At(line, length, i - 1) + At(line, length, i + 1));
        for (int i = 1; i < length; i += 2)
            line[i] += gamma * (At(line, length, i - 1) + At(line, length, i + 1));
        for (int i = 0; i < length; i += 2)
            line[i] += delta * (At(line, length, i - 1) + At(line, length, i + 1));
        for (int i = 0; i < length; i += 2)
            line[i] /= kappa;
        for (int i = 1; i < length; i += 2)
            line[i] *= kappa;
    }

    /// <summary>Interleaved lifting output back into low-then-high quadrant order.</summary>
    private static void Deinterleave(float[] line, int length, float[] destination, int offset, int stride)
    {
        int low = (length + 1) / 2;
        var ordered = new float[length];

        for (int i = 0; i < low; i++)
            ordered[i] = line[2 * i];
        for (int i = 0; i + low < length; i++)
            ordered[low + i] = line[(2 * i) + 1];

        for (int i = 0; i < length; i++)
            destination[offset + (i * stride)] = ordered[i];
    }

    private static float At(float[] line, int length, int index)
    {
        if (index < 0)
            index = -index;
        if (index >= length)
            index = (2 * length) - index - 2;
        return index >= 0 && index < length ? line[index] : 0;
    }

    private static byte[] Soc() => [0xFF, 0x4F];

    private static byte[] Marker(int code, byte[] body)
    {
        var segment = new List<byte> { (byte)(code >> 8), (byte)code };
        int length = body.Length + 2;
        segment.Add((byte)(length >> 8));
        segment.Add((byte)length);
        segment.AddRange(body);
        return [.. segment];
    }

    private static byte[] Siz(
        int width,
        int height,
        int components,
        int tileWidth = 0,
        int tileHeight = 0,
        int subsampling = 1)
    {
        var body = new List<byte>();
        Add16(body, 0);                                   // Rsiz: Part 1
        Add32(body, width);
        Add32(body, height);
        Add32(body, 0);
        Add32(body, 0);
        Add32(body, tileWidth == 0 ? width : tileWidth);
        Add32(body, tileHeight == 0 ? height : tileHeight);
        Add32(body, 0);
        Add32(body, 0);
        Add16(body, components);

        for (int i = 0; i < components; i++)
        {
            body.Add(7);                                  // 8-bit unsigned
            body.Add((byte)subsampling);
            body.Add((byte)subsampling);
        }

        return Marker(0xFF51, [.. body]);
    }

    private static void Add16(List<byte> data, int value)
    {
        data.Add((byte)(value >> 8));
        data.Add((byte)value);
    }

    private static void Add32(List<byte> data, int value)
    {
        data.Add((byte)(value >> 24));
        data.Add((byte)(value >> 16));
        data.Add((byte)(value >> 8));
        data.Add((byte)value);
    }
}

/// <summary>A tag-tree encoder, written from the coding procedure.</summary>
internal static class TagTreeEncoder
{
    internal static byte[] Encode(int[,] values, int width, int height)
    {
        // Build the levels the decoder walks, each holding the minimum of the
        // four below it.
        var levels = new List<int[,]>();
        var current = new int[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                current[y, x] = values[y, x];

        levels.Add(current);
        int w = width;
        int h = height;

        while (w > 1 || h > 1)
        {
            int nw = (w + 1) / 2;
            int nh = (h + 1) / 2;
            var next = new int[nh, nw];

            for (int y = 0; y < nh; y++)
            {
                for (int x = 0; x < nw; x++)
                {
                    int min = int.MaxValue;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                            if ((2 * y) + dy < h && (2 * x) + dx < w)
                                min = Math.Min(min, current[(2 * y) + dy, (2 * x) + dx]);
                    next[y, x] = min;
                }
            }

            levels.Add(next);
            current = next;
            w = nw;
            h = nh;
        }

        var bits = new List<int>();
        var emitted = new HashSet<(int Level, int X, int Y)>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int low = 0;
                for (int level = levels.Count - 1; level >= 0; level--)
                {
                    int lx = x >> level;
                    int ly = y >> level;
                    int value = levels[level][ly, lx];

                    if (!emitted.Add((level, lx, ly)))
                    {
                        low = value;
                        continue;
                    }

                    for (int i = low; i < value; i++)
                        bits.Add(0);
                    bits.Add(1);
                    low = value;
                }
            }
        }

        return Pack(bits);
    }

    /// <summary>Packs bits with the stuffing rule the reader undoes.</summary>
    private static byte[] Pack(List<int> bits)
    {
        var output = new List<byte>();
        int current = 0;
        int count = 0;
        int previous = 0;

        foreach (int bit in bits)
        {
            int capacity = previous == 0xFF ? 7 : 8;
            current = (current << 1) | bit;
            count++;

            if (count == capacity)
            {
                output.Add((byte)current);
                previous = current;
                current = 0;
                count = 0;
            }
        }

        if (count > 0)
        {
            int capacity = previous == 0xFF ? 7 : 8;
            output.Add((byte)(current << (capacity - count)));
        }

        return [.. output];
    }
}
