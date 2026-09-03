using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// The inverse discrete wavelet transforms JPEG 2000 Part 1 defines: the
/// reversible 5/3 integer filter and the irreversible 9/7 float filter.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the part of the JPEG 2000 decoder that can actually be
/// tested.</strong> Both transforms are invertible by construction — 5/3 exactly,
/// 9/7 to floating-point tolerance — so a forward transform written in the test
/// suite and applied before this one is not a mirror of the same assumption the
/// way an entropy-coder round trip is. If the lifting steps here were wrong, an
/// independently written forward transform would not undo them.
/// </para>
/// <para>
/// The rest of the decoder does not have that property, and the difference is
/// worth carrying: the wavelets and the component transforms rest on evidence,
/// and the entropy coder's context tables rest on a careful reading nobody has
/// checked.
/// </para>
/// <para>
/// The lifting formulations are T.800 Annex F. Both are written as in-place
/// lifting over an interleaved signal with symmetric extension at the edges,
/// which is what the standard specifies and is also the only way the boundary
/// behaviour comes out right for the short rows a small code-block produces.
/// </para>
/// </remarks>
internal static class JpxWavelet
{
    // The 9/7 lifting coefficients, T.800 Table F.4.
    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float Kappa = 1.230174104914001f;

    /// <summary>
    /// One inverse decomposition level: combines a low-pass quadrant with the
    /// three high-pass quadrants into the next larger image.
    /// </summary>
    /// <param name="reversible">True for the 5/3 filter, false for 9/7.</param>
    public static void InverseLevel(
        float[] coefficients,
        int width,
        int height,
        int lowWidth,
        int lowHeight,
        bool reversible)
    {
        // Rows first, then columns. The standard's 2D transform is separable, so
        // the order only has to be the inverse of the forward one.
        var row = new float[width];
        for (int y = 0; y < height; y++)
        {
            Interleave(coefficients, row, y * width, 1, width, lowWidth);
            Inverse1D(row, width, reversible);
            Scatter(row, coefficients, y * width, 1, width);
        }

        var column = new float[height];
        for (int x = 0; x < width; x++)
        {
            Interleave(coefficients, column, x, width, height, lowHeight);
            Inverse1D(column, height, reversible);
            Scatter(column, coefficients, x, width, height);
        }
    }

    /// <summary>
    /// Reads a quadrant-ordered line into the interleaved order lifting works on:
    /// low-pass samples at even positions, high-pass at odd.
    /// </summary>
    private static void Interleave(float[] source, float[] line, int offset, int stride, int length, int lowLength)
    {
        for (int i = 0; i < lowLength; i++)
            line[2 * i] = source[offset + (i * stride)];

        for (int i = 0; i + lowLength < length; i++)
            line[(2 * i) + 1] = source[offset + ((lowLength + i) * stride)];
    }

    private static void Scatter(float[] line, float[] destination, int offset, int stride, int length)
    {
        for (int i = 0; i < length; i++)
            destination[offset + (i * stride)] = line[i];
    }

    private static void Inverse1D(float[] line, int length, bool reversible)
    {
        if (length == 1)
        {
            // A single sample is its own low-pass band; the irreversible filter
            // still scales it.
            if (!reversible)
                line[0] /= Kappa;
            return;
        }

        if (reversible)
        {
            // 5/3, T.800 F.3.8.2. Even samples first, then odd.
            for (int i = 0; i < length; i += 2)
                line[i] -= MathF.Floor((At(line, length, i - 1) + At(line, length, i + 1) + 2) / 4);

            for (int i = 1; i < length; i += 2)
                line[i] += MathF.Floor((At(line, length, i - 1) + At(line, length, i + 1)) / 2);

            return;
        }

        // 9/7, T.800 F.3.8.2: undo the scaling, then the four lifting steps in
        // reverse order and with reversed sign.
        for (int i = 0; i < length; i += 2)
            line[i] *= Kappa;

        for (int i = 1; i < length; i += 2)
            line[i] /= Kappa;

        for (int i = 0; i < length; i += 2)
            line[i] -= Delta * (At(line, length, i - 1) + At(line, length, i + 1));

        for (int i = 1; i < length; i += 2)
            line[i] -= Gamma * (At(line, length, i - 1) + At(line, length, i + 1));

        for (int i = 0; i < length; i += 2)
            line[i] -= Beta * (At(line, length, i - 1) + At(line, length, i + 1));

        for (int i = 1; i < length; i += 2)
            line[i] -= Alpha * (At(line, length, i - 1) + At(line, length, i + 1));
    }

    /// <summary>
    /// A sample with the whole-sample symmetric extension the standard defines at
    /// the boundaries: position -1 mirrors 1, and length mirrors length-2.
    /// </summary>
    private static float At(float[] line, int length, int index)
    {
        if (index < 0)
            index = -index;
        if (index >= length)
            index = (2 * length) - index - 2;

        return index >= 0 && index < length ? line[index] : 0;
    }
}

/// <summary>
/// The multiple component transforms: reversible (RCT) and irreversible (ICT).
/// </summary>
/// <remarks>
/// Both are invertible and both are tested by inversion, on the same footing as
/// the wavelets: RCT exactly, ICT to tolerance.
/// </remarks>
internal static class JpxComponentTransform
{
    /// <summary>Undoes the reversible colour transform, T.800 G.2.</summary>
    public static void InverseReversible(float[] c0, float[] c1, float[] c2)
    {
        for (int i = 0; i < c0.Length; i++)
        {
            float y = c0[i];
            float u = c1[i];
            float v = c2[i];

            float g = y - MathF.Floor((u + v) / 4);
            c0[i] = v + g;
            c1[i] = g;
            c2[i] = u + g;
        }
    }

    /// <summary>Undoes the irreversible colour transform, T.800 G.3.</summary>
    public static void InverseIrreversible(float[] c0, float[] c1, float[] c2)
    {
        for (int i = 0; i < c0.Length; i++)
        {
            float y = c0[i];
            float cb = c1[i];
            float cr = c2[i];

            c0[i] = y + (1.402f * cr);
            c1[i] = y - (0.34413f * cb) - (0.71414f * cr);
            c2[i] = y + (1.772f * cb);
        }
    }
}
