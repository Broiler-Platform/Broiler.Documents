using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt;

/// <summary>ODT table grids, spans, cell styles, and nested table content.</summary>
internal static class OdtTableReader
{
    /// <summary>
    /// Reads a table: its cells' paragraphs into the body, left-to-right and
    /// top-to-bottom, and the grid they are arranged in into a
    /// <see cref="DocumentTable"/> over the range they occupy.
    /// </summary>
    /// <remarks>
    /// ODF states a merge on the cell that opens it and writes a
    /// <c>table:covered-table-cell</c> for every grid position it covers. Those
    /// hold nothing and are drawn by nobody, so they are counted for the column
    /// they occupy and otherwise passed over - the span on the opening cell is
    /// the whole of what the model needs.
    /// </remarks>
    internal static void Read(
        XElement table, OdtReadContext context, int depth,
        Action<IEnumerable<XElement>, int> readBlocks)
    {
        int start = context.Builder.CurrentParagraphIndex;
        var rows = new List<TableRow>();

        foreach (XElement row in EnumerateRows(table, depth))
        {
            var cells = new List<TableCell>();
            int column = 0;

            // Columns a span in this row has already claimed. The covered cells
            // that follow it stand for those, so they take no column of their
            // own; a covered cell with none pending is the lower half of a merge
            // from the row above, and does take one.
            int claimed = 0;
            foreach (XElement cell in row.Elements())
            {
                bool covered = cell.Name == OdtNamespaces.Table + "covered-table-cell";
                if (!covered && cell.Name != OdtNamespaces.Table + "table-cell")
                    continue;

                int repeat = Math.Clamp(TableInt(cell, "number-columns-repeated", 1), 1, MaxColumnRepeat);
                if (covered)
                {
                    for (int i = 0; i < repeat; i++)
                    {
                        if (claimed > 0)
                            claimed--;
                        else
                            column++;
                    }

                    continue;
                }

                int span = Math.Max(1, TableInt(cell, "number-columns-spanned", 1));
                claimed = span - 1;
                int rowSpan = Math.Max(1, TableInt(cell, "number-rows-spanned", 1));
                XElement? properties = CellProperties(cell, context.Styles);

                for (int i = 0; i < repeat; i++)
                {
                    int cellStart = context.Builder.CurrentParagraphIndex;
                    var nested = new List<DocumentTable>();
                    context.Builder.PushTableSink(nested);
                    readBlocks(cell.Elements(), depth + 1);
                    context.Builder.PopTableSink();

                    // A cell is the end of a flow as far as a page break is
                    // concerned. The paragraph that follows the last one in this
                    // cell is the first one in the next cell, which is across the
                    // grid rather than down the page, so a break carried there
                    // would land somewhere the document never pointed at.
                    context.DropPageBreak();

                    cells.Add(new TableCell(
                        cellStart,
                        context.Builder.CurrentParagraphIndex - cellStart,
                        column,
                        span,
                        rowSpan,
                        ReadCellShading(properties),
                        ReadCellBorders(properties),
                        isRowSpanContinuation: false,
                        tables: nested));
                    column += span;
                }
            }

            rows.Add(new TableRow(
                cells,
                row.Parent?.Name == OdtNamespaces.Table + "table-header-rows",
                ReadRowMinHeight(row, context.Styles)));
        }

        context.Builder.AddTable(new DocumentTable(
            start,
            context.Builder.CurrentParagraphIndex - start,
            rows,
            ReadColumnWidths(table, context.Styles, depth),
            DocumentTable.DefaultCellPadding));

        context.Builder.NoteTable();
    }

    /// <summary>A repeat count beyond this is a spreadsheet's empty tail, not a table.</summary>
    private const int MaxColumnRepeat = 64;

    private static int TableInt(XElement element, string attribute, int fallback) =>
        int.TryParse(
            (string?)element.Attribute(OdtNamespaces.Table + attribute),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;

    /// <summary>
    /// The grid's column widths in points, from each <c>table:table-column</c>'s
    /// style. A column that states none contributes zero, which a renderer reads
    /// as "share what is left".
    /// </summary>
    private static IReadOnlyList<double> ReadColumnWidths(XElement table, OdtStyles styles, int depth)
    {
        var widths = new List<double>();
        foreach (XElement column in EnumerateColumns(table, depth))
        {
            int repeat = Math.Clamp(TableInt(column, "number-columns-repeated", 1), 1, MaxColumnRepeat);
            string? styleName = (string?)column.Attribute(OdtNamespaces.Table + "style-name");
            double width = 0;
            if (styleName is not null &&
                styles.TableColumnProperties.TryGetValue(styleName, out XElement? properties) &&
                OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Style + "column-width"), out double points))
            {
                width = points;
            }

            for (int i = 0; i < repeat; i++)
                widths.Add(width);
        }

