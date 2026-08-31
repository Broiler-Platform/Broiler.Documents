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
        string? linkHref,
        double wordSpacing = 0)
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
        WordSpacing = wordSpacing;
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

    /// <summary>
    /// Extra width given to every space in this run, as PDF's <c>Tw</c>. Non-zero
    /// only on a justified line, where the slack is spread into the spaces rather
    /// than left at one end.
    /// </summary>
    public double WordSpacing { get; }
}

/// <summary>A floating shape's painted box, in PDF user space.</summary>
internal sealed record PdfPlacedShape(
    double X,
    double Y,
    double Width,
    double Height,
    ShapeFill? Fill,
    BColor Outline);

/// <summary>One laid-out page.</summary>
internal sealed class PdfLayoutPage
{
    /// <summary>The boxes painted under this page's text.</summary>
    public List<PdfPlacedShape> Shapes { get; } = [];
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
    /// <summary>The width of one indent level, in points.</summary>
    private const double IndentWidth = 24d;

    /// <summary>
    /// The distance between the default tab stops, in points, measured from where
    /// the paragraph's text starts. It is the tab stop the RichEdit control lays
    /// out with, so a tabbed paragraph prints where it sits on screen.
    /// </summary>
    private const double TabStopWidth = 48d;

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
        PdfPageSetup setup = SetupFor(document);

        double top = setup.Height - setup.MarginTop;
        double bottom = setup.MarginBottom;
        double y = top;
        int listNumber = 1;
        ListKind previousList = ListKind.None;

        var anchors = new Dictionary<int, (PdfLayoutPage Page, double Top)>();
        int paragraphIndex = -1;
        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            paragraphIndex++;

            ParagraphStyle style = paragraph.Style;
            double lineSpacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;

            if (style.ListKind == ListKind.Numbered && previousList == ListKind.Numbered)
                listNumber++;
            else if (style.ListKind == ListKind.Numbered)
                listNumber = 1;
            previousList = style.ListKind;

            y -= style.SpacingBefore;

