using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>
/// Projects the document's Info dictionary and Catalog language into the
/// normalized metadata envelope.
/// </summary>
/// <remarks>
/// <para>
/// XMP is deliberately <em>detected and dropped</em> in this release: it needs
/// its own standards, patent, schema, and provenance review (IP-004), and the
/// reader must not quietly become an XMP implementation by way of a convenient
/// XML parse. The presence of a metadata stream is reported so the omission is
/// visible rather than silent.
/// </para>
/// <para>
/// No source value ever reaches a diagnostic message — only field names do.
/// </para>
/// </remarks>
internal static class PdfMetadataReader
{
    public static PdfDocumentMetadata Read(PdfObjectStore store, PdfDictionary? catalog)
    {
        PdfDictionary? info = store.Resolve(store.Trailer["Info"]) as PdfDictionary;

        if (catalog is not null && store.Resolve(catalog["Metadata"]) is not null)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.MetadataRawDropped,
                "The document carries an XMP metadata stream. XMP is outside this release's reviewed scope, so the packet was dropped.");
        }

        if (info is null)
            return PdfDocumentMetadata.Empty;

        string? title = ReadText(store, info, "Title");
        string? author = ReadText(store, info, "Author");
        string? subject = ReadText(store, info, "Subject");
        string? keywords = ReadText(store, info, "Keywords");
        string? creator = ReadText(store, info, "Creator");
        string? producer = ReadText(store, info, "Producer");
        PdfDate? created = ReadDate(store, info, "CreationDate");
        PdfDate? modified = ReadDate(store, info, "ModDate");
        string? language = catalog is not null ? ReadText(store, catalog, "Lang") : null;

        int dropped = 0;
        foreach (string key in info.Keys)
        {
            if (key is not ("Title" or "Author" or "Subject" or "Keywords" or "Creator" or "Producer" or "CreationDate" or "ModDate" or "Trapped"))
                dropped++;
        }

        if (dropped > 0)
        {
            store.Diagnostics.Info(
                PdfDiagnosticCodes.MetadataDropped,
                $"{dropped} custom Info entries were dropped; only the normalized metadata allowlist is projected.");
        }

        return new PdfDocumentMetadata(
            title,
            SplitList(author),
            subject,
            SplitList(keywords),
            language,
            creator,
            producer,
            created,
            modified);
    }

    /// <summary>
    /// Decodes a PDF text string: UTF-16 with a byte-order mark, otherwise
    /// PDFDocEncoding, whose printable range coincides with Latin-1 for every
    /// code point this release maps.
    /// </summary>
    private static string? ReadText(PdfObjectStore store, PdfDictionary dictionary, string key)
    {
        if (store.Resolve(dictionary[key]) is not PdfString value)
            return null;

        byte[] bytes = value.Bytes;
        if (bytes.Length == 0)
            return string.Empty;

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Decode(bytes, 2, bigEndian: true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Decode(bytes, 2, bigEndian: false);

        var builder = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
            builder.Append(PdfDocEncoding.ToChar(b));
        return builder.ToString();
    }

    private static string Decode(byte[] bytes, int offset, bool bigEndian)
    {
        var builder = new StringBuilder((bytes.Length - offset) / 2);
        for (int i = offset; i + 1 < bytes.Length; i += 2)
        {
            int unit = bigEndian
                ? (bytes[i] << 8) | bytes[i + 1]
                : (bytes[i + 1] << 8) | bytes[i];
            builder.Append((char)unit);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits an Info list field. PDF stores authors and keywords as one string
    /// with no normative separator, so only unambiguous separators are honoured
    /// and a value containing none stays a single entry.
    /// </summary>
    private static IEnumerable<string>? SplitList(string? value)
    {
        if (value is null)
            return null;
        if (value.Length == 0)
            return Array.Empty<string>();

        string[] parts = value.Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? [value] : parts;
    }

    private static PdfDate? ReadDate(PdfObjectStore store, PdfDictionary dictionary, string key)
    {
        if (store.Resolve(dictionary[key]) is not PdfString value)
            return null;

        string text = PdfLexer.Latin1(value.Bytes, 0, value.Bytes.Length);
        return TryParseDate(text, out PdfDate date) ? date : null;
    }

    /// <summary>
    /// Parses the <c>D:YYYYMMDDHHmmSSOHH'mm'</c> form (clause 7.9.4). Every field
    /// after the year is optional, and an out-of-range component rejects the value
    /// rather than being clamped into a different date.
    /// </summary>
    internal static bool TryParseDate(string text, out PdfDate date)
    {
        date = default;
        if (string.IsNullOrEmpty(text))
            return false;

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.StartsWith("D:", StringComparison.Ordinal))
            span = span[2..];

        if (span.Length < 4 || !TryDigits(span, 0, 4, out int year))
            return false;

        int month = 1, day = 1, hour = 0, minute = 0, second = 0;
        if (span.Length >= 6 && !TryDigits(span, 4, 2, out month))
            return false;
        if (span.Length >= 8 && !TryDigits(span, 6, 2, out day))
            return false;
        if (span.Length >= 10 && !TryDigits(span, 8, 2, out hour))
            return false;
        if (span.Length >= 12 && !TryDigits(span, 10, 2, out minute))
            return false;
        if (span.Length >= 14 && !TryDigits(span, 12, 2, out second))
            return false;

        if (year < 1 || year > 9999 || month is < 1 or > 12 || day < 1 ||
            day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 60)
            return false;

        // A leap second is folded back to :59 rather than rejecting the timestamp.
        if (second == 60)
            second = 59;

        if (span.Length <= 14)
        {
            date = PdfDate.WithoutOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified));
            return true;
        }

        char sign = span[14];
        if (sign == 'Z')
        {
            date = PdfDate.WithOffset(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
            return true;
        }

        if (sign is not ('+' or '-'))
            return false;

        int offsetHours = 0;
        int offsetMinutes = 0;
        if (span.Length >= 17 && !TryDigits(span, 15, 2, out offsetHours))
            return false;
        // The apostrophe separator is at index 17 in the canonical form.
        if (span.Length >= 20 && !TryDigits(span, 18, 2, out offsetMinutes))
            return false;

        if (offsetHours > 14 || offsetMinutes > 59)
            return false;

        var offset = new TimeSpan(offsetHours, offsetMinutes, 0);
        if (sign == '-')
            offset = -offset;

        date = PdfDate.WithOffset(new DateTimeOffset(year, month, day, hour, minute, second, offset));
        return true;
    }

    private static bool TryDigits(ReadOnlySpan<char> span, int start, int length, out int value)
    {
        value = 0;
        if (start + length > span.Length)
            return false;
        return int.TryParse(span.Slice(start, length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>
/// PDFDocEncoding, the single-byte encoding PDF uses for text strings without a
/// byte-order mark (clause 7.9.2 and Annex D).
/// </summary>
/// <remarks>
/// The mapping is Latin-1 except for the 0x18–0x1F and 0x80–0x9F ranges, where
/// PDF assigns typographic characters rather than control codes. Only those
/// exceptional slots are tabulated here; everything else is the identity mapping,
/// so the table stays small enough to read and check by eye.
/// </remarks>
internal static class PdfDocEncoding
{
    private static readonly char[] LowRange =
    [
        '˘', 'ˇ', 'ˆ', '˙', '˝', '˛', '˚', '˜',
    ];

    private static readonly char[] HighRange =
    [
        '•', '†', '‡', '…', '—', '–', 'ƒ', '⁄',
        '‹', '›', '−', '‰', '„', '“', '”', '‘',
        '’', '‚', '™', 'ﬁ', 'ﬂ', 'Ł', 'Œ', 'Š',
        'Ÿ', 'Ž', 'ı', 'ł', 'œ', 'š', 'ž', '�',
    ];

    public static char ToChar(byte value) => value switch
    {
        >= 0x18 and <= 0x1F => LowRange[value - 0x18],
        >= 0x80 and <= 0x9F => HighRange[value - 0x80],
        _ => (char)value,
    };
}
