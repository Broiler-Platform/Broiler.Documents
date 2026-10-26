using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>Resolves a shape's theme format-matrix references before direct properties.</summary>
internal static class DocxShapeStyle
{
    private static bool IsFill(XElement element) => element.Name.Namespace == DocxNamespaces.Drawing &&
        element.Name.LocalName is "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill";

    public static XElement Resolve(XElement shape, DocxTheme theme)
    {
        XNamespace a = DocxNamespaces.Drawing;
        XElement result = new(shape.Element(DocxNamespaces.WordShape + "spPr") ?? new XElement(DocxNamespaces.WordShape + "spPr"));
        XElement? style = shape.Element(DocxNamespaces.WordShape + "style");
        XElement? fill = Entry(style?.Element(a + "fillRef"), false);
        if (!result.Elements().Any(IsFill) && fill is not null) result.Add(fill);
        XElement? line = Entry(style?.Element(a + "lnRef"), true);
        if (line is not null)
        {
            XElement? direct = result.Element(a + "ln");
            if (direct is not null)
            {
                foreach (XAttribute attribute in direct.Attributes()) line.SetAttributeValue(attribute.Name, attribute.Value);
                if (direct.Elements().Any(IsFill)) line.Elements().Where(IsFill).Remove();
                foreach (XElement property in direct.Elements())
                {
                    line.Elements(property.Name).Remove();
                    line.Add(new XElement(property));
                }
                direct.ReplaceWith(line);
            }
            else result.Add(line);
        }
        return result;

        XElement? Entry(XElement? reference, bool isLine)
        {
            if (!int.TryParse((string?)reference?.Attribute("idx"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                theme.FormatStyle(isLine, index) is not XElement template) return null;
            var resolved = new XElement(template);
            bool hasColor = DocxDrawingColor.TryRead(reference, theme, out BColor color);
            foreach (XElement placeholder in resolved.Descendants(a + "schemeClr").Where(e => (string?)e.Attribute("val") == "phClr").ToArray())
            {
                if (!hasColor) continue;
                placeholder.Name = a + "srgbClr";
                placeholder.SetAttributeValue("val", color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture) + color.B.ToString("X2", CultureInfo.InvariantCulture));
                // Reference transforms precede the matrix entry's transforms.
                if (color.A != 255) placeholder.AddFirst(new XElement(a + "alpha", new XAttribute("val", Math.Round(color.A / 255d * 100000))));
            }
            return resolved;
        }
    }
}
