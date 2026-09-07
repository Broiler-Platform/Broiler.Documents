using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>
/// A document's running content: the headers and footers that repeat on the page
/// rather than flowing with the body.
/// </summary>
/// <remarks>
/// <para>
/// This sits beside <see cref="RichTextDocument.Paragraphs"/> rather than in it.
/// Flattening a header into the body flow - the trick the DOCX reader uses to
/// keep a table's text - would drop a letterhead into the middle of the letter,
/// and a round trip would then write it back there.
/// </para>
/// <para>
/// The three selections mirror what the formats actually store: DOCX names a
/// part per <c>w:type</c>, and ODT has a first-page and a left-page variant of
/// each. A document that wants one header everywhere sets only
/// <see cref="PageSelection.Default"/>.
/// </para>
/// <para>
/// A header carries shapes as well as paragraphs - a letterhead's stripe is the
/// reason the part exists at all. They are placed against the page rather than
/// against a paragraph: <see cref="DocumentShape.OffsetX"/> is read from the
/// text column's left edge as it is everywhere else, but
/// <see cref="DocumentShape.OffsetY"/> is read from the top of the page, and
/// <see cref="DocumentShape.ParagraphIndex"/> is unused. Running content repeats
/// on every page and has no paragraph of the body to hang from, which is what
/// anchoring it to one used to approximate.
/// </para>
/// </remarks>
public sealed class RunningContent
{
    private static readonly RichTextParagraph[] None = [];
    private static readonly DocumentShape[] NoShapes = [];

    private readonly RichTextParagraph[][] _headers;
    private readonly RichTextParagraph[][] _footers;
    private readonly DocumentShape[][] _headerShapes;
    private readonly DocumentShape[][] _footerShapes;

    private RunningContent(
        RichTextParagraph[][] headers,
        RichTextParagraph[][] footers,
        DocumentShape[][] headerShapes,
        DocumentShape[][] footerShapes,
        bool differentFirstPage)
    {
        _headers = headers;
        _footers = footers;
        _headerShapes = headerShapes;
        _footerShapes = footerShapes;
        DifferentFirstPage = differentFirstPage;
    }

    /// <summary>A document with no header and no footer.</summary>
    public static RunningContent Empty { get; } = new(
        [None, None, None],
        [None, None, None],
        [NoShapes, NoShapes, NoShapes],
        [NoShapes, NoShapes, NoShapes],
        differentFirstPage: false);

    /// <summary>
    /// True when the document says the first page has running content of its own,
    /// so a band the first page does not state is empty there rather than the
    /// default one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a separate fact from whether a First part exists, and a letterhead is
    /// the case that shows why. DOCX writes <c>w:titlePg</c> in the section and
    /// then a <c>first</c> header and a <c>default</c> footer; ODF puts the first
    /// page on its own master page and gives it a header and no footer. Both say
    /// the same thing - page one takes the header it names and <em>no</em> footer -
    /// and without this flag the empty First footer falls back to the default and
    /// a page number appears on a page that should carry none.
    /// </para>
    /// <para>
    /// One flag for both bands, because that is how both formats state it: a
    /// section is a title page or it is not, and a master page is the first page's
    /// or it is not. Neither has a way to say the header is special and the footer
    /// is not.
    /// </para>
    /// </remarks>
    public bool DifferentFirstPage { get; }

