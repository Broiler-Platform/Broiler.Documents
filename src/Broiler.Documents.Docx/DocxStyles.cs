using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>
/// The style table from <c>word/styles.xml</c>: document defaults plus the named
/// paragraph, character and table styles a document refers to through
/// <c>w:pStyle</c>, <c>w:rStyle</c> and <c>w:tblStyle</c>.
/// </summary>
/// <remarks>
/// Templates put nearly all of their formatting here — a CV's name is a 48pt
/// <c>Title</c> paragraph carrying no direct formatting at all — so a reader that
/// only honors direct <c>w:pPr</c>/<c>w:rPr</c> renders such a document as
/// undifferentiated body text. Resolution follows ECMA-376 §17.7.2: document
/// defaults first, then for content inside a table the table style's chain, then
/// the <c>w:basedOn</c> chain from its root down to the referenced paragraph
/// style, then direct formatting (applied by the caller). The default paragraph
/// style applies only to content that names no style of its own, and the default
/// table style only to a table that names none.
/// </remarks>
internal sealed class DocxStyles
{
    private readonly Dictionary<string, StyleDefinition> _styles;
    private readonly XElement? _defaultParagraphProperties;
    private readonly XElement? _defaultRunProperties;
    private readonly string? _defaultParagraphStyleId;
    private readonly string? _defaultTableStyleId;
    private readonly int _maxChainDepth;
    private readonly List<DocumentDiagnostic> _diagnostics;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement[]> _chainCache = new(StringComparer.Ordinal);

    private DocxStyles(
        Dictionary<string, StyleDefinition> styles,
        XElement? defaultParagraphProperties,
        XElement? defaultRunProperties,
        string? defaultParagraphStyleId,
        string? defaultTableStyleId,
        DocxTheme theme,
        int maxChainDepth,
        List<DocumentDiagnostic> diagnostics)
    {
        _styles = styles;
        _defaultParagraphProperties = defaultParagraphProperties;
        _defaultRunProperties = defaultRunProperties;
        _defaultParagraphStyleId = defaultParagraphStyleId;
        _defaultTableStyleId = defaultTableStyleId;
        _maxChainDepth = maxChainDepth;
        _diagnostics = diagnostics;
        Theme = theme;
    }

    /// <summary>Theme fonts, needed to resolve <c>w:rFonts</c> theme references.</summary>
    public DocxTheme Theme { get; }

    /// <summary>The number of named styles that were loaded.</summary>
    public int Count => _styles.Count;

    public static DocxStyles Load(
        ZipArchive archive,
        DocxRelationships documentRelationships,
        string documentBaseDirectory,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        DocxTheme theme = DocxTheme.Load(
            archive,
            documentRelationships,
            documentBaseDirectory,
            limits,
            diagnostics);

        string stylesPath = DocxPackage.ResolvePartPath(
            documentRelationships,
            DocxNamespaces.StylesRelationship,
            documentBaseDirectory,
            "styles.xml");

        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, stylesPath);
        if (entry is null)
            return WithoutStyles(theme, limits, diagnostics);

        XDocument? xml = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.styles");
        if (xml?.Root is null)
            return WithoutStyles(theme, limits, diagnostics);

        XElement? docDefaults = xml.Root.Element(DocxNamespaces.Wordprocessing + "docDefaults");
        XElement? defaultParagraphProperties = docDefaults
            ?.Element(DocxNamespaces.Wordprocessing + "pPrDefault")
            ?.Element(DocxNamespaces.Wordprocessing + "pPr");
        XElement? defaultRunProperties = docDefaults
            ?.Element(DocxNamespaces.Wordprocessing + "rPrDefault")
            ?.Element(DocxNamespaces.Wordprocessing + "rPr");

