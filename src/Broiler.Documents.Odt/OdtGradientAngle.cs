using System;

namespace Broiler.Documents.Odt;

/// <summary>
/// The one conversion between ODF's gradient angle and the model's.
/// </summary>
/// <remarks>
/// <para>
/// The two do not share a zero, and reading one as the other draws a letterhead's
/// stripe across the page instead of down it. Both are degrees and both turn the
/// same way; they differ by where they start. ODF measures from <em>down</em> the
/// page and the model from <em>along</em> it, so the two are a quarter turn apart:
/// <c>model = 90 - odf</c>, which is its own inverse and is why one method serves
/// both directions.
/// </para>
/// <para>
/// Measured rather than derived from prose, because the prose is thin and the
/// cost of guessing is a stripe nobody notices is wrong. LibreOffice was asked to
/// render a black-to-white linear gradient in a square at five ODF angles, and the
/// corners were sampled:
/// </para>
/// <list type="bullet">
///   <item><description><c>0deg</c> - start at the top, end at the bottom: the axis points down.</description></item>
///   <item><description><c>45deg</c> - start top-left: down and to the right.</description></item>
///   <item><description><c>90deg</c> - start at the left, end at the right: the axis points right.</description></item>
///   <item><description><c>135deg</c> - start bottom-left: up and to the right.</description></item>
///   <item><description><c>270deg</c> - start at the right: the axis points left.</description></item>
/// </list>
/// <para>
/// The model's angle is degrees clockwise from the <c>+x</c> axis with <c>y</c>
/// down, running from the start colour to the end colour - which is OOXML's
/// <c>a:lin ang</c> exactly, so the DOCX reader passes its value straight through.
/// The same shape exported by LibreOffice to both formats confirms the quarter
/// turn from the other side: what ODF writes as <c>30deg</c> it writes as
/// <c>ang="3600000"</c>, sixty degrees, in OOXML.
/// </para>
/// </remarks>
internal static class OdtGradientAngle
{
    /// <summary>ODF degrees to the model's, and the model's back to ODF's.</summary>
    /// <remarks>
    /// Normalised into <c>[0, 360)</c> so a round trip writes the angle a reader
    /// expects rather than a negative one it is not obliged to accept - ODF's
    /// <c>angle</c> type permits a sign, but nothing is gained by writing
    /// <c>-45deg</c> where <c>315deg</c> says the same thing.
    /// </remarks>
    public static double ToModel(double degrees)
    {
        if (!double.IsFinite(degrees))
            return 0;

        double turned = (90 - degrees) % 360;
        return turned < 0 ? turned + 360 : turned;
    }

    /// <summary>
    /// The model's degrees as ODF's. The same quarter turn, because subtracting
    /// from ninety is an involution - stated as its own method so a caller reads
    /// which way it is converting.
    /// </summary>
    public static double ToOdf(double degrees) => ToModel(degrees);
}
