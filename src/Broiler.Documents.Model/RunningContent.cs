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
/// </remarks>
public sealed class RunningContent
{
    private static readonly RichTextParagraph[] None = [];

    private readonly RichTextParagraph[][] _headers;
    private readonly RichTextParagraph[][] _footers;

    private RunningContent(RichTextParagraph[][] headers, RichTextParagraph[][] footers)
    {
        _headers = headers;
        _footers = footers;
    }

    /// <summary>A document with no header and no footer.</summary>
    public static RunningContent Empty { get; } = new(
        [None, None, None],
        [None, None, None]);

    /// <summary>True when nothing is set, which is the common case.</summary>
    public bool IsEmpty
    {
        get
        {
            for (int i = 0; i < _headers.Length; i++)
            {
                if (_headers[i].Length > 0 || _footers[i].Length > 0)
                    return false;
            }

            return true;
        }
    }

    /// <summary>The header for <paramref name="selection"/>, empty when unset.</summary>
    public IReadOnlyList<RichTextParagraph> Header(PageSelection selection) => _headers[Index(selection)];

    /// <summary>The footer for <paramref name="selection"/>, empty when unset.</summary>
    public IReadOnlyList<RichTextParagraph> Footer(PageSelection selection) => _footers[Index(selection)];

    /// <summary>
    /// The header that actually applies to <paramref name="selection"/>: its own
    /// if it has one, else the default. A document with a first-page header and
    /// nothing else still draws the default on page two.
    /// </summary>
    public IReadOnlyList<RichTextParagraph> EffectiveHeader(PageSelection selection) =>
        Effective(_headers, selection);

    /// <summary>The footer that applies to <paramref name="selection"/>, falling back to the default.</summary>
    public IReadOnlyList<RichTextParagraph> EffectiveFooter(PageSelection selection) =>
        Effective(_footers, selection);

    public RunningContent WithHeader(PageSelection selection, IReadOnlyList<RichTextParagraph>? paragraphs) =>
        new(Replace(_headers, selection, paragraphs), _footers);

    public RunningContent WithFooter(PageSelection selection, IReadOnlyList<RichTextParagraph>? paragraphs) =>
        new(_headers, Replace(_footers, selection, paragraphs));

    private static IReadOnlyList<RichTextParagraph> Effective(RichTextParagraph[][] set, PageSelection selection)
    {
        RichTextParagraph[] own = set[Index(selection)];
        return own.Length > 0 ? own : set[Index(PageSelection.Default)];
    }

    private static RichTextParagraph[][] Replace(
        RichTextParagraph[][] set,
        PageSelection selection,
        IReadOnlyList<RichTextParagraph>? paragraphs)
    {
        var copy = new RichTextParagraph[set.Length][];
        Array.Copy(set, copy, set.Length);

        if (paragraphs is null || paragraphs.Count == 0)
        {
            copy[Index(selection)] = None;
            return copy;
        }

        var replacement = new RichTextParagraph[paragraphs.Count];
        for (int i = 0; i < paragraphs.Count; i++)
            replacement[i] = paragraphs[i] ?? RichTextParagraph.Empty;

        copy[Index(selection)] = replacement;
        return copy;
    }

    private static int Index(PageSelection selection) => selection switch
    {
        PageSelection.First => 1,
        PageSelection.Even => 2,
        _ => 0,
    };
}
