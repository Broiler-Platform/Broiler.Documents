using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace Broiler.Documents.Office;

/// <summary>
/// The smallest rectangle that contains every inked pixel on a page, in
/// top-down pixel coordinates, both edges inclusive.
/// </summary>
/// <remarks>
/// This is the single most useful number the comparison produces, and the
/// measurement is the reason why: across three different renderings of the
/// same PDF - poppler's splash backend, poppler's cairo backend, and
/// LibreOffice's own PNG export - the first and last inked rows came out
/// identical, to the pixel, while the pixels between them differed by up to
/// 14% of the page. Text baselines land where the layout puts them; only the
/// glyph rasterisation underneath disagrees. So the box is measuring the thing
/// this suite actually cares about, and is blind to the thing it does not.
/// </remarks>
internal readonly record struct InkBox(int Left, int Top, int Right, int Bottom)
{
    /// <summary>
    /// True when nothing was inked. An empty box is not a box at the origin,
    /// and the distinction matters: a blank page and a page with a single dot
    /// in the corner would otherwise report the same geometry.
    /// </summary>
    public bool IsEmpty => Right < Left || Bottom < Top;

    /// <summary>
    /// The box a page with no ink on it produces. The edges are deliberately
    /// inverted rather than zeroed so that <see cref="IsEmpty"/> is a property
    /// of the value itself and survives being copied, compared and printed.
    /// </summary>
    public static readonly InkBox Empty = new(0, 0, -1, -1);
}

/// <summary>
/// The metrics this suite compares two renderings with, and the reasoning for
/// each one being the metric it is.
/// </summary>
/// <remarks>
/// <para>
/// The problem these solve is that a pixel-exact comparison between two
/// renderers answers a question nobody asked. Measured on one PDF at one
/// resolution: two backends inside poppler itself, splash against cairo,
/// disagreed on 8.39% of pixels, and LibreOffice's own PNG export disagreed
/// with poppler on 14.44%. None of those three renderings is wrong. They
/// antialias differently, hint differently, and round a glyph's coverage
/// differently, and a suite that reported those numbers as failures would be
/// reporting the font rasteriser, on every single page, for ever.
/// </para>
/// <para>
/// After a blur of about 2.5 pixels and a threshold of 32, the same two
/// numbers fell to 0.000% and 0.054%. That is the whole argument for the blur:
/// it is not leniency and it is not a fudge factor tuned until the suite went
/// green. It is the step that takes the rasteriser out of the measurement and
/// leaves the layout behind, and the evidence that it does so is that it
/// collapses a known-irrelevant difference to nothing while the geometry below
/// it is untouched.
/// </para>
/// <para>
/// Everything else here measures ink geometry rather than pixels for the same
/// reason, and was chosen the same way: by checking, against three renderings
/// that were all correct, that it survived the swap. A metric that cannot tell
/// splash from cairo is a metric that will only speak up when the layout has
/// actually moved.
/// </para>
/// </remarks>
internal static class InkProfile
{
    /// <summary>
    /// Luma at or below this counts as ink. Set well above black on purpose:
    /// an antialiased glyph edge is grey, and a threshold that only accepted
    /// near-black would measure a stem as narrower on whichever renderer
    /// softened it more, which is the rasteriser difference coming back in
    /// through the door the blur was built to close.
    /// </summary>
    public const byte InkThreshold = 160;

    public static InkBox Box(RawImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        byte[] luma = image.Luma;

        for (int y = 0; y < image.Height; y++)
        {
            int row = y * image.Width;
            for (int x = 0; x < image.Width; x++)
            {
                if (luma[row + x] > InkThreshold)
                    continue;

                if (x < left)
                    left = x;
                if (x > right)
                    right = x;
                if (y < top)
                    top = y;

                bottom = y;
            }
        }

        return right < left ? InkBox.Empty : new InkBox(left, top, right, bottom);
    }