        var styles = new Dictionary<string, StyleDefinition>(StringComparer.Ordinal);
        string? defaultParagraphStyleId = null;
        string? defaultTableStyleId = null;
        foreach (XElement style in xml.Root.Elements(DocxNamespaces.Wordprocessing + "style"))
        {
            string? id = (string?)style.Attribute(DocxNamespaces.Wordprocessing + "styleId");
            if (string.IsNullOrEmpty(id))
                continue;

            string type = (string?)style.Attribute(DocxNamespaces.Wordprocessing + "type") ?? "paragraph";
            var definition = new StyleDefinition(
                id,
                type,
                (string?)style.Element(DocxNamespaces.Wordprocessing + "basedOn")?.Attribute(DocxNamespaces.Wordprocessing + "val"),
                style.Element(DocxNamespaces.Wordprocessing + "pPr"),
                style.Element(DocxNamespaces.Wordprocessing + "rPr"));
            styles[id] = definition;

            bool isDefault = IsOn((string?)style.Attribute(DocxNamespaces.Wordprocessing + "default"));
            if (isDefault && type.Equals("paragraph", StringComparison.Ordinal))
                defaultParagraphStyleId ??= id;
            if (isDefault && type.Equals("table", StringComparison.Ordinal))
                defaultTableStyleId ??= id;
        }

