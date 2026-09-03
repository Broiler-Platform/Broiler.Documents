using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Decodes a JBIG2 generic region coded with the arithmetic coder.
/// </summary>
/// <remarks>
/// <para>
/// A generic region is decoded pixel by pixel. Each pixel's probability context
/// is formed from pixels already decoded — some on the two rows above, some to
/// the left on the current row — and the decoder and encoder must agree exactly
/// on which pixels, and in which bit order, or the two produce unrelated
/// bitstreams.
/// </para>
/// <para>
/// <strong>That agreement is what the tests cannot check.</strong> The suite
/// round-trips this against an encoder written beside it, which proves the two
/// halves agree with each other and nothing about whether either agrees with
/// T.88. The templates below are therefore written out coordinate by coordinate,
/// in the raster order the context is packed in, so a reviewer holding the
/// standard can compare them against its figures directly. That is the only
/// check available: the standard's own test sequence is official test material
/// and the source rules exclude it, and no third-party JBIG2 file may be
/// committed as a fixture (IP-020).
/// </para>
/// <para>
/// Four templates exist and all four are implemented, because a document does not
/// choose the easy one for the reader's benefit. Each names adaptive pixels whose
/// position the region header may move; they are substituted at their slot in the
/// ordering rather than appended, since moving a pixel changes where it looks and
/// not which bit it contributes.
/// </para>
/// </remarks>
internal static class Jbig2GenericDecoder
{
    /// <summary>
    /// The fixed pixels of each template, in the raster order the context packs
    /// them: most significant bit first, top row to bottom, left to right within
    /// a row. A null entry is an adaptive pixel's slot, filled from the region
    /// header — <c>A1</c> first, then <c>A2</c>, <c>A3</c>, <c>A4</c>.
    /// </summary>
    /// <remarks>
    /// Read against T.88 figures 4 through 7. Template 0 carries four adaptive
    /// pixels and the rest carry one, which is why only template 0's row above
    /// has two of them.
    /// </remarks>
    private static readonly (int X, int Y)?[][] Templates =
    [
        // Template 0 — 16 pixels. Nominal adaptive positions are
        // A1 (+3,-1), A2 (-3,-1), A3 (+2,-2), A4 (-2,-2).
        [
            null,     (-1, -2), (0, -2), (1, -2), null,          // A4 . . . A3
            null,     (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), null,  // A2 . . . . . A1
            (-4, 0), (-3, 0), (-2, 0), (-1, 0),
        ],

        // Template 1 — 13 pixels. Nominal A1 (+3,-1).
        [
            (-1, -2), (0, -2), (1, -2), (2, -2),
            (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), null,
            (-3, 0), (-2, 0), (-1, 0),
        ],

        // Template 2 — 10 pixels. Nominal A1 (+2,-1).
        [
            (-1, -2), (0, -2), (1, -2),
            (-2, -1), (-1, -1), (0, -1), (1, -1), null,
            (-2, 0), (-1, 0),
        ],

        // Template 3 — 10 pixels, one row of context above. Nominal A1 (+2,-1).
        [
            (-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -1), null,
            (-4, 0), (-3, 0), (-2, 0), (-1, 0),
        ],
    ];

    /// <summary>The adaptive-pixel slot order within each template.</summary>
    private static readonly int[][] AdaptiveOrder =
    [
        [3, 2, 1, 0],   // template 0 fills A4, A3, A2, A1 in raster order
        [0],
        [0],
        [0],
    ];

    /// <summary>
    /// Decodes a region into one byte per pixel, 1 meaning black, or null when
    /// the region is outside what this decodes.
    /// </summary>
    /// <param name="adaptive">
    /// The adaptive pixel positions from the region header, A1 first. Nominal
    /// values are used for any the header did not supply.
    /// </param>
    public static byte[]? Decode(
        ReadOnlyMemory<byte> data,
        int width,
        int height,
        int template,
        bool typicalPrediction,
        ReadOnlySpan<(int X, int Y)> adaptive)
    {
        if (template is < 0 or > 3 || width <= 0 || height <= 0)
            return null;

        (int X, int Y)[] pixels = Resolve(template, adaptive);
        var decoder = new Jbig2ArithmeticDecoder(data);
        var contexts = new Jbig2ArithmeticContexts(16);
        var bitmap = new byte[(long)width * height <= int.MaxValue ? width * height : 0];
        if (bitmap.Length == 0)
            return null;

        // Typical prediction lets a row that repeats the one above it be coded as
        // a single bit. The context it is decoded against is a fixed value per
        // template, which is why it is a constant here rather than a computation.
        int typicalContext = template switch
        {
            0 => 0x9B25,
            1 => 0x0795,
            2 => 0x00E5,
            _ => 0x0195,
        };

        bool skipRow = false;

        for (int y = 0; y < height; y++)
        {
            if (typicalPrediction)
            {
                if (decoder.Decode(contexts, typicalContext) == 1)
                    skipRow = !skipRow;

                if (skipRow)
                {
                    // The row is identical to its predecessor. The first row
                    // having no predecessor leaves it blank, which is what
                    // copying from nothing means.
                    if (y > 0)
                        Array.Copy(bitmap, (y - 1) * width, bitmap, y * width, width);
                    continue;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int context = 0;
                foreach ((int dx, int dy) in pixels)
                    context = (context << 1) | At(bitmap, width, height, x + dx, y + dy);

                bitmap[(y * width) + x] = (byte)decoder.Decode(contexts, context);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// A pixel, or zero outside the bitmap. Every template reaches above the
    /// first rows and left of the first column, and the format's answer there is
    /// that the missing pixels are white.
    /// </summary>
    private static int At(byte[] bitmap, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height ? bitmap[(y * width) + x] : 0;

    /// <summary>The template with its adaptive slots filled in.</summary>
    internal static (int X, int Y)[] Resolve(int template, ReadOnlySpan<(int X, int Y)> adaptive)
    {
        (int X, int Y)?[] slots = Templates[template];
        int[] order = AdaptiveOrder[template];

        var resolved = new (int X, int Y)[slots.Length];
        int next = 0;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is (int x, int y))
            {
                resolved[i] = (x, y);
                continue;
            }

            int which = order[next++];
            resolved[i] = which < adaptive.Length ? adaptive[which] : Nominal(template, which);
        }

        return resolved;
    }

    /// <summary>The adaptive positions the format defines when a header supplies none.</summary>
    private static (int X, int Y) Nominal(int template, int which) => (template, which) switch
    {
        (0, 0) => (3, -1),
        (0, 1) => (-3, -1),
        (0, 2) => (2, -2),
        (0, 3) => (-2, -2),
        (1, _) => (3, -1),
        _ => (2, -1),
    };
}
