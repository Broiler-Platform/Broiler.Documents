using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Comparison;

/// <summary>How the difference image is drawn.</summary>
public enum DiffStyle
{
    /// <summary>The first image, faded, with differing pixels marked. Shows where in the page the difference is.</summary>
    Overlay,

    /// <summary>Black where the images agree, white where they differ. Feeds cleanly into another tool.</summary>
    Mask,

    /// <summary>Differing pixels coloured by how far apart they are, from yellow through red.</summary>
    Heat,
}

/// <summary>The measured difference between two images.</summary>
public sealed class ImageComparison
{
    private ImageComparison(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Width of the compared region: the smaller of the two when sizes differ.</summary>
    public int Width { get; }

    /// <summary>Height of the compared region.</summary>
    public int Height { get; }

    public int LeftWidth { get; private set; }

    public int LeftHeight { get; private set; }

    public int RightWidth { get; private set; }

    public int RightHeight { get; private set; }

    /// <summary>True when the two images are not the same size.</summary>
    public bool SizeDiffers => LeftWidth != RightWidth || LeftHeight != RightHeight;

    /// <summary>Pixels in the compared region whose channel delta exceeded the tolerance.</summary>
    public long DifferingPixels { get; private set; }

    /// <summary>Pixels in the compared region.</summary>
    public long ComparedPixels => (long)Width * Height;

    /// <summary>Differing pixels as a fraction of the compared region.</summary>
    public double DifferingRatio => ComparedPixels == 0 ? 0 : (double)DifferingPixels / ComparedPixels;

    /// <summary>The largest single-channel difference found, 0-255.</summary>
    public int MaxChannelDelta { get; private set; }

    /// <summary>Mean absolute channel difference over the compared region, 0-255.</summary>
    public double MeanAbsoluteError { get; private set; }

    /// <summary>Root mean square channel difference over the compared region, 0-255.</summary>
    public double RootMeanSquareError { get; private set; }

    /// <summary>The smallest rectangle containing every differing pixel, or null when there are none.</summary>
    public (int Left, int Top, int Right, int Bottom)? DifferenceBounds { get; private set; }

    /// <summary>The difference image, when one was asked for. The caller disposes it.</summary>
    public BBitmap? Diff { get; private set; }

    /// <summary>
    /// Compares two images pixel by pixel.
    /// </summary>
    /// <param name="left">The reference image.</param>
    /// <param name="right">The image under test.</param>
    /// <param name="tolerance">
    /// Per-channel difference that still counts as equal, 0-255. Zero is exact.
    /// Anti-aliased glyph edges move by one or two levels for reasons that have
    /// nothing to do with a codec, so a small tolerance is usually the honest
    /// setting for a rendered-text comparison.
    /// </param>
    /// <param name="style">How to draw the difference image, or null for none.</param>
    /// <remarks>
    /// Images of different sizes are compared over the region they share rather
    /// than refused. The size difference is reported and is itself a finding -
    /// usually one document paginating differently from the other - and refusing
    /// to compare would throw away the pixels that could say why.
    /// </remarks>
    public static ImageComparison Compare(BBitmap left, BBitmap right, int tolerance, DiffStyle? style)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int width = Math.Min(left.Width, right.Width);
        int height = Math.Min(left.Height, right.Height);

        var comparison = new ImageComparison(width, height)
        {
            LeftWidth = left.Width,
            LeftHeight = left.Height,
            RightWidth = right.Width,
            RightHeight = right.Height,
        };

        byte[] leftPixels = left.CopyRgba();
        byte[] rightPixels = right.CopyRgba();

        BBitmap? diff = null;
        if (style is not null)
        {
            diff = new BBitmap(Math.Max(left.Width, right.Width), Math.Max(left.Height, right.Height));
            diff.Clear(style == DiffStyle.Mask ? BColor.Black : new BColor(0x20, 0x00, 0x30));
        }

        long absoluteTotal = 0;
        double squareTotal = 0;
        int boundsLeft = int.MaxValue;
        int boundsTop = int.MaxValue;
        int boundsRight = int.MinValue;
        int boundsBottom = int.MinValue;

        for (int y = 0; y < height; y++)
        {
            int leftRow = y * left.Width * 4;
            int rightRow = y * right.Width * 4;

            for (int x = 0; x < width; x++)
            {
                int l = leftRow + (x * 4);
                int r = rightRow + (x * 4);

                int deltaR = Math.Abs(leftPixels[l] - rightPixels[r]);
                int deltaG = Math.Abs(leftPixels[l + 1] - rightPixels[r + 1]);
                int deltaB = Math.Abs(leftPixels[l + 2] - rightPixels[r + 2]);
                int deltaA = Math.Abs(leftPixels[l + 3] - rightPixels[r + 3]);
                int worst = Math.Max(Math.Max(deltaR, deltaG), Math.Max(deltaB, deltaA));

                absoluteTotal += deltaR + deltaG + deltaB + deltaA;
                squareTotal += ((double)deltaR * deltaR) + ((double)deltaG * deltaG) +
                    ((double)deltaB * deltaB) + ((double)deltaA * deltaA);

                if (worst > comparison.MaxChannelDelta)
                    comparison.MaxChannelDelta = worst;

                bool differs = worst > tolerance;
                if (differs)
                {
                    comparison.DifferingPixels++;
                    boundsLeft = Math.Min(boundsLeft, x);
                    boundsTop = Math.Min(boundsTop, y);
                    boundsRight = Math.Max(boundsRight, x);
                    boundsBottom = Math.Max(boundsBottom, y);
                }

                if (diff is not null)
                    diff.SetPixel(x, y, DiffPixel(style!.Value, differs, worst, leftPixels, l));
            }
        }

        // A size difference is a difference. Marking the region only one image
        // covers keeps the diff image honest instead of showing a clean page
        // where the other export had content.
        if (diff is not null && comparison.SizeDiffers)
            MarkUncomparedRegion(diff, width, height, style!.Value);

        long channels = comparison.ComparedPixels * 4;
        comparison.MeanAbsoluteError = channels == 0 ? 0 : (double)absoluteTotal / channels;
        comparison.RootMeanSquareError = channels == 0 ? 0 : Math.Sqrt(squareTotal / channels);
        comparison.Diff = diff;

        if (comparison.DifferingPixels > 0)
            comparison.DifferenceBounds = (boundsLeft, boundsTop, boundsRight, boundsBottom);

        return comparison;
    }

