using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Odt;

/// <summary>
/// What an ODF package states about itself in <c>meta.xml</c>, to and from the
/// shared <see cref="DocumentMetadata"/> envelope.
/// </summary>
/// <remarks>
/// <para>
/// One part states all of it, so there is nothing to reconcile here and no
/// conflict to diagnose — unlike a PDF, where an Info dictionary and an XMP
/// packet can each name the title and disagree.
/// </para>
/// <para>
/// Two mappings are worth stating because the obvious reading of the element
/// names is wrong. ODF's <c>dc:creator</c> is <em>the last person to modify the
/// document</em>, not its author; the author is
/// <c>meta:initial-creator</c>, and that is what
/// <see cref="DocumentMetadata.Authors"/> is read from. And
/// <c>dc:date</c> is the modification time, with the creation time in
/// <c>meta:creation-date</c>. Reading the pair the other way round is a silent
/// error: both fields are populated, both look plausible, and every timestamp is
/// wrong.
/// </para>
/// <para>
/// ODF states one producing application in <c>meta:generator</c> and no separate
/// authoring one, so <see cref="DocumentMetadata.CreatorApplication"/> stays
/// absent rather than repeating <see cref="DocumentMetadata.Producer"/>.
/// </para>
/// </remarks>
internal static class OdtMetadata
{
    /// <summary>What this writer names itself when the caller states no producer.</summary>
    public const string DefaultGenerator = "Broiler.Documents.Odt";

    /// <summary>Reads the package's <c>meta.xml</c>, if it has one.</summary>
    public static DocumentMetadata Read(
        ZipArchive archive,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        ZipArchiveEntry? entry = OdtPackage.FindEntry(archive, OdtNamespaces.MetaPart);
        if (entry is null)
            return DocumentMetadata.Empty;

        XElement? meta = OdtPackage
            .LoadEntryXml(entry, limits, diagnostics, "odt.meta.xml")?
            .Root?
            .Element(OdtNamespaces.Office + "meta");

        if (meta is null)
            return DocumentMetadata.Empty;

        return new DocumentMetadata(
            title: Text(meta, OdtNamespaces.DublinCore + "title"),
            authors: Author(meta),
            subject: Text(meta, OdtNamespaces.DublinCore + "subject"),
            keywords: meta.Elements(OdtNamespaces.Meta + "keyword")
                .Select(static k => k.Value)
                .Where(static k => k.Length > 0),
            language: Text(meta, OdtNamespaces.DublinCore + "language"),
            creatorApplication: null,
            producer: Text(meta, OdtNamespaces.Meta + "generator"),
            creationDate: Date(meta, OdtNamespaces.Meta + "creation-date", diagnostics),
            modificationDate: Date(meta, OdtNamespaces.DublinCore + "date", diagnostics));
    }

    /// <summary>
    /// The <c>office:meta</c> body for a write. Always returns at least the
    /// generator, because a package that names no producer at all is less useful
    /// than one that names this writer.
    /// </summary>
    public static XElement Build(DocumentMetadata metadata)
    {
        var meta = new XElement(OdtNamespaces.Office + "meta");

        Add(meta, OdtNamespaces.DublinCore + "title", metadata.Title);
        if (metadata.Authors.Count > 0)
            meta.Add(new XElement(OdtNamespaces.Meta + "initial-creator", metadata.Authors[0]));
        Add(meta, OdtNamespaces.DublinCore + "subject", metadata.Subject);
        foreach (string keyword in metadata.Keywords)
            meta.Add(new XElement(OdtNamespaces.Meta + "keyword", keyword));
        Add(meta, OdtNamespaces.DublinCore + "language", metadata.Language);
        meta.Add(new XElement(
            OdtNamespaces.Meta + "generator",
            metadata.Producer ?? DefaultGenerator));
        AddDate(meta, OdtNamespaces.Meta + "creation-date", metadata.CreationDate);
        AddDate(meta, OdtNamespaces.DublinCore + "date", metadata.ModificationDate);

        return meta;
    }

    /// <summary>The fields ODF states no equivalent for at all.</summary>
    public static IEnumerable<string> UnsupportedFields(DocumentMetadata metadata)
    {
        yield return nameof(metadata.CreatorApplication);
    }

    /// <summary>
    /// The fields ODF states in a narrower form than the envelope carries.
    /// <c>meta:initial-creator</c> holds one name: ODF records who started a
    /// document, not everyone who wrote it, and folding the rest into that
    /// element would assert that one person is called "A; B". The first author
    /// does reach the file, so this is a narrowing rather than a loss — and a
    /// narrowing is the one a caller cannot see for themselves, because the
    /// output looks like a document with a single author.
    /// </summary>
    public static IEnumerable<string> NarrowedFields(DocumentMetadata metadata)
    {
        if (metadata.Authors.Count > 1)
            yield return nameof(metadata.Authors);
    }

    private static IEnumerable<string>? Author(XElement meta)
    {
        string? author = Text(meta, OdtNamespaces.Meta + "initial-creator");
        return string.IsNullOrEmpty(author) ? null : [author];
    }

    private static string? Text(XElement parent, XName name) => parent.Element(name)?.Value;

    private static DocumentDate? Date(XElement meta, XName name, List<DocumentDiagnostic> diagnostics)
    {
        string? value = Text(meta, name);
        if (string.IsNullOrEmpty(value))
            return null;

        if (DocumentTimestamp.TryParse(value, out DocumentDate date))
            return date;

        diagnostics.Add(DocumentDiagnostic.Warning(
            "odt.meta.date",
            "A document property stated a timestamp this build could not read, so it is " +
            "dropped rather than guessed at: " + name.LocalName + "."));
        return null;
    }

    private static void Add(XElement meta, XName name, string? value)
    {
        if (value is not null)
            meta.Add(new XElement(name, value));
    }

    private static void AddDate(XElement meta, XName name, DocumentDate? date)
    {
        if (date is not null)
            meta.Add(new XElement(name, DocumentTimestamp.ToW3cdtf(date.Value)));
    }
}
