using System;
using System.Collections.Generic;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>What a tracked change did to the text it marks.</summary>
public enum RevisionKind
{
    /// <summary>The text was inserted.</summary>
    Insertion = 0,

    /// <summary>The text was deleted, and is kept so it can be shown struck through.</summary>
    Deletion,

    /// <summary>The text's formatting was changed. What it was before is not kept.</summary>
    Format,
}

/// <summary>
/// One tracked change: what it did, who made it and when. Carried by the runs it
/// marks through <see cref="InlineStyle.Revision"/>, and by a paragraph mark
/// through its <see cref="RichTextParagraph.MarkStyle"/>.
/// </summary>
/// <remarks>
/// <para>
/// A deleted run is still in the paragraph, as it is in a DOCX or ODT file and
/// in LibreOffice's model: a document shown with its changes draws it struck
/// through, and one that hides them leaves it out
/// (<see cref="RichTextDocument.ShowsRevisions"/>, <see cref="RevisionDisplay"/>).
/// A deleted paragraph mark joins its paragraph to the next once the change is
/// hidden or accepted.
/// </para>
/// <para>
/// A record, so two runs marked by equal changes are one run. Text moved from
/// one place to another is a deletion where it was and an insertion where it
/// went, each <see cref="Moved"/>.
/// </para>
/// </remarks>
/// <param name="Kind">What the change did.</param>
/// <param name="Author">Who made it, or null when the document does not say.</param>
/// <param name="Date">When, or null when the document does not say.</param>
/// <param name="Moved">True when the change is one side of a move.</param>
public sealed record TextRevision(RevisionKind Kind, string? Author = null, DateTimeOffset? Date = null, bool Moved = false)
{
    /// <summary>True for a deletion.</summary>
    public bool IsDeletion => Kind == RevisionKind.Deletion;
}

/// <summary>
/// How a document's tracked changes are drawn: LibreOffice's defaults, which a
/// renderer and the PDF writer both follow.
/// </summary>
/// <remarks>
/// <para>
/// Inserted text is underlined and deleted text struck through, both in the
/// colour of the change's author; text whose formatting changed is bold in it.
/// Moved text is dark green, its line doubled. The authors take LibreOffice's
/// nine author colours in the order they first appear in the document, which is
/// the order LibreOffice registers them as it reads a file (Writer.xcs
/// Revision/TextDisplay, colorcfg.cxx AUTHOR1-9, redlnitr.cxx).
/// </para>
/// <para>
/// A document that says its changes are not shown - ODT's
/// <c>ShowRedlineChanges</c> off, DOCX's <c>w:revisionView w:insDel="0"</c> - is
/// drawn as LibreOffice draws it then: insertions as plain text and deletions
/// not at all.
/// </para>
/// </remarks>
public sealed class RevisionDisplay
{
    /// <summary>LibreOffice's default author colours, AUTHOR1 to AUTHOR9.</summary>
    private static readonly BColor[] AuthorColors =
    [
        new(0xC6, 0x92, 0x00),
        new(0x06, 0x46, 0xA2),
        new(0x57, 0x9D, 0x1C),
        new(0x69, 0x2B, 0x9D),
        new(0xC5, 0x00, 0x0B),
        new(0x00, 0x80, 0x80),
        new(0x8C, 0x84, 0x00),
        new(0x35, 0x55, 0x6B),
        new(0xD1, 0x76, 0x00),
    ];

    /// <summary>The colour of moved text, LibreOffice's COL_GREEN.</summary>
    public static readonly BColor MovedColor = new(0x00, 0x80, 0x00);

    private readonly Dictionary<string, int> _authors = new(StringComparer.Ordinal);

    private RevisionDisplay(bool shows) => Shows = shows;

    /// <summary>A display that draws nothing as a change, for a document that has none.</summary>
    public static RevisionDisplay None { get; } = new(shows: false);

    /// <summary>True when changes are drawn as changes; false when the text is drawn as it would read with them hidden.</summary>
    public bool Shows { get; }

