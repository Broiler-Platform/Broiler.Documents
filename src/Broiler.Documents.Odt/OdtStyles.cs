using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
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

    /// <summary>
    /// The table family. Nothing here resolves table properties through the
    /// chain, but a table style may name the master page a document starts on,
    /// and a document may begin with a table.
    /// </summary>
    public const string TableFamily = "table";

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
    public int ListStyleCount => _listStyles.ContainsKey(OutlineKey) ? _listStyles.Count - 1 : _listStyles.Count;

    // The outline style is kept with the list styles, which it is one of in all
    // but name, under a key no style:name can be.
    private static readonly string OutlineKey = (char)1 + "outline";

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
    /// The <c>text:notes-configuration</c> elements from <c>office:styles</c>:
    /// how the document numbers its footnotes and its endnotes.
    /// </summary>
    public IReadOnlyList<XElement> NotesConfigurations { get; init; } = [];

    /// <summary>
    /// A <c>style:num-format</c>: <c>1</c>, <c>a</c>, <c>A</c>, <c>i</c> or
    /// <c>I</c>, empty for no number, and decimal for anything else.
    /// </summary>
    public static ListNumberFormat NumberFormat(string? value) => value switch
    {
        null or "" => ListNumberFormat.None,
        "1" => ListNumberFormat.Decimal,
        "a" => ListNumberFormat.LowerLetter,
        "A" => ListNumberFormat.UpperLetter,
        "i" => ListNumberFormat.LowerRoman,
        "I" => ListNumberFormat.UpperRoman,
        _ => ListNumberFormat.Decimal,
    };

    /// <summary>
    /// The <c>style:graphic-properties</c> of every graphic style, by name. A
    /// shape names one, and that is where ODF states how the box is painted.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> GraphicProperties { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>style:section-properties</c> of every section style, by name: where
    /// a <c>text:section</c> states the columns its text is laid out in.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> SectionProperties { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>style:table-cell-properties</c> of every style that has them, by
    /// name. A cell names one, and that is where ODF states its background and
    /// its borders.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> TableCellProperties { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>style:table-column-properties</c> by name, which is where a column
    /// states its width.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> TableColumnProperties { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>style:table-row-properties</c> by name, which is where a row states
    /// its height.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> TableRowProperties { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>draw:gradient</c> elements by name. A graphic style refers to one
    /// rather than carrying the stops itself.
    /// </summary>
    public IReadOnlyDictionary<string, XElement> Gradients { get; init; } =
        new Dictionary<string, XElement>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>style:drawing-page-properties</c> by style name: where a master
    /// page's <c>draw:style-name</c> states the page's fill (ODF 1.3 §16.10).
    /// </summary>
    public IReadOnlyDictionary<string, XElement> DrawingPageProperties { get; init; } =
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
        ZipArchiveEntry? stylesEntry = OdtPackage.FindEntry(archive, OdtNamespaces.StylesPart);
        XElement? stylesRoot = stylesEntry is null
            ? null
            : OdtPackage.LoadEntryXml(stylesEntry, limits, diagnostics, "odt.styles")?.Root;
        return Load(stylesRoot, content.Root, limits, diagnostics);
    }

    /// <summary>
    /// Builds the table from the root of each part that holds styles.
    /// </summary>
    /// <remarks>
    /// A flat document passes its one <c>office:document</c> root as
    /// <paramref name="stylesRoot"/> and nothing as <paramref name="contentRoot"/>:
    /// it holds <c>office:styles</c>, <c>office:automatic-styles</c> and
    /// <c>office:master-styles</c> side by side, which is everything the two
    /// package parts hold between them.
    /// </remarks>
    public static OdtStyles Load(
        XElement? stylesRoot,
        XElement? contentRoot,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        var styles = new Dictionary<StyleKey, StyleDefinition>();
        var defaults = new Dictionary<string, DefaultStyle>(StringComparer.Ordinal);
        var fontFaces = new Dictionary<string, string>(StringComparer.Ordinal);
        var listStyles = new Dictionary<string, ListStyle>(StringComparer.Ordinal);
        var graphicProperties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var cellProperties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var columnProperties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var rowProperties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var sectionProperties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var gradients = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var drawingPageProperties = new Dictionary<string, XElement>(StringComparer.Ordinal);
        XElement? masterStyles = null;
        List<XElement> pageLayouts = [];
        List<XElement> notesConfigurations = [];

        if (stylesRoot is not null)
        {
            Collect(
                stylesRoot,
                styles,
                defaults,
                fontFaces,
                listStyles,
                graphicProperties,
                cellProperties,
                columnProperties,
                rowProperties,
                sectionProperties,
                gradients,
                drawingPageProperties);
            masterStyles = stylesRoot.Element(OdtNamespaces.Office + "master-styles");
            pageLayouts = [.. stylesRoot.Descendants(OdtNamespaces.Style + "page-layout")];
            notesConfigurations = [.. stylesRoot.Elements(OdtNamespaces.Office + "styles")
                .Elements(OdtNamespaces.Text + "notes-configuration")];
        }

        if (contentRoot is not null)
        {
            Collect(
                contentRoot,
                styles,
                defaults,
                fontFaces,
                listStyles,
                graphicProperties,
                cellProperties,
                columnProperties,
                rowProperties,
                sectionProperties,
                gradients,
                drawingPageProperties);
        }

        return new OdtStyles(styles, defaults, fontFaces, listStyles, limits.MaxGroupDepth, diagnostics)
        {
            MasterStyles = masterStyles,
            PageLayouts = pageLayouts,
            NotesConfigurations = notesConfigurations,
            GraphicProperties = ResolveGraphicChains(styles, graphicProperties, limits.MaxGroupDepth),
            TableCellProperties = cellProperties,
            TableColumnProperties = columnProperties,
            TableRowProperties = rowProperties,
            SectionProperties = sectionProperties,
            Gradients = gradients,
            DrawingPageProperties = drawingPageProperties,
        };
    }

    /// <summary>
    /// Each graphic style's properties with what its parents state beneath them.
    /// </summary>
    /// <remarks>
    /// ODF styles inherit through <c>style:parent-style-name</c>, and LibreOffice
    /// relies on it for frames: the paint and border of a text box live in a
    /// named style such as <c>encarts</c>, and the automatic style the frame
    /// names only adds where it stands. Read alone, that automatic style left the
    /// box unpainted. Attributes are merged root first, so the nearest statement
    /// wins; child elements are taken whole from the nearest style that has any.
    /// A chain that loops or runs past <paramref name="maxDepth"/> stops there.
    /// </remarks>
    private static Dictionary<string, XElement> ResolveGraphicChains(
        Dictionary<StyleKey, StyleDefinition> styles,
        Dictionary<string, XElement> own,
        int maxDepth)
    {
        var resolved = new Dictionary<string, XElement>(own, StringComparer.Ordinal);
        foreach (StyleKey key in styles.Keys)
        {
            if (!string.Equals(key.Family, "graphic", StringComparison.Ordinal))
                continue;

            var chain = new List<XElement>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? current = key.Name;
            while (current is not null && visited.Count <= maxDepth && visited.Add(current))
            {
                if (own.TryGetValue(current, out XElement? properties))
                    chain.Add(properties);

                current = styles.TryGetValue(new StyleKey("graphic", current), out StyleDefinition? definition)
                    ? definition.ParentStyleName
                    : null;
            }

            if (chain.Count < 2)
                continue;

            var merged = new XElement(chain[0].Name);
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                foreach (XAttribute attribute in chain[i].Attributes())
                    merged.SetAttributeValue(attribute.Name, attribute.Value);
            }

            XElement? withChildren = chain.Find(properties => properties.HasElements);
            if (withChildren is not null)
                merged.Add(withChildren.Elements());

            resolved[key.Name] = merged;
        }

        return resolved;
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
        Dictionary<string, XElement> cellProperties,
        Dictionary<string, XElement> columnProperties,
        Dictionary<string, XElement> rowProperties,
        Dictionary<string, XElement> sectionProperties,
        Dictionary<string, XElement> gradients,
        Dictionary<string, XElement> drawingPageProperties)
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
                style.Element(OdtNamespaces.Style + "text-properties"),
                (string?)style.Attribute(OdtNamespaces.Style + "master-page-name"));

            XElement? graphic = style.Element(OdtNamespaces.Style + "graphic-properties");
            if (graphic is not null)
                graphicProperties[name] = graphic;

            XElement? cell = style.Element(OdtNamespaces.Style + "table-cell-properties");
            if (cell is not null)
                cellProperties[name] = cell;

            XElement? column = style.Element(OdtNamespaces.Style + "table-column-properties");
            if (column is not null)
                columnProperties[name] = column;

            XElement? tableRow = style.Element(OdtNamespaces.Style + "table-row-properties");
            if (tableRow is not null)
                rowProperties[name] = tableRow;

            XElement? section = style.Element(OdtNamespaces.Style + "section-properties");
            if (section is not null)
                sectionProperties[name] = section;

            XElement? drawingPage = style.Element(OdtNamespaces.Style + "drawing-page-properties");
            if (drawingPage is not null)
                drawingPageProperties[name] = drawingPage;
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

        foreach (XElement outlineStyle in root.Descendants(OdtNamespaces.Text + "outline-style"))
            listStyles[OutlineKey] = ListStyle.Read(outlineStyle);
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
            return [];

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
    /// The master page a style puts its content on, resolved through the
    /// inheritance chain, or null when no style in the chain names one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ODF 1.3 section 20.301 puts <c>style:master-page-name</c> on the
    /// paragraph or table style rather than on the content, and a style that
    /// carries a non-empty one starts a new page laid out by that master. It is
    /// the only route from a document's body to its paper: the master pages
    /// themselves sit in <c>styles.xml</c> in no particular order and none of
    /// them is marked as the one the document begins on.
    /// </para>
    /// <para>
    /// The most specific declaration wins, which is why the chain is walked
    /// rather than the named style being read directly. A word processor
    /// routinely emits an automatic style whose only content is this attribute
    /// and whose parent carries everything else.
    /// </para>
    /// <para>
    /// An empty value is kept rather than treated as absent, and the difference
    /// is load-bearing. ODF says an empty <c>style:master-page-name</c> means no
    /// page break occurs, so a style can use it to cancel one it would otherwise
    /// have inherited. Folding empty into null would turn that cancellation back
    /// into the inherited page.
    /// </para>
    /// </remarks>
    public string? MasterPageName(string family, string? styleName)
    {
        string? name = null;
        foreach (StyleDefinition style in Chain(family, styleName))
        {
            if (style.MasterPageName is not null)
                name = style.MasterPageName;
        }

        return name;
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

    /// <summary>
    /// The list definition <paramref name="listStyleName"/> stands for, or null
    /// for a list that names no style or one the package never defined. A list
    /// that starts counting afresh copies it.
    /// </summary>
    public ListDefinition? DefinitionForList(string? listStyleName) =>
        !string.IsNullOrEmpty(listStyleName) && _listStyles.TryGetValue(listStyleName, out ListStyle? style)
            ? style.Definition
            : null;

    /// <summary>
    /// The document's outline numbering (<c>text:outline-style</c>), which
    /// numbers its headings by <c>text:outline-level</c>; null when it has none
    /// or numbers no level.
    /// </summary>
    public ListDefinition? OutlineDefinition =>
        _listStyles.TryGetValue(OutlineKey, out ListStyle? style) && style.Numbers ? style.Definition : null;

    private XElement[] Resolve(string key, Func<List<XElement>> build)
    {
        if (_chainCache.TryGetValue(key, out XElement[]? cached))
            return cached;

        XElement[] resolved = [.. build()];
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

    /// <summary>Reports a diagnostic once per <paramref name="key"/>.</summary>
    internal void Report(string key, string code, string message)
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
        XElement? TextProperties,
        string? MasterPageName);

    private sealed record DefaultStyle(XElement? ParagraphProperties, XElement? TextProperties);

    /// <summary>
    /// One <c>text:list-style</c>, or the document's <c>text:outline-style</c>:
    /// how each level counts and what its label reads (ODF 1.3 §16.30-16.33).
    /// </summary>
    private sealed class ListStyle
    {
        private readonly Dictionary<int, ListKind> _levels;

        private ListStyle(Dictionary<int, ListKind> levels, ListDefinition definition, bool numbers)
        {
            _levels = levels;
            Definition = definition;
            Numbers = numbers;
        }

        /// <summary>
        /// The levels as a list definition. This one instance stands for the
        /// style; each <c>text:list</c> that starts counting afresh is a copy.
        /// </summary>
        public ListDefinition Definition { get; }

        /// <summary>True when some level draws a number, which an outline style needs to number anything.</summary>
        public bool Numbers { get; }

        public static ListStyle Read(XElement listStyle)
        {
            var levels = new Dictionary<int, ListKind>();
            var stated = new Dictionary<int, ListLevel>();
            bool numbers = false;
            foreach (XElement level in listStyle.Elements())
            {
                if (level.Name.Namespace != OdtNamespaces.Text)
                    continue;

                if (!int.TryParse(
                        (string?)level.Attribute(OdtNamespaces.Text + "level"),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int number) ||
                    number <= 0)
                {
                    continue;
                }

                ListKind kind;
                ListLevel definition;
                switch (level.Name.LocalName)
                {
                    case "list-level-style-number":
                    case "outline-level-style":
                        definition = ReadNumberLevel(level, number);

                        // style:num-format is empty for an unnumbered level,
                        // which is how ODF writes a plain indent step.
                        kind = definition.Format == ListNumberFormat.None ? ListKind.Bullet : ListKind.Numbered;
                        numbers |= definition.Format != ListNumberFormat.None;
                        break;
                    case "list-level-style-bullet":
                        string? bullet = (string?)level.Attribute(OdtNamespaces.Text + "bullet-char");
                        definition = new ListLevel
                        {
                            Format = ListNumberFormat.Bullet,
                            Template = string.IsNullOrEmpty(bullet) ? ((char)0x2022).ToString() : bullet,
                        };
                        kind = ListKind.Bullet;
                        break;
                    case "list-level-style-image":
                        definition = new ListLevel { Format = ListNumberFormat.Bullet, Template = ((char)0x2022).ToString() };
                        kind = ListKind.Bullet;
                        break;
                    default:
                        continue;
                }

                levels[number] = kind;
                if (number <= ListDefinition.MaxLevels)
                    stated[number - 1] = definition;
            }

            // ODF only requires the levels a document uses to be present; a
            // level left out is drawn as the deepest one above it that is.
            var resolved = new ListLevel[ListDefinition.MaxLevels];
            ListLevel? above = null;
            for (int i = 0; i < resolved.Length; i++)
            {
                if (stated.TryGetValue(i, out ListLevel? level))
                {
                    above = level;
                    resolved[i] = level;
                    continue;
                }

                resolved[i] = above is null
                    ? ListLevel.DecimalAt(i)
                    : above with
                    {
                        Template = above.Format is ListNumberFormat.Bullet or ListNumberFormat.None
                            ? above.Template
                            : "%" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".",
                    };
            }

            string? name = (string?)listStyle.Attribute(OdtNamespaces.Style + "name");
            return new ListStyle(levels, new ListDefinition(resolved, name), numbers);
        }

        /// <summary>
        /// A numbered level. Its label is the prefix, the numbers of the
        /// <c>text:display-levels</c> levels that end at this one joined by full
        /// stops, and the suffix: display-levels 2 at the third level with the
        /// suffix "." reads "%2.%3.".
        /// </summary>
        private static ListLevel ReadNumberLevel(XElement level, int number)
        {
            ListNumberFormat format = NumberFormat((string?)level.Attribute(OdtNamespaces.Style + "num-format"));

            int last = Math.Min(number, ListDefinition.MaxLevels);
            int display = 1;
            if (int.TryParse((string?)level.Attribute(OdtNamespaces.Text + "display-levels"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int displayLevels))
            {
                display = Math.Clamp(displayLevels, 1, last);
            }

            int start = 1;
            if (int.TryParse((string?)level.Attribute(OdtNamespaces.Text + "start-value"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int startValue))
            {
                start = startValue;
            }

            var template = new System.Text.StringBuilder();
            template.Append((string?)level.Attribute(OdtNamespaces.Style + "num-prefix"));
            if (format != ListNumberFormat.None)
            {
                for (int i = last - display + 1; i <= last; i++)
                {
                    if (i > last - display + 1)
                        template.Append('.');
                    template.Append('%').Append(i.ToString(CultureInfo.InvariantCulture));
                }
            }

            template.Append((string?)level.Attribute(OdtNamespaces.Style + "num-suffix"));
            return new ListLevel { Format = format, Template = template.ToString(), Start = start };
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
