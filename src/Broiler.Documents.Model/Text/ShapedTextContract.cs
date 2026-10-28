using System;
using System.Collections.Generic;
using Broiler.Documents.Model.Fonts;

namespace Broiler.Documents.Model.Text;

/// <summary>
/// Text flow direction of a shaped cluster or run.
/// </summary>
public enum TextDirection
{
    LeftToRight = 0,
    RightToLeft = 1,
}

/// <summary>
/// Information about a single shaped glyph in a cluster.
/// </summary>
public readonly record struct ShapedGlyphInfo(
    int GlyphIndex,
    double Advance,
    double XOffset,
    double YOffset);

/// <summary>
/// Contract connecting a sequence of shaped glyphs to their originating source-text cluster,
/// direction, and resolved font face identity.
/// </summary>
public sealed class ShapedCluster
{
    public int SourceOffset { get; }
    public int SourceLength { get; }
    public IReadOnlyList<ShapedGlyphInfo> Glyphs { get; }
    public double Advance { get; }
    public TextDirection Direction { get; }
    public FontFaceIdentity Face { get; }

    public ShapedCluster(
        int sourceOffset,
        int sourceLength,
        IReadOnlyList<ShapedGlyphInfo> Glyphs,
        double advance,
        TextDirection direction,
        FontFaceIdentity face)
    {
        SourceOffset = sourceOffset;
        SourceLength = sourceLength;
        this.Glyphs = Glyphs;
        Advance = advance;
        Direction = direction;
        Face = face;
    }
}

/// <summary>
/// Declares the supported vs unsupported script catalog for text shaping and bidi layout.
/// A contract alone does not establish complex shaping or bidi conformance for undeclared scripts.
/// </summary>
public static class ScriptSupportStatus
{
    /// <summary>
    /// Explicitly supported scripts:
    /// - Latin, Cyrillic, Greek, Armenian, Georgian (simple LTR without contextual joining).
    /// - Common CJK (Han ideographs, Hiragana, Katakana, Hangul syllables with UAX #14 line breaking).
    /// - Arabic and Persian (cursive contextual joining init/medi/fina/isol, standard ligatures, and bidi reordering via ComplexTextShaper).
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedScripts =
    [
        "Latin",
        "Cyrillic",
        "Greek",
        "Armenian",
        "Georgian",
        "CJK Ideographs & Kana",
        "Hangul",
        "Arabic (basic contextual forms & ligatures)",
        "Persian (basic contextual forms & ligatures)",
        "Hebrew (bidi reordering without cantillation)"
    ];

    /// <summary>
    /// Declared unsupported scripts (requiring complex syllable reordering, GPOS mark positioning, or dictionary breaking):
    /// - Indic scripts (Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada, Malayalam, Sinhala).
    /// - Southeast Asian scripts (Khmer, Myanmar, Thai, Lao).
    /// - Tibetan, Mongolian vertical.
    /// </summary>
    public static readonly IReadOnlyList<string> UnsupportedScripts =
    [
        "Devanagari",
        "Bengali",
        "Gurmukhi",
        "Gujarati",
        "Oriya",
        "Tamil",
        "Telugu",
        "Kannada",
        "Malayalam",
        "Sinhala",
        "Khmer",
        "Myanmar",
        "Thai (dictionary line breaking)",
        "Lao (dictionary line breaking)",
        "Tibetan",
        "Mongolian"
    ];

    /// <summary>
    /// Tests whether a Unicode scalar value belongs to a declared unsupported complex script.
    /// </summary>
    public static bool IsUnsupportedScript(int codepoint) =>
        (codepoint >= 0x0900 && codepoint <= 0x0DFF) || // Indic blocks: Devanagari through Malayalam
        (codepoint >= 0x0E00 && codepoint <= 0x0E7F) || // Thai
        (codepoint >= 0x0E80 && codepoint <= 0x0EFF) || // Lao
        (codepoint >= 0x0F00 && codepoint <= 0x0FFF) || // Tibetan
        (codepoint >= 0x1000 && codepoint <= 0x109F) || // Myanmar
        (codepoint >= 0x1780 && codepoint <= 0x17FF) || // Khmer
        (codepoint >= 0x1800 && codepoint <= 0x18AF);   // Mongolian

    /// <summary>
    /// Checks whether a string contains characters from an unsupported complex script.
    /// </summary>
    public static bool ContainsUnsupportedScript(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        for (int i = 0; i < text.Length;)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (IsUnsupportedScript(cp))
                return true;
            i += char.IsSurrogatePair(text, i) ? 2 : 1;
        }

        return false;
    }
}
