using System;
using System.Globalization;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// The way a run of text reads on the page as displayed: the direction its
/// baseline runs in, and which side of the baseline its letters stand on.
/// </summary>
/// <remarks>
/// <para>
/// Everything that sets text into lines and paragraphs measures along a
/// baseline and across it. On an upright page those are the page's own x and y,
/// and <see cref="Upright"/> is that frame. A label running up the margin, a
/// heading turned in a narrow table column, a stamp set at a slant: each reads
/// along a baseline of its own, and measured in its own frame it is ordinary
/// text again, with lines that stack and words that follow one another.
/// </para>
/// <para>
/// <see cref="Across"/> grows towards the top of the letters, whichever way
/// they face, so lines read in descending order of it in every frame - a
/// mirrored run's lines too, whose letters stand on the other side of the
/// baseline from where a turn alone would put them.
/// </para>
/// </remarks>
internal readonly record struct PdfTextFrame
{
    private PdfTextFrame(double cos, double sin, bool mirrored)
    {
        Cos = cos;
        Sin = sin;
        IsMirrored = mirrored;
    }

    /// <summary>Left to right along the page as displayed, letters standing up.</summary>
    public static PdfTextFrame Upright { get; } = new(1, 0, false);

    /// <summary>The cosine of the angle the baseline makes with the page's x axis.</summary>
    public double Cos { get; }

    /// <summary>The sine of that angle.</summary>
    public double Sin { get; }

    /// <summary>
    /// True where the letters stand on the right-hand side of the baseline as it
    /// runs: text drawn reflected, as a mirror shows it.
    /// </summary>
    public bool IsMirrored { get; }

    /// <summary>The baseline's angle, counter-clockwise from the page's x axis, to the nearest whole degree.</summary>
    public int Degrees
    {
        get
        {
            int degrees = (int)Math.Round(Math.Atan2(Sin, Cos) * 180 / Math.PI);
            return ((degrees % 360) + 360) % 360;
        }
    }

    /// <summary>
    /// This frame turned to the nearest whole degree. Runs that read the same
    /// way to within half a degree share it, so they can be set into lines
    /// together; a page measured in two frames a fraction of a degree apart
    /// drifts by points across its width.
    /// </summary>
    public PdfTextFrame Settled
    {
        get
        {
            int degrees = Degrees;
            (double cos, double sin) = degrees switch
            {
                0 => (1d, 0d),
                90 => (0d, 1d),
                180 => (-1d, 0d),
                270 => (0d, -1d),
                _ => (Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180)),
            };

            return new PdfTextFrame(cos, sin, IsMirrored);
        }
    }

    /// <summary>
    /// The frame text set through <paramref name="matrix"/> reads in: along the
    /// matrix's first column, with the letters standing up along its second.
    /// </summary>
    /// <remarks>
    /// A matrix that sets no baseline at all - one that scales text space to a
    /// point - has no direction to read in, and is read as upright.
    /// </remarks>
    public static PdfTextFrame Of(PdfMatrix matrix)
    {
        double length = Math.Sqrt((matrix.A * matrix.A) + (matrix.B * matrix.B));
        if (!double.IsFinite(length) || length <= 0)
            return Upright;

        double cos = matrix.A / length;
        double sin = matrix.B / length;

        // Where the letters' tops point, against the baseline's left-hand normal.
        double up = (matrix.C * -sin) + (matrix.D * cos);
        return new PdfTextFrame(cos, sin, up < 0);
    }

    /// <summary>How far along this frame's baseline a point on the page lies.</summary>
    public double Along(double x, double y) => (x * Cos) + (y * Sin);

    /// <summary>How far across this frame's baseline a point on the page lies, towards the letters' tops.</summary>
    public double Across(double x, double y)
    {
        double across = (y * Cos) - (x * Sin);
        return IsMirrored ? -across : across;
    }

    /// <summary>The point on the page that lies <paramref name="along"/> and <paramref name="across"/> this frame.</summary>
    public (double X, double Y) ToPage(double along, double across)
    {
        double normal = IsMirrored ? -across : across;
        return ((along * Cos) - (normal * Sin), (along * Sin) + (normal * Cos));
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Degrees} degrees{(IsMirrored ? ", mirrored" : string.Empty)}");
}
