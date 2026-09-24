using System;
using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Images;

/// <summary>How a table's normalized output is read as a connection-space value.</summary>
internal enum IccPcsEncoding
{
    /// <summary>CIEXYZ, where the full range stands for just under two.</summary>
    Xyz,

    /// <summary>CIELAB in the eight-bit and version-4 encoding: L* over 100, a* and b* over 255 from -128.</summary>
    Lab,

    /// <summary>CIELAB in the sixteen-bit encoding <c>lut16Type</c> keeps, where 0xFF00 is L* = 100.</summary>
    LabLegacy,
}

/// <summary>
/// A device-to-connection-space lookup: a <c>lut8Type</c>, a
/// <c>lut16Type</c>, or a <c>lutAtoBType</c>, read into one pipeline of
/// curves, a grid of samples, and a matrix.
/// </summary>
/// <remarks>
/// <para>
/// The three types share one shape - curves in, a multidimensional table, and
/// curves out - and <c>lutAtoBType</c> adds a matrix with its own curves before
/// the last ones. Each element is optional in the version-4 type, and the order
/// is the one it defines: A curves, the table, M curves, the matrix, B curves.
/// The older types keep a matrix too, but it applies only where the input is
/// the connection space, which a device-to-PCS table never has.
/// </para>
/// <para>
/// The table is interpolated linearly in every dimension, from the corners of
/// the cell a colour falls in. Up to four inputs are read, which covers every
/// colour space a PDF image is converted from here; the grid's size is checked
/// against the tag's bytes before anything is allocated.
/// </para>
/// </remarks>
internal sealed class IccLut
{
    /// <summary>Most grid samples a table may hold, well past the largest in common print profiles.</summary>
    private const long MaxGridSamples = 16_000_000;

    private readonly IccCurve[]? _a;
    private readonly int[]? _grid;
    private readonly float[]? _table;
    private readonly IccCurve[]? _m;
    private readonly double[]? _matrix;
    private readonly IccCurve[] _b;

    private IccLut(int inputs, IccCurve[]? a, int[]? grid, float[]? table, IccCurve[]? m, double[]? matrix, IccCurve[] b, IccPcsEncoding encoding)
    {
        Inputs = inputs;
        _a = a;
        _grid = grid;
        _table = table;
        _m = m;
        _matrix = matrix;
        _b = b;
        Encoding = encoding;
    }

    public int Inputs { get; }

    public IccPcsEncoding Encoding { get; }

    /// <summary>
    /// Runs a colour through the pipeline: <paramref name="input"/> in [0, 1]
    /// per component, three normalized connection-space values out.
    /// </summary>
    public void Evaluate(ReadOnlySpan<double> input, Span<double> output)
    {
        Span<double> values = stackalloc double[4];
        for (int i = 0; i < Inputs; i++)
            values[i] = _a is null ? input[i] : _a[i].Apply(input[i]);

        Span<double> three = stackalloc double[3];
        if (_table is not null)
            Interpolate(values[..Inputs], three);
        else
            values[..3].CopyTo(three);

        if (_m is not null)
        {
            for (int c = 0; c < 3; c++)
                three[c] = _m[c].Apply(three[c]);
        }

        if (_matrix is not null)
        {
            double x = three[0], y = three[1], z = three[2];
            for (int r = 0; r < 3; r++)
                three[r] = (_matrix[r * 3] * x) + (_matrix[(r * 3) + 1] * y) + (_matrix[(r * 3) + 2] * z) + _matrix[9 + r];
        }

        for (int c = 0; c < 3; c++)
            output[c] = _b[c].Apply(three[c]);
    }

    /// <summary>
    /// Reads a lookup tag, declining a table this reader does not convert: more
    /// than four inputs, anything but three outputs, a grid past the tag's
    /// bytes, or an element of a type it does not know.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> tag, bool lab, out IccLut? lut, out string? declined)
    {
        lut = null;
        declined = null;

        if (tag.Length < 32)
        {
            declined = "a lookup table shorter than its own header";
            return false;
        }

        string type = IccProfile.Signature(tag, 0);
        int inputs = tag[8];
        int outputs = tag[9];

        if (inputs is < 1 or > 4)
        {
            declined = "a lookup table with more inputs than this reader converts";
            return false;
        }

        if (outputs != 3)
        {
            declined = "a lookup table that does not end in three connection-space values";
            return false;
        }

        return type switch
        {
            "mft1" => TryReadLut8(tag, inputs, lab, out lut, out declined),
            "mft2" => TryReadLut16(tag, inputs, lab, out lut, out declined),
            "mAB " => TryReadAtoB(tag, inputs, lab, out lut, out declined),
            _ => Decline("a lookup table of a type this reader does not convert", out lut, out declined),
        };
    }

