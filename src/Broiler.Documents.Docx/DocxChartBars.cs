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

/// <summary>
/// Clustered bar and column charts, flat (<c>c:barChart</c>, either direction)
/// and three-dimensional (<c>c:bar3DChart</c> columns), drawn from the series'
/// cached values the way LibreOffice lays them out: the plot area takes what
/// the title, the legend and the axis labels leave of the chart (or the
/// manual inner layout it states), and the legend keys are drawn. Text is
/// not drawn; its room is estimated from the label lengths and font sizes.
/// </summary>
internal static partial class DocxChartPreview
{
    private static readonly XNamespace A = DocxNamespaces.Drawing;
    private static readonly BColor DefaultLine = new(179, 179, 179);
    private static readonly BColor WallLine = new(134, 134, 134);

    private sealed record BarSeries(Dictionary<int, double> Values, BColor Color, Dictionary<int, BColor> Points, string Name);

    /// <summary>The plot geometry of a bar chart in points.</summary>
    internal readonly record struct BarLayout(double Left, double Top, double Right, double Bottom, double DepthX, double DepthY);

    private static byte[]? RenderBars(XElement root, XElement chart, XElement plot, XElement bars, double width, double height, DocxTheme theme)
    {
        bool threeD = bars.Name == C + "bar3DChart";
        bool horizontal = Value(bars, "barDir") == "bar";
        if (ChartTypeCount(plot) != 1 || plot.Descendants(C + "logBase").Any() || (threeD && horizontal) ||
            Value(bars, "grouping") is not (null or "clustered") ||
            plot.Elements().Any(axis => axis.Element(C + "scaling")?.Element(C + "orientation")?.Attribute("val")?.Value == "maxMin"))
            return null;

        var series = new List<BarSeries>();
        int categories = 0;
        double minimum = double.MaxValue, maximum = double.MinValue;
        foreach (XElement item in bars.Elements(C + "ser"))
        {
            if (series.Count == 16 || ReadValues(item) is not Dictionary<int, double> values)
                return null;
            foreach ((int index, double number) in values)
            {
                categories = Math.Max(categories, index + 1);
                minimum = Math.Min(minimum, number);
                maximum = Math.Max(maximum, number);
            }
            int order = int.TryParse(Value(item, "idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int seriesIndex)
                ? Math.Min(seriesIndex, 1000) : series.Count;
            if (!TryFill(item.Element(C + "spPr"), theme, out BColor color))
                color = AccentColor(order, theme);
            var points = new Dictionary<int, BColor>();
            foreach (XElement dataPoint in item.Elements(C + "dPt"))
                if (int.TryParse(Value(dataPoint, "idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                    TryFill(dataPoint.Element(C + "spPr"), theme, out BColor pointColor))
                    points.TryAdd(index, pointColor);
            series.Add(new BarSeries(values, color, points, SeriesName(item) ?? "Series " + (series.Count + 1).ToString(CultureInfo.InvariantCulture)));
        }
        List<Dictionary<int, string>> levels = CategoryLevels(bars.Element(C + "ser"));
        if (levels.Count > 0 && levels[0].Count > 0)
            categories = Math.Max(categories, levels[0].Keys.Max() + 1);
        if (series.Count == 0 || categories == 0 || categories > 256)
            return null;
        if (minimum > maximum)
            (minimum, maximum) = (0, 0);

        XElement? valueAxis = plot.Element(C + "valAx");
        XElement? scaling = valueAxis?.Element(C + "scaling");
        (double axisMinimum, double axisMaximum, double step) = AutoScale(minimum, maximum,
            Number(scaling?.Element(C + "min")), Number(scaling?.Element(C + "max")), expand: !threeD);
        if (Number(valueAxis?.Element(C + "majorUnit")) is double unit && unit > 0)
            step = unit;
        if (!(axisMaximum - axisMinimum > 1e-100) || !(step > 0) || (axisMaximum - axisMinimum) / step > 1000)
            return null;

        int gap = int.TryParse(Value(bars, "gapWidth"), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedGap)
            ? Math.Clamp(parsedGap, 0, 500) : 150;
        int overlap = int.TryParse(Value(bars, "overlap"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsedOverlap)
            ? Math.Clamp(parsedOverlap, -100, 100) : 0;
        List<string> names = series.Select(item => item.Name).ToList();
        BarLayout layout = LayoutBars(root, chart, plot, names, levels, categories, series.Count, horizontal, threeD,
            axisMinimum, axisMaximum, step, gap, width, height);
        // LibreOffice allows no more intervals than the axis has room for
        // labels (VCartesianAxis::estimateMaximumAutoMainIncrementCount).
        if (Number(valueAxis?.Element(C + "majorUnit")) is null && Shown(valueAxis))
        {
            double size = TextScale(width, height) * (TextSize(valueAxis!.Element(C + "txPr")) ?? TextSize(root.Element(C + "txPr")) ?? 10);
            double length = horizontal ? layout.Right - layout.Left : layout.Bottom - layout.Top;
            double needed = 1.2 * size;
            if (horizontal)
            {
                int ticks = (int)Math.Min(1000, Math.Round((axisMaximum - axisMinimum) / step));
                for (int tick = 0; tick <= ticks; tick++)
                    needed = Math.Max(needed, TextWidth(FormatTick(axisMinimum + step * tick), size));
            }
            int most = (int)Math.Clamp(Math.Floor(length / needed), 2, 10);
            if (most < 10)
            {
                (axisMinimum, axisMaximum, step) = AutoScale(minimum, maximum,
                    Number(scaling?.Element(C + "min")), Number(scaling?.Element(C + "max")), expand: !threeD, maxIntervals: most);
                if (!(axisMaximum - axisMinimum > 1e-100) || !(step > 0) || (axisMaximum - axisMinimum) / step > 1000)
                    return null;
                layout = LayoutBars(root, chart, plot, names, levels, categories, series.Count, horizontal, threeD,
                    axisMinimum, axisMaximum, step, gap, width, height);
            }
        }

        double scale = Math.Min(2, 1024 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));
        float left = (float)(layout.Left * scale), right = (float)(layout.Right * scale);
        float top = (float)(layout.Top * scale), bottom = (float)(layout.Bottom * scale);
        float depthX = (float)(layout.DepthX * scale), depthY = (float)(layout.DepthY * scale);
        if (!(right - left > 2) || !(bottom - top > 2))
            return null;

        // Values run along the plot's height for columns and its width for bars.
        float Along(double value)
        {
            double share = (Math.Clamp(value, axisMinimum, axisMaximum) - axisMinimum) / (axisMaximum - axisMinimum);
            return horizontal ? left + (float)share * (right - left) : bottom - (float)share * (bottom - top);
        }

        // LibreOffice's slot arithmetic (CategoryPositionHelper): each category
        // holds the series' slots, separated by the overlap and flanked by the gap.
        float category = (horizontal ? bottom - top : right - left) / categories;
        double inner = -overlap / 100.0, outer = gap / 100.0;
        float slot = (float)(category / (series.Count + outer + inner * (series.Count - 1)));
        float pitch = (float)(slot * (1 + inner));
        float cluster = slot + pitch * (series.Count - 1);
        float baseline = Along(Math.Clamp(0, axisMinimum, axisMaximum));

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            DrawChartSpace(canvas, root, theme, pixelsWide, pixelsHigh);
            if (TryFill(plot.Element(C + "spPr"), theme, out BColor plotFill) && !plotFill.IsEmpty && !threeD)
                canvas.FillRect(new RectangleF(left, top, right - left, bottom - top), plotFill);

            XElement? gridlines = valueAxis?.Element(C + "majorGridlines");
            if (!TryLine(gridlines?.Element(C + "spPr")?.Element(A + "ln"), theme, scale, out BColor grid, out float gridWidth))
                (grid, gridWidth) = (threeD ? WallLine : DefaultLine, (float)Math.Max(1, 0.75 * scale));
            bool hidden = gridlines?.Element(C + "spPr")?.Element(A + "ln")?.Element(A + "noFill") is not null;
            int intervals = (int)Math.Round((axisMaximum - axisMinimum) / step);
            if (threeD)
            {
                Draw3DWalls(canvas, left, top, right, bottom, depthX, depthY, intervals, step, axisMinimum, Along,
                    gridlines is not null && !hidden ? grid : BColor.Empty, gridWidth, (float)Math.Max(1, 0.75 * scale), scale);
            }
            else if (gridlines is not null && !hidden)
            {
                for (int tick = 0; tick <= intervals; tick++)
                {
                    float at = Along(axisMinimum + step * tick);
                    if (horizontal)
                        Rule(canvas, new PointF(at, top), new PointF(at, bottom), grid, gridWidth, scale);
                    else
                        Rule(canvas, new PointF(left, at), new PointF(right, at), grid, gridWidth, scale);
                }
            }

            string shape = Value(bars, "shape") ?? "box";
            double depthGap = int.TryParse(Value(bars, "gapDepth"), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedDepth)
                ? Math.Clamp(parsedDepth, 0, 500) / 100.0 : 1.5;
            for (int index = 0; index < categories; index++)
            {
                for (int i = 0; i < series.Count; i++)
                {
                    if (!series[i].Values.TryGetValue(index, out double number))
                        continue;
                    BColor color = series[i].Points.TryGetValue(index, out BColor pointColor) ? pointColor : series[i].Color;
                    if (color.IsEmpty)
                        continue;
                    float start = index * category + (category - cluster) / 2 + i * pitch;
                    float end = Along(number);
                    if (horizontal)
                    {
                        float y = bottom - start - slot;
                        canvas.FillRect(new RectangleF(Math.Min(baseline, end), y, Math.Abs(end - baseline), slot), color);
                    }
                    else if (threeD)
                    {
                        Draw3DColumn(canvas, left + start, slot, baseline, end, depthX, depthY, depthGap, shape, color);
                    }
                    else
                    {
                        canvas.FillRect(new RectangleF(left + start, Math.Min(baseline, end), slot, Math.Abs(end - baseline)), color);
                    }
                }
            }

            if (!threeD)
            {
                XElement? categoryAxis = plot.Element(C + "catAx") ?? plot.Element(C + "dateAx");
                if (AxisLine(categoryAxis, theme, scale, out BColor axis, out float axisWidth))
                {
                    if (horizontal)
                        Rule(canvas, new PointF(baseline, top), new PointF(baseline, bottom), axis, axisWidth, scale);
                    else
                        Rule(canvas, new PointF(left, baseline), new PointF(right, baseline), axis, axisWidth, scale);
                }
                if (AxisLine(valueAxis, theme, scale, out axis, out axisWidth))
                {
                    if (horizontal)
                        Rule(canvas, new PointF(left, bottom), new PointF(right, bottom), axis, axisWidth, scale);
                    else
                        Rule(canvas, new PointF(left, top), new PointF(left, bottom), axis, axisWidth, scale);
                }
            }
            DrawLegendKeys(canvas, root, chart, series, horizontal, width, height, scale);
        }
        return DocxVmlPath.EncodePng(bitmap);
    }

    /// <summary>
    /// Where LibreOffice puts the plot of a bar chart, in points: a manual inner
    /// layout as stated; otherwise the chart less a 2% margin, the title, the
    /// legend and the axis labels. A 3D plot keeps room at the right and top for
    /// its depth, which follows the scene's preferred aspect ratio
    /// (<c>BarChart::getPreferredDiagramAspectRatio</c>) seen under the view's
    /// rotation.
    /// </summary>
    internal static BarLayout LayoutBars(XElement root, XElement chart, XElement plot, IReadOnlyList<string> legend,
        IReadOnlyList<Dictionary<int, string>> levels, int categories, int seriesCount, bool horizontal, bool threeD,
        double axisMinimum, double axisMaximum, double step, int gap, double width, double height)
    {
        double textScale = TextScale(width, height);
        double baseSize = TextSize(root.Element(C + "txPr")) ?? 10;
        double marginX = width * 0.02, marginY = height * 0.02;
        double left = marginX, right = width - marginX, top = marginY, bottom = height - marginY;
        if (Titled(chart))
            top += 2.5 * textScale * (TextSize(chart.Element(C + "title")?.Element(C + "txPr")) ??
                TextSize(chart.Element(C + "title")?.Element(C + "tx")?.Element(C + "rich")) ?? 18);

        if (chart.Element(C + "legend") is XElement legendElement && Value(legendElement, "overlay") is not ("1" or "true"))
        {
            double size = textScale * (TextSize(legendElement.Element(C + "txPr")) ?? baseSize);
            XElement? manual = legendElement.Element(C + "layout")?.Element(C + "manualLayout");
            if (manual is not null && Number(manual.Element(C + "x")) is double legendX)
            {
                // A legend placed by hand at the right takes the room from there on.
                if (legendX is > 0.5 and < 1)
                    right = Math.Min(right, width * legendX - 0.02 * width);
            }
            else
            {
                double longest = legend.Select(entry => TextWidth(entry, size)).DefaultIfEmpty(0).Max();
                switch (Value(legendElement, "legendPos") ?? "r")
                {
                    case "r" or "tr":
                        right -= longest + 2.1 * size;
                        break;
                    case "l":
                        left += longest + 2.1 * size;
                        break;
                    case "t":
                        top += 2.35 * size;
                        break;
                    case "b":
                        bottom -= 2.35 * size;
                        break;
                }
            }
        }

        XElement? valueAxis = plot.Element(C + "valAx");
        XElement? categoryAxis = plot.Element(C + "catAx") ?? plot.Element(C + "dateAx");
        double valueSize = textScale * (TextSize(valueAxis?.Element(C + "txPr")) ?? baseSize);
        double categorySize = textScale * (TextSize(categoryAxis?.Element(C + "txPr")) ?? baseSize);
        XElement? manualPlot = plot.Element(C + "layout")?.Element(C + "manualLayout");
        bool edge = manualPlot is not null && Value(manualPlot, "xMode") == "edge" && Value(manualPlot, "yMode") == "edge" &&
            Number(manualPlot.Element(C + "x")) is double && Number(manualPlot.Element(C + "y")) is double &&
            Number(manualPlot.Element(C + "w")) is double && Number(manualPlot.Element(C + "h")) is double;
        if (edge)
        {
            double x = Number(manualPlot!.Element(C + "x"))!.Value, y = Number(manualPlot.Element(C + "y"))!.Value;
            double w = Number(manualPlot.Element(C + "w"))!.Value, h = Number(manualPlot.Element(C + "h"))!.Value;
            (left, top, right, bottom) = (width * x, height * y, width * (x + w), height * (y + h));
        }
        if (!edge || Value(manualPlot!, "layoutTarget") is not "inner")
        {
            double valueWidth = 0;
            if (Shown(valueAxis))
            {
                int intervals = (int)Math.Min(1000, Math.Round((axisMaximum - axisMinimum) / step));
                for (int tick = 0; tick <= intervals; tick++)
                    valueWidth = Math.Max(valueWidth, TextWidth(FormatTick(axisMinimum + step * tick), valueSize));
            }
            if (horizontal)
            {
                if (Shown(categoryAxis))
                {
                    double longest = levels.Count == 0 ? 0 : levels[0].Values.Select(text => TextWidth(text, categorySize)).DefaultIfEmpty(0).Max();
                    left += longest + LabelGap(categoryAxis) + (levels.Count - 1) * (1.2 * categorySize + 2.5);
                }
                if (Shown(valueAxis))
                    bottom -= 1.2 * valueSize + LabelGap(valueAxis);
            }
            else
            {
                if (Shown(valueAxis))
                    left += valueWidth + LabelGap(valueAxis);
                if (Shown(categoryAxis))
                {
                    double extra = LabelGap(categoryAxis) + Math.Max(0, levels.Count - 1) * (1.2 * categorySize + 2.5);
                    double angle = LabelRotation(categoryAxis!);
                    IEnumerable<string> texts = levels.Count == 0 ? [] : levels[0].Values;
                    if (Math.Abs(Math.Abs(angle) - 90) < 0.5)
                    {
                        // Upright labels break at about three fifths of the room below the title.
                        double room = 0.6 * (bottom - top);
                        double longest = texts.Select(text => Math.Min(TextWidth(text, categorySize), room)).DefaultIfEmpty(0).Max();
                        bottom -= longest + extra;
                    }
                    else if (angle != 0)
                    {
                        double radians = angle * Math.PI / 180;
                        double longest = texts.Select(text => TextWidth(text, categorySize)).DefaultIfEmpty(0).Max();
                        bottom -= longest * Math.Abs(Math.Sin(radians)) + 1.2 * categorySize * Math.Abs(Math.Cos(radians)) + extra;
                    }
                    else
                    {
                        // Category labels wrap at their category's width.
                        double room = (right - left) / Math.Max(1, categories) * 0.95;
                        int lines = texts.Select(text => WrappedLines(text, categorySize, room)).DefaultIfEmpty(1).Max();
                        bottom -= Math.Min(lines, 4) * 1.2 * categorySize + extra;
                    }
                }
            }
        }

        double depthX = 0, depthY = 0;
        if (threeD)
        {
            XElement? view = chart.Element(C + "view3D");
            double rotX = Math.Clamp(Number(view?.Element(C + "rotX")) ?? 15, -60, 60) * Math.PI / 180;
            double rotY = Math.Clamp(Number(view?.Element(C + "rotY")) ?? 20, 0, 360) * Math.PI / 180;
            double slot = 1 / (seriesCount + gap / 100.0);
            double depth = Math.Clamp(1 / (categories * (1 + (seriesCount - 1) * slot)), 0.05, 10) * 0.7;
            double sideways = Math.Min(Math.Abs(Math.Sin(rotY)) / Math.Max(0.2, Math.Abs(Math.Cos(rotY))), 1);
            double front = (right - left) / (1 + depth * sideways);
            depthX = front * depth * sideways;
            depthY = Math.Min(front * depth * Math.Abs(Math.Tan(rotX)), (bottom - top) * 0.3);
            right -= depthX;
            top += depthY;
        }
        return new BarLayout(left, top, right, bottom, depthX, depthY);
    }

    /// <summary>
    /// The walls and floor of a 3D column chart with right-angled axes: the back
    /// wall carries the value gridlines, the left wall their continuation to the
    /// front axis, and the floor's front and right edges close the box.
    /// </summary>
    private static void Draw3DWalls(BCanvas canvas, float left, float top, float right, float bottom, float depthX, float depthY,
        int intervals, double step, double axisMinimum, Func<double, float> along, BColor grid, float gridWidth, float lineWidth, double scale)
    {
        if (!grid.IsEmpty)
        {
            for (int tick = 0; tick <= intervals; tick++)
            {
                float y = along(axisMinimum + step * tick);
                Rule(canvas, new PointF(left + depthX, y - depthY), new PointF(right + depthX, y - depthY), grid, gridWidth, scale);
                Rule(canvas, new PointF(left, y), new PointF(left + depthX, y - depthY), grid, gridWidth, scale);
            }
        }
        Rule(canvas, new PointF(left + depthX, top - depthY), new PointF(left + depthX, bottom - depthY), WallLine, lineWidth, scale);
        Rule(canvas, new PointF(right + depthX, top - depthY), new PointF(right + depthX, bottom - depthY), WallLine, lineWidth, scale);
        Rule(canvas, new PointF(left, top), new PointF(left, bottom), WallLine, lineWidth, scale);
        Rule(canvas, new PointF(left, bottom), new PointF(right, bottom), WallLine, lineWidth, scale);
        Rule(canvas, new PointF(right, bottom), new PointF(right + depthX, bottom - depthY), WallLine, lineWidth, scale);
        Rule(canvas, new PointF(left, bottom), new PointF(left + depthX, bottom - depthY), WallLine, lineWidth, scale);
        Rule(canvas, new PointF(left + depthX, bottom - depthY), new PointF(right + depthX, bottom - depthY), WallLine, lineWidth, scale);
    }

    /// <summary>
    /// One 3D column, centred in the depth: a box shows its lit front, its top
    /// and its shaded right side; a cylinder (also a cone or pyramid, drawn as
    /// one) shows a rounded body and an elliptic top.
    /// </summary>
    private static void Draw3DColumn(BCanvas canvas, float x, float width, float baseline, float end,
        float depthX, float depthY, double depthGap, string shape, BColor color)
    {
        float share = (float)(1 / (1 + depthGap));
        float near = (1 - share) / 2, far = near + share;
        float top = Math.Min(baseline, end), bottom = Math.Max(baseline, end);
        if (shape == "box")
        {
            float fx = x + near * depthX, fy = -near * depthY, bx = x + far * depthX, by = -far * depthY;
            canvas.FillRect(new RectangleF(fx, top + fy, width, bottom - top), Shade(color, 1.1));
            canvas.FillGlyphContours([[new PointF(fx, top + fy), new PointF(fx + width, top + fy),
                new PointF(bx + width, top + by), new PointF(bx, top + by), new PointF(fx, top + fy)]], Shade(color, 0.96));
            canvas.FillGlyphContours([[new PointF(fx + width, top + fy), new PointF(bx + width, top + by),
                new PointF(bx + width, bottom + by), new PointF(fx + width, bottom + fy), new PointF(fx + width, top + fy)]], Shade(color, 0.875));
            return;
        }

        float cx = x + width / 2 + 0.5f * depthX, offset = -0.5f * depthY;
        float radiusX = width / 2, radiusY = Math.Max(1, width * 0.18f);
        canvas.FillGlyphContours([Ellipse(cx, bottom + offset, radiusX, radiusY, 0, Math.PI)], Shade(color, 1.05));
        int strips = Math.Max(1, (int)Math.Ceiling(width));
        for (int strip = 0; strip < strips; strip++)
        {
            float from = x + 0.5f * depthX + width * strip / strips, to = x + 0.5f * depthX + width * (strip + 1) / strips;
            double light = 0.97 + 0.18 * Math.Sin(Math.PI * (strip + 0.5) / strips);
            canvas.FillRect(new RectangleF(from, top + offset, to - from + 0.5f, bottom - top), Shade(color, light));
        }
        canvas.FillGlyphContours([Ellipse(cx, top + offset, radiusX, radiusY, 0, 2 * Math.PI)], Shade(color, 0.95));
    }

    /// <summary>
    /// Draws a line. A horizontal or vertical one is filled as whole pixels, at
    /// least a point wide in the picture, so that it survives the picture's
    /// scaling to the page instead of falling between two sampled rows.
    /// </summary>
    private static void Rule(BCanvas canvas, PointF from, PointF to, BColor color, float width, double scale)
    {
        if (from.X != to.X && from.Y != to.Y)
        {
            canvas.DrawLine(from, to, color, width);
            return;
        }
        float thickness = MathF.Max(1, MathF.Round(MathF.Max(width, (float)scale)));
        if (from.Y == to.Y)
            canvas.FillRect(new RectangleF(MathF.Min(from.X, to.X), MathF.Round(from.Y - thickness / 2), MathF.Abs(to.X - from.X), thickness), color);
        else
            canvas.FillRect(new RectangleF(MathF.Round(from.X - thickness / 2), MathF.Min(from.Y, to.Y), thickness, MathF.Abs(to.Y - from.Y)), color);
    }

    private static PointF[] Ellipse(float cx, float cy, float rx, float ry, double from, double to)
    {
        int steps = 48;
        var points = new PointF[steps + 2];
        for (int i = 0; i <= steps; i++)
        {
            double at = from + (to - from) * i / steps;
            points[i] = new PointF(cx + rx * (float)Math.Cos(at), cy + ry * (float)Math.Sin(at));
        }
        points[steps + 1] = points[0];
        return points;
    }

    private static BColor Shade(BColor color, double factor) =>
        BColor.FromArgb(255, Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.G * factor), 0, 255), Math.Clamp((int)Math.Round(color.B * factor), 0, 255));

    /// <summary>
    /// The legend's keys, without their text: a square per series beside where
    /// its name goes, stacked for a side legend and in a row for a top or bottom
    /// one. LibreOffice lists a horizontal bar chart's series from the last.
    /// </summary>
    private static void DrawLegendKeys(BCanvas canvas, XElement root, XElement chart, List<BarSeries> series, bool reversed,
        double width, double height, double scale)
    {
        if (chart.Element(C + "legend") is not XElement legend)
            return;
        double size = TextScale(width, height) * (TextSize(legend.Element(C + "txPr")) ?? TextSize(root.Element(C + "txPr")) ?? 10);
        double key = 0.7 * size;
        IEnumerable<BarSeries> entries = reversed ? Enumerable.Reverse(series) : series;
        var keys = new List<(double X, double Y, BColor Color)>();
        XElement? manual = legend.Element(C + "layout")?.Element(C + "manualLayout");
        string position = Value(legend, "legendPos") ?? "r";
        if (manual is not null && Number(manual.Element(C + "x")) is double x && Number(manual.Element(C + "y")) is double y)
        {
            double pitch = Number(manual.Element(C + "h")) is double h && h > 0 ? height * h / series.Count : 1.4 * size;
            double at = height * y + pitch / 2;
            foreach (BarSeries entry in entries)
            {
                keys.Add((width * x + 0.3 * size, at, entry.Color));
                at += pitch;
            }
        }
        else if (position is "t" or "b")
        {
            double total = entries.Sum(entry => key + 0.4 * size + TextWidth(entry.Name, size) + 0.8 * size);
            double at = (width - total) / 2;
            double row = position == "b" ? height * 0.98 - 0.45 * size - 0.5 * key : height * 0.02 + 0.45 * size;
            foreach (BarSeries entry in entries)
            {
                keys.Add((at, row, entry.Color));
                at += key + 0.4 * size + TextWidth(entry.Name, size) + 0.8 * size;
            }
        }
        else
        {
            double longest = series.Select(entry => TextWidth(entry.Name, size)).DefaultIfEmpty(0).Max();
            double at = height / 2 - 1.4 * size * (series.Count - 1) / 2 - key / 2;
            double keyX = position == "l" ? width * 0.02 + 1 : width * 0.98 - 1 - longest - 0.35 * size - key;
            foreach (BarSeries entry in entries)
            {
                keys.Add((keyX, at, entry.Color));
                at += 1.4 * size;
            }
        }
        foreach ((double keyX, double keyY, BColor color) in keys)
            if (!color.IsEmpty)
                canvas.FillRect(new RectangleF((float)(keyX * scale), (float)(keyY * scale), (float)(key * scale), (float)(key * scale)), color);
    }

    /// <summary>The line an axis draws: its <c>c:spPr</c> line, LibreOffice's grey when it states none.</summary>
    private static bool AxisLine(XElement? axis, DocxTheme theme, double scale, out BColor color, out float width)
    {
        color = DefaultLine;
        width = (float)Math.Max(1, 0.75 * scale);
        if (axis is null || Value(axis, "delete") is "1" or "true")
            return false;
        XElement? line = axis.Element(C + "spPr")?.Element(A + "ln");
        if (line is null)
            return true;
        if (TryLine(line, theme, scale, out color, out width))
            return true;
        // An a:ln that states only its width keeps the default colour.
        return line.Element(A + "noFill") is null && line.Element(A + "solidFill") is null;
    }

    /// <summary>The rotation an axis's text body states, in degrees (a:bodyPr rot, in 60000ths).</summary>
    private static double LabelRotation(XElement axis)
    {
        string? rotation = (string?)axis.Element(C + "txPr")?.Element(A + "bodyPr")?.Attribute("rot");
        // -60000000 is the "unset" value Office writes.
        return int.TryParse(rotation, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) &&
            value is >= -5400000 and <= 5400000 ? value / 60000.0 : 0;
    }

    private static bool Titled(XElement chart) =>
        chart.Element(C + "title") is not null && Value(chart, "autoTitleDeleted") is not ("1" or "true");

    private static bool Shown(XElement? axis) =>
        axis is not null && Value(axis, "delete") is not ("1" or "true") && Value(axis, "tickLblPos") is not "none";

    /// <summary>The distance from an axis to its labels: tick marks outside it add their length.</summary>
    private static double LabelGap(XElement? axis) =>
        2.25 + (axis is not null && (Value(axis, "majorTickMark") ?? "cross") is "out" or "cross" ? 3 : 0);

    /// <summary>
    /// How much LibreOffice scales a chart's text: its sizes are relative to a
    /// 16 cm by 9 cm page, and a chart drawn at another size scales them by the
    /// smaller of its two ratios (<c>RelativeSizeHelper::calculate</c>).
    /// </summary>
    internal static double TextScale(double width, double height) =>
        Math.Clamp(Math.Min(width / (16 / 2.54 * 72), height / (9 / 2.54 * 72)), 0.1, 10);

    /// <summary>The font size the first run properties of a text body state, in points.</summary>
    private static double? TextSize(XElement? body)
    {
        if (body is null)
            return null;
        foreach (XElement properties in body.Descendants().Where(element => element.Name == A + "defRPr" || element.Name == A + "rPr"))
            if (int.TryParse((string?)properties.Attribute("sz"), NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size >= 100)
                return Math.Min(size, 40000) / 100.0;
        return null;
    }

    /// <summary>
    /// An estimate of a label's width in points from Carlito's (Calibri's)
    /// average advances: digits half an em, capitals and lower case a little
    /// over and under it, spaces under a quarter.
    /// </summary>
    internal static double TextWidth(string text, double size)
    {
        double ems = 0;
        foreach (char character in text)
            ems += char.IsDigit(character) ? 0.507 : character == ' ' ? 0.226 : char.IsUpper(character) ? 0.55 :
                char.IsLetter(character) ? 0.43 : 0.3;
        return ems * size;
    }

    private static int WrappedLines(string text, double size, double room)
    {
        int lines = 1;
        double line = 0;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            double length = TextWidth(word, size);
            if (line > 0 && line + TextWidth(" ", size) + length > room)
            {
                lines++;
                line = length;
            }
            else
                line += (line > 0 ? TextWidth(" ", size) : 0) + length;
        }
        return lines;
    }

    private static string FormatTick(double value) =>
        Math.Round(value, 10).ToString("0.##########", CultureInfo.InvariantCulture);

    private static string? SeriesName(XElement series)
    {
        XElement? text = series.Element(C + "tx");
        return text?.Element(C + "strRef")?.Element(C + "strCache")?.Element(C + "pt")?.Element(C + "v")?.Value ??
            text?.Element(C + "v")?.Value;
    }

    /// <summary>A series' category labels by point index, one dictionary per level, the innermost first.</summary>
    private static List<Dictionary<int, string>> CategoryLevels(XElement? series)
    {
        var result = new List<Dictionary<int, string>>();
        XElement? categories = series?.Element(C + "cat");
        if (categories is null)
            return result;
        XElement? multi = categories.Element(C + "multiLvlStrRef")?.Element(C + "multiLvlStrCache") ?? categories.Element(C + "multiLvlStrLit");
        IEnumerable<XElement> caches = multi is not null
            ? multi.Elements(C + "lvl")
            : new[]
            {
                categories.Element(C + "strRef")?.Element(C + "strCache") ?? categories.Element(C + "numRef")?.Element(C + "numCache") ??
                categories.Element(C + "strLit") ?? categories.Element(C + "numLit"),
            }.OfType<XElement>();
        foreach (XElement cache in caches.Take(8))
        {
            var level = new Dictionary<int, string>();
            foreach (XElement point in cache.Elements(C + "pt"))
                if (int.TryParse((string?)point.Attribute("idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < 256)
                    level.TryAdd(index, point.Element(C + "v")?.Value ?? string.Empty);
            result.Add(level);
        }
        return result;
    }
}
