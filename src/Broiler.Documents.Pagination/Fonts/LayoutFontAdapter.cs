using System;
using System.Drawing;
using Broiler.Documents.Model.Fonts;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination.Fonts;

/// <summary>
/// Adapts a <see cref="ResolvedFontFace"/> to the <see cref="ILayoutFont"/> contract
/// consumed by CSS box and text layout.
/// </summary>
/// <remarks>
/// <para>
/// <b>Unit Boundaries:</b>
/// <list type="bullet">
///   <item><term><see cref="Size"/></term><description>Typographic points (as resolved by CSS cascade).</description></item>
///   <item><term><see cref="Height"/></term><description>Total line height in CSS pixels (96 DPI).</description></item>
///   <item><term><see cref="UnderlineOffset"/></term><description>Underline baseline offset in CSS pixels.</description></item>
///   <item><term><see cref="LeftPadding"/></term><description>Left bearing in CSS pixels.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class LayoutFontAdapter : ILayoutFont
{
    private readonly ResolvedFontFace _face;
    private readonly double _sizePoints;
    private readonly double _heightPx;
    private readonly double _underlineOffsetPx;
    private readonly double _leftPaddingPx;
    private readonly string? _fontFeatures;

    public LayoutFontAdapter(
        ResolvedFontFace face,
        double sizePoints,
        string? fontFeatures = null,
        double lineSpacingMultiplier = 1.0)
    {
        _face = face ?? throw new ArgumentNullException(nameof(face));
        _sizePoints = sizePoints > 0 ? sizePoints : 12.0;
        _fontFeatures = fontFeatures;

        var (ascentPx, descentPx, lineGapPx) = face.GetVerticalMetricsPixels(_sizePoints);
        double intrinsicHeightPx = FontMetricConversions.IntrinsicLineHeightPixels(ascentPx, descentPx, lineGapPx);

        if (lineSpacingMultiplier > 0 && Math.Abs(lineSpacingMultiplier - 1.0) > 0.001)
        {
            _heightPx = FontMetricConversions.CalculateLineHeightPoints(
                FontMetricConversions.PixelsToPoints(intrinsicHeightPx),
                _sizePoints,
                lineSpacingMultiplier);
            _heightPx = FontMetricConversions.PointsToPixels(_heightPx);
        }
        else
        {
            _heightPx = intrinsicHeightPx;
        }

        _underlineOffsetPx = face.GetUnderlineOffsetPixels(_sizePoints);
        _leftPaddingPx = 0; // Default zero left-bearing margin for neutral layout
    }

    /// <summary>The typographic font size in points.</summary>
    public double Size => _sizePoints;

    /// <summary>The line height in CSS pixels.</summary>
    public double Height => _heightPx;

    /// <summary>Offset of the underline from the baseline, in CSS pixels.</summary>
    public double UnderlineOffset => _underlineOffsetPx;

    /// <summary>Left-side bearing applied before the first glyph, in CSS pixels.</summary>
    public double LeftPadding => _leftPaddingPx;

    /// <summary>OpenType feature tags enabled for this font, or null.</summary>
    public string? FontFeatures => _fontFeatures;

    /// <summary>The underlying resolved font face program.</summary>
    public ResolvedFontFace Face => _face;
}

/// <summary>
/// Adapts an <see cref="IOperationFontService"/> to CSS layout text measurement in CSS pixels.
/// </summary>
public sealed class LayoutEnvironmentFontAdapter
{
    private readonly IOperationFontService _fontService;

    public LayoutEnvironmentFontAdapter(IOperationFontService fontService)
    {
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
    }

    /// <summary>Resolves a font for the given family, point size, weight, slant, and features.</summary>
    public ILayoutFont GetFont(string family, double sizePoints, bool bold = false, bool italic = false, string? fontFeatures = null)
    {
        ResolvedFontFace face = _fontService.ResolveFace(family, bold, italic)
            ?? throw new InvalidOperationException($"Could not resolve font face for {family}");

        return new LayoutFontAdapter(face, sizePoints, fontFeatures);
    }

    /// <summary>Measures the full size of <paramref name="text"/> in CSS pixels.</summary>
    public SizeF MeasureText(ILayoutFont font, string text)
    {
        if (string.IsNullOrEmpty(text))
            return new SizeF(0, (float)font.Height);

        if (font is LayoutFontAdapter adapter)
        {
            double widthPx = adapter.Face.GetAdvancePixels(text, font.Size);
            return new SizeF((float)widthPx, (float)font.Height);
        }

        // Generic fallback if external ILayoutFont
        double pointsWidth = text.Length * font.Size * 0.556;
        double pxWidth = FontMetricConversions.PointsToPixels(pointsWidth);
        return new SizeF((float)pxWidth, (float)font.Height);
    }

    /// <summary>Measures how many characters fit into <paramref name="maxWidthPx"/> in CSS pixels.</summary>
    public void MeasureText(ILayoutFont font, string text, double maxWidthPx, out int charFit, out double charFitWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidthPx <= 0)
        {
            charFit = 0;
            charFitWidth = 0;
            return;
        }

        if (font is LayoutFontAdapter adapter)
        {
            double currentWidth = 0;
            int count = 0;

            for (int i = 0; i < text.Length; i++)
            {
                double charWidth = adapter.Face.GetAdvancePixels(text[i], font.Size);
                if (currentWidth + charWidth > maxWidthPx && count > 0)
                    break;

                currentWidth += charWidth;
                count++;
            }

            charFit = count;
            charFitWidth = currentWidth;
            return;
        }

        // Generic fallback
        double singleCharWidth = FontMetricConversions.PointsToPixels(font.Size * 0.556);
        int fit = singleCharWidth > 0 ? (int)(maxWidthPx / singleCharWidth) : text.Length;
        charFit = Math.Clamp(fit, 0, text.Length);
        charFitWidth = charFit * singleCharWidth;
    }

    /// <summary>Returns the whitespace width in CSS pixels.</summary>
    public double GetWhitespaceWidth(ILayoutFont font)
    {
        if (font is LayoutFontAdapter adapter)
            return adapter.Face.GetAdvancePixels(' ', font.Size);

        return FontMetricConversions.PointsToPixels(font.Size * 0.278);
    }
}
