using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.Documents.Model;

/// <summary>Where a note's text is set: at the foot of the page that cites it, or after the body.</summary>
public enum NoteKind
{
    /// <summary>Set at the foot of the page its citation is on.</summary>
    Footnote = 0,

    /// <summary>Set after the body, in the order cited.</summary>
    Endnote,
}

/// <summary>
/// A footnote or an endnote: the paragraphs of its text, held by the one
/// character in the body that cites it.
/// </summary>
/// <remarks>
/// <para>
/// The citation is a run of exactly one <see cref="Placeholder"/> whose
/// <see cref="InlineStyle.Note"/> is the note, as an image is a run of one
/// placeholder whose <see cref="InlineStyle.Image"/> is the image. The note
/// travels with its citation: cutting the sentence that cites it and pasting it
/// elsewhere moves the note, and deleting the citation deletes the note, which
/// is what a word processor does.
/// </para>
/// <para>
/// The citation's label is not stored. Notes are numbered in the order they are
/// cited (<see cref="NoteNumberer"/>), so a note inserted before another
/// renumbers it, as Word and LibreOffice renumber it. A note whose author typed
/// a label of their own - an asterisk, a dagger - keeps it in
/// <see cref="CustomMark"/> and takes no number.
/// </para>
/// <para>
/// The text is paragraphs and nothing else: a table, a picture or a shape in a
/// note is not held. The first paragraph does not hold the label either; DOCX
/// repeats it there as a <c>w:footnoteRef</c> and ODT does not, and whoever
/// draws or writes the note puts it back.
/// </para>
/// </remarks>
public sealed class DocumentNote
{
    /// <summary>
    /// U+FFFC OBJECT REPLACEMENT CHARACTER, the one character a citation run
    /// holds. The same character an image run holds; the run's style says which.
    /// </summary>
    public const char Placeholder = InlineImage.Placeholder;

    /// <summary><see cref="Placeholder"/> as a string, for insertion calls.</summary>
    public const string PlaceholderText = InlineImage.PlaceholderText;

    /// <summary>Creates a note.</summary>
    /// <param name="kind">Whether it is a footnote or an endnote.</param>
    /// <param name="paragraphs">Its text. An empty list becomes one empty paragraph.</param>
    /// <param name="customMark">The label its author typed, or null to number it.</param>
    public DocumentNote(NoteKind kind, IReadOnlyList<RichTextParagraph> paragraphs, string? customMark = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        Kind = kind;
        Paragraphs = paragraphs.Count == 0 ? [RichTextParagraph.Empty] : [.. paragraphs];
        CustomMark = string.IsNullOrEmpty(customMark) ? null : customMark;
    }

    public NoteKind Kind { get; }

    /// <summary>The note's text, never empty.</summary>
    public IReadOnlyList<RichTextParagraph> Paragraphs { get; }

    /// <summary>The label the author chose, or null for a note that is numbered.</summary>
    public string? CustomMark { get; }

    /// <summary>The note's paragraphs joined with newlines.</summary>
    public string PlainText
    {
        get
        {
            var builder = new StringBuilder();
            for (int i = 0; i < Paragraphs.Count; i++)
            {
                if (i > 0)
                    builder.Append('\n');
                builder.Append(Paragraphs[i].Text);
            }

            return builder.ToString();
        }
    }

    /// <summary>The notes cited in <paramref name="paragraphs"/>, in the order they are cited.</summary>
    public static IReadOnlyList<DocumentNote> CitedIn(IEnumerable<RichTextParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var notes = new List<DocumentNote>();
        foreach (RichTextParagraph paragraph in paragraphs)
        {
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.Note is DocumentNote note)
                    notes.Add(note);
            }
        }

        return notes;
    }
}

