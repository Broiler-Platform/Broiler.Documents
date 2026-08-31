using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Odt;

/// <summary>
/// The style table an ODF text document resolves against: the named styles from
/// <c>styles.xml</c>, the automatic styles from both <c>styles.xml</c> and
/// <c>content.xml</c>, the per-family default styles, the font-face
/// declarations, and the list styles.
/// </summary>
/// <remarks>
/// <para>
/// ODF puts almost nothing inline. A bold word is a <c>text:span</c> naming an
/// automatic style <c>T1</c> that lives in <c>office:automatic-styles</c>, and a
/// heading is a <c>text:h</c> naming <c>Heading_20_1</c> in <c>styles.xml</c>. A
/// reader that ignored this table would render every ODF document as
/// undifferentiated body text, and unlike DOCX there is no direct-formatting
/// fallback to catch it.
/// </para>
/// <para>
/// Resolution follows ODF 1.3 section 16.2: the family default style first, then
/// the <c>style:parent-style-name</c> chain from its root down to the style the
/// content names. Automatic styles inherit from named ones through the same
/// attribute, so one chain walk covers both.
/// </para>
/// </remarks>
internal sealed class OdtStyles
{
    /// <summary>The two style families this codec resolves.</summary>
    public const string ParagraphFamily = "paragraph";

    public const string TextFamily = "text";

    private readonly Dictionary<StyleKey, StyleDefinition> _styles;
    private readonly Dictionary<string, DefaultStyle> _defaults;
    private readonly Dictionary<string, string> _fontFaces;
    private readonly Dictionary<string, ListStyle> _listStyles;
    private readonly int _maxChainDepth;
    private readonly List<DocumentDiagnostic> _diagnostics;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement[]> _chainCache = new(StringComparer.Ordinal);

    private OdtStyles(
        Dictionary<StyleKey, StyleDefinition> styles,
        Dictionary<string, DefaultStyle> defaults,
        Dictionary<string, string> fontFaces,
        Dictionary<string, ListStyle> listStyles,
        int maxChainDepth,
        List<DocumentDiagnostic> diagnostics)
    {
        _styles = styles;
        _defaults = defaults;
        _fontFaces = fontFaces;
        _listStyles = listStyles;
        _maxChainDepth = maxChainDepth;
        _diagnostics = diagnostics;
    }

    /// <summary>The number of paragraph and text styles that were loaded.</summary>
    public int Count => _styles.Count;

    /// <summary>The number of list styles that were loaded.</summary>
    public int ListStyleCount => _listStyles.Count;

    /// <summary>
    /// The <c>office:master-styles</c> element from <c>styles.xml</c>, where ODF
    /// keeps headers and footers - they hang off a master page rather than living
    /// in the content, so the reader needs the element itself rather than the
    /// style properties collected from it.
    /// </summary>
    public XElement? MasterStyles { get; init; }

    /// <summary>
    /// The <c>style:page-layout</c> elements from <c>styles.xml</c>. A master page
    /// names one, and that is where ODF states the paper size and its margins.
    /// </summary>
    public IReadOnlyList<XElement> PageLayouts { get; init; } = [];