    /// <summary>True when nothing is set, which is the common case.</summary>
    public bool IsEmpty
    {
        get
        {
            for (int i = 0; i < _headers.Length; i++)
            {
                if (_headers[i].Length > 0 || _footers[i].Length > 0 ||
                    _headerShapes[i].Length > 0 || _footerShapes[i].Length > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The header for <paramref name="selection"/>, empty when unset.</summary>
    public IReadOnlyList<RichTextParagraph> Header(PageSelection selection) => _headers[Index(selection)];

    /// <summary>The footer for <paramref name="selection"/>, empty when unset.</summary>
    public IReadOnlyList<RichTextParagraph> Footer(PageSelection selection) => _footers[Index(selection)];

    /// <summary>The header's own shapes for <paramref name="selection"/>, empty when it has none.</summary>
    public IReadOnlyList<DocumentShape> HeaderShapes(PageSelection selection) => _headerShapes[Index(selection)];

    /// <summary>The footer's own shapes for <paramref name="selection"/>, empty when it has none.</summary>
    public IReadOnlyList<DocumentShape> FooterShapes(PageSelection selection) => _footerShapes[Index(selection)];

    /// <summary>
    /// The header that actually applies to <paramref name="selection"/>: its own
    /// if it has one, else the default. A document with a first-page header and
    /// nothing else still draws the default on page two.
    /// </summary>
    public IReadOnlyList<RichTextParagraph> EffectiveHeader(PageSelection selection) =>
        _headers[EffectiveIndex(_headers, _headerShapes, selection)];

    /// <summary>The footer that applies to <paramref name="selection"/>, falling back to the default.</summary>
    public IReadOnlyList<RichTextParagraph> EffectiveFooter(PageSelection selection) =>
        _footers[EffectiveIndex(_footers, _footerShapes, selection)];

    /// <summary>The shapes of the header that applies to <paramref name="selection"/>.</summary>
    /// <remarks>
    /// The fallback is resolved once for the whole header, so its shapes and its
    /// paragraphs always come from the same one. Resolving them apart would let a
    /// first page that states a header of its own borrow the default's stripe.
    /// </remarks>
    public IReadOnlyList<DocumentShape> EffectiveHeaderShapes(PageSelection selection) =>
        _headerShapes[EffectiveIndex(_headers, _headerShapes, selection)];

    /// <summary>The shapes of the footer that applies to <paramref name="selection"/>.</summary>
    public IReadOnlyList<DocumentShape> EffectiveFooterShapes(PageSelection selection) =>
        _footerShapes[EffectiveIndex(_footers, _footerShapes, selection)];

    public RunningContent WithHeader(
        PageSelection selection,
        IReadOnlyList<RichTextParagraph>? paragraphs,
        IReadOnlyList<DocumentShape>? shapes = null) =>
        new(
            Replace(_headers, selection, paragraphs, None, RichTextParagraph.Empty),
            _footers,
            Replace(_headerShapes, selection, shapes, NoShapes, substitute: null),
            _footerShapes,
            DifferentFirstPage);

    public RunningContent WithFooter(
        PageSelection selection,
        IReadOnlyList<RichTextParagraph>? paragraphs,
        IReadOnlyList<DocumentShape>? shapes = null) =>
        new(
            _headers,
            Replace(_footers, selection, paragraphs, None, RichTextParagraph.Empty),
            _headerShapes,
            Replace(_footerShapes, selection, shapes, NoShapes, substitute: null),
            DifferentFirstPage);

    /// <summary>
    /// The same running content with <see cref="DifferentFirstPage"/> set or
    /// cleared.
    /// </summary>
    public RunningContent WithDifferentFirstPage(bool differentFirstPage) =>
        differentFirstPage == DifferentFirstPage
            ? this
            : new(_headers, _footers, _headerShapes, _footerShapes, differentFirstPage);

    /// <summary>
    /// The selection whose header or footer actually applies: its own when it has
    /// one, else the default. A part that holds only a stripe and no words is
    /// still a part, so shapes count towards having one.
    /// </summary>
    private int EffectiveIndex(
        RichTextParagraph[][] paragraphs,
        DocumentShape[][] shapes,
        PageSelection selection)
    {
        int own = Index(selection);
        if (paragraphs[own].Length > 0 || shapes[own].Length > 0)
            return own;

        // The one case where an empty slot is an answer rather than a gap. A
        // document that states a first page of its own has said what page one
        // carries, and a band it did not state is empty there - falling back
        // would put the default footer under a letterhead that asked for none.
        if (selection == PageSelection.First && DifferentFirstPage)
            return own;

        return Index(PageSelection.Default);
    }

    /// <summary>
    /// One selection's slot replaced, copying the rest.
    /// </summary>
    /// <remarks>
    /// <paramref name="substitute"/> is what a null entry becomes. A paragraph
    /// gets an empty one, because a blank line in a header is a line and dropping
    /// it would close the gap the author left. A shape has no empty form, so a
    /// null one is dropped instead.
    /// </remarks>
    private static T[][] Replace<T>(
        T[][] set,
        PageSelection selection,
        IReadOnlyList<T>? items,
        T[] none,
        T? substitute)
        where T : class
    {
        var copy = new T[set.Length][];
        Array.Copy(set, copy, set.Length);

        if (items is null || items.Count == 0)
        {
            copy[Index(selection)] = none;
            return copy;
        }

        var replacement = new T[items.Count];
        int kept = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if ((items[i] ?? substitute) is T item)
                replacement[kept++] = item;
        }

        copy[Index(selection)] = kept == replacement.Length ? replacement : replacement[..kept];
        return copy;
    }

    private static int Index(PageSelection selection) => selection switch
    {
        PageSelection.First => 1,
        PageSelection.Even => 2,
        _ => 0,
    };
}
