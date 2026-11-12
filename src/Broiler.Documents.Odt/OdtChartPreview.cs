using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Resources;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Odt;

/// <summary>
/// Draws the plot of an embedded ODF chart (<c>office:chart</c>) that has no
/// stored replacement picture: a clustered column chart (<c>chart:bar</c>,
/// not turned, not stacked, two-dimensional) from its own data table, in the
/// places the chart file states. LibreOffice renders such a chart live from
/// the object's <c>content.xml</c>.
/// </summary>
/// <remarks>
/// Drawn: the wall, the value axis's major (and minor) grid, the axis lines
/// and their outer major ticks, the columns in their series' fills and lines,
/// and the legend's symbols. Text - axis labels, titles, legend entries - is
/// not drawn, as the DOCX chart previews do not draw it. The plot stands in
/// the <c>chartooo:coordinate-region</c> LibreOffice saved, or the plot
/// area's own rectangle where none was saved; the value axis is scaled as
/// LibreOffice's ScaleAutomatism does (a 1, 2 or 5 interval, at most ten of
/// them, from zero) unless the axis states its minimum, maximum or interval.
/// Anything else returns null and the frame keeps its empty box.
/// </remarks>
internal static class OdtChartPreview
{
    private static readonly XNamespace Chart = "urn:oasis:names:tc:opendocument:xmlns:chart:1.0";
    private static readonly XNamespace ChartOoo = "http://openoffice.org/2010/chart";

    /// <summary>LibreOffice's default series colours (the "LibreOffice" chart palette).</summary>
    private static readonly BColor[] SeriesColors =
    [
        BColor.FromArgb(0x00, 0x45, 0x86), BColor.FromArgb(0xFF, 0x42, 0x0E), BColor.FromArgb(0xFF, 0xD3, 0x20),
        BColor.FromArgb(0x57, 0x9D, 0x1C), BColor.FromArgb(0x7E, 0x00, 0x21), BColor.FromArgb(0x83, 0xCA, 0xFF),
        BColor.FromArgb(0x31, 0x40, 0x04), BColor.FromArgb(0xAE, 0xCF, 0x00), BColor.FromArgb(0x4B, 0x1F, 0x6F),
        BColor.FromArgb(0xFF, 0x95, 0x0E), BColor.FromArgb(0xC5, 0x00, 0x0B), BColor.FromArgb(0x00, 0x84, 0xD1),
    ];

    private static readonly BColor AxisGrey = BColor.FromArgb(0xB3, 0xB3, 0xB3);

    /// <summary>LibreOffice's major tick, 150 hundredths of a millimetre, in points.</summary>
    private const double TickLength = 150 * 72 / 2540.0;

    private const int MaxCategories = 512;
    private const int MaxSeries = 64;