    /// <summary>
    /// The <c>style:graphic-properties</c> of every graphic style, by name. A
    /// shape names one, and that is where ODF states how the box is painted.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> GraphicProperties { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>draw:gradient</c> elements by name. A graphic style refers to one
    /// rather than carrying the stops itself.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> Gradients { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// Builds the table from the package. <paramref name="content"/> is harvested
    /// after <c>styles.xml</c> so a content automatic style wins a name
    /// collision: it is the one the body actually refers to.
    /// </summary>
    public static OdtStyles Load(
        ZipArchive archive,
        XDocument content,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        var styles = new Dictionary<StyleKey, StyleDefinition>();
        var defaults = new Dictionary<string, DefaultStyle>(StringComparer.Ordinal);
        var fontFaces = new Dictionary<string, string>(StringComparer.Ordinal);
        var listStyles = new Dictionary<string, ListStyle>(StringComparer.Ordinal);
        var graphicProperties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var gradients = new Dictionary<string, XElement>(StringComparer.Ordinal);
        XElement? masterStyles = null;
        List<XElement> pageLayouts = [];

        ZipArchiveEntry? stylesEntry = OdtPackage.FindEntry(archive, OdtNamespaces.StylesPart);
        if (stylesEntry is not null)
        {
            XDocument? stylesXml = OdtPackage.LoadEntryXml(stylesEntry, limits, diagnostics, "odt.styles");
            if (stylesXml?.Root is not null)
            {
                Collect(stylesXml.Root, styles, defaults, fontFaces, listStyles, graphicProperties, gradients);
                masterStyles = stylesXml.Root.Element(OdtNamespaces.Office + "master-styles");
                pageLayouts = stylesXml.Root
                    .Descendants(OdtNamespaces.Style + "page-layout")
                    .ToList();
            }
        }

        if (content.Root is not null)
            Collect(content.Root, styles, defaults, fontFaces, listStyles, graphicProperties, gradients);

        return new OdtStyles(styles, defaults, fontFaces, listStyles, limits.MaxGroupDepth, diagnostics)
        {
            MasterStyles = masterStyles,
            PageLayouts = pageLayouts,
            GraphicProperties = graphicProperties,
            Gradients = gradients,
        };
    }

    /// <summary>
    /// Harvests one document root. <c>office:styles</c>,
    /// <c>office:automatic-styles</c>, and <c>office:master-styles</c> all hold
    /// the same element shapes, so all three are read the same way.
    /// </summary>
    private static void Collect(
        XElement root,
        Dictionary<StyleKey, StyleDefinition> styles,
        Dictionary<string, DefaultStyle> defaults,
        Dictionary<string, string> fontFaces,
        Dictionary<string, ListStyle> listStyles,
        Dictionary<string, XElement> graphicProperties,
        Dictionary<string, XElement> gradients)
    {
        foreach (XElement fontFace in root.Descendants(OdtNamespaces.Style + "font-face"))
        {
            string? name = (string?)fontFace.Attribute(OdtNamespaces.Style + "name");
            string? family = (string?)fontFace.Attribute(OdtNamespaces.Svg + "font-family");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(family))
                fontFaces[name] = family;
        }

        foreach (XElement style in root.Descendants(OdtNamespaces.Style + "style"))
        {
            string? name = (string?)style.Attribute(OdtNamespaces.Style + "name");
            string? family = (string?)style.Attribute(OdtNamespaces.Style + "family");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(family))
                continue;

            styles[new StyleKey(family, name)] = new StyleDefinition(
                name,
                (string?)style.Attribute(OdtNamespaces.Style + "parent-style-name"),
                style.Element(OdtNamespaces.Style + "paragraph-properties"),
                style.Element(OdtNamespaces.Style + "text-properties"));