/// <summary>
/// How a document numbers and labels its notes: the format and first number of
/// each kind, and how the label at the head of a note's text is set. The
/// default is LibreOffice's: footnotes 1, 2, 3, endnotes i, ii, iii, and the
/// label at the head of the note at the text's own size.
/// </summary>
/// <param name="FootnoteFormat">The format a footnote's number is written in.</param>
/// <param name="FootnoteStart">The first footnote's number.</param>
/// <param name="EndnoteFormat">The format an endnote's number is written in.</param>
/// <param name="EndnoteStart">The first endnote's number.</param>
/// <summary>Where footnote numbering restarts.</summary>
public enum NoteNumberingRestart
{
    /// <summary>Numbering continues across the whole document.</summary>
    Continuous,

    /// <summary>Numbering restarts at each page.</summary>
    EachPage,

    /// <summary>Numbering restarts at each section.</summary>
    EachSection,
}

/// <summary>Where footnotes are placed.</summary>
public enum FootnotePosition
{
    /// <summary>At the bottom of each page.</summary>
    PageBottom,

    /// <summary>Beneath the text of the page.</summary>
    BeneathText,

    /// <summary>Collected at the end of the section or document.</summary>
    DocumentEnd,
}

/// <param name="RaisedLabels">
/// True when the label at the head of a note's text is raised like its
/// citation and the text follows it on the line, as Word sets a note; false
/// for LibreOffice's, a label at the text's size in a hanging indent.
/// </param>
/// <param name="FootnoteRestart">Where footnote numbering restarts.</param>
/// <param name="FootnotePosition">Where footnotes are placed.</param>
public sealed record NoteNumbering(
    ListNumberFormat FootnoteFormat = ListNumberFormat.Decimal,
    int FootnoteStart = 1,
    ListNumberFormat EndnoteFormat = ListNumberFormat.LowerRoman,
    int EndnoteStart = 1,
    bool RaisedLabels = false,
    NoteNumberingRestart FootnoteRestart = NoteNumberingRestart.Continuous,
    FootnotePosition FootnotePosition = FootnotePosition.PageBottom)
{
    /// <summary>The numbering a document that states none takes.</summary>
    public static NoteNumbering Default { get; } = new();
}

/// <summary>
/// Labels notes in the order they are cited: each kind counted on its own from
/// its start, a note with a label of its own given that and not counted.
/// </summary>
/// <remarks>
/// Everything that shows a label - the page, the PDF, a writer that has to
/// write one, the text dump - labels a body through one of these, so they agree
/// on what the third footnote is called. A note cited twice keeps its first
/// label.
/// </remarks>
public sealed class NoteNumberer
{
    private readonly NoteNumbering _numbering;
    private readonly Dictionary<DocumentNote, string> _labels = new(ReferenceEqualityComparer.Instance);
    private int _footnotes;
    private int _endnotes;

    public NoteNumberer(NoteNumbering? numbering = null) => _numbering = numbering ?? NoteNumbering.Default;

    /// <summary>
    /// A numberer that has already labelled every note cited in
    /// <paramref name="paragraphs"/>, in order.
    /// </summary>
    public static NoteNumberer For(IEnumerable<RichTextParagraph> paragraphs, NoteNumbering? numbering = null)
    {
        var numberer = new NoteNumberer(numbering);
        foreach (DocumentNote note in DocumentNote.CitedIn(paragraphs))
            numberer.Label(note);

        return numberer;
    }

    /// <summary>A labelled note's label, or labels it now as the next of its kind.</summary>
    public string Label(DocumentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (_labels.TryGetValue(note, out string? label))
            return label;

        if (note.CustomMark is string mark)
        {
            label = mark;
        }
        else if (note.Kind == NoteKind.Endnote)
        {
            label = ListNumberer.Format(_numbering.EndnoteStart + _endnotes, _numbering.EndnoteFormat);
            _endnotes++;
        }
        else
        {
            label = ListNumberer.Format(_numbering.FootnoteStart + _footnotes, _numbering.FootnoteFormat);
            _footnotes++;
        }

        _labels.Add(note, label);
        return label;
    }
}
