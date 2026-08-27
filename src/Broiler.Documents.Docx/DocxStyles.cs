using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// The style table from <c>word/styles.xml</c>: document defaults plus the named
/// paragraph and character styles a document refers to through <c>w:pStyle</c>
/// and <c>w:rStyle</c>.
/// </summary>
/// <remarks>
/// Templates put nearly all of their formatting here — a CV's name is a 48pt
/// <c>Title</c> paragraph carrying no direct formatting at all — so a reader that
/// only honors direct <c>w:pPr</c>/<c>w:rPr</c> renders such a document as
/// undifferentiated body text. Resolution follows ECMA-376 §17.7.2: document
/// defaults first, then the <c>w:basedOn</c> chain from its root down to the
/// referenced style, then direct formatting (applied by the caller). The default
/// style applies only to content that names no style of its own.
/// </remarks>
internal sealed class DocxStyles
{
    private readonly Dictionary<string, StyleDefinition> _styles;
    private readonly XElement? _defaultParagraphProperties;
    private readonly XElement? _defaultRunProperties;
    private readonly string? _defaultParagraphStyleId;
    private readonly int _maxChainDepth;
    private readonly List<DocumentDiagnostic> _diagnostics;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement[]> _chainCache = new(StringComparer.Ordinal);

    private DocxStyles(
        Dictionary<string, StyleDefinition> styles,
        XElement? defaultParagraphProperties,
        XElement? defaultRunProperties,
        string? defaultParagraphStyleId,
        DocxTheme theme,
        int maxChainDepth,
        List<DocumentDiagnostic> diagnostics)
    {
        _styles = styles;
        _defaultParagraphProperties = defaultParagraphProperties;
        _defaultRunProperties = defaultRunProperties;
        _defaultParagraphStyleId = defaultParagraphStyleId;
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
        }

        return new DocxStyles(
            styles,
            defaultParagraphProperties,
            defaultRunProperties,
            defaultParagraphStyleId,
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
            theme,
            limits.MaxGroupDepth,
            diagnostics);

    /// <summary>
    /// The <c>w:pPr</c> elements that apply to a paragraph naming
    /// <paramref name="paragraphStyleId"/>, least specific first. The caller
    /// applies the paragraph's own <c>w:pPr</c> after these.
    /// </summary>
    public IReadOnlyList<XElement> ParagraphProperties(string? paragraphStyleId) =>
        Resolve("p:" + (paragraphStyleId ?? string.Empty), () =>
        {
            var properties = new List<XElement>();
            if (_defaultParagraphProperties is not null)
                properties.Add(_defaultParagraphProperties);

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
    /// defaults, then the paragraph style chain's run properties.
    /// </summary>
    public IReadOnlyList<XElement> RunPropertiesForParagraph(string? paragraphStyleId) =>
        Resolve("pr:" + (paragraphStyleId ?? string.Empty), () =>
        {
            var properties = new List<XElement>();
            if (_defaultRunProperties is not null)
                properties.Add(_defaultRunProperties);

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
    public IReadOnlyList<XElement> RunPropertiesForCharacterStyle(string? characterStyleId)
    {
        if (string.IsNullOrEmpty(characterStyleId))
            return Array.Empty<XElement>();

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

    private IReadOnlyList<XElement> Resolve(string key, Func<List<XElement>> build)
    {
        if (_chainCache.TryGetValue(key, out XElement[]? cached))
            return cached;

        XElement[] resolved = build().ToArray();
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
/// The major/minor latin typefaces from <c>word/theme/theme1.xml</c>. Styles
/// reference fonts through <c>w:rFonts w:asciiTheme="majorHAnsi"</c> rather than
/// by name, so without the theme every themed run loses its family.
/// </summary>
internal sealed class DocxTheme
{
    private DocxTheme(string? majorLatin, string? minorLatin)
    {
        MajorLatin = majorLatin;
        MinorLatin = minorLatin;
    }

    public static DocxTheme Empty { get; } = new(null, null);

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
        XElement? fontScheme = xml?.Root
            ?.Element(DocxNamespaces.Drawing + "themeElements")
            ?.Element(DocxNamespaces.Drawing + "fontScheme");
        if (fontScheme is null)
            return Empty;

        return new DocxTheme(
            LatinTypeface(fontScheme.Element(DocxNamespaces.Drawing + "majorFont")),
            LatinTypeface(fontScheme.Element(DocxNamespaces.Drawing + "minorFont")));
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
