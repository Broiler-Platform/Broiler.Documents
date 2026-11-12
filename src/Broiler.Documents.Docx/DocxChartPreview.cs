using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;

namespace Broiler.Documents.Docx;

/// <summary>Renders the plot of a clustered column or an area chart from its embedded numeric cache.</summary>
internal static partial class DocxChartPreview
{
    private static readonly XNamespace C = DocxNamespaces.DrawingChart;
    private static readonly BColor[] SeriesColors =
    [
        new(0x4F, 0x81, 0xBD), new(0xC0, 0x50, 0x4D), new(0x9B, 0xBB, 0x59),
        new(0x80, 0x64, 0xA2), new(0x4B, 0xAC, 0xC6), new(0xF7, 0x96, 0x46),
    ];

    public static byte[]? Render(XElement root, double width, double height, DocxTheme? theme = null)
    {
        XElement? chart = root.Element(C + "chart");
        XElement? plot = chart?.Element(C + "plotArea");
        if (plot?.Element(C + "areaChart") is XElement area)
            return RenderArea(chart!, plot, area, width, height, theme ?? DocxTheme.Empty);
        if (plot?.Element(C + "pieChart") is XElement pie)
            return RenderPie(root, chart!, plot, pie, width, height, theme ?? DocxTheme.Empty);
        if (plot?.Element(C + "bar3DChart") is XElement bars3D)
            return RenderBars(root, chart!, plot, bars3D, width, height, theme ?? DocxTheme.Empty);
        XElement? bars = plot?.Element(C + "barChart");
        if (bars is not null && Value(bars, "barDir") == "bar")
            return RenderBars(root, chart!, plot!, bars, width, height, theme ?? DocxTheme.Empty);
        if (bars is not null && Value(bars, "barDir") == "col" && Value(bars, "grouping") is "stacked" or "percentStacked")
            return RenderStackedColumns(root, chart!, plot!, bars, width, height, theme ?? DocxTheme.Empty);
        if (bars is null || Value(bars, "barDir") != "col" ||
            Value(bars, "grouping") is not (null or "clustered") ||
            plot!.Elements().Count(element => element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)) != 1 ||
            plot.Descendants(C + "logBase").Any())
            return null;

        var series = new List<Dictionary<int, double>>();
        int categories = 0;
        double minimum = 0, maximum = 0;
        foreach (XElement item in bars.Elements(C + "ser"))
        {
            XElement? values = item.Element(C + "val");
            XElement? cache = values?.Element(C + "numRef")?.Element(C + "numCache") ?? values?.Element(C + "numLit");
            if (cache is null || series.Count == 16)
                return null;
            var numbers = new Dictionary<int, double>();
            foreach (XElement point in cache.Elements(C + "pt"))
            {
                if (!int.TryParse((string?)point.Attribute("idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                    index is < 0 or >= 256 ||
                    !double.TryParse(point.Element(C + "v")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                    !double.IsFinite(number) || Math.Abs(number) > 1e100 || !numbers.TryAdd(index, number))
                    return null;
                categories = Math.Max(categories, index + 1);
                minimum = Math.Min(minimum, number);
                maximum = Math.Max(maximum, number);
            }
            series.Add(numbers);
        }
        if (categories == 0 || maximum - minimum < 1e-100)
            return null;

        double interval = (maximum - minimum) / 5;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(interval)));
        double unit = interval / magnitude;
        double step = magnitude * (unit <= 1 ? 1 : unit <= 2 ? 2 : unit <= 5 ? 5 : 10);
        minimum = Math.Floor(minimum / step) * step;
        maximum = Math.Ceiling(maximum / step) * step;

        double scale = Math.Min(2, 1024 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));
        float left = pixelsWide * 0.055f;
        float right = pixelsWide * (chart!.Element(C + "legend") is null ? 0.97f : 0.87f);
        float top = pixelsHigh * 0.075f;
        float bottom = pixelsHigh * 0.91f;
        float cluster = (right - left) / categories;
        int gap = int.TryParse(Value(bars, "gapWidth"), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedGap)
            ? Math.Clamp(parsedGap, 0, 500) : 150;
        float column = cluster / (series.Count + gap / 100f);
        float Y(double value) => bottom - (float)((value - minimum) / (maximum - minimum)) * (bottom - top);

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            var bounds = new RectangleF(0.5f, 0.5f, pixelsWide - 1, pixelsHigh - 1);
            canvas.FillRect(bounds, new BColor(255, 255, 255));
            canvas.DrawRectangleStroke(bounds, new BColor(216, 216, 216), 1);
            int intervals = (int)Math.Round((maximum - minimum) / step);
            for (int tick = 0; tick <= intervals; tick++)
            {
                float y = Y(minimum + step * tick);
                canvas.DrawLine(new PointF(left, y), new PointF(right, y), new BColor(166, 166, 166), 1);
            }