        return new DocxStyles(
            styles,
            defaultParagraphProperties,
            defaultRunProperties,
            defaultParagraphStyleId,
            defaultTableStyleId,
            theme,
            limits.MaxGroupDepth,
            diagnostics);
    }

    private static DocxStyles WithoutStyles(
        DocxTheme theme,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics) =>
        new(new Dictionary<string, StyleDefinition>(StringComparer.Ordinal),
            defaultParagraphProperties: null,
            defaultRunProperties: null,
            defaultParagraphStyleId: null,
            defaultTableStyleId: null,
            theme,
            limits.MaxGroupDepth,
            diagnostics);

    /// <summary>
    /// The table style that governs a table: the one its <c>w:tblStyle</c>
    /// names, or the document's default table style when it names none. Null
    /// when neither exists.
    /// </summary>
    public string? TableStyleFor(string? tableStyleId) =>
        string.IsNullOrEmpty(tableStyleId) ? _defaultTableStyleId : tableStyleId;

    /// <summary>
    /// The <c>w:pPr</c> elements that apply to a paragraph naming
    /// <paramref name="paragraphStyleId"/>, least specific first. The caller
    /// applies the paragraph's own <c>w:pPr</c> after these.
    /// </summary>
    /// <remarks>
    /// <paramref name="tableStyleId"/> is the table style of the table the
    /// paragraph is in, null outside one. Its properties sit between the
    /// document defaults and the paragraph style, which is where ECMA-376
    /// §17.7.2 puts them and where Word and LibreOffice apply them: a
    /// <c>Table Grid</c> that says "no space after" closes up every cell of a
    /// document whose defaults leave ten points under each paragraph, and a
    /// paragraph style that says otherwise still wins.
    /// </remarks>
    public IReadOnlyList<XElement> ParagraphProperties(string? paragraphStyleId, string? tableStyleId = null) =>
        Resolve("p:" + (tableStyleId ?? string.Empty) + "\0" + (paragraphStyleId ?? string.Empty), () =>
        {
            var properties = new List<XElement>();
            if (_defaultParagraphProperties is not null)
                properties.Add(_defaultParagraphProperties);

            foreach (StyleDefinition style in TableChain(tableStyleId))
            {
                if (style.ParagraphProperties is not null)
                    properties.Add(style.ParagraphProperties);
            }

            foreach (StyleDefinition style in ParagraphChain(paragraphStyleId))
            {
                if (style.ParagraphProperties is not null)
                    properties.Add(style.ParagraphProperties);
            }

            return properties;
        });

    /// <summary>
    /// The <c>w:rPr</c> elements that form a run's inherited character formatting
    /// inside a paragraph naming <paramref name="paragraphStyleId"/>: document
    /// defaults, then the table style chain's run properties when the paragraph
    /// is in a table, then the paragraph style chain's.
    /// </summary>
    public IReadOnlyList<XElement> RunPropertiesForParagraph(string? paragraphStyleId, string? tableStyleId = null) =>
        Resolve("pr:" + (tableStyleId ?? string.Empty) + "\0" + (paragraphStyleId ?? string.Empty), () =>
        {
            var properties = new List<XElement>();
            if (_defaultRunProperties is not null)
                properties.Add(_defaultRunProperties);

            foreach (StyleDefinition style in TableChain(tableStyleId))
            {
                if (style.RunProperties is not null)
                    properties.Add(style.RunProperties);
            }

            foreach (StyleDefinition style in ParagraphChain(paragraphStyleId))
            {
                if (style.RunProperties is not null)
                    properties.Add(style.RunProperties);
            }

            return properties;
        });

    /// <summary>
    /// The <c>w:rPr</c> elements of a character style named by <c>w:rStyle</c>,
    /// least specific first. Applied over the paragraph's inherited formatting
    /// and before the run's own <c>w:rPr</c>.
    /// </summary>
    public XElement[] RunPropertiesForCharacterStyle(string? characterStyleId)
    {
        if (string.IsNullOrEmpty(characterStyleId))
            return [];

        return Resolve("cr:" + characterStyleId, () =>
        {
            var properties = new List<XElement>();
            foreach (StyleDefinition style in Chain(characterStyleId))
            {
                if (style.RunProperties is not null)
                    properties.Add(style.RunProperties);
            }

            return properties;
        });
    }

    private XElement[] Resolve(string key, Func<List<XElement>> build)
    {
        if (_chainCache.TryGetValue(key, out XElement[]? cached))
            return cached;

        XElement[] resolved = [.. build()];
        _chainCache[key] = resolved;
        return resolved;
    }

    /// <summary>
    /// The style chain for a paragraph. A paragraph that names no style inherits
    /// the document's default paragraph style; one that names a style does not,
    /// except through its own <c>w:basedOn</c> ancestry.
    /// </summary>
    private List<StyleDefinition> ParagraphChain(string? paragraphStyleId) =>
        Chain(string.IsNullOrEmpty(paragraphStyleId) ? _defaultParagraphStyleId : paragraphStyleId);

    /// <summary>
    /// The chain of a table style. Only table styles belong in it: a
    /// <c>w:tblStyle</c> naming a paragraph style is a damaged reference, and
    /// reading that style's properties into every cell would apply formatting
    /// the document gave to something else.
    /// </summary>
    private List<StyleDefinition> TableChain(string? tableStyleId)
    {
        if (string.IsNullOrEmpty(tableStyleId))
            return [];

        List<StyleDefinition> chain = Chain(tableStyleId);
        chain.RemoveAll(style => !style.Type.Equals("table", StringComparison.Ordinal));
        return chain;
    }

    /// <summary>Walks <c>w:basedOn</c> to the root and returns the chain root-first.</summary>
    private List<StyleDefinition> Chain(string? styleId)
    {
        var chain = new List<StyleDefinition>();
        if (string.IsNullOrEmpty(styleId))
            return chain;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = styleId;
        while (!string.IsNullOrEmpty(current))
        {
            if (!_styles.TryGetValue(current, out StyleDefinition? style))
            {
                // A package with no style table at all is one fact, not one per
                // referenced id. Otherwise only the id the document actually
                // referenced is worth reporting — a dangling w:basedOn inside the
                // table is the same defect seen one link further along.
                if (_styles.Count == 0)
                {
                    Report(
                        "docx.styles.missing",
                        "docx.styles.missing",
                        "DOCX content referenced named styles, but the package had no styles part.");
                }
                else if (string.Equals(current, styleId, StringComparison.Ordinal))
                {
                    Report(
                        "docx.styles.unknown:" + current,
                        "docx.styles.unknown",
                        "A DOCX style reference named an undefined style: " + current + ".");
                }

                break;
            }

            if (!visited.Add(current))
            {
                Report("docx.styles.cycle", "docx.styles.cycle", "A DOCX style inheritance chain was cyclic and was cut short.");
                break;
            }

            if (chain.Count >= _maxChainDepth)
            {
                Report("docx.styles.depth", "docx.styles.depth", "A DOCX style inheritance chain exceeded MaxGroupDepth and was cut short.");
                break;
            }

            chain.Add(style);
            current = style.BasedOn;
        }

        chain.Reverse();
        return chain;
    }

    private void Report(string key, string code, string message)
    {
        if (_reported.Add(key))
            _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }

    private static bool IsOn(string? value) =>
        value is not null &&
        !(value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
          value.Equals("0", StringComparison.Ordinal) ||
          value.Equals("off", StringComparison.OrdinalIgnoreCase));

    private sealed record StyleDefinition(
        string Id,
        string Type,
        string? BasedOn,
        XElement? ParagraphProperties,
        XElement? RunProperties);
}

