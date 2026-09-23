using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Groups assembled lines into model paragraphs.
/// </summary>
/// <remarks>
/// <para>
/// The rules are spacing, indentation, and list markers, in that order. A gap
/// noticeably larger than the block's own line spacing ends a paragraph; so does
/// a first-line indent, a line that ends far short of the block's right edge, and
/// a line that begins with a list marker. Each rule is a heuristic over geometry,
/// which is why the reader reports that reading order was inferred.
/// </para>
/// <para>
/// <strong>The block is the lines stacked with it, not the page.</strong> A line
/// is short against the lines above and below it in the same column. Measured
/// against the page, every line of a letter set beside a narrower column of
/// times was "short", and the letter came back one paragraph per line. Where a
/// tagged document declared which block each line belongs to, the blocks are the
/// paragraphs, and none of the geometry is asked.
/// </para>
/// <para>
/// <strong>A marker ends a paragraph; only a sequence makes a list.</strong> A
/// line that begins "D. Carter" or "1. Halbjahr" starts a new paragraph, as
/// any marker-shaped line does. Turning it into a list item is a different claim,
/// and a costly one when it is wrong: the marker is taken out of the text, and
/// the model numbers the list itself. So a numbered marker becomes a list item
/// only where consecutive paragraphs count 1, 2, 3 - the one numbering the model
/// reproduces exactly - and a letter never does, because a lettered list and a
/// run of initials are the same shape. Anything else keeps its marker as text,
/// which loses nothing. Bullets are unambiguous and stay lists.
/// </para>
/// <para>
/// Source page boundaries are extraction boundaries by default, not layout: a
/// caller must ask for page breaks explicitly, and even then the result says that
/// re-pagination can differ.
/// </para>
/// </remarks>
internal static class PdfModelProjector
{
    private const double ParagraphGapFactor = 1.55;
    private const double IndentThreshold = 6;
    // A wrapped line is often a little shorter than the widest one, so only a
    // markedly short line is read as the end of its paragraph. Being conservative
    // here costs a missed break; being aggressive splits every ragged paragraph.
    private const double ShortLineFactor = 0.65;

    public static List<RichTextParagraph> Project(
        IReadOnlyList<PdfTextLine> lines,
        IReadOnlyList<PdfPlacedImage> images,
        bool insertPageBreak,
        int maxParagraphs)
    {
        var paragraphs = new List<RichTextParagraph>();
        if (lines.Count == 0 && images.Count == 0)
            return paragraphs;

        // Images become paragraphs of their own rather than joining a line of
        // text. The model has no anchoring for an inline picture at a page
        // coordinate, and pretending an image belongs to whichever line happens
        // to be nearest would state a relationship the page never expressed.
        // Ordering by the top edge puts each one where a reader meets it.
        var pending = new List<PdfPlacedImage>(images);
        pending.Sort(static (left, right) => right.Top.CompareTo(left.Top));
        int nextImage = 0;

        // Grouped first and emitted after, because whether a numbered line is a
        // list item depends on the paragraphs that follow it.
        var items = new List<Item>();

        void FlushImagesAbove(double baseline)
        {
            while (nextImage < pending.Count && pending[nextImage].Top >= baseline)
            {
                items.Add(new Item(null, pending[nextImage]));
                nextImage++;
            }
        }

        int[] blocks = Blocks(lines, out List<(double Left, double Right)> extents);
        Dictionary<int, double> measures = DeclaredMeasures(lines);
        var pendingLines = new List<PdfTextLine>();
        PdfTextLine? previous = null;
        int previousBlock = -1;

        // Where the previous row of text stopped: the right end of every line on
        // its baseline. A row set with wide gaps - a label, a run of spaces, a
        // value - arrives as several lines, and the one that matters for whether
        // the next row's first word would have fitted is the last of them.
        double rowRight = double.MinValue;

        for (int i = 0; i < lines.Count; i++)
        {
            PdfTextLine line = lines[i];
            if (line.IsBlank)
                continue;

            if (previous is not null && StartsNewParagraph(previous, rowRight, line, previousBlock, blocks[i], extents, measures))
            {
                items.Add(new Item([.. pendingLines], null));
                pendingLines.Clear();
            }

            // Anything drawn above this line belongs before it, and a paragraph
            // is only complete once the lines that follow it are known - so the
            // flush happens at the boundary rather than mid-paragraph.
            if (pendingLines.Count == 0)
                FlushImagesAbove(line.Baseline);

            rowRight = previous is not null && SameRow(previous, line) ? Math.Max(rowRight, line.Right) : line.Right;
            pendingLines.Add(line);
            previous = line;
            previousBlock = blocks[i];
        }

        if (pendingLines.Count > 0)
            items.Add(new Item([.. pendingLines], null));

        // Whatever sits below the last line, and every image on a page with no
        // text at all.
        FlushImagesAbove(double.NegativeInfinity);

        ListKind[] lists = ConfirmLists(items);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Image is { } image)
            {
                // A picture past the allowance is left out rather than failing
                // the read, which is what the allowance has always done to them.
                if (paragraphs.Count < maxParagraphs)
                    paragraphs.Add(ImageParagraph(image));
                continue;
            }

