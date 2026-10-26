using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>Imports saved SmartArt drawing caches, without running a diagram layout engine.</summary>
internal static class DocxDiagram
{
    internal static readonly XNamespace Diagram = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    internal static readonly XNamespace Saved = "http://schemas.microsoft.com/office/drawing/2008/diagram";
    private const string DataRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    private const string DrawingRelationship = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";
    private static readonly XNamespace A = DocxNamespaces.Drawing, W = DocxNamespaces.Wordprocessing;

    internal sealed record Cache(XElement Group, DocxRelationships Relationships);
    internal sealed class ReadState
    {
        public Dictionary<string, (XElement? Root, long Length)> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (DocxRelationships Value, long Length)> Relationships { get; } = new(StringComparer.OrdinalIgnoreCase);
        public long ExpandedBytes;
    }

    public static Cache? Load(ZipArchive archive, XElement references, XElement anchor,
        DocxRelationships owner, DocxDocumentBuilder builder, DocxTheme theme, ReadState state)
    {
        void Report(string code, string message) => builder.AddDiagnosticOnce(code, message);
        bool Reserve(long length)
        {
            if (length > builder.Limits.MaxDocumentBytes - state.ExpandedBytes)
            {
                Report("docx.diagram.limit", "Saved diagram references exceeded the document XML expansion budget; later cached content was skipped.");
                return false;
            }
            state.ExpandedBytes += length;
            return true;
        }
        DocxRelationships RelationshipsFor(string partPath)
        {
            string path = DocxPackage.RelationshipsPartPath(partPath);
            bool cached = state.Relationships.TryGetValue(path, out var part);
            long length = cached ? part.Length : DocxPackage.FindEntry(archive, path)?.Length ?? 0;
            if (!Reserve(length)) return DocxRelationships.Empty;
            if (cached) return part.Value;
            DocxRelationships value = DocxPackage.ReadRelationships(archive, path,
                DocxPackage.BasePartDirectory(partPath), builder.Limits, builder.Diagnostics);
            state.Relationships[path] = (value, length);
            return value;
        }
        XElement? LoadPart(DocxRelationship? relationship, string type)
        {
            if (relationship is null || relationship.Type != type || relationship.TargetModeExternal)
                return null;
            bool cached = state.Parts.TryGetValue(relationship.Target, out var part);
            ZipArchiveEntry? entry = cached ? null : DocxPackage.FindEntry(archive, relationship.Target);
            long length = cached ? part.Length : entry?.Length ?? 0;
            // Reusing one large cache many times must not evade the document's
            // overall expansion budget. Count each reference, but parse a part once.
            if (!Reserve(length)) return null;
            if (cached) return part.Root;
            XElement? root = entry is null ? null : DocxPackage.LoadEntryXml(entry, builder.Limits, builder.Diagnostics, "docx.diagram.xml")?.Root;
            state.Parts[relationship.Target] = (root, length);
            return root;
        }
        string? id = (string?)references.Attribute(DocxNamespaces.Relationships + "dm");
        DocxRelationship? data = null;
        if (id is not null) owner.TryGet(id, out data);
        XElement? model = LoadPart(data, DataRelationship);
        string? cacheId = (string?)model?.Descendants(Saved + "dataModelExt").FirstOrDefault()?.Attribute("relId");
        DocxRelationship? drawing = null;
        if (cacheId is not null) owner.TryGet(cacheId, out drawing);
        if ((drawing is null || drawing.Type != DrawingRelationship) && data is { TargetModeExternal: false } && cacheId is not null)
        {
            DocxRelationships dataRelationships = RelationshipsFor(data.Target);
            dataRelationships.TryGet(cacheId, out drawing);
        }
        XElement? tree = LoadPart(drawing, DrawingRelationship)?.Element(Saved + "spTree");
        if (tree is null)
        {
            Report("docx.diagram.cache", "A SmartArt diagram had no readable embedded drawing cache; its layout was not synthesized.");
            return null;
        }
        DocxRelationships relationships = RelationshipsFor(drawing!.Target);
        int count = 0;
        XElement group = Group(tree, true, 0);
        return new Cache(group, relationships);

        XElement Group(XElement source, bool root, int depth)
        {
            XNamespace groupNamespace = DocxNamespaces.WordGroup;
            var result = new XElement(groupNamespace + (root ? "wgp" : "grpSp"));
            XElement? properties = source.Element(Saved + "grpSpPr");
            var mapped = new XElement(groupNamespace + "grpSpPr", properties?.Attributes(), properties?.Nodes());
            if (root && mapped.Element(A + "xfrm") is null)
            {
                XElement? extent = anchor.Element(DocxNamespaces.WordDrawing + "extent");
                string cx = (string?)extent?.Attribute("cx") ?? "0", cy = (string?)extent?.Attribute("cy") ?? "0";
                mapped.Add(new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                    new XElement(A + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(A + "chExt", new XAttribute("cx", cx), new XAttribute("cy", cy))));
            }
            if (mapped.Element(A + "xfrm") is XElement groupTransform &&
                (Number(groupTransform, "rot") != 0 || On(groupTransform, "flipH") || On(groupTransform, "flipV")))
                Report("docx.diagram.transform", "A saved diagram group transform was approximated without group rotation or reflection.");
            result.Add(mapped);
            if (depth >= Math.Min(16, builder.Limits.MaxGroupDepth))
            {
                Report("docx.diagram.limit", "A saved diagram exceeded the supported group depth; deeper children were skipped.");
                return result;
            }
            foreach (XElement child in source.Elements())
            {
                if (child.Name == Saved + "grpSp") result.Add(Group(child, false, depth + 1));
                else if (child.Name == Saved + "sp")
                {
                    if (++count > builder.Limits.MaxParagraphCount)
                    {
                        Report("docx.diagram.limit", "A saved diagram exceeded the supported object count; later children were skipped."); break;
                    }
                    result.Add(Shape(child));
                }
                else if (child.Name != Saved + "grpSpPr" && child.Name != Saved + "nvGrpSpPr")
                    Report("docx.diagram.object", "An unsupported saved diagram object was skipped; supported shapes and text were retained.");
            }
            return result;
        }
        XElement Shape(XElement source)
        {
            XNamespace shapeNamespace = DocxNamespaces.WordShape;
            var result = new XElement(shapeNamespace + "wsp");
            var properties = new XElement(shapeNamespace + "spPr", source.Element(Saved + "spPr")?.Attributes(), source.Element(Saved + "spPr")?.Nodes());
            string? preset = (string?)properties.Element(A + "prstGeom")?.Attribute("prst");
            if (preset is not (null or "rect" or "ellipse" or "line" or "straightConnector1"))
                Report("docx.diagram.geometry", "A saved diagram's unsupported preset geometry was approximated by a rectangle.");
            if (properties.Element(A + "custGeom") is not null || source.Element(Saved + "txXfrm") is not null)
                Report("docx.diagram.geometry", "Saved diagram custom geometry or a separate text transform was approximated by the shape's text frame.");
            if (properties.Element(A + "xfrm") is XElement transform && Number(transform, "rot") != 0)
            {
                long rotation = Number(transform, "rot");
                if (rotation % 5400000 == 0 && preset is not ("line" or "straightConnector1"))
                {
                    long quarter = ((rotation / 5400000) % 4 + 4) % 4;
                    if (quarter is 1 or 3 && transform.Element(A + "off") is XElement off && transform.Element(A + "ext") is XElement ext)
                    {
                        double x = Number(off, "x"), y = Number(off, "y"), w = Number(ext, "cx"), h = Number(ext, "cy");
                        off.SetAttributeValue("x", Math.Round(x + (w - h) / 2)); off.SetAttributeValue("y", Math.Round(y + (h - w) / 2));
                        ext.SetAttributeValue("cx", h); ext.SetAttributeValue("cy", w);
                    }
                    transform.SetAttributeValue("rot", null);
                }
                Report("docx.diagram.transform", "A saved diagram's rotated shape was approximated by an axis-aligned box; text rotation is not retained.");
            }
            result.Add(properties);
            XElement? style = source.Element(Saved + "style");
            if (style is not null) result.Add(new XElement(shapeNamespace + "style", style.Attributes(), style.Nodes()));
            XElement? text = source.Element(Saved + "txBody");
            if (text is not null)
            {
                result.Add(new XElement(shapeNamespace + "txbx", new XElement(W + "txbxContent", Paragraphs(text, style, builder, theme))));
                XElement? body = text.Element(A + "bodyPr");
                if (body is not null) result.Add(new XElement(shapeNamespace + "bodyPr", body.Attributes(), body.Nodes()));
            }
            return result;
        }
    }

