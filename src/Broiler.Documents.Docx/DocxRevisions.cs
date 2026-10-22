using System;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// The tracked changes of ECMA-376 §17.13.5: which elements mark one, who made
/// it and when, and whether the document says they are shown.
/// </summary>
/// <remarks>
/// <c>w:ins</c> and <c>w:del</c> wrap the runs they inserted and deleted, a
/// deleted run's text in <c>w:delText</c>; <c>w:moveTo</c> and
/// <c>w:moveFrom</c> are the two sides of a move; <c>w:rPrChange</c> in a run's
/// properties records that its formatting changed; and the same <c>w:ins</c>,
/// <c>w:del</c>, <c>w:moveTo</c> and <c>w:moveFrom</c> in a paragraph mark's
/// <c>w:rPr</c> mark the mark itself. The reader keeps every one of them, the
/// deleted text included, as LibreOffice does.
/// </remarks>
internal static class DocxRevisions
{
    /// <summary>
    /// The change <paramref name="element"/> records, or null when it is not one
    /// of <c>w:ins</c>, <c>w:del</c>, <c>w:moveTo</c> or <c>w:moveFrom</c>.
    /// </summary>
    public static TextRevision? Of(XElement element)
    {
        if (element.Name.Namespace != DocxNamespaces.Wordprocessing)
            return null;

        return element.Name.LocalName switch
        {
            "ins" => Read(element, RevisionKind.Insertion, moved: false),
            "del" => Read(element, RevisionKind.Deletion, moved: false),
            "moveTo" => Read(element, RevisionKind.Insertion, moved: true),
            "moveFrom" => Read(element, RevisionKind.Deletion, moved: true),
            _ => null,
        };
    }

    /// <summary>The change a run's or a mark's <c>w:rPr</c> records: an insertion or deletion of the mark, else a change of formatting.</summary>
    public static TextRevision? OfProperties(XElement? rPr, bool includeFormat)
    {
        if (rPr is null)
            return null;

        foreach (XElement child in rPr.Elements())
        {
            if (Of(child) is TextRevision revision)
                return revision;
        }

        XElement? change = includeFormat ? rPr.Element(DocxNamespaces.Wordprocessing + "rPrChange") : null;
        return change is null ? null : Read(change, RevisionKind.Format, moved: false);
    }

    /// <summary>
    /// The change of paragraph properties a <c>w:pPr</c> records in its
    /// <c>w:pPrChange</c>, as a change of formatting, or null.
    /// </summary>
    public static TextRevision? OfParagraphProperties(XElement? pPr)
    {
        XElement? change = pPr?.Element(DocxNamespaces.Wordprocessing + "pPrChange");
        return change is null ? null : Read(change, RevisionKind.Format, moved: false);
    }

    /// <summary>
    /// Whether the document shows its changes. LibreOffice hides them when
    /// <c>w:revisionView</c> turns off insertions and deletions or markup, unless
    /// it turns formatting on (writerfilter's SettingsTable).
    /// </summary>
    public static bool Shown(XElement? settings)
    {
        XElement? view = settings?.Element(DocxNamespaces.Wordprocessing + "revisionView");
        if (view is null)
            return true;

        bool hidden = IsOff(view, "insDel") || IsOff(view, "markup");
        return !hidden || IsOn(view, "formatting");
    }

    private static TextRevision Read(XElement element, RevisionKind kind, bool moved)
    {
        string? author = (string?)element.Attribute(DocxNamespaces.Wordprocessing + "author");
        DateTimeOffset? date = DateTimeOffset.TryParse(
            (string?)element.Attribute(DocxNamespaces.Wordprocessing + "date"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
        return new TextRevision(kind, string.IsNullOrEmpty(author) ? null : author, date, moved);
    }

    private static bool IsOff(XElement view, string name) =>
        (string?)view.Attribute(DocxNamespaces.Wordprocessing + name) is "0" or "false" or "off";

    private static bool IsOn(XElement view, string name) =>
        (string?)view.Attribute(DocxNamespaces.Wordprocessing + name) is "1" or "true" or "on";
}
