using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// The footnotes and endnotes parts of a package, each note by the id a
/// <c>w:footnoteReference</c> or <c>w:endnoteReference</c> in the body names.
/// </summary>
/// <remarks>
/// <para>
/// A note is read when the body cites it, not up front: its paragraphs go into
/// the citation, and a note no reference names is not part of the document a
/// reader sees. The separators Word keeps in the same part
/// (<c>w:type="separator"</c>, <c>continuationSeparator</c>,
/// <c>continuationNotice</c>) are its drawing of the line above the notes, not
/// notes, and are left out.
/// </para>
/// <para>
/// Each part has relationships of its own, which a picture or a hyperlink in a
/// note resolves against.
/// </para>
/// </remarks>
internal sealed class DocxNotes
{
    private readonly Dictionary<int, XElement> _footnotes;
    private readonly Dictionary<int, XElement> _endnotes;

    private DocxNotes(
        Dictionary<int, XElement> footnotes,
        DocxRelationships footnoteRelationships,
        Dictionary<int, XElement> endnotes,
        DocxRelationships endnoteRelationships,
        NoteNumbering numbering)
    {
        _footnotes = footnotes;
        _endnotes = endnotes;
        FootnoteRelationships = footnoteRelationships;
        EndnoteRelationships = endnoteRelationships;
        Numbering = numbering;
    }

    public static DocxNotes Empty { get; } = new([], DocxRelationships.Empty, [], DocxRelationships.Empty, NoteNumbering.Default);

    public DocxRelationships FootnoteRelationships { get; }

    public DocxRelationships EndnoteRelationships { get; }

    /// <summary>How the document numbers its notes, from <c>w:footnotePr</c> and <c>w:endnotePr</c>.</summary>
    public NoteNumbering Numbering { get; }

    /// <summary>
    /// Whether the first note read raised the label at its head, as Word's
    /// FootnoteReference style does; null until a note has been read.
    /// </summary>
    public bool? LabelsRaised { get; set; }

    public static DocxNotes Load(
        ZipArchive archive,
        DocxRelationships documentRelationships,
        string baseDirectory,
        XElement? settings,
        XElement? finalSectionProperties,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        (Dictionary<int, XElement> footnotes, DocxRelationships footnoteRelationships) = LoadPart(
            archive, documentRelationships, DocxNamespaces.FootnotesRelationship, "footnote", limits, diagnostics);
        (Dictionary<int, XElement> endnotes, DocxRelationships endnoteRelationships) = LoadPart(
            archive, documentRelationships, DocxNamespaces.EndnotesRelationship, "endnote", limits, diagnostics);

        NoteNumbering numbering = ReadNumbering(settings, NoteNumbering.Default);
        numbering = ReadNumbering(finalSectionProperties, numbering);
        return new DocxNotes(footnotes, footnoteRelationships, endnotes, endnoteRelationships, numbering);
    }

    /// <summary>The note a reference names, or null when the part has no such note.</summary>
    public XElement? Find(NoteKind kind, int id) =>
        (kind == NoteKind.Endnote ? _endnotes : _footnotes).TryGetValue(id, out XElement? note) ? note : null;

    private static (Dictionary<int, XElement> Notes, DocxRelationships Relationships) LoadPart(
        ZipArchive archive,
        DocxRelationships documentRelationships,
        string relationshipType,
        string element,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        string? path = null;
        foreach (DocxRelationship relationship in documentRelationships.All)
        {
            if (relationship.Type.Equals(relationshipType, StringComparison.Ordinal) && !relationship.TargetModeExternal)
            {
                path = relationship.Target;
                break;
            }
        }

        var notes = new Dictionary<int, XElement>();
        ZipArchiveEntry? entry = path is null ? null : DocxPackage.FindEntry(archive, path);
        if (path is null || entry is null)
            return (notes, DocxRelationships.Empty);

        XDocument? xml = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.notes");
        if (xml?.Root is null)
            return (notes, DocxRelationships.Empty);

        foreach (XElement note in xml.Root.Elements(DocxNamespaces.Wordprocessing + element))
        {
            string? type = (string?)note.Attribute(DocxNamespaces.Wordprocessing + "type");
            if (type is not null && type != "normal")
                continue;

            if (DocxValueReader.TryReadInt(note.Attribute(DocxNamespaces.Wordprocessing + "id"), out int id))
                notes.TryAdd(id, note);
        }

        DocxRelationships relationships = DocxPackage.ReadRelationships(
            archive,
            DocxPackage.RelationshipsPartPath(path),
            DocxPackage.BasePartDirectory(path),
            limits,
            diagnostics);
        return (notes, relationships);
    }

    /// <summary>
    /// Reads <c>w:footnotePr</c> and <c>w:endnotePr</c> under
    /// <paramref name="parent"/> over <paramref name="numbering"/>: the
    /// settings part states the document's, and a section may restate them.
    /// </summary>
    private static NoteNumbering ReadNumbering(XElement? parent, NoteNumbering numbering)
    {
        if (parent is null)
            return numbering;

        XNamespace w = DocxNamespaces.Wordprocessing;
        if (parent.Element(w + "footnotePr") is XElement footnote)
        {
            numbering = numbering with
            {
                FootnoteFormat = Format(footnote) ?? numbering.FootnoteFormat,
                FootnoteStart = Start(footnote) ?? numbering.FootnoteStart,
            };
        }

        if (parent.Element(w + "endnotePr") is XElement endnote)
        {
            numbering = numbering with
            {
                EndnoteFormat = Format(endnote) ?? numbering.EndnoteFormat,
                EndnoteStart = Start(endnote) ?? numbering.EndnoteStart,
            };
        }

        return numbering;
    }

    private static ListNumberFormat? Format(XElement properties)
    {
        string? numFmt = DocxValueReader.WordValue(properties.Element(DocxNamespaces.Wordprocessing + "numFmt"));
        return numFmt is null ? null : DocxNumbering.NumberFormat(numFmt);
    }

    private static int? Start(XElement properties) =>
        DocxValueReader.TryReadInt(
            properties.Element(DocxNamespaces.Wordprocessing + "numStart")?.Attribute(DocxNamespaces.Wordprocessing + "val"),
            out int start) && start is >= 0 and < 100000
            ? start
            : null;
}
