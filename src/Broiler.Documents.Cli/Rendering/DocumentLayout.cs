using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// Turns a <see cref="RichTextDocument"/> into positioned lines on pages.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is.</b> A deterministic, single-column paragraph layout: word
/// wrapping, alignment, indents, list markers, line and paragraph spacing,
/// inline images, and pagination. It measures through
/// <see cref="BTextMeasurer"/>, which is the same path the renderer advances its
/// pen along, so what was measured is what gets drawn.
/// </para>
/// <para>
/// <b>What this is not.</b> It is not a word processor's layout engine and does
/// not try to be one. There are no tables, columns, floats, footnotes, headers,
/// footers, hyphenation, kerning pairs, or bidirectional reordering here -
/// mostly because the document model has no way to express them, so there is
/// nothing to lay out. The shared paginator the PDF roadmap tracks as
/// <c>Broiler.Documents.Pagination</c> is where a component-level version of
/// this belongs; until it exists this is an application head's own layout, and
/// its numbers are this tool's, not the component's.
/// </para>
/// <para>
/// <b>Why that is still useful for finding gaps.</b> A comparison between two
/// exports run through <em>this same</em> layout isolates the codecs: identical
/// geometry on both sides means every pixel that differs came from the document
/// model, which is to say from the reader or the writer under test.
/// </para>
/// </remarks>
public sealed class DocumentLayout
{
    private readonly LayoutSettings _settings;
    private readonly ImageStore _images;
    private readonly List<string> _notes = new();

