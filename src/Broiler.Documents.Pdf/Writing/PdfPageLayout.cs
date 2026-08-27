using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Writing;

/// <summary>A run of text placed at a definite position on a page.</summary>
internal sealed class PdfPlacedRun
{
    public PdfPlacedRun(
        string text,
        double x,
        double baseline,
        double width,
        double fontSize,
        PdfStandardFont font,
        BColor color,
        BColor background,
        bool underline,
        bool strikethrough,
        string? linkHref)
    {
        Text = text;
        X = x;
        Baseline = baseline;
        Width = width;
        FontSize = fontSize;
        Font = font;
        Color = color;
        Background = background;
        Underline = underline;
        Strikethrough = strikethrough;
        LinkHref = linkHref;
    }

    public string Text { get; }

    public double X { get; }

    public double Baseline { get; }

    public double Width { get; }

    public double FontSize { get; }

    public PdfStandardFont Font { get; }

    public BColor Color { get; }

    public BColor Background { get; }

    public bool Underline { get; }

    public bool Strikethrough { get; }

    /// <summary>The admitted link target, or null. Revalidated again at emission.</summary>
    public string? LinkHref { get; }
}

/// <summary>One laid-out page.</summary>
internal sealed class PdfLayoutPage
{
    public List<PdfPlacedRun> Runs { get; } = [];
}

/// <summary>
/// Breaks a rich-text document into lines and pages.
/// </summary>
/// <remarks>
/// <para>
/// Layout is resolved exactly once, here, and the serializer consumes the result
/// without measuring, shaping, or re-breaking anything. That separation is what
/// the roadmap's paginated-artifact boundary is for, and it is why the writer can
/// be deterministic: nothing downstream can reach for a host font or a DPI.
/// </para>
/// <para>
/// Measurement goes through <see cref="IPdfFontMetricsProvider"/>. With the
/// built-in approximate model the line breaks are consistent and reproducible but
/// not metrically exact, which the writer reports once per document.
/// </para>
/// </remarks>
internal sealed class PdfPageLayout
{
    private readonly PdfWriteOptions _options;
    private readonly IPdfFontMetricsProvider _metrics;
    private readonly PdfUriPolicy _uriPolicy;
    private readonly PdfDiagnosticSink _diagnostics;
    private readonly CancellationToken _cancellationToken;

    public PdfPageLayout(
        PdfWriteOptions options,
        IPdfFontMetricsProvider metrics,
        PdfUriPolicy uriPolicy,
        PdfDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        _options = options;
        _metrics = metrics;
        _uriPolicy = uriPolicy;
        _diagnostics = diagnostics;
        _cancellationToken = cancellationToken;
    }

    public List<PdfLayoutPage> Build(RichTextDocument document)
    {
        var pages = new List<PdfLayoutPage>();
        var page = new PdfLayoutPage();
        PdfPageSetup setup = _options.PageSetup;

        double top = setup.Height - setup.MarginTop;
        double bottom = setup.MarginBottom;
        double y = top;
        int listNumber = 1;
        ListKind previousList = ListKind.None;

        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            ParagraphStyle style = paragraph.Style;
            double lineSpacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;

            if (style.ListKind == ListKind.Numbered && previousList == ListKind.Numbered)
                listNumber++;
            else if (style.ListKind == ListKind.Numbered)
                listNumber = 1;
            previousList = style.ListKind;

            y -= style.SpacingBefore;

            double indent = style.IndentLevel * 24d;
            double left = setup.MarginLeft + indent;
            double available = setup.Width - setup.MarginRight - left;
            if (available <= 0)
            {
                _diagnostics.Warning(
                    PdfDiagnosticCodes.WriteOverflow,
                    "A paragraph's indent left no usable line width; the indent was clamped to the page margin.");
                left = setup.MarginLeft;
                available = setup.ContentWidth;
            }

            string marker = PdfModelProjector.FormatListMarker(style.ListKind, listNumber);
            List<LayoutLine> lines = BreakParagraph(paragraph, available, marker);

            if (lines.Count == 0)
            {
                // An empty paragraph still advances by one line so blank lines and
                // opt-in page boundaries survive a round trip.
                y -= EmptyLineHeight() * lineSpacing;
                y -= SpacingAfter(style, EmptyLineHeight());
                if (y < bottom)
                {
                    pages.Add(page);
                    page = new PdfLayoutPage();
                    y = top;
                }

                continue;
            }

            double lastLineHeight = 0;
            foreach (LayoutLine line in lines)
            {
                double lineHeight = line.Height * lineSpacing;
                if (y - lineHeight < bottom && page.Runs.Count > 0)
                {
                    pages.Add(page);
                    page = new PdfLayoutPage();
                    y = top;
                }

                y -= lineHeight;
                lastLineHeight = lineHeight;
                Place(page, line, left, available, y, style.Alignment);
            }

            y -= SpacingAfter(style, lastLineHeight);
        }

