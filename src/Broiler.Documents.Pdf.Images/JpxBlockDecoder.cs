using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>The subband a code-block belongs to, which selects its context table.</summary>
internal enum JpxSubband
{
    Ll,
    Hl,
    Lh,
    Hh,
}

/// <summary>
/// EBCOT tier-1: decodes one code-block's coefficients from its MQ-coded
/// bit-planes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the least verifiable file in the decoder, and the tests do not
/// establish that it is right.</strong> Everything below turns on the context
/// assignment tables — which of nineteen adaptive contexts a bit is coded
/// against, chosen from the significance of a coefficient's eight neighbours and
/// the subband it sits in. Those tables are normative (T.800 Annex D, tables D.1
/// through D.4 and the initial states in D.7), they are transcribed here, and
/// nothing available inside this repository's rules can check them: a round trip
/// needs a JPEG 2000 encoder as large as this decoder and sharing its
/// assumptions, the standard's test codestreams are official test material, and
/// no conforming file may be committed as a fixture (IP-020).
/// </para>
/// <para>
/// What that means concretely: this file can be wrong in a way that compiles,
/// passes every test in the suite, and produces a plausible wrong picture on the
/// first real image. The wavelets beside it do not have that property — they are
/// invertible and are tested by inversion — and the difference is stated in both
/// places so that a reviewer weighs them differently.
/// </para>
/// <para>
/// The tables are therefore written as explicit branches rather than as packed
/// lookup arrays. It is slower and it is the point: a reviewer holding Annex D
/// can read the conditions against the standard's rows.
/// </para>
/// </remarks>
internal static class JpxBlockDecoder
{
    // The nineteen contexts, T.800 Annex D. Zero through eight are significance,
    // nine through thirteen sign, fourteen through sixteen magnitude refinement,
    // seventeen the cleanup run-length, eighteen UNIFORM.
    private const int RunLengthContext = 17;
    private const int UniformContext = 18;
    private const int ContextCount = 19;

    // Per-coefficient flags kept beside the magnitudes.
    private const byte Significant = 1;
    private const byte VisitedThisPass = 2;
    private const byte Refined = 4;

    /// <summary>
    /// Decodes a code-block into signed coefficient magnitudes.
    /// </summary>
    /// <param name="passes">How many coding passes the packet headers said this block carries.</param>
    /// <param name="missingBitPlanes">The block's zero bit-planes, from its tag tree.</param>
    public static int[]? Decode(
        ReadOnlyMemory<byte> data,
        int width,
        int height,
        int passes,
        int missingBitPlanes,
        int maxBitPlanes,
        JpxSubband subband)
    {
        if (width <= 0 || height <= 0 || passes <= 0)
            return null;

        long area = (long)width * height;
        if (area > int.MaxValue / 4)
            return null;

        var magnitudes = new int[area];
        var signs = new byte[area];
        var flags = new byte[area];

        var decoder = new MqDecoder(data);
        var contexts = new MqContexts(5);
        InitialiseContexts(contexts);

        // The first pass of the most significant coded plane is always a cleanup
        // pass; the three-pass cycle starts after it.
        int plane = maxBitPlanes - 1 - missingBitPlanes;
        if (plane < 0)
            return magnitudes;

        int pass = 0;
        int kind = 2;

        while (pass < passes && plane >= 0)
        {
            switch (kind)
            {
                case 0:
                    SignificancePropagation(decoder, contexts, magnitudes, signs, flags, width, height, plane, subband);
                    break;
                case 1:
                    MagnitudeRefinement(decoder, contexts, magnitudes, flags, width, height, plane);
                    break;
                default:
                    Cleanup(decoder, contexts, magnitudes, signs, flags, width, height, plane, subband);
                    break;
            }

            pass++;
            if (kind == 2)
            {
                // A cleanup pass ends a bit-plane: the per-plane visit marks go
                // and the next plane starts clean.
                ClearVisited(flags);
                plane--;
                kind = 0;
            }
            else
            {
                kind++;
            }
        }

        var result = new int[area];
        for (int i = 0; i < area; i++)
            result[i] = signs[i] != 0 ? -magnitudes[i] : magnitudes[i];

        return result;
    }

    /// <summary>
    /// The initial states, T.800 Table D.7. Three contexts start away from zero
    /// and the rest at it, which is the only initialisation the standard states.
    /// </summary>
    private static void InitialiseContexts(MqContexts contexts)
    {
        for (int i = 0; i < ContextCount; i++)
        {
            contexts.State(i) = 0;
            contexts.Mps(i) = 0;
        }

        contexts.State(0) = 4;                  // the all-insignificant context
        contexts.State(RunLengthContext) = 3;
        contexts.State(UniformContext) = 46;
    }

