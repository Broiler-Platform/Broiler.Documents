using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// The colour space a converted picture lands in: sRGB, which is what the
/// model's pixels are drawn as by every renderer that shows them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is a table copied from anywhere. The matrix from the
/// connection space is <em>computed</em>, once per white point, from the facts
/// that define sRGB - its three primaries and its D65 white, as IEC 61966-2-1
/// states them - and the connection space's own white, which comes out of the
/// profile being read (SRC-023).
/// </para>
/// <para>
/// The connection space is relative to D50 and sRGB to D65, so the matrix
/// adapts between them. The adaptation is the linear Bradford transform, which
/// is the one ICC.1 recommends and the one an sRGB profile's own colorants were
/// computed with: a picture tagged with an sRGB profile therefore comes back as
/// the values it stored, which is the case almost every document is.
/// </para>
/// </remarks>
internal static class IccSrgb
{
    // sRGB's primaries and white point as CIE xy chromaticities.
    private const double RedX = 0.64, RedY = 0.33;
    private const double GreenX = 0.30, GreenY = 0.60;
    private const double BlueX = 0.15, BlueY = 0.06;
    private const double WhiteX = 0.3127, WhiteY = 0.3290;

    // The linear Bradford cone-response matrix (Lam 1985), as ICC.1 recommends
    // for chromatic adaptation.
    private static readonly double[] Bradford =
    [
        0.8951, 0.2664, -0.1614,
        -0.7502, 1.7135, 0.0367,
        0.0389, -0.0685, 1.0296,
    ];

    private static readonly byte[] Encoding = BuildEncoding();

    /// <summary>
    /// The matrix from connection-space XYZ, relative to <paramref name="white"/>,
    /// to linear sRGB: nine numbers, row by row.
    /// </summary>
    public static double[] FromConnectionSpace(IccXyz white)
    {
        // sRGB to XYZ under its own white: each primary's XYZ at unit
        // luminance, scaled so the three sum to the white point.
        double[] primaries =
        [
            RedX / RedY, GreenX / GreenY, BlueX / BlueY,
            1, 1, 1,
            (1 - RedX - RedY) / RedY, (1 - GreenX - GreenY) / GreenY, (1 - BlueX - BlueY) / BlueY,
        ];

        double[] d65 = [WhiteX / WhiteY, 1, (1 - WhiteX - WhiteY) / WhiteY];
        double[] scale = Multiply(Invert(primaries), d65);
        double[] toXyz =
        [
            primaries[0] * scale[0], primaries[1] * scale[1], primaries[2] * scale[2],
            primaries[3] * scale[0], primaries[4] * scale[1], primaries[5] * scale[2],
            primaries[6] * scale[0], primaries[7] * scale[1], primaries[8] * scale[2],
        ];

        // Adapt D65 to the connection space's white in the Bradford cone space.
        double[] source = Multiply(Bradford, d65);
        double[] target = Multiply(Bradford, [white.X, white.Y, white.Z]);
        double[] gain =
        [
            target[0] / source[0], 0, 0,
            0, target[1] / source[1], 0,
            0, 0, target[2] / source[2],
        ];

        double[] adapt = Product(Invert(Bradford), Product(gain, Bradford));
        return Invert(Product(adapt, toXyz));
    }

    /// <summary>A linear sRGB level as the eight-bit value its transfer function encodes it to.</summary>
    public static byte Encode(double linear)
    {
        if (!(linear > 0))
            return 0;

        return linear >= 1 ? (byte)255 : Encoding[(int)((linear * 65535) + 0.5)];
    }

    /// <summary>
    /// sRGB's transfer function, tabulated finely enough that the steep segment
    /// near black stays exact to the eight-bit value.
    /// </summary>
    private static byte[] BuildEncoding()
    {
        var table = new byte[65536];
        for (int i = 0; i < table.Length; i++)
        {
            double linear = i / 65535.0;
            double encoded = linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
            table[i] = (byte)Math.Clamp(Math.Round(encoded * 255, MidpointRounding.AwayFromZero), 0, 255);
        }

        return table;
    }

    private static double[] Multiply(double[] matrix, double[] vector) =>
    [
        (matrix[0] * vector[0]) + (matrix[1] * vector[1]) + (matrix[2] * vector[2]),
        (matrix[3] * vector[0]) + (matrix[4] * vector[1]) + (matrix[5] * vector[2]),
        (matrix[6] * vector[0]) + (matrix[7] * vector[1]) + (matrix[8] * vector[2]),
    ];

    private static double[] Product(double[] left, double[] right)
    {
        var result = new double[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                result[(r * 3) + c] = (left[r * 3] * right[c]) + (left[(r * 3) + 1] * right[3 + c]) + (left[(r * 3) + 2] * right[6 + c]);
        }

        return result;
    }

    /// <summary>The inverse of a 3x3 matrix, by its adjugate.</summary>
    public static double[] Invert(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
        double determinant = (a * ((e * i) - (f * h))) - (b * ((d * i) - (f * g))) + (c * ((d * h) - (e * g)));
        double k = 1 / determinant;

        return
        [
            ((e * i) - (f * h)) * k, ((c * h) - (b * i)) * k, ((b * f) - (c * e)) * k,
            ((f * g) - (d * i)) * k, ((a * i) - (c * g)) * k, ((c * d) - (a * f)) * k,
            ((d * h) - (e * g)) * k, ((b * g) - (a * h)) * k, ((a * e) - (b * d)) * k,
        ];
    }
}