    private static bool TryReadLut8(ReadOnlySpan<byte> tag, int inputs, bool lab, out IccLut? lut, out string? declined)
    {
        lut = null;
        declined = null;
        int points = tag[10];
        if (points < 2)
            return Decline("a lookup table with a grid of fewer than two points", out lut, out declined);

        long gridSamples = Power(points, inputs);
        long needed = 48 + (inputs * 256L) + (gridSamples * 3) + (3 * 256L);
        if (gridSamples > MaxGridSamples || needed > tag.Length)
            return Decline("a lookup table larger than its tag", out lut, out declined);

        int at = 48;
        IccCurve[] a = ReadTables(tag, ref at, inputs, 256, wide: false);
        float[] table = ReadGrid(tag, ref at, gridSamples * 3, wide: false);
        IccCurve[] b = ReadTables(tag, ref at, 3, 256, wide: false);

        lut = new IccLut(inputs, a, Uniform(points, inputs), table, null, null, b, lab ? IccPcsEncoding.Lab : IccPcsEncoding.Xyz);
        return true;
    }

    private static bool TryReadLut16(ReadOnlySpan<byte> tag, int inputs, bool lab, out IccLut? lut, out string? declined)
    {
        lut = null;
        declined = null;
        int points = tag[10];
        if (points < 2 || tag.Length < 52)
            return Decline("a lookup table with a grid of fewer than two points", out lut, out declined);

        int inEntries = BinaryPrimitives.ReadUInt16BigEndian(tag[48..]);
        int outEntries = BinaryPrimitives.ReadUInt16BigEndian(tag[50..]);
        if (inEntries < 2 || outEntries < 2)
            return Decline("a lookup table whose curves have fewer than two entries", out lut, out declined);

        long gridSamples = Power(points, inputs);
        long needed = 52 + (inputs * inEntries * 2L) + (gridSamples * 3 * 2) + (3L * outEntries * 2);
        if (gridSamples > MaxGridSamples || needed > tag.Length)
            return Decline("a lookup table larger than its tag", out lut, out declined);

        int at = 52;
        IccCurve[] a = ReadTables(tag, ref at, inputs, inEntries, wide: true);
        float[] table = ReadGrid(tag, ref at, gridSamples * 3, wide: true);
        IccCurve[] b = ReadTables(tag, ref at, 3, outEntries, wide: true);

        lut = new IccLut(inputs, a, Uniform(points, inputs), table, null, null, b, lab ? IccPcsEncoding.LabLegacy : IccPcsEncoding.Xyz);
        return true;
    }

