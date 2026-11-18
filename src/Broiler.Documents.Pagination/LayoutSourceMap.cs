using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Model.Fonts;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination;

/// <summary>
/// A selectable source cluster. Offsets are UTF-16 positions in the line's source
/// paragraph. Advances are points from the positioned piece's origin.
/// </summary>
public readonly record struct LayoutSourceCluster(
    int SourceOffset, int SourceLength, double StartAdvance, double EndAdvance);

// Keep display spans through wrapping. Only the final piece knows its actual allocated width.
internal sealed class LayoutSourceMap(IDocumentTextMeasurer? measurer, LayoutSourceMap.Span[] spans,
    LayoutVisualTextMeasurer? visualMeasurer = null)
{
    internal readonly record struct Span(int Source, int Length, int DisplayStart, int DisplayEnd);

    internal static LayoutSourceMap Atomic(int offset, int displayLength) =>
        new(null, [new Span(offset, 1, 0, displayLength)]);

    internal static LayoutSourceMap Text(int[] starts, int paragraphLength, int offset, int length,
        int displayLength, IDocumentTextMeasurer? measurer, LayoutVisualTextMeasurer? visualMeasurer)
    {
        int end = offset + length;
        int first = Array.BinarySearch(starts, offset);
        if (first < 0) first = Math.Max(0, ~first - 1);
        var spans = new List<Span>();
        for (int i = first; i < starts.Length && starts[i] < end; i++)
        {
            int start = starts[i];
            int next = i + 1 < starts.Length ? starts[i + 1] : paragraphLength;
            spans.Add(new Span(start, next - start, Math.Max(start, offset) - offset, Math.Min(next, end) - offset));
        }

        // Current case and Symbol transforms preserve UTF-16 length. If a transform changes
        // length, keep its source extent atomic instead of inventing interior caret positions.
        if (length != displayLength && spans.Count > 0)
        {
            Span last = spans[^1];
            return new LayoutSourceMap(measurer,
                [new Span(spans[0].Source, last.Source + last.Length - spans[0].Source, 0, displayLength)], visualMeasurer);
        }
        return new LayoutSourceMap(measurer, spans.ToArray(), visualMeasurer);
    }

    internal LayoutSourceMap Slice(int skip, int length)
    {
        var slice = new List<Span>();
        int end = skip + length;
        int low = 0;
        int high = spans.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (spans[middle].DisplayEnd <= skip) low = middle + 1;
            else high = middle;
        }
        for (int i = low; i < spans.Length && spans[i].DisplayStart < end; i++)
        {
            Span span = spans[i];
            slice.Add(span with { DisplayStart = Math.Max(skip, span.DisplayStart) - skip,
                DisplayEnd = Math.Min(end, span.DisplayEnd) - skip });
        }
        return new LayoutSourceMap(measurer, slice.ToArray(), visualMeasurer);
    }

    internal IReadOnlyList<LayoutSourceCluster> Measure(LayoutPiece piece)
    {
        if (spans.Length == 0) return [];
        double extent = Math.Max(0, piece.Width + ExtraSpace(piece.Text.AsSpan()));
        // A joiner on its own can join emoji and does not change their direction.
        if (ComplexTextShaper.RequiresShaping(piece.Text.Replace("\u200D", "", StringComparison.Ordinal)) ||
            piece.Text.Any(IsDirectionalControl))
        {
            var ranges = spans.Select(span => new LayoutDisplayRange(span.DisplayStart, span.DisplayEnd - span.DisplayStart)).ToArray();
            var visual = visualMeasurer?.Invoke(piece.Text, piece.Font, ranges);
            if (visual?.Count == spans.Length && visual.All(range => double.IsFinite(range.Start) && double.IsFinite(range.End)))
            {
                // The renderer offsets are relative to the same origin used to draw the run.
                double left = visual.Min(range => Math.Min(range.Start, range.End));
                double right = visual.Max(range => Math.Max(range.Start, range.End));
                if (right > left)
                {
                    piece.HasResolvedVisualSourceGeometry = true;
                    return Array.AsReadOnly(spans.Select((span, i) => new LayoutSourceCluster(span.Source, span.Length,
                        visual[i].Start, visual[i].End)).ToArray());
                }
            }

            return Array.AsReadOnly(spans.Select(span => new LayoutSourceCluster(span.Source, span.Length, 0, 0)).ToArray());
        }

        piece.HasResolvedVisualSourceGeometry = true;
        if (spans.Length == 1)
            return Array.AsReadOnly(new[] { new LayoutSourceCluster(spans[0].Source, spans[0].Length, 0, extent) });

        var clusters = new LayoutSourceCluster[spans.Length];
        double full = Advance(piece.Text);
        double start = 0;
        for (int i = 0; i < spans.Length; i++)
        {
            Span span = spans[i];
            double end = i == spans.Length - 1 ? extent
                : full > 0 ? Math.Clamp(Advance(piece.Text[..span.DisplayEnd]) / full * piece.Width +
                    ExtraSpace(piece.Text.AsSpan(0, span.DisplayEnd)), start, extent)
                : start;
            clusters[i] = new LayoutSourceCluster(span.Source, span.Length, start, end);
            start = end;
        }
        return Array.AsReadOnly(clusters);

        double Advance(string text) => measurer?.MeasureAdvance(text, piece.Font) ?? BTextMeasurer.MeasureAdvance(text, piece.Font);
        static bool IsDirectionalControl(char c) => c is '\u061C' or '\u200E' or '\u200F' or
            >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069';
        double ExtraSpace(ReadOnlySpan<char> text)
        {
            if (piece.SourceExtraSpace == 0) return 0;
            int spaces = 0;
            foreach (char c in text)
                if (c == ' ') spaces++;
            return spaces * piece.SourceExtraSpace;
        }
    }
}
