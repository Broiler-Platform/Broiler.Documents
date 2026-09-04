using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// The document properties an OPC package states about itself, to and from the
/// shared <see cref="DocumentMetadata"/> envelope.
/// </summary>
/// <remarks>
/// <para>
/// Two parts carry them. <c>docProps/core.xml</c> holds the Dublin Core set — the
/// title, author, subject, keywords, language and timestamps — and
/// <c>docProps/app.xml</c> holds the producing application. Unlike a PDF, where
/// an Info dictionary and an XMP packet can each state the same field and
/// disagree, these two parts do not overlap on anything this envelope normalizes,
/// so there is no conflict to reconcile and none is invented.
/// </para>
/// <para>
/// OOXML states no application distinct from the producing one, so
/// <see cref="DocumentMetadata.CreatorApplication"/> stays absent rather than
/// repeating <see cref="DocumentMetadata.Producer"/>. Absent is the honest value:
/// a reader that filled both from one source would be inventing the distinction
/// the two fields exist to record.
/// </para>
/// </remarks>
internal static class DocxMetadata
{
    public const string CorePart = "docProps/core.xml";
    public const string AppPart = "docProps/app.xml";

    public const string CoreContentType = "application/vnd.openxmlformats-package.core-properties+xml";
    public const string AppContentType =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";

    public const string CoreRelationship =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    public const string AppRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";

    private static readonly XNamespace CoreProperties =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DublinCoreTerms = "http://purl.org/dc/terms/";
    private static readonly XNamespace XmlSchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace ExtendedProperties =
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    /// <summary>Reads both property parts, skipping either one that is absent.</summary>
    public static DocumentMetadata Read(
        ZipArchive archive,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        XElement? core = LoadRoot(archive, CorePart, limits, diagnostics);
        XElement? app = LoadRoot(archive, AppPart, limits, diagnostics);
        if (core is null && app is null)
            return DocumentMetadata.Empty;

        return new DocumentMetadata(
            title: Text(core, DublinCore + "title"),
            authors: Split(Text(core, DublinCore + "creator")),
            subject: Text(core, DublinCore + "subject"),
            keywords: Split(Text(core, CoreProperties + "keywords")),
            language: Text(core, DublinCore + "language"),
            creatorApplication: null,
            producer: Text(app, ExtendedProperties + "Application"),
            creationDate: Date(core, DublinCoreTerms + "created", diagnostics),
            modificationDate: Date(core, DublinCoreTerms + "modified", diagnostics));
    }

    /// <summary>The Dublin Core part, or null when there is nothing to state.</summary>
    public static XDocument? BuildCore(DocumentMetadata metadata)
    {
        var root = new XElement(
            CoreProperties + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", CoreProperties.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", DublinCore.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcterms", DublinCoreTerms.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", XmlSchemaInstance.NamespaceName));

        Add(root, DublinCore + "title", metadata.Title);
        Add(root, DublinCore + "creator", Join(metadata.Authors));
        Add(root, DublinCore + "subject", metadata.Subject);
        Add(root, CoreProperties + "keywords", Join(metadata.Keywords));
        Add(root, DublinCore + "language", metadata.Language);
        AddDate(root, DublinCoreTerms + "created", metadata.CreationDate);
        AddDate(root, DublinCoreTerms + "modified", metadata.ModificationDate);

        return root.HasElements ? new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root) : null;
    }

    /// <summary>The extended-properties part, or null when there is nothing to state.</summary>
    public static XDocument? BuildApp(DocumentMetadata metadata)
    {
        if (metadata.Producer is null)
            return null;

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                ExtendedProperties + "Properties",
                new XAttribute(XNamespace.Xmlns + "vt", ExtendedProperties.NamespaceName),
                new XElement(ExtendedProperties + "Application", metadata.Producer)));
    }

    private static XElement? LoadRoot(
        ZipArchive archive,
        string partPath,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, partPath);
        return entry is null
            ? null
            : DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.properties.xml")?.Root;
    }

    /// <summary>
    /// An element's text, keeping the difference between an element that is not
    /// there and one that is there and empty.
    /// </summary>
    private static string? Text(XElement? parent, XName name) =>
        parent?.Element(name)?.Value;

    /// <summary>
    /// One delimited property split into the list the envelope carries. Word
    /// writes several authors or keywords as one string, and the separator it
    /// uses is not fixed, so both of the conventional ones are accepted.
    /// </summary>
    private static IEnumerable<string>? Split(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => part.Length > 0);
    }

    private static string? Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? null : string.Join("; ", values);

    private static DocumentDate? Date(XElement? parent, XName name, List<DocumentDiagnostic> diagnostics)
    {
        string? value = Text(parent, name);
        if (string.IsNullOrEmpty(value))
            return null;

        if (DocumentTimestamp.TryParse(value, out DocumentDate date))
            return date;

        diagnostics.Add(DocumentDiagnostic.Warning(
            "docx.properties.date",
            "A document property stated a timestamp this build could not read, so it is " +
            "dropped rather than guessed at: " + name.LocalName + "."));
        return null;
    }

    private static void Add(XElement root, XName name, string? value)
    {
        if (value is not null)
            root.Add(new XElement(name, value));
    }

    private static void AddDate(XElement root, XName name, DocumentDate? date)
    {
        if (date is null)
            return;

        root.Add(new XElement(
            name,
            new XAttribute(XmlSchemaInstance + "type", "dcterms:W3CDTF"),
            DocumentTimestamp.ToW3cdtf(date.Value)));
    }
}