            float zero = Y(0);
            for (int i = 0; i < series.Count; i++)
            {
                BColor color = SeriesColors[i % SeriesColors.Length];
                foreach ((int category, double number) in series[i])
                {
                    float y = Y(number);
                    float x = left + (category + 0.5f) * cluster + (i - series.Count / 2f) * column;
                    canvas.FillRect(new RectangleF(x, Math.Min(y, zero), column, Math.Abs(y - zero)), color);
                }
            }
            canvas.DrawLine(new PointF(left, top), new PointF(left, bottom), new BColor(166, 166, 166), 1);
        }
        return DocxVmlPath.EncodePng(bitmap);
    }

    /// <summary>
    /// Draws an area chart — standard, stacked or percent-stacked — from the
    /// series' cached values: the frame, the value axis's major gridlines, and
    /// each series as a filled band in the colour its <c>c:spPr</c> states (or
    /// the theme's accent for its index). Titles, labels and the legend are not
    /// drawn; the plot leaves the room LibreOffice's layout gives them.
    /// </summary>
    private static byte[]? RenderArea(XElement chart, XElement plot, XElement area, double width, double height, DocxTheme theme)
    {
        if (plot.Elements().Count(element => element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)) != 1 ||
            plot.Descendants(C + "logBase").Any() ||
            plot.Element(C + "valAx")?.Element(C + "scaling")?.Element(C + "orientation")?.Attribute("val")?.Value == "maxMin" ||
            plot.Element(C + "catAx")?.Element(C + "scaling")?.Element(C + "orientation")?.Attribute("val")?.Value == "maxMin" ||
            plot.Element(C + "dateAx")?.Element(C + "scaling")?.Element(C + "orientation")?.Attribute("val")?.Value == "maxMin")
            return null;
        string grouping = Value(area, "grouping") ?? "standard";
        if (grouping is not ("standard" or "stacked" or "percentStacked"))
            return null;

        var colors = new List<BColor>();
        var parsed = new List<Dictionary<int, double>>();
        int categories = 0;
        foreach (XElement item in area.Elements(C + "ser"))
        {
            XElement? values = item.Element(C + "val");
            XElement? cache = values?.Element(C + "numRef")?.Element(C + "numCache") ?? values?.Element(C + "numLit");
            if (cache is null || parsed.Count == 16)
                return null;
            var numbers = new Dictionary<int, double>();
            foreach (XElement point in cache.Elements(C + "pt"))
            {
                if (!int.TryParse((string?)point.Attribute("idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                    index is < 0 or >= 256 ||
                    !double.TryParse(point.Element(C + "v")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                    !double.IsFinite(number) || Math.Abs(number) > 1e100 || !numbers.TryAdd(index, number))
                    return null;
                categories = Math.Max(categories, index + 1);
            }
            parsed.Add(numbers);
            int order = colors.Count;
            if (!DocxDrawingColor.TryRead(item.Element(C + "spPr")?.Element(DocxNamespaces.Drawing + "solidFill"), theme, out BColor color) &&
                !theme.TryResolveColor("accent" + (order % 6 + 1), out color))
                color = SeriesColors[order % SeriesColors.Length];
            colors.Add(color);
        }
        if (categories == 0)
            return null;

        // Each band runs from the series below it (the axis for the first, or
        // for every series of a standard chart) to its own top.
        var lower = new double[colors.Count][];
        var upper = new double[colors.Count][];
        var running = new double[categories];
        var totals = new double[categories];
        for (int i = 0; i < colors.Count; i++)
            foreach ((int category, double number) in parsed[i])
                totals[category] += Math.Abs(number);
        double minimum = double.MaxValue, maximum = double.MinValue;
        for (int i = 0; i < colors.Count; i++)
        {
            lower[i] = new double[categories];
            upper[i] = new double[categories];
            for (int category = 0; category < categories; category++)
            {
                double number = parsed[i].GetValueOrDefault(category);
                if (grouping == "percentStacked")
                    number = totals[category] > 0 ? number / totals[category] : 0;
                lower[i][category] = grouping == "standard" ? double.NaN : running[category];
                upper[i][category] = grouping == "standard" ? number : running[category] + number;
                if (grouping != "standard")
                    running[category] += number;
                minimum = Math.Min(minimum, upper[i][category]);
                maximum = Math.Max(maximum, upper[i][category]);
            }
        }
        if (grouping != "standard")
        {
            minimum = Math.Min(minimum, 0);
            maximum = Math.Max(maximum, 0);
        }

        XElement? scaling = plot.Element(C + "valAx")?.Element(C + "scaling");
        double? fixedMinimum = Number(scaling?.Element(C + "min"));
        double? fixedMaximum = Number(scaling?.Element(C + "max"));
        (double axisMinimum, double axisMaximum, double step) = grouping == "percentStacked"
            ? (minimum < 0 ? -1 : 0, maximum > 0 ? 1 : 0, 0.1)
            : AutoScale(minimum, maximum, fixedMinimum, fixedMaximum);
        if (!(axisMaximum - axisMinimum > 1e-100) || !(step > 0) || (axisMaximum - axisMinimum) / step > 1000)
            return null;

        double scale = Math.Min(2, 1024 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));
        bool titled = chart.Element(C + "title") is not null && Value(chart, "autoTitleDeleted") is not ("1" or "true");
        string? legend = chart.Element(C + "legend") is XElement legendElement ? Value(legendElement, "legendPos") ?? "r" : null;
        float left = pixelsWide * 0.06f;
        float right = pixelsWide * (legend == "r" ? 0.83f : 0.95f);
        float top = pixelsHigh * (titled ? 0.175f : 0.04f) + (legend == "t" ? pixelsHigh * 0.09f : 0);
        float bottom = pixelsHigh * (legend == "b" ? 0.82f : 0.91f);
        bool between = Value(plot.Element(C + "valAx") ?? plot, "crossBetween") is not "midCat";
        float X(int category) => between || categories == 1
            ? left + (category + 0.5f) * (right - left) / categories
            : left + category * (right - left) / (categories - 1);
        float Y(double value) => bottom - (float)((Math.Clamp(value, axisMinimum, axisMaximum) - axisMinimum) / (axisMaximum - axisMinimum)) * (bottom - top);

        var grid = new BColor(217, 217, 217);
        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            var bounds = new RectangleF(0.5f, 0.5f, pixelsWide - 1, pixelsHigh - 1);
            canvas.FillRect(bounds, new BColor(255, 255, 255));
            canvas.DrawRectangleStroke(bounds, grid, 1);
            if (plot.Element(C + "valAx")?.Element(C + "majorGridlines") is not null)
            {
                int intervals = (int)Math.Round((axisMaximum - axisMinimum) / step);
                for (int tick = 0; tick <= intervals; tick++)
                {
                    float y = Y(axisMinimum + step * tick);
                    canvas.DrawLine(new PointF(left, y), new PointF(right, y), grid, 1);
                }
            }

            // The first series is in front: LibreOffice draws an area chart's
            // series last to first, so in a standard chart a later series shows
            // only where it rises above the earlier ones.
            double baseline = Math.Clamp(0, axisMinimum, axisMaximum);
            for (int i = colors.Count - 1; i >= 0; i--)
            {
                var outline = new PointF[categories * 2];
                for (int category = 0; category < categories; category++)
                {
                    double floor = double.IsNaN(lower[i][category]) ? baseline : lower[i][category];
                    outline[category] = new PointF(X(category), Y(upper[i][category]));
                    outline[categories * 2 - 1 - category] = new PointF(X(category), Y(floor));
                }
                if (categories == 1)
                    canvas.DrawLine(outline[0], outline[1], colors[i], 1);
                else
                    canvas.FillGlyphContours([outline], colors[i]);
            }
            canvas.DrawLine(new PointF(left, Y(baseline)), new PointF(right, Y(baseline)), grid, 1);
        }
        return DocxVmlPath.EncodePng(bitmap);
    }

    /// <summary>
    /// Draws a pie chart from its first series' cached values, as LibreOffice
    /// draws <c>c:pieChart</c>: the slices run clockwise from twelve o'clock
    /// turned by <c>c:firstSliceAng</c>, each filled with its data point's
    /// colour (<c>c:dPt</c>), else the series' colour, else — with
    /// <c>c:varyColors</c> — the theme's accent for its index, and outlined with
    /// the line the point or series states. The pie is centred in the plot area
    /// and leaves room for the title, the legend and outside data labels; the
    /// labels themselves are not drawn.
    /// </summary>
    private static byte[]? RenderPie(XElement root, XElement chart, XElement plot, XElement pie, double width, double height, DocxTheme theme)
    {
        if (ChartTypeCount(plot) != 1 || pie.Element(C + "ser") is not XElement series ||
            ReadValues(series) is not Dictionary<int, double> values)
            return null;
        int points = values.Count == 0 ? 0 : values.Keys.Max() + 1;
        double total = values.Values.Sum(Math.Abs);
        if (points == 0 || !(total > 0) || !double.IsFinite(total))
            return null;

        double scale = Math.Min(2, 1024 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));
        bool titled = chart.Element(C + "title") is not null && Value(chart, "autoTitleDeleted") is not ("1" or "true");
        XElement? legendElement = chart.Element(C + "legend");
        string? legend = legendElement is null ? null : Value(legendElement, "legendPos") ?? "r";
        // The title and a side legend take room for their text, which does not
        // shrink with the chart: about 34 pt for a title line, and the longest
        // legend entry (the categories', as the pie varies its colours) at
        // about 5.5 pt a character plus its key.
        int longest = series.Element(C + "cat")?.Descendants(C + "v").Select(entry => entry.Value.Length).DefaultIfEmpty(0).Max() ?? 0;
        float legendWidth = (float)Math.Clamp((Math.Min(longest, 40) * 5.5 + 22) * scale, pixelsWide * 0.17, pixelsWide * 0.45);
        float titleHeight = titled ? (float)Math.Min(Math.Max(pixelsHigh * 0.135, 34 * scale), pixelsHigh * 0.35) : 0;
        float left = legend == "l" ? legendWidth : pixelsWide * 0.02f;
        float right = legend == "r" ? pixelsWide - legendWidth : pixelsWide * 0.98f;
        float top = pixelsHigh * (0.04f + (legend == "t" ? 0.09f : 0)) + titleHeight;
        float bottom = pixelsHigh * (legend == "b" ? 0.86f : 0.96f);
        // Labels outside the slices take a line of text all round.
        if (OutsideLabels(series) || OutsideLabels(pie))
        {
            float margin = pixelsHigh * 0.04f;
            (left, right, top, bottom) = (left + margin, right - margin, top + margin, bottom - margin);
        }
        float radius = Math.Min(right - left, bottom - top) / 2;
        if (!(radius > 1))
            return null;
        float centerX = (left + right) / 2, centerY = (top + bottom) / 2;

        bool vary = pie.Element(C + "varyColors") is not XElement varyElement ||
            (string?)varyElement.Attribute("val") is null or "1" or "true";
        XElement? seriesShape = series.Element(C + "spPr");
        var pointShapes = new Dictionary<int, XElement>();
        foreach (XElement dataPoint in series.Elements(C + "dPt"))
            if (int.TryParse(Value(dataPoint, "idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                dataPoint.Element(C + "spPr") is XElement shape)
                pointShapes.TryAdd(index, shape);

        double angle = (Number(pie.Element(C + "firstSliceAng")) ?? 0) * Math.PI / 180;
        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            DrawChartSpace(canvas, root, theme, pixelsWide, pixelsHigh);
            for (int index = 0; index < points; index++)
            {
                double share = Math.Abs(values.GetValueOrDefault(index)) / total;
                if (!(share > 0))
                    continue;
                double sweep = share * 2 * Math.PI;
                int steps = Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 180)));
                var outline = new PointF[steps + 3];
                outline[0] = new PointF(centerX, centerY);
                for (int step = 0; step <= steps; step++)
                {
                    double at = angle + sweep * step / steps;
                    outline[step + 1] = new PointF(centerX + radius * (float)Math.Sin(at), centerY - radius * (float)Math.Cos(at));
                }
                outline[steps + 2] = outline[0];
                angle += sweep;

                XElement? pointShape = pointShapes.GetValueOrDefault(index);
                if (!TryFill(pointShape, theme, out BColor fill) && !TryFill(seriesShape, theme, out fill))
                    fill = AccentColor(vary ? index : 0, theme);
                if (!fill.IsEmpty)
                    canvas.FillGlyphContours([outline], fill);
                if (TryLine(pointShape?.Element(DocxNamespaces.Drawing + "ln") ?? seriesShape?.Element(DocxNamespaces.Drawing + "ln"),
                    theme, scale, out BColor line, out float lineWidth))
                    canvas.DrawPathStroke(outline, line, lineWidth);
            }
        }
        return DocxVmlPath.EncodePng(bitmap);
    }

    /// <summary>
    /// Draws a stacked or percent-stacked column chart from the series' cached
    /// values: the frame, the value axis's major gridlines and, per category, one
    /// column of the series' segments, positive values stacked up from zero and
    /// negative ones down, each in the colour its <c>c:spPr</c> states (or the
    /// theme's accent for its index). Titles, labels and the legend are not
    /// drawn; the plot leaves the room LibreOffice's layout gives them.
    /// </summary>
    private static byte[]? RenderStackedColumns(XElement root, XElement chart, XElement plot, XElement bars, double width, double height, DocxTheme theme)
    {
        if (ChartTypeCount(plot) != 1 || plot.Descendants(C + "logBase").Any() ||
            plot.Elements().Any(axis => axis.Element(C + "scaling")?.Element(C + "orientation")?.Attribute("val")?.Value == "maxMin"))
            return null;
        bool percent = Value(bars, "grouping") == "percentStacked";
        var series = new List<(Dictionary<int, double> Values, BColor Color, Dictionary<int, BColor> Points)>();
        int categories = 0;
        foreach (XElement item in bars.Elements(C + "ser"))
        {
            if (series.Count == 16 || ReadValues(item) is not Dictionary<int, double> values)
                return null;
            if (values.Count > 0)
                categories = Math.Max(categories, values.Keys.Max() + 1);
            if (!TryFill(item.Element(C + "spPr"), theme, out BColor color))
                color = AccentColor(series.Count, theme);
            var points = new Dictionary<int, BColor>();
            foreach (XElement dataPoint in item.Elements(C + "dPt"))
                if (int.TryParse(Value(dataPoint, "idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                    TryFill(dataPoint.Element(C + "spPr"), theme, out BColor pointColor))
                    points.TryAdd(index, pointColor);
            series.Add((values, color, points));
        }
        if (categories == 0)
            return null;

        // Each segment runs from the top of the stack below it to its own top.
        var segments = new List<(int Category, double From, double To, BColor Color)>();
        double minimum = 0, maximum = 0;
        for (int category = 0; category < categories; category++)
        {
            double total = series.Sum(item => Math.Abs(item.Values.GetValueOrDefault(category)));
            double above = 0, below = 0;
            foreach (var item in series)
            {
                double number = item.Values.GetValueOrDefault(category);
                if (percent)
                    number = total > 0 ? number / total : 0;
                if (number == 0)
                    continue;
                BColor color = item.Points.TryGetValue(category, out BColor pointColor) ? pointColor : item.Color;
                if (number > 0)
                    segments.Add((category, above, above += number, color));
                else
                    segments.Add((category, below, below += number, color));
            }
            maximum = Math.Max(maximum, above);
            minimum = Math.Min(minimum, below);
        }

        XElement? valueAxis = plot.Element(C + "valAx");
        XElement? scaling = valueAxis?.Element(C + "scaling");
        (double axisMinimum, double axisMaximum, double step) = percent
            ? (minimum < 0 ? -1 : 0, maximum > 0 ? 1 : 0, 0.1)
            : AutoScale(minimum, maximum, Number(scaling?.Element(C + "min")), Number(scaling?.Element(C + "max")));
        if (!(axisMaximum - axisMinimum > 1e-100) || !(step > 0) || (axisMaximum - axisMinimum) / step > 1000)
            return null;

        double scale = Math.Min(2, 1024 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));
        bool titled = chart.Element(C + "title") is not null && Value(chart, "autoTitleDeleted") is not ("1" or "true");
        XElement? legendElement = chart.Element(C + "legend");
        string? legend = legendElement is null ? null : Value(legendElement, "legendPos") ?? "r";
        float left = pixelsWide * 0.073f;
        float right = pixelsWide * (legend == "r" ? 0.83f : 0.979f);
        // A legend placed by hand at the right takes the room from there on.
        if (legendElement?.Element(C + "layout")?.Element(C + "manualLayout") is XElement manual &&
            Number(manual.Element(C + "x")) is double legendX && legendX is > 0.5 and < 1)
        {
            right = Math.Min(right, pixelsWide * ((float)legendX - 0.02f));
            legend = null;
        }
        float top = pixelsHigh * (titled ? 0.176f : 0.04f) + (legend == "t" ? pixelsHigh * 0.09f : 0);
        float bottom = pixelsHigh * (legend == "b" ? 0.842f : 0.91f);
        float cluster = (right - left) / categories;
        int gap = int.TryParse(Value(bars, "gapWidth"), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedGap)
            ? Math.Clamp(parsedGap, 0, 500) : 150;
        float column = cluster / (1 + gap / 100f);
        float Y(double value) => bottom - (float)((Math.Clamp(value, axisMinimum, axisMaximum) - axisMinimum) / (axisMaximum - axisMinimum)) * (bottom - top);

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            DrawChartSpace(canvas, root, theme, pixelsWide, pixelsHigh);
            if (valueAxis?.Element(C + "majorGridlines") is XElement gridlines)
            {
                if (!TryLine(gridlines.Element(C + "spPr")?.Element(DocxNamespaces.Drawing + "ln"), theme, scale, out BColor grid, out float gridWidth))
                    (grid, gridWidth) = (new BColor(217, 217, 217), (float)Math.Max(1, scale));
                int intervals = (int)Math.Round((axisMaximum - axisMinimum) / step);
                for (int tick = 0; tick <= intervals; tick++)
                {
                    float y = Y(axisMinimum + step * tick);
                    canvas.DrawLine(new PointF(left, y), new PointF(right, y), grid, gridWidth);
                }
            }
            foreach ((int category, double from, double to, BColor color) in segments)
            {
                float x = left + (category + 0.5f) * cluster - column / 2;
                float y0 = Y(from), y1 = Y(to);
                canvas.FillRect(new RectangleF(x, Math.Min(y0, y1), column, Math.Abs(y1 - y0)), color);
            }
            if (TryLine(plot.Element(C + "catAx")?.Element(C + "spPr")?.Element(DocxNamespaces.Drawing + "ln"), theme, scale, out BColor axis, out float axisWidth))
            {
                float y = Y(Math.Clamp(0, axisMinimum, axisMaximum));
                canvas.DrawLine(new PointF(left, y), new PointF(right, y), axis, axisWidth);
            }
        }
        return DocxVmlPath.EncodePng(bitmap);
    }

    private static int ChartTypeCount(XElement plot) =>
        plot.Elements().Count(element => element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal));

    /// <summary>A series' cached values by point index, or null when the cache is missing or malformed.</summary>
    private static Dictionary<int, double>? ReadValues(XElement series)
    {
        XElement? values = series.Element(C + "val");
        XElement? cache = values?.Element(C + "numRef")?.Element(C + "numCache") ?? values?.Element(C + "numLit");
        if (cache is null)
            return null;
        var numbers = new Dictionary<int, double>();
        foreach (XElement point in cache.Elements(C + "pt"))
        {
            if (!int.TryParse((string?)point.Attribute("idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                index is < 0 or >= 256 ||
                !double.TryParse(point.Element(C + "v")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                !double.IsFinite(number) || Math.Abs(number) > 1e100 || !numbers.TryAdd(index, number))
                return null;
        }
        return numbers;
    }

    /// <summary>
    /// The colour Office gives the series or data point at <paramref name="index"/>
    /// when none is stated: the six theme accents, then the same six darker
    /// (60% luminance), then lighter (80% plus 20%).
    /// </summary>
    internal static BColor AccentColor(int index, DocxTheme theme)
    {
        string accent = "accent" + (index % 6 + 1);
        XElement scheme = new(DocxNamespaces.Drawing + "schemeClr", new XAttribute("val", accent));
        switch (index / 6 % 3)
        {
            case 1:
                scheme.Add(new XElement(DocxNamespaces.Drawing + "lumMod", new XAttribute("val", "60000")));
                break;
            case 2:
                scheme.Add(new XElement(DocxNamespaces.Drawing + "lumMod", new XAttribute("val", "80000")),
                    new XElement(DocxNamespaces.Drawing + "lumOff", new XAttribute("val", "20000")));
                break;
        }
        return DocxDrawingColor.TryRead(new XElement("fill", scheme), theme, out BColor color)
            ? color : SeriesColors[index % SeriesColors.Length];
    }

    /// <summary>The solid fill a <c>c:spPr</c> states; a <c>a:noFill</c> reads as an empty colour.</summary>
    private static bool TryFill(XElement? shape, DocxTheme theme, out BColor color)
    {
        color = BColor.Empty;
        if (shape is null)
            return false;
        if (shape.Element(DocxNamespaces.Drawing + "noFill") is not null)
            return true;
        return DocxDrawingColor.TryRead(shape.Element(DocxNamespaces.Drawing + "solidFill"), theme, out color);
    }

    /// <summary>A drawn <c>a:ln</c>: its solid colour and its width in pixels (0.75 pt when unstated).</summary>
    private static bool TryLine(XElement? line, DocxTheme theme, double scale, out BColor color, out float width)
    {
        color = BColor.Empty;
        width = 0;
        if (line is null || line.Element(DocxNamespaces.Drawing + "noFill") is not null ||
            !DocxDrawingColor.TryRead(line.Element(DocxNamespaces.Drawing + "solidFill"), theme, out color))
            return false;
        double emu = int.TryParse((string?)line.Attribute("w"), NumberStyles.None, CultureInfo.InvariantCulture, out int w)
            ? Math.Min(w, 20 * 12700) : 9525;
        width = (float)Math.Max(1, emu / 12700 * scale);
        return true;
    }

    /// <summary>The chart's background and border, from the chart space's <c>c:spPr</c>.</summary>
    private static void DrawChartSpace(BCanvas canvas, XElement root, DocxTheme theme, int pixelsWide, int pixelsHigh)
    {
        var bounds = new RectangleF(0.5f, 0.5f, pixelsWide - 1, pixelsHigh - 1);
        XElement? shape = root.Element(C + "spPr");
        if (!TryFill(shape, theme, out BColor background))
            background = new BColor(255, 255, 255);
        canvas.FillRect(bounds, background.IsEmpty ? new BColor(255, 255, 255) : background);
        XElement? line = shape?.Element(DocxNamespaces.Drawing + "ln");
        if (line is null)
            canvas.DrawRectangleStroke(bounds, new BColor(217, 217, 217), 1);
        else if (TryLine(line, theme, 1, out BColor border, out _))
            canvas.DrawRectangleStroke(bounds, border, 1);
    }

    /// <summary>Whether the data labels a series or chart group shows sit outside the pie.</summary>
    private static bool OutsideLabels(XElement owner) =>
        owner.Element(C + "dLbls") is XElement labels && Value(labels, "dLblPos") == "outEnd" &&
        (Value(labels, "showVal") is "1" or "true" || Value(labels, "showCatName") is "1" or "true" ||
         Value(labels, "showPercent") is "1" or "true" || Value(labels, "showSerName") is "1" or "true");

    /// <summary>
    /// The automatic value axis Excel and LibreOffice choose: it starts at zero
    /// unless every value lies within a sixth of the largest (then half the data
    /// range below the smallest), takes a twentieth of the range as headroom (2D only,
    /// <paramref name="expand"/>),
    /// and uses the finest 1-2-5 step that splits it into at most ten intervals.
    /// </summary>
    internal static (double Minimum, double Maximum, double Step) AutoScale(double minimum, double maximum,
        double? fixedMinimum = null, double? fixedMaximum = null, bool expand = true)
    {
        double range = maximum - minimum;
        double low = minimum > 0 ? (minimum * 6 >= maximum * 5 - 1e-9 * maximum ? Math.Max(0, minimum - range / 2) : 0) : minimum;
        double high = maximum < 0 ? (maximum * 6 <= minimum * 5 - 1e-9 * minimum ? Math.Min(0, maximum + range / 2) : 0) : maximum;
        // A 3D chart's axis is not widened: LibreOffice expands only a 2D
        // chart's automatic axis when the data come close to its ends.
        double headroom = expand ? (high - low) / 20 : 0;
        if (high > 0)
            high += headroom;
        if (low < 0)
            low -= headroom;
        low = fixedMinimum ?? low;
        high = fixedMaximum ?? high;
        if (!(high > low))
            return (0, 0, 0);

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10((high - low) / 10)));
        double step = magnitude;
        foreach (double factor in (ReadOnlySpan<double>)[1, 2, 5, 10, 20])
        {
            step = magnitude * factor;
            if (Math.Ceiling(high / step - 1e-9) - Math.Floor(low / step + 1e-9) <= 10)
                break;
        }
        return (fixedMinimum ?? Math.Floor(low / step + 1e-9) * step, fixedMaximum ?? Math.Ceiling(high / step - 1e-9) * step, step);
    }

    private static double? Number(XElement? element) =>
        double.TryParse((string?)element?.Attribute("val"), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
        double.IsFinite(value) ? value : null;

    private static string? Value(XElement element, string name) => (string?)element.Element(C + name)?.Attribute("val");
}