    public DocumentLayout(LayoutSettings settings, ImageStore images)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _images = images ?? throw new ArgumentNullException(nameof(images));
    }

    /// <summary>Lays the document out onto pages of the given size.</summary>
    public LayoutResult Layout(RichTextDocument document, PageSetup setup)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(setup);

        _notes.Clear();

        // A continuous render has no page break to place, so it lays out against
        // an effectively unbounded column and then shrinks the page to the
        // content. That is the form to reach for when comparing two exports:
        // with pagination on, one extra line before a break moves every
        // subsequent page and a one-line difference reads as a whole-document one.
        double columnHeight = setup.Continuous ? double.MaxValue : setup.ContentHeightPoints;

        var pages = new List<LayoutPage>();
        var currentLines = new List<LayoutLine>();
        double y = setup.ContentTopPoints;
        double contentBottom = setup.ContentTopPoints + columnHeight;
        bool truncated = false;

        var numbering = new ListNumbering();

        for (int paragraphIndex = 0; paragraphIndex < document.ParagraphCount && !truncated; paragraphIndex++)
        {
            RichTextParagraph paragraph = document.Paragraphs[paragraphIndex];
            ParagraphStyle style = paragraph.Style;
            string? marker = numbering.Advance(style);

            ParagraphLines composed = ComposeParagraph(paragraph, marker, setup, paragraphIndex);

            // Space before never opens a page: a paragraph that starts a page
            // starts at the top margin, the way every page-based renderer does it.
            if (currentLines.Count > 0)
                y += Math.Max(0, style.SpacingBefore);

            foreach (LayoutLine line in composed.Lines)
            {
                if (y + line.Height > contentBottom && currentLines.Count > 0)
                {
                    pages.Add(NewPage(pages.Count + 1, setup, currentLines));
                    currentLines = new List<LayoutLine>();
                    y = setup.ContentTopPoints;

                    if (pages.Count >= _settings.MaxPages)
                    {
                        truncated = true;
                        break;
                    }
                }

                line.Top = y;
                currentLines.Add(line);
                y += line.Height;
            }

            y += Math.Max(0, style.SpacingAfter);
        }

        if (currentLines.Count > 0 || pages.Count == 0)
            pages.Add(NewPage(pages.Count + 1, setup, currentLines));

        if (truncated)
        {
            _notes.Add(string.Format(
                CultureInfo.InvariantCulture,
                "stopped at the --max-pages limit of {0}; the document has more content.",
                _settings.MaxPages));
        }

        PageSetup finalSetup = setup;
        if (setup.Continuous)
        {
            double used = pages[0].Lines.Count > 0
                ? pages[0].Lines[^1].Top + pages[0].Lines[^1].Height
                : setup.ContentTopPoints;
            finalSetup = setup.WithHeight(used + setup.MarginBottomPoints);
            pages = new List<LayoutPage>
            {
                new(1, finalSetup.WidthPoints, finalSetup.HeightPoints, pages[0].Lines.ToList()),
            };
        }

        return new LayoutResult(pages, finalSetup, _notes, truncated);
    }

    private LayoutPage NewPage(int number, PageSetup setup, List<LayoutLine> lines) =>
        new(number, setup.WidthPoints, setup.HeightPoints, lines);

    /// <summary>Wraps one paragraph into lines, without deciding which page they land on.</summary>
    private ParagraphLines ComposeParagraph(
        RichTextParagraph paragraph,
        string? marker,
        PageSetup setup,
        int paragraphIndex)
    {
        ParagraphStyle style = paragraph.Style;
        double indent = Math.Max(0, style.IndentLevel) * _settings.IndentStepPoints;
        double columnLeft = setup.ContentLeftPoints + indent;
        double columnWidth = Math.Max(1.0, setup.ContentWidthPoints - indent);

        BFontStyle defaultFont = FontFor(InlineStyle.Default);
        LayoutPiece? markerPiece = null;
        double hang = 0;

        if (marker is not null)
        {
            InlineStyle markerStyle = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : InlineStyle.Default;
            markerPiece = MakeTextPiece(marker, markerStyle, decorateLink: false);
            hang = Math.Min(columnWidth * 0.5, markerPiece.Width + _settings.ListMarkerGapPoints);
        }

        double textLeft = columnLeft + hang;
        double textWidth = Math.Max(1.0, columnWidth - hang);

        List<Token> tokens = Tokenize(paragraph);
        List<List<LayoutPiece>> rows = Wrap(tokens, textWidth);

        var lines = new List<LayoutLine>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            List<LayoutPiece> pieces = rows[i];

            // The marker belongs to the first line only, and sits in the hanging
            // indent rather than in the text column, so wrapped lines align under
            // the text and not under the bullet.
            if (i == 0 && markerPiece is not null)
            {
                markerPiece.X = columnLeft;
                pieces.Insert(0, markerPiece);
            }

            lines.Add(PlaceLine(pieces, i == 0 && markerPiece is not null, textLeft, textWidth, style, defaultFont, paragraphIndex));
        }

        return new ParagraphLines(lines);
    }

    /// <summary>
    /// Positions one line's pieces, applies alignment, and computes the line box.
    /// </summary>
    private LayoutLine PlaceLine(
        List<LayoutPiece> pieces,
        bool hasMarker,
        double textLeft,
        double textWidth,
        ParagraphStyle style,
        BFontStyle defaultFont,
        int paragraphIndex)
    {
        double ascent = BTextMeasurer.Measure(string.Empty, defaultFont).Baseline;
        double descent = Math.Max(0, BTextMeasurer.GetLineHeight(defaultFont) - ascent);
        double used = 0;

        // The marker is placed already and sits outside the text column, so it
        // contributes to the line's height but not to the width alignment
        // distributes.
        int first = hasMarker ? 1 : 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            ascent = Math.Max(ascent, pieces[i].Ascent);
            descent = Math.Max(descent, pieces[i].Descent);
            if (i >= first)
                used += pieces[i].Width;
        }

        double offset = style.Alignment switch
        {
            TextAlignment.Center => Math.Max(0, (textWidth - used) / 2),
            TextAlignment.Right => Math.Max(0, textWidth - used),
            _ => 0,
        };

        double x = textLeft + offset;
        for (int i = first; i < pieces.Count; i++)
        {
            pieces[i].X = x;
            x += pieces[i].Width;
        }

        double natural = ascent + descent;
        double spacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;

        // Extra leading goes below the baseline. Putting it above would push the
        // first line of every double-spaced paragraph down by half a line, which
        // is not what a document that says "line spacing 2" is asking for.
        return new LayoutLine(pieces, 0, natural * spacing, ascent, paragraphIndex);
    }

    /// <summary>
    /// Greedy first-fit wrapping. Break opportunities are whitespace runs; a
    /// single token wider than the column is split by character so that one long
    /// URL cannot push a page off its own right edge.
    /// </summary>
    private List<List<LayoutPiece>> Wrap(List<Token> tokens, double maxWidth)
    {
        var rows = new List<List<LayoutPiece>>();
        var current = new List<LayoutPiece>();
        var pendingSpace = new List<Token>();
        double currentWidth = 0;
        double pendingWidth = 0;

        void Flush()
        {
            rows.Add(current);
            current = new List<LayoutPiece>();
            currentWidth = 0;
            pendingSpace.Clear();
            pendingWidth = 0;
        }

        foreach (Token token in tokens)
        {
            if (token.IsWhitespace)
            {
                // Leading whitespace on a wrapped line is dropped; whitespace
                // inside a line is held back until a word arrives to justify it,
                // so a line never ends with a visible ragged space.
                if (current.Count == 0)
                    continue;

                pendingSpace.Add(token);
                pendingWidth += token.Width;
                continue;
            }

            if (current.Count > 0 && currentWidth + pendingWidth + token.Width > maxWidth)
                Flush();

            if (current.Count == 0 && token.Width > maxWidth)
            {
                foreach (Token chunk in BreakToken(token, maxWidth))
                {
                    if (current.Count > 0 && currentWidth + chunk.Width > maxWidth)
                        Flush();

                    current.AddRange(chunk.Pieces);
                    currentWidth += chunk.Width;
                }

                continue;
            }

            foreach (Token space in pendingSpace)
            {
                current.AddRange(space.Pieces);
                currentWidth += space.Width;
            }

            pendingSpace.Clear();
            pendingWidth = 0;

            current.AddRange(token.Pieces);
            currentWidth += token.Width;
        }

        rows.Add(current);
        return rows;
    }

    /// <summary>Splits an over-wide token into chunks that fit, one character at a time.</summary>
    private IEnumerable<Token> BreakToken(Token token, double maxWidth)
    {
        foreach (LayoutPiece piece in token.Pieces)
        {
            if (piece.IsImage)
            {
                // An image cannot be broken, so an over-wide one is scaled to the
                // column instead. Letting it keep its size would put pixels past
                // the right margin, where the page clip silently eats them.
                yield return Token.Single(piece.Width > maxWidth ? ScaleToWidth(piece, maxWidth) : piece);
                continue;
            }

            if (piece.Width <= maxWidth)
            {
                yield return Token.Single(piece);
                continue;
            }

            var builder = new StringBuilder();
            double width = 0;

            foreach (char character in piece.Text)
            {
                double advance = BTextMeasurer.MeasureAdvance(character.ToString(), piece.Font);
                if (builder.Length > 0 && width + advance > maxWidth)
                {
                    yield return Token.Single(Retext(piece, builder.ToString(), width));
                    builder.Clear();
                    width = 0;
                }

                builder.Append(character);
                width += advance;
            }

            if (builder.Length > 0)
                yield return Token.Single(Retext(piece, builder.ToString(), width));
        }
    }

    /// <summary>The same image piece drawn narrower, keeping its aspect ratio.</summary>
    private static LayoutPiece ScaleToWidth(LayoutPiece piece, double width)
    {
        double factor = width / piece.Width;
        return new LayoutPiece(
            piece.Text,
            piece.Font,
            piece.Color,
            piece.Highlight,
            piece.Underline,
            piece.Strikethrough,
            piece.Link,
            piece.Image,
            width,
            piece.Ascent * factor,
            piece.Descent * factor);
    }

    private static LayoutPiece Retext(LayoutPiece source, string text, double width) => new(
        text,
        source.Font,
        source.Color,
        source.Highlight,
        source.Underline,
        source.Strikethrough,
        source.Link,
        null,
        width,
        source.Ascent,
        source.Descent);

    /// <summary>
    /// Splits a paragraph into wrap tokens. A word that spans two runs - "very"
    /// in one and "**bold**" in the next - stays one token, because a break
    /// between them would be a break in the middle of a word.
    /// </summary>
    private List<Token> Tokenize(RichTextParagraph paragraph)
    {
        var tokens = new List<Token>();
        Token? word = null;
        int offset = 0;

        foreach (StyleRun run in paragraph.Runs)
        {
            int length = Math.Min(run.Length, Math.Max(0, paragraph.Length - offset));
            if (length <= 0)
            {
                offset += run.Length;
                continue;
            }

            string text = paragraph.Text.Substring(offset, length);
            offset += run.Length;

            foreach ((string fragment, bool whitespace, bool image) in Fragments(text, run.Style))
            {
                if (image)
                {
                    LayoutPiece piece = MakeImagePiece(run.Style);
                    word ??= Token.Empty();
                    word.Add(piece);
                    tokens.Add(word);
                    word = null;
                    continue;
                }

                if (whitespace)
                {
                    if (word is not null)
                    {
                        tokens.Add(word);
                        word = null;
                    }

                    Token space = Token.Empty(isWhitespace: true);
                    foreach (LayoutPiece piece in MakePieces(fragment, run.Style))
                        space.Add(piece);
                    tokens.Add(space);
                    continue;
                }

                word ??= Token.Empty();
                foreach (LayoutPiece piece in MakePieces(fragment, run.Style))
                    word.Add(piece);
            }
        }

        if (word is not null)
            tokens.Add(word);

        return tokens;
    }

    /// <summary>Splits run text into whitespace runs, image placeholders, and words.</summary>
    private static IEnumerable<(string Text, bool Whitespace, bool Image)> Fragments(string text, InlineStyle style)
    {
        var builder = new StringBuilder();
        bool? whitespace = null;

        foreach (char character in text)
        {
            if (character == InlineImage.Placeholder && style.IsImage)
            {
                if (builder.Length > 0)
                {
                    yield return (builder.ToString(), whitespace ?? false, false);
                    builder.Clear();
                    whitespace = null;
                }

                yield return (string.Empty, false, true);
                continue;
            }

            bool isSpace = char.IsWhiteSpace(character);
            if (whitespace is not null && isSpace != whitespace)
            {
                yield return (builder.ToString(), whitespace.Value, false);
                builder.Clear();
            }

            whitespace = isSpace;
            builder.Append(character);
        }

        if (builder.Length > 0)
            yield return (builder.ToString(), whitespace ?? false, false);
    }

    /// <summary>
    /// The drawable pieces for a fragment. Usually one; small capitals produce
    /// two sizes and therefore more than one.
    /// </summary>
    private IEnumerable<LayoutPiece> MakePieces(string text, InlineStyle style)
    {
        if (style.Capitalization != TextCapitalization.SmallCaps)
        {
            yield return MakeTextPiece(Transform(text, style.Capitalization), style, _settings.DecorateLinks);
            yield break;
        }

        // Small capitals: letters the author typed in lower case are drawn as
        // capitals at a reduced size, everything else at full size. Splitting at
        // that boundary is what lets both halves be measured in the size they
        // are actually drawn in.
        int start = 0;
        bool? small = null;

        for (int i = 0; i <= text.Length; i++)
        {
            bool? current = i < text.Length ? char.IsLower(text[i]) : null;
            if (i == text.Length || (small is not null && current != small))
            {
                string slice = text[start..i].ToUpperInvariant();
                if (slice.Length > 0)
                    yield return MakeTextPiece(slice, style, _settings.DecorateLinks, small == true ? 0.8 : 1.0);
                start = i;
            }

            small = current;
        }
    }

    private LayoutPiece MakeTextPiece(string text, InlineStyle style, bool decorateLink, double sizeScale = 1.0)
    {
        BFontStyle font = FontFor(style, sizeScale);
        BColor color = ColorText.Or(style.Foreground, _settings.DefaultForeground);
        bool underline = style.Underline;

        if (decorateLink && style.IsLink)
        {
            underline = true;
            if (style.Foreground.IsEmpty)
                color = _settings.LinkColor;
        }

        double width = BTextMeasurer.MeasureAdvance(text, font);
        double ascent = font.SizeInPixels * 0.8;
        double descent = Math.Max(0, BTextMeasurer.GetLineHeight(font) - ascent);

        // Shearing is a fallback for a family with no designed italic face. It
        // does not change the advance, so nothing in the layout moves either way.
        bool oblique = style.Italic &&
            _settings.SynthesizeItalic &&
            !(_settings.ItalicFaceAvailable?.Invoke(font.FamilyName) ?? false);

        return new LayoutPiece(
            text,
            font,
            color,
            style.Background,
            underline,
            style.Strikethrough,
            style.LinkHref,
            null,
            width,
            ascent,
            descent,
            oblique);
    }

    private LayoutPiece MakeImagePiece(InlineStyle style)
    {
        InlineImage image = style.Image!;
        (double width, double height) = _images.MeasurePoints(image);

        return new LayoutPiece(
            string.Empty,
            FontFor(style),
            ColorText.Or(style.Foreground, _settings.DefaultForeground),
            style.Background,
            false,
            false,
            style.LinkHref,
            image,
            Math.Max(1, width),
            Math.Max(1, height),
            0);
    }

    private BFontStyle FontFor(InlineStyle style, double sizeScale = 1.0)
    {
        double size = style.FontSize is > 0 ? style.FontSize.Value : _settings.DefaultFontSizePoints;
        string family = string.IsNullOrWhiteSpace(style.FontFamily)
            ? _settings.DefaultFontFamily
            : style.FontFamily!;

        return new BFontStyle(
            family,
            Math.Max(1.0, size * sizeScale),
            style.Bold ? BFontWeight.Bold : BFontWeight.Normal,
            style.Italic ? BFontSlant.Italic : BFontSlant.Normal);
    }

    private static string Transform(string text, TextCapitalization capitalization) => capitalization switch
    {
        TextCapitalization.AllCaps => text.ToUpperInvariant(),
        _ => text,
    };

    /// <summary>The lines one paragraph produced, before pagination places them.</summary>
    private sealed class ParagraphLines
    {
        public ParagraphLines(List<LayoutLine> lines) => Lines = lines;

        public List<LayoutLine> Lines { get; }
    }

    /// <summary>An unbreakable run of pieces: one word, one whitespace gap, or one image.</summary>
    private sealed class Token
    {
        private Token(bool isWhitespace) => IsWhitespace = isWhitespace;

        public bool IsWhitespace { get; }

        public List<LayoutPiece> Pieces { get; } = new();

        public double Width { get; private set; }

        public static Token Empty(bool isWhitespace = false) => new(isWhitespace);

        public static Token Single(LayoutPiece piece)
        {
            var token = new Token(false);
            token.Add(piece);
            return token;
        }

        public void Add(LayoutPiece piece)
        {
            Pieces.Add(piece);
            Width += piece.Width;
        }
    }

    /// <summary>
    /// Tracks list counters across paragraphs and produces the marker text.
    /// </summary>
    /// <remarks>
    /// The model records that a paragraph is in a numbered list at a given
    /// indent level and nothing else - there is no list identity, no start
    /// number, and no restart flag. So the rule here is the simple one those
    /// facts support: a counter per level, deeper levels reset when a shallower
    /// item appears, and every counter resets at the first paragraph that is not
    /// a list item. Documents whose original numbering said otherwise lost that
    /// on the way into the model, not here.
    /// </remarks>
    private sealed class ListNumbering
    {
        private const string Bullets = "•◦▪";

        private readonly List<int> _counters = new();

        public string? Advance(ParagraphStyle style)
        {
            int level = Math.Max(0, style.IndentLevel);

            if (style.ListKind == ListKind.None)
            {
                _counters.Clear();
                return null;
            }

            if (style.ListKind == ListKind.Bullet)
            {
                Truncate(level);
                return Bullets[level % Bullets.Length].ToString();
            }

            Truncate(level);
            while (_counters.Count <= level)
                _counters.Add(0);

            _counters[level]++;
            return Format(_counters[level], level) + ".";
        }

        private void Truncate(int level)
        {
            if (_counters.Count > level + 1)
                _counters.RemoveRange(level + 1, _counters.Count - level - 1);
        }

        private static string Format(int value, int level) => (level % 3) switch
        {
            0 => value.ToString(CultureInfo.InvariantCulture),
            1 => Alphabetic(value),
            _ => Roman(value),
        };

        private static string Alphabetic(int value)
        {
            var builder = new StringBuilder();
            while (value > 0)
            {
                value--;
                builder.Insert(0, (char)('a' + (value % 26)));
                value /= 26;
            }

            return builder.Length == 0 ? "a" : builder.ToString();
        }

        private static string Roman(int value)
        {
            if (value <= 0 || value >= 4000)
                return value.ToString(CultureInfo.InvariantCulture);

            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };

            var builder = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                while (value >= values[i])
                {
                    builder.Append(symbols[i]);
                    value -= values[i];
                }
            }

            return builder.ToString();
        }
    }
}
