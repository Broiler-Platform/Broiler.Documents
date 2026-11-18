using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Documents.Model.Text;

/// <summary>A measured word or a breakable gap. The payload retains the caller's source information.</summary>
public readonly record struct ParagraphItem<T>(T Value, double Advance, bool IsGap = false, bool IsContextual = false);

/// <summary>A composed line in the same units as its input advances and available width.</summary>
public readonly record struct ComposedParagraphLine<T>(IReadOnlyList<T> Items, double Advance, int? NextSourceOffset = null);

/// <summary>
/// Greedy line composition for ordinary text. The caller supplies measured items and available
/// widths, and owns font resolution, line height, and placement of the composed lines.
/// </summary>
public static class ParagraphComposer
{
    /// <summary>
    /// Composes one paragraph. Gaps at a wrap are consumed without being drawn, and an
    /// oversized word is split by the caller at valid source boundaries.
    /// </summary>
    public static IReadOnlyList<ComposedParagraphLine<T>> Compose<T>(
        IEnumerable<ParagraphItem<T>> items, Func<int, double> widthForLine,
        Func<T, double, IEnumerable<ParagraphItem<T>>> splitWord) =>
        ComposeLines(items, widthForLine, splitWord).ToList().AsReadOnly();

    /// <summary>
    /// Yields each line before asking for the next width, so a caller can place it and
    /// resolve the next band. Source offsets allow dropped gaps to remain editable.
    /// </summary>
    public static IEnumerable<ComposedParagraphLine<T>> ComposeLines<T>(
        IEnumerable<ParagraphItem<T>> items, Func<int, double> widthForLine,
        Func<T, double, IEnumerable<ParagraphItem<T>>> splitWord, Func<T, int>? sourceStart = null,
        Func<T, ParagraphItem<T>?>? discretionaryBreak = null, Func<T, T, double, double>? measureContextual = null)
    {
        int lineIndex = 0;
        var line = new List<T>();
        var gaps = new List<T>();
        double used = 0;
        double pending = 0;
        double width = Math.Max(1, widthForLine(0));
        foreach (var item in items)
        {
            if (item.IsContextual && measureContextual is not null)
            {
                // Tabs are atomic, including on a line that is narrower than the stop.
                T first = line.Count > 0 ? line[0] : gaps.Count > 0 ? gaps[0] : item.Value;
                double advance = measureContextual(item.Value, first, used + pending);
                if (line.Count > 0 && used + pending + advance > width + 0.01)
                {
                    yield return Flush(item.Value);
                    width = Math.Max(1, widthForLine(++lineIndex));
                    advance = measureContextual(item.Value, item.Value, 0);
                }
                Add(item.Value, advance);
                continue;
            }
            if (item.IsGap)
            {
                if (line.Count > 0 || lineIndex == 0)
                {
                    gaps.Add(item.Value);
                    pending += item.Advance;
                }
                continue;
            }

            if (line.Count > 0 && used + pending + item.Advance > width + 0.01)
            {
                yield return Flush(item.Value);
                width = Math.Max(1, widthForLine(++lineIndex));
            }

            if (line.Count == 0 && item.Advance > width)
            {
                foreach (var part in splitWord(item.Value, width))
                {
                    if (line.Count > 0 && used + part.Advance > width)
                    {
                        yield return Flush(part.Value);
                        width = Math.Max(1, widthForLine(++lineIndex));
                    }
                    line.Add(part.Value);
                    used += part.Advance;
                }
                continue;
            }

            Add(item.Value, item.Advance);
        }

        yield return new ComposedParagraphLine<T>(line.AsReadOnly(), used);

        void Add(T item, double advance)
        {
            line.AddRange(gaps);
            used += pending;
            gaps.Clear();
            pending = 0;
            line.Add(item);
            used += advance;
        }

        ComposedParagraphLine<T> Flush(T next)
        {
            if (gaps.Count > 0 && discretionaryBreak?.Invoke(gaps[^1]) is { } replacement)
            {
                line.Add(replacement.Value);
                used += replacement.Advance;
            }
            var result = new ComposedParagraphLine<T>(line.AsReadOnly(), used, sourceStart?.Invoke(next));
            line = [];
            used = 0;
            gaps.Clear();
            pending = 0;
            return result;
        }
    }

    /// <summary>
    /// Splits an oversized string into measured chunks without dividing a grapheme. Offsets
    /// are UTF-16 positions in the supplied string. A grapheme wider than the line occupies it alone.
    /// </summary>
    public static IEnumerable<(int Offset, int Length, double Advance)> SplitText(
        string text, double width, Func<string, double> measure) =>
        SplitText(text, width, (offset, length) => measure(text.Substring(offset, length)));

    /// <summary>Splits an oversized string using source offsets for run-sensitive measurements.</summary>
    public static IEnumerable<(int Offset, int Length, double Advance)> SplitText(
        string text, double width, Func<int, int, double> measure)
    {
        int start = 0;
        int length = 0;
        double used = 0;
        foreach (GraphemeCluster cluster in UnicodeBoundaryClassifier.GetGraphemeClusters(text))
        {
            double advance = measure(cluster.Offset, cluster.Length);
            if (length > 0 && used + advance > width)
            {
                yield return (start, length, used);
                start += length;
                length = 0;
                used = 0;
            }

            length += cluster.Length;
            used += advance;
            if (used > width)
            {
                yield return (start, length, used);
                start += length;
                length = 0;
                used = 0;
            }
        }

        if (length > 0)
            yield return (start, length, used);
    }
}
