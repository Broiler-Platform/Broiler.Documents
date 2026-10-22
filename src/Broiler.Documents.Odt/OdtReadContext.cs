using System;
using System.Collections.Generic;
using Broiler.Documents.Model;

namespace Broiler.Documents.Odt;

/// <summary>Everything a read needs to turn one element into document content.</summary>
/// <remarks>
/// One of these belongs to one flow of text. The body builds its own, and so
/// does every header, every footer, and every shape that keeps its text to
/// itself, which is what stops a page break stated at the end of a header
/// coming out at the top of the body. A text box read as body content is
/// body content and shares the body's, which is the same answer read the
/// other way round.
/// </remarks>
/// <param name="Page">
/// The page the body is laid out on, when the document states one: a frame
/// anchored to a page states its position from the page's edges, and the model
/// measures from the text column.
/// </param>
internal sealed record OdtReadContext(
    OdtStyles Styles,
    OdtImageLoader Images,
    OdtDocumentBuilder Builder,
    PageGeometry? Page = null)
{
    /// <summary>True for the flow of a note's own text, which cannot hold another note.</summary>
    public bool InNote { get; init; }

    /// <summary>The document's tracked changes by region id; see <see cref="OdtRevisions"/>.</summary>
    public IReadOnlyDictionary<string, OdtChangedRegion> Regions { get; init; } =
        new Dictionary<string, OdtChangedRegion>(StringComparer.Ordinal);

    private readonly List<(string Id, TextRevision Revision)> _openChanges = [];

    /// <summary>
    /// A <c>text:change-start</c>: the text from here to its
    /// <c>text:change-end</c> is the insertion or change of formatting its
    /// region records.
    /// </summary>
    public void OpenChange(string? id)
    {
        if (id is null || !Regions.TryGetValue(id, out OdtChangedRegion? region) || region.Revision.IsDeletion)
            return;

        _openChanges.Add((id, region.Revision));
        Builder.Revision = region.Revision;
    }

    /// <summary>A <c>text:change-end</c>: the change it names ends here.</summary>
    public void CloseChange(string? id)
    {
        int index = _openChanges.FindLastIndex(open => open.Id == id);
        if (index < 0)
            return;

        _openChanges.RemoveAt(index);
        Builder.Revision = _openChanges.Count > 0 ? _openChanges[^1].Revision : null;
    }

    private OdtBreak _pendingBreak;
    private readonly Dictionary<string, ListDefinition> _lastListByStyle = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ListDefinition> _listsById = new(StringComparer.Ordinal);

    /// <summary>
    /// The list a top-level <c>text:list</c> counts in: a fresh copy of its
    /// style's levels, or the list it says it continues.
    /// </summary>
    /// <remarks>
    /// Each <c>text:list</c> starts its numbering again unless it says otherwise
    /// (ODF 1.3 §5.3.1): <c>text:continue-list</c> names the <c>xml:id</c> of the
    /// list it carries on, and <c>text:continue-numbering="true"</c> carries on
    /// the last list in the same style, which is how LibreOffice reads it. A
    /// list split by a table or a heading is written that way.
    /// </remarks>
    public ListDefinition? StartList(ListDefinition? style, string? styleName, string? id, bool continueNumbering, string? continueList)
    {
        if (style is null)
            return null;

        ListDefinition? list = null;
        if (!string.IsNullOrEmpty(continueList))
            _listsById.TryGetValue(continueList, out list);
        if (list is null && continueNumbering && !string.IsNullOrEmpty(styleName))
            _lastListByStyle.TryGetValue(styleName, out list);
        list ??= style.Copy();

        if (!string.IsNullOrEmpty(id))
            _listsById[id] = list;
        if (!string.IsNullOrEmpty(styleName))
            _lastListByStyle[styleName] = list;

        return list;
    }

    /// <summary>
    /// Records that the paragraph just read asked for a page or column break
    /// after itself, so the next paragraph in this flow can carry it.
    /// </summary>
    /// <remarks>
    /// A break stated after paragraph five and a break stated before
    /// paragraph six are the same break - that identity is the reason the
    /// model holds one end of it and not both. Moving the far spelling onto
    /// the near paragraph is therefore not a guess about what the author
    /// meant; it is the only way the model has of writing down what the
    /// document already said. The alternative, reporting it and dropping it,
    /// would be honest about the loss and would still lose a page break a
    /// word processor draws. A column break stated after a paragraph travels the
    /// same way.
    /// </remarks>
    public void HoldBreak(OdtBreak kind) => _pendingBreak = kind;

    /// <summary>Hands any held break to the paragraph now starting.</summary>
    public OdtBreak TakeBreak()
    {
        OdtBreak held = _pendingBreak;
        _pendingBreak = OdtBreak.None;
        return held;
    }

    /// <summary>
    /// Gives up on a held break, because nothing follows it that the model
    /// could hang one on: the flow ended, or the next block is a table, whose
    /// page break would belong to the table rather than to a paragraph inside
    /// somebody's cell. It goes out as a diagnostic rather than quietly,
    /// since a break the reader knew about and could not place is exactly the
    /// thing a caller comparing page counts needs told.
    /// </summary>
    public void DropPageBreak()
    {
        if (_pendingBreak == OdtBreak.None)
            return;

        _pendingBreak = OdtBreak.None;
        Builder.AddDiagnosticOnce(
            "odt.break.after",
            "A fo:break-after page break had no following paragraph to carry it and was dropped.");
    }
}

/// <summary>A break one paragraph hands to the next: none, a page, or a column.</summary>
internal enum OdtBreak
{
    None,
    Page,
    Column,
}
