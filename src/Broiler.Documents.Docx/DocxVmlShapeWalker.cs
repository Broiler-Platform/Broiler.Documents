using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>Flattens VML drawing groups into the existing point-based shape reader.</summary>
internal static class DocxVmlShapeWalker
{
    private sealed record Frame(double X, double Y, double ScaleX, double ScaleY, double OriginX, double OriginY,
        double Angle, Dictionary<string, string> Style)
    {
        public (double X, double Y) Map(double x, double y)
        {
            (double dx, double dy) = Rotate((x - OriginX) * ScaleX, (y - OriginY) * ScaleY, Angle);
            return (X + dx, Y + dy);
        }
    }

    private static (double X, double Y) Rotate(double x, double y, double angle) =>
        (x * Math.Cos(angle) - y * Math.Sin(angle), x * Math.Sin(angle) + y * Math.Cos(angle));

    internal static double Rotation(string? degrees) =>
        double.TryParse(degrees, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? (value % 360) * Math.PI / 180 : 0;

    public static IEnumerable<XElement> Shapes(XElement picture, PageGeometry? page, int maxDepth, Action<string, string> report)
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
                    double ownAngle = Rotation(style.GetValueOrDefault("rotation"));
                    if (style.ContainsKey("flip") ||
                        (frame is not null && Math.Abs(frame.ScaleX - frame.ScaleY) > 0.000001 && ownAngle != 0))
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
                    if (frame is null && !string.Equals(style.GetValueOrDefault("position"), "absolute", StringComparison.OrdinalIgnoreCase) &&
                        !new[] { "left", "top", "margin-left", "margin-top" }.Any(style.ContainsKey))
                    {
                        // An inline group (a drawing canvas, fdo46361) is drawn by
                        // LibreOffice shrunk to its children's bounds at the anchor.
                        var bounds = shape.Elements().Where(e => e.Name.Namespace == DocxNamespaces.Vml && e.Name.LocalName is not ("shapetype" or "fill" or "stroke" or "path" or "formulas"))
                            .Select(e => DocxVml.ReadStyle((string?)e.Attribute("style")))
                            .Select(s => (X: Unitless(s, "left") + Unitless(s, "margin-left"), Y: Unitless(s, "top") + Unitless(s, "margin-top")))
                            .ToList();
                        if (bounds.Count > 0)
                        {
                            ox = Math.Max(ox, bounds.Min(b => b.X));
                            oy = Math.Max(oy, bounds.Min(b => b.Y));
                        }
                    }
                    Length(style, "left", out double left); Length(style, "margin-left", out double ml);
                    Length(style, "top", out double top); Length(style, "margin-top", out double mt);
                    (double x, double y) = frame?.Map(left + ml, top + mt) ?? (left + ml, top + mt);
                    if (frame is null && page is not null)
                    {
                        // Apply group alignment once, against the group's full
                        // extent. Centering each child would collapse the group.
                        string? across = style.GetValueOrDefault("mso-position-horizontal-relative");
                        string? down = style.GetValueOrDefault("mso-position-vertical-relative");
                        x += Align(style.GetValueOrDefault("mso-position-horizontal"),
                            (across == "page" ? page.Width : page.ContentWidth) - width);
                        if (down is "page" or "margin")
                            y += Align(style.GetValueOrDefault("mso-position-vertical"),
                                (down == "page" ? page.Height : page.ContentHeight) - height);
                    }

                    var inherited = frame is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new(frame.Style, StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in style) inherited[pair.Key] = pair.Value;
                    double physicalWidth = width * (frame?.ScaleX ?? 1);
                    double physicalHeight = height * (frame?.ScaleY ?? 1);
                    double parentAngle = frame?.Angle ?? 0;
                    double angle = parentAngle + ownAngle;
                    (double centerX, double centerY) = Rotate(physicalWidth / 2, physicalHeight / 2, parentAngle);
                    (double cornerX, double cornerY) = Rotate(physicalWidth / 2, physicalHeight / 2, angle);
                    var next = new Frame(x + centerX - cornerX, y + centerY - cornerY,
                        physicalWidth / cx, physicalHeight / cy, ox, oy, angle, inherited);
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
                    (double x, double y) = frame.Map(left + ml + width / 2, top + mt + height / 2);
                    style["left"] = Pt(x - width * frame.ScaleX / 2);
                    style["top"] = Pt(y - height * frame.ScaleY / 2);
                    style["width"] = Pt(width * frame.ScaleX); style["height"] = Pt(height * frame.ScaleY);
                    double angle = frame.Angle + Rotation(style.GetValueOrDefault("rotation"));
                    if (angle != 0 || style.ContainsKey("rotation"))
                        style["rotation"] = (angle * 180 / Math.PI).ToString(CultureInfo.InvariantCulture);
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
            // A child overrides one inherited from a shape type, never a sibling:
            // a group's own v:rect children must all survive.
            var inherited = result.Elements().ToList();
            foreach (XElement child in element.Elements())
            {
                XElement? existing = inherited.FirstOrDefault(e => e.Parent is not null && e.Name == child.Name);
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

    private static double Align(string? position, double slack) => position switch
    {
        "center" => slack / 2,
        "right" or "bottom" => slack,
        _ => 0,
    };

    private static double Unitless(Dictionary<string, string> style, string key) =>
        double.TryParse(style.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : 0;
    private static string Pt(double value) => value.ToString("R", CultureInfo.InvariantCulture) + "pt";
    private static bool Length(Dictionary<string, string> style, string key, out double value) => DocxVml.TryLength(style.GetValueOrDefault(key), out value);
    private static bool Pair(string value, out double x, out double y)
    {
        string[] parts = value.Split(','); x = y = 0;
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && double.IsFinite(x) && double.IsFinite(y);
    }
}
