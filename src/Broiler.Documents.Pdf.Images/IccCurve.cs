using System;
using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// One tone curve from a profile - a <c>curv</c> or a <c>para</c> - mapping a
/// level in [0, 1] to a level in [0, 1].
/// </summary>
/// <remarks>
/// <para>
/// Both forms are evaluated once, into a table, when the profile is read, and
/// looked up with linear interpolation after that: a picture asks for the same
/// curve millions of times, and a power function per channel per pixel is the
/// most expensive thing a conversion would otherwise do. Four thousand intervals
/// keep the interpolation well below what an eight-bit result can show.
/// </para>
/// <para>
/// The parametric forms are the five ICC.1 defines, written from their
/// definitions; a sampled curve is interpolated between its own samples.
/// </para>
/// </remarks>
internal sealed class IccCurve
{
    private const int Intervals = 4096;

    private readonly double[] _table;

    private IccCurve(double[] table) => _table = table;

    /// <summary>The curve that changes nothing.</summary>
    public static IccCurve Identity { get; } = FromFunction(static x => x);

    /// <summary>Maps a level through the curve; out-of-range input is clamped first.</summary>
    public double Apply(double level)
    {
        double position = (level <= 0 ? 0 : level >= 1 ? 1 : level) * Intervals;
        int index = (int)position;
        if (index >= Intervals)
            return _table[Intervals];

        double fraction = position - index;
        return _table[index] + ((_table[index + 1] - _table[index]) * fraction);
    }

    /// <summary>
    /// Reads the curve at the start of <paramref name="data"/> and says how many
    /// bytes it took, or declines.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> data, out IccCurve? curve, out int length, out string? declined)
    {
        curve = null;
        length = 0;
        declined = null;

        if (data.Length < 12)
        {
            declined = "a curve shorter than its own header";
            return false;
        }

        switch (IccProfile.Signature(data, 0))
        {
            case "curv":
                return TryReadSampled(data, out curve, out length, out declined);
            case "para":
                return TryReadParametric(data, out curve, out length, out declined);
            default:
                declined = "a curve of a type this reader does not convert";
                return false;
        }
    }

    private static bool TryReadSampled(ReadOnlySpan<byte> data, out IccCurve? curve, out int length, out string? declined)
    {
        curve = null;
        declined = null;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        length = 12;

        if (count > (uint)((data.Length - 12) / 2))
        {
            declined = "a curve with more samples than its tag holds";
            return false;
        }

        length = 12 + ((int)count * 2);

        if (count == 0)
        {
            curve = Identity;
            return true;
        }

        if (count == 1)
        {
            // One entry is a gamma, as an unsigned 8.8 fixed-point number.
            double gamma = BinaryPrimitives.ReadUInt16BigEndian(data[12..]) / 256.0;
            curve = FromFunction(x => Math.Pow(x, gamma));
            return true;
        }

        var samples = new double[count];
        for (int i = 0; i < count; i++)
            samples[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(12 + (i * 2))..]) / 65535.0;

        curve = FromSamples(samples);
        return true;
    }

    /// <summary>
    /// A curve sampled at evenly spaced inputs, interpolated between its samples.
    /// Two samples at the least; the older table types store their curves so.
    /// </summary>
    public static IccCurve FromSamples(double[] samples) => FromFunction(x =>
    {
        double position = x * (samples.Length - 1);
        int index = Math.Min((int)position, samples.Length - 2);
        return samples[index] + ((samples[index + 1] - samples[index]) * (position - index));
    });

    private static bool TryReadParametric(ReadOnlySpan<byte> data, out IccCurve? curve, out int length, out string? declined)
    {
        curve = null;
        declined = null;
        int type = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        int parameters = type switch
        {
            0 => 1,
            1 => 3,
            2 => 4,
            3 => 5,
            4 => 7,
            _ => 0,
        };

        length = 12 + (parameters * 4);
        if (parameters == 0)
        {
            declined = "a parametric curve of a type this reader does not convert";
            return false;
        }

        if (data.Length < length)
        {
            declined = "a curve with more parameters than its tag holds";
            return false;
        }

        var p = new double[7];
        for (int i = 0; i < parameters; i++)
        {
            p[i] = IccProfile.S15Fixed16(data, 12 + (i * 4));
            if (!double.IsFinite(p[i]))
            {
                declined = "a parametric curve with a parameter that is not a number";
                return false;
            }
        }

        double g = p[0], a = p[1], b = p[2], c = p[3], d = p[4], e = p[5], f = p[6];

        // The five forms, by their definitions: a gamma, then a gamma of a
        // linear function with the offsets and the linear segment near black
        // the later forms add.
        curve = type switch
        {
            0 => FromFunction(x => Math.Pow(x, g)),
            1 => FromFunction(x => Power((a * x) + b, g)),
            2 => FromFunction(x => Power((a * x) + b, g) + c),
            3 => FromFunction(x => x >= d ? Power((a * x) + b, g) : c * x),
            _ => FromFunction(x => x >= d ? Power((a * x) + b, g) + e : (c * x) + f),
        };
        return true;
    }

    /// <summary>A base raised to a power, where a base below zero contributes nothing.</summary>
    private static double Power(double value, double exponent) => value > 0 ? Math.Pow(value, exponent) : 0;

    private static IccCurve FromFunction(Func<double, double> function)
    {
        var table = new double[Intervals + 1];
        for (int i = 0; i <= Intervals; i++)
        {
            double y = function((double)i / Intervals);
            table[i] = double.IsFinite(y) ? Math.Clamp(y, 0, 1) : 0;
        }

        return new IccCurve(table);
    }
}
