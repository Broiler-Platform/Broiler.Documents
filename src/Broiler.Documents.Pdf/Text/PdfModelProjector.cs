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
        bool insertPageBreak,
        int maxParagraphs)
    {
        var paragraphs = new List<RichTextParagraph>();
        if (lines.Count == 0)
            return paragraphs;

        double blockRight = double.MinValue;
        double blockLeft = double.MaxValue;
        foreach (PdfTextLine line in lines)
        {
            if (line.IsBlank)
                continue;
            blockRight = Math.Max(blockRight, line.Right);
            blockLeft = Math.Min(blockLeft, line.Left);
        }

        var pending = new List<PdfTextLine>();
        PdfTextLine? previous = null;

        foreach (PdfTextLine line in lines)
        {
            if (line.IsBlank)
                continue;

            if (previous is not null && StartsNewParagraph(previous, line, blockLeft, blockRight))
            {
                Emit(paragraphs, pending, maxParagraphs);
                pending.Clear();
            }

            pending.Add(line);
            previous = line;
        }

        Emit(paragraphs, pending, maxParagraphs);

        if (insertPageBreak && paragraphs.Count > 0)
        {
            // A page boundary is represented as an empty paragraph, which is the
            // only page-break notion the shared model has today. It is opt-in
            // precisely because it is a weaker statement than a real page break.
            paragraphs.Add(RichTextParagraph.Empty);
        }

        return paragraphs;
    }

    private static bool StartsNewParagraph(PdfTextLine previous, PdfTextLine line, double blockLeft, double blockRight)
    {
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

        // A first-line indent relative to the block's left edge.
        if (line.Left - blockLeft > IndentThreshold && previous.Left - blockLeft <= IndentThreshold)
            return true;

        // A previous line that stopped well short of the block's right edge ended
        // its paragraph, unless the block is a single ragged column.
        double width = blockRight - blockLeft;
        return width > 0 && previous.Right < blockLeft + (width * ShortLineFactor) && line.Left <= blockLeft + IndentThreshold;
    }

    private static void Emit(List<RichTextParagraph> paragraphs, List<PdfTextLine> lines, int maxParagraphs)
    {
        if (lines.Count == 0)
            return;

        if (paragraphs.Count >= maxParagraphs)
            throw PdfWorkBudget.Exceeded(nameof(DocumentLimits.MaxParagraphCount), maxParagraphs);

        var spans = new List<PdfTextSpan>();
        bool isList = DetectListMarker(lines[0].Text, out ListKind kind, out int markerLength);

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
            ListKind = isList ? kind : ListKind.None,
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
    internal static bool DetectListMarker(string text, out ListKind kind, out int markerLength)
    {
        kind = ListKind.None;
        markerLength = 0;

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
