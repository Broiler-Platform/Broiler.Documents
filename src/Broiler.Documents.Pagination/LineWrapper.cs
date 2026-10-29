using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Model.Text;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination;

/// <summary>Wraps measured pieces into rows, accounting for shape bands and tab stops.</summary>
internal sealed class LineWrapper(double tabStopPoints, IDocumentTextMeasurer? textMeasurer = null)
{
    private readonly double _tabStopPoints = tabStopPoints;
    private readonly IDocumentTextMeasurer? _textMeasurer = textMeasurer;

    /// <summary>
    /// Greedy first-fit wrapping. Break opportunities are whitespace runs; a
    /// single token wider than the column is split by character so that one long
    /// URL cannot push a page off its own right edge.
    /// </summary>
    internal List<List<LayoutPiece>> Wrap(List<LayoutToken> tokens, double maxWidth) =>
        Wrap(tokens, _ => new TextBand(0, maxWidth), bands: null);

    /// <remarks>
    /// <para>
    /// The width is asked for per row rather than given once, because a wrapping
    /// shape leaves each line a different amount of room depending on where the
    /// line lands. <paramref name="bands"/> collects what each row was given, so
    /// the caller can place it at the left edge it was wrapped to.
    /// </para>
    /// <para>
    /// With <paramref name="shrinks"/>, a row that a word would overflow may be
    /// set wider than the band by <see cref="DocumentStyleDefaults.JustifiedSpaceShrink"/>
    /// of a space for each space before the break and the one before that word,
    /// as LibreOffice breaks a justified line of a Word 2013 document; the row
    /// is then filled to that width. A row holding a tab does not shrink
    /// (tdf#164499).
    /// </para>
    /// <para>
    /// With <paramref name="tabsOverMargin"/>, a stop the paragraph states
    /// keeps its position and alignment past the band's right edge, and once a
    /// tab has reached such a stop the rest of the row runs on rather than
    /// wrapping (<see cref="DocumentStyleDefaults.TabsOverMargin"/>). That is
    /// LibreOffice's <c>TabOverMargin</c>: <c>SwTabPortion::PostFormat</c>
    /// aligns to the stop itself, and <c>SwTextFormatInfo::GetLineWidth</c>
    /// lets the text after it run to <see cref="DocumentStyleDefaults.TabOverMarginLimitPoints"/>.
    /// </para>
    /// </remarks>
    internal List<List<LayoutPiece>> Wrap(List<LayoutToken> tokens, Func<int, TextBand> bandFor, List<TextBand>? bands,
        ParagraphTabStops? tabStops = null, double indent = 0, bool shrinks = false, bool tabsOverMargin = false)
    {
        var rows = new List<List<LayoutPiece>>();
        var current = new List<LayoutPiece>();
        var pendingSpace = new List<LayoutToken>();
        double currentWidth = 0;
        double pendingWidth = 0;

        TextBand band = bandFor(0);
        bands?.Add(band);
        double maxWidth = Math.Max(1, band.Width);

        // The width the row may take by shrinking its spaces, once a word has
        // overflowed it.
        double? shrunkWidth = null;

        // Set once a tab has reached a stated stop past the band's right edge,
        // when tabs may go over the margin: the row then runs on.
        bool overMargin = false;

        void Flush()
        {
            // A line that breaks at a soft hyphen shows the hyphen.
            if (current.Count > 0 && pendingSpace.Count > 0 && pendingSpace[^1].Hyphen is LayoutPiece hyphen)
                current.Add(hyphen);

            rows.Add(current);
            current = [];
            currentWidth = 0;
            pendingSpace.Clear();
            pendingWidth = 0;
            shrunkWidth = null;
            overMargin = false;

            band = bandFor(rows.Count);
            bands?.Add(band);
            maxWidth = Math.Max(1, band.Width);
        }

        for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            LayoutToken token = tokens[tokenIndex];
            if (token.IsForcedBreak)
            {
                // The document says the line ends here, so it ends here even with
                // room to spare. The whitespace held back in front of the break is
                // dropped the way a wrapped line's trailing space is: it is the gap
                // between two words that are no longer on the same line.
                //
                // Justification is deliberately not touched. The line before a
                // forced break is stretched like any other non-final line, which
                // is measured rather than assumed - LibreOffice does the same, and
                // `isLastLine` below still names only the paragraph's final row.
                pendingSpace.Clear();
                pendingWidth = 0;
                Flush();
                continue;
            }

            if (token.IsWhitespace)
            {
                // Leading whitespace on a wrapped line is dropped; whitespace
                // inside a line is held back until a word arrives to justify it,
                // so a line never ends with a visible ragged space. A tab that
                // opens the paragraph is not that space: it is the indent the
                // author typed. Explicit tab stops also apply after manual breaks.
                if (current.Count == 0 && !(token.IsTab && (rows.Count == 0 || tabStops is { Count: > 0 })))
                    continue;

                if (token.IsTab)
                {
                    double reached = currentWidth + pendingWidth;
                    double origin = indent + band.Left;
                    ParagraphTabStop? explicitStop = null;
                    if (tabStops is not null)
                        foreach (ParagraphTabStop stop in tabStops)
                            if (stop.Position > origin + reached + 0.01)
                            {
                                explicitStop = stop;
                                break;
                            }
                    double target = NextTabStop(reached);
                    if (explicitStop is ParagraphTabStop selected)
                    {
                        double segmentWidth = 0;
                        double? decimalWidth = null;
                        double trailing = 0;
                        for (int next = tokenIndex + 1; next < tokens.Count && !tokens[next].IsTab && !tokens[next].IsForcedBreak; next++)
                        {
                            foreach (LayoutPiece piece in tokens[next].Pieces)
                            {
                                int separator = piece.Text.IndexOf(selected.DecimalCharacter);
                                if (decimalWidth is null && separator >= 0)
                                    decimalWidth = segmentWidth + (_textMeasurer is not null ? _textMeasurer.MeasureAdvance(piece.Text[..separator], piece.Font) : BTextMeasurer.MeasureAdvance(piece.Text[..separator], piece.Font));
                                segmentWidth += piece.Width;
                            }
                            trailing = tokens[next].IsWhitespace ? trailing + tokens[next].Width : 0;
                        }
                        segmentWidth -= trailing;
                        double alignment = selected.Alignment switch
                        {
                            TabAlignment.Right => segmentWidth,
                            TabAlignment.Center => segmentWidth / 2,
                            TabAlignment.Decimal => decimalWidth ?? segmentWidth,
                            _ => 0,
                        };
                        // Writer brings a right-aligned stop back to a narrow
                        // text frame's edge. TOC styles often retain the wider
                        // page's stop even when their paragraphs live in a frame.
                        // Over the margin, the stop stays where it is.
                        double position = selected.Alignment == TabAlignment.Right && !tabsOverMargin
                            ? Math.Min(selected.Position, origin + maxWidth) : selected.Position;
                        if (tabsOverMargin && selected.Position > origin + maxWidth + 0.01)
                            overMargin = true;
                        target = position - origin - alignment;
                        token.Pieces[0].TabLeader = selected.Leader;
                    }
                    token.ResolveTabWidth(Math.Max(0, target - reached));
                }

                pendingSpace.Add(token);
                pendingWidth += token.Width;
                continue;
            }

            if (overMargin)
            {
                if (currentWidth + pendingWidth + token.Width <= DocumentStyleDefaults.TabOverMarginLimitPoints)
                {
                    AddPending();
                    current.AddRange(token.Pieces);
                    currentWidth += token.Width;
                    continue;
                }

                Flush();
            }

            if (current.Count > 0 && currentWidth + pendingWidth + token.Width > (shrunkWidth ?? maxWidth) + 0.01)
            {
                if (shrinks && shrunkWidth is null)
                    shrunkWidth = maxWidth + ShrinkAllowance(current, pendingSpace);

                if (shrunkWidth is not double room || currentWidth + pendingWidth + token.Width > room + 0.01)
                    Flush();
            }

            if (current.Count == 0 && token.Width > maxWidth)
            {
                foreach (LayoutToken chunk in BreakToken(token, maxWidth))
                {
                    if (current.Count > 0 && currentWidth + chunk.Width > maxWidth)
                        Flush();

                    current.AddRange(chunk.Pieces);
                    currentWidth += chunk.Width;
                }

                continue;
            }

            AddPending();
            current.AddRange(token.Pieces);
            currentWidth += token.Width;
        }

