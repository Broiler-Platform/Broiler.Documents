using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination;

/// <summary>Wraps measured pieces into rows, accounting for shape bands and tab stops.</summary>
internal sealed class LineWrapper(double tabStopPoints)
{
    private readonly double _tabStopPoints = tabStopPoints;

    /// <summary>
    /// Greedy first-fit wrapping. Break opportunities are whitespace runs; a
    /// single token wider than the column is split by character so that one long
    /// URL cannot push a page off its own right edge.
    /// </summary>
    internal List<List<LayoutPiece>> Wrap(List<LayoutToken> tokens, double maxWidth) =>
        Wrap(tokens, _ => new TextBand(0, maxWidth), bands: null);

    /// <remarks>
    /// The width is asked for per row rather than given once, because a wrapping
    /// shape leaves each line a different amount of room depending on where the
    /// line lands. <paramref name="bands"/> collects what each row was given, so
    /// the caller can place it at the left edge it was wrapped to.
    /// </remarks>
    internal List<List<LayoutPiece>> Wrap(List<LayoutToken> tokens, Func<int, TextBand> bandFor, List<TextBand>? bands,
        ParagraphTabStops? tabStops = null, double indent = 0)
    {
        var rows = new List<List<LayoutPiece>>();
        var current = new List<LayoutPiece>();
        var pendingSpace = new List<LayoutToken>();
        double currentWidth = 0;
        double pendingWidth = 0;

        TextBand band = bandFor(0);
        bands?.Add(band);
        double maxWidth = Math.Max(1, band.Width);

        void Flush()
        {
            rows.Add(current);
            current = [];
            currentWidth = 0;
            pendingSpace.Clear();
            pendingWidth = 0;

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
                                    decimalWidth = segmentWidth + BTextMeasurer.MeasureAdvance(piece.Text[..separator], piece.Font);
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
                        double position = selected.Alignment == TabAlignment.Right
                            ? Math.Min(selected.Position, origin + maxWidth) : selected.Position;
                        target = position - origin - alignment;
                        token.Pieces[0].TabLeader = selected.Leader;
                    }
                    token.ResolveTabWidth(Math.Max(0, target - reached));
                }

                pendingSpace.Add(token);
                pendingWidth += token.Width;
                continue;
            }

            if (current.Count > 0 && currentWidth + pendingWidth + token.Width > maxWidth + 0.01)
                Flush();

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

            foreach (LayoutToken space in pendingSpace)
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
    private static IEnumerable<LayoutToken> BreakToken(LayoutToken token, double maxWidth)
    {
        foreach (LayoutPiece piece in token.Pieces)
        {
            if (piece.IsImage)
            {
                // An image cannot be broken, so an over-wide one is scaled to the
                // column instead. Letting it keep its size would put pixels past
                // the right margin, where the page clip silently eats them.
                yield return LayoutToken.Single(piece.Width > maxWidth ? ScaleToWidth(piece, maxWidth) : piece);
                continue;
            }

            if (piece.Width <= maxWidth)
            {
                yield return LayoutToken.Single(piece);
                continue;
            }

            var builder = new StringBuilder();
            double width = 0;

            foreach (char character in piece.Text)
            {
                double advance = BTextMeasurer.MeasureAdvance(character.ToString(), piece.Font);
                if (builder.Length > 0 && width + advance > maxWidth)
                {
                    yield return LayoutToken.Single(Retext(piece, builder.ToString(), width));
                    builder.Clear();
                    width = 0;
                }

                builder.Append(character);
                width += advance;
            }

            if (builder.Length > 0)
                yield return LayoutToken.Single(Retext(piece, builder.ToString(), width));
        }
    }

    /// <summary>The same image piece drawn narrower, keeping its aspect ratio.</summary>
    private static LayoutPiece ScaleToWidth(LayoutPiece piece, double width)
    {
        double factor = width / piece.Width;
        return new LayoutPiece(piece.Text, piece.Font, piece.Color, piece.Highlight, piece.Underline,
            piece.Strikethrough, piece.Link, piece.Image, width, piece.Ascent * factor, piece.Descent * factor,
            lineGap: piece.LineGap * factor);
    }

    private static LayoutPiece Retext(LayoutPiece source, string text, double width) =>
        new(text, source.Font, source.Color, source.Highlight, source.Underline, 
            source.Strikethrough, source.Link, null, width, source.Ascent, source.Descent, lineGap: source.LineGap)
        {
            Rise = source.Rise,
            Note = source.Note,
            Revised = source.Revised,
            Doubled = source.Doubled,
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
