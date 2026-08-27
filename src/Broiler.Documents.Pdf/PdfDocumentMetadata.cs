using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Broiler.Documents.Pdf;

/// <summary>
/// A PDF date value, keeping the distinction the format makes between a
/// timestamp that stated a UTC offset and one that did not.
/// </summary>
/// <remarks>
/// Broiler never invents a zone for a zone-less PDF date (PDF roadmap §6.2). A
/// zone-less value is carried as an unspecified local time with
/// <see cref="HasUtcOffset"/> false, so a writer can emit it back in the same
/// form it arrived in.
/// </remarks>
public readonly struct PdfDate : IEquatable<PdfDate>
{
    private PdfDate(DateTimeOffset value, bool hasUtcOffset)
    {
        Value = value;
        HasUtcOffset = hasUtcOffset;
    }

    public DateTimeOffset Value { get; }

    /// <summary>True when the source stated a UTC offset.</summary>
    public bool HasUtcOffset { get; }

    /// <summary>A date whose source stated an offset.</summary>
    public static PdfDate WithOffset(DateTimeOffset value) => new(value, true);

    /// <summary>A date whose source stated no offset; none is invented.</summary>
    public static PdfDate WithoutOffset(DateTime value) =>
        new(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero), false);

    public bool Equals(PdfDate other) => Value == other.Value && HasUtcOffset == other.HasUtcOffset;

    public override bool Equals(object? obj) => obj is PdfDate other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, HasUtcOffset);

    public static bool operator ==(PdfDate left, PdfDate right) => left.Equals(right);

    public static bool operator !=(PdfDate left, PdfDate right) => !left.Equals(right);

    public override string ToString() => Value.ToString(
        HasUtcOffset ? "yyyy-MM-ddTHH:mm:sszzz" : "yyyy-MM-ddTHH:mm:ss",
        CultureInfo.InvariantCulture);
}

/// <summary>
/// The format-neutral normalized metadata envelope. Only the V1 allowlist crosses
/// this boundary: no raw XMP packet, no arbitrary Info key, no trailer identifier,
/// no producer path or user name.
/// </summary>
/// <remarks>
/// <para>
/// Missing and explicitly empty stay distinct — a null property means the source
/// said nothing, an empty string means the source said "nothing". A writer needs
/// that difference to decide between omitting a key and emitting an empty one.
/// </para>
/// <para>
/// Metadata supplied to a writer comes from the caller, never from a read result:
/// reading a document does not authorize re-publishing what it said about itself.
/// </para>
/// </remarks>
public sealed class PdfDocumentMetadata
{
    // Declared before Empty: static field initializers run in textual order, and
    // Empty's constructor reads this one.
    private static readonly ReadOnlyCollection<string> EmptyList = new([]);

    /// <summary>Metadata that states nothing at all.</summary>
    public static PdfDocumentMetadata Empty { get; } = new();

    public PdfDocumentMetadata(
        string? title = null,
        IEnumerable<string>? authors = null,
        string? subject = null,
        IEnumerable<string>? keywords = null,
        string? language = null,
        string? creatorApplication = null,
        string? producer = null,
        PdfDate? creationDate = null,
        PdfDate? modificationDate = null)
    {
        Title = title;
        Authors = Freeze(authors);
        Subject = subject;
        Keywords = Freeze(keywords);
        Language = language;
        CreatorApplication = creatorApplication;
        Producer = producer;
        CreationDate = creationDate;
        ModificationDate = modificationDate;
    }

    public string? Title { get; }

    /// <summary>Authors in source order; the PDF Info form is one delimited string.</summary>
    public IReadOnlyList<string> Authors { get; }

    public string? Subject { get; }

    /// <summary>Keywords in source order.</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>The document's natural language from the Catalog's <c>/Lang</c>.</summary>
    public string? Language { get; }

    /// <summary>The application that authored the original document.</summary>
    public string? CreatorApplication { get; }

    /// <summary>The application that produced the PDF.</summary>
    public string? Producer { get; }

    public PdfDate? CreationDate { get; }

    public PdfDate? ModificationDate { get; }

    /// <summary>True when every normalized field is absent.</summary>
    public bool IsEmpty =>
        Title is null && Authors.Count == 0 && Subject is null && Keywords.Count == 0 &&
        Language is null && CreatorApplication is null && Producer is null &&
        CreationDate is null && ModificationDate is null;

    private static ReadOnlyCollection<string> Freeze(IEnumerable<string>? values)
    {
        if (values is null)
            return EmptyList;

        var list = new List<string>();
        foreach (string value in values)
        {
            if (value is not null)
                list.Add(value);
        }

        return list.Count == 0 ? EmptyList : new ReadOnlyCollection<string>(list);
    }
}
