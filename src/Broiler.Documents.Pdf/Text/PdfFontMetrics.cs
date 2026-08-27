using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Text;

/// <summary>The fourteen font names every conforming PDF reader provides.</summary>
public enum PdfStandardFont
{
    Helvetica,
    HelveticaBold,
    HelveticaOblique,
    HelveticaBoldOblique,
    TimesRoman,
    TimesBold,
    TimesItalic,
    TimesBoldItalic,
    Courier,
    CourierBold,
    CourierOblique,
    CourierBoldOblique,
    Symbol,
    ZapfDingbats,
}

/// <summary>Maps between <see cref="PdfStandardFont"/> values and their PDF base-font names.</summary>
public static class PdfStandardFonts
{
    private static readonly Dictionary<PdfStandardFont, string> Names = new()
    {
        [PdfStandardFont.Helvetica] = "Helvetica",
        [PdfStandardFont.HelveticaBold] = "Helvetica-Bold",
        [PdfStandardFont.HelveticaOblique] = "Helvetica-Oblique",
        [PdfStandardFont.HelveticaBoldOblique] = "Helvetica-BoldOblique",
        [PdfStandardFont.TimesRoman] = "Times-Roman",
        [PdfStandardFont.TimesBold] = "Times-Bold",
        [PdfStandardFont.TimesItalic] = "Times-Italic",
        [PdfStandardFont.TimesBoldItalic] = "Times-BoldItalic",
        [PdfStandardFont.Courier] = "Courier",
        [PdfStandardFont.CourierBold] = "Courier-Bold",
        [PdfStandardFont.CourierOblique] = "Courier-Oblique",
        [PdfStandardFont.CourierBoldOblique] = "Courier-BoldOblique",
        [PdfStandardFont.Symbol] = "Symbol",
        [PdfStandardFont.ZapfDingbats] = "ZapfDingbats",
    };

    private static readonly Dictionary<string, PdfStandardFont> ByName = BuildReverseIndex();

    /// <summary>The PDF <c>/BaseFont</c> name for a standard font.</summary>
    public static string NameOf(PdfStandardFont font) =>
        Names.TryGetValue(font, out string? name) ? name : Names[PdfStandardFont.Helvetica];

    /// <summary>
    /// Recognizes a <c>/BaseFont</c> value as one of the fourteen. This is an
    /// exact-name lookup over the format's own naming, not a substring guess at
    /// weight or slant.
    /// </summary>
    public static bool TryParse(string? baseFont, out PdfStandardFont font)
    {
        font = PdfStandardFont.Helvetica;
        if (string.IsNullOrEmpty(baseFont))
            return false;
        return ByName.TryGetValue(baseFont, out font);
    }

    /// <summary>Chooses the standard font for a logical family and style.</summary>
    public static PdfStandardFont Select(PdfFontFamilyKind family, bool bold, bool italic) => family switch
    {
        PdfFontFamilyKind.Serif => (bold, italic) switch
        {
            (true, true) => PdfStandardFont.TimesBoldItalic,
            (true, false) => PdfStandardFont.TimesBold,
            (false, true) => PdfStandardFont.TimesItalic,
            _ => PdfStandardFont.TimesRoman,
        },
        PdfFontFamilyKind.Monospace => (bold, italic) switch
        {
            (true, true) => PdfStandardFont.CourierBoldOblique,
            (true, false) => PdfStandardFont.CourierBold,
            (false, true) => PdfStandardFont.CourierOblique,
            _ => PdfStandardFont.Courier,
        },
        _ => (bold, italic) switch
        {
            (true, true) => PdfStandardFont.HelveticaBoldOblique,
            (true, false) => PdfStandardFont.HelveticaBold,
            (false, true) => PdfStandardFont.HelveticaOblique,
            _ => PdfStandardFont.Helvetica,
        },
    };

    private static Dictionary<string, PdfStandardFont> BuildReverseIndex()
    {
        var index = new Dictionary<string, PdfStandardFont>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<PdfStandardFont, string> entry in Names)
            index[entry.Value] = entry.Key;

        // The aliases every reader is expected to accept for the same faces.
        index["Arial"] = PdfStandardFont.Helvetica;
        index["Arial-Bold"] = PdfStandardFont.HelveticaBold;
        index["Arial,Bold"] = PdfStandardFont.HelveticaBold;
        index["TimesNewRoman"] = PdfStandardFont.TimesRoman;
        index["Times"] = PdfStandardFont.TimesRoman;
        index["CourierNew"] = PdfStandardFont.Courier;
        return index;
    }
}

/// <summary>The three logical families the writer maps model font families onto.</summary>
public enum PdfFontFamilyKind
{
    SansSerif,
    Serif,
    Monospace,
}