    /// <summary>
    /// Inked pixels as a fraction of the page, 0..1. Coarse by design: it will
    /// not notice a paragraph moving, but it will notice a paragraph going
    /// missing, and those are different failures that deserve different
    /// evidence.
    /// </summary>
    public static double Coverage(RawImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        long area = (long)image.Width * image.Height;
        if (area <= 0)
            return 0.0;

        long inked = 0;
        byte[] luma = image.Luma;
        for (int i = 0; i < luma.Length; i++)
        {
            if (luma[i] <= InkThreshold)
                inked++;
        }

        return inked / (double)area;
    }

    /// <summary>
    /// Ink per horizontal band, L2-normalised. <paramref name="bandHeight"/>
    /// is in pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape of the page written down as a vector: where the lines
    /// are, how heavy each one is, how much white sits between the paragraphs.
    /// Normalising it is what makes it comparable across two renderings that
    /// disagree about how many pixels a glyph covers - one may lay down 3% more
    /// ink everywhere, and after normalisation that is the same page, which is
    /// the right answer, because it is the same page.
    /// </para>
    /// <para>
    /// Ink per pixel is <c>(255 - luma) / 255.0</c> rather than a count of
    /// pixels past the threshold, so a band of pale antialiasing contributes in
    /// proportion to how pale it is instead of appearing and disappearing as
    /// the rasteriser nudges it across the threshold.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Smooths a profile so that a shift smaller than a line of text does not
    /// read as a different document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the unsmoothed comparison was measured and found
    /// useless. Two renderings of one page with the same fonts, the same page
    /// box and a coverage difference of five parts in a hundred thousand -
    /// which is to say the same ink, in the same amount - scored a cosine
    /// similarity of 0.11. The reason is not subtle once seen: a page of text is
    /// a sparse, almost periodic signal of inked lines separated by blank
    /// leading, and shifting such a signal by half a line puts every peak of one
    /// vector against a trough of the other. The metric was reporting a
    /// vertical offset of a few pixels as a total disagreement about content.
    /// </para>
    /// <para>
    /// So the profile is blurred before it is compared, for the same reason the
    /// page bitmaps are: to take a difference that belongs to the rasteriser and
    /// the typesetter out of a measurement that is supposed to be about what is
    /// on the page. What survives is what the metric was always meant to catch -
    /// a paragraph that is missing, content that landed on the wrong page, a
    /// block that moved by more than its own height. What no longer registers is
    /// a line sitting three pixels lower, which
    /// <see cref="Box"/> already reports and reports better.
    /// </para>
    /// <para>
    /// The radius is in bands rather than pixels, so a caller that changes the
    /// band height changes the smoothing with it and the metric keeps meaning
    /// the same thing at two resolutions.
    /// </para>
    /// </remarks>
    public static double[] Smooth(double[] profile, int radius)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (radius < 1 || profile.Length == 0)
            return profile;

        double[] smoothed = new double[profile.Length];
        for (int i = 0; i < profile.Length; i++)
        {
            double total = 0.0;
            int count = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                int index = i + offset;
                if (index < 0 || index >= profile.Length)
                    continue;

                total += profile[index];
                count++;
            }

