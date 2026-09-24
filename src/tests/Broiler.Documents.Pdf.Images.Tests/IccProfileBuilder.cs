using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Assembles ICC profiles byte by byte for the tests: a header, a tag table,
/// and the tags a test names.
/// </summary>
/// <remarks>
/// No profile file is committed or read. Every profile here is built from the
/// structure ICC.1 defines and the numbers a test states, so each test says
/// exactly what the profile it converts through contains - and no third
/// party's profile, with its own terms, is ever in the tree (IP-020).
/// </remarks>
internal sealed class IccProfileBuilder(string colorSpace = "RGB ", string connection = "XYZ ", string deviceClass = "mntr", int version = 4)
{
    private readonly List<(string Signature, byte[] Data)> _tags = [];

    /// <summary>The parameters of sRGB's tone curve, as a type 3 parametric curve.</summary>
    public static readonly double[] SrgbCurve = [2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045];

    public IccProfileBuilder With(string signature, byte[] data)
    {
        _tags.Add((signature, data));
        return this;
    }

    public byte[] Build()
    {
        int table = 4 + (_tags.Count * 12);
        var body = new List<byte>();
        var entries = new List<(string Signature, int Offset, int Length)>();

        foreach ((string signature, byte[] data) in _tags)
        {
            while ((128 + table + body.Count) % 4 != 0)
                body.Add(0);

            entries.Add((signature, 128 + table + body.Count, data.Length));
            body.AddRange(data);
        }

        byte[] profile = new byte[128 + table + body.Count];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        profile[8] = (byte)version;
        profile[9] = 0x20;
        WriteSignature(profile, 12, deviceClass);
        WriteSignature(profile, 16, colorSpace);
        WriteSignature(profile, 20, connection);
        WriteSignature(profile, 36, "acsp");
        WriteFixed(profile, 68, 0.9642);
        WriteFixed(profile, 72, 1.0);
        WriteFixed(profile, 76, 0.8249);

        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), (uint)entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            int at = 132 + (i * 12);
            WriteSignature(profile, at, entries[i].Signature);
            BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(at + 4), (uint)entries[i].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(at + 8), (uint)entries[i].Length);
        }

        body.CopyTo(profile, 128 + table);
        return profile;
    }

    // ---- tags ------------------------------------------------------------------

    public static byte[] Xyz(double x, double y, double z)
    {
        byte[] tag = Typed("XYZ ", 12);
        WriteFixed(tag, 8, x);
        WriteFixed(tag, 12, y);
        WriteFixed(tag, 16, z);
        return tag;
    }

    /// <summary>A <c>curv</c> of one entry: a gamma in 8.8 fixed point.</summary>
    public static byte[] Gamma(double gamma)
    {
        byte[] tag = Typed("curv", 6);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(12), (ushort)Math.Round(gamma * 256));
        return tag;
    }

    /// <summary>A <c>curv</c> of evenly spaced samples, each in [0, 1].</summary>
    public static byte[] Sampled(params double[] samples)
    {
        byte[] tag = Typed("curv", 4 + (samples.Length * 2));
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), (uint)samples.Length);
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(12 + (i * 2)), Sixteen(samples[i]));
        return tag;
    }

    public static byte[] Parametric(int type, params double[] parameters)
    {
        byte[] tag = Typed("para", 4 + (parameters.Length * 4));
        BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(8), (ushort)type);
        for (int i = 0; i < parameters.Length; i++)
            WriteFixed(tag, 12 + (i * 4), parameters[i]);
        return tag;
    }

    /// <summary>
    /// A <c>lut16Type</c> with straight-line input and output curves and a
    /// grid of <paramref name="points"/> per input, whose samples
    /// <paramref name="grid"/> gives as normalized outputs for grid indices.
    /// </summary>
    public static byte[] Lut16(int inputs, int points, Func<int[], double[]> grid)
    {
        // The 48-byte header - type, sizes, and a matrix a device table ignores -
        // then the two curve lengths.
        var data = new List<byte>();
        data.AddRange(Typed("mft2", 40));
        data[8] = (byte)inputs;
        data[9] = 3;
        data[10] = (byte)points;
        data.AddRange(Short(2));
        data.AddRange(Short(2));

        for (int i = 0; i < inputs; i++)
        {
            data.AddRange(Short(0));
            data.AddRange(Short(65535));
        }

        foreach (int[] index in GridIndices(inputs, points))
        {
            foreach (double value in grid(index))
                data.AddRange(Short(Sixteen(value)));
        }

        for (int o = 0; o < 3; o++)
        {
            data.AddRange(Short(0));
            data.AddRange(Short(65535));
        }

        return [.. data];
    }

    /// <summary>A <c>lut8Type</c> with straight-line curves and the grid <paramref name="grid"/> gives.</summary>
    public static byte[] Lut8(int inputs, int points, Func<int[], double[]> grid)
    {
        var data = new List<byte>();
        data.AddRange(Typed("mft1", 40));
        data[8] = (byte)inputs;
        data[9] = 3;
        data[10] = (byte)points;

        for (int i = 0; i < inputs; i++)
        {
            for (int v = 0; v < 256; v++)
                data.Add((byte)v);
        }

        foreach (int[] index in GridIndices(inputs, points))
        {
            foreach (double value in grid(index))
                data.Add((byte)Math.Round(value * 255));
        }

        for (int o = 0; o < 3; o++)
        {
            for (int v = 0; v < 256; v++)
                data.Add((byte)v);
        }

        return [.. data];
    }

    /// <summary>
    /// A <c>lutAToBType</c> of three inputs with no grid: M curves, a matrix
    /// of nine coefficients and three offsets, and straight-line B curves.
    /// </summary>
    public static byte[] MatrixAtoB(byte[] mCurve, double[] matrix)
    {
        byte[] identity = Sampled();
        var data = new List<byte>(Typed("mAB ", 24));
        data[8] = 3;
        data[9] = 3;

        int bOffset = data.Count;
        for (int i = 0; i < 3; i++)
            AddAligned(data, identity);

        int matrixOffset = data.Count;
        foreach (double value in matrix)
        {
            byte[] fixedValue = new byte[4];
            WriteFixed(fixedValue, 0, value);
            data.AddRange(fixedValue);
        }

        int mOffset = data.Count;
        for (int i = 0; i < 3; i++)
            AddAligned(data, mCurve);

        byte[] bytes = [.. data];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), (uint)bOffset);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)matrixOffset);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)mOffset);
        return bytes;
    }

    // ---- helpers ---------------------------------------------------------------

    /// <summary>Grid indices in the order a table stores them: the first input slowest.</summary>
    private static IEnumerable<int[]> GridIndices(int inputs, int points)
    {
        int count = (int)Math.Pow(points, inputs);
        for (int n = 0; n < count; n++)
        {
            int[] index = new int[inputs];
            int rest = n;
            for (int d = inputs - 1; d >= 0; d--)
            {
                index[d] = rest % points;
                rest /= points;
            }

            yield return index;
        }
    }

    private static void AddAligned(List<byte> data, byte[] element)
    {
        data.AddRange(element);
        while (data.Count % 4 != 0)
            data.Add(0);
    }

    private static byte[] Typed(string type, int payload)
    {
        byte[] tag = new byte[8 + payload];
        WriteSignature(tag, 0, type);
        return tag;
    }

    private static ushort Sixteen(double value) => (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535);

    private static byte[] Short(int value) => [(byte)(value >> 8), (byte)value];

    private static void WriteSignature(byte[] target, int at, string signature)
    {
        for (int i = 0; i < 4; i++)
            target[at + i] = (byte)signature[i];
    }

    private static void WriteFixed(byte[] target, int at, double value) =>
        BinaryPrimitives.WriteInt32BigEndian(target.AsSpan(at), (int)Math.Round(value * 65536));
}
