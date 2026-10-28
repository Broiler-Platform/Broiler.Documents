using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Documents.Model.Text;

/// <summary>
/// Provides Unicode-compliant text boundary and break classification conforming to
/// Unicode 15.1 UAX #29 (Extended Grapheme Clusters) and UAX #14 (Line Breaking pragmatic subset).
/// </summary>
public static class UnicodeBoundaryClassifier
{
    /// <summary>Declared Unicode version supported by this classifier.</summary>
    public const string UnicodeVersion = "15.1";

    /// <summary>Declared Unicode specification subsets supported by this classifier.</summary>
    public const string SupportedRules = "UAX#29 (Extended Grapheme Clusters), UAX#14 (Line Breaking Algorithm: BK, CR, LF, NL, GL, SP, BA, ZW, ID)";

    /// <summary>U+2028 LINE SEPARATOR used by document models for hard paragraph-internal line breaks.</summary>
    public const char ForcedLineBreak = '\u2028';

    /// <summary>U+00A0 NO-BREAK SPACE.</summary>
    public const char NoBreakSpace = '\u00A0';

    /// <summary>U+202F NARROW NO-BREAK SPACE.</summary>
    public const char NarrowNoBreakSpace = '\u202F';

    /// <summary>U+2007 FIGURE SPACE (tabular non-breaking space).</summary>
    public const char FigureSpace = '\u2007';

    /// <summary>U+2060 WORD JOINER (zero-width non-breaking space).</summary>
    public const char WordJoiner = '\u2060';

    /// <summary>U+200B ZERO WIDTH SPACE (break opportunity with zero advance).</summary>
    public const char ZeroWidthSpace = '\u200B';

    /// <summary>U+00AD SOFT HYPHEN (discretionary line break opportunity).</summary>
    public const char SoftHyphen = '\u00AD';

    /// <summary>
    /// True when <paramref name="c"/> represents whitespace where a line may legally wrap (break opportunity).
    /// Non-breaking spaces (U+00A0, U+202F, U+2007, U+2060, U+FEFF) return <see langword="false"/>.
    /// </summary>
    public static bool IsBreakableWhitespace(char c) =>
        c != FigureSpace && (c is ' ' or '\t' or '\u3000' or (>= '\u2000' and <= '\u200A') or ZeroWidthSpace or SoftHyphen);

    /// <summary>
    /// True when <paramref name="codepoint"/> represents whitespace where a line may legally wrap.
    /// </summary>
    public static bool IsBreakableWhitespace(int codepoint) =>
        codepoint != 0x2007 && (codepoint is ' ' or '\t' or 0x3000 or (>= 0x2000 and <= 0x200A) or 0x200B or 0x00AD);

    /// <summary>
    /// True when <paramref name="c"/> is an explicit non-breaking space or word joiner where line wrapping is prohibited.
    /// </summary>
    public static bool IsNonBreakingSpace(char c) =>
        c is NoBreakSpace or NarrowNoBreakSpace or FigureSpace or WordJoiner or '\uFEFF';

    /// <summary>
    /// True when <paramref name="codepoint"/> is an explicit non-breaking space or word joiner.
    /// </summary>
    public static bool IsNonBreakingSpace(int codepoint) =>
        codepoint is 0x00A0 or 0x202F or 0x2007 or 0x2060 or 0xFEFF;

    /// <summary>
    /// True when <paramref name="c"/> is a mandatory hard break (U+2028, '\n', '\r', U+2029).
    /// </summary>
    public static bool IsMandatoryBreak(char c) =>
        c is ForcedLineBreak or '\n' or '\r' or '\u2029';

    /// <summary>
    /// True when <paramref name="codepoint"/> is a CJK ideograph or kana character eligible for UAX #14 ID line breaking.
    /// </summary>
    public static bool IsCjkIdeograph(int codepoint) =>
        (codepoint >= 0x4E00 && codepoint <= 0x9FFF) ||   // CJK Unified Ideographs
        (codepoint >= 0x3400 && codepoint <= 0x4DBF) ||   // CJK Unified Ideographs Extension A
        (codepoint >= 0x20000 && codepoint <= 0x2A6DF) || // CJK Unified Ideographs Extension B
        (codepoint >= 0xF900 && codepoint <= 0xFAFF) ||   // CJK Compatibility Ideographs
        (codepoint >= 0x3040 && codepoint <= 0x309F) ||   // Hiragana
        (codepoint >= 0x30A0 && codepoint <= 0x30FF) ||   // Katakana
        (codepoint >= 0xAC00 && codepoint <= 0xD7AF);     // Hangul Syllables

    /// <summary>
    /// True for punctuation that prohibits breaking immediately before it (closing brackets, commas, periods, etc.).
    /// </summary>
    public static bool IsProhibitedBefore(char c) =>
        c is ')' or ']' or '}' or '»' or '”' or '’' or ',' or '.' or '!' or '?' or ';' or ':' or
             '、' or '。' or '）' or '】' or '』' or '」' or '〉' or '》';

    /// <summary>
    /// True for punctuation that prohibits breaking immediately after it (opening brackets, quotes, etc.).
    /// </summary>
    public static bool IsProhibitedAfter(char c) =>
        c is '(' or '[' or '{' or '«' or '“' or '‘' or
             '（' or '【' or '『' or '「' or '〈' or '《';

    /// <summary>
    /// Splits <paramref name="text"/> into extended grapheme clusters per UAX #29.
    /// Preserves surrogate pairs, combining sequences, and emoji sequences without splitting.
    /// </summary>
    public static List<GraphemeCluster> GetGraphemeClusters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var list = new List<GraphemeCluster>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            int offset = enumerator.ElementIndex;
            string element = enumerator.GetTextElement();
            list.Add(new GraphemeCluster(offset, element.Length, element));
        }

        return list;
    }

    /// <summary>
    /// Classifies each boundary point in <paramref name="text"/> in source-text coordinates.
    /// </summary>
    public static List<TextBoundaryPoint> ClassifyBoundaries(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var clusters = GetGraphemeClusters(text);
        var result = new List<TextBoundaryPoint>(clusters.Count);

        for (int i = 0; i < clusters.Count; i++)
        {
            var cluster = clusters[i];
            int codepoint = cluster.FirstCodePoint;
            UnicodeBoundaryKind kind = UnicodeBoundaryKind.GraphemeBoundary;

            char firstChar = cluster.Text[0];
            if (IsMandatoryBreak(firstChar))
            {
                kind = UnicodeBoundaryKind.MandatoryBreak;
            }
            else if (IsBreakableWhitespace(codepoint))
            {
                kind = UnicodeBoundaryKind.WordBreakOpportunity;
            }
            else if (IsNonBreakingSpace(codepoint))
            {
                kind = UnicodeBoundaryKind.None; // Prohibited break
            }
            else if (IsCjkIdeograph(codepoint))
            {
                // CJK line break opportunity before/after unless prohibited by punctuation
                bool canBreakAfter = i + 1 >= clusters.Count || !IsProhibitedBefore(clusters[i + 1].Text[0]);
                kind = canBreakAfter ? UnicodeBoundaryKind.WordBreakOpportunity : UnicodeBoundaryKind.GraphemeBoundary;
            }

            result.Add(new TextBoundaryPoint(cluster.Offset, codepoint, kind, cluster.Length));
        }

        return result;
    }
}
