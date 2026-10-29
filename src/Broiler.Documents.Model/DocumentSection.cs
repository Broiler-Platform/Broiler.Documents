using System;
using System.Collections.Generic;
using Broiler.Graphics.Color;

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
/// What a section holds here is its columns and, where it states them other than
/// the document's, its own <see cref="Page"/> - the paper and margins its text is
/// laid out within - and its own <see cref="RunningContent"/>. Where a section starts a new page, its first paragraph
/// carries <see cref="ParagraphStyle.PageBreakBefore"/>; where it does not, the
/// section follows on from the text above it on the same page, and a layout
/// balances the columns of the section above before it starts - which is what
/// a continuous section break in Word and an ODF section in LibreOffice both do.
/// </para>
/// </remarks>
public sealed class DocumentSection(
    int paragraphIndex,
    int paragraphCount,
    int columnCount,
    double columnGap,
    PageGeometry? page = null,
    RunningContent? runningContent = null,
    BColor background = default,
    PageStyleSequence? pageStyles = null,
    bool hidden = false,
    bool dontBalanceColumns = false,
    SectionBreakType breakType = SectionBreakType.Continuous,
    double leftIndent = 0,
    double rightIndent = 0,
    int? pageNumberStart = null)
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
    /// A layout takes these margins on the pages the section starts and the
    /// pages its text runs onto from there. A section that follows on the same
    /// page - a continuous break - keeps the margins of the page it is on, and
    /// its columns are spread across that page's text width, which is how
    /// LibreOffice lays it out: such a section is a text section inside the page
    /// style of the section that started the page, its own margins unused. Word
    /// sets it between its own left and right margins instead. The columns here
    /// are <see cref="ColumnCount"/> and <see cref="ColumnGap"/>, not the page's.
    /// </remarks>
    public PageGeometry? Page { get; } = page;

    /// <summary>
    /// The headers and footers the pages the section starts carry, or null where
    /// they are the document's <see cref="RichTextDocument.RunningContent"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ECMA-376 §17.10.5 gives each section its own header and footer references
    /// and §17.10.6 its own <c>w:titlePg</c>, so a report's cover section can carry
    /// a full-page title in its first-page header while the sections after it carry
    /// a running head, and its <see cref="RunningContent.DifferentFirstPage"/> is
    /// about the first page of the section, not of the document. LibreOffice gives
    /// each such section a page style of its own.
    /// </para>
    /// <para>
    /// A page carries the running content of the section that started it. A
    /// section that follows on the same page starts no page, and LibreOffice keeps
    /// the page style of the section that did on the pages its text runs onto,
    /// which is what a layout does here too.
    /// </para>
    /// </remarks>
    public RunningContent? RunningContent { get; } = runningContent;

    /// <summary>The colour behind this section's text area; empty means transparent.</summary>
    public BColor Background { get; } = background;

    /// <summary>The page style sequence applying to this section, or null when uniform.</summary>
    public PageStyleSequence? PageStyles { get; } = pageStyles;

    /// <summary>Retain this section in the model without displaying its body content.</summary>
    public bool Hidden { get; } = hidden;

    /// <summary>True when text columns in this section should not be balanced at the end of the section.</summary>
    public bool DontBalanceColumns { get; } = dontBalanceColumns;

    /// <summary>How this section starts relative to the preceding content.</summary>
    public SectionBreakType BreakType { get; } = breakType;

    /// <summary>
    /// How far the section's text stands in from the left of the text area of
    /// the page it is on, in points.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Page"/>'s margins. Those are the page's, and a section
    /// that follows on a page leaves them to the section that started it. An
    /// indent is the section's own and applies on whichever page it lands:
    /// ODF's <c>fo:margin-left</c> on a section, which LibreOffice keeps for a
    /// text section on any page - a form template sets its title in the left
    /// half of the page that way.
    /// </remarks>
    public double LeftIndent { get; } = double.IsFinite(leftIndent) ? leftIndent : 0;

    /// <summary>
    /// How far the section's text stands in from the right of the text area of
    /// the page it is on, in points. See <see cref="LeftIndent"/>.
    /// </summary>
    public double RightIndent { get; } = double.IsFinite(rightIndent) ? rightIndent : 0;

    /// <summary>
    /// The number the first page this section starts takes, or null for a
    /// section that carries the count on: ODF's <c>style:page-number</c> on the
    /// paragraph that starts the section's page style. Only a section that
    /// starts a page restarts the count.
    /// </summary>
    public int? PageNumberStart { get; } = pageNumberStart is >= 0 ? pageNumberStart : null;

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
    public DocumentSection? Shifted(int at, int removed, int inserted) =>
        Shifted(at, removed, inserted, RangeAffinity.Default);

    /// <summary>
    /// This section with its range moved for an edit that replaced
    /// <paramref name="removed"/> paragraphs at <paramref name="at"/> with
    /// <paramref name="inserted"/>, taking <paramref name="affinity"/> into account
    /// for insertions directly on section boundaries.
    /// </summary>
    public DocumentSection? Shifted(int at, int removed, int inserted, RangeAffinity affinity)
    {
        if (removed == inserted && removed == 0)
            return this;

        (int index, int count) = DocumentTable.ShiftRange(ParagraphIndex, ParagraphCount, at, removed, inserted, affinity);
        return count <= 0 ? null : new DocumentSection(index, count, ColumnCount, ColumnGap, Page, RunningContent, Background, PageStyles, Hidden, DontBalanceColumns, BreakType, LeftIndent, RightIndent, PageNumberStart);
    }

    /// <summary>
    /// Returns this section with its paragraph range shifted by a fixed offset.
    /// </summary>
    public DocumentSection ShiftedBy(int paragraphOffset)
    {
        if (paragraphOffset == 0)
            return this;

        return new DocumentSection(
            ParagraphIndex + paragraphOffset,
            ParagraphCount,
            ColumnCount,
            ColumnGap,
            Page,
            RunningContent,
            Background,
            PageStyles,
            Hidden,
            DontBalanceColumns,
            BreakType,
            LeftIndent,
            RightIndent,
            PageNumberStart);
    }

    /// <summary>
    /// Relocates this section's paragraph range when a block of paragraphs is moved.
    /// </summary>
    public DocumentSection? RelocateParagraphs(int sourceStart, int sourceCount, int destinationIndex)
    {
        (int index, int count) = DocumentTable.RelocateRange(ParagraphIndex, ParagraphCount, sourceStart, sourceCount, destinationIndex);
        return count <= 0
            ? null
            : new DocumentSection(index, count, ColumnCount, ColumnGap, Page, RunningContent, Background, PageStyles, Hidden, DontBalanceColumns, BreakType, LeftIndent, RightIndent, PageNumberStart);
    }
}
