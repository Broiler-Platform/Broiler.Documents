using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Broiler.Documents.Ods;

/// <summary>
/// A cell style as Calc applies it: what the style states, over what its
/// parents state (<c>style:parent-style-name</c>, ending at <c>Default</c>),
/// and the cell defaults of Calc's pool (<c>style:default-style</c>) beneath
/// them all.
/// </summary>
/// <remarks>
/// A sheet's cells mostly name an automatic style that states a number format
/// and nothing else, or no style at all and the column's <c>Default</c>. Their
/// font is the one <c>Default</c> or the pool states: calc-data-source.ods
/// sets every cell in Calibri 11 pt through <c>Default</c>, where reading the
/// cell's own style alone left it in the reader's fallback.
/// </remarks>
internal sealed class OdsCellStyles
{
    private const int MaxDepth = 32;

    private readonly Dictionary<(string Family, string Name), XElement> _styles;
    private readonly Dictionary<string, string> _fontFaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement?> _resolved = new(StringComparer.Ordinal);
    private readonly XElement? _pool;

    public OdsCellStyles(XElement? shared, XElement? content, Dictionary<(string Family, string Name), XElement> styles)
    {
        _styles = styles;
        foreach (XElement? root in new[] { shared, content })
        {
            if (root is null)
                continue;
            foreach (XElement face in root.Elements(OdsNamespaces.Office + "font-face-decls").Elements(OdsNamespaces.Style + "font-face"))
            {
                if ((string?)face.Attribute(OdsNamespaces.Style + "name") is string name &&
                    (string?)face.Attribute(Svg + "font-family") is string family)
                    _fontFaces[name] = family.Trim().Trim('\'', '"');
            }
        }

        foreach (XElement group in shared?.Elements(OdsNamespaces.Office + "styles") ?? [])
        {
            foreach (XElement defaults in group.Elements(OdsNamespaces.Style + "default-style"))
            {
                if ((string?)defaults.Attribute(OdsNamespaces.Style + "family") == "table-cell")
                    _pool = defaults;
            }
        }

        XElement? text = _pool?.Element(OdsNamespaces.Style + "text-properties");
        DefaultFontFamily = (text is null ? null : FontFamily(text)) ?? "Liberation Sans";
        DefaultFontSize = OdsReader.TryParseLength((string?)text?.Attribute(OdsNamespaces.Fo + "font-size"), out double size) &&
            size is > 0 and <= 1638 ? (float)size : 10f;
    }

    private static XNamespace Svg => "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    /// <summary>The family Calc's pool sets cell text in: the default style's, else Liberation Sans.</summary>
    public string DefaultFontFamily { get; }

    /// <summary>The size Calc's pool sets cell text at: the default style's, else 10 pt (<c>ATTR_FONT_HEIGHT</c>).</summary>
    public float DefaultFontSize { get; }

    /// <summary>
    /// The style named <paramref name="name"/> with its parents' properties
    /// beneath its own, as one style with the named style's attributes; null for
    /// a name no cell style has. The pool's defaults are not folded in: they
    /// are the document's (<see cref="DefaultFontFamily"/>,
    /// <see cref="DefaultFontSize"/>).
    /// </summary>
    public XElement? Resolve(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (_resolved.TryGetValue(name, out XElement? cached))
            return cached;

        var chain = new List<XElement>();
        XElement? current = _styles.GetValueOrDefault(("table-cell", name));
        var seen = new HashSet<XElement>();
        while (current is not null && chain.Count < MaxDepth && seen.Add(current))
        {
            chain.Add(current);
            current = (string?)current.Attribute(OdsNamespaces.Style + "parent-style-name") is string parent
                ? _styles.GetValueOrDefault(("table-cell", parent))
                : null;
        }

        XElement? resolved = null;
        if (chain.Count > 0)
        {
            resolved = new XElement(chain[0].Name, chain[0].Attributes());
            foreach (string kind in new[] { "table-cell-properties", "paragraph-properties", "text-properties" })
            {
                var merged = new XElement(OdsNamespaces.Style + kind);
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    if (chain[i].Element(OdsNamespaces.Style + kind) is not XElement properties)
                        continue;
                    foreach (XAttribute attribute in properties.Attributes())
                        merged.SetAttributeValue(attribute.Name, attribute.Value);
                    // A face named by style:font-name replaces a family a parent stated.
                    if (kind == "text-properties" && FontFamily(properties) is string family)
                        merged.SetAttributeValue(OdsNamespaces.Fo + "font-family", family);
                }

                if (merged.HasAttributes)
                    resolved.Add(merged);
            }
        }

        _resolved[name] = resolved;
        return resolved;
    }

    /// <summary>
    /// Whether a resolved style fixes its cells' horizontal alignment. Calc
    /// writes <c>style:text-align-source="value-type"</c> for its standard
    /// alignment, which follows the cell's value, and states no
    /// <c>fo:text-align</c> at all where nothing set one.
    /// </summary>
    public static bool StatesAlignment(XElement? resolved) =>
        resolved?.Element(OdsNamespaces.Style + "paragraph-properties")?.Attribute(OdsNamespaces.Fo + "text-align") is not null &&
        (string?)resolved.Element(OdsNamespaces.Style + "table-cell-properties")?.Attribute(OdsNamespaces.Style + "text-align-source") != "value-type";

    /// <summary>
    /// The family a text-properties element states: its <c>fo:font-family</c>,
    /// or the family of the face its <c>style:font-name</c> names.
    /// </summary>
    private string? FontFamily(XElement text)
    {
        if ((string?)text.Attribute(OdsNamespaces.Fo + "font-family") is { Length: > 0 } family)
            return family.Trim().Trim('\'', '"');
        if ((string?)text.Attribute(OdsNamespaces.Style + "font-name") is { Length: > 0 } face)
            return _fontFaces.GetValueOrDefault(face) ?? face;
        return null;
    }
}
