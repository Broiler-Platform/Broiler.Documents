using System.Collections.Generic;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination;

/// <summary>A UTF-16 range in the exact display string passed to a renderer.</summary>
public readonly record struct LayoutDisplayRange(int Offset, int Length);

/// <summary>Visual inline offsets for the logical start and end of a display range.</summary>
public readonly record struct LayoutVisualTextRange(double Start, double End);

/// <summary>
/// Measures resolved display ranges with the same shaping engine that draws them.
/// Returns null when the renderer cannot supply geometry for the run.
/// </summary>
public delegate IReadOnlyList<LayoutVisualTextRange>? LayoutVisualTextMeasurer(
    string displayText, BFontStyle font, IReadOnlyList<LayoutDisplayRange> ranges);