    public static byte[]? Render(XElement root, double width, double height)
    {
        XElement? body = root.Element(OdtNamespaces.Office + "body")?.Element(OdtNamespaces.Office + "chart");
        XElement? chart = body?.Element(Chart + "chart");
        if (chart is null || !(width > 0) || !(height > 0) || (string?)chart.Attribute(Chart + "class") != "chart:bar")
            return null;
        XElement? plot = chart.Element(Chart + "plot-area");
        if (plot is null)
            return null;

        var styles = (root.Element(OdtNamespaces.Office + "automatic-styles")?.Elements(OdtNamespaces.Style + "style") ?? [])
            .Where(style => (string?)style.Attribute(OdtNamespaces.Style + "name") is not null)
            .GroupBy(style => (string)style.Attribute(OdtNamespaces.Style + "name")!)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        XElement? StyleOf(XElement? element) =>
            (string?)element?.Attribute(Chart + "style-name") is string name && styles.TryGetValue(name, out XElement? style) ? style : null;
        string? ChartProperty(XElement? element, string name) =>
            (string?)StyleOf(element)?.Element(OdtNamespaces.Style + "chart-properties")?.Attribute(Chart + name);
        XElement? Graphic(XElement? element) => StyleOf(element)?.Element(OdtNamespaces.Style + "graphic-properties");

        // Columns only: not turned into horizontal bars, not stacked, flat.
        if (ChartProperty(plot, "vertical") == "true" || ChartProperty(plot, "stacked") == "true" ||
            ChartProperty(plot, "percentage") == "true" || ChartProperty(plot, "three-dimensional") == "true" ||
            ChartProperty(plot, "deep") == "true")
            return null;

        if (!Length(chart, OdtNamespaces.Svg + "width", out double chartWidth) ||
            !Length(chart, OdtNamespaces.Svg + "height", out double chartHeight) || chartWidth <= 0 || chartHeight <= 0)
            return null;
        XElement region = plot.Element(ChartOoo + "coordinate-region") ?? plot;
        if (!Length(region, OdtNamespaces.Svg + "x", out double plotX) || !Length(region, OdtNamespaces.Svg + "y", out double plotY) ||
            !Length(region, OdtNamespaces.Svg + "width", out double plotWidth) ||
            !Length(region, OdtNamespaces.Svg + "height", out double plotHeight) || plotWidth <= 0 || plotHeight <= 0)
            return null;

        XElement? valueAxis = plot.Elements(Chart + "axis").FirstOrDefault(axis => (string?)axis.Attribute(Chart + "dimension") == "y");
        XElement? categoryAxis = plot.Elements(Chart + "axis").FirstOrDefault(axis => (string?)axis.Attribute(Chart + "dimension") == "x");
        if (ChartProperty(valueAxis, "logarithmic") == "true" || ChartProperty(valueAxis, "reverse-direction") == "true" ||
            ChartProperty(categoryAxis, "reverse-direction") == "true")
            return null;

        Dictionary<(int Row, int Column), XElement> table = ReadTable(chart);
        var series = new List<(XElement Element, double?[] Values)>();
        foreach (XElement item in plot.Elements(Chart + "series"))
        {
            string? seriesClass = (string?)item.Attribute(Chart + "class");
            if ((seriesClass is not null && seriesClass != "chart:bar") || (string?)item.Attribute(Chart + "attached-axis") is "secondary-y" ||
                series.Count == MaxSeries)
                return null;
            if (ReadRange((string?)item.Attribute(Chart + "values-cell-range-address"), table) is not double?[] values)
                return null;
            series.Add((item, values));
        }
        int categories = series.Count == 0 ? 0 : series.Max(item => item.Values.Length);
        if (categories == 0 || categories > MaxCategories)
            return null;

        // An automatically placed plot area is laid out afresh by LibreOffice
        // whenever it draws the chart: the saved region holds only until the
        // labels change, and a file saved by an older layout (tdf127304.odt,
        // whose three-level category labels were laid out wrongly) keeps an
        // out-of-date one. Its height is worked out again here from the plot
        // area's outer rectangle, which keeps the labels as well.
        if (ChartProperty(plot, "auto-position") == "true" && region != plot &&
            Length(plot, OdtNamespaces.Svg + "y", out double outerY) && Length(plot, OdtNamespaces.Svg + "height", out double outerHeight) &&
            CategoryBand(categoryAxis, ChartProperty, StyleOf, table, plotWidth / categories) is double band)
        {
            double valueLabels = ChartProperty(valueAxis, "display-label") != "false" && valueAxis is not null
                ? LabelHeight(StyleOf(valueAxis)) / 2
                : 0;
            double innerTop = outerY + valueLabels, innerBottom = outerY + outerHeight - band;
            if (innerBottom - innerTop > 1)
                (plotY, plotHeight) = (innerTop, innerBottom - innerTop);
        }

        // The value axis: stated limits and interval, else automatic.
        double dataMinimum = Math.Min(0, series.SelectMany(item => item.Values).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(0).Min());
        double dataMaximum = Math.Max(0, series.SelectMany(item => item.Values).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(0).Max());
        double? statedMinimum = Number(ChartProperty(valueAxis, "minimum"));
        double? statedMaximum = Number(ChartProperty(valueAxis, "maximum"));
        double? statedInterval = Number(ChartProperty(valueAxis, "interval-major"));
        (double minimum, double maximum, double interval) = Scale(statedMinimum ?? dataMinimum, statedMaximum ?? dataMaximum,
            statedMinimum.HasValue, statedMaximum.HasValue, statedInterval);
        if (!(maximum > minimum) || !(interval > 0) || (maximum - minimum) / interval > 1000)
            return null;

        double scale = Math.Min(2, 1024 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));
        // Chart coordinates are points from the chart's own top left; the
        // object is stretched to its frame.
        double sx = pixelsWide / chartWidth, sy = pixelsHigh / chartHeight;
        // A hairline is a device pixel in LibreOffice. One point keeps it a
        // pixel wide once the picture is scaled down to the page.
        float hairline = (float)Math.Max(1, scale);
        float left = (float)(plotX * sx), top = (float)(plotY * sy);
        float right = (float)((plotX + plotWidth) * sx), bottom = (float)((plotY + plotHeight) * sy);
        float ValueY(double value) => bottom - (float)((Math.Clamp(value, minimum, maximum) - minimum) / (maximum - minimum)) * (bottom - top);

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            if (Fill(Graphic(chart)) is BColor background)
                canvas.FillRect(new RectangleF(0, 0, pixelsWide, pixelsHigh), background);
            XElement? wall = plot.Element(Chart + "wall");
            if (Fill(Graphic(wall)) is BColor wallFill)
                canvas.FillRect(new RectangleF(left, top, right - left, bottom - top), wallFill);