            double indent = style.IndentLevel * IndentWidth;
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
            anchors[paragraphIndex] = (page, y);

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
                Place(page, line, left, available, y, style.Alignment, line == lines[^1]);
            }

            y -= SpacingAfter(style, lastLineHeight);
        }

        pages.Add(page);
        PlaceShapes(document.Shapes, anchors, setup);
        PlaceRunningContent(pages, document.RunningContent, setup, document.PageGeometry);
        return pages;
    }

    /// <summary>
    /// Draws the header and footer on every page, once the body has decided how
    /// many pages there are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model carries no header distance - no reader produces one - so this
    /// picks a convention rather than inventing a setting: the block sits in the
    /// middle of the margin it belongs to. A header taller than its margin would
    /// run into the body, so it is reported rather than drawn over the text.
    /// </para>
    /// <para>
    /// Page one takes the first-page selection and even-numbered pages the even
    /// one, each falling back to the default, which is what
    /// <see cref="RunningContent.EffectiveHeader"/> resolves.
    /// </para>
    /// </remarks>
    private void PlaceRunningContent(
        List<PdfLayoutPage> pages,
        RunningContent running,
        PdfPageSetup setup,
        PageGeometry? geometry)
    {
        if (running is null || running.IsEmpty)
            return;

        double left = setup.MarginLeft;
        double available = setup.ContentWidth;

        // A document that states how far its header sits from the edge gets that;
        // one that states nothing keeps the old convention of halfway up the
        // margin, which is the best guess available without a number.
        double headerBaseline = geometry is not null && geometry.HeaderDistance > 0
            ? setup.Height - geometry.HeaderDistance
            : setup.Height - (setup.MarginTop / 2);
        double footerBaseline = geometry is not null && geometry.FooterDistance > 0
            ? geometry.FooterDistance
            : setup.MarginBottom / 2;

        for (int i = 0; i < pages.Count; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            PageSelection selection = SelectionForPage(i);

            PlaceRunningBlock(
                pages[i],
                running.EffectiveHeader(selection),
                left,
                available,
                headerBaseline,
                setup.MarginTop,
                isHeader: true);

            PlaceRunningBlock(
                pages[i],
                running.EffectiveFooter(selection),
                left,
                available,
                footerBaseline,
                setup.MarginBottom,
                isHeader: false);
        }
    }

    /// <summary>
    /// Places the document's floating shapes against the paragraphs they anchor to.
    /// </summary>
    /// <remarks>
    /// A shape's x is measured from the text column's left edge, so a letterhead's
    /// stripe sits in the margin without any page geometry; its y runs down from
    /// the top of its paragraph, which in PDF's upward user space is a subtraction.
    /// The shape's own text becomes ordinary placed runs, so it draws through the
    /// same path as everything else and lands above the box.
    /// </remarks>
    private void PlaceShapes(
        IReadOnlyList<DocumentShape> shapes,
        Dictionary<int, (PdfLayoutPage Page, double Top)> anchors,
        PdfPageSetup setup)
    {
        foreach (DocumentShape shape in shapes)
        {
            if (!anchors.TryGetValue(shape.ParagraphIndex, out (PdfLayoutPage Page, double Top) anchor))
                continue;

            if (shape.Width <= 0 || shape.Height <= 0)
                continue;

            if (shape.HasImage)
            {
                // The box is still placed, so a bordered picture leaves its frame
                // on the page rather than nothing at all.
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.WriteImageNotComposed,
                    "A floating image was dropped. This build composes no image emitter, so images are omitted rather than rasterized or transcoded.");
            }

            double left = setup.MarginLeft + shape.OffsetX;
            double top = anchor.Top - shape.OffsetY;
            anchor.Page.Shapes.Add(new PdfPlacedShape(
                left,
                top - shape.Height,
                shape.Width,
                shape.Height,
                shape.Fill,
                shape.Outline));

            if (shape.HasText)
                PlaceRunningBlock(anchor.Page, shape.Paragraphs, left, shape.Width, top, shape.Height, isHeader: true);
        }
    }

    /// <summary>Page one is the first page; pages two, four, six are the even ones.</summary>
    private static PageSelection SelectionForPage(int index) => index switch
    {
        0 => PageSelection.First,
        _ => (index + 1) % 2 == 0 ? PageSelection.Even : PageSelection.Default,
    };

    private void PlaceRunningBlock(
        PdfLayoutPage page,
        IReadOnlyList<RichTextParagraph> paragraphs,
        double left,
        double available,
        double firstBaseline,
        double margin,
        bool isHeader)
    {
        if (paragraphs.Count == 0)
            return;

        var lines = new List<(LayoutLine Line, ParagraphStyle Style, bool IsLast)>();
        double height = 0;
        foreach (RichTextParagraph paragraph in paragraphs)
        {
            List<LayoutLine> broken = BreakParagraph(paragraph, available, marker: string.Empty);
            double spacing = paragraph.Style.LineSpacing > 0 ? paragraph.Style.LineSpacing : 1f;
            for (int i = 0; i < broken.Count; i++)
            {
                lines.Add((broken[i], paragraph.Style, i == broken.Count - 1));
                height += broken[i].Height * spacing;
            }

            if (broken.Count == 0)
                height += EmptyLineHeight() * spacing;
        }

        if (height > margin)
        {
            _diagnostics.Warning(
                PdfDiagnosticCodes.WriteOverflow,
                isHeader
                    ? "A DOCX header was taller than the page's top margin and was not drawn."
                    : "A DOCX footer was taller than the page's bottom margin and was not drawn.");
            return;
        }

        double y = firstBaseline + (height / 2);
        foreach ((LayoutLine line, ParagraphStyle style, bool isLast) in lines)
        {
            double spacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;
            y -= line.Height * spacing;
            Place(page, line, left, available, y, style.Alignment, isLast);
        }
    }

    /// <summary>
    /// The page to lay out on: the one the document states, else the one the
    /// caller asked for.
    /// </summary>
    /// <remarks>
    /// The document wins because printing an A4 letter on US Letter, when the
    /// letter says A4 and nobody said otherwise, is the writer overruling the
    /// author. A document that states nothing, or states nonsense, still gets the
    /// caller's page.
    /// </remarks>
    internal PdfPageSetup SetupFor(RichTextDocument document)
    {
        if (document.PageGeometry is not PageGeometry geometry || !geometry.IsUsable)
            return _options.PageSetup;

        return new PdfPageSetup(
            geometry.Width,
            geometry.Height,
            geometry.MarginLeft,
            geometry.MarginRight,
            geometry.MarginTop,
            geometry.MarginBottom);
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

    /// <summary>
    /// Places one laid-out line at <paramref name="baseline"/>. Center and right
    /// move the whole line by the slack; justification spreads that slack into
    /// the line's own spaces instead, as PDF word spacing.
    /// </summary>
    /// <remarks>
    /// The last line of a paragraph is never justified: the slack there is
    /// whatever the text happened to leave, and stretching a two-word closing
    /// line across the column is the one thing every typesetter agrees is wrong.
    /// A line with no spaces to stretch — one long word — is left flush too,
    /// rather than having its glyphs pulled apart.
    /// </remarks>
    private void Place(
        PdfLayoutPage page,
        LayoutLine line,
        double left,
        double available,
        double baseline,
        TextAlignment alignment,
        bool isLastLine)
    {
        double slack = available - line.Width;
        double wordSpacing = 0;
        if (alignment == TextAlignment.Justify && !isLastLine && slack > 0)
        {
            int spaces = CountSpaces(line);
            if (spaces > 0)
                wordSpacing = slack / spaces;
        }

        double x = alignment switch
        {
            TextAlignment.Center => left + (slack / 2),
            TextAlignment.Right => left + slack,
            _ => left,
        };

        // Never start left of the margin, however the alignment arithmetic came out.
        if (x < left)
            x = left;

        foreach (LayoutPiece piece in line.Pieces)
        {
            int spacesHere = CountSpaces(piece.Text);
            if (piece.Text.Length > 0)
            {
                page.Runs.Add(new PdfPlacedRun(
                    piece.Text,
                    x,
                    baseline,
                    piece.Width + (spacesHere * wordSpacing),
                    piece.FontSize,
                    piece.Font,
                    piece.Color,
                    piece.Background,
                    piece.Underline,
                    piece.Strikethrough,
                    piece.LinkHref,
                    wordSpacing));
            }

            // Each run carries an absolute x, so a run has to start past the extra
            // width word spacing gave the spaces before it on this line.
            x += piece.Width + (spacesHere * wordSpacing);
        }
    }

    /// <summary>The spaces a line can stretch: PDF word spacing applies to the space byte.</summary>
    private static int CountSpaces(LayoutLine line)
    {
        int spaces = 0;
        foreach (LayoutPiece piece in line.Pieces)
            spaces += CountSpaces(piece.Text);

        return spaces;
    }

    private static int CountSpaces(string text)
    {
        int spaces = 0;
        foreach (char c in text)
        {
            if (c == ' ')
                spaces++;
        }

        return spaces;
    }

    // ---- line breaking --------------------------------------------------------

    private List<LayoutLine> BreakParagraph(RichTextParagraph paragraph, double available, string marker)
    {
        var lines = new List<LayoutLine>();
        var current = new LayoutLine();
        double used = 0;

        foreach (Word enumerated in EnumerateWords(paragraph, marker))
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // What a tab is worth is the distance to the stop it lands on, so it
            // can only be measured here, where the line's used width is known.
            Word word = enumerated.IsTab
                ? enumerated.WithText(string.Empty, NextTabStop(used) - used)
                : enumerated;

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

        // A tab is a gap, not glyphs, so it stays its own piece: merging it into
        // its neighbours would fold its width into a run the viewer sets from the
        // font's own advances, and the text after it would close the gap up.
        if (last is not null && !last.IsTab && !word.IsTab && last.SameStyleAs(word))
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
            if (last.IsTab)
            {
                line.Width -= last.Width;
                line.Pieces.RemoveAt(line.Pieces.Count - 1);
                continue;
            }

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
            // A tab is its own word: its width is the distance to the tab stop it
            // reaches, so it can be neither absorbed into the word in front of it
            // nor measured from the font.
            if (text[index] == '\t')
            {
                index++;
                yield return Word.Tab(style);
                continue;
            }

            int start = index;
            while (index < text.Length && !IsBreakSpace(text[index]))
                index++;

            // Absorb the run of spaces that follows the word.
            int wordEnd = index;
            while (index < text.Length && text[index] == ' ')
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

    /// <summary>
    /// The width a line has used once the tab reaching <paramref name="used"/> has
    /// landed: the first tab stop strictly past it, so a tab always moves the text
    /// along even when it starts exactly on a stop.
    /// </summary>
    private static double NextTabStop(double used) =>
        (Math.Floor(Math.Max(0, used) / TabStopWidth) + 1) * TabStopWidth;

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
        public Word(string text, double width, RunStyle style, bool isSpace, bool isTab = false)
        {
            Text = text;
            Width = width;
            Style = style;
            IsSpace = isSpace;
            IsTab = isTab;
        }

        /// <summary>
        /// A tab, carrying its style and no width yet: the line it lands on is what
        /// decides how far it reaches.
        /// </summary>
        public static Word Tab(RunStyle style) => new(string.Empty, 0, style, isSpace: true, isTab: true);

        public string Text { get; }

        public double Width { get; }

        public RunStyle Style { get; }

        /// <summary>True when the word is only whitespace.</summary>
        public bool IsSpace { get; }

        /// <summary>True for a tab: a gap of measured width that draws no glyphs.</summary>
        public bool IsTab { get; }

        public PdfStandardFont Font => Style.Font;

        public double FontSize => Style.FontSize;

        public Word WithText(string text, double width) => new(text, width, Style, IsSpace, IsTab);

        public LayoutPiece ToPiece() => new(Text, Width, Style, IsTab);
    }

    private sealed class LayoutPiece
    {
        public LayoutPiece(string text, double width, RunStyle style, bool isTab = false)
        {
            Text = text;
            Width = width;
            Style = style;
            IsTab = isTab;
        }

        public string Text { get; }

        /// <summary>True for a tab: a gap of measured width that draws no glyphs.</summary>
        public bool IsTab { get; }

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