            smoothed[i] = count == 0 ? 0.0 : total / count;
        }

        // Re-normalised, because the caller compares directions rather than
        // magnitudes and an averaging pass shrinks the vector.
        double norm = 0.0;
        for (int i = 0; i < smoothed.Length; i++)
            norm += smoothed[i] * smoothed[i];

        norm = Math.Sqrt(norm);
        if (norm <= 0.0)
            return smoothed;

        for (int i = 0; i < smoothed.Length; i++)
            smoothed[i] /= norm;

        return smoothed;
    }

    public static double[] RowProfile(RawImage image, int bandHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (bandHeight < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bandHeight), bandHeight, "A band must be at least one pixel tall.");
        }

        int bands = (image.Height + bandHeight - 1) / bandHeight;
        double[] profile = new double[bands];
        if (bands == 0)
            return profile;

        byte[] luma = image.Luma;
        for (int y = 0; y < image.Height; y++)
        {
            int row = y * image.Width;
            double ink = 0.0;
            for (int x = 0; x < image.Width; x++)
                ink += (255 - luma[row + x]) / 255.0;

            profile[y / bandHeight] += ink;
        }

        double norm = 0.0;
        for (int i = 0; i < profile.Length; i++)
            norm += profile[i] * profile[i];

        norm = Math.Sqrt(norm);
        if (norm <= 0.0)
            return profile;

        for (int i = 0; i < profile.Length; i++)
            profile[i] /= norm;

        return profile;
    }

    /// <summary>
    /// 1.0 when identical in shape. Vectors of different lengths are compared
    /// over the shorter, which is the honest answer when the two pages differ
    /// in height.
    /// </summary>
    /// <remarks>
    /// The blank cases are handled first and explicitly, because they are the
    /// ones a plain dot product gets wrong by dividing by zero and returning
    /// NaN - which then propagates through the report as a number that is
    /// neither a pass nor a fail. Two blank pages agree completely, so that is
    /// 1.0. One blank page and one with ink on it agree about nothing at all,
    /// so that is 0.0. Neither of those is an error, and neither is rare: a
    /// document whose first page is a title sheet produces one on almost every
    /// run.
    /// </remarks>
    public static double CosineSimilarity(double[] left, double[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int length = Math.Min(left.Length, right.Length);
        double dot = 0.0;
        double leftNorm = 0.0;
        double rightNorm = 0.0;

        // The norms are taken over the truncated prefix, not over the whole of
        // each vector. Normalising by ink that was never compared would make
        // the taller page look less like itself the taller it got.
        for (int i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        bool leftBlank = leftNorm <= 0.0;
        bool rightBlank = rightNorm <= 0.0;
        if (leftBlank && rightBlank)
            return 1.0;
        if (leftBlank || rightBlank)
            return 0.0;

        double similarity = dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        if (similarity > 1.0)
            return 1.0;

        return similarity < -1.0 ? -1.0 : similarity;
    }

    /// <summary>
    /// Separable box blur, radius in pixels. Returns a new image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separable and running-sum, both of which matter at the sizes this suite
    /// works at. A page at 150 dpi is around three megapixels; the direct form
    /// of this filter would touch <c>(2r+1)^2</c> pixels for each one of them,
    /// so a radius of 3 would be forty-nine reads per pixel and a radius of 10
    /// would be four hundred and forty-one. A horizontal pass followed by a
    /// vertical one gives the identical result for a box kernel, and carrying
    /// a running sum makes each pass cost two reads per pixel whatever the
    /// radius is. The blur then stops being a thing anybody has to budget for,
    /// which is what allows the radius to be a tuning knob rather than a
    /// commitment.
    /// </para>
    /// <para>
    /// Edges clamp: a window that runs off the page repeats the edge pixel.
    /// That keeps the divisor constant at <c>2r+1</c> for every output pixel,
    /// so the margins are not darkened by averaging against an imaginary black
    /// border, and the ink box measured after a blur still sits where the ink
    /// is.
    /// </para>
    /// </remarks>
    public static RawImage BoxBlur(RawImage image, int radius)
    {
        ArgumentNullException.ThrowIfNull(image);

        int width = image.Width;
        int height = image.Height;
        byte[] source = image.Luma;

        if (radius <= 0 || width == 0 || height == 0)
            return RawImage.FromLuma(width, height, (byte[])source.Clone());

        int window = (radius * 2) + 1;

        // The intermediate holds the horizontal average as an integer rather
        // than a byte only so the rounding happens once per pass instead of
        // being re-rounded; the arithmetic stays integral either way, so two
        // machines cannot produce two blurs.
        int[] horizontal = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            long sum = 0;
            for (int k = -radius; k <= radius; k++)
                sum += source[row + Clamp(k, width)];

            for (int x = 0; x < width; x++)
            {
                horizontal[row + x] = (int)((sum + radius) / window);
                sum += source[row + Clamp(x + radius + 1, width)];
                sum -= source[row + Clamp(x - radius, width)];
            }
        }

        byte[] blurred = new byte[width * height];
        for (int x = 0; x < width; x++)
        {
            long sum = 0;
            for (int k = -radius; k <= radius; k++)
                sum += horizontal[(Clamp(k, height) * width) + x];

            for (int y = 0; y < height; y++)
            {
                blurred[(y * width) + x] = (byte)((sum + radius) / window);
                sum += horizontal[(Clamp(y + radius + 1, height) * width) + x];
                sum -= horizontal[(Clamp(y - radius, height) * width) + x];
            }
        }

        return RawImage.FromLuma(width, height, blurred);
    }

    /// <summary>
    /// Fraction of pixels whose blurred luma differs by more than
    /// <paramref name="delta"/>. Compares the overlapping region when the two
    /// differ in size.
    /// </summary>
    /// <remarks>
    /// An empty overlap - either image having no pixels, or the two sharing no
    /// rows or columns - is reported as 1.0, total disagreement. Returning 0.0
    /// would be the arithmetic answer to "what fraction of nothing differed",
    /// and it would arrive in the report as a perfect match. Two images with
    /// nothing in common are not evidence of agreement.
    /// </remarks>
    public static double BlurredDifferenceRatio(RawImage left, RawImage right, int radius, int delta)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        RawImage blurredLeft = BoxBlur(left, radius);
        RawImage blurredRight = BoxBlur(right, radius);

        int width = Math.Min(blurredLeft.Width, blurredRight.Width);
        int height = Math.Min(blurredLeft.Height, blurredRight.Height);
        if (width <= 0 || height <= 0)
            return 1.0;

        byte[] a = blurredLeft.Luma;
        byte[] b = blurredRight.Luma;
        long differing = 0;

        for (int y = 0; y < height; y++)
        {
            int rowA = y * blurredLeft.Width;
            int rowB = y * blurredRight.Width;
            for (int x = 0; x < width; x++)
            {
                int difference = a[rowA + x] - b[rowB + x];
                if (difference < 0)
                    difference = -difference;
                if (difference > delta)
                    differing++;
            }
        }

        return differing / ((double)width * height);
    }

    /// <summary>
    /// The largest single-edge disagreement between two ink boxes, in pixels.
    /// </summary>
    /// <remarks>
    /// The worst edge rather than the average of the four, because the average
    /// hides the failure this is here to catch: a block that has shifted down
    /// the page moves the top and bottom edges together, and a mean over all
    /// four edges would halve that and report a shift of ten pixels as five.
    /// One box empty and the other not returns <c>int.MaxValue</c>: there is no
    /// distance between a rectangle and nothing, and a caller thresholding the
    /// result should treat "one side rendered a blank page" as the largest
    /// disagreement there is, not as a small one.
    /// </remarks>
    public static int BoxDelta(InkBox left, InkBox right)
    {
        if (left.IsEmpty && right.IsEmpty)
            return 0;
        if (left.IsEmpty || right.IsEmpty)
            return int.MaxValue;

        int worst = Math.Abs(left.Left - right.Left);
        worst = Math.Max(worst, Math.Abs(left.Top - right.Top));
        worst = Math.Max(worst, Math.Abs(left.Right - right.Right));
        return Math.Max(worst, Math.Abs(left.Bottom - right.Bottom));
    }

    /// <summary>
    /// Proves the two raw readers agree, before anything else is measured.
    /// Returns null when they do, or a sentence naming the disagreement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every number this suite prints is computed from bytes that
    /// <see cref="RawImage"/> parsed out of a file format by hand. Get the row
    /// order backwards, or the channel order, or the stride, and nothing throws
    /// - the metrics keep running and keep producing plausible percentages,
    /// and the report is then confidently meaningless. That is a worse failure
    /// than a crash, because somebody will act on it.
    /// </para>
    /// <para>
    /// So this runs first and the run stops if it fails. It paints a small
    /// page in memory, writes it as the two formats the real tools produce -
    /// a bottom-up 32bpp BMP and a P6 PPM with a comment in its header - reads
    /// both back through the ordinary entry point, and requires the two to
    /// agree with each other and with the shape that was painted. Agreement
    /// alone would not be enough on its own, since two readers can be wrong in
    /// the same direction; the painted rectangle is the independent answer.
    /// The orange block is there for one reason: its luma sits above the ink
    /// threshold as painted and below it if a reader swaps red for blue, so a
    /// channel-order mistake changes the coverage instead of hiding inside it.
    /// The strip under the rectangle is there for another: its luma is exactly
    /// <see cref="InkThreshold"/>, which the doc comment there says counts as
    /// ink, and a page with no pixel on the boundary would let that sentence
    /// and the comparison drift apart without either of them noticing. And the
    /// top-left pixel is there for a third: its red channel is 0x0A, so the
    /// first byte of the PPM's pixel data is a newline. A reader that consumed
    /// a run of whitespace after maxval instead of the single byte the format
    /// specifies would swallow it and shift the entire page by one byte, and
    /// that is a mistake which is otherwise invisible until a real page happens
    /// to start with a dark pixel.
    /// </para>
    /// <para>
    /// A missing or unwritable temporary directory is reported the same way as
    /// a mismatch, as a message rather than an exception, because the caller's
    /// job either way is to say what went wrong and not measure anything.
    /// </para>
    /// </remarks>
    public static string? SelfTest(string temporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);

        const int width = 24;
        const int height = 16;
        byte[] rgb = new byte[width * height * 3];

        void Paint(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int offset = ((y * width) + x) * 3;
                    rgb[offset] = r;
                    rgb[offset + 1] = g;
                    rgb[offset + 2] = b;
                }
            }
        }

        Paint(0, 0, width - 1, height - 1, 255, 255, 255);   // the page
        Paint(0, 0, 0, 0, 10, 255, 255);                     // luma 181, and its first PPM byte is 0x0A
        Paint(5, 3, 12, 9, 0, 0, 0);                         // solid ink, 8 x 7
        Paint(5, 10, 12, 10, 160, 160, 160);                 // luma exactly 160, ink by a hair
        Paint(16, 3, 19, 5, 255, 200, 0);                    // luma 193 as painted, 146 if swapped
        Paint(2, 13, 21, 13, 200, 200, 200);                 // luma 200, just too pale to count

        var expectedBox = new InkBox(5, 3, 12, 10);
        double expectedCoverage = 64.0 / (width * height);

        string ppmPath = Path.Combine(temporaryDirectory, "inkprofile-selftest.ppm");
        string bmpPath = Path.Combine(temporaryDirectory, "inkprofile-selftest.bmp");
        RawImage fromPpm;
        RawImage fromBmp;

        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllBytes(ppmPath, BuildPpm(width, height, rgb));
            File.WriteAllBytes(bmpPath, BuildBmp(width, height, rgb));
            fromPpm = RawImage.Read(ppmPath);
            fromBmp = RawImage.Read(bmpPath);
        }
        catch (IOException error)
        {
            return "the raw image self test could not use " + temporaryDirectory + ": " + error.Message;
        }
        catch (UnauthorizedAccessException error)
        {
            return "the raw image self test could not use " + temporaryDirectory + ": " + error.Message;
        }
        catch (InvalidDataException error)
        {
            return "a reader rejected the self test's own file, which it wrote itself: " + error.Message;
        }
        catch (Exception error) when (error is IndexOutOfRangeException or ArgumentException)
        {
            // A reader is supposed to turn every malformed file into an
            // InvalidDataException that names the fault. If one ever fails to,
            // the exception arrives here, and here is the one place that must
            // not simply propagate it: this runs before anything else is
            // measured, and a stack trace out of the first check reads like the
            // harness broke rather than like the readers did. The type name
            // goes into the message so the difference is still visible.
            return "a reader threw " + error.GetType().Name + " on the self test's own file " +
                "instead of refusing it by name, which is a defect in the reader rather " +
                "than in the file: " + error.Message;
        }

        if (fromBmp.Width != width || fromBmp.Height != height)
        {
            return "the BMP reader returned " + fromBmp.Width + "x" + fromBmp.Height +
                " for a " + width + "x" + height + " bitmap (" + bmpPath + ").";
        }

        if (fromPpm.Width != width || fromPpm.Height != height)
        {
            return "the PPM reader returned " + fromPpm.Width + "x" + fromPpm.Height +
                " for a " + width + "x" + height + " image (" + ppmPath + ").";
        }

        InkBox bmpBox = Box(fromBmp);
        InkBox ppmBox = Box(fromPpm);
        if (bmpBox != ppmBox)
        {
            return "the two readers disagree about the ink box: the BMP gives " + bmpBox +
                " and the PPM gives " + ppmBox + ". One of them has the rows or the " +
                "channels the wrong way round.";
        }

        if (bmpBox != expectedBox)
        {
            return "both readers agree on an ink box of " + bmpBox + " but the page was " +
                "painted with its only ink at " + expectedBox + ".";
        }

        double bmpCoverage = Coverage(fromBmp);
        double ppmCoverage = Coverage(fromPpm);
        if (Math.Abs(bmpCoverage - ppmCoverage) > 1e-12)
        {
            return "the two readers disagree about coverage: the BMP gives " +
                Format(bmpCoverage) + " and the PPM gives " + Format(ppmCoverage) + ".";
        }

        if (Math.Abs(bmpCoverage - expectedCoverage) > 1e-12)
        {
            return "both readers agree on a coverage of " + Format(bmpCoverage) +
                " but the page was painted with " + Format(expectedCoverage) +
                " of it inked.";
        }

        double[] bmpProfile = RowProfile(fromBmp, 4);
        double[] ppmProfile = RowProfile(fromPpm, 4);
        if (bmpProfile.Length != ppmProfile.Length)
        {
            return "the two readers produced row profiles of different lengths: " +
                bmpProfile.Length + " bands from the BMP and " + ppmProfile.Length +
                " from the PPM.";
        }

        for (int i = 0; i < bmpProfile.Length; i++)
        {
            if (Math.Abs(bmpProfile[i] - ppmProfile[i]) <= 1e-9)
                continue;

            return "the two readers disagree about band " + i + " of the row profile: " +
                Format(bmpProfile[i]) + " from the BMP and " + Format(ppmProfile[i]) +
                " from the PPM.";
        }

        double similarity = CosineSimilarity(bmpProfile, ppmProfile);
        if (similarity < 0.999999)
        {
            return "the row profiles of one image read two ways scored " + Format(similarity) +
                " against each other, where anything below 1.0 means the readers " +
                "disagree about the shape of the page.";
        }

        return null;
    }

    private static int Clamp(int value, int limit)
    {
        if (value < 0)
            return 0;

        return value >= limit ? limit - 1 : value;
    }

    private static string Format(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>
    /// A P6 PPM with a comment line in its header, deliberately placed between
    /// the magic and the dimensions. poppler has shipped a version banner
    /// there, so the reader has to skip it, and a self test that only wrote the
    /// simplest possible header would leave that path unexercised until the day
    /// a real file used it.
    /// </summary>
    private static byte[] BuildPpm(int width, int height, byte[] rgb)
    {
        byte[] header = Encoding.ASCII.GetBytes(
            "P6\n# written by InkProfile.SelfTest\n" + width + " " + height + "\n255\n");
        byte[] file = new byte[header.Length + rgb.Length];
        header.CopyTo(file, 0);
        rgb.CopyTo(file, header.Length);
        return file;
    }

    /// <summary>
    /// A 32bpp BI_RGB BMP with a positive height, which is to say bottom-up:
    /// the same layout the renderer was observed to write, including the
    /// vertical flip that the reader has to undo. Writing this one top-down
    /// would make the self test agree with itself while leaving the flip that
    /// every real file needs untested.
    /// </summary>
    private static byte[] BuildBmp(int width, int height, byte[] rgb)
    {
        const int offset = 54;
        int stride = width * 4;
        byte[] file = new byte[offset + (stride * height)];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2, 4), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10, 4), offset);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22, 4), height);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28, 2), 32);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(30, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(34, 4), stride * height);

        for (int y = 0; y < height; y++)
        {
            int source = (height - 1 - y) * width * 3;
            int target = offset + (y * stride);
            for (int x = 0; x < width; x++)
            {
                int from = source + (x * 3);
                int to = target + (x * 4);
                file[to] = rgb[from + 2];
                file[to + 1] = rgb[from + 1];
                file[to + 2] = rgb[from];
                file[to + 3] = 255;
            }
        }

        return file;
    }
}
