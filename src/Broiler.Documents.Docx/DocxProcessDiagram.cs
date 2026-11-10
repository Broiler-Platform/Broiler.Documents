using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>Builds a Basic Timeline SmartArt diagram from its ordered process steps.</summary>
internal static class DocxProcessDiagram
{
    private static readonly XNamespace D = DocxDiagram.Diagram, S = DocxDiagram.Saved, A = DocxNamespaces.Drawing;
    private const string LayoutId = "urn:microsoft.com/office/officeart/2005/8/layout/hProcess11";

    public static bool Supports(XElement layout) =>
        layout.Name == D + "layoutDef" && (string?)layout.Attribute("uniqueId") == LayoutId;

    public static XElement? Build(XElement model, XElement? colors, XElement anchor, DocxTheme theme, int maxShapes)
    {
        XElement? points = model.Element(D + "ptLst");
        XElement? root = points?.Elements(D + "pt").FirstOrDefault(point => (string?)point.Attribute("type") == "doc");
        string? rootId = (string?)root?.Attribute("modelId");
        XElement? extent = anchor.Element(DocxNamespaces.WordDrawing + "extent");
        long width = Number(extent, "cx"), height = Number(extent, "cy");
        if (rootId is null || width <= 0 || height <= 0)
            return null;

        var dataPoints = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (XElement point in points!.Elements(D + "pt"))
        {
            if ((string?)point.Attribute("type") is null or "node" &&
                (string?)point.Attribute("modelId") is string id)
            {
                if (dataPoints.Count >= 256 || !dataPoints.TryAdd(id, point))
                    return null;
            }
        }

        var steps = new SortedDictionary<long, XElement>();
        foreach (XElement connection in model.Element(D + "cxnLst")?.Elements(D + "cxn") ?? [])
        {
            if ((string?)connection.Attribute("srcId") != rootId ||
                (string?)connection.Attribute("type") is not (null or "parOf"))
                continue;
            string? destination = (string?)connection.Attribute("destId");
            if (destination is null || !dataPoints.TryGetValue(destination, out XElement? point) ||
                !steps.TryAdd(Number(connection, "srcOrd"), point))
                return null;
        }
        if (steps.Count == 0 || 1 + 2 * steps.Count > maxShapes)
            return null;

        bool reversed = (string?)root?.Descendants(D + "dir").FirstOrDefault()?.Attribute("val") == "rev";
        BColor arrowColor = Palette(colors, "bgShp", theme).FirstOrDefault();
        BColor[] dotColors = Palette(colors, "node1", theme);
        if (dotColors.Length == 0)
            dotColors = [new BColor(79, 129, 189)];
        if (arrowColor.IsEmpty)
            arrowColor = new BColor(189, 211, 235);

        var tree = new XElement(S + "spTree");
        tree.Add(Shape("notchedRightArrow", 0, height * 0.3, width, height * 0.4, arrowColor, reversed));

        // The layout reserves 90% of the width for evenly spaced points, with
        // an inter-point gap of 5% of a point's width. Labels alternate around
        // the arrow and keep the original DrawingML text and run properties.
        double gapRatio = 0.05;
        double slot = width * 0.9 / (steps.Count + (steps.Count - 1) * gapRatio);
        double diameter = Math.Min(height * 0.1, slot);
        int index = 0;
        foreach (XElement step in steps.Values)
        {
            double x = index * slot * (1 + gapRatio);
            if (reversed)
                x = width - slot - x;
            double center = x + slot / 2;
            int colorIndex = steps.Count == 1 ? 0 : (int)Math.Round(index * (dotColors.Length - 1d) / (steps.Count - 1));
            tree.Add(Shape("ellipse", center - diameter / 2, (height - diameter) / 2, diameter, diameter, dotColors[colorIndex]));
            if (step.Element(D + "t") is XElement text)
            {
                bool above = index % 2 == 0;
                XElement label = Shape("rect", x, above ? 0 : height * 0.6, slot, height * 0.4, BColor.Empty);
                XElement body = new(S + "txBody", text.Elements());
                body.Element(A + "bodyPr")?.Remove();
                body.AddFirst(new XElement(A + "bodyPr", new XAttribute("anchor", above ? "b" : "t"),
                    new XAttribute("lIns", 0), new XAttribute("rIns", 0), new XAttribute("tIns", 0), new XAttribute("bIns", 0)));
                foreach (XElement paragraph in body.Elements(A + "p"))
                {
                    XElement? properties = paragraph.Element(A + "pPr");
                    if (properties is null)
                    {
                        properties = new XElement(A + "pPr");
                        paragraph.AddFirst(properties);
                    }
                    if (properties.Attribute("algn") is null)
                        properties.SetAttributeValue("algn", "ctr");
                }
                label.Add(body);
                tree.Add(label);
            }
            index++;
        }
        return tree;
    }

    private static BColor[] Palette(XElement? colors, string style, DocxTheme theme)
    {
        XElement? list = colors?.Elements(D + "styleLbl").FirstOrDefault(label => (string?)label.Attribute("name") == style)
            ?.Element(D + "fillClrLst");
        var palette = new List<BColor>();
        foreach (XElement entry in list?.Elements().Take(32) ?? [])
            if (DocxDrawingColor.TryRead(new XElement(A + "solidFill", entry), theme, out BColor color))
                palette.Add(color);
        return [.. palette];
    }

    private static XElement Shape(string geometry, double x, double y, double width, double height, BColor fill, bool flip = false)
    {
        var transform = new XElement(A + "xfrm",
            new XElement(A + "off", new XAttribute("x", Math.Round(x)), new XAttribute("y", Math.Round(y))),
            new XElement(A + "ext", new XAttribute("cx", Math.Round(width)), new XAttribute("cy", Math.Round(height))));
        if (flip)
            transform.SetAttributeValue("flipH", "1");
        return new XElement(S + "sp", new XElement(S + "spPr", transform,
            new XElement(A + "prstGeom", new XAttribute("prst", geometry)),
            fill.IsEmpty ? new XElement(A + "noFill") : new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", $"{fill.R:X2}{fill.G:X2}{fill.B:X2}"))),
            new XElement(A + "ln", new XElement(A + "noFill"))));
    }

    private static long Number(XElement? element, string attribute) =>
        long.TryParse((string?)element?.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : 0;
}
