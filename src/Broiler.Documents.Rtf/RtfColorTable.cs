using System.Collections.Generic;
using Broiler.Graphics;

namespace Broiler.Documents.Rtf;

/// <summary>
/// The document's <c>\colortbl</c>: an ordered list of colors indexed by
/// <c>\cfN</c>/<c>\highlightN</c>. The conventional first entry (an empty
/// <c>;</c>) is the "auto" color and maps to <see cref="BColor.Empty"/>.
/// </summary>
internal sealed class RtfColorTable
{
    private readonly List<BColor> _colors = [];

    public int Count => _colors.Count;

    public void Add(BColor color) => _colors.Add(color);

    /// <summary>The color at <paramref name="index"/>, or <see cref="BColor.Empty"/> if out of range.</summary>
    public BColor Get(int index) =>
        index >= 0 && index < _colors.Count ? _colors[index] : BColor.Empty;
}