        return widths;
    }

    /// <summary>
    /// The height a row asks for, in points, as a minimum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ODF states this two ways and they do not mean the same thing.
    /// <c>style:min-row-height</c> is a floor and is taken as one.
    /// <c>style:row-height</c> is a fixed height, and is <em>also</em> taken as a
    /// floor: a row honouring it exactly would clip its own text, and losing text
    /// is the one outcome a document reader may not choose. That is the position
    /// the DOCX codec takes for <c>w:hRule="exact"</c>, and the two formats agree
    /// here rather than each inventing an answer.
    /// </para>
    /// <para>
    /// Where a row states both, the larger wins: each is a floor and a row
    /// satisfying the taller satisfies the other. <c>style:use-optimal-row-height</c>
    /// asks for the height the content needs, which is what a row does anyway
    /// without a floor, so it is read as asking for none.
    /// </para>
    /// </remarks>
    private static double ReadRowMinHeight(XElement row, OdtStyles styles)
    {
        string? styleName = (string?)row.Attribute(OdtNamespaces.Table + "style-name");
        if (styleName is null ||
            !styles.TableRowProperties.TryGetValue(styleName, out XElement? properties))
        {
            return 0;
        }

        if (string.Equals(
                (string?)properties.Attribute(OdtNamespaces.Style + "use-optimal-row-height"),
                "true",
                StringComparison.Ordinal))
        {
            return 0;
        }

        double height = 0;
        if (OdtUnits.TryParseLength(
                (string?)properties.Attribute(OdtNamespaces.Style + "min-row-height"), out double minimum))
        {
            height = Math.Max(height, minimum);
        }

        if (OdtUnits.TryParseLength(
                (string?)properties.Attribute(OdtNamespaces.Style + "row-height"), out double stated))
        {
            height = Math.Max(height, stated);
        }

        return Math.Max(0, height);
    }

    /// <summary>A table's columns, looking through the groups ODF wraps them in.</summary>
    private static IEnumerable<XElement> EnumerateColumns(XElement parent, int depth)
    {
        if (depth > MaxColumnRepeat)
            yield break;

        foreach (XElement child in parent.Elements())
        {
            if (child.Name == OdtNamespaces.Table + "table-column")
            {
                yield return child;
                continue;
            }

            if (child.Name == OdtNamespaces.Table + "table-columns" ||
                child.Name == OdtNamespaces.Table + "table-column-group" ||
                child.Name == OdtNamespaces.Table + "table-header-columns")
            {
                foreach (XElement nested in EnumerateColumns(child, depth + 1))
                    yield return nested;
            }
        }
    }

    private static XElement? CellProperties(XElement cell, OdtStyles styles)
    {
        string? styleName = (string?)cell.Attribute(OdtNamespaces.Table + "style-name");
        return styleName is not null && styles.TableCellProperties.TryGetValue(styleName, out XElement? properties)
            ? properties
            : null;
    }

    private static BColor ReadCellShading(XElement? properties)
    {
        string? color = (string?)properties?.Attribute(OdtNamespaces.Fo + "background-color");
        if (color is null || color.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return BColor.Empty;

        return OdtUnits.TryParseColor(color, out BColor parsed) ? parsed : BColor.Empty;
    }

    /// <summary>
    /// A cell's four edges. ODF writes them as CSS shorthand - a width, a style,
    /// and a colour - either once for all four or once per side, with the side
    /// winning where both are stated.
    /// </summary>
    private static CellBorders ReadCellBorders(XElement? properties)
    {
        if (properties is null)
            return default;

        TableBorder all = ReadBorderEdge(properties, "border");
        return new CellBorders(
            FirstStated(ReadBorderEdge(properties, "border-left"), all),
            FirstStated(ReadBorderEdge(properties, "border-top"), all),
            FirstStated(ReadBorderEdge(properties, "border-right"), all),
            FirstStated(ReadBorderEdge(properties, "border-bottom"), all));
    }

    private static TableBorder FirstStated(TableBorder edge, TableBorder fallback) =>
        edge.Width > 0 || edge.Color.A > 0 ? edge : fallback;

    /// <summary>
    /// One <c>fo:border</c> value: <c>0.5pt solid #000000</c>. The keyword
    /// <c>none</c> is a border turned off, and anything unparseable is left off
    /// rather than guessed at.
    /// </summary>
    private static TableBorder ReadBorderEdge(XElement properties, string attribute)
    {
        string? value = (string?)properties.Attribute(OdtNamespaces.Fo + attribute);
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return TableBorder.None;

        double width = 0;
        BColor color = BColor.Black;
        foreach (string part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (OdtUnits.TryParseLength(part, out double points))
                width = points;
            else if (OdtUnits.TryParseColor(part, out BColor parsed))
                color = parsed;
        }

        return width > 0 ? new TableBorder(color, width) : TableBorder.None;
    }

    /// <summary>
    /// Yields a table's rows, looking through the row groups ODF wraps them in:
    /// header rows, row groups, and the row-level equivalents of a column split.
    /// </summary>
    private static IEnumerable<XElement> EnumerateRows(XElement parent, int depth)
    {
        if (depth > 64)
            yield break;

        foreach (XElement child in parent.Elements())
        {
            if (child.Name == OdtNamespaces.Table + "table-row")
            {
                yield return child;
                continue;
            }

            if (child.Name == OdtNamespaces.Table + "table-header-rows" ||
                child.Name == OdtNamespaces.Table + "table-row-group" ||
                child.Name == OdtNamespaces.Table + "table-rows")
            {
                foreach (XElement nested in EnumerateRows(child, depth + 1))
                    yield return nested;
            }
        }
    }

}