            // Grid lines under the columns: minor, then major.
            foreach (XElement grid in valueAxis?.Elements(Chart + "grid") ?? [])
            {
                bool major = (string?)grid.Attribute(Chart + "class") != "minor";
                double step = interval;
                if (!major)
                {
                    double divisor = Number(ChartProperty(valueAxis, "interval-minor-divisor")) ?? 2;
                    if (!(divisor >= 1) || divisor > 100)
                        continue;
                    step = interval / divisor;
                }
                BColor color = Stroke(Graphic(grid)) ?? AxisGrey;
                for (int i = 0; minimum + i * step <= maximum + step * 1e-9 && i <= 2000; i++)
                {
                    float y = ValueY(minimum + i * step);
                    canvas.DrawLine(new PointF(left, y), new PointF(right, y), color, hairline);
                }
            }

            if (Stroke(Graphic(wall), solidDefault: false) is BColor wallLine)
                canvas.DrawRectangleStroke(new RectangleF(left, top, right - left, bottom - top), wallLine, hairline);

            // The columns: per category a cluster of one column per series,
            // with gap-width percent of a column between clusters and the
            // columns of a cluster overlapping by overlap percent.
            XElement? firstSeries = series.Count > 0 ? series[0].Element : null;
            double gap = Number(ChartProperty(firstSeries, "gap-width") ?? ChartProperty(plot, "gap-width")) ?? 100;
            double overlap = Number(ChartProperty(firstSeries, "overlap") ?? ChartProperty(plot, "overlap")) ?? 0;
            float slot = (right - left) / categories;
            int count = series.Count;
            double columns = count - (count - 1) * overlap / 100 + Math.Max(0, gap) / 100;
            float column = (float)(slot / Math.Max(0.01, columns));
            float step2 = column * (float)(1 - overlap / 100);
            float zero = ValueY(Math.Clamp(0, minimum, maximum));
            for (int s = 0; s < count; s++)
            {
                XElement? graphic = Graphic(series[s].Element);
                BColor fill = Fill(graphic, solidDefault: true) ?? SeriesColors[s % SeriesColors.Length];
                if ((string?)graphic?.Attribute(OdtNamespaces.Draw + "fill") == "none")
                    fill = BColor.Empty;
                BColor? line = Stroke(graphic, solidDefault: false);
                for (int c = 0; c < series[s].Values.Length; c++)
                {
                    if (series[s].Values[c] is not double value)
                        continue;
                    float x = left + c * slot + (float)(slot - column - step2 * (count - 1)) / 2 + s * step2;
                    float y = ValueY(value);
                    var bar = new RectangleF(x, Math.Min(y, zero), column, Math.Abs(zero - y));
                    if (!fill.IsEmpty)
                        canvas.FillRect(bar, fill);
                    if (line is BColor outline)
                        canvas.DrawRectangleStroke(bar, outline, hairline);
                }
            }

