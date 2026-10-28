using System;
using Broiler.Documents.Model.Fonts;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Adapts an <see cref="IOperationFontService"/> to <see cref="IPdfFontMetricsProvider"/>,
/// translating resolved face metrics into PDF 1/1000 em font space.
/// </summary>
/// <remarks>
/// When a matching face program is provisioned, exact metrics are supplied and <see cref="IsApproximate"/>
/// reports <c>false</c>. When a font is unmapped, fallback to <see cref="PdfApproximateFontMetrics"/> occurs
/// and <see cref="IsApproximate"/> reports <c>true</c>, preserving the writer's diagnostic reporting
/// (<see cref="PdfDiagnosticCodes.WriteMetricsApproximate"/>).
/// </remarks>
public sealed class PdfFontServiceMetricsAdapter : IPdfFontMetricsProvider
{
    private readonly IOperationFontService _fontService;
    private readonly IPdfFontMetricsProvider _fallback;

    public PdfFontServiceMetricsAdapter(
        IOperationFontService fontService,
        IPdfFontMetricsProvider? fallback = null)
    {
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _fallback = fallback ?? PdfApproximateFontMetrics.Instance;
    }

    /// <summary>The underlying operation-scoped font service.</summary>
    public IOperationFontService FontService => _fontService;

    public bool IsApproximate
    {
        get
        {
            // If the service has no provisioned/scanned font mappings, it is approximate
            return _fontService.MappedFamilies.Count == 0;
        }
    }

    public double GetAdvanceWidth(PdfStandardFont font, char character)
    {
        // Courier is fixed pitch 600 thousandths of an em by definition
        if (PdfStandardFonts.IsFixedPitch(font))
            return 600;

        ResolvedFontFace? face = ResolveStandardFont(font);
        if (face?.TrueType is not null)
        {
            return face.GetAdvancePdfUnits(character);
        }

        return _fallback.GetAdvanceWidth(font, character);
    }

    public double GetAscent(PdfStandardFont font)
    {
        ResolvedFontFace? face = ResolveStandardFont(font);
        if (face is not null && face.DesignAscender > 0)
        {
            return face.GetVerticalMetricsPdfUnits().Ascent;
        }

        return _fallback.GetAscent(font);
    }

    public double GetDescent(PdfStandardFont font)
    {
        ResolvedFontFace? face = ResolveStandardFont(font);
        if (face is not null && face.DesignDescender != 0)
        {
            return face.GetVerticalMetricsPdfUnits().Descent;
        }

        return _fallback.GetDescent(font);
    }

    private ResolvedFontFace? ResolveStandardFont(PdfStandardFont font)
    {
        (string family, bool bold, bool italic) = MapStandardFont(font);
        var (face, outcome) = _fontService.ResolveWithOutcome(family, bold, italic);
        if (outcome is FontResolutionOutcome.ExactMatch or FontResolutionOutcome.StyleMatch or FontResolutionOutcome.DirectoryMatch)
        {
            return face;
        }

        return null;
    }

    private static (string Family, bool Bold, bool Italic) MapStandardFont(PdfStandardFont font) => font switch
    {
        PdfStandardFont.Helvetica => ("Helvetica", false, false),
        PdfStandardFont.HelveticaBold => ("Helvetica", true, false),
        PdfStandardFont.HelveticaOblique => ("Helvetica", false, true),
        PdfStandardFont.HelveticaBoldOblique => ("Helvetica", true, true),
        PdfStandardFont.TimesRoman => ("Times New Roman", false, false),
        PdfStandardFont.TimesBold => ("Times New Roman", true, false),
        PdfStandardFont.TimesItalic => ("Times New Roman", false, true),
        PdfStandardFont.TimesBoldItalic => ("Times New Roman", true, true),
        PdfStandardFont.Courier => ("Courier New", false, false),
        PdfStandardFont.CourierBold => ("Courier New", true, false),
        PdfStandardFont.CourierOblique => ("Courier New", false, true),
        PdfStandardFont.CourierBoldOblique => ("Courier New", true, true),
        PdfStandardFont.Symbol => ("Symbol", false, false),
        PdfStandardFont.ZapfDingbats => ("ZapfDingbats", false, false),
        _ => ("Helvetica", false, false),
    };
}
