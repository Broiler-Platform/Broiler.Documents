using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>A finite chain of page styles followed by a repeating suffix.</summary>
public sealed class PageStyleSequence(IReadOnlyList<PageStyle> styles, int repeatFrom)
{
    public IReadOnlyList<PageStyle> Styles { get; } = styles is null || styles.Count == 0
        ? throw new ArgumentException("A page style sequence must contain a style.", nameof(styles)) : [.. styles];

    public int RepeatFrom { get; } = Math.Clamp(repeatFrom, 0, Math.Max(0, styles.Count - 1));

    public PageStyle At(int pageOffset)
    {
        int index = Math.Max(0, pageOffset);
        if (index >= Styles.Count)
            index = RepeatFrom + (index - RepeatFrom) % (Styles.Count - RepeatFrom);
        return Styles[index];
    }
}

/// <summary>The paper and running content used by one page style.</summary>
public sealed record PageStyle(PageGeometry? Page, RunningContent RunningContent, ShapeFill? Background = null);