    /// <summary>True when the comparison is within every threshold it was given.</summary>
    public bool Passes(long maxDifferingPixels, double maxDifferingRatio, bool requireSameSize) =>
        (!requireSameSize || !SizeDiffers) &&
        DifferingPixels <= maxDifferingPixels &&
        DifferingRatio <= maxDifferingRatio;

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["left"] = new JsonObject { ["width"] = LeftWidth, ["height"] = LeftHeight },
            ["right"] = new JsonObject { ["width"] = RightWidth, ["height"] = RightHeight },
            ["sizeDiffers"] = SizeDiffers,
            ["comparedWidth"] = Width,
            ["comparedHeight"] = Height,
            ["comparedPixels"] = ComparedPixels,
            ["differingPixels"] = DifferingPixels,
            ["differingRatio"] = Round(DifferingRatio, 8),
            ["maxChannelDelta"] = MaxChannelDelta,
            ["meanAbsoluteError"] = Round(MeanAbsoluteError, 6),
            ["rootMeanSquareError"] = Round(RootMeanSquareError, 6),
        };

        if (DifferenceBounds is (int left, int top, int right, int bottom))
        {
            json["differenceBounds"] = new JsonObject
            {
                ["left"] = left,
                ["top"] = top,
                ["right"] = right,
                ["bottom"] = bottom,
                ["width"] = right - left + 1,
                ["height"] = bottom - top + 1,
            };
        }
        else
        {
            json["differenceBounds"] = null;
        }

        return json;
    }

    /// <summary>The comparison as report lines.</summary>
    public string[] Describe()
    {
        var lines = new System.Collections.Generic.List<string>
        {
            string.Format(
                CultureInfo.InvariantCulture,
                "  size          {0}x{1} vs {2}x{3}{4}",
                LeftWidth,
                LeftHeight,
                RightWidth,
                RightHeight,
                SizeDiffers ? "   (DIFFERENT)" : string.Empty),
            string.Format(
                CultureInfo.InvariantCulture,
                "  differing     {0} of {1} pixels ({2:P4})",
                DifferingPixels,
                ComparedPixels,
                DifferingRatio),
            string.Format(CultureInfo.InvariantCulture, "  max delta     {0} of 255", MaxChannelDelta),
            string.Format(CultureInfo.InvariantCulture, "  mean error    {0:F4}", MeanAbsoluteError),
            string.Format(CultureInfo.InvariantCulture, "  rms error     {0:F4}", RootMeanSquareError),
        };

        if (DifferenceBounds is (int left, int top, int right, int bottom))
        {
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "  bounds        ({0},{1}) to ({2},{3}), {4}x{5} pixels",
                left,
                top,
                right,
                bottom,
                right - left + 1,
                bottom - top + 1));
        }

        return lines.ToArray();
    }

    private static BColor DiffPixel(DiffStyle style, bool differs, int delta, byte[] leftPixels, int index)
    {
        switch (style)
        {
            case DiffStyle.Mask:
                return differs ? BColor.White : BColor.Black;

            case DiffStyle.Heat:
                if (!differs)
                    return new BColor(0x10, 0x10, 0x18);
                // Yellow at the smallest visible difference through red at the
                // largest, so the eye ranks severity without reading numbers.
                return new BColor(0xFF, (byte)Math.Max(0, 255 - delta), 0x20);

            default:
                if (differs)
                    return new BColor(0xFF, 0x20, 0x60);

                // The agreeing pixels stay as a dim ghost of the reference image,
                // so a lone differing pixel can be located on the page rather
                // than floating in a black field.
                return new BColor(
                    (byte)(leftPixels[index] / 4),
                    (byte)(leftPixels[index + 1] / 4),
                    (byte)((leftPixels[index + 2] / 4) + 32));
        }
    }

    private static void MarkUncomparedRegion(BBitmap diff, int width, int height, DiffStyle style)
    {
        BColor mark = style == DiffStyle.Mask ? BColor.White : new BColor(0xFF, 0xA0, 0x00);

        for (int y = 0; y < diff.Height; y++)
        {
            for (int x = 0; x < diff.Width; x++)
            {
                if (x < width && y < height)
                    continue;

                // A diagonal hatch, so the region reads as "not compared" rather
                // than as a solid block of difference.
                diff.SetPixel(x, y, ((x + y) % 8) < 3 ? mark : BColor.Black);
            }
        }
    }

    private static double Round(double value, int digits) =>
        double.IsFinite(value) ? Math.Round(value, digits) : 0;
}