/// <summary>
/// Supplies glyph advance widths for the writer's line breaking and for reader
/// word-gap estimation when a font declares no <c>/Widths</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the codec's font-metrics extension point. The base package ships
/// <see cref="PdfApproximateFontMetrics"/>, which is authored from character
/// proportions rather than from any font vendor's metric file, so the codec
/// carries no third-party font data. A caller that has cleared a real metric set
/// — or that wants metrics measured from an embedded font program — composes its
/// own provider instead, and every consumer picks the change up unchanged.
/// </para>
/// <para>
/// Widths are in thousandths of the em, which is the unit PDF itself uses.
/// </para>
/// </remarks>
public interface IPdfFontMetricsProvider
{
    /// <summary>
    /// True when the widths are estimates rather than a font's real metrics. The
    /// writer reports this once per document so output is never presented as
    /// metrically exact when it is not.
    /// </summary>
    bool IsApproximate { get; }

    /// <summary>The advance width of <paramref name="character"/>, in units of 1/1000 em.</summary>
    double GetAdvanceWidth(PdfStandardFont font, char character);

    /// <summary>The typographic ascent, in units of 1/1000 em.</summary>
    double GetAscent(PdfStandardFont font);

    /// <summary>The typographic descent as a positive magnitude, in units of 1/1000 em.</summary>
    double GetDescent(PdfStandardFont font);
}

/// <summary>
/// The built-in approximate metric model: a small table of proportion classes,
/// scaled per family.
/// </summary>
/// <remarks>
/// <para>
/// The numbers are Broiler-authored from the relative proportions of Latin
/// letterforms, deliberately erring slightly wide so a line the writer measures
/// as fitting also fits when a reader lays it out with the real font. They are
/// not any vendor's metrics and must not be described as such.
/// </para>
/// <para>
/// Because the model is deterministic and platform-independent, pagination is
/// identical on every host — which is the property the writer actually depends
/// on. Visual fidelity in a viewer is approximate, and the writer says so with
/// <see cref="PdfDiagnosticCodes.WriteMetricsApproximate"/>.
/// </para>
/// </remarks>
public sealed class PdfApproximateFontMetrics : IPdfFontMetricsProvider
{
    /// <summary>The shared instance; the model has no state.</summary>
    public static PdfApproximateFontMetrics Instance { get; } = new();

    public bool IsApproximate => true;

    public double GetAdvanceWidth(PdfStandardFont font, char character)
    {
        // Courier is monospaced by definition, so its width needs no estimate.
        if (IsMonospace(font))
            return 600;

        double width = ClassWidth(character);
        double familyScale = IsSerif(font) ? 0.94 : 1.0;
        double weightScale = IsBold(font) ? 1.04 : 1.0;
        return width * familyScale * weightScale;
    }

    public double GetAscent(PdfStandardFont font) => IsSerif(font) ? 683 : 718;

    public double GetDescent(PdfStandardFont font) => IsSerif(font) ? 217 : 207;

    private static bool IsMonospace(PdfStandardFont font) =>
        font is PdfStandardFont.Courier or PdfStandardFont.CourierBold
            or PdfStandardFont.CourierOblique or PdfStandardFont.CourierBoldOblique;

    private static bool IsSerif(PdfStandardFont font) =>
        font is PdfStandardFont.TimesRoman or PdfStandardFont.TimesBold
            or PdfStandardFont.TimesItalic or PdfStandardFont.TimesBoldItalic;

    private static bool IsBold(PdfStandardFont font) =>
        font is PdfStandardFont.HelveticaBold or PdfStandardFont.HelveticaBoldOblique
            or PdfStandardFont.TimesBold or PdfStandardFont.TimesBoldItalic
            or PdfStandardFont.CourierBold or PdfStandardFont.CourierBoldOblique;

    // The proportion classes. Anything outside them takes the lowercase default,
    // which is the commonest width in running Latin text.
    private static double ClassWidth(char character) => character switch
    {
        ' ' or '\u00A0' => 278,
        '\t' => 278,
        'i' or 'j' or 'l' or '.' or ',' or ';' or ':' or '!' or '|' or '\'' or '`' or 'I' => 250,
        'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '-' => 320,
        '"' or '*' => 380,
        >= '0' and <= '9' => 556,
        'm' => 833,
        'w' => 722,
        'M' or 'W' or '@' or '—' => 889,
        'A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G' or 'H' or 'J' or 'K' or 'L'
            or 'N' or 'O' or 'P' or 'Q' or 'R' or 'S' or 'T' or 'U' or 'V' or 'X' or 'Y' or 'Z' => 690,
        >= 'a' and <= 'z' => 556,
        _ => 556,
    };
}
