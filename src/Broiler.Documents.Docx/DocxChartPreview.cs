using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Docx;

/// <summary>Renders the plot of a clustered column or an area chart from its embedded numeric cache.</summary>
internal static class DocxChartPreview
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
        XElement? bars = plot?.Element(C + "barChart");
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
    /// The automatic value axis Excel and LibreOffice choose: it starts at zero
    /// unless every value lies within a sixth of the largest (then half the data
    /// range below the smallest), takes a twentieth of the range as headroom,
    /// and uses the finest 1-2-5 step that splits it into at most ten intervals.
    /// </summary>
    internal static (double Minimum, double Maximum, double Step) AutoScale(double minimum, double maximum,
        double? fixedMinimum = null, double? fixedMaximum = null)
    {
        double range = maximum - minimum;
        double low = minimum > 0 ? (minimum * 6 >= maximum * 5 - 1e-9 * maximum ? Math.Max(0, minimum - range / 2) : 0) : minimum;
        double high = maximum < 0 ? (maximum * 6 <= minimum * 5 - 1e-9 * minimum ? Math.Min(0, maximum + range / 2) : 0) : maximum;
        double headroom = (high - low) / 20;
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
