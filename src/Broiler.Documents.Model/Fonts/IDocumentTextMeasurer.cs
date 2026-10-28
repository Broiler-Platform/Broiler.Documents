using System;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Model.Fonts;

/// <summary>
/// Text measurement and font metrics interface consumed by document pagination, line wrapping, and painting.
/// </summary>
/// <remarks>
/// Replaces direct reliance on process-global <see cref="BTextMeasurer"/> and ambient <see cref="BSystemFontFiles"/>
/// with an explicitly supplied, operation-scoped font provider.
/// </remarks>
public interface IDocumentTextMeasurer
{
    /// <summary>Measures the horizontal advance width of <paramref name="text"/> in typographic points.</summary>
    double MeasureAdvance(string text, BFontStyle font);

    /// <summary>Returns the total line height of <paramref name="font"/> in typographic points.</summary>
    double GetLineHeight(BFontStyle font);

    /// <summary>Returns the typographic ascent, descent, and line gap in typographic points.</summary>
    (double Ascent, double Descent, double LineGap) GetVerticalMetrics(BFontStyle font);

    /// <summary>True when a genuine italic face is available for <paramref name="family"/>.</summary>
    bool HasItalicFace(string? family);

    /// <summary>Measures the advance width of a single whitespace character in typographic points.</summary>
    double GetWhitespaceWidth(BFontStyle font);
}