        pages.Add(page);
        return pages;
    }

    private double EmptyLineHeight() => _options.DefaultFontSize * 1.2;

    /// <summary>
    /// The gap after a paragraph. A paragraph the model does not space explicitly
    /// still gets a default gap, scaled to its own line height.
    /// </summary>
    /// <remarks>
    /// This is not only a typographic default. PDF records no paragraph structure,
    /// so a reader — ours included — has to infer it from vertical spacing. Setting
    /// consecutive paragraphs solid would make them indistinguishable from the
    /// wrapped lines of one paragraph, and a write-then-read round trip would
    /// silently merge them.
    /// </remarks>
    private static double SpacingAfter(ParagraphStyle style, double lineHeight) =>
        style.SpacingAfter > 0 ? style.SpacingAfter : lineHeight * 0.55;

    private void Place(PdfLayoutPage page, LayoutLine line, double left, double available, double baseline, TextAlignment alignment)
    {
        double x = alignment switch
        {
            TextAlignment.Center => left + ((available - line.Width) / 2),
            TextAlignment.Right => left + available - line.Width,
            _ => left,
        };

        // Never start left of the margin, however the alignment arithmetic came out.
        if (x < left)
            x = left;

        foreach (LayoutPiece piece in line.Pieces)
        {
            if (piece.Text.Length > 0)
            {
                page.Runs.Add(new PdfPlacedRun(
                    piece.Text,
                    x,
                    baseline,
                    piece.Width,
                    piece.FontSize,
                    piece.Font,
                    piece.Color,
                    piece.Background,
                    piece.Underline,
                    piece.Strikethrough,
                    piece.LinkHref));
            }

            x += piece.Width;
        }
    }

    // ---- line breaking --------------------------------------------------------

    private List<LayoutLine> BreakParagraph(RichTextParagraph paragraph, double available, string marker)
    {
        var lines = new List<LayoutLine>();
        var current = new LayoutLine();
        double used = 0;

        foreach (Word word in EnumerateWords(paragraph, marker))
        {
            _cancellationToken.ThrowIfCancellationRequested();

            double wordWidth = word.Width;
            bool fits = used + wordWidth <= available || current.Pieces.Count == 0;

            if (!fits)
            {
                TrimTrailingSpace(current);
                lines.Add(current);
                current = new LayoutLine();
                used = 0;

                // A word wider than the whole line is broken by character; the
                // alternative is a run that overflows the page.
                if (wordWidth > available)
                {
                    foreach (Word part in SplitOversizedWord(word, available))
                    {
                        if (used + part.Width > available && current.Pieces.Count > 0)
                        {
                            lines.Add(current);
                            current = new LayoutLine();
                            used = 0;
                        }

                        Append(current, part);
                        used += part.Width;
                    }

                    continue;
                }

                if (word.IsSpace)
                    continue; // do not start a line with the space that wrapped
            }

            Append(current, word);
            used += wordWidth;
        }

        TrimTrailingSpace(current);
        if (current.Pieces.Count > 0)
            lines.Add(current);

        return lines;
    }

    private static void Append(LayoutLine line, Word word)
    {
        LayoutPiece? last = line.Pieces.Count > 0 ? line.Pieces[^1] : null;
        if (last is not null && last.SameStyleAs(word))
        {
            line.Pieces[^1] = last.Extend(word.Text, word.Width);
        }
        else
        {
            line.Pieces.Add(word.ToPiece());
        }

        line.Width += word.Width;
        line.Height = Math.Max(line.Height, word.FontSize * 1.2);
    }

    // Trailing spaces are dropped one at a time so alignment measures the line's
    // visible width rather than the width of the space that ended it.
    private void TrimTrailingSpace(LayoutLine line)
    {
        while (line.Pieces.Count > 0)
        {
            LayoutPiece last = line.Pieces[^1];
            if (last.Text.Length == 0 || !IsBreakSpace(last.Text[^1]))
                return;

            double spaceWidth = CharacterWidth(last.Font, last.Text[^1], last.FontSize);
            LayoutPiece trimmed = last.WithoutLastCharacter(spaceWidth);
            line.Width -= spaceWidth;
            if (trimmed.Text.Length == 0)
                line.Pieces.RemoveAt(line.Pieces.Count - 1);
            else
                line.Pieces[^1] = trimmed;
        }
    }

    private IEnumerable<Word> SplitOversizedWord(Word word, double available)
    {
        var builder = new StringBuilder();
        double width = 0;

        foreach (char c in word.Text)
        {
            double advance = CharacterWidth(word.Font, c, word.FontSize);
            if (builder.Length > 0 && width + advance > available)
            {
                yield return word.WithText(builder.ToString(), width);
                builder.Clear();
                width = 0;
            }

            builder.Append(c);
            width += advance;
        }

        if (builder.Length > 0)
            yield return word.WithText(builder.ToString(), width);
    }

    /// <summary>
    /// Walks a paragraph's runs and yields words, keeping each word's style. A
    /// trailing space belongs to the word before it, so a line break falls between
    /// words rather than in front of a space.
    /// </summary>
    private IEnumerable<Word> EnumerateWords(RichTextParagraph paragraph, string marker)
    {
        string text = paragraph.Text;
        int offset = 0;

        if (marker.Length > 0)
        {
            InlineStyle markerStyle = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : InlineStyle.Default;
            RunStyle resolved = Resolve(markerStyle);
            yield return MakeWord(marker, resolved, isSpace: false);
        }

        foreach (StyleRun run in paragraph.Runs)
        {
            if (offset >= text.Length)
                break;

            int length = Math.Min(run.Length, text.Length - offset);
            string runText = text.Substring(offset, length);
            offset += length;

            if (run.Style.IsImage)
            {
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.WriteImageNotComposed,
                    "An inline image was dropped. This build composes no image emitter, so images are omitted rather than rasterized or transcoded.");
                continue;
            }

            RunStyle style = Resolve(run.Style);
            foreach (Word word in SplitWords(runText, style))
                yield return word;
        }
    }

    private IEnumerable<Word> SplitWords(string text, RunStyle style)
    {
        int index = 0;
        while (index < text.Length)
        {
            int start = index;
            while (index < text.Length && !IsBreakSpace(text[index]))
                index++;

            // Absorb the run of spaces that follows the word.
            int wordEnd = index;
            while (index < text.Length && IsBreakSpace(text[index]))
                index++;

            if (index == start)
            {
                index++;
                continue;
            }

            string piece = text[start..index];
            yield return MakeWord(piece, style, isSpace: wordEnd == start);
        }
    }

    // A non-breaking space is deliberately not a break opportunity: it is the one
    // space a document uses to say "do not wrap here".
    private static bool IsBreakSpace(char c) => c is ' ' or '\t';

    private Word MakeWord(string text, RunStyle style, bool isSpace)
    {
        string encodable = MakeEncodable(text);
        double width = 0;
        foreach (char c in encodable)
            width += CharacterWidth(style.Font, c, style.FontSize);
        return new Word(encodable, width, style, isSpace);
    }

    private double CharacterWidth(PdfStandardFont font, char character, double fontSize) =>
        _metrics.GetAdvanceWidth(font, character) / 1000d * fontSize;

    /// <summary>
    /// Replaces characters the writer's WinAnsi encoding cannot represent. A
    /// Unicode-capable writer needs embedded composite fonts, which is a separate
    /// reviewed step; until then the substitution is reported rather than silent.
    /// </summary>
    private string MakeEncodable(string text)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            if (PdfWinAnsiEncoder.CanEncode(text[i]))
            {
                builder?.Append(text[i]);
                continue;
            }

            builder ??= new StringBuilder(text.Length).Append(text, 0, i);
            builder.Append('?');
            _diagnostics.Skipped(
                PdfDiagnosticCodes.WriteCharacterUnsupported,
                "Some characters are outside the writer's WinAnsi encoding and were replaced. Writing them needs an embedded composite font, which this build does not compose.");
        }

        return builder?.ToString() ?? text;
    }

    private RunStyle Resolve(InlineStyle style)
    {
        PdfFontFamilyKind family = ClassifyFamily(style.FontFamily);
        PdfStandardFont font = PdfStandardFonts.Select(family, style.Bold, style.Italic);
        double size = style.FontSize is > 0 ? style.FontSize.Value : _options.DefaultFontSize;

        string? href = null;
        if (style.IsLink)
        {
            if (_uriPolicy.TryAdmit(style.LinkHref, out string canonical, out string? reason))
            {
                href = canonical;
            }
            else
            {
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.UriRejected,
                    $"A link target was not emitted because {reason ?? "it failed the active URI policy"}. The text was written without an annotation.");
            }
        }

        BColor foreground = style.Foreground.IsEmpty ? BColor.Black : style.Foreground;
        return new RunStyle(font, size, foreground, style.Background, style.Underline, style.Strikethrough, href);
    }

    /// <summary>
    /// Maps a model family name onto one of the three logical families the
    /// standard fonts cover. The match is on the family the document names, not
    /// on any font installed on the machine — nothing here consults the host.
    /// </summary>
    private PdfFontFamilyKind ClassifyFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return _options.DefaultFamily;

        string name = family.Trim();
        if (PdfStandardFonts.TryParse(name, out PdfStandardFont standard))
        {
            return standard switch
            {
                PdfStandardFont.TimesRoman or PdfStandardFont.TimesBold
                    or PdfStandardFont.TimesItalic or PdfStandardFont.TimesBoldItalic => PdfFontFamilyKind.Serif,
                PdfStandardFont.Courier or PdfStandardFont.CourierBold
                    or PdfStandardFont.CourierOblique or PdfStandardFont.CourierBoldOblique => PdfFontFamilyKind.Monospace,
                _ => PdfFontFamilyKind.SansSerif,
            };
        }

        if (name.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Courier", StringComparison.OrdinalIgnoreCase))
            return PdfFontFamilyKind.Monospace;

        if (name.Contains("Serif", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Sans", StringComparison.OrdinalIgnoreCase))
            return PdfFontFamilyKind.Serif;

        if (name.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Garamond", StringComparison.OrdinalIgnoreCase))
            return PdfFontFamilyKind.Serif;

        return _options.DefaultFamily;
    }

    // ---- layout value types ---------------------------------------------------

    private readonly struct RunStyle
    {
        public RunStyle(
            PdfStandardFont font,
            double fontSize,
            BColor color,
            BColor background,
            bool underline,
            bool strikethrough,
            string? linkHref)
        {
            Font = font;
            FontSize = fontSize;
            Color = color;
            Background = background;
            Underline = underline;
            Strikethrough = strikethrough;
            LinkHref = linkHref;
        }

        public PdfStandardFont Font { get; }

        public double FontSize { get; }

        public BColor Color { get; }

        public BColor Background { get; }

        public bool Underline { get; }

        public bool Strikethrough { get; }

        public string? LinkHref { get; }

        public bool Matches(RunStyle other) =>
            Font == other.Font && FontSize.Equals(other.FontSize) && Color == other.Color &&
            Background == other.Background && Underline == other.Underline &&
            Strikethrough == other.Strikethrough &&
            string.Equals(LinkHref, other.LinkHref, StringComparison.Ordinal);
    }

    private readonly struct Word
    {
        public Word(string text, double width, RunStyle style, bool isSpace)
        {
            Text = text;
            Width = width;
            Style = style;
            IsSpace = isSpace;
        }

        public string Text { get; }

        public double Width { get; }

        public RunStyle Style { get; }

        /// <summary>True when the word is only whitespace.</summary>
        public bool IsSpace { get; }

        public PdfStandardFont Font => Style.Font;

        public double FontSize => Style.FontSize;

        public Word WithText(string text, double width) => new(text, width, Style, IsSpace);

        public LayoutPiece ToPiece() => new(Text, Width, Style);
    }

    private sealed class LayoutPiece
    {
        public LayoutPiece(string text, double width, RunStyle style)
        {
            Text = text;
            Width = width;
            Style = style;
        }

        public string Text { get; }

        public double Width { get; }

        public RunStyle Style { get; }

        public PdfStandardFont Font => Style.Font;

        public double FontSize => Style.FontSize;

        public BColor Color => Style.Color;

        public BColor Background => Style.Background;

        public bool Underline => Style.Underline;

        public bool Strikethrough => Style.Strikethrough;

        public string? LinkHref => Style.LinkHref;

        public bool SameStyleAs(Word word) => Style.Matches(word.Style);

        public LayoutPiece Extend(string text, double width) =>
            new(Text + text, Width + width, Style);

        public LayoutPiece WithoutLastCharacter(double width) =>
            new(Text[..^1], Width - width, Style);
    }

    private sealed class LayoutLine
    {
        public List<LayoutPiece> Pieces { get; } = [];

        public double Width { get; set; }

        public double Height { get; set; }
    }
}
