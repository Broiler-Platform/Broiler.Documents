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
    /// <para>
    /// The paragraphs go where they always went. What is new is that the reader
    /// now also records which of them each cell holds, so the grid, the spans,
    /// the borders, and the shading survive instead of the text arriving as an
    /// undifferentiated run of paragraphs in row order.
    /// </para>
    /// <para>
    /// The cells are read under the table's own style, so their paragraphs and
    /// runs inherit what it states. A table nested in a cell is read by this
    /// same method and takes its own style, or the default one, rather than its
    /// parent's: the style belongs to a table, not to everything inside it.
    /// </para>
    /// <para>
    /// Each cell is also read under the conditional formats of that style it
    /// falls in - the first row, the last column, a band - as the table's
    /// <c>w:tblLook</c> selects them (<see cref="DocxTableLook"/>). They reach its
    /// paragraphs, its runs, its borders and its shading.
    /// </para>
    /// </remarks>
    internal static void Read(
        XElement table, DocxReadContext context, int depth,
        Action<IEnumerable<XElement>, DocxReadContext, int> readBlocks)
    {
        int start = context.Builder.CurrentParagraphIndex;
        XElement? properties = table.Element(DocxNamespaces.Wordprocessing + "tblPr");
        DocxStyles styles = context.Styles;
        string? styleId = styles.TableStyleFor(
            DocxValueReader.WordValue(properties?.Element(DocxNamespaces.Wordprocessing + "tblStyle")));
        DocxTableLook look = DocxTableLook.Read(properties, type => styles.DefinesTableCondition(styleId, type));
        IReadOnlyList<XElement> styleTableParts = styles.TableStyleParts(styleId, TableConditions.None, "tblPr");
        Edges tableEdges = ReadTableEdges(styleTableParts, properties);
        BColor? tableShading = ReadTableShading(styleTableParts, properties);
        List<XElement> rowElements = [.. EnumerateTableChildren(table, "tr")];
        var rows = new List<RowDraft>();
        bool exactHeightSeen = false;

        for (int rowIndex = 0; rowIndex < rowElements.Count; rowIndex++)
        {
            XElement row = rowElements[rowIndex];
            bool isHeader = ReadRowSwitch(row, "tblHeader");
            TableConditions rowConditions = look.ForRow(rowIndex, rowElements.Count, isHeader);
            IReadOnlyList<XElement> styleRowParts = styles.TableStyleParts(styleId, rowConditions, "trPr");

            // A row's w:tblPrEx states table borders for that row alone.
            Edges rowEdges = Edges.Read(row
                .Element(DocxNamespaces.Wordprocessing + "tblPrEx")?
                .Element(DocxNamespaces.Wordprocessing + "tblBorders")).Over(tableEdges);
            List<XElement> cellElements = [.. EnumerateTableChildren(row, "tc")];
            var cells = new List<CellDraft>();
            int column = 0;
            for (int cellIndex = 0; cellIndex < cellElements.Count; cellIndex++)
            {
                XElement cell = cellElements[cellIndex];
                XElement? cellProperties = cell.Element(DocxNamespaces.Wordprocessing + "tcPr");
                TableConditions conditions = look.ForCell(rowConditions, cellIndex, cellElements.Count);
                DocxReadContext cellContext = context with
                {
                    TableStyle = styleId,
                    TableConditions = conditions,
                };
                int span = Math.Max(1, WordInt(cellProperties, "gridSpan", 1));
                int cellStart = context.Builder.CurrentParagraphIndex;

                // Tables the cell's content opens belong to the cell, so they are
                // collected here rather than landing beside this one in the body.
                var nested = new List<DocumentTable>();
                context.Builder.PushTableSink(nested);
                readBlocks(cell.Elements(), cellContext, depth + 1);
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
                    Properties = cellProperties,
                    StyleParts = styles.TableStyleParts(styleId, conditions, "tcPr"),
                    RowIndex = rowIndex,
                    IsFirstInRow = cellIndex == 0,
                    IsLastInRow = cellIndex == cellElements.Count - 1,
                    TableEdges = rowEdges,
                    Tables = nested,
                });
                column += span;
            }

            // A row the style's first-row format marks as a header repeats as one,
            // and a style's rows can be kept whole; the row's own switches win.
            rows.Add(new RowDraft(
                cells,
                ReadRowMinHeight(row, ref exactHeightSeen),
                !(ReadRowSwitchOrNull(row.Element(DocxNamespaces.Wordprocessing + "trPr"), "cantSplit") ??
                  StyleRowSwitch(styleRowParts, "cantSplit")),
                ReadRowSwitchOrNull(row.Element(DocxNamespaces.Wordprocessing + "trPr"), "tblHeader") ??
                  ((rowConditions & TableConditions.FirstRow) != 0 && StyleRowSwitch(styleRowParts, "tblHeader"))));
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
        foreach (RowDraft row in rows)
        {
            foreach (CellDraft cell in row.Cells)
            {
                bool reachesEnd = cell.RowIndex + cell.RowSpan >= rows.Count;
                cell.Borders = ResolveCellBorders(cell, cell.TableEdges, reachesEnd);
                cell.Shading = ResolveCellShading(cell, tableShading);
            }
        }

        context.Builder.AddTable(new DocumentTable(
            start,
            context.Builder.CurrentParagraphIndex - start,
            [.. rows.Select(BuildRow)],
            ReadTableGrid(table),
            ReadCellPadding(styleTableParts, properties),
            depth <= 1 ? ReadPosition(properties) : null));

        if (depth > 1 && properties?.Element(DocxNamespaces.Wordprocessing + "tblpPr") is not null)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.table.floating",
                "A DOCX table nested in a cell stated a floating position; it was laid out in its cell.");
        }

        context.Builder.NoteTable();
        string? unapplied = FirstUnappliedStyleProperty(styles.AllTableStyleParts(styleId));
        if (unapplied is not null)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.table.style",
                "A DOCX table style stated a property this reader does not apply (w:" + unapplied + "); " +
                "its borders, shading, cell margin, header rows and paragraph and run properties were applied.");
        }
    }

    /// <summary>
    /// The first property a table style states that the read leaves out, or
    /// null. Indent and width are not listed: no table is indented or sized by
    /// them here, whoever states them.
    /// </summary>
    private static string? FirstUnappliedStyleProperty(IEnumerable<XElement> parts)
    {
        foreach (XElement part in parts)
        {
            string kind = part.Name.LocalName;
            if (kind is not ("tblPr" or "trPr" or "tcPr"))
                continue;

            foreach (XElement property in part.Elements())
            {
                string name = property.Name.LocalName;
                bool applied = (kind, name) switch
                {
                    ("tblPr", "tblBorders" or "shd" or "tblCellMar" or "tblInd" or "tblW" or "tblLook" or "tblStyle") => true,
                    ("tblPr", "tblStyleRowBandSize" or "tblStyleColBandSize") =>
                        !DocxValueReader.TryReadInt(property.Attribute(DocxNamespaces.Wordprocessing + "val"), out int size) || size <= 1,
                    ("trPr", "cantSplit" or "tblHeader") => true,
                    ("tcPr", "tcBorders") => !property.Elements().Any(edge => edge.Name.LocalName is "tl2br" or "tr2bl"),
                    ("tcPr", "shd" or "tcW") => true,
                    _ => false,
                };
                if (!applied)
                    return name == "tcBorders" ? "tl2br" : name;
            }
        }

        return null;
    }

    /// <summary>
    /// ECMA-376 §17.4.57's <c>w:tblpPr</c>: where a floating table stands, or
    /// null for a table in the flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An attribute left out takes LibreOffice's reading of it
    /// (<c>writerfilter/dmapper/TablePositionHandler</c>): measured from the
    /// margin down and from the text across, at nought. Word writes both anchors
    /// in practice. A <c>tblpXSpec</c> or <c>tblpYSpec</c> places the table in
    /// place of the offset in its direction; <c>inline</c>, which LibreOffice
    /// does not map either, is read as no alignment.
    /// </para>
    /// <para>
    /// <c>w:tblOverlap</c> (§17.4.56) is read with it. <c>overlap</c> is its
    /// default, and the model keeps <c>never</c> without acting on it: it is
    /// about other floating tables, which this layout does not move apart.
    /// </para>
    /// </remarks>
    private static TablePosition? ReadPosition(XElement? properties)
    {
        XElement? position = properties?.Element(DocxNamespaces.Wordprocessing + "tblpPr");
        if (position is null)
            return null;

        string? overlap = DocxValueReader.WordValue(properties!.Element(DocxNamespaces.Wordprocessing + "tblOverlap"));
        return new TablePosition(
            Anchor(Attribute(position, "horzAnchor"), TableAnchor.Text),
            DocxValueReader.Twips(position, "tblpX"),
            Anchor(Attribute(position, "vertAnchor"), TableAnchor.Margin),
            DocxValueReader.Twips(position, "tblpY"),
            Alignment(Attribute(position, "tblpXSpec")),
            Alignment(Attribute(position, "tblpYSpec")),
            Math.Max(0, DocxValueReader.Twips(position, "leftFromText")),
            Math.Max(0, DocxValueReader.Twips(position, "rightFromText")),
            Math.Max(0, DocxValueReader.Twips(position, "topFromText")),
            Math.Max(0, DocxValueReader.Twips(position, "bottomFromText")),
            AllowOverlap: !string.Equals(overlap, "never", StringComparison.Ordinal));

        static string? Attribute(XElement element, string name) =>
            (string?)element.Attribute(DocxNamespaces.Wordprocessing + name);

        static TableAnchor Anchor(string? value, TableAnchor fallback) => value switch
        {
            "text" => TableAnchor.Text,
            "margin" => TableAnchor.Margin,
            "page" => TableAnchor.Page,
            _ => fallback,
        };

        static TableAlignment Alignment(string? value) => value switch
        {
            "left" or "top" => TableAlignment.Start,
            "center" => TableAlignment.Center,
            "right" or "bottom" => TableAlignment.End,
            "inside" => TableAlignment.Inside,
            "outside" => TableAlignment.Outside,
            _ => TableAlignment.None,
        };
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
        public XElement? Properties;
        public IReadOnlyList<XElement> StyleParts = [];
        public int RowIndex;
        public bool IsFirstInRow;
        public bool IsLastInRow;
        public Edges TableEdges;
        public List<DocumentTable> Tables = [];
    }

    /// <summary>
    /// A row being read: its cells, the height it asked for, whether it may break
    /// across a page, and whether it repeats at the top of each page.
    /// </summary>
    private readonly record struct RowDraft(List<CellDraft> Cells, double MinHeight, bool CanSplit, bool IsHeader);

    /// <summary>
    /// One of ECMA-376's on/off row properties: <c>w:cantSplit</c>, which keeps
    /// a row on one page, or <c>w:tblHeader</c>, which repeats it at the top of
    /// every page the table continues onto. A bare element is on and the file
    /// has to spell <c>false</c> to turn it off.
    /// </summary>
    private static bool ReadRowSwitch(XElement row, string name) =>
        ReadRowSwitchOrNull(row.Element(DocxNamespaces.Wordprocessing + "trPr"), name) ?? false;

    /// <summary>One on/off row property of a <c>w:trPr</c>, or null when it is not stated.</summary>
    private static bool? ReadRowSwitchOrNull(XElement? rowProperties, string name)
    {
        XElement? element = rowProperties?.Element(DocxNamespaces.Wordprocessing + name);
        if (element is null)
            return null;

        string? value = DocxValueReader.WordValue(element);
        return value is null ||
            !(value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
              value.Equals("0", StringComparison.Ordinal) ||
              value.Equals("off", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An on/off row property as a table style's row formats state it, the most specific last.</summary>
    private static bool StyleRowSwitch(IReadOnlyList<XElement> styleRowParts, string name)
    {
        bool result = false;
        foreach (XElement part in styleRowParts)
        {
            if (ReadRowSwitchOrNull(part, name) is bool stated)
                result = stated;
        }

        return result;
    }

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
            isHeader: row.IsHeader,
            minHeight: row.MinHeight,
            canSplit: row.CanSplit);

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
            widths.Add(DocxValueReader.TryReadTwips(column.Attribute(DocxNamespaces.Wordprocessing + "w"), out int twips) && twips > 0
                ? twips / 20.0
                : 0);
        }

        return widths;
    }

    /// <summary>
    /// The space between a cell's edge and its text, from <c>w:tblCellMar</c>:
    /// the table's own, else its style's. Word states each side; this model has
    /// one padding, so the left margin is the one it takes - it is the one that
    /// moves the text.
    /// </summary>
    private static double ReadCellPadding(IReadOnlyList<XElement> styleTableParts, XElement? properties)
    {
        if (LeftCellMargin(properties) is double own)
            return own;

        for (int i = styleTableParts.Count - 1; i >= 0; i--)
        {
            if (LeftCellMargin(styleTableParts[i]) is double styled)
                return styled;
        }

        return DocumentTable.DefaultCellPadding;

        static double? LeftCellMargin(XElement? tableProperties)
        {
            XElement? margins = tableProperties?.Element(DocxNamespaces.Wordprocessing + "tblCellMar");
            XElement? left = margins?.Element(DocxNamespaces.Wordprocessing + "left") ??
                margins?.Element(DocxNamespaces.Wordprocessing + "start");
            if (left is null ||
                !DocxValueReader.TryReadTwips(left.Attribute(DocxNamespaces.Wordprocessing + "w"), out int twips) ||
                twips < 0)
            {
                return null;
            }

            return twips / 20.0;
        }
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

    /// <summary>
    /// A cell's background: its own <c>w:shd</c>, else what its style's formats
    /// state for it, the most specific last, else the table's. A <c>w:shd</c>
    /// whose fill is <c>auto</c> is stated, and states no background.
    /// </summary>
    private static BColor ResolveCellShading(CellDraft cell, BColor? table)
    {
        if (ReadShading(cell.Properties) is BColor own)
            return own;

        for (int i = cell.StyleParts.Count - 1; i >= 0; i--)
        {
            if (ReadShading(cell.StyleParts[i]) is BColor styled)
                return styled;
        }

        return table ?? BColor.Empty;
    }

    /// <summary>The background a table states for all its cells: its own <c>w:shd</c>, else its style's.</summary>
    private static BColor? ReadTableShading(IReadOnlyList<XElement> styleTableParts, XElement? properties)
    {
        if (ReadShading(properties) is BColor own)
            return own;

        for (int i = styleTableParts.Count - 1; i >= 0; i--)
        {
            if (ReadShading(styleTableParts[i]) is BColor styled)
                return styled;
        }

        return null;
    }

    /// <summary>The fill of a <c>w:shd</c>, empty for <c>auto</c>, or null when there is none.</summary>
    private static BColor? ReadShading(XElement? properties)
    {
        XElement? shading = properties?.Element(DocxNamespaces.Wordprocessing + "shd");
        if (shading is null)
            return null;

        string? fill = (string?)shading.Attribute(DocxNamespaces.Wordprocessing + "fill");
        return DocxValueReader.TryParseHexColor(fill, out BColor color) ? color : BColor.Empty;
    }

    /// <summary>
    /// The six edges a <c>w:tblBorders</c> or <c>w:tcBorders</c> can state, each
    /// null where it says nothing.
    /// </summary>
    private readonly record struct Edges(
        TableBorder? Left, TableBorder? Top, TableBorder? Right, TableBorder? Bottom,
        TableBorder? InsideH, TableBorder? InsideV)
    {
        /// <summary>These edges over <paramref name="lower"/>: each stated one wins.</summary>
        public Edges Over(Edges lower) => new(
            Left ?? lower.Left, Top ?? lower.Top, Right ?? lower.Right, Bottom ?? lower.Bottom,
            InsideH ?? lower.InsideH, InsideV ?? lower.InsideV);

        public static Edges Read(XElement? borders) => borders is null
            ? default
            : new Edges(
                ReadBorderEdge(borders, "left") ?? ReadBorderEdge(borders, "start"),
                ReadBorderEdge(borders, "top"),
                ReadBorderEdge(borders, "right") ?? ReadBorderEdge(borders, "end"),
                ReadBorderEdge(borders, "bottom"),
                ReadBorderEdge(borders, "insideH"),
                ReadBorderEdge(borders, "insideV"));
    }

    /// <summary>
    /// The edges a table states in <c>w:tblBorders</c> for its cells to take by
    /// where they stand: its style's, the most specific last, under its own.
    /// </summary>
    private static Edges ReadTableEdges(IReadOnlyList<XElement> styleTableParts, XElement? properties)
    {
        Edges edges = default;
        foreach (XElement part in styleTableParts)
            edges = Edges.Read(part.Element(DocxNamespaces.Wordprocessing + "tblBorders")).Over(edges);

        return Edges.Read(properties?.Element(DocxNamespaces.Wordprocessing + "tblBorders")).Over(edges);
    }

    /// <summary>
    /// A cell's four borders, resolved as LibreOffice resolves them
    /// (<c>DomainMapperTableHandler</c>, <c>lcl_computeCellBorders</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cell's own <c>w:tcBorders</c> wins, then what its style states for
    /// cells - its own <c>w:tcPr</c> and the conditional formats the cell falls
    /// in. An edge neither states comes from the table by where the cell stands:
    /// its outer edge for a cell against the table's boundary, and
    /// <c>w:insideH</c> or <c>w:insideV</c> between cells, where a conditional
    /// format's inside edges replace the table's. A cell turning off an edge is
    /// a decision and stays off - except over a style that would draw one, which
    /// Word reads as giving the edge back to the table; and the same when both
    /// turn it off but the table states that outer edge.
    /// </para>
    /// <para>
    /// The inside edges a cell states for itself mean nothing without a region
    /// around it, and are ignored, as LibreOffice ignores them.
    /// </para>
    /// </remarks>
    private static CellBorders ResolveCellBorders(CellDraft cell, Edges table, bool reachesEnd)
    {
        Edges styled = default;
        foreach (XElement part in cell.StyleParts)
            styled = Edges.Read(part.Element(DocxNamespaces.Wordprocessing + "tcBorders")).Over(styled);

        Edges own = Edges.Read(cell.Properties?.Element(DocxNamespaces.Wordprocessing + "tcBorders"));

        TableBorder? insideH = styled.InsideH ?? table.InsideH;
        TableBorder? insideV = styled.InsideV ?? table.InsideV;
        return new CellBorders(
            Resolve(own.Left, styled.Left, table.Left) ?? (cell.IsFirstInRow ? table.Left : insideV) ?? TableBorder.None,
            Resolve(own.Top, styled.Top, table.Top) ?? (cell.RowIndex == 0 ? table.Top : insideH) ?? TableBorder.None,
            Resolve(own.Right, styled.Right, table.Right) ?? (cell.IsLastInRow ? table.Right : insideV) ?? TableBorder.None,
            Resolve(own.Bottom, styled.Bottom, table.Bottom) ?? (reachesEnd ? table.Bottom : insideH) ?? TableBorder.None);

        static TableBorder? Resolve(TableBorder? own, TableBorder? styled, TableBorder? tableEdge)
        {
            if (own is TableBorder cellEdge && styled is TableBorder styleEdge && !cellEdge.IsVisible)
            {
                if (styleEdge.IsVisible || tableEdge is { IsVisible: true })
                    return null;
            }

            return own ?? styled;
        }
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
