using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>Flattens VML drawing groups into the existing point-based shape reader.</summary>
internal static class DocxVmlShapeWalker
{
    private sealed record Frame(double X, double Y, double ScaleX, double ScaleY, double OriginX, double OriginY,
        Dictionary<string, string> Style);

    public static IEnumerable<XElement> Shapes(XElement picture, int maxDepth, Action<string, string> report)
    {
        maxDepth = Math.Min(maxDepth, 16);
        XElement scope = picture.Ancestors().LastOrDefault() ?? picture;
        var types = scope.Descendants(DocxNamespaces.Vml + "shapetype")
            .Where(e => e.Attribute("id") is not null).GroupBy(e => (string)e.Attribute("id")!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return Walk(picture.Elements(), null, 0);

        IEnumerable<XElement> Walk(IEnumerable<XElement> elements, Frame? frame, int depth)
        {
            if (depth > maxDepth)
            {
                report("docx.shape.group", "A VML group exceeded the drawing depth limit and was skipped.");
                yield break;
            }
            foreach (XElement element in elements)
            {
                if (element.Name.Namespace != DocxNamespaces.Vml) continue;
                XElement shape = ResolveType(element, types, maxDepth, report);
                var style = DocxVml.ReadStyle((string?)shape.Attribute("style"));
                if (shape.Name.LocalName == "group")
                {
                    if (style.ContainsKey("rotation") || style.ContainsKey("flip"))
                    {
                        report("docx.shape.group", "A rotated or mirrored VML group was skipped; its transform cannot be represented.");
                        continue;
                    }
                    if (!Length(style, "width", out double width) || !Length(style, "height", out double height) || width <= 0 || height <= 0 ||
                        !Pair((string?)shape.Attribute("coordsize") ?? "1000,1000", out double cx, out double cy) || cx <= 0 || cy <= 0)
                    {
                        report("docx.shape.group", "A VML group had unusable dimensions and was skipped.");
                        continue;
                    }
                    if (!Pair((string?)shape.Attribute("coordorigin") ?? "0,0", out double ox, out double oy))
                    {
                        report("docx.shape.group", "A VML group had an invalid coordinate origin and was skipped.");
                        continue;
                    }
                    Length(style, "left", out double left); Length(style, "margin-left", out double ml);
                    Length(style, "top", out double top); Length(style, "margin-top", out double mt);
                    double x = frame is null ? left + ml : frame.X + (left + ml - frame.OriginX) * frame.ScaleX;
                    double y = frame is null ? top + mt : frame.Y + (top + mt - frame.OriginY) * frame.ScaleY;
                    var inherited = frame is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new(frame.Style, StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in style) inherited[pair.Key] = pair.Value;
                    var next = new Frame(x, y, width * (frame?.ScaleX ?? 1) / cx, height * (frame?.ScaleY ?? 1) / cy, ox, oy, inherited);
                    foreach (XElement child in Walk(shape.Elements(), next, depth + 1)) yield return child;
                    continue;
                }
                if (shape.Name.LocalName is not ("rect" or "roundrect" or "oval" or "shape" or "line" or "polyline")) continue;
                if (frame is not null)
                {
                    // Unitless child coordinates belong to the group's local space.
                    // Explicit units within a group are not silently scaled as local numbers.
                    if (new[] { "left", "top", "margin-left", "margin-top", "width", "height" }.Any(k => style.TryGetValue(k, out string? value) &&
                        !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) || shape.Name.LocalName is "line" or "polyline")
                    {
                        report("docx.shape.group", "A grouped VML object used unsupported physical-unit or line coordinates and was skipped.");
                        continue;
                    }
                    Length(style, "left", out double left); Length(style, "top", out double top);
                    Length(style, "margin-left", out double ml); Length(style, "margin-top", out double mt);
                    Length(style, "width", out double width); Length(style, "height", out double height);
                    style["left"] = Pt(frame.X + (left + ml - frame.OriginX) * frame.ScaleX);
                    style["top"] = Pt(frame.Y + (top + mt - frame.OriginY) * frame.ScaleY);
                    style["width"] = Pt(width * frame.ScaleX); style["height"] = Pt(height * frame.ScaleY);
                    style.Remove("margin-left"); style.Remove("margin-top");
                    foreach (string key in new[] { "mso-position-horizontal-relative", "mso-position-vertical-relative", "z-index" })
                        if (!style.ContainsKey(key) && frame.Style.TryGetValue(key, out string? value)) style[key] = value;
                    shape.SetAttributeValue("style", string.Join(";", style.Select(p => p.Key + ":" + p.Value)));
                }
                yield return shape;
            }
        }
    }

    private static XElement ResolveType(XElement source, Dictionary<string, XElement> types, int maxDepth, Action<string, string> report)
    {
        var chain = new List<XElement> { source };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        XElement current = source;
        while (((string?)current.Attribute("type")) is string id && id.StartsWith('#') && types.TryGetValue(id[1..], out XElement? parent))
        {
            if (!seen.Add(id) || chain.Count >= maxDepth)
            {
                report("docx.shape.type", "A cyclic or overly deep VML shape-type chain was cut off.");
                break;
            }
            chain.Add(parent); current = parent;
        }
        XElement result = new(source.Name);
        foreach (XElement element in chain.AsEnumerable().Reverse())
        {
            var style = DocxVml.ReadStyle((string?)result.Attribute("style"));
            foreach (XAttribute attr in element.Attributes())
                if (!attr.IsNamespaceDeclaration && (attr.Name.LocalName != "style" || ReferenceEquals(element, source)))
                    result.SetAttributeValue(attr.Name, attr.Value);
            if (ReferenceEquals(element, source))
                foreach (var pair in DocxVml.ReadStyle((string?)element.Attribute("style"))) style[pair.Key] = pair.Value;
            if (style.Count > 0) result.SetAttributeValue("style", string.Join(";", style.Select(p => p.Key + ":" + p.Value)));
            foreach (XElement child in element.Elements())
            {
                XElement? existing = result.Element(child.Name);
                if (existing is not null && child.Name.Namespace == DocxNamespaces.Vml && child.Name.LocalName is "fill" or "stroke")
                {
                    var merged = new XElement(existing);
                    foreach (var attr in child.Attributes()) merged.SetAttributeValue(attr.Name, attr.Value);
                    existing.ReplaceWith(merged);
                }
                else { existing?.Remove(); result.Add(new XElement(child)); }
            }
        }
        if (source.Name.LocalName == "shape" && (string?)result.Attribute(XName.Get("spt", "urn:schemas-microsoft-com:office:office")) == "3")
            result.Name = DocxNamespaces.Vml + "oval";
        return result;
    }

    private static string Pt(double value) => value.ToString("R", CultureInfo.InvariantCulture) + "pt";
    private static bool Length(Dictionary<string, string> style, string key, out double value) => DocxVml.TryLength(style.GetValueOrDefault(key), out value);
    private static bool Pair(string value, out double x, out double y)
    {
        string[] parts = value.Split(','); x = y = 0;
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && double.IsFinite(x) && double.IsFinite(y);
    }
}