        rows.Add(current);
        return rows;

        // Moves the whitespace held back into the row, now that a word follows it.
        void AddPending()
        {
            foreach (LayoutToken space in pendingSpace)
            {
                current.AddRange(space.Pieces);
                currentWidth += space.Width;
            }

            pendingSpace.Clear();
            pendingWidth = 0;
        }
    }

    /// <summary>
    /// How much wider than its band a justified row may be set by shrinking
    /// its spaces: a share of a space for each space in <paramref name="row"/>
    /// and one for the gap <paramref name="gap"/> before the word that
    /// overflowed it, nothing where there is no such gap or the row holds a tab.
    /// The space is the gap's, as LibreOffice measures one in the font of the
    /// text where the line breaks.
    /// </summary>
    private static double ShrinkAllowance(List<LayoutPiece> row, List<LayoutToken> gap)
    {
        if (gap.Count == 0 || gap.Any(token => token.IsTab) || row.Any(piece => piece.IsTab))
            return 0;

        LayoutPiece? space = gap[0].Pieces.FirstOrDefault(piece => piece.Text.Length > 0);
        if (space is null)
            return 0;

        int spaces = 1;
        foreach (LayoutPiece piece in row)
        {
            foreach (char character in piece.Text)
            {
                if (character == ' ')
                    spaces++;
            }
        }

        return spaces * space.Width / space.Text.Length * DocumentStyleDefaults.JustifiedSpaceShrink;
    }

    /// <summary>Splits an over-wide token into chunks that fit, one character at a time.</summary>
    private IEnumerable<LayoutToken> BreakToken(LayoutToken token, double maxWidth)
    {
        foreach (LayoutPiece piece in token.Pieces)
        {
            if (piece.IsImage)
            {
                // Office frames can deliberately extend beyond a narrow cell.
                // Their stated dimensions also determine the row's height and
                // pagination. Only an intrinsically sized picture is fitted to
                // the available width; an explicit extent must survive layout.
                bool explicitSize = piece.Image!.Width.HasValue || piece.Image.Height.HasValue;
                yield return LayoutToken.Single(!explicitSize && piece.Width > maxWidth ? ScaleToWidth(piece, maxWidth) : piece);
                continue;
            }

            if (piece.Width <= maxWidth)
            {
                yield return LayoutToken.Single(piece);
                continue;
            }

            var clusters = UnicodeBoundaryClassifier.GetGraphemeClusters(piece.Text);
            var builder = new StringBuilder();
            double width = 0;
            int consumed = 0;

            foreach (var cluster in clusters)
            {
                double advance = _textMeasurer is not null ? _textMeasurer.MeasureAdvance(cluster.Text, piece.Font) : BTextMeasurer.MeasureAdvance(cluster.Text, piece.Font);
                if (builder.Length > 0 && width + advance > maxWidth)
                {
                    yield return LayoutToken.Single(Retext(piece, builder.ToString(), width, consumed));
                    consumed += builder.Length;
                    builder.Clear();
                    width = 0;
                }

                builder.Append(cluster.Text);
                width += advance;

                // When a single indivisible cluster exceeds maxWidth on an empty row, terminate row
                if (builder.Length > 0 && width > maxWidth)
                {
                    yield return LayoutToken.Single(Retext(piece, builder.ToString(), width, consumed));
                    consumed += builder.Length;
                    builder.Clear();
                    width = 0;
                }
            }

            if (builder.Length > 0)
                yield return LayoutToken.Single(Retext(piece, builder.ToString(), width, consumed));
        }
    }

    /// <summary>The same image piece drawn narrower, keeping its aspect ratio.</summary>
    private static LayoutPiece ScaleToWidth(LayoutPiece piece, double width)
    {
        double factor = width / piece.Width;
        return new LayoutPiece(piece.Text, piece.Font, piece.Color, piece.Highlight, piece.Underline,
            piece.Strikethrough, piece.Link, piece.Image, width, piece.Ascent * factor, piece.Descent * factor,
            lineGap: piece.LineGap * factor)
        {
            SourceOffset = piece.SourceOffset,
        };
    }

    /// <summary>
    /// Part of <paramref name="source"/>: <paramref name="text"/>, which starts
    /// <paramref name="skipped"/> characters into it.
    /// </summary>
    private static LayoutPiece Retext(LayoutPiece source, string text, double width, int skipped) =>
        new(text, source.Font, source.Color, source.Highlight, source.Underline, 
            source.Strikethrough, source.Link, null, width, source.Ascent, source.Descent, lineGap: source.LineGap)
        {
            Rise = source.Rise,
            Note = source.Note,
            Revised = source.Revised,
            Doubled = source.Doubled,
            SourceOffset = source.SourceOffset < 0 ? -1 : source.SourceOffset + skipped,
        };

    /// <summary>
    /// The width a line has used once a tab reaching <paramref name="used"/> has
    /// landed: the first tab stop strictly past it, so a tab always moves the text
    /// along even when it starts exactly on a stop.
    /// </summary>
    private double NextTabStop(double used)
    {
        double stop = _tabStopPoints > 0 ? _tabStopPoints : 36.0;
        return (Math.Floor(Math.Max(0, used) / stop) + 1) * stop;
    }
}