/// <summary>
/// The major/minor latin typefaces and the colour scheme from
/// <c>word/theme/theme1.xml</c>. Styles reference fonts through
/// <c>w:rFonts w:asciiTheme="majorHAnsi"</c> rather than by name, and shapes
/// colours through <c>a:schemeClr</c>, so without the theme every themed run
/// loses its family and every themed shape its paint.
/// </summary>
internal sealed class DocxTheme
{
    private readonly Dictionary<string, BColor> _colors;

    private DocxTheme(string? majorLatin, string? minorLatin, Dictionary<string, BColor> colors)
    {
        MajorLatin = majorLatin;
        MinorLatin = minorLatin;
        _colors = colors;
    }

    public static DocxTheme Empty { get; } = new(null, null, new Dictionary<string, BColor>(StringComparer.Ordinal));

    public string? MajorLatin { get; }

    public string? MinorLatin { get; }

    public static DocxTheme Load(
        ZipArchive archive,
        DocxRelationships documentRelationships,
        string documentBaseDirectory,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        string themePath = DocxPackage.ResolvePartPath(
            documentRelationships,
            DocxNamespaces.ThemeRelationship,
            documentBaseDirectory,
            "theme/theme1.xml");

        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, themePath);
        if (entry is null)
            return Empty;

        XDocument? xml = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.theme");
        XElement? elements = xml?.Root?.Element(DocxNamespaces.Drawing + "themeElements");
        if (elements is null)
            return Empty;

        XElement? fontScheme = elements.Element(DocxNamespaces.Drawing + "fontScheme");
        return new DocxTheme(
            LatinTypeface(fontScheme?.Element(DocxNamespaces.Drawing + "majorFont")),
            LatinTypeface(fontScheme?.Element(DocxNamespaces.Drawing + "minorFont")),
            ReadColorScheme(elements.Element(DocxNamespaces.Drawing + "clrScheme")));
    }

    /// <summary>
    /// The colour a <c>a:schemeClr</c> names, before its transforms.
    /// </summary>
    /// <remarks>
    /// The scheme holds twelve slots (ECMA-376 §20.1.6.2); a shape may also name
    /// the four a document maps onto them, background and text one and two.
    /// <c>w:clrSchemeMapping</c> in the settings can remap those, but Word writes
    /// the default mapping, and that is the one taken here: <c>bg1</c> is
    /// <c>lt1</c>, <c>tx1</c> is <c>dk1</c>, and so on. <c>phClr</c> stands for a
    /// colour a style supplies, which this reader does not resolve.
    /// </remarks>
    public bool TryResolveColor(string? name, out BColor color)
    {
        string? slot = name switch
        {
            "bg1" => "lt1",
            "tx1" => "dk1",
            "bg2" => "lt2",
            "tx2" => "dk2",
            _ => name,
        };
        if (slot is not null && _colors.TryGetValue(slot, out color))
            return true;

        color = BColor.Empty;
        return false;
    }

    private static Dictionary<string, BColor> ReadColorScheme(XElement? scheme)
    {
        var colors = new Dictionary<string, BColor>(StringComparer.Ordinal);
        if (scheme is null)
            return colors;

        foreach (XElement slot in scheme.Elements())
        {
            XElement? value = slot.Elements().FirstOrDefault();
            string? hex = value?.Name.LocalName switch
            {
                "srgbClr" => (string?)value.Attribute("val"),
                "sysClr" => (string?)value.Attribute("lastClr"),
                _ => null,
            };
            if (DocxValueReader.TryParseHexColor(hex, out BColor color))
                colors[slot.Name.LocalName] = color;
        }

        return colors;
    }

    /// <summary>Maps a <c>w:rFonts</c> theme reference such as <c>majorHAnsi</c> to a family name.</summary>
    public string? Resolve(string? themeReference)
    {
        if (string.IsNullOrEmpty(themeReference))
            return null;
        if (themeReference.StartsWith("major", StringComparison.OrdinalIgnoreCase))
            return MajorLatin;
        if (themeReference.StartsWith("minor", StringComparison.OrdinalIgnoreCase))
            return MinorLatin;

        return null;
    }

    private static string? LatinTypeface(XElement? font)
    {
        string? typeface = (string?)font?.Element(DocxNamespaces.Drawing + "latin")?.Attribute("typeface");
        return string.IsNullOrWhiteSpace(typeface) ? null : typeface;
    }
}
