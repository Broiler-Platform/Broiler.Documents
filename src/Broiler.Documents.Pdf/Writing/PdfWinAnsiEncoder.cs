using System;
using System.Collections.Generic;
using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf.Writing;

/// <summary>
/// Encodes text into the WinAnsi byte values the writer's simple fonts use.
/// </summary>
/// <remarks>
/// <para>
/// The table is the inverse of the reader's <c>WinAnsiEncoding</c> table, so the
/// two directions cannot drift apart. WinAnsi covers the Latin repertoire the
/// non-embedded standard fonts can actually show; anything outside it is the
/// writer's boundary, not a bug to paper over.
/// </para>
/// <para>
/// Extending past this boundary means embedding a composite font with a
/// <c>ToUnicode</c> map, which needs a font resource whose embedding rights the
/// caller has established. That is a separate, reviewed step (IP-012), and until
/// it is composed the writer substitutes and reports rather than emitting a glyph
/// the reader has no font for.
/// </para>
/// </remarks>
internal static class PdfWinAnsiEncoder
{
    private static readonly Dictionary<char, byte> Reverse = BuildReverse();

    public static bool CanEncode(char c) => Reverse.ContainsKey(c);

    /// <summary>
    /// Encodes a string, substituting <c>?</c> for anything unrepresentable. The
    /// caller has already reported the substitution during layout.
    /// </summary>
    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = Reverse.TryGetValue(text[i], out byte value) ? value : (byte)'?';
        return bytes;
    }

    private static Dictionary<char, byte> BuildReverse()
    {
        var reverse = new Dictionary<char, byte>();
        char[] table = PdfEncodings.WinAnsiEncoding;

        for (int code = 32; code < table.Length; code++)
        {
            char c = table[code];
            if (c == '\0')
                continue;

            // Lower codes win a duplicate: 0xA0 and 0xAD repeat space and hyphen,
            // and the plain ASCII form is the one to emit.
            reverse.TryAdd(c, (byte)code);
        }

        // The characters a document commonly carries that WinAnsi has no slot for,
        // mapped to their closest representable form rather than to '?'.
        reverse.TryAdd('‑', (byte)'-');   // non-breaking hyphen
        reverse.TryAdd('−', (byte)'-');   // minus sign
        reverse.TryAdd(' ', (byte)' ');   // no-break space
        reverse.TryAdd(' ', (byte)' ');   // figure space
        reverse.TryAdd(' ', (byte)' ');   // narrow no-break space
        reverse.TryAdd('\t', (byte)' ');
        return reverse;
    }
}
