using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        Action<IEnumerable<XElement>, int> readBlocks,
        OdtBreak precedingBreak = OdtBreak.None,
        TablePosition? position = null)
    {
        int start = context.Builder.CurrentParagraphIndex;
        var rows = new List<TableRow>();
        BColor tableShading = ReadCellShading(TableProperties(table, context.Styles));

        foreach (XElement row in EnumerateRows(table, depth))
        {
            var cells = new List<TableCell>();
            int column = 0;
            XElement? rowProperties = (string?)row.Attribute(OdtNamespaces.Table + "style-name") is string rowStyle &&
                context.Styles.TableRowProperties.TryGetValue(rowStyle, out XElement? foundRow) ? foundRow : null;
            BColor rowShading = ReadCellShading(rowProperties);
            if (rowShading.IsEmpty || rowShading.A == 0)
                rowShading = tableShading;

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
                    context.TableDepth++;
                    try
                    {
                        readBlocks(DisplayCellContent(cell, context.Styles), depth + 1);
                    }
                    finally
                    {
                        context.TableDepth--;
                    }
                    // An empty ODF cell still has an editable paragraph and a
                    // line box in Writer. Leaving its range empty collapses
                    // spacer rows and loses their contribution to pagination.
                    if (context.Builder.CurrentParagraphIndex == cellStart && nested.Count == 0)
                        readBlocks([new XElement(OdtNamespaces.Text + "p")], depth + 1);
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
                        ReadCellShading(properties, rowShading),
                        ReadCellBorders(properties),
                        isRowSpanContinuation: false,
                        tables: nested,
                        padding: ReadCellPadding(properties),
                        verticalAlignment: (string?)properties?.Attribute(OdtNamespaces.Style + "vertical-align") switch
                        {
                            "middle" => CellVerticalAlignment.Center,
                            "bottom" => CellVerticalAlignment.Bottom,
                            _ => CellVerticalAlignment.Top,
                        }));
                    column += span;
                }
            }

            rows.Add(new TableRow(
                cells,
                row.Parent?.Name == OdtNamespaces.Table + "table-header-rows",
                ReadRowMinHeight(row, context.Styles),
                ReadRowCanSplit(row, context.Styles),
                exactHeight: IsFixedRow(row, context.Styles),
                keepWithNext: (string?)rowProperties?.Attribute(OdtNamespaces.Fo + "keep-with-next") == "always"));
        }

        (List<double> widths, bool relativeGrid) = ReadColumnWidths(table, context.Styles, depth);
        double? preferredWidth = TableLength(table, context.Styles, OdtNamespaces.Style + "width");
        context.Builder.AddTable(new DocumentTable(
            start,
            context.Builder.CurrentParagraphIndex - start,
            rows,
            widths,
            DocumentTable.DefaultCellPadding,
            position: position,
            keepWithNext: (string?)TableProperties(table, context.Styles)?.Attribute(OdtNamespaces.Fo + "keep-with-next") == "always",
            keepTogether: (string?)TableProperties(table, context.Styles)?.Attribute(OdtNamespaces.Fo + "keep-together") == "always",
            preferredWidth: preferredWidth,
            widthPercent: TablePercent(table, context.Styles) ?? (relativeGrid && preferredWidth is not > 0 ? 100 : null),
            alignment: (string?)TableProperties(table, context.Styles)?.Attribute(OdtNamespaces.Table + "align") switch
            {
                "center" => TableAlignment.Center,
                "right" => TableAlignment.End,
                _ => TableAlignment.Start,
            },
            leftIndent: TableLength(table, context.Styles, OdtNamespaces.Fo + "margin-left") ?? 0,
            pageBreakBefore: precedingBreak == OdtBreak.Page ||
                (string?)TableProperties(table, context.Styles)?.Attribute(OdtNamespaces.Fo + "break-before") == "page",
            columnBreakBefore: precedingBreak == OdtBreak.Column ||
                (string?)TableProperties(table, context.Styles)?.Attribute(OdtNamespaces.Fo + "break-before") == "column"));

        context.HoldBreak((string?)TableProperties(table, context.Styles)?.Attribute(OdtNamespaces.Fo + "break-after") switch
        {
            "page" => OdtBreak.Page,
            "column" => OdtBreak.Column,
            _ => OdtBreak.None,
        });

        context.Builder.NoteTable();
    }

    private static double? TableLength(XElement table, OdtStyles styles, XName attribute) =>
        OdtUnits.TryParseLength((string?)TableProperties(table, styles)?.Attribute(attribute), out double points)
            ? points : null;

    private static IEnumerable<XElement> DisplayCellContent(XElement cell, OdtStyles styles)
    {
        // For a General-format numeric cell the stored value is authoritative.
        // Keep rich/multi-paragraph content and explicit number formats unchanged.
        XElement? paragraph = cell.Elements().FirstOrDefault();
        if ((string?)cell.Attribute(OdtNamespaces.Office + "value-type") == "float" &&
            cell.Elements().Count() == 1 && paragraph?.Name == OdtNamespaces.Text + "p" && !paragraph.HasElements &&
            styles.HasGeneralNumberFormat((string?)cell.Attribute(OdtNamespaces.Table + "style-name")) &&
            double.TryParse((string?)cell.Attribute(OdtNamespaces.Office + "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
        {
            var displayed = new XElement(paragraph) { Value = value.ToString("G15", styles.NumberCulture) };
            return [displayed];
        }
        return cell.Elements();
    }

    private static double? TablePercent(XElement table, OdtStyles styles) =>
        OdtUnits.TryParsePercentage((string?)TableProperties(table, styles)?.Attribute(OdtNamespaces.Style + "rel-width"), out double ratio) && ratio > 0
            ? ratio * 100 : null;

    private static XElement? TableProperties(XElement table, OdtStyles styles) =>
        (string?)table.Attribute(OdtNamespaces.Table + "style-name") is string name &&
        styles.TableProperties.TryGetValue(name, out XElement? properties) ? properties : null;

    private static TableCellPadding? ReadCellPadding(XElement? properties)
    {
        if (properties is null)
            return null;
        double? uniform = Read("padding");
        double? left = Read("padding-left") ?? uniform;
        double? right = Read("padding-right") ?? uniform;
        double? top = Read("padding-top") ?? uniform;
        double? bottom = Read("padding-bottom") ?? uniform;
        if (left is null && right is null && top is null && bottom is null)
            return null;
        return new TableCellPadding(left ?? 0, top ?? 0, right ?? 0, bottom ?? 0);

        double? Read(string name) => OdtUnits.TryParseLength(
            (string?)properties.Attribute(OdtNamespaces.Fo + name), out double value) && value >= 0 ? value : null;
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
    /// style, or a proportional point basis for an exclusively relative grid.
    /// A column that states neither contributes zero, which a renderer reads
    /// as "share what is left".
    /// </summary>
    private static (List<double> Widths, bool Relative) ReadColumnWidths(XElement table, OdtStyles styles, int depth)
    {
        var widths = new List<double>();
        var relative = new List<double>();
        foreach (XElement column in EnumerateColumns(table, depth))
        {
            int repeat = Math.Clamp(TableInt(column, "number-columns-repeated", 1), 1, MaxColumnRepeat);
            string? styleName = (string?)column.Attribute(OdtNamespaces.Table + "style-name");
            double width = 0;
            double weight = 0;
            if (styleName is not null &&
                styles.TableColumnProperties.TryGetValue(styleName, out XElement? properties))
            {
                if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Style + "column-width"), out double points))
                    width = points;
                string? value = ((string?)properties.Attribute(OdtNamespaces.Style + "rel-column-width"))?.Trim();
                if (value?.EndsWith('*') == true && double.TryParse(value[..^1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) && parsed > 0)
                    weight = parsed;
            }

            for (int i = 0; i < repeat; i++)
            {
                widths.Add(width);
                relative.Add(weight);
            }
        }

        if (widths.Count == 0 || widths.Any(width => width > 0) || relative.Any(weight => weight <= 0))
            return (widths, false);

        // A grid expressed only as star weights still fills its table. Keep a
        // proportional point basis and let the table's width/percentage scale
        // it in its containing cell or page. Scaling by the maximum first keeps
        // a sum of large finite weights from overflowing.
        double maximum = relative.Max();
        double total = relative.Sum(weight => weight / maximum);
        double basis = TableLength(table, styles, OdtNamespaces.Style + "width") is double stated && stated > 0 ? stated : 100;
        for (int i = 0; i < widths.Count; i++)
            widths[i] = basis * (relative[i] / maximum) / total;
        return (widths, true);
    }

    /// <summary>
    /// Whether the row has a positive fixed height, rather than an optimal or
    /// minimum height. Its text remains in the model even where rendering clips it.
    /// </summary>
    private static bool IsFixedRow(XElement row, OdtStyles styles) =>
        (string?)row.Attribute(OdtNamespaces.Table + "style-name") is string name &&
        styles.TableRowProperties.TryGetValue(name, out XElement? properties) &&
        (string?)properties.Attribute(OdtNamespaces.Style + "use-optimal-row-height") != "true" &&
        RowHeightAttribute(properties) is XAttribute attribute && attribute.Name == OdtNamespaces.Style + "row-height" &&
        OdtUnits.TryParseLength(attribute.Value, out double height) && height > 0;

    private static XAttribute? RowHeightAttribute(XElement properties) => properties.Attributes().LastOrDefault(attribute =>
        attribute.Name == OdtNamespaces.Style + "row-height" || attribute.Name == OdtNamespaces.Style + "min-row-height");

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

        // Writer imports these mutually exclusive choices in attribute order.
        return OdtUnits.TryParseLength(RowHeightAttribute(properties)?.Value, out double height) ? Math.Max(0, height) : 0;
    }

    /// <summary>
    /// ODF's <c>fo:keep-together</c> on a row: <c>always</c> keeps the row on one
    /// page, and <c>auto</c>, or saying nothing, lets it break across one.
    /// </summary>
    private static bool ReadRowCanSplit(XElement row, OdtStyles styles)
    {
        string? styleName = (string?)row.Attribute(OdtNamespaces.Table + "style-name");
        return styleName is null ||
            !styles.TableRowProperties.TryGetValue(styleName, out XElement? properties) ||
            !string.Equals(
                (string?)properties.Attribute(OdtNamespaces.Fo + "keep-together"),
                "always",
                StringComparison.Ordinal);
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

    private static BColor ReadCellShading(XElement? properties, BColor fallback = default)
    {
        string? color = (string?)properties?.Attribute(OdtNamespaces.Fo + "background-color");
        if (color is null || color.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return fallback;

        return OdtUnits.TryParseColor(color, out BColor parsed) ? parsed : fallback;
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
            Edge("border-left"), Edge("border-top"), Edge("border-right"), Edge("border-bottom"));

        // An explicit 'none' is a declaration too: falling back based on its
        // zero width would resurrect the uniform border the author disabled.
        TableBorder Edge(string name) => properties.Attribute(OdtNamespaces.Fo + name) is not null
            ? ReadBorderEdge(properties, name) : all;
    }

    /// <summary>
    /// One <c>fo:border</c> value: <c>0.5pt solid #000000</c>. The keyword
    /// <c>none</c> is a border turned off, and anything unparseable is left off
    /// rather than guessed at.
    /// </summary>
    internal static TableBorder ReadBorderEdge(XElement properties, string attribute)
    {
        string? value = (string?)properties.Attribute(OdtNamespaces.Fo + attribute);
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return TableBorder.None;

        double width = 0;
        BColor color = BColor.Black;
        TableBorderStyle style = TableBorderStyle.Solid;
        foreach (string part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (OdtUnits.TryParseLength(part, out double points))
                width = points;
            else if (OdtUnits.TryParseColor(part, out BColor parsed))
                color = parsed;
            else if (TryParseStyle(part, out TableBorderStyle parsedStyle))
                style = parsedStyle;
        }

        return width > 0 && style != TableBorderStyle.None ? new TableBorder(color, width, style) : TableBorder.None;
    }

    private static bool TryParseStyle(string text, out TableBorderStyle style)
    {
        switch (text.ToLowerInvariant())
        {
            case "solid": style = TableBorderStyle.Solid; return true;
            case "dashed": style = TableBorderStyle.Dashed; return true;
            case "dotted": style = TableBorderStyle.Dotted; return true;
            case "double": style = TableBorderStyle.Double; return true;
            case "groove": style = TableBorderStyle.Groove; return true;
            case "ridge": style = TableBorderStyle.Ridge; return true;
            case "inset": style = TableBorderStyle.Inset; return true;
            case "outset": style = TableBorderStyle.Outset; return true;
            case "none": style = TableBorderStyle.None; return true;
            default: style = TableBorderStyle.Solid; return false;
        }
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