            XElement? graphic = style.Element(OdtNamespaces.Style + "graphic-properties");
            if (graphic is not null)
                graphicProperties[name] = graphic;
        }

        foreach (XElement gradient in root.Descendants(OdtNamespaces.Draw + "gradient"))
        {
            string? name = (string?)gradient.Attribute(OdtNamespaces.Draw + "name");
            if (!string.IsNullOrWhiteSpace(name))
                gradients[name] = gradient;
        }

        foreach (XElement style in root.Descendants(OdtNamespaces.Style + "default-style"))
        {
            string? family = (string?)style.Attribute(OdtNamespaces.Style + "family");
            if (string.IsNullOrWhiteSpace(family))
                continue;

            defaults[family] = new DefaultStyle(
                style.Element(OdtNamespaces.Style + "paragraph-properties"),
                style.Element(OdtNamespaces.Style + "text-properties"));
        }

        foreach (XElement listStyle in root.Descendants(OdtNamespaces.Text + "list-style"))
        {
            string? name = (string?)listStyle.Attribute(OdtNamespaces.Style + "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            listStyles[name] = ListStyle.Read(listStyle);
        }
    }

    /// <summary>
    /// The <c>style:paragraph-properties</c> elements that apply to a paragraph
    /// naming <paramref name="styleName"/>, least specific first.
    /// </summary>
    public IReadOnlyList<XElement> ParagraphProperties(string? styleName) =>
        Resolve("pp:" + (styleName ?? string.Empty), () =>
        {
            var properties = new List<XElement>();
            if (_defaults.TryGetValue(ParagraphFamily, out DefaultStyle? fallback) &&
                fallback.ParagraphProperties is not null)
            {
                properties.Add(fallback.ParagraphProperties);
            }

            foreach (StyleDefinition style in Chain(ParagraphFamily, styleName))
            {
                if (style.ParagraphProperties is not null)
                    properties.Add(style.ParagraphProperties);
            }

            return properties;
        });

    /// <summary>
    /// The character formatting a run inherits inside a paragraph naming
    /// <paramref name="styleName"/>: the paragraph family default style, then the
    /// text properties carried by the paragraph style chain.
    /// </summary>
    public IReadOnlyList<XElement> TextPropertiesForParagraph(string? styleName) =>
        Resolve("pt:" + (styleName ?? string.Empty), () =>
        {
            var properties = new List<XElement>();
            if (_defaults.TryGetValue(ParagraphFamily, out DefaultStyle? fallback) &&
                fallback.TextProperties is not null)
            {
                properties.Add(fallback.TextProperties);
            }

            foreach (StyleDefinition style in Chain(ParagraphFamily, styleName))
            {
                if (style.TextProperties is not null)
                    properties.Add(style.TextProperties);
            }

            return properties;
        });

    /// <summary>
    /// The <c>style:text-properties</c> of the text-family style a
    /// <c>text:span</c> names, least specific first.
    /// </summary>
    public IReadOnlyList<XElement> TextPropertiesForSpan(string? styleName)
    {
        if (string.IsNullOrEmpty(styleName))
            return Array.Empty<XElement>();

        return Resolve("st:" + styleName, () =>
        {
            var properties = new List<XElement>();
            foreach (StyleDefinition style in Chain(TextFamily, styleName))
            {
                if (style.TextProperties is not null)
                    properties.Add(style.TextProperties);
            }

            return properties;
        });
    }

    /// <summary>
    /// Resolves a <c>style:font-name</c> reference to the family its
    /// <c>style:font-face</c> declares. A document that names a face it never
    /// declared keeps the reference, which is usually the family name anyway.
    /// </summary>
    public string? ResolveFontName(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return null;

        return _fontFaces.TryGetValue(fontName, out string? family) ? family : fontName;
    }

    /// <summary>
    /// The kind of list a <c>text:list</c> naming <paramref name="listStyleName"/>
    /// draws at <paramref name="level"/> (one-based). A list that names no style,
    /// or one the package never defined, is a bullet list, which is what an ODF
    /// consumer draws for it.
    /// </summary>
    public ListKind KindForList(string? listStyleName, int level)
    {
        if (string.IsNullOrEmpty(listStyleName))
            return ListKind.Bullet;

        if (!_listStyles.TryGetValue(listStyleName, out ListStyle? style))
        {
            Report(
                "odt.styles.list-unknown:" + listStyleName,
                "odt.styles.list-unknown",
                "A text:list named an undefined list style: " + listStyleName + ".");
            return ListKind.Bullet;
        }

        return style.KindAt(level);
    }

    private IReadOnlyList<XElement> Resolve(string key, Func<List<XElement>> build)
    {
        if (_chainCache.TryGetValue(key, out XElement[]? cached))
            return cached;

        XElement[] resolved = build().ToArray();
        _chainCache[key] = resolved;
        return resolved;
    }

    /// <summary>Walks <c>style:parent-style-name</c> to the root and returns the chain root-first.</summary>
    private List<StyleDefinition> Chain(string family, string? styleName)
    {
        var chain = new List<StyleDefinition>();
        if (string.IsNullOrEmpty(styleName))
            return chain;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = styleName;
        while (!string.IsNullOrEmpty(current))
        {
            if (!_styles.TryGetValue(new StyleKey(family, current), out StyleDefinition? style))
            {
                // Only the name the content actually referenced is worth
                // reporting: a dangling style:parent-style-name further along the
                // chain is the same defect seen one link later.
                if (string.Equals(current, styleName, StringComparison.Ordinal))
                {
                    Report(
                        "odt.styles.unknown:" + family + ":" + current,
                        "odt.styles.unknown",
                        "An ODT style reference named an undefined " + family + " style: " + current + ".");
                }

                break;
            }

            if (!visited.Add(current))
            {
                Report(
                    "odt.styles.cycle",
                    "odt.styles.cycle",
                    "An ODT style inheritance chain was cyclic and was cut short.");
                break;
            }

            if (chain.Count >= _maxChainDepth)
            {
                Report(
                    "odt.styles.depth",
                    "odt.styles.depth",
                    "An ODT style inheritance chain exceeded MaxGroupDepth and was cut short.");
                break;
            }

            chain.Add(style);
            current = style.ParentStyleName;
        }

        chain.Reverse();
        return chain;
    }

    private void Report(string key, string code, string message)
    {
        if (_reported.Add(key))
            _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }

    /// <summary>A style is identified by family and name; the families share one name space.</summary>
    private readonly record struct StyleKey(string Family, string Name);

    private sealed record StyleDefinition(
        string Name,
        string? ParentStyleName,
        XElement? ParagraphProperties,
        XElement? TextProperties);

    private sealed record DefaultStyle(XElement? ParagraphProperties, XElement? TextProperties);

    /// <summary>
    /// One <c>text:list-style</c>, reduced to the only question the model can
    /// answer: whether each level is bulleted or numbered.
    /// </summary>
    private sealed class ListStyle
    {
        private readonly Dictionary<int, ListKind> _levels;

        private ListStyle(Dictionary<int, ListKind> levels) => _levels = levels;

        public static ListStyle Read(XElement listStyle)
        {
            var levels = new Dictionary<int, ListKind>();
            foreach (XElement level in listStyle.Elements())
            {
                if (level.Name.Namespace != OdtNamespaces.Text)
                    continue;

                ListKind? kind = level.Name.LocalName switch
                {
                    // style:num-format is empty for an unnumbered level, which is
                    // how ODF writes a plain indent step.
                    "list-level-style-number" =>
                        string.IsNullOrEmpty((string?)level.Attribute(OdtNamespaces.Style + "num-format"))
                            ? ListKind.Bullet
                            : ListKind.Numbered,
                    "list-level-style-bullet" or "list-level-style-image" => ListKind.Bullet,
                    _ => null,
                };

                if (kind is null)
                    continue;

                if (int.TryParse(
                        (string?)level.Attribute(OdtNamespaces.Text + "level"),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int number) &&
                    number > 0)
                {
                    levels[number] = kind.Value;
                }
            }

            return new ListStyle(levels);
        }

        /// <summary>
        /// The kind at <paramref name="level"/>, falling back to the deepest level
        /// the style does define: ODF only requires the levels a document actually
        /// uses to be present.
        /// </summary>
        public ListKind KindAt(int level)
        {
            if (_levels.TryGetValue(level, out ListKind kind))
                return kind;

            ListKind best = ListKind.Bullet;
            int bestLevel = 0;
            foreach (KeyValuePair<int, ListKind> entry in _levels)
            {
                if (entry.Key <= level && entry.Key > bestLevel)
                {
                    best = entry.Value;
                    bestLevel = entry.Key;
                }
            }

            return best;
        }
    }
}
