using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Odt;

/// <summary>
/// One <c>text:changed-region</c> of ODF 1.3 §5.5: the change it records and,
/// for a deletion, the content it deleted.
/// </summary>
/// <param name="Revision">What the change did, who made it and when.</param>
/// <param name="Deleted">The block content a deletion removed, in order; empty for any other change.</param>
internal sealed record OdtChangedRegion(TextRevision Revision, IReadOnlyList<XElement> Deleted);

/// <summary>
/// The tracked changes of ODF 1.3 §5.5: the regions <c>text:tracked-changes</c>
/// lists, and whether the document says they are shown.
/// </summary>
/// <remarks>
/// <para>
/// An insertion or a change of formatting is marked in the body by a
/// <c>text:change-start</c> and a <c>text:change-end</c> naming its region; the
/// text between them is the change. A deletion is marked by one
/// <c>text:change</c> where the text was, and the text itself is kept in its
/// region - as paragraphs, so a deletion that crossed a paragraph break holds
/// two, and the first and last join the text on either side of the mark.
/// </para>
/// <para>
/// The reader puts the deleted text back where its mark is, as LibreOffice does,
/// so a document shown with its changes draws it struck through.
/// </para>
/// </remarks>
internal static class OdtRevisions
{
    /// <summary>The regions <paramref name="body"/>'s <c>text:tracked-changes</c> lists, by id.</summary>
    public static IReadOnlyDictionary<string, OdtChangedRegion> ReadRegions(XElement? body)
    {
        var regions = new Dictionary<string, OdtChangedRegion>(StringComparer.Ordinal);
        XElement? tracked = body?.Element(OdtNamespaces.Text + "tracked-changes");
        if (tracked is null)
            return regions;

        foreach (XElement region in tracked.Elements(OdtNamespaces.Text + "changed-region"))
        {
            string? id = (string?)region.Attribute(OdtNamespaces.Text + "id") ??
                (string?)region.Attribute(OdtNamespaces.Xml + "id");
            XElement? change = region.Elements().FirstOrDefault();
            if (string.IsNullOrEmpty(id) || change is null || change.Name.Namespace != OdtNamespaces.Text)
                continue;

            RevisionKind? kind = change.Name.LocalName switch
            {
                "insertion" => RevisionKind.Insertion,
                "deletion" => RevisionKind.Deletion,
                "format-change" => RevisionKind.Format,
                _ => null,
            };
            if (kind is not RevisionKind known)
                continue;

            XElement? info = change.Element(OdtNamespaces.Office + "change-info");
            string? author = info?.Element(OdtNamespaces.DublinCore + "creator")?.Value.Trim();
            DateTimeOffset? date = DateTimeOffset.TryParse(
                info?.Element(OdtNamespaces.DublinCore + "date")?.Value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed)
                ? parsed
                : null;

            IReadOnlyList<XElement> deleted = known == RevisionKind.Deletion
                ? [.. change.Elements().Where(element => element.Name != OdtNamespaces.Office + "change-info")]
                : [];
            regions[id] = new OdtChangedRegion(
                new TextRevision(known, string.IsNullOrEmpty(author) ? null : author, date),
                deleted);
        }

        return regions;
    }

    /// <summary>
    /// Whether the document shows its changes: false where its settings say
    /// <c>ShowRedlineChanges</c> is off, as LibreOffice writes it.
    /// </summary>
    public static bool Shown(XElement? settings)
    {
        if (settings is null)
            return true;

        foreach (XElement item in settings.Descendants(OdtNamespaces.Config + "config-item"))
        {
            if ((string?)item.Attribute(OdtNamespaces.Config + "name") == "ShowRedlineChanges")
                return !string.Equals(item.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
