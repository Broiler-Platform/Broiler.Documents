using System;
using System.Text;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Reads the Latin ligature characters as the letters they join.
/// </summary>
/// <remarks>
/// <para>
/// A ligature is how a page drew two or three letters, not what it says. A font
/// that draws "fi" as one glyph maps that glyph - correctly - to U+FB01, through
/// its <c>ToUnicode</c> map, its glyph name, or its own character map, and the
/// character arrives in the text as one letter no word contains: "ﬁnden" is not
/// "finden" to a search, a spelling checker, or a hyphenation dictionary, and a
/// document read in order to be edited meets all three. Unicode keeps these
/// presentation forms only for compatibility with encodings that had them, and
/// its own decomposition of each is the letters it joins.
/// </para>
/// <para>
/// Exactly the seven Latin ligatures, U+FB00 to U+FB06, and each to that one-step
/// compatibility decomposition, so "ﬅ" keeps its long s. Nothing else is
/// normalized: an accent composed or decomposed, a letter such as "æ" that is a
/// letter and not a ligature, and every other compatibility character arrive as
/// the document mapped them. Marked-content <c>ActualText</c> is not passed
/// through here either; it is the author's own statement of the text, and is
/// taken as it stands.
/// </para>
/// </remarks>
internal static class PdfLigatures
{
    private const char First = 'ﬀ';
    private const char Last = 'ﬆ';

    /// <summary><paramref name="text"/> with each Latin ligature character spelled out.</summary>
    public static string Expand(string text)
    {
        if (text.AsSpan().IndexOfAnyInRange(First, Last) < 0)
            return text;

        var builder = new StringBuilder(text.Length + 2);
        foreach (char character in text)
        {
            if (character is >= First and <= Last)
                builder.Append(LettersOf(character));
            else
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static string LettersOf(char ligature) => ligature switch
    {
        'ﬀ' => "ff",
        'ﬁ' => "fi",
        'ﬂ' => "fl",
        'ﬃ' => "ffi",
        'ﬄ' => "ffl",
        'ﬅ' => "ſt",
        _ => "st",
    };
}
