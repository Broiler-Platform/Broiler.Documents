using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Model.Text;

namespace Broiler.Documents.Pagination.Drawing;

/// <summary>
/// A resolved, detached shaping record for a single text run drawn on a <see cref="Broiler.Graphics.Paging.BPage"/>.
/// Survives producer disposal and holds exact glyph advances, offsets, and cluster mappings.
/// </summary>
public sealed class ShapedPageRun
{
    public int CommandIndex { get; }
    public FontFaceIdentity FontFace { get; }
    public IReadOnlyList<ShapedCluster> Clusters { get; }
    public IReadOnlyList<ShapedGlyphInfo> Glyphs { get; }
    public TextDirection Direction { get; }

    public ShapedPageRun(
        int commandIndex,
        FontFaceIdentity fontFace,
        IEnumerable<ShapedCluster> clusters,
        IEnumerable<ShapedGlyphInfo> glyphs,
        TextDirection direction = TextDirection.LeftToRight)
    {
        ArgumentNullException.ThrowIfNull(fontFace);
        ArgumentNullException.ThrowIfNull(clusters);
        ArgumentNullException.ThrowIfNull(glyphs);

        CommandIndex = commandIndex;
        FontFace = fontFace;
        Clusters = new ReadOnlyCollection<ShapedCluster>([.. clusters]);
        Glyphs = new ReadOnlyCollection<ShapedGlyphInfo>([.. glyphs]);
        Direction = direction;
    }

    /// <summary>Sum of advances across all glyphs in typographical points.</summary>
    public double TotalAdvance
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < Glyphs.Count; i++)
                sum += Glyphs[i].Advance;
            return sum;
        }
    }
}

/// <summary>
/// Immutable container of resolved font and glyph shaping data traveling with a fixed page.
/// Detached from unmanaged font handles and live text measurers, allowing print, preview,
/// and PDF writers to replay exact shaping without remeasuring.
/// </summary>
public sealed class PageShapingManifest
{
    public static PageShapingManifest Empty { get; } = new([]);

    private readonly ReadOnlyDictionary<int, ShapedPageRun> _runsByCommand;
    private readonly ReadOnlyCollection<ShapedPageRun> _runs;

    public PageShapingManifest(IEnumerable<ShapedPageRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var list = new List<ShapedPageRun>(runs);
        var dict = new Dictionary<int, ShapedPageRun>();

        foreach (var run in list)
        {
            dict[run.CommandIndex] = run;
        }

        _runs = list.AsReadOnly();
        _runsByCommand = new ReadOnlyDictionary<int, ShapedPageRun>(dict);
    }

    /// <summary>All shaped runs recorded on this page.</summary>
    public IReadOnlyList<ShapedPageRun> Runs => _runs;

    /// <summary>Finds the shaping record for the <see cref="Broiler.Graphics.RenderList.BRenderCommand.DrawText"/> at the specified command index.</summary>
    public ShapedPageRun? GetRunForCommand(int commandIndex) =>
        _runsByCommand.TryGetValue(commandIndex, out var run) ? run : null;
}