            // Axis lines and their outer major ticks.
            float tickX = (float)(TickLength * sx), tickY = (float)(TickLength * sy);
            if (valueAxis is not null)
            {
                BColor color = Stroke(Graphic(valueAxis)) ?? AxisGrey;
                canvas.DrawLine(new PointF(left, top), new PointF(left, bottom), color, hairline);
                for (int i = 0; minimum + i * interval <= maximum + interval * 1e-9 && i <= 2000; i++)
                {
                    float y = ValueY(minimum + i * interval);
                    canvas.DrawLine(new PointF(left - tickX, y), new PointF(left, y), color, hairline);
                }
            }
            if (categoryAxis is not null)
            {
                BColor color = Stroke(Graphic(categoryAxis)) ?? AxisGrey;
                canvas.DrawLine(new PointF(left, zero), new PointF(right, zero), color, hairline);
                for (int c = 0; c <= categories; c++)
                {
                    float x = left + c * slot;
                    canvas.DrawLine(new PointF(x, bottom), new PointF(x, bottom + tickY), color, hairline);
                }
            }

            // The legend's symbols, one row per series from the legend's
            // saved top left. The entries' names are text and not drawn.
            XElement? legend = chart.Element(Chart + "legend");
            if (legend is not null && Length(legend, OdtNamespaces.Svg + "x", out double legendX) &&
                Length(legend, OdtNamespaces.Svg + "y", out double legendY))
            {
                double fontSize = OdtUnits.TryParseLength((string?)StyleOf(legend)?.Element(OdtNamespaces.Style + "text-properties")
                    ?.Attribute(OdtNamespaces.Fo + "font-size"), out double size) && size > 0 ? size : 10;
                double symbol = fontSize * 0.6, pitch = fontSize * 1.43, padding = fontSize * 0.3;
                for (int s = 0; s < count; s++)
                {
                    XElement? graphic = Graphic(series[s].Element);
                    BColor fill = Fill(graphic, solidDefault: true) ?? SeriesColors[s % SeriesColors.Length];
                    double y = legendY + padding + s * pitch + (pitch - symbol) / 2 - (pitch - fontSize) / 2;
                    canvas.FillRect(new RectangleF((float)((legendX + padding) * sx), (float)(y * sy), (float)(symbol * sx), (float)(symbol * sy)), fill);
                }
            }
        }

        return EmfRasterizer.EncodePng(bitmap);
    }

    /// <summary>
    /// LibreOffice's automatic linear axis (ScaleAutomatism): the range is
    /// widened to include zero, cut into at most ten intervals of 1, 2 or 5
    /// times a power of ten, and its ends rounded out to whole intervals.
    /// </summary>
    internal static (double Minimum, double Maximum, double Interval) Scale(double minimum, double maximum,
        bool fixedMinimum, bool fixedMaximum, double? statedInterval)
    {
        const int maxIntervals = 10;
        if (maximum < minimum)
            (minimum, maximum) = (maximum, minimum);
        if (statedInterval is double fixedInterval && fixedInterval > 0)
        {
            double low = fixedMinimum ? minimum : Math.Floor(minimum / fixedInterval) * fixedInterval;
            double high = fixedMaximum ? maximum : Math.Ceiling(maximum / fixedInterval) * fixedInterval;
            if (high <= low)
                high = low + fixedInterval;
            return (low, high, fixedInterval);
        }

        double distance = (maximum - minimum) / maxIntervals;
        if (!(distance > 0))
            distance = 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(distance)));
        double normalized = distance / magnitude;
        normalized = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            double interval = normalized * magnitude;
            double low = fixedMinimum ? minimum : Math.Floor(minimum / interval + 1e-9) * interval;
            double high = fixedMaximum ? maximum : Math.Ceiling(maximum / interval - 1e-9) * interval;
            if (high <= low)
                high = low + interval;
            if ((high - low) / interval <= maxIntervals + 1e-9)
                return (low, high, interval);
            if (normalized == 1)
                normalized = 2;
            else if (normalized == 2)
                normalized = 5;
            else
            {
                normalized = 1;
                magnitude *= 10;
            }
        }
        return (minimum, maximum, maximum - minimum);
    }

    /// <summary>The 100 hundredths of a millimetre LibreOffice leaves between a tick and its label (AXIS2D_TICKLABELSPACING), in points.</summary>
    private const double LabelSpacing = 100 * 72 / 2540.0;

    /// <summary>
    /// The height the category axis's labels take below the plot, as
    /// LibreOffice lays them out: the outer tick, a label spacing, then one
    /// row per category level, each as tall as its text (Liberation Sans:
    /// ascent and descent 1.117 em). Innermost labels too wide for their
    /// category stand turned a quarter on a multi-level axis, so their row is
    /// as tall as the widest of them. Null where this cannot be told
    /// without the labels' layout: single-level labels too wide to stand side
    /// by side (LibreOffice staggers or turns them), or no category labels.
    /// </summary>
    private static double? CategoryBand(XElement? axis, Func<XElement?, string, string?> chartProperty,
        Func<XElement?, XElement?> styleOf, Dictionary<(int Row, int Column), XElement> table, double slot)
    {
        if (axis is null)
            return 0;
        if (chartProperty(axis, "display-label") == "false")
            return null;
        List<string[]>? labels = ReadCategories((string?)axis.Element(Chart + "categories")?.Attribute(OdtNamespaces.Table + "cell-range-address"), table);
        if (labels is not { Count: > 0 })
            return null;
        XElement? style = styleOf(axis);
        double size = FontSize(style);
        double lineHeight = LabelHeight(style);
        int levels = labels.Max(label => label.Length);
        double widest = labels.Max(label => label.Length == levels ? TextWidth(label[^1], size) : 0);
        double band = (chartProperty(axis, "tick-marks-major-outer") == "false" ? 0 : TickLength) + LabelSpacing;
        if (widest <= slot)
            band += lineHeight;
        else if (levels > 1)
            band += widest;
        else
            return null;
        return band + (levels - 1) * lineHeight;
    }

    private static double FontSize(XElement? style) =>
        OdtUnits.TryParseLength((string?)style?.Element(OdtNamespaces.Style + "text-properties")?.Attribute(OdtNamespaces.Fo + "font-size"),
            out double size) && size > 0 ? size : 10;

    private static double LabelHeight(XElement? style) => FontSize(style) * 1.117;

    /// <summary>
    /// The category labels a range names, each as its levels from the outermost
    /// in: a cell holding a <c>text:list</c> gives one level per item (the
    /// complex categories LibreOffice saves), any other its paragraph's text.
    /// </summary>
    private static List<string[]>? ReadCategories(string? address, Dictionary<(int Row, int Column), XElement> table)
    {
        if (Range(address) is not ((int row1, int column1), (int row2, int column2)))
            return null;
        var labels = new List<string[]>();
        for (int row = row1; row <= row2; row++)
        {
            for (int column = column1; column <= column2; column++)
            {
                if (!table.TryGetValue((row, column), out XElement? cell))
                {
                    labels.Add([""]);
                    continue;
                }
                XElement? list = cell.Element(OdtNamespaces.Text + "list");
                labels.Add(list is not null
                    ? [.. list.Elements(OdtNamespaces.Text + "list-item").Select(item => item.Value)]
                    : [cell.Element(OdtNamespaces.Text + "p")?.Value ?? ""]);
            }
        }
        return labels;
    }

    /// <summary>A label's advance width in points, by Liberation Sans's (Arial's, Helvetica's) widths.</summary>
    internal static double TextWidth(string text, double size)
    {
        double width = 0;
        foreach (char c in text)
            width += c is >= ' ' and <= '~' ? SansWidths[c - ' '] : 556;
        return width * size / 1000;
    }

    private static readonly short[] SansWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    /// <summary>The chart's own data table (<c>local-table</c>): its cells by one-based row and column.</summary>
    private static Dictionary<(int Row, int Column), XElement> ReadTable(XElement chart)
    {
        var cells = new Dictionary<(int, int), XElement>();
        XElement? table = chart.Element(OdtNamespaces.Table + "table");
        if (table is null)
            return cells;
        int row = 0;
        foreach (XElement tableRow in table.Descendants(OdtNamespaces.Table + "table-row"))
        {
            int rows = Repeat(tableRow, "number-rows-repeated");
            for (int r = 0; r < rows && row < 4096; r++)
            {
                row++;
                int column = 0;
                foreach (XElement cell in tableRow.Elements())
                {
                    if (cell.Name.LocalName is not ("table-cell" or "covered-table-cell"))
                        continue;
                    int columns = Repeat(cell, "number-columns-repeated");
                    for (int c = 0; c < columns && column < 1024; c++)
                        cells[(row, ++column)] = cell;
                }
            }
        }
        return cells;

        static int Repeat(XElement element, string name) =>
            int.TryParse((string?)element.Attribute(OdtNamespaces.Table + name), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0
                ? Math.Min(n, 1024)
                : 1;
    }

    /// <summary>
    /// The values a range address such as <c>local-table.$B$2:.$B$5</c> names,
    /// in reading order; a cell with no number is a gap. Null for an address
    /// this reader does not follow.
    /// </summary>
    private static double?[]? ReadRange(string? address, Dictionary<(int Row, int Column), XElement> table)
    {
        if (Range(address) is not ((int row1, int column1), (int row2, int column2)))
            return null;
        var values = new List<double?>();
        for (int row = row1; row <= row2; row++)
            for (int column = column1; column <= column2; column++)
                values.Add(table.TryGetValue((row, column), out XElement? cell) ? CellValue(cell) : null);
        return [.. values];
    }

    private static double? CellValue(XElement cell)
    {
        double? value = Number((string?)cell.Attribute(OdtNamespaces.Office + "value"));
        if (value is null && (string?)cell.Attribute(OdtNamespaces.Office + "value-type") is null or "float" or "percentage")
            value = Number(cell.Element(OdtNamespaces.Text + "p")?.Value);
        return value;
    }

    /// <summary>The first and last cell of a range address in the chart's own table; null for one this reader does not follow.</summary>
    private static ((int Row, int Column) First, (int Row, int Column) Last)? Range(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Contains(' '))
            return null;
        string[] ends = address.Split(':');
        if (ends.Length is not (1 or 2) || Cell(ends[0]) is not (int row1, int column1))
            return null;
        (int row2, int column2) = ends.Length == 2 && Cell(ends[1]) is (int r, int c) ? (r, c) : (row1, column1);
        if (row2 < row1 || column2 < column1 || (long)(row2 - row1 + 1) * (column2 - column1 + 1) > MaxCategories)
            return null;
        return ((row1, column1), (row2, column2));

        static (int Row, int Column)? Cell(string text)
        {
            int dot = text.LastIndexOf('.');
            string reference = (dot >= 0 ? text[(dot + 1)..] : text).Replace("$", "", StringComparison.Ordinal);
            int i = 0, column = 0;
            while (i < reference.Length && char.IsAsciiLetter(reference[i]) && i < 4)
                column = column * 26 + (char.ToUpperInvariant(reference[i++]) - 'A' + 1);
            return column > 0 && i < reference.Length &&
                int.TryParse(reference.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out int row) && row > 0
                ? (row, column)
                : null;
        }
    }

    private static BColor? Fill(XElement? graphic, bool solidDefault = false)
    {
        string? fill = (string?)graphic?.Attribute(OdtNamespaces.Draw + "fill");
        if (fill is not null && fill != "solid")
            return null;
        if (fill is null && !solidDefault)
            return null;
        return Color((string?)graphic?.Attribute(OdtNamespaces.Draw + "fill-color"));
    }

    private static BColor? Stroke(XElement? graphic, bool solidDefault = true)
    {
        string? stroke = (string?)graphic?.Attribute(OdtNamespaces.Draw + "stroke");
        if (stroke == "none" || (stroke is null && !solidDefault))
            return null;
        return Color((string?)graphic?.Attribute(OdtNamespaces.Svg + "stroke-color"));
    }

    private static BColor? Color(string? value) =>
        value is { Length: 7 } && value[0] == '#' &&
        int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
            ? BColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
            : null;

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) &&
        Math.Abs(number) < 1e100
            ? number
            : null;

    private static bool Length(XElement element, XName name, out double points) =>
        OdtUnits.TryParseLength((string?)element.Attribute(name), out points);
}
