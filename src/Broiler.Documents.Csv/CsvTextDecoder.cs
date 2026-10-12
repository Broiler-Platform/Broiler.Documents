using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Text;

namespace Broiler.Documents.Csv;

/// <summary>
/// Turns CSV bytes into text. CSV declares no encoding of its own, so the bytes
/// are asked in a fixed order: a byte-order mark if there is one, then whether
/// they are valid UTF-8, and failing both, Windows-1252.
/// </summary>
/// <remarks>
/// <para>
/// RFC 4180 leaves the character set to a media-type parameter that a file on
/// disk does not have. What turns up in practice is UTF-8, with or without a
/// byte-order mark, and the legacy code page a spreadsheet on Windows writes by
/// default. UTF-8 is tried strictly first because the converse is not safe:
/// every byte sequence is valid Windows-1252, so only a UTF-8 decode can fail
/// and tell the two apart.
/// </para>
/// <para>
/// The fallback is Windows-1252 rather than Latin-1 because the two differ in
/// exactly the characters such files are full of - the euro sign and curly
/// quotes - and it is reported, because it is a guess.
/// </para>
/// </remarks>
internal static class CsvTextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <param name="bytes">The input, possibly cut short at the byte limit.</param>
    /// <param name="truncated">True when the input was cut short, so its last character may be incomplete.</param>
    /// <param name="diagnostics">Where an encoding guess or a repair is reported.</param>
    public static string Decode(ReadOnlySpan<byte> bytes, bool truncated, List<DocumentDiagnostic> diagnostics)
    {
        if (TryDecodeMarked(bytes, diagnostics, out string? marked))
            return marked;

        // A cut at the byte limit can split a multi-byte character, and the
        // broken tail would fail the strict decode and drag the whole file into
        // the fallback. The limit is already reported; the half character goes.
        if (truncated)
            bytes = WithoutIncompleteUtf8Tail(bytes);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                CsvDiagnosticCodes.EncodingFallback,
                "The input is not UTF-8 and has no byte-order mark, so it was read as Windows-1252."));
            return Windows1252.Decode(bytes);
        }
    }

    /// <summary>The byte-order mark at the start of <paramref name="bytes"/>, if any, as an encoding and its length.</summary>
    internal static (Encoding Encoding, int Length)? ByteOrderMark(ReadOnlySpan<byte> bytes)
    {
        // UTF-32 LE first: its mark starts with UTF-16 LE's.
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return (StrictUtf8, 3);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        return null;
    }

    /// <summary>
    /// <paramref name="encoding"/> decoding what it cannot read as U+FFFD. The
    /// framework's own replacement fallback substitutes a question mark, which
    /// is a character a field could really hold.
    /// </summary>
    internal static Encoding Lenient(Encoding encoding)
    {
        var lenient = (Encoding)encoding.Clone();
        lenient.DecoderFallback = new DecoderReplacementFallback(((char)0xFFFD).ToString());
        return lenient;
    }

    private static bool TryDecodeMarked(ReadOnlySpan<byte> bytes, List<DocumentDiagnostic> diagnostics, out string text)
    {
        if (ByteOrderMark(bytes) is not (Encoding encoding, int length))
        {
            text = string.Empty;
            return false;
        }

        ReadOnlySpan<byte> body = bytes[length..];
        try
        {
            text = encoding.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            // The mark is an explicit statement, so the encoding is not second-
            // guessed. What breaks it is replaced and reported.
            text = Lenient(encoding).GetString(body);
            diagnostics.Add(DocumentDiagnostic.Warning(
                CsvDiagnosticCodes.EncodingInvalid,
                "The input's byte-order mark names an encoding its bytes do not follow; " +
                "the invalid sequences were replaced with U+FFFD."));
        }

        return true;
    }

    /// <summary>
    /// <paramref name="bytes"/> without a trailing UTF-8 sequence that its lead
    /// byte says is longer than what is left.
    /// </summary>
    internal static ReadOnlySpan<byte> WithoutIncompleteUtf8Tail(ReadOnlySpan<byte> bytes)
    {
        int lead = bytes.Length - 1;
        int stop = Math.Max(0, bytes.Length - 4);
        while (lead >= stop && (bytes[lead] & 0xC0) == 0x80)
            lead--;

        if (lead < 0 || lead < stop)
            return bytes;

        byte first = bytes[lead];
        int expected = first switch
        {
            < 0x80 => 1,
            >= 0xF0 and < 0xF8 => 4,
            >= 0xE0 => 3,
            >= 0xC0 => 2,
            _ => 1,
        };

        return lead + expected > bytes.Length ? bytes[..lead] : bytes;
    }
}
