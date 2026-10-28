using System;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Model.Fonts;

/// <summary>
/// A resolved, immutable font face capable of providing exact metrics and glyph advances
/// across points, pixels, and PDF em units.
/// </summary>
public sealed class ResolvedFontFace
{
    private readonly TrueTypeFont? _trueType;
    private readonly FontMetadataInfo _metadata;

    public ResolvedFontFace(
        FontFaceIdentity identity,
        FontMetadataInfo metadata,
        TrueTypeFont? trueType = null,
        string? sourcePath = null,
        ReadOnlyMemory<byte> programBytes = default)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _trueType = trueType;
        SourcePath = sourcePath;
        ProgramBytes = programBytes;
    }

    /// <summary>The immutable, unambiguous identity of this face.</summary>
    public FontFaceIdentity Identity { get; }

    /// <summary>The parsed metadata information from the font program.</summary>
    public FontMetadataInfo Metadata => _metadata;

    /// <summary>The underlying TrueType parser instance if loaded, or null.</summary>
    public TrueTypeFont? TrueType => _trueType;

    /// <summary>The path to the font file, if resolved from the filesystem.</summary>
    public string? SourcePath { get; }

    /// <summary>The raw program bytes, if resolved from in-memory data.</summary>
    public ReadOnlyMemory<byte> ProgramBytes { get; }

    /// <summary>Declared typographic family name.</summary>
    public string Family => _metadata.Family;

    /// <summary>Declared subfamily / style name.</summary>
    public string Subfamily => _metadata.Subfamily;

    /// <summary>True when bold.</summary>
    public bool IsBold => Identity.IsBold || _metadata.IsBold;

    /// <summary>True when italic or oblique.</summary>
    public bool IsItalic => Identity.IsItalic || _metadata.IsItalic;

    /// <summary>The font's units per em (typically 1000 or 2048).</summary>
    public int UnitsPerEm => _metadata.UnitsPerEm;

    /// <summary>Typographic ascent in design units.</summary>
    public int DesignAscender => _metadata.TypographicAscender;

    /// <summary>Typographic descent in design units (typically negative).</summary>
    public int DesignDescender => _metadata.TypographicDescender;

    /// <summary>Typographic line gap in design units.</summary>
    public int DesignLineGap => _metadata.TypographicLineGap;

    /// <summary>Returns typographic vertical metrics in points at <paramref name="fontSizePoints"/>.</summary>
    public (double Ascent, double Descent, double LineGap) GetVerticalMetricsPoints(double fontSizePoints)
    {
        double ascent = FontMetricConversions.DesignUnitsToPoints(DesignAscender, fontSizePoints, UnitsPerEm);
        double descent = FontMetricConversions.DesignUnitsToPoints(Math.Abs(DesignDescender), fontSizePoints, UnitsPerEm);
        double lineGap = FontMetricConversions.DesignUnitsToPoints(Math.Max(0, DesignLineGap), fontSizePoints, UnitsPerEm);
        return (ascent, descent, lineGap);
    }

    /// <summary>Returns typographic vertical metrics in CSS pixels at <paramref name="fontSizePoints"/>.</summary>
    public (double Ascent, double Descent, double LineGap) GetVerticalMetricsPixels(double fontSizePoints)
    {
        var (ascent, descent, lineGap) = GetVerticalMetricsPoints(fontSizePoints);
        return (
            FontMetricConversions.PointsToPixels(ascent),
            FontMetricConversions.PointsToPixels(descent),
            FontMetricConversions.PointsToPixels(lineGap));
    }

    /// <summary>Returns vertical metrics normalized to PDF 1/1000 em units.</summary>
    public (double Ascent, double Descent, double LineGap) GetVerticalMetricsPdfUnits()
    {
        double ascent = FontMetricConversions.DesignUnitsToPdfUnits(DesignAscender, UnitsPerEm);
        double descent = FontMetricConversions.DesignUnitsToPdfUnits(Math.Abs(DesignDescender), UnitsPerEm);
        double lineGap = FontMetricConversions.DesignUnitsToPdfUnits(Math.Max(0, DesignLineGap), UnitsPerEm);
        return (ascent, descent, lineGap);
    }

    /// <summary>Returns the advance width of a character in typographic points at <paramref name="fontSizePoints"/>.</summary>
    public double GetAdvancePoints(char character, double fontSizePoints)
    {
        int advanceFUnits = GetAdvanceFUnits(character);
        return FontMetricConversions.DesignUnitsToPoints(advanceFUnits, fontSizePoints, UnitsPerEm);
    }

    /// <summary>Returns the total advance width of a string in typographic points at <paramref name="fontSizePoints"/>.</summary>
    public double GetAdvancePoints(string text, double fontSizePoints)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        double total = 0;
        for (int i = 0; i < text.Length;)
        {
            int codepoint = char.ConvertToUtf32(text, i);
            i += char.IsSurrogatePair(text, i) ? 2 : 1;
            int advanceFUnits = GetAdvanceFUnits(codepoint);
            total += FontMetricConversions.DesignUnitsToPoints(advanceFUnits, fontSizePoints, UnitsPerEm);
        }

        return total;
    }

    /// <summary>Returns the advance width of a character in CSS pixels at <paramref name="fontSizePoints"/>.</summary>
    public double GetAdvancePixels(char character, double fontSizePoints) =>
        FontMetricConversions.PointsToPixels(GetAdvancePoints(character, fontSizePoints));

    /// <summary>Returns the total advance width of a string in CSS pixels at <paramref name="fontSizePoints"/>.</summary>
    public double GetAdvancePixels(string text, double fontSizePoints) =>
        FontMetricConversions.PointsToPixels(GetAdvancePoints(text, fontSizePoints));

    /// <summary>Returns the advance width of a character in PDF 1/1000 em units.</summary>
    public double GetAdvancePdfUnits(char character)
    {
        int advanceFUnits = GetAdvanceFUnits(character);
        return FontMetricConversions.DesignUnitsToPdfUnits(advanceFUnits, UnitsPerEm);
    }

    /// <summary>Returns the underline offset from the baseline in points (negative below baseline).</summary>
    public double GetUnderlineOffsetPoints(double fontSizePoints) =>
        FontMetricConversions.DesignUnitsToPoints(_metadata.UnderlinePosition, fontSizePoints, UnitsPerEm);

    /// <summary>Returns the underline offset from baseline in CSS pixels.</summary>
    public double GetUnderlineOffsetPixels(double fontSizePoints) =>
        FontMetricConversions.PointsToPixels(GetUnderlineOffsetPoints(fontSizePoints));

    /// <summary>Returns the underline thickness in points.</summary>
    public double GetUnderlineThicknessPoints(double fontSizePoints) =>
        Math.Max(0.5, FontMetricConversions.DesignUnitsToPoints(_metadata.UnderlineThickness, fontSizePoints, UnitsPerEm));

    /// <summary>Returns the underline thickness in CSS pixels.</summary>
    public double GetUnderlineThicknessPixels(double fontSizePoints) =>
        FontMetricConversions.PointsToPixels(GetUnderlineThicknessPoints(fontSizePoints));

    private int GetAdvanceFUnits(char character) => GetAdvanceFUnits((int)character);

    private int GetAdvanceFUnits(int codepoint)
    {
        if (_trueType is not null)
        {
            int glyphIndex = _trueType.GetGlyphIndex(codepoint);
            if (glyphIndex > 0 || codepoint == ' ' || codepoint == '\t')
            {
                return _trueType.GetAdvanceWidth(glyphIndex);
            }

            // Non-breaking space fallback to standard space advance when explicit glyph is absent
            if (glyphIndex == 0 && (codepoint == 0x00A0 || codepoint == 0x202F || codepoint == 0x2007))
            {
                int spaceGlyph = _trueType.GetGlyphIndex(' ');
                if (spaceGlyph > 0)
                    return _trueType.GetAdvanceWidth(spaceGlyph);
            }
        }

        // Proportional Latin fallback estimate scaled to UnitsPerEm when font table is missing hmtx
        double emFraction = codepoint switch
        {
            ' ' or '\u00A0' or '\t' => 0.278,
            'i' or 'j' or 'l' or '.' or ',' or ';' or ':' or '!' or '|' or '\'' or '`' or 'I' => 0.250,
            'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '-' => 0.320,
            '"' or '*' => 0.380,
            >= '0' and <= '9' => 0.556,
            'm' => 0.833,
            'w' => 0.722,
            'M' or 'W' or '@' or '—' => 0.889,
            'A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G' or 'H' or 'J' or 'K' or 'L'
                or 'N' or 'O' or 'P' or 'Q' or 'R' or 'S' or 'T' or 'U' or 'V' or 'X' or 'Y' or 'Z' => 0.690,
            _ => 0.556,
        };

        if (IsBold) emFraction *= 1.05;
        return (int)(emFraction * UnitsPerEm);
    }
}