    /// <summary>
    /// The display for <paramref name="document"/>: its authors numbered in the
    /// order they first appear in the body, its notes, and its headers and
    /// footers.
    /// </summary>
    public static RevisionDisplay For(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var display = new RevisionDisplay(document.ShowsRevisions);
        if (!document.ShowsRevisions)
            return display;

        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            display.Register(paragraph);
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.Note is DocumentNote note)
                {
                    foreach (RichTextParagraph noteParagraph in note.Paragraphs)
                        display.Register(noteParagraph);
                }
            }
        }

        return display;
    }

    private void Register(RichTextParagraph paragraph)
    {
        foreach (StyleRun run in paragraph.Runs)
            Register(run.Style.Revision);
        Register(paragraph.MarkStyle.Revision);
    }

    private void Register(TextRevision? revision)
    {
        string author = revision?.Author ?? string.Empty;
        if (revision is not null && !_authors.ContainsKey(author))
            _authors.Add(author, _authors.Count);
    }

    /// <summary>The colour <paramref name="revision"/>'s author draws in.</summary>
    public BColor ColorOf(TextRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.Moved && revision.Kind != RevisionKind.Format)
            return MovedColor;

        string author = revision.Author ?? string.Empty;
        if (!_authors.TryGetValue(author, out int index))
        {
            index = _authors.Count;
            _authors.Add(author, index);
        }

        return AuthorColors[index % AuthorColors.Length];
    }

    /// <summary>
    /// <paramref name="style"/> as it is drawn: underlined, struck through or
    /// bold, in its author's colour, where it is a change and changes are shown;
    /// unchanged otherwise. <paramref name="doubled"/> is true where the line is
    /// drawn twice, for moved text.
    /// </summary>
    public InlineStyle Decorate(InlineStyle style, out bool doubled)
    {
        doubled = false;
        if (!Shows || style.Revision is not TextRevision revision)
            return style;

        BColor color = ColorOf(revision);
        doubled = revision.Moved && revision.Kind != RevisionKind.Format;
        return revision.Kind switch
        {
            RevisionKind.Insertion => style with { Underline = true, Foreground = color },
            RevisionKind.Deletion => style with { Strikethrough = true, Foreground = color },
            _ => style with { Bold = true, Foreground = color },
        };
    }

    /// <summary>
    /// <paramref name="document"/> with its deletions taken out, when it says its
    /// changes are not shown; the document itself otherwise. See
    /// <see cref="WithoutDeletions"/>.
    /// </summary>
    public static RichTextDocument HideDeletions(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.ShowsRevisions ? document : WithoutDeletions(document);
    }

    /// <summary>True when any run or paragraph mark of the body is part of a tracked change.</summary>
    public static bool HasRevisions(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            if (paragraph.MarkStyle.Revision is not null)
                return true;
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.Revision is not null)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="document"/> as it reads with its changes accepted: its
    /// deletions taken out, which is also how it is drawn when it hides them.
    /// Inserted text stays, still marked. The document itself when it has no
    /// deletion.
    /// </summary>
    /// <remarks>
    /// A deleted run is removed, and so is a table every word of which was
    /// deleted. A paragraph whose mark is deleted joins the next paragraph in
    /// the same flow, keeping its own paragraph properties as LibreOffice does
    /// when it hides a change, and is removed whole when nothing of it is left,
    /// handing a page or column break it opened to the paragraph after it. A
    /// mark that is the last in its table cell or its document stays, because
    /// there is nothing in the same flow to join.
    /// </remarks>
    public static RichTextDocument WithoutDeletions(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!HasDeletion(document))
            return document;

        // A table every word of which was deleted goes whole, grid and all.
        RichTextDocument result = document;
        for (int t = document.Tables.Count - 1; t >= 0; t--)
        {
            DocumentTable table = document.Tables[t];
            if (IsWhollyDeleted(document, table.ParagraphIndex, table.ParagraphCount))
                result = result.RemoveParagraphs(table.ParagraphIndex, table.ParagraphCount);
        }

        var boundaries = new HashSet<int>();
        foreach (DocumentTable table in result.Tables)
            AddFlowBoundaries(table, boundaries);

        for (int index = result.ParagraphCount - 1; index >= 0; index--)
        {
            RichTextParagraph paragraph = result.Paragraphs[index];
            RichTextParagraph kept = WithoutDeletedRuns(paragraph);
            bool markDeleted = paragraph.MarkStyle.Revision?.IsDeletion == true;
            bool canJoin = index + 1 < result.ParagraphCount && !boundaries.Contains(index + 1);

            if (markDeleted && canJoin && kept.Length == 0)
            {
                // A page or column the paragraph started is still started, by
                // the paragraph that takes its place.
                RichTextParagraph next = result.Paragraphs[index + 1];
                ParagraphStyle style = paragraph.Style;
                if (style.PageBreakBefore && !next.Style.PageBreakBefore)
                    next = next.WithParagraphStyle(next.Style with { PageBreakBefore = true, ColumnBreakBefore = false });
                else if (style.ColumnBreakBefore && !next.Style.PageBreakBefore && !next.Style.ColumnBreakBefore)
                    next = next.WithParagraphStyle(next.Style with { ColumnBreakBefore = true });

                result = result.ReplaceParagraph(index + 1, next).RemoveParagraphs(index, 1);
                continue;
            }

            if (!ReferenceEquals(kept, paragraph))
                result = result.ReplaceParagraph(index, kept);

            if (markDeleted && canJoin)
                result = result.MergeParagraphs(index).Document;
        }

        return result;
    }

    /// <summary>True when every run and every mark of the paragraphs in the range was deleted.</summary>
    private static bool IsWhollyDeleted(RichTextDocument document, int index, int count)
    {
        if (count <= 0 || index < 0 || index + count > document.ParagraphCount)
            return false;

        for (int i = index; i < index + count; i++)
        {
            RichTextParagraph paragraph = document.Paragraphs[i];
            if (paragraph.MarkStyle.Revision?.IsDeletion != true)
                return false;
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Length > 0 && run.Style.Revision?.IsDeletion != true)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// U+2029 PARAGRAPH SEPARATOR, which stands for a deleted paragraph break
    /// in a document <see cref="JoinDeletedBreaks"/> returns.
    /// </summary>
    public const char DeletedBreak = (char)0x2029;

    /// <summary>
    /// <paramref name="document"/> with each paragraph whose break a change
    /// deleted joined to the next, the break kept in the deleted text as
    /// <see cref="DeletedBreak"/>.
    /// </summary>
    /// <remarks>
    /// For a writer whose format holds a deleted break inside the deletion
    /// rather than between paragraphs - ODT, whose body is the text with its
    /// deletions taken out. The joined paragraph keeps the first one's
    /// properties and the last one's mark. A break that is the last in its
    /// table cell or in the document has nothing in the same flow to join, and
    /// stays a break.
    /// </remarks>
    public static RichTextDocument JoinDeletedBreaks(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var boundaries = new HashSet<int>();
        foreach (DocumentTable table in document.Tables)
            AddFlowBoundaries(table, boundaries);

        RichTextDocument result = document;
        for (int index = document.ParagraphCount - 2; index >= 0; index--)
        {
            RichTextParagraph paragraph = result.Paragraphs[index];
            if (paragraph.MarkStyle.Revision is not { Kind: RevisionKind.Deletion } deletion || boundaries.Contains(index + 1))
                continue;

            RichTextParagraph next = result.Paragraphs[index + 1];
            InlineStyle separator = paragraph.MarkStyle with { Revision = deletion, Image = null, Note = null, LinkHref = null };
            RichTextParagraph joined = paragraph
                .InsertText(paragraph.Length, DeletedBreak.ToString(), separator)
                .Append(next)
                .WithMarkStyle(next.MarkStyle);
            result = result.ReplaceParagraph(index, joined).RemoveParagraphs(index + 1, 1);
        }

        return result;
    }

    private static bool HasDeletion(RichTextDocument document)
    {
        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            if (paragraph.MarkStyle.Revision?.IsDeletion == true)
                return true;
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.Revision?.IsDeletion == true)
                    return true;
            }
        }

        return false;
    }

    /// <summary>The paragraph without its deleted runs; the paragraph itself when it has none.</summary>
    private static RichTextParagraph WithoutDeletedRuns(RichTextParagraph paragraph)
    {
        RichTextParagraph result = paragraph;
        int end = paragraph.Length;
        for (int i = paragraph.Runs.Count - 1; i >= 0; i--)
        {
            StyleRun run = paragraph.Runs[i];
            int start = end - run.Length;
            if (run.Style.Revision?.IsDeletion == true && run.Length > 0)
                result = result.RemoveRange(Math.Max(0, start), Math.Min(run.Length, end - Math.Max(0, start)));
            end = start;
        }

        return result;
    }

    /// <summary>
    /// The paragraph indexes a join must not cross: where a table and each of its
    /// cells start and end, so a hidden deletion never pulls text into or out of
    /// a cell.
    /// </summary>
    private static void AddFlowBoundaries(DocumentTable table, HashSet<int> boundaries)
    {
        boundaries.Add(table.ParagraphIndex);
        boundaries.Add(table.ParagraphIndex + table.ParagraphCount);
        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
            {
                boundaries.Add(cell.ParagraphIndex);
                boundaries.Add(cell.ParagraphIndex + cell.ParagraphCount);
                foreach (DocumentTable nested in cell.Tables)
                    AddFlowBoundaries(nested, boundaries);
            }
        }
    }
}