    private static void ClearVisited(byte[] flags)
    {
        for (int i = 0; i < flags.Length; i++)
            flags[i] &= unchecked((byte)~VisitedThisPass);
    }

    // ---- the three passes -------------------------------------------------------

    private static void SignificancePropagation(
        MqDecoder decoder,
        MqContexts contexts,
        int[] magnitudes,
        byte[] signs,
        byte[] flags,
        int width,
        int height,
        int plane,
        JpxSubband subband)
    {
        foreach (int i in Stripes(width, height))
        {
            int x = i % width;
            int y = i / width;

            if ((flags[i] & Significant) != 0)
                continue;

            // Only coefficients with a significant neighbour are coded here; the
            // rest wait for the cleanup pass.
            (int h, int v, int d) = Neighbours(flags, width, height, x, y);
            if (h + v + d == 0)
                continue;

            int context = SignificanceContext(h, v, d, subband);
            if (decoder.Decode(contexts, context) == 1)
            {
                signs[i] = (byte)DecodeSign(decoder, contexts, flags, signs, width, height, x, y);
                flags[i] |= Significant;
                magnitudes[i] |= 1 << plane;
            }

            flags[i] |= VisitedThisPass;
        }
    }

    private static void MagnitudeRefinement(
        MqDecoder decoder,
        MqContexts contexts,
        int[] magnitudes,
        byte[] flags,
        int width,
        int height,
        int plane)
    {
        foreach (int i in Stripes(width, height))
        {
            if ((flags[i] & Significant) == 0 || (flags[i] & VisitedThisPass) != 0)
                continue;

            int x = i % width;
            int y = i / width;

            // Table D.4: the first refinement distinguishes a coefficient with
            // significant neighbours from one without; later refinements do not.
            int context;
            if ((flags[i] & Refined) != 0)
            {
                context = 16;
            }
            else
            {
                (int h, int v, int d) = Neighbours(flags, width, height, x, y);
                context = h + v + d > 0 ? 15 : 14;
            }

            if (decoder.Decode(contexts, context) == 1)
                magnitudes[i] |= 1 << plane;

            flags[i] |= Refined;
            flags[i] |= VisitedThisPass;
        }
    }

    private static void Cleanup(
        MqDecoder decoder,
        MqContexts contexts,
        int[] magnitudes,
        byte[] signs,
        byte[] flags,
        int width,
        int height,
        int plane,
        JpxSubband subband)
    {
        for (int y0 = 0; y0 < height; y0 += 4)
        {
            for (int x = 0; x < width; x++)
            {
                int y = y0;
                while (y < Math.Min(y0 + 4, height))
                {
                    // A whole column of four, all insignificant with no significant
                    // neighbours and none already coded this plane, is coded as one
                    // run-length symbol.
                    bool runLength = y == y0 && y0 + 3 < height && ColumnIsClean(flags, width, height, x, y0);

                    if (runLength)
                    {
                        if (decoder.Decode(contexts, RunLengthContext) == 0)
                        {
                            y = y0 + 4;
                            continue;
                        }

                        // Two UNIFORM bits say which of the four becomes significant.
                        int which = (decoder.Decode(contexts, UniformContext) << 1) |
                                    decoder.Decode(contexts, UniformContext);
                        y = y0 + which;

                        int at = (y * width) + x;
                        signs[at] = (byte)DecodeSign(decoder, contexts, flags, signs, width, height, x, y);
                        flags[at] |= Significant;
                        magnitudes[at] |= 1 << plane;
                        y++;
                        continue;
                    }

                    int index = (y * width) + x;
                    if ((flags[index] & Significant) != 0 || (flags[index] & VisitedThisPass) != 0)
                    {
                        y++;
                        continue;
                    }

                    (int h, int v, int d) = Neighbours(flags, width, height, x, y);
                    if (decoder.Decode(contexts, SignificanceContext(h, v, d, subband)) == 1)
                    {
                        signs[index] = (byte)DecodeSign(decoder, contexts, flags, signs, width, height, x, y);
                        flags[index] |= Significant;
                        magnitudes[index] |= 1 << plane;
                    }

                    y++;
                }
            }
        }
    }