    private static bool TryReadAtoB(ReadOnlySpan<byte> tag, int inputs, bool lab, out IccLut? lut, out string? declined)
    {
        lut = null;
        declined = null;
        uint bOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[12..]);
        uint matrixOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[16..]);
        uint mOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[20..]);
        uint tableOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[24..]);
        uint aOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[28..]);

        // B curves are the one element the type requires; a table needs A
        // curves before it, and a matrix M curves - and without a table the
        // inputs are already the three values the rest works on.
        if (bOffset == 0 || (tableOffset != 0 && aOffset == 0) || (matrixOffset != 0 && mOffset == 0) ||
            (tableOffset == 0 && inputs != 3))
        {
            return Decline("a lookup table missing an element its others need", out lut, out declined);
        }

        if (!TryCurves(tag, bOffset, 3, out IccCurve[]? b, out declined))
            return false;

        IccCurve[]? a = null;
        if (aOffset != 0 && !TryCurves(tag, aOffset, inputs, out a, out declined))
            return false;

        IccCurve[]? m = null;
        if (mOffset != 0 && !TryCurves(tag, mOffset, 3, out m, out declined))
            return false;

        double[]? matrix = null;
        if (matrixOffset != 0)
        {
            if (matrixOffset > (uint)(tag.Length - 48))
                return Decline("a lookup table whose matrix runs past its tag", out lut, out declined);

            matrix = new double[12];
            for (int i = 0; i < 12; i++)
                matrix[i] = IccProfile.S15Fixed16(tag, (int)matrixOffset + (i * 4));
        }

        int[]? grid = null;
        float[]? table = null;
        if (tableOffset != 0)
        {
            if (tableOffset > (uint)(tag.Length - 20))
                return Decline("a lookup table whose grid runs past its tag", out lut, out declined);

            int at = (int)tableOffset;
            grid = new int[inputs];
            long gridSamples = 1;
            for (int i = 0; i < inputs; i++)
            {
                grid[i] = tag[at + i];
                if (grid[i] < 2)
                    return Decline("a lookup table with a grid of fewer than two points", out lut, out declined);

                gridSamples *= grid[i];
            }

            int precision = tag[at + 16];
            if (precision is not (1 or 2))
                return Decline("a lookup table of a precision this reader does not convert", out lut, out declined);

            at += 20;
            if (gridSamples > MaxGridSamples || at + (gridSamples * 3 * precision) > tag.Length)
                return Decline("a lookup table larger than its tag", out lut, out declined);

            table = ReadGrid(tag, ref at, gridSamples * 3, wide: precision == 2);
        }

        lut = new IccLut(inputs, a, grid, table, m, matrix, b!, lab ? IccPcsEncoding.Lab : IccPcsEncoding.Xyz);
        return true;
    }

    /// <summary>
    /// Interpolates the grid linearly in every dimension: each of the cell's
    /// corners weighted by how near the colour is to it.
    /// </summary>
    private void Interpolate(ReadOnlySpan<double> input, Span<double> output)
    {
        int dimensions = input.Length;
        Span<int> cell = stackalloc int[4];
        Span<double> fraction = stackalloc double[4];
        Span<int> stride = stackalloc int[4];

        // The first input varies slowest through the grid.
        int step = 3;
        for (int d = dimensions - 1; d >= 0; d--)
        {
            stride[d] = step;
            step *= _grid![d];
        }

        for (int d = 0; d < dimensions; d++)
        {
            double position = Math.Clamp(input[d], 0, 1) * (_grid![d] - 1);
            int index = Math.Min((int)position, _grid[d] - 2);
            cell[d] = index;
            fraction[d] = position - index;
        }

        output[0] = output[1] = output[2] = 0;
        for (int corner = 0; corner < 1 << dimensions; corner++)
        {
            double weight = 1;
            int at = 0;
            for (int d = 0; d < dimensions; d++)
            {
                bool upper = (corner & (1 << d)) != 0;
                weight *= upper ? fraction[d] : 1 - fraction[d];
                at += (cell[d] + (upper ? 1 : 0)) * stride[d];
            }

            if (weight == 0)
                continue;

            output[0] += weight * _table![at];
            output[1] += weight * _table[at + 1];
            output[2] += weight * _table[at + 2];
        }
    }

    /// <summary>Reads a sequence of curves at an offset, each starting on a four-byte boundary.</summary>
    private static bool TryCurves(ReadOnlySpan<byte> tag, uint offset, int count, out IccCurve[]? curves, out string? declined)
    {
        curves = null;
        declined = null;
        if (offset >= (uint)tag.Length)
        {
            declined = "a lookup table whose curves run past its tag";
            return false;
        }

        var read = new IccCurve[count];
        int at = (int)offset;
        for (int i = 0; i < count; i++)
        {
            if (at >= tag.Length || !IccCurve.TryRead(tag[at..], out IccCurve? curve, out int length, out declined))
            {
                declined ??= "a lookup table whose curves run past its tag";
                return false;
            }

            read[i] = curve!;
            at += (length + 3) & ~3;
        }

        curves = read;
        return true;
    }

    /// <summary>Reads per-channel sampled curves, as the older table types store them.</summary>
    private static IccCurve[] ReadTables(ReadOnlySpan<byte> tag, ref int at, int channels, int entries, bool wide)
    {
        var curves = new IccCurve[channels];
        int size = wide ? 2 : 1;
        for (int c = 0; c < channels; c++)
        {
            var samples = new double[entries];
            for (int i = 0; i < entries; i++)
            {
                int position = at + (i * size);
                samples[i] = wide ? BinaryPrimitives.ReadUInt16BigEndian(tag[position..]) / 65535.0 : tag[position] / 255.0;
            }

            curves[c] = IccCurve.FromSamples(samples);
            at += entries * size;
        }

        return curves;
    }

    /// <summary>Reads a grid's samples, normalized to [0, 1].</summary>
    private static float[] ReadGrid(ReadOnlySpan<byte> tag, ref int at, long count, bool wide)
    {
        var table = new float[count];
        for (long i = 0; i < count; i++)
        {
            table[i] = wide
                ? BinaryPrimitives.ReadUInt16BigEndian(tag[(int)(at + (i * 2))..]) / 65535f
                : tag[(int)(at + i)] / 255f;
        }

        at += (int)(count * (wide ? 2 : 1));
        return table;
    }

    private static int[] Uniform(int points, int inputs)
    {
        var grid = new int[inputs];
        Array.Fill(grid, points);
        return grid;
    }

    private static long Power(int value, int exponent)
    {
        long result = 1;
        for (int i = 0; i < exponent; i++)
            result *= value;
        return result;
    }

    private static bool Decline(string reason, out IccLut? lut, out string? declined)
    {
        lut = null;
        declined = reason;
        return false;
    }
}
