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

    /// <summary>
    /// True when <paramref name="family"/> resolves to a face of its own, not to
    /// the host's fallback or a metric model standing in for one.
    /// </summary>
    public bool HasFace(string? family) =>
        !string.IsNullOrWhiteSpace(family) &&
        _fontService.ResolveWithOutcome(family).Outcome is FontResolutionOutcome.ExactMatch or
            FontResolutionOutcome.StyleMatch or FontResolutionOutcome.DirectoryMatch;

    public double GetWhitespaceWidth(BFontStyle font) => MeasureAdvance(" ", font);

    public bool HasUnmappedVerticalCjk(string text, BFontStyle font)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        bool bold = font.Weight >= BFontWeight.Bold;
        bool italic = font.Slant is BFontSlant.Italic or BFontSlant.Oblique;

        ResolvedFontFace? face = _fontService.ResolveFace(font.FamilyName, bold, italic);
        if (face?.TrueType is not { } tt)
            return false;

        bool hasCjk = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
                continue;
            int codepoint = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                ? char.ConvertToUtf32(c, text[++i])
                : c;
            if (codepoint < 0x3000 || tt.GetGlyphIndex(codepoint) != 0)
                return false;
            hasCjk = true;
        }

        return hasCjk;
    }
}