    /// <summary>Whether a stripe column qualifies for run-length coding.</summary>
    private static bool ColumnIsClean(byte[] flags, int width, int height, int x, int y0)
    {
        for (int dy = 0; dy < 4; dy++)
        {
            int y = y0 + dy;
            int index = (y * width) + x;
            if ((flags[index] & (Significant | VisitedThisPass)) != 0)
                return false;

            (int h, int v, int d) = Neighbours(flags, width, height, x, y);
            if (h + v + d != 0)
                return false;
        }

        return true;
    }

    // ---- the context tables -----------------------------------------------------

    /// <summary>
    /// Table D.1: nine significance contexts from the neighbour counts, with the
    /// roles of the horizontal and vertical neighbours depending on the subband.
    /// </summary>
    private static int SignificanceContext(int h, int v, int d, JpxSubband subband)
    {
        // HL swaps the two axes; HH counts diagonals first. LL and LH share a row.
        if (subband == JpxSubband.Hl)
            (h, v) = (v, h);

        if (subband == JpxSubband.Hh)
        {
            int hv = h + v;
            if (d >= 3)
                return 8;
            if (d == 2)
                return hv >= 1 ? 7 : 6;
            if (d == 1)
                return hv >= 2 ? 5 : hv == 1 ? 4 : 3;
            return hv >= 2 ? 2 : hv == 1 ? 1 : 0;
        }

        if (h == 2)
            return 8;
        if (h == 1)
            return v >= 1 ? 7 : d >= 1 ? 6 : 5;
        if (v == 2)
            return 4;
        if (v == 1)
            return 3;
        return d >= 2 ? 2 : d == 1 ? 1 : 0;
    }

    /// <summary>
    /// Table D.3: the sign is coded against a context chosen from the horizontal
    /// and vertical neighbours' signs, and the decoded bit is exclusive-ored with
    /// a prediction rather than used directly.
    /// </summary>
    private static int DecodeSign(
        MqDecoder decoder,
        MqContexts contexts,
        byte[] flags,
        byte[] signs,
        int width,
        int height,
        int x,
        int y)
    {
        int h = SignContribution(flags, signs, width, height, x - 1, y) +
                SignContribution(flags, signs, width, height, x + 1, y);
        int v = SignContribution(flags, signs, width, height, x, y - 1) +
                SignContribution(flags, signs, width, height, x, y + 1);

        h = Math.Clamp(h, -1, 1);
        v = Math.Clamp(v, -1, 1);

        int context;
        int prediction;

        if (h == 1)
        {
            context = v == 1 ? 13 : v == 0 ? 12 : 11;
            prediction = 0;
        }
        else if (h == 0)
        {
            context = v == 0 ? 9 : 10;
            prediction = v == -1 ? 1 : 0;
        }
        else
        {
            context = v == 1 ? 11 : v == 0 ? 12 : 13;
            prediction = 1;
        }

        return decoder.Decode(contexts, context) ^ prediction;
    }

    /// <summary>A neighbour's contribution: +1 positive, -1 negative, 0 insignificant.</summary>
    private static int SignContribution(byte[] flags, byte[] signs, int width, int height, int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return 0;

        int index = (y * width) + x;
        if ((flags[index] & Significant) == 0)
            return 0;

        return signs[index] != 0 ? -1 : 1;
    }

    /// <summary>The counts of significant horizontal, vertical and diagonal neighbours.</summary>
    private static (int H, int V, int D) Neighbours(byte[] flags, int width, int height, int x, int y)
    {
        int h = Sig(flags, width, height, x - 1, y) + Sig(flags, width, height, x + 1, y);
        int v = Sig(flags, width, height, x, y - 1) + Sig(flags, width, height, x, y + 1);
        int d = Sig(flags, width, height, x - 1, y - 1) + Sig(flags, width, height, x + 1, y - 1) +
                Sig(flags, width, height, x - 1, y + 1) + Sig(flags, width, height, x + 1, y + 1);

        return (h, v, d);
    }

    private static int Sig(byte[] flags, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height && (flags[(y * width) + x] & Significant) != 0 ? 1 : 0;

    /// <summary>
    /// Coefficient indices in the stripe-column order every pass scans: four rows
    /// at a time, each stripe left to right, each column top to bottom.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<int> Stripes(int width, int height)
    {
        for (int y0 = 0; y0 < height; y0 += 4)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = y0; y < Math.Min(y0 + 4, height); y++)
                    yield return (y * width) + x;
            }
        }
    }
}
