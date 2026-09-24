using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf.Security;

/// <summary>
/// Turns a password a caller typed into the bytes a security handler hashes.
/// </summary>
/// <remarks>
/// <para>
/// The format fixes the encoding, but producers have not always followed it, and
/// a reader that tried only the one encoding would refuse a correct password.
/// Each method therefore returns the specified form first and then the forms
/// producers are known to have used instead, without repeats. Every candidate is
/// checked against the document's own verification value, so a wrong form can
/// only fail to open a document; it cannot open one wrongly.
/// </para>
/// </remarks>
internal static class PdfPasswordEncoding
{
    /// <summary>The most bytes a revision 6 password contributes (ISO 32000-2 §7.6.4.3.3).</summary>
    private const int Revision6MaxBytes = 127;

    /// <summary>
    /// The byte forms of a password for revisions 2 to 4, which pad or truncate
    /// it to 32 bytes: PDFDocEncoding, as ISO 32000-1 §7.6.3.3 has it; then
    /// Windows-1252, which is what LibreOffice writes; then Latin-1 and UTF-8.
    /// </summary>
    /// <remarks>
    /// Windows-1252 is reached through the WinAnsi table this codec already
    /// authors for fonts (IP-021) - the two are the same assignment of the upper
    /// half - so no code page is loaded and no new character data is added.
    /// </remarks>
    public static IReadOnlyList<byte[]> LegacyCandidates(string password)
    {
        var candidates = new List<byte[]>(4);
        if (TryPdfDocEncoding(password, out byte[] pdfDoc))
            Add(candidates, pdfDoc);
        if (TryWinAnsi(password, out byte[] winAnsi))
            Add(candidates, winAnsi);
        if (TryLatin1(password, out byte[] latin1))
            Add(candidates, latin1);
        Add(candidates, Encoding.UTF8.GetBytes(password));
        return candidates;
    }

    /// <summary>
    /// The byte forms of a password for revision 6: prepared with SASLprep and
    /// encoded as UTF-8, as ISO 32000-2 has it, then the same without the
    /// preparation. Each is truncated to 127 bytes.
    /// </summary>
    public static IReadOnlyList<byte[]> Revision6Candidates(string password)
    {
        var candidates = new List<byte[]>(2);
        Add(candidates, Truncate(Encoding.UTF8.GetBytes(SaslPrepare(password)), Revision6MaxBytes));
        Add(candidates, Truncate(Encoding.UTF8.GetBytes(password), Revision6MaxBytes));
        return candidates;
    }

    /// <summary>
    /// The mapping and normalization steps of SASLprep (RFC 4013, a profile of
    /// stringprep, RFC 3454).
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a reader needs of the profile is what changes the bytes: characters
    /// commonly mapped to nothing are removed, non-ASCII spaces become U+0020,
    /// and the result is normalized to form KC. The prohibition and bidirectional
    /// checks only ever refuse a string, and a reader has nothing to refuse: a
    /// producer that applied them never protected a document with such a
    /// password, and one that did not is served by the unprepared candidate.
    /// </para>
    /// <para>
    /// The non-ASCII spaces are authored from character identity rather than
    /// transcribed: the Unicode space separators other than U+0020. That is the
    /// set RFC 3454 table C.1.2 lists, but for U+200B, which later versions of
    /// Unicode stopped calling a space and which table B.1 removes anyway.
    /// </para>
    /// </remarks>
    public static string SaslPrepare(string password)
    {
        var mapped = new StringBuilder(password.Length);
        foreach (char c in password)
        {
            if (IsMappedToNothing(c))
                continue;

            mapped.Append(c != ' ' && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator ? ' ' : c);
        }

        try
        {
            return mapped.ToString().Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // An unpaired surrogate cannot be normalized. The unprepared form is
            // still tried, which is all a reader can do with such a password.
            return mapped.ToString();
        }
    }

    /*
     * The characters RFC 3454 table B.1 maps to nothing, transcribed as the
     * notice below permits (SRC-026). They are code points a keyboard produces
     * without the typist seeing them - a soft hyphen, zero-width joiners, the
     * variation selector that follows an emoji - and removing them is what makes
     * a password typed on one device open a document protected on another.
     *
     * Copyright (C) The Internet Society (2002).  All Rights Reserved.
     *
     * This document and translations of it may be copied and furnished to
     * others, and derivative works that comment on or otherwise explain it
     * or assist in its implementation may be prepared, copied, published
     * and distributed, in whole or in part, without restriction of any
     * kind, provided that the above copyright notice and this paragraph are
     * included on all such copies and derivative works.  However, this
     * document itself may not be modified in any way, such as by removing
     * the copyright notice or references to the Internet Society or other
     * Internet organizations, except as needed for the purpose of
     * developing Internet standards in which case the procedures for
     * copyrights defined in the Internet Standards process must be
     * followed, or as required to translate it into languages other than
     * English.
     */
    private static bool IsMappedToNothing(char c) => c switch
    {
        '\u00AD' => true,                   // soft hyphen
        '\u034F' => true,                   // combining grapheme joiner
        '\u1806' => true,                   // Mongolian todo soft hyphen
        >= '\u180B' and <= '\u180D' => true, // Mongolian free variation selectors
        >= '\u200B' and <= '\u200D' => true, // zero width space, non-joiner, joiner
        '\u2060' => true,                   // word joiner
        >= '\uFE00' and <= '\uFE0F' => true, // variation selectors 1-16
        '\uFEFF' => true,                   // zero width no-break space
        _ => false,
    };

    private static bool TryPdfDocEncoding(string password, out byte[] bytes)
    {
        bytes = new byte[password.Length];
        for (int i = 0; i < password.Length; i++)
        {
            if (!PdfDocEncoding.TryFromChar(password[i], out bytes[i]))
                return false;
        }

        return true;
    }

    private static bool TryWinAnsi(string password, out byte[] bytes)
    {
        char[] table = PdfEncodings.WinAnsiEncoding;
        bytes = new byte[password.Length];
        for (int i = 0; i < password.Length; i++)
        {
            int code = password[i] == '\0' ? 0 : Array.IndexOf(table, password[i], 1);
            if (code < 0)
                return false;
            bytes[i] = (byte)code;
        }

        return true;
    }

    private static bool TryLatin1(string password, out byte[] bytes)
    {
        bytes = new byte[password.Length];
        for (int i = 0; i < password.Length; i++)
        {
            if (password[i] > '\u00FF')
                return false;
            bytes[i] = (byte)password[i];
        }

        return true;
    }

    private static byte[] Truncate(byte[] bytes, int max) => bytes.Length <= max ? bytes : bytes[..max];

    private static void Add(List<byte[]> candidates, byte[] candidate)
    {
        foreach (byte[] existing in candidates)
        {
            if (existing.AsSpan().SequenceEqual(candidate))
                return;
        }

        candidates.Add(candidate);
    }
}
