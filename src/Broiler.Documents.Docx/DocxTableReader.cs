using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>DOCX table grids, spans, cell styles, and nested table content.</summary>
internal static class DocxTableReader
{
    /// <summary>
    /// Reads a table: its cells' paragraphs into the body, left-to-right and
    /// top-to-bottom, and the grid they are arranged in into a
    /// <see cref="DocumentTable"/> over the range they occupy.
    /// </summary>
    /// <remarks>
    /// The paragraphs go where they always went. What is new is that the reader
    /// now also records which of them each cell holds, so the grid, the spans,
    /// the borders, and the shading survive instead of the text arriving as an
    /// undifferentiated run of paragraphs in row order.
    /// </remarks>
    internal static void Read(
        XElement table, DocxReadContext context, int depth,
        Action<IEnumerable<XElement>, int> readBlocks)
    {
        int start = context.Builder.CurrentParagraphIndex;
        XElement? properties = table.Element(DocxNamespaces.Wordprocessing + "tblPr");
        CellBorders tableBorders = ReadTableBorders(properties);
        var rows = new List<RowDraft>();
        bool exactHeightSeen = false;

        foreach (XElement row in EnumerateTableChildren(table, "tr"))
        {
            var cells = new List<CellDraft>();
            int column = 0;
            foreach (XElement cell in EnumerateTableChildren(row, "tc"))
            {
                XElement? cellProperties = cell.Element(DocxNamespaces.Wordprocessing + "tcPr");
                int span = Math.Max(1, WordInt(cellProperties, "gridSpan", 1));
                int cellStart = context.Builder.CurrentParagraphIndex;

                // Tables the cell's content opens belong to the cell, so they are
                // collected here rather than landing beside this one in the body.
                var nested = new List<DocumentTable>();
                context.Builder.PushTableSink(nested);
                readBlocks(cell.Elements(), depth + 1);
                context.Builder.PopTableSink();

                // A cell ends the reach of a page break stated in its last
                // paragraph: the paragraphs of every cell are one flat list, so
                // the next one is the neighbouring cell's or the body's past
                // the table, and neither is where the document put the break.
                context.Builder.DiscardPageBreakAtCellEnd();

                cells.Add(new CellDraft
                {
                    ParagraphIndex = cellStart,
                    ParagraphCount = context.Builder.CurrentParagraphIndex - cellStart,
                    ColumnIndex = column,
                    ColumnSpan = span,
                    Merge = ReadVerticalMerge(cellProperties),
                    Shading = ReadCellShading(cellProperties),
                    Borders = ReadCellBorders(cellProperties, tableBorders),
                    Tables = nested,
                });
                column += span;
            }

            rows.Add(new RowDraft(cells, ReadRowMinHeight(row, ref exactHeightSeen)));
            if (rows.Count >= context.Builder.Limits.MaxParagraphCount)
                break;
        }

        if (exactHeightSeen)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.table.rowheight",
                "A DOCX table row stated an exact height; it was applied as a minimum, " +
                "because a row that clipped its own text would lose content the document has.");
        }

        ResolveRowSpans([.. rows.Select(draft => draft.Cells)]);
        context.Builder.AddTable(new DocumentTable(
            start,
            context.Builder.CurrentParagraphIndex - start,
            [.. rows.Select(BuildRow)],
            ReadTableGrid(table),
            ReadCellPadding(properties)));

        context.Builder.NoteTable();
        if (properties?.Element(DocxNamespaces.Wordprocessing + "tblStyle") is not null)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.table.style",
                "A DOCX table named a table style; banding, conditional formatting, " +
                "and the borders a style states are not applied.");
        }
    }

    /// <summary>What a cell's <c>w:vMerge</c> says about the merge it is part of.</summary>
    private enum VerticalMerge
    {
        /// <summary>No <c>w:vMerge</c>: an ordinary cell.</summary>
        None,

        /// <summary><c>w:val="restart"</c>: the cell a merge runs down from.</summary>
        Start,

        /// <summary>A cell the merge above it covers.</summary>
        Continue,
    }

    /// <summary>A cell being read, before its row span is known.</summary>
    private sealed class CellDraft
    {
        public int ParagraphIndex;
        public int ParagraphCount;
        public int ColumnIndex;
        public int ColumnSpan = 1;
        public int RowSpan = 1;
        public VerticalMerge Merge;
        public BColor Shading;
        public CellBorders Borders;
        public List<DocumentTable> Tables = [];
    }

    /// <summary>A row being read: its cells, and the height it asked for.</summary>
    private readonly record struct RowDraft(List<CellDraft> Cells, double MinHeight);

    /// <summary>
    /// ECMA-376 §17.4.81: the height a row states, in points, as a minimum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule attribute decides what the value means. <c>exact</c> is read as a
    /// minimum rather than obeyed - clipping a row's own text is a worse outcome
    /// than a row taller than it asked for - and it is diagnosed so the difference
    /// is visible. An explicit <c>auto</c> asks for nothing and gets nothing.
    /// </para>
    /// <para>
    /// A <c>w:trHeight</c> carrying no rule at all is read as a minimum, which is
    /// worth stating because the specification's own default for the attribute is
    /// <c>auto</c>. Word does not render it that way and neither does anything
    /// else that reads these files: a bare height is a floor in practice. The CV
    /// and letterhead templates depend on it, stating a tall empty first row to
    /// place the block beneath it, and read as <c>auto</c> those rows collapse and
    /// the layout falls in on itself.
    /// </para>
    /// </remarks>
    private static double ReadRowMinHeight(XElement row, ref bool exactHeightSeen)
    {
        XElement? height = row
            .Element(DocxNamespaces.Wordprocessing + "trPr")?
            .Element(DocxNamespaces.Wordprocessing + "trHeight");
        if (height is null)
            return 0;

        string? rule = (string?)height.Attribute(DocxNamespaces.Wordprocessing + "hRule");
        if (string.Equals(rule, "auto", StringComparison.Ordinal))
            return 0;

        if (string.Equals(rule, "exact", StringComparison.Ordinal))
            exactHeightSeen = true;

        return Math.Max(0, DocxValueReader.Twips(height, "val"));
    }

    private static TableRow BuildRow(RowDraft row) =>
        new([.. row.Cells.Select(cell => new TableCell(
            cell.ParagraphIndex,
            cell.ParagraphCount,
            cell.ColumnIndex,
            cell.ColumnSpan,
            cell.RowSpan,
            cell.Shading,
            cell.Borders,
            cell.Merge == VerticalMerge.Continue,
            cell.Tables))],
            minHeight: row.MinHeight);

    /// <summary>
    /// Turns <c>w:vMerge</c> continuations into a row span on the cell that
    /// started the merge: the format writes the merge as a run of cells and the
    /// model holds it as one cell that is taller than its row.
    /// </summary>
    private static void ResolveRowSpans(List<List<CellDraft>> rows)
    {
        for (int r = 1; r < rows.Count; r++)
        {
            foreach (CellDraft cell in rows[r])
            {
                if (cell.Merge != VerticalMerge.Continue)
                    continue;

                CellDraft? origin = FindMergeOrigin(rows, r, cell.ColumnIndex);
                if (origin is null)
                {
                    // A continuation with no merge above it continues nothing.
                    // ECMA-376 §17.4.85 starts a merge at the first preceding
                    // restart, and a document with none said something it did not
                    // mean; the cell is read as the cell it is.
                    cell.Merge = VerticalMerge.None;
                    continue;
                }

                origin.RowSpan++;
            }
        }
    }

    /// <summary>
    /// The cell a merge continuation continues: walking up its column, the first
    /// one that is not itself a continuation - and then only if it opened a
    /// merge. A column that stops before then has no merge to join.
    /// </summary>
    private static CellDraft? FindMergeOrigin(List<List<CellDraft>> rows, int row, int columnIndex)
    {
        for (int r = row - 1; r >= 0; r--)
        {
            CellDraft? candidate = null;
            foreach (CellDraft above in rows[r])
            {
                if (above.ColumnIndex == columnIndex)
                {
                    candidate = above;
                    break;
                }
            }

            if (candidate is null)
                return null;

            if (candidate.Merge != VerticalMerge.Continue)
                return candidate.Merge == VerticalMerge.Start ? candidate : null;
        }

        return null;
    }

    /// <summary>
    /// The grid's column widths in points, from <c>w:tblGrid</c>. Widths are in
    /// twentieths of a point, which is what the rest of the format measures in.
    /// </summary>
    private static List<double> ReadTableGrid(XElement table)
    {
        XElement? grid = table.Element(DocxNamespaces.Wordprocessing + "tblGrid");
        if (grid is null)
            return [];

        var widths = new List<double>();
        foreach (XElement column in grid.Elements(DocxNamespaces.Wordprocessing + "gridCol"))
        {
            widths.Add(DocxValueReader.TryReadInt(column.Attribute(DocxNamespaces.Wordprocessing + "w"), out int twips) && twips > 0
                ? twips / 20.0
                : 0);
        }

        return widths;
    }

    /// <summary>
    /// The space between a cell's edge and its text, from <c>w:tblCellMar</c>.
    /// Word states each side; this model has one padding, so the left margin is
    /// the one it takes - it is the one that moves the text.
    /// </summary>
    private static double ReadCellPadding(XElement? properties)
    {
        XElement? margins = properties?.Element(DocxNamespaces.Wordprocessing + "tblCellMar");
        XElement? left = margins?.Element(DocxNamespaces.Wordprocessing + "left") ??
            margins?.Element(DocxNamespaces.Wordprocessing + "start");
        if (left is null ||
            !DocxValueReader.TryReadInt(left.Attribute(DocxNamespaces.Wordprocessing + "w"), out int twips) ||
            twips < 0)
        {
            return DocumentTable.DefaultCellPadding;
        }

        return twips / 20.0;
    }

    /// <summary>
    /// What a cell's <c>w:vMerge</c> says: <c>w:val="restart"</c> opens a merge,
    /// and anything else, including no value at all, continues the one above.
    /// </summary>
    private static VerticalMerge ReadVerticalMerge(XElement? properties)
    {
        XElement? merge = properties?.Element(DocxNamespaces.Wordprocessing + "vMerge");
        if (merge is null)
            return VerticalMerge.None;

        return string.Equals(DocxValueReader.WordValue(merge), "restart", StringComparison.OrdinalIgnoreCase)
            ? VerticalMerge.Start
            : VerticalMerge.Continue;
    }

    /// <summary>A cell's background, from <c>w:shd</c>; empty when it states none.</summary>
    private static BColor ReadCellShading(XElement? properties)
    {
        XElement? shading = properties?.Element(DocxNamespaces.Wordprocessing + "shd");
        if (shading is null)
            return BColor.Empty;

        string? fill = (string?)shading.Attribute(DocxNamespaces.Wordprocessing + "fill");
        return DocxValueReader.TryParseHexColor(fill, out BColor color) ? color : BColor.Empty;
    }

    /// <summary>The four edges a table states in <c>w:tblBorders</c>, for its cells to inherit.</summary>
    private static CellBorders ReadTableBorders(XElement? properties)
    {
        XElement? borders = properties?.Element(DocxNamespaces.Wordprocessing + "tblBorders");
        if (borders is null)
            return default;

        // insideH/insideV are the edges between cells. A cell takes them for the
        // sides that face another cell, which the outer edges then override on
        // the cells that sit against the table's own boundary.
        TableBorder? inside = ReadBorderEdge(borders, "insideH");
        TableBorder? insideVertical = ReadBorderEdge(borders, "insideV");
        return new CellBorders(
            FirstStated(ReadBorderEdge(borders, "left"), ReadBorderEdge(borders, "start"), insideVertical),
            FirstStated(ReadBorderEdge(borders, "top"), inside),
            FirstStated(ReadBorderEdge(borders, "right"), ReadBorderEdge(borders, "end"), insideVertical),
            FirstStated(ReadBorderEdge(borders, "bottom"), inside));
    }

    /// <summary>
    /// A cell's own borders, falling back to what the table states. A cell that
    /// states an edge wins outright, including when what it states is no border
    /// at all - turning one off is a decision, not a gap to fill in from above.
    /// </summary>
    private static CellBorders ReadCellBorders(XElement? properties, CellBorders table)
    {
        XElement? borders = properties?.Element(DocxNamespaces.Wordprocessing + "tcBorders");
        if (borders is null)
            return table;

        return new CellBorders(
            FirstStated(ReadBorderEdge(borders, "left"), ReadBorderEdge(borders, "start"), table.Left),
            FirstStated(ReadBorderEdge(borders, "top"), table.Top),
            FirstStated(ReadBorderEdge(borders, "right"), ReadBorderEdge(borders, "end"), table.Right),
            FirstStated(ReadBorderEdge(borders, "bottom"), table.Bottom));
    }

    /// <summary>
    /// One border edge: its colour and its width, or null when the document does
    /// not state that edge at all. <c>w:sz</c> is in eighths of a point, and
    /// <c>w:val="none"</c> or <c>"nil"</c> is a border turned off - which is
    /// stated, and so is not the same as saying nothing.
    /// </summary>
    private static TableBorder? ReadBorderEdge(XElement borders, string edge)
    {
        XElement? element = borders.Element(DocxNamespaces.Wordprocessing + edge);
        if (element is null)
            return null;

        string? kind = DocxValueReader.WordValue(element);
        if (string.Equals(kind, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kind, "nil", StringComparison.OrdinalIgnoreCase))
        {
            return TableBorder.None;
        }

        double width = DocxValueReader.TryReadInt(element.Attribute(DocxNamespaces.Wordprocessing + "sz"), out int eighths) && eighths > 0
            ? Math.Min(eighths / 8.0, MaxBorderWidth)
            : DefaultBorderWidth;

        string? color = (string?)element.Attribute(DocxNamespaces.Wordprocessing + "color");
        // "auto" is the format's way of saying the reader chooses; Word draws black.
        return new TableBorder(
            DocxValueReader.TryParseHexColor(color, out BColor parsed) ? parsed : BColor.Black,
            width);
    }

    /// <summary>The first edge that was stated at all, else no border.</summary>
    private static TableBorder FirstStated(params TableBorder?[] candidates)
    {
        foreach (TableBorder? candidate in candidates)
        {
            if (candidate is TableBorder stated)
                return stated;
        }

        return TableBorder.None;
    }

    /// <summary>A border thicker than this is a rule, not a border, and would swallow the cell.</summary>
    private const double MaxBorderWidth = 6.0;

    /// <summary>What Word draws for a border that states no width: a hairline.</summary>
    private const double DefaultBorderWidth = 0.5;

    /// <summary>An integer attribute of a child element, or <paramref name="fallback"/>.</summary>
    private static int WordInt(XElement? properties, string localName, int fallback)
    {
        XElement? element = properties?.Element(DocxNamespaces.Wordprocessing + localName);
        return DocxValueReader.TryReadInt(element?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int value)
            ? value
            : fallback;
    }

    /// <summary>
    /// Yields the <paramref name="localName"/> children of a table or row,
    /// looking through the content controls and revision markers Word may wrap
    /// rows and cells in.
    /// </summary>
    private static IEnumerable<XElement> EnumerateTableChildren(XElement parent, string localName)
    {
        foreach (XElement child in parent.Elements())
        {
            if (child.Name == DocxNamespaces.Wordprocessing + localName)
            {
                yield return child;
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "sdt")
            {
                XElement? content = child.Element(DocxNamespaces.Wordprocessing + "sdtContent");
                if (content is null)
                    continue;

                foreach (XElement nested in EnumerateTableChildren(content, localName))
                    yield return nested;
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "ins" ||
                child.Name == DocxNamespaces.Wordprocessing + "customXml")
            {
                foreach (XElement nested in EnumerateTableChildren(child, localName))
                    yield return nested;
            }
        }
    }

}
