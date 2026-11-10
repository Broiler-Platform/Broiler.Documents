using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Docx;

/// <summary>Renders the plot of a clustered column chart from its embedded numeric cache.</summary>
internal static class DocxChartPreview
{
    private static readonly XNamespace C = DocxNamespaces.DrawingChart;
    private static readonly BColor[] SeriesColors =
    [
        new(0x4F, 0x81, 0xBD), new(0xC0, 0x50, 0x4D), new(0x9B, 0xBB, 0x59),
        new(0x80, 0x64, 0xA2), new(0x4B, 0xAC, 0xC6), new(0xF7, 0x96, 0x46),
    ];

    public static byte[]? Render(XElement root, double width, double height)
    {
        XElement? chart = root.Element(C + "chart");
        XElement? plot = chart?.Element(C + "plotArea");
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

    private static string? Value(XElement element, string name) => (string?)element.Element(C + name)?.Attribute("val");
}
