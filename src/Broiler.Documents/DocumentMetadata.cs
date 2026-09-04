using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Broiler.Documents;

/// <summary>
/// A document timestamp, keeping the distinction every format Broiler reads makes
/// between a value that stated a UTC offset and one that did not.
/// </summary>
/// <remarks>
/// Broiler never invents a zone for a zone-less timestamp (PDF roadmap §6.2). A
/// zone-less value is carried as an unspecified local time with
/// <see cref="HasUtcOffset"/> false, so a writer can emit it back in the form it
/// arrived in rather than in whichever zone the converting machine happened to
/// sit in. That machine's zone is not a fact about the document.
/// </remarks>
public readonly struct DocumentDate : IEquatable<DocumentDate>
{
    private DocumentDate(DateTimeOffset value, bool hasUtcOffset)
    {
        Value = value;
        HasUtcOffset = hasUtcOffset;
    }

    public DateTimeOffset Value { get; }

    /// <summary>True when the source stated a UTC offset.</summary>
    public bool HasUtcOffset { get; }

    /// <summary>A date whose source stated an offset.</summary>
    public static DocumentDate WithOffset(DateTimeOffset value) => new(value, true);

    /// <summary>A date whose source stated no offset; none is invented.</summary>
    public static DocumentDate WithoutOffset(DateTime value) =>
        new(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero), false);

    public bool Equals(DocumentDate other) => Value == other.Value && HasUtcOffset == other.HasUtcOffset;

    public override bool Equals(object? obj) => obj is DocumentDate other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, HasUtcOffset);

    public static bool operator ==(DocumentDate left, DocumentDate right) => left.Equals(right);

    public static bool operator !=(DocumentDate left, DocumentDate right) => !left.Equals(right);

    public override string ToString() => Value.ToString(
        HasUtcOffset ? "yyyy-MM-ddTHH:mm:sszzz" : "yyyy-MM-ddTHH:mm:ss",
        CultureInfo.InvariantCulture);
}

/// <summary>
/// The format-neutral normalized metadata envelope. Only the V1 allowlist crosses
/// this boundary: no raw XMP packet, no arbitrary Info key or custom property, no
/// trailer identifier, no producer path or user name.
/// </summary>
/// <remarks>
/// <para>
/// The nine fields below are frozen (PDF roadmap §6.2). A format states them in
/// its own vocabulary and its own parts — PDF in an Info dictionary and an XMP
/// packet, DOCX in <c>docProps/core.xml</c> and <c>docProps/app.xml</c>, ODT in
/// <c>meta.xml</c> — and each codec reconciles its own sources before it reaches
/// this type. What arrives here is the agreed value, not the argument that
/// produced it: a codec that found two sources disagreeing says so in a
/// diagnostic, because a silently resolved conflict is indistinguishable from no
/// conflict.
/// </para>
/// <para>
/// Missing and explicitly empty stay distinct — a null property means the source
/// said nothing, an empty string means the source said "nothing". A writer needs
/// that difference to decide between omitting a key and emitting an empty one,
/// and collapsing the two would make a round trip lose a statement the author
/// made on purpose.
/// </para>
/// <para>
/// <strong>Metadata supplied to a writer comes from the caller, never from a read
/// result.</strong> Nothing in this component copies one to the other, and that
/// absence is the transfer policy rather than an omission in it: having read what
/// a document says about itself is not authority to republish it under someone
/// else's name. A caller that wants the transfer performs it, which is one
/// explicit line, and may then override any field with <see cref="With"/>.
/// </para>
/// <para>
/// The envelope is an in-process value. It is not written to a sidecar file, not
/// logged, and not cached across documents; it lives as long as the result or
/// options object holding it and no longer.
/// </para>
/// </remarks>
public sealed class DocumentMetadata
{
    // Declared before Empty: static field initializers run in textual order, and
    // Empty's constructor reads this one.
    private static readonly ReadOnlyCollection<string> EmptyList = new([]);

    /// <summary>Metadata that states nothing at all.</summary>
    public static DocumentMetadata Empty { get; } = new();

    public DocumentMetadata(
        string? title = null,
        IEnumerable<string>? authors = null,
        string? subject = null,
        IEnumerable<string>? keywords = null,
        string? language = null,
        string? creatorApplication = null,
        string? producer = null,
        DocumentDate? creationDate = null,
        DocumentDate? modificationDate = null)
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

    /// <summary>
    /// Authors in source order. Formats state this in incompatible shapes — one
    /// delimited string in a PDF Info dictionary, a repeated element in ODT — and
    /// the list is the shape that loses nothing either way.
    /// </summary>
    public IReadOnlyList<string> Authors { get; }

    public string? Subject { get; }

    /// <summary>Keywords in source order.</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>The document's natural language.</summary>
    public string? Language { get; }

    /// <summary>
    /// The application that authored the original document, where the format
    /// distinguishes that from the one that produced the file. Most do not, and
    /// leave this absent rather than repeating <see cref="Producer"/>.
    /// </summary>
    public string? CreatorApplication { get; }

    /// <summary>The application that produced the file being read or written.</summary>
    public string? Producer { get; }

    public DocumentDate? CreationDate { get; }

    public DocumentDate? ModificationDate { get; }

    /// <summary>True when every normalized field is absent.</summary>
    public bool IsEmpty =>
        Title is null && Authors.Count == 0 && Subject is null && Keywords.Count == 0 &&
        Language is null && CreatorApplication is null && Producer is null &&
        CreationDate is null && ModificationDate is null;

    /// <summary>
    /// This envelope with selected fields replaced. The caller's override step of
    /// the transfer policy: take what a read produced, then correct the fields
    /// that should not survive into a new document.
    /// </summary>
    /// <remarks>
    /// A parameter left unset keeps the current value, which means this cannot
    /// clear a field back to absent. Clearing is a different intent from leaving
    /// alone and reads better as constructing the envelope you actually want, so
    /// it is deliberately not expressible here.
    /// </remarks>
    public DocumentMetadata With(
        string? title = null,
        IEnumerable<string>? authors = null,
        string? subject = null,
        IEnumerable<string>? keywords = null,
        string? language = null,
        string? creatorApplication = null,
        string? producer = null,
        DocumentDate? creationDate = null,
        DocumentDate? modificationDate = null) =>
        new(
            title ?? Title,
            authors ?? Authors,
            subject ?? Subject,
            keywords ?? Keywords,
            language ?? Language,
            creatorApplication ?? CreatorApplication,
            producer ?? Producer,
            creationDate ?? CreationDate,
            modificationDate ?? ModificationDate);

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
