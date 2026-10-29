using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.Documents.Model;

/// <summary>
/// A field that shows the text formatted with a named character style: Word's
/// <c>STYLEREF</c> naming a character style, which a dictionary's header uses to
/// show the first and the last entry word on each page.
/// </summary>
/// <remarks>
/// <para>
/// Like a <see cref="PageField"/>, the run holding the field keeps the text the
/// document last computed for it, and a header or footer laid out on a page
/// draws the page's own value instead (<see cref="Text"/>). Word saves the value
/// from wherever it last drew the header, so the saved word is right on one page
/// at most.
/// </para>
/// <para>
/// The search is Word's, and LibreOffice's since the tdf#163894 fixes (master,
/// February 2026; <c>SwGetRefFieldType::FindAnchorRefStyleMarginal</c> in
/// <c>reffld.cxx</c>). Text in the style is taken in stretches: adjacent runs in
/// the style are one stretch, and a stretch never crosses a paragraph. The page
/// is searched first, for its first stretch or, with <see cref="FromBottom"/>
/// (<c>\l</c>), its last; then back from the top of the page for the nearest
/// stretch before it; then forward from its foot. Word's style names ignore
/// case, so the three searches are made again ignoring it where the exact name
/// finds nothing. Hidden text counts: a page whose only stretch is a hidden
/// space shows a space.
/// </para>
/// <para>
/// LibreOffice before those fixes (24.2 among them) gave the whole paragraph
/// holding the first stretch, so its headers on a dictionary page hold the
/// paragraph in full.
/// </para>
/// </remarks>
/// <param name="StyleName">The character style's name, as the document's styles name it.</param>
/// <param name="FromBottom">True for <c>\l</c>: the page's last stretch rather than its first.</param>
public sealed record StyleReferenceField(string StyleName, bool FromBottom = false)
{
    /// <summary>
    /// The field's text on a page whose body runs from <paramref name="pageStart"/>
    /// up to, not including, <paramref name="pageEnd"/> in
    /// <paramref name="paragraphs"/>, or null where no text anywhere is in the
    /// style, which keeps the text the document saved. Each end is a paragraph's
    /// index and an offset in its text.
    /// </summary>
    /// <remarks>
    /// A page with no body text is given an empty range at the start of the
    /// document, which finds the document's first stretch, as LibreOffice's
    /// search from the header itself does.
    /// </remarks>
    public string? Text(IReadOnlyList<RichTextParagraph> paragraphs, (int Paragraph, int Offset) pageStart, (int Paragraph, int Offset) pageEnd)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        return Find(paragraphs, pageStart, pageEnd, StringComparison.Ordinal) ??
            Find(paragraphs, pageStart, pageEnd, StringComparison.OrdinalIgnoreCase);
    }

    private string? Find(IReadOnlyList<RichTextParagraph> paragraphs, (int Paragraph, int Offset) pageStart, (int Paragraph, int Offset) pageEnd,
        StringComparison comparison)
    {
        Stretch? onPage = null;
        Stretch? before = null;
        Stretch? after = null;
        foreach (Stretch stretch in Stretches(paragraphs, comparison))
        {
            if (Compare(stretch.Start, pageEnd) < 0 && Compare(stretch.End, pageStart) > 0)
            {
                // Any stretch on the page wins over one off it; the first or the
                // last of them by the switch.
                if (onPage is null || FromBottom)
                    onPage = stretch;
                if (!FromBottom)
                    break;
            }
            else if (Compare(stretch.End, pageStart) <= 0)
            {
                before = stretch;
            }
            else
            {
                // Past the page's foot, and so is every stretch after it.
                after = stretch;
                break;
            }
        }

        return (onPage ?? before ?? after)?.Text;
    }

    /// <summary>The stretches of text in the style, in document order.</summary>
    private IEnumerable<Stretch> Stretches(IReadOnlyList<RichTextParagraph> paragraphs, StringComparison comparison)
    {
        for (int p = 0; p < paragraphs.Count; p++)
        {
            RichTextParagraph paragraph = paragraphs[p];
            int offset = 0;
            int start = -1;
            var text = new StringBuilder();
            foreach (StyleRun run in paragraph.Runs)
            {
                int length = Math.Min(run.Length, Math.Max(0, paragraph.Length - offset));
                if (run.Style.CharacterStyle is string name && string.Equals(name, StyleName, comparison))
                {
                    if (start < 0)
                        start = offset;

                    // A picture or a note citation in the style is part of the
                    // stretch but has no text to show.
                    if (!run.Style.IsImage && !run.Style.IsNote && length > 0)
                        text.Append(paragraph.Text, offset, length);
                }
                else if (start >= 0)
                {
                    yield return new Stretch((p, start), (p, offset), text.ToString());
                    start = -1;
                    text.Clear();
                }

                offset += run.Length;
            }

            if (start >= 0)
                yield return new Stretch((p, start), (p, offset), text.ToString());
        }
    }

    private static int Compare((int Paragraph, int Offset) left, (int Paragraph, int Offset) right) =>
        left.Paragraph != right.Paragraph ? left.Paragraph.CompareTo(right.Paragraph) : left.Offset.CompareTo(right.Offset);

    private sealed record Stretch((int Paragraph, int Offset) Start, (int Paragraph, int Offset) End, string Text);
}