    private static bool On(XElement element, string attribute) => (string?)element.Attribute(attribute) is "1" or "true";
    private static long Number(XElement? element, string attribute) => long.TryParse((string?)element?.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

    private static XElement Merge(params XElement?[] parts)
    {
        var result = new XElement("properties");
        foreach (XElement? part in parts)
        {
            if (part is null) continue;
            foreach (XAttribute attribute in part.Attributes()) result.SetAttributeValue(attribute.Name, attribute.Value);
            foreach (XElement child in part.Elements())
            {
                result.Elements(child.Name).Remove(); result.Add(new XElement(child));
            }
        }
        return result;
    }

    private static IEnumerable<XElement> Paragraphs(XElement body, XElement? style, DocxDocumentBuilder builder, DocxTheme theme)
    {
        XElement? listStyle = body.Element(A + "lstStyle");
        foreach (XElement paragraph in body.Elements(A + "p").Take(builder.Limits.MaxParagraphCount))
        {
            XElement? own = paragraph.Element(A + "pPr");
            int level = (int)Math.Clamp(Number(own, "lvl"), 0, 8);
            XElement properties = Merge(listStyle?.Element(A + "defPPr"), listStyle?.Element(A + ("lvl" + (level + 1) + "pPr")), own);
            XElement defaults = Merge(listStyle?.Element(A + "defPPr")?.Element(A + "defRPr"),
                listStyle?.Element(A + ("lvl" + (level + 1) + "pPr"))?.Element(A + "defRPr"), own?.Element(A + "defRPr"));
            XElement spacingDefaults = new(defaults);
            if (spacingDefaults.Attribute("sz") is null)
            {
                XAttribute? firstSize = paragraph.Elements(A + "r").Select(r => r.Element(A + "rPr")?.Attribute("sz")).FirstOrDefault(a => a is not null)
                    ?? paragraph.Element(A + "endParaRPr")?.Attribute("sz");
                if (firstSize is not null) spacingDefaults.SetAttributeValue("sz", firstSize.Value);
            }
            var p = new XElement(W + "p", ParagraphProperties(properties, spacingDefaults));
            foreach (XElement run in paragraph.Elements())
            {
                if (run.Name == A + "r" || run.Name == A + "fld" || run.Name == A + "br")
                {
                    XElement formatting = RunProperties(Merge(defaults, run.Element(A + "rPr")), style, builder, theme);
                    var output = new XElement(W + "r", formatting);
                    if (run.Name == A + "br") output.Add(new XElement(W + "br"));
                    else output.Add(new XElement(W + "t", new XAttribute(DocxNamespaces.Xml + "space", "preserve"), (string?)run.Element(A + "t") ?? ""));
                    p.Add(output);
                }
            }
            if (properties.Element(A + "buChar") is not null || properties.Element(A + "buAutoNum") is not null)
                builder.AddDiagnosticOnce("docx.diagram.list", "Saved diagram list markers are not synthesized; their text and indentation were retained.");
            yield return p;
        }
    }

    private static XElement ParagraphProperties(XElement source, XElement defaults)
    {
        var result = new XElement(W + "pPr");
        string? alignment = (string?)source.Attribute("algn");
        if (alignment is not null) result.Add(new XElement(W + "jc", new XAttribute(W + "val", alignment switch { "ctr" => "center", "r" => "right", "just" => "both", "dist" => "distribute", _ => "left" })));
        var indent = new XElement(W + "ind");
        foreach ((string from, string to) in new[] { ("marL", "left"), ("marR", "right"), ("indent", "firstLine") })
            if (source.Attribute(from) is not null) indent.SetAttributeValue(W + to, Math.Round(Number(source, from) / 635d));
        if (indent.HasAttributes) result.Add(indent);
        var spacing = new XElement(W + "spacing");
        XElement? line = source.Element(A + "lnSpc");
        if (line?.Element(A + "spcPts") is XElement points)
        {
            spacing.SetAttributeValue(W + "line", Math.Round(Number(points, "val") / 5d)); spacing.SetAttributeValue(W + "lineRule", "exact");
        }
        else if (line?.Element(A + "spcPct") is XElement percent)
        {
            spacing.SetAttributeValue(W + "line", Math.Round(Number(percent, "val") * 240d / 100000d)); spacing.SetAttributeValue(W + "lineRule", "auto");
        }
        foreach ((string from, string to) in new[] { ("spcBef", "before"), ("spcAft", "after") })
        {
            XElement? item = source.Element(A + from);
            if (item?.Element(A + "spcPts") is XElement pts) spacing.SetAttributeValue(W + to, Math.Round(Number(pts, "val") / 5d));
            else if (item?.Element(A + "spcPct") is XElement pct && Number(defaults, "sz") > 0)
                spacing.SetAttributeValue(W + to, Math.Round(Number(pct, "val") * (Number(defaults, "sz") / 100d) * 20 / 100000d));
        }
        if (spacing.HasAttributes) result.Add(spacing);
        return result;
    }

    private static XElement RunProperties(XElement source, XElement? style, DocxDocumentBuilder builder, DocxTheme theme)
    {
        var result = new XElement(W + "rPr");
        foreach ((string from, string to) in new[] { ("b", "b"), ("i", "i") })
            if (source.Attribute(from) is not null) result.Add(new XElement(W + to, new XAttribute(W + "val", On(source, from) ? "1" : "0")));
        if (source.Attribute("u") is XAttribute underline) result.Add(new XElement(W + "u", new XAttribute(W + "val", underline.Value == "none" ? "none" : "single")));
        if (source.Attribute("strike") is XAttribute strike) result.Add(new XElement(W + "strike", new XAttribute(W + "val", strike.Value == "noStrike" ? "0" : "1")));
        long size = Number(source, "sz");
        if (size > 0) result.Add(new XElement(W + "sz", new XAttribute(W + "val", Math.Clamp(Math.Round(size / 50d), 1, 3276))));
        string? family = (string?)source.Element(A + "latin")?.Attribute("typeface");
        string? fontTheme = family switch { "+mj-lt" => "majorHAnsi", "+mn-lt" => "minorHAnsi", _ => null };
        if (string.IsNullOrWhiteSpace(family) && (string?)style?.Element(A + "fontRef")?.Attribute("idx") is string font)
            fontTheme = font == "major" ? "majorHAnsi" : font == "minor" ? "minorHAnsi" : null;
        if (fontTheme is not null) result.Add(new XElement(W + "rFonts", new XAttribute(W + "asciiTheme", fontTheme)));
        else if (!string.IsNullOrWhiteSpace(family)) result.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", family)));
        XElement? paint = source.Element(A + "solidFill") ?? style?.Element(A + "fontRef");
        if (DocxDrawingColor.TryRead(paint, theme, out var color))
        {
            result.Add(new XElement(W + "color", new XAttribute(W + "val", $"{color.R:X2}{color.G:X2}{color.B:X2}")));
            if (color.A != 255) builder.AddDiagnosticOnce("docx.diagram.text", "Saved diagram text opacity was flattened to an opaque text color.");
        }
        return result;
    }
}
