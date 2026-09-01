using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>
/// Projects the document's Info dictionary, XMP packet, and Catalog language into
/// the normalized metadata envelope.
/// </summary>
/// <remarks>
/// <para>
/// A PDF can say the same thing twice. Info is the original dictionary and XMP is
/// the packet that superseded it, and many producers write both and keep only one
/// current. So this does not pick a source and ignore the other: XMP wins for a
/// field it actually supplies, Info is the fallback for every field it does not,
/// and a field both supplied differently is <em>reported</em> rather than quietly
/// resolved (PDF roadmap §6.2).
/// </para>
/// <para>
/// The packet is read for the allowlist and then discarded. Nothing preserves it,
/// no writer emits it, and no property outside the allowlist reaches the result —
/// parsing XMP is not the same as carrying it.
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
        XmpMetadata? xmp = ReadXmpPacket(store, catalog, info);

        string? title = ReadText(store, info, "Title");
        string? subject = ReadText(store, info, "Subject");
        string? creator = ReadText(store, info, "Creator");
        string? producer = ReadText(store, info, "Producer");
        PdfDate? created = ReadDate(store, info, "CreationDate");
        PdfDate? modified = ReadDate(store, info, "ModDate");
        string? language = ReadText(store, catalog, "Lang");
        IReadOnlyList<string>? authors = SplitList(ReadText(store, info, "Author"));
        IReadOnlyList<string>? keywords = SplitList(ReadText(store, info, "Keywords"));

        if (xmp is not null)
        {
            var conflicts = new List<string>();

            title = Prefer(xmp.Title, title, "title", conflicts);
            authors = Prefer(xmp.Authors, authors, "authors", conflicts);
            subject = Prefer(xmp.Description, subject, "subject", conflicts);
            keywords = Prefer(xmp.Keywords, keywords, "keywords", conflicts);
            language = Prefer(xmp.Language, language, "language", conflicts);
            creator = Prefer(xmp.CreatorTool, creator, "creator application", conflicts);
            producer = Prefer(xmp.Producer, producer, "producer", conflicts);
            created = Prefer(ToPdfDate(xmp.CreateDate), created, "creation date", conflicts);
            modified = Prefer(ToPdfDate(xmp.ModifyDate), modified, "modification date", conflicts);

            if (conflicts.Count > 0)
            {
                bool one = conflicts.Count == 1;
                store.Diagnostics.Info(
                    PdfDiagnosticCodes.MetadataConflict,
                    $"Info and XMP disagree on {conflicts.Count} normalized field{(one ? string.Empty : "s")}: " +
                    $"{string.Join(", ", conflicts)}. The XMP value was taken{(one ? string.Empty : " for each")}. " +
                    "Only the field name is reported; neither value is.");
            }
        }

        NoteDroppedInfoEntries(store, info);

        return new PdfDocumentMetadata(
            title,
            authors,
            subject,
            keywords,
            language,
            creator,
            producer,
            created,
            modified);
    }

    private static void NoteDroppedInfoEntries(PdfObjectStore store, PdfDictionary? info)
    {
        if (info is null)
            return;

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
                $"{dropped} custom Info entr{(dropped == 1 ? "y was" : "ies were")} dropped; only the normalized metadata allowlist is projected.");
        }
    }

    /// <summary>
    /// Chooses between what XMP said and what Info said, recording a conflict
    /// when both spoke and disagreed.
    /// </summary>
    /// <remarks>
    /// Silence is not disagreement. A field conflicts only when both sources
    /// supplied a value and the values differ; a field XMP omits falls back to
    /// Info without comment, which is the common case for <c>/Trapped</c>-era
    /// dictionaries that XMP never mirrored.
    /// </remarks>
    private static string? Prefer(string? fromXmp, string? fromInfo, string field, List<string> conflicts)
    {
        if (fromXmp is null)
            return fromInfo;

        if (fromInfo is not null && !string.Equals(fromXmp, fromInfo, StringComparison.Ordinal))
            conflicts.Add(field);

        return fromXmp;
    }

    private static IReadOnlyList<string>? Prefer(
        IReadOnlyList<string> fromXmp,
        IReadOnlyList<string>? fromInfo,
        string field,
        List<string> conflicts)
    {
        if (fromXmp.Count == 0)
            return fromInfo;

        if (fromInfo is { Count: > 0 } && !SameSequence(fromXmp, fromInfo))
            conflicts.Add(field);

        return fromXmp;
    }

    private static PdfDate? Prefer(PdfDate? fromXmp, PdfDate? fromInfo, string field, List<string> conflicts)
    {
        if (fromXmp is not PdfDate value)
            return fromInfo;

        if (fromInfo is PdfDate other && value != other)
            conflicts.Add(field);

        return value;
    }

    private static bool SameSequence(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Maps an XMP timestamp onto the PDF one. Both keep the same distinction —
    /// whether the source stated a UTC offset — so the mapping carries it across
    /// instead of normalizing it away.
    /// </summary>
    private static PdfDate? ToPdfDate(XmpDate? date) =>
        date is not XmpDate value
            ? null
            : value.HasUtcOffset
                ? PdfDate.WithOffset(value.Value)
                : PdfDate.WithoutOffset(value.Value.DateTime);

    /// <summary>
    /// Decodes and reads the catalog's XMP packet, returning what it supplied, or
    /// null when there is none or it could not be used.
    /// </summary>
    /// <remarks>
    /// The packet goes through the same filter pipeline and the same budget as
    /// every other stream, so a metadata stream cannot buy itself a fresh
    /// allowance by being metadata.
    /// </remarks>
    private static XmpMetadata? ReadXmpPacket(PdfObjectStore store, PdfDictionary? catalog, PdfDictionary? info)
    {
        if (catalog is null || store.Resolve(catalog["Metadata"]) is not PdfObject entry)
            return null;

        if (entry is not PdfStream stream)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.MetadataXmpUnusable,
                "The catalog names an XMP metadata entry that is not a stream. It is malformed, and the normalized metadata came from Info alone.");
            return null;
        }

        PdfStreamDecodeResult decoded = store.Filters.Decode(stream, store.Resolve, store.Budget);
        XmpReadResult? result = decoded.Succeeded && decoded.Data is { } packet
            ? XmpReader.Read(packet, store.Budget.Limits.MaxXmpBytes)
            : null;

        // Reported whatever the outcome: the raw packet is dropped either way, and
        // that is what this code has always meant.
        store.Diagnostics.Info(
            PdfDiagnosticCodes.MetadataRawDropped,
            DescribeXmpPacket(store, stream, info, result));

        if (result is { Outcome: XmpReadOutcome.Read } usable)
            return usable.Metadata;

        store.Diagnostics.Skipped(
            PdfDiagnosticCodes.MetadataXmpUnusable,
            DescribeUnusableXmp(result, decoded));
        return null;
    }

    /// <summary>
    /// Describes the XMP packet by its container and its yield, never by its
    /// content.
    /// </summary>
    /// <remarks>
    /// A count of normalized fields and a count of ignored properties are facts
    /// about the packet's shape. The values themselves are not, and none appears
    /// here. The Info sentence is the one a caller needs most: a file whose only
    /// metadata was XMP is a very different result from one where Info said the
    /// same thing anyway.
    /// </remarks>
    private static string DescribeXmpPacket(
        PdfObjectStore store,
        PdfStream stream,
        PdfDictionary? info,
        XmpReadResult? result)
    {
        var text = new StringBuilder("The document carries an XMP metadata packet.");

        text.Append(result is { Outcome: XmpReadOutcome.Read }
            ? " Its normalized fields were read into the metadata allowlist, and the raw packet was then dropped: this release preserves no packet and writes no XMP back."
            : " The raw packet was dropped and is not preserved.");

        text.Append(CultureInfo.InvariantCulture, $" The packet holds {stream.RawData.Length} raw bytes");

        string filters = DescribeFilters(store, stream.Dictionary["Filter"]);
        if (filters.Length > 0)
            text.Append(", encoded with ").Append(filters);
        text.Append('.');

        if (result is { Outcome: XmpReadOutcome.Read } read)
        {
            int fields = read.Metadata.FieldCount;
            text.Append(CultureInfo.InvariantCulture, $" {fields} normalized field{(fields == 1 ? string.Empty : "s")} came from it");

            int ignored = read.IgnoredProperties;
            if (ignored > 0)
            {
                text.Append(CultureInfo.InvariantCulture,
                    $", and {ignored} propert{(ignored == 1 ? "y" : "ies")} outside the allowlist {(ignored == 1 ? "was" : "were")} ignored");
            }

            if (read.PropertiesTruncated)
                text.Append(", with the rest left unexamined at the property ceiling");

            text.Append('.');
        }

        string subtype = (store.Resolve(stream.Dictionary["Subtype"]) as PdfName)?.Value ?? string.Empty;
        if (subtype.Length > 0 && !string.Equals(subtype, "XML", StringComparison.Ordinal))
            text.Append(CultureInfo.InvariantCulture, $" It declares /Subtype /{subtype} rather than the /XML the format specifies.");

        text.Append(info is null
            ? " The document has no Info dictionary either, so nothing fell back to one."
            : " An Info dictionary was also present; XMP wins per field and Info is the fallback.");

        return text.ToString();
    }

    /// <summary>
    /// Says why a packet that was there could not be used. The reason is always
    /// structural — a filter name, a limit name, an exception type — because an
    /// XML parser's own message quotes the markup it choked on, and that markup
    /// is document content.
    /// </summary>
    private static string DescribeUnusableXmp(XmpReadResult? result, PdfStreamDecodeResult decoded)
    {
        if (result is null)
        {
            string filter = decoded.Filter is null ? string.Empty : $" ({decoded.Filter})";
            return $"The document's XMP packet could not be decoded{filter}, so the normalized metadata came from Info alone.";
        }

        return result.Outcome switch
        {
            XmpReadOutcome.TooLarge =>
                "The document's XMP packet is larger than the XMP byte ceiling and was refused without parsing, so the normalized metadata came from Info alone.",
            _ =>
                $"The document's XMP packet is not well-formed RDF/XML within the pinned subset ({result.Failure}), so the normalized metadata came from Info alone.",
        };
    }

    /// <summary>The filter chain as canonical names, in the order it is applied.</summary>
    private static string DescribeFilters(PdfObjectStore store, PdfObject? filter)
    {
        var names = new List<string>(2);

        switch (store.Resolve(filter))
        {
            case PdfName single:
                names.Add(PdfFilterNames.Canonicalize(single.Value));
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                {
                    if (store.Resolve(entry) is PdfName name)
                        names.Add(PdfFilterNames.Canonicalize(name.Value));
                }

                break;
        }

        return string.Join("+", names);
    }

    /// <summary>
    /// Decodes a PDF text string: UTF-16 with a byte-order mark, otherwise
    /// PDFDocEncoding, whose printable range coincides with Latin-1 for every
    /// code point this release maps.
    /// </summary>
    private static string? ReadText(PdfObjectStore store, PdfDictionary? dictionary, string key)
    {
        if (dictionary is null || store.Resolve(dictionary[key]) is not PdfString value)
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
    private static IReadOnlyList<string>? SplitList(string? value)
    {
        if (value is null)
            return null;
        if (value.Length == 0)
            return Array.Empty<string>();

        string[] parts = value.Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? [value] : parts;
    }

    private static PdfDate? ReadDate(PdfObjectStore store, PdfDictionary? dictionary, string key)
    {
        if (dictionary is null || store.Resolve(dictionary[key]) is not PdfString value)
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