            Emit(paragraphs, items[i].Lines!, lists[i], maxParagraphs);
        }

        if (insertPageBreak && paragraphs.Count > 0)
        {
            // A page boundary is represented as an empty paragraph, which is the
            // only page-break notion the shared model has today. It is opt-in
            // precisely because it is a weaker statement than a real page break.
            paragraphs.Add(RichTextParagraph.Empty);
        }

        return paragraphs;
    }

    /// <summary>One paragraph's worth of lines, or one picture: exactly one of the two is set.</summary>
    private readonly record struct Item(List<PdfTextLine>? Lines, PdfPlacedImage? Image);

    /// <summary>
    /// One image as a paragraph: a single object replacement character carrying
    /// the picture, which is the model's only way to hold one.
    /// </summary>
    private static RichTextParagraph ImageParagraph(PdfPlacedImage placed) =>
        RichTextParagraph.Create(
            InlineImage.PlaceholderText,
            InlineStyle.Default with { Image = placed.Image });

    /// <summary>
    /// Assigns each line to a block: a run of lines stacked one under another
    /// with some horizontal extent in common. Returns each line's block, and each
    /// block's left and right edge.
    /// </summary>
    /// <remarks>
    /// A column is what a paragraph's width is measured against, and lines in
    /// reading order that stack and overlap are what a column looks like from
    /// the inside. A line beside the previous one, or clear of it to either side,
    /// is somewhere else on the page.
    /// </remarks>
    private static int[] Blocks(IReadOnlyList<PdfTextLine> lines, out List<(double Left, double Right)> extents)
    {
        var blocks = new int[lines.Count];
        extents = [];
        PdfTextLine? previous = null;

        for (int i = 0; i < lines.Count; i++)
        {
            PdfTextLine line = lines[i];
            if (line.IsBlank)
            {
                blocks[i] = -1;
                continue;
            }

            bool continues = previous is not null &&
                previous.Baseline - line.Baseline > 0 &&
                line.Left < previous.Right &&
                line.Right > previous.Left;

            if (continues)
            {
                (double left, double right) = extents[^1];
                extents[^1] = (Math.Min(left, line.Left), Math.Max(right, line.Right));
            }
            else
            {
                extents.Add((line.Left, line.Right));
            }

            blocks[i] = extents.Count - 1;
            previous = line;
        }

        return blocks;
    }

    /// <summary>
    /// The widest line of each declared block: the measure its own paragraph
    /// was set to, which a heading or a wider column beside it says nothing about.
    /// </summary>
    private static Dictionary<int, double> DeclaredMeasures(IReadOnlyList<PdfTextLine> lines)
    {
        var measures = new Dictionary<int, double>();
        foreach (PdfTextLine line in lines)
        {
            if (line.Block < 0 || line.IsBlank)
                continue;

            measures[line.Block] = measures.TryGetValue(line.Block, out double right) ? Math.Max(right, line.Right) : line.Right;
        }

        return measures;
    }

    /// <summary>Whether two lines sit on one baseline, as the pieces of one row.</summary>
    private static bool SameRow(PdfTextLine previous, PdfTextLine line) =>
        Math.Abs(previous.Baseline - line.Baseline) <= Math.Max(1.0, Math.Max(previous.Height, line.Height) * 0.35);

    private static bool StartsNewParagraph(
        PdfTextLine previous,
        double rowRight,
        PdfTextLine line,
        int previousBlock,
        int block,
        List<(double Left, double Right)> extents,
        Dictionary<int, double> measures)
    {
        // Where a tagged document declared the block each line belongs to, two
        // blocks are two paragraphs however closely they are set. Inside one,
        // spacing and indents say nothing the tree has not already, and the one
        // question left is whether a row that stopped short was broken there on
        // purpose: a row cut short because the next word was a long address is
        // a wrap, and one that stopped with room to spare for the next word was
        // ended by hand. The model has no line break inside a paragraph, so a
        // break made by hand is the paragraph break it comes closest to.
        if (line.Block >= 0 && previous.Block >= 0)
        {
            return line.Block != previous.Block ||
                (line.Baseline < previous.Baseline && !SameRow(previous, line) &&
                 NextWordFits(rowRight, previous, line, measures[line.Block]));
        }

        // A line that is not in the previous line's block is somewhere else on
        // the page: beside it, above it, or across a gutter from it.
        if (block != previousBlock)
            return true;

        double gap = previous.Baseline - line.Baseline;
        double reference = Math.Max(previous.Height, line.Height);

        // A negative or zero gap means the lines are side by side rather than
        // stacked; treat that as a new block so their text does not run together.
        if (gap <= 0)
            return true;

        if (reference > 0 && gap > reference * ParagraphGapFactor)
            return true;

        if (DetectListMarker(line.Text, out _, out _))
            return true;

        (double blockLeft, double blockRight) = extents[block];

        // A first-line indent relative to the block's left edge.
        if (line.Left - blockLeft > IndentThreshold && previous.Left - blockLeft <= IndentThreshold)
            return true;

        // A previous line that stopped well short of the block's right edge ended
        // its paragraph, unless the block is a single ragged column - or the
        // line after it starts with a word too long to have fitted there, which
        // is a wrap, not an ending.
        double width = blockRight - blockLeft;
        return width > 0 &&
            previous.Right < blockLeft + (width * ShortLineFactor) &&
            line.Left <= blockLeft + IndentThreshold &&
            NextWordFits(previous.Right, previous, line, blockRight);
    }

    /// <summary>
    /// Whether the first word of <paramref name="line"/> would have fitted on
    /// the row before it, which stopped at <paramref name="rowRight"/> and runs
    /// to <paramref name="measure"/> at most. Text is set greedily, so a word
    /// that fitted and was carried over anyway was carried over on purpose.
    /// </summary>
    /// <remarks>
    /// The measure is the widest line of the paragraph, which is at most the
    /// real column and often a little short of it. Erring there makes a word
    /// look as though it would not have fitted, and keeps a wrap a wrap - the
    /// side a paragraph broken in two would have been wrong on.
    /// </remarks>
    private static bool NextWordFits(double rowRight, PdfTextLine previous, PdfTextLine line, double measure)
    {
        double space = Math.Max(previous.SpaceWidth, line.SpaceWidth);
        return rowRight + space + line.FirstWordWidth <= measure - space;
    }

    /// <summary>
    /// Decides which paragraphs are list items. A bullet is one wherever it
    /// appears; a number is one only inside a run of consecutive paragraphs
    /// counting up from one; a letter never is.
    /// </summary>
    private static ListKind[] ConfirmLists(List<Item> items)
    {
        var kinds = new ListKind[items.Count];
        var numbers = new int[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Lines is not { Count: > 0 } lines ||
                !ReadMarker(lines[0].Text, out ListKind kind, out _, out int number))
            {
                continue;
            }

            if (kind == ListKind.Bullet)
                kinds[i] = ListKind.Bullet;
            else
                numbers[i] = number;
        }

        // Runs of 1, 2, 3 in consecutive paragraphs. A single "1." is a heading
        // or a date as often as a list, and a run starting anywhere else is one
        // the model would renumber from one.
        for (int i = 0; i < items.Count; i++)
        {
            if (numbers[i] != 1)
                continue;

            int end = i + 1;
            while (end < items.Count && numbers[end] == numbers[end - 1] + 1)
                end++;

            if (end - i < 2)
                continue;

            for (int j = i; j < end; j++)
                kinds[j] = ListKind.Numbered;

            i = end - 1;
        }

        return kinds;
    }

    private static void Emit(List<RichTextParagraph> paragraphs, List<PdfTextLine> lines, ListKind list, int maxParagraphs)
    {
        if (lines.Count == 0)
            return;

        if (paragraphs.Count >= maxParagraphs)
            throw PdfWorkBudget.Exceeded(nameof(DocumentLimits.MaxParagraphCount), maxParagraphs);

        var spans = new List<PdfTextSpan>();
        bool isList = list != ListKind.None;
        int markerLength = 0;
        if (isList)
            DetectListMarker(lines[0].Text, out _, out markerLength);

        for (int i = 0; i < lines.Count; i++)
        {
            PdfTextLine line = lines[i];
            List<PdfTextSpan> lineSpans = line.Spans;

            if (i == 0 && isList)
                lineSpans = StripLeading(lineSpans, markerLength);

            if (i > 0)
            {
                // Wrapped lines join with a space unless the break already has one
                // or the previous line ended with a soft hyphen.
                string previousText = spans.Count > 0 ? spans[^1].Text : string.Empty;
                if (previousText.Length > 0 && !previousText.EndsWith(' ') && lineSpans.Count > 0 && !lineSpans[0].Text.StartsWith(' '))
                    spans.Add(new PdfTextSpan(" ", spans[^1].Style));
            }

            spans.AddRange(lineSpans);
        }

        var paragraphStyle = ParagraphStyle.Default with
        {
            ListKind = list,
            IndentLevel = isList ? 1 : 0,
        };

        paragraphs.Add(Build(spans, paragraphStyle));
    }

    private static RichTextParagraph Build(List<PdfTextSpan> spans, ParagraphStyle style)
    {
        if (spans.Count == 0)
            return RichTextParagraph.Empty.WithParagraphStyle(style);

        RichTextParagraph paragraph = RichTextParagraph.Create(spans[0].Text, spans[0].Style, style);
        for (int i = 1; i < spans.Count; i++)
        {
            if (spans[i].Text.Length == 0)
                continue;
            paragraph = paragraph.InsertText(paragraph.Length, spans[i].Text, spans[i].Style);
        }

        return paragraph;
    }

    private static List<PdfTextSpan> StripLeading(List<PdfTextSpan> spans, int count)
    {
        var stripped = new List<PdfTextSpan>(spans.Count);
        int remaining = count;

        foreach (PdfTextSpan span in spans)
        {
            if (remaining <= 0)
            {
                stripped.Add(span);
                continue;
            }

            if (span.Text.Length <= remaining)
            {
                remaining -= span.Text.Length;
                continue;
            }

            stripped.Add(new PdfTextSpan(span.Text[remaining..], span.Style));
            remaining = 0;
        }

        return stripped;
    }

    /// <summary>
    /// Recognizes a leading list marker: a bullet character, or a number or
    /// letter followed by a period or parenthesis. The marker is removed from the
    /// text because the model expresses it as a paragraph property.
    /// </summary>
    /// <remarks>
    /// This says a line is marker-shaped, which is enough to start a paragraph.
    /// Whether the paragraph is a list item is decided across paragraphs; see the
    /// class remarks.
    /// </remarks>
    internal static bool DetectListMarker(string text, out ListKind kind, out int markerLength) =>
        ReadMarker(text, out kind, out markerLength, out _);

    /// <summary>
    /// <see cref="DetectListMarker"/>, also reporting what the marker counts:
    /// its value for a number, -1 for a letter, and zero for a bullet.
    /// </summary>
    private static bool ReadMarker(string text, out ListKind kind, out int markerLength, out int number)
    {
        kind = ListKind.None;
        markerLength = 0;
        number = 0;

        int index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        if (index >= text.Length)
            return false;

        char first = text[index];
        if (first is '•' or '‣' or '▪' or '◦' or '·' or '-' or '–' or '*')
        {
            int after = index + 1;
            // A hyphen only starts a list when a space follows it; otherwise it is
            // an ordinary hyphenated word.
            if (first is '-' or '–' or '*' && (after >= text.Length || text[after] != ' '))
                return false;

            while (after < text.Length && text[after] == ' ')
                after++;
            if (after >= text.Length)
                return false;

            kind = ListKind.Bullet;
            markerLength = after;
            return true;
        }

        int digits = index;
        while (digits < text.Length && char.IsDigit(text[digits]))
            digits++;

        bool numeric = digits > index && digits - index <= 3;
        bool alphabetic = !numeric && index + 1 < text.Length && char.IsLetter(first) && !char.IsLetter(text[index + 1]);
        int markerEnd = numeric ? digits : index + 1;

        if (!numeric && !alphabetic)
            return false;
        if (markerEnd >= text.Length || text[markerEnd] is not ('.' or ')'))
            return false;

        int textStart = markerEnd + 1;
        while (textStart < text.Length && text[textStart] == ' ')
            textStart++;
        if (textStart >= text.Length || textStart == markerEnd + 1)
            return false;

        kind = ListKind.Numbered;
        markerLength = textStart;
        number = numeric && int.TryParse(
            text.AsSpan(index, digits - index), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : -1;
        return true;
    }

    /// <summary>Formats a numbered-list marker for the writer, the inverse of detection.</summary>
    internal static string FormatListMarker(ListKind kind, int number) => kind switch
    {
        ListKind.Bullet => "• ",
        ListKind.Numbered => string.Create(CultureInfo.InvariantCulture, $"{number}. "),
        _ => string.Empty,
    };
}
