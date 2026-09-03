using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// The generic refinement region decoding procedure, T.88 6.3: a bitmap decoded
/// as a correction to one that already exists.
/// </summary>
/// <remarks>
/// <para>
/// Refinement is the format's answer to "almost, but not quite". A scanned page
/// coded by symbol substitution draws one dictionary shape wherever a character
/// occurs, which is a lie in the small: the third <em>e</em> on the line had a
/// broken serif. Refinement codes the difference — each pixel against both its
/// neighbours in the bitmap being built and the corresponding neighbourhood of
/// the reference — so a lossy page can be made exact for a few bits per changed
/// pixel.
/// </para>
/// <para>
/// <strong>Three callers, one procedure.</strong> A refinement region segment
/// refines the page under it; a text region refines a symbol before drawing it;
/// a symbol dictionary defines a new symbol as a refinement of one it already
/// has. All three arrive here, and the differences between them are which bitmap
/// is the reference and where it is anchored.
/// </para>
/// <para>
/// <strong>The templates are the part a reviewer must check.</strong> Each
/// context mixes pixels from two bitmaps, and their order decides the context
/// value: the pixels being decoded first, most significant, then the reference's.
/// They are written out coordinate by coordinate against T.88's figures for
/// exactly the reason the generic templates are — the round trip in the test
/// suite cannot catch a template misread the same way twice, and no conforming
/// file may be committed to catch it instead (IP-020).
/// </para>
/// <para>
/// <strong>Typical prediction.</strong> With TPGRON set, a row can declare that
/// the reference already answers it: where the reference's three-by-three
/// neighbourhood is uniform, the pixel takes that value and no bit is decoded.
/// That is the case refinement exists for — most of a refined bitmap is
/// unchanged — and it is implemented rather than refused, because a decoder that
/// skipped the flag would read the following bits as pixels and produce noise.
/// </para>
/// </remarks>
internal static class Jbig2RefinementDecoder
{
    /// <summary>The context width both refinement templates pack into.</summary>
    internal const int RefinementContextBits = 13;

    /// <summary>
    /// Template 0's pixels in the bitmap being decoded, most significant first.
    /// The null is A1's slot, which the region header may move.
    /// </summary>
    private static readonly (int X, int Y)?[] Coding0 = [(0, -1), (1, -1), (-1, 0), null];

    /// <summary>Template 0's pixels in the reference, with A2's slot last.</summary>
    private static readonly (int X, int Y)?[] Reference0 =
        [(0, -1), (1, -1), (-1, 0), (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1), null];

    /// <summary>Template 1 has no adaptive pixels: ten fixed positions.</summary>
    private static readonly (int X, int Y)?[] Coding1 = [(-1, -1), (0, -1), (1, -1), (-1, 0)];

    private static readonly (int X, int Y)?[] Reference1 = [(0, -1), (-1, 0), (0, 0), (1, 0), (0, 1), (1, 1)];

    /// <summary>
    /// Decodes a refinement of <paramref name="reference"/>, or null when the
    /// request is outside what this decodes.
    /// </summary>
    /// <param name="referenceDx">
    /// Where the reference sits under the bitmap being decoded: a pixel at
    /// <c>(x, y)</c> is refined against the reference at
    /// <c>(x - referenceDx, y - referenceDy)</c>.
    /// </param>
    public static byte[]? Decode(
        MqDecoder decoder,
        MqContexts contexts,
        int width,
        int height,
        int template,
        bool typicalPrediction,
        Jbig2Bitmap reference,
        int referenceDx,
        int referenceDy,
        ReadOnlySpan<(int X, int Y)> adaptive)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(reference);

        if (template is < 0 or > 1 || width <= 0 || height <= 0)
            return null;

        if ((long)width * height > int.MaxValue)
            return null;

        (int X, int Y)[] coding = Resolve(template == 0 ? Coding0 : Coding1, adaptive, slot: 0);
        (int X, int Y)[] referenced = Resolve(template == 0 ? Reference0 : Reference1, adaptive, slot: 1);

        var bitmap = new byte[width * height];

        // The context a row's typical-prediction bit is decoded against: a fixed
        // value per template, as the standard states it.
        int typicalContext = template == 0 ? 0x0100 : 0x0080;
        bool predicting = false;

        for (int y = 0; y < height; y++)
        {
            if (typicalPrediction && decoder.Decode(contexts, typicalContext) == 1)
                predicting = !predicting;

            for (int x = 0; x < width; x++)
            {
                int referenceX = x - referenceDx;
                int referenceY = y - referenceDy;

                if (predicting && Settled(reference, referenceX, referenceY) is byte settled)
                {
                    // The reference's neighbourhood agrees with itself, so the
                    // refined pixel is that value and costs nothing to say.
                    bitmap[(y * width) + x] = settled;
                    continue;
                }

                int context = 0;
                foreach ((int dx, int dy) in coding)
                    context = (context << 1) | At(bitmap, width, height, x + dx, y + dy);

                foreach ((int dx, int dy) in referenced)
                    context = (context << 1) | reference.At(referenceX + dx, referenceY + dy);

                bitmap[(y * width) + x] = (byte)decoder.Decode(contexts, context);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// The value the reference's three-by-three neighbourhood agrees on, or null
    /// where it does not and the pixel has to be decoded after all.
    /// </summary>
    /// <remarks>
    /// Shared with the encoder in the test suite rather than written twice: it is
    /// a fact about the reference and not a coding decision, and an encoder that
    /// disagreed with the decoder about which pixels are settled would produce a
    /// stream neither could read.
    /// </remarks>
    internal static byte? Settled(Jbig2Bitmap reference, int x, int y)
    {
        int sum = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
                sum += reference.At(x + dx, y + dy);
        }

        return sum switch
        {
            0 => (byte)0,
            9 => (byte)1,
            _ => null,
        };
    }

    /// <summary>The template with its adaptive slot filled from the header.</summary>
    /// <param name="slot">
    /// Which adaptive pixel this template takes: A1 for the bitmap being decoded,
    /// A2 for the reference. Template 1 has neither and is returned as it stands.
    /// </param>
    internal static (int X, int Y)[] Resolve(
        (int X, int Y)?[] template,
        ReadOnlySpan<(int X, int Y)> adaptive,
        int slot)
    {
        var resolved = new (int X, int Y)[template.Length];
        for (int i = 0; i < template.Length; i++)
        {
            resolved[i] = template[i] is (int x, int y)
                ? (x, y)
                : slot < adaptive.Length ? adaptive[slot] : (-1, -1);
        }

        return resolved;
    }

    /// <summary>The template pair a caller needs to resolve its own adaptive pixels.</summary>
    internal static ((int X, int Y)?[] Coding, (int X, int Y)?[] Reference) Templates(int template) =>
        template == 0 ? (Coding0, Reference0) : (Coding1, Reference1);

    private static int At(byte[] bitmap, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height ? bitmap[(y * width) + x] : 0;
}
