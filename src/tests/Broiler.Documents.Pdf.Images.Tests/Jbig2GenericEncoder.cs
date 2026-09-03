namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Encodes a generic region so the decoder has something to be round-tripped
/// against. Test-only, like the arithmetic encoder beside it.
/// </summary>
/// <remarks>
/// <para>
/// It forms each pixel's context exactly as the decoder does, by walking the same
/// template. That is the honest arrangement and also the limit of what the round
/// trip proves: the two halves cannot disagree about the templates, so the tests
/// cannot catch a template read wrongly from the standard. What they do catch is
/// every way the arithmetic coder itself can be wrong, which is most of the
/// surface and all of the fiddly part.
/// </para>
/// <para>
/// Nothing here ships. This repository writes no JBIG2, and an encoder in the
/// product would be a separate decision — the roadmap says so of
/// symbol-substitution encoding specifically, because it can silently change the
/// characters in a scanned document.
/// </para>
/// </remarks>
internal static class Jbig2GenericEncoder
{
    internal static byte[] Encode(
        byte[] image,
        int width,
        int height,
        int template,
        bool typicalPrediction = false,
        (int X, int Y)[]? adaptive = null)
    {
        var encoder = new Jbig2ArithmeticEncoder(16);
        // The decoder's own template resolution, so the two cannot drift apart —
        // and so a template misread from the standard is a single fact rather
        // than two that happen to agree.
        (int X, int Y)[] pixels = Jbig2GenericDecoder.Resolve(template, adaptive ?? []);

        int typicalContext = template switch
        {
            0 => 0x9B25,
            1 => 0x0795,
            2 => 0x00E5,
            _ => 0x0195,
        };

        bool skipping = false;

        for (int y = 0; y < height; y++)
        {
            if (typicalPrediction)
            {
                // Whether this row repeats the one above decides the bit, and
                // the bit toggles the running state rather than setting it.
                bool repeats = y > 0 && RowsMatch(image, width, y);
                encoder.Encode(typicalContext, repeats == skipping ? 0 : 1);
                skipping = repeats;

                if (skipping)
                    continue;
            }

            for (int x = 0; x < width; x++)
            {
                int context = 0;
                foreach ((int dx, int dy) in pixels)
                    context = (context << 1) | At(image, width, height, x + dx, y + dy);

                encoder.Encode(context, image[(y * width) + x]);
            }
        }

        return encoder.Flush();
    }

    private static bool RowsMatch(byte[] image, int width, int y)
    {
        int current = y * width;
        int previous = current - width;
        for (int x = 0; x < width; x++)
        {
            if (image[current + x] != image[previous + x])
                return false;
        }

        return true;
    }

    private static int At(byte[] image, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height ? image[(y * width) + x] : 0;
}
