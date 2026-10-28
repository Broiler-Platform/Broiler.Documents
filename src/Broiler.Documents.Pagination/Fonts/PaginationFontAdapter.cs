using System;
using Broiler.Documents.Model.Fonts;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination.Fonts;

/// <summary>
/// Adapts an <see cref="IOperationFontService"/> to the <see cref="IDocumentTextMeasurer"/> contract
/// consumed by document pagination, line breaking, and rendering.
/// </summary>
public sealed class PaginationFontAdapter : IDocumentTextMeasurer
{
    private readonly IOperationFontService _fontService;

    public PaginationFontAdapter(IOperationFontService fontService)
    {
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
    }

    /// <summary>The underlying operation-scoped font service.</summary>
    public IOperationFontService FontService => _fontService;

    public double MeasureAdvance(string text, BFontStyle font)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        bool bold = font.Weight >= BFontWeight.Bold;
        bool italic = font.Slant is BFontSlant.Italic or BFontSlant.Oblique;

        ResolvedFontFace? face = _fontService.ResolveFace(font.FamilyName, bold, italic);
        if (face?.TrueType is not null)
        {
            return face.GetAdvancePoints(text, font.Size);
        }

        // When no TrueType binary was loaded, fall back to Graphics measurer
        return BTextMeasurer.MeasureAdvance(text, font);
    }

    public double GetLineHeight(BFontStyle font)
    {
        var (ascent, descent, lineGap) = GetVerticalMetrics(font);
        return ascent + descent + lineGap;
    }

    public (double Ascent, double Descent, double LineGap) GetVerticalMetrics(BFontStyle font)
    {
        bool bold = font.Weight >= BFontWeight.Bold;
        bool italic = font.Slant is BFontSlant.Italic or BFontSlant.Oblique;

        ResolvedFontFace? face = _fontService.ResolveFace(font.FamilyName, bold, italic);
        if (face is not null)
        {
            return face.GetVerticalMetricsPoints(font.Size);
        }

        double ascent = font.Size * 0.8;
        double descent = Math.Max(0, BTextMeasurer.GetLineHeight(font) - ascent);
        return (ascent, descent, 0);
    }

    public bool HasItalicFace(string? family) => _fontService.HasItalicFace(family);

    public double GetWhitespaceWidth(BFontStyle font) => MeasureAdvance(" ", font);
}
