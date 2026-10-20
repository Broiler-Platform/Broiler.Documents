using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>
/// A stretch of the body laid out in text columns of its own: the section a DOCX
/// <c>w:sectPr</c> closes, an ODF <c>text:section</c>, an RTF section.
/// </summary>
/// <remarks>
/// <para>
/// A section is a paragraph range, held the way a <see cref="DocumentTable"/>
/// holds one, and moved by the same rule when an edit changes the paragraph
/// count around or inside it. Body paragraphs that no section covers take the
/// columns of the document's <see cref="PageGeometry"/>.
/// </para>
/// <para>
/// What a section holds here is its columns and, where it states one other than
/// the document's, its own <see cref="Page"/>: the paper and margins its text is
/// laid out within. Its running content is the document's; a reader that meets a
/// section stating its own says so. Where a section starts a new page, its first paragraph
/// carries <see cref="ParagraphStyle.PageBreakBefore"/>; where it does not, the
/// section follows on from the text above it on the same page, and a layout
/// balances the columns of the section above before it starts - which is what
/// a continuous section break in Word and an ODF section in LibreOffice both do.
/// </para>
/// </remarks>
public sealed class DocumentSection(
    int paragraphIndex, int paragraphCount, int columnCount, double columnGap, PageGeometry? page = null)
{
    /// <summary>The first paragraph the section holds.</summary>
    public int ParagraphIndex { get; } = Math.Max(0, paragraphIndex);

    /// <summary>How many consecutive paragraphs, from <see cref="ParagraphIndex"/>, the section holds.</summary>
    public int ParagraphCount { get; } = Math.Max(0, paragraphCount);

    /// <summary>The paragraph just past the section.</summary>
    public int ParagraphEnd => ParagraphIndex + ParagraphCount;

    /// <summary>How many text columns the section is divided into; at least one.</summary>
    public int ColumnCount { get; } = Math.Max(1, columnCount);

    /// <summary>Points between adjacent columns.</summary>
    public double ColumnGap { get; } = double.IsFinite(columnGap) && columnGap > 0 ? columnGap : 0;

    /// <summary>
    /// The paper and margins the section states, or null where they are the
    /// document's <see cref="RichTextDocument.PageGeometry"/>.
    /// </summary>
    /// <remarks>
    /// A layout takes the left and right margins for the section's text wherever
    /// it is, and the top and bottom margins on a page the section starts, as
    /// Word does: a section that follows on the same page shares that page's
    /// top and foot. The columns here are <see cref="ColumnCount"/> and
    /// <see cref="ColumnGap"/>, not the page's.
    /// </remarks>
    public PageGeometry? Page { get; } = page;

    /// <summary>True when <paramref name="paragraphIndex"/> is one of the section's paragraphs.</summary>
    public bool Contains(int paragraphIndex) => paragraphIndex >= ParagraphIndex && paragraphIndex < ParagraphEnd;

    /// <summary>
    /// The section that holds <paramref name="paragraphIndex"/>, or null when none
    /// does and the page's own columns apply.
    /// </summary>
    public static DocumentSection? Containing(IReadOnlyList<DocumentSection> sections, int paragraphIndex)
    {
        ArgumentNullException.ThrowIfNull(sections);
        foreach (DocumentSection section in sections)
        {
            if (section.Contains(paragraphIndex))
                return section;
        }

        return null;
    }

    /// <summary>
    /// This section with its range moved for an edit that replaced
    /// <paramref name="removed"/> paragraphs at <paramref name="at"/> with
    /// <paramref name="inserted"/>, or null when the edit removed every paragraph
    /// it held.
    /// </summary>
    public DocumentSection? Shifted(int at, int removed, int inserted)
    {
        if (removed == inserted)
            return this;

        (int index, int count) = DocumentTable.ShiftRange(ParagraphIndex, ParagraphCount, at, removed, inserted);
        return count <= 0 ? null : new DocumentSection(index, count, ColumnCount, ColumnGap, Page);
    }
}
