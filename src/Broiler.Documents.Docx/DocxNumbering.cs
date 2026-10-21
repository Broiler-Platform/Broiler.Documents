using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// The document's <c>numbering.xml</c>: which list each <c>w:numId</c> is, and
/// how each of its levels counts and reads (ECMA-376 §17.9).
/// </summary>
/// <remarks>
/// <para>
/// An abstract numbering (<c>w:abstractNum</c>) holds the levels, and a
/// <c>w:num</c> names one. Every <c>w:num</c> naming the same abstract
/// numbering is one list here and counts on from the others, which is what Word
/// draws; one that overrides a level (<c>w:lvlOverride</c>, §17.9.9) is a list of
/// its own, starting where its <c>w:startOverride</c> says.
/// </para>
/// <para>
/// An abstract numbering that only points at a numbering style
/// (<c>w:numStyleLink</c>, §17.9.21) takes its levels from the abstract
/// numbering that defines that style (<c>w:styleLink</c>, §17.9.27), and is the
/// same list as it.
/// </para>
/// </remarks>
internal sealed class DocxNumbering
{
    private readonly Dictionary<int, NumEntry> _nums;

    private DocxNumbering(Dictionary<int, NumEntry> nums) => _nums = nums;

    public static DocxNumbering Empty { get; } = new([]);

    public static DocxNumbering Load(ZipArchive archive, DocxRelationships documentRelationships, string documentBaseDirectory,
        DocumentLimits limits, List<DocumentDiagnostic> diagnostics)
    {
        string numberingPath = DocxPackage.ResolvePartPath(documentRelationships, DocxNamespaces.NumberingRelationship,
            documentBaseDirectory, "numbering.xml");

        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, numberingPath);
        if (entry is null)
            return Empty;

        XDocument? xml = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.numbering");
        if (xml?.Root is null)
            return Empty;

        return Read(xml.Root, diagnostics);
    }

    /// <summary>Reads a <c>w:numbering</c> element.</summary>
    internal static DocxNumbering Read(XElement root, List<DocumentDiagnostic>? diagnostics = null)
    {
        XNamespace w = DocxNamespaces.Wordprocessing;
        var abstracts = new Dictionary<int, AbstractEntry>();
        var styleLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        bool reportedFormat = false;

        foreach (XElement abstractNum in root.Elements(w + "abstractNum"))
        {
            if (!DocxValueReader.TryReadInt(abstractNum.Attribute(w + "abstractNumId"), out int abstractId))
                continue;

            var levels = new ListLevel[ListDefinition.MaxLevels];
            var indents = new LevelIndent?[ListDefinition.MaxLevels];
            var styles = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (XElement lvl in abstractNum.Elements(w + "lvl"))
            {
                if (!DocxValueReader.TryReadInt(lvl.Attribute(w + "ilvl"), out int ilvl) ||
                    ilvl < 0 || ilvl >= ListDefinition.MaxLevels)
                {
                    continue;
                }

                levels[ilvl] = ReadLevel(lvl, ilvl, ref reportedFormat, diagnostics);
                indents[ilvl] = ReadIndent(lvl.Element(w + "pPr")?.Element(w + "ind"));
                if (DocxValueReader.WordValue(lvl.Element(w + "pStyle")) is { Length: > 0 } styleId)
                    styles.TryAdd(styleId, ilvl);
            }

            string? name = DocxValueReader.WordValue(abstractNum.Element(w + "name"));
            string? styleLink = DocxValueReader.WordValue(abstractNum.Element(w + "styleLink"));
            string? numStyleLink = DocxValueReader.WordValue(abstractNum.Element(w + "numStyleLink"));
            abstracts[abstractId] = new AbstractEntry(levels, indents, styles, name, numStyleLink);
            if (!string.IsNullOrEmpty(styleLink))
                styleLinks.TryAdd(styleLink, abstractId);
        }

        // One definition per abstract numbering, the linked ones sharing the
        // definition of the abstract numbering they link to.
        var definitions = new Dictionary<int, (ListDefinition Definition, AbstractEntry Entry)>();
        foreach ((int id, AbstractEntry entry) in abstracts)
        {
            AbstractEntry source = entry;
            if (entry.NumStyleLink is { Length: > 0 } link &&
                styleLinks.TryGetValue(link, out int linkedId) &&
                linkedId != id &&
                abstracts.TryGetValue(linkedId, out AbstractEntry? linked))
            {
                source = linked;
            }

            definitions[id] = (source.Definition, source);
        }

        var nums = new Dictionary<int, NumEntry>();
        foreach (XElement num in root.Elements(w + "num"))
        {
            if (!DocxValueReader.TryReadInt(num.Attribute(w + "numId"), out int numId))
                continue;
            if (!DocxValueReader.TryReadInt(num.Element(w + "abstractNumId")?.Attribute(w + "val"), out int abstractId))
                continue;
            if (!definitions.TryGetValue(abstractId, out (ListDefinition Definition, AbstractEntry Entry) found))
                continue;

            ListDefinition definition = found.Definition;
            LevelIndent?[] indents = found.Entry.Indents;
            foreach (XElement lvlOverride in num.Elements(w + "lvlOverride"))
            {
                if (!DocxValueReader.TryReadInt(lvlOverride.Attribute(w + "ilvl"), out int ilvl) ||
                    ilvl < 0 || ilvl >= ListDefinition.MaxLevels)
                {
                    continue;
                }

                ListLevel level = definition.LevelAt(ilvl);
                bool changed = false;
                if (lvlOverride.Element(w + "lvl") is XElement replacement)
                {
                    changed = true;
                    level = ReadLevel(replacement, ilvl, ref reportedFormat, diagnostics);
                    if (ReadIndent(replacement.Element(w + "pPr")?.Element(w + "ind")) is LevelIndent indent)
                    {
                        indents = (LevelIndent?[])indents.Clone();
                        indents[ilvl] = indent;
                    }
                }

                if (DocxValueReader.TryReadInt(lvlOverride.Element(w + "startOverride")?.Attribute(w + "val"), out int start))
                {
                    level = level with { Start = start };
                    changed = true;
                }

                // Each change makes a new definition, and so a list counted
                // apart from the abstract numbering's other lists.
                if (changed)
                    definition = definition.WithLevel(ilvl, level);
            }

            nums[numId] = new NumEntry(definition, indents, found.Entry.Styles);
        }

        return new DocxNumbering(nums);
    }

    /// <summary>
    /// The list <paramref name="numId"/> names at <paramref name="ilvl"/>, or at
    /// the level whose <c>w:pStyle</c> is <paramref name="styleId"/> when the
    /// paragraph names no level, or null when the document defines no such list.
    /// </summary>
    public ListReference? Reference(int numId, int? ilvl, string? styleId)
    {
        if (!_nums.TryGetValue(numId, out NumEntry? entry))
            return null;

        int level = ilvl ?? (styleId is not null && entry.Styles.TryGetValue(styleId, out int styled) ? styled : 0);
        return new ListReference(entry.Definition, Math.Clamp(level, 0, ListDefinition.MaxLevels - 1));
    }

    /// <summary>The indent a list's level states for its paragraphs, in twips, or null when it states none.</summary>
    public LevelIndent? IndentFor(int numId, int level) =>
        _nums.TryGetValue(numId, out NumEntry? entry) && level >= 0 && level < entry.Indents.Length
            ? entry.Indents[level]
            : null;

    /// <summary>
    /// The kind a list with no definition here draws: a bullet, which is what a
    /// paragraph naming a list the document never defined has always been read as.
    /// </summary>
    public ListKind KindFor(int numId) =>
        _nums.TryGetValue(numId, out NumEntry? entry) ? new ListReference(entry.Definition, 0).Kind : ListKind.Bullet;

    private static ListLevel ReadLevel(XElement lvl, int ilvl, ref bool reportedFormat, List<DocumentDiagnostic>? diagnostics)
    {
        XNamespace w = DocxNamespaces.Wordprocessing;
        string? numFmt = DocxValueReader.WordValue(lvl.Element(w + "numFmt"));
        ListNumberFormat format = numFmt switch
        {
            null or "decimal" => ListNumberFormat.Decimal,
            "decimalZero" => ListNumberFormat.DecimalZero,
            "lowerLetter" => ListNumberFormat.LowerLetter,
            "upperLetter" => ListNumberFormat.UpperLetter,
            "lowerRoman" => ListNumberFormat.LowerRoman,
            "upperRoman" => ListNumberFormat.UpperRoman,
            "bullet" => ListNumberFormat.Bullet,
            "none" => ListNumberFormat.None,
            _ => ListNumberFormat.Decimal,
        };

        if (numFmt is not null && format == ListNumberFormat.Decimal && numFmt != "decimal" && !reportedFormat)
        {
            diagnostics?.Add(DocumentDiagnostic.Info(
                "docx.numbering.format",
                "A DOCX list level numbered in " + numFmt + " was numbered in decimal; only decimal, letters and roman numerals are drawn as such."));
            reportedFormat = true;
        }

        // ECMA-376 §17.9.25 makes an absent w:start nought, but LibreOffice
        // starts such a level at one, as Word's own lists always state.
        int start = DocxValueReader.TryReadInt(lvl.Element(w + "start")?.Attribute(w + "val"), out int value) ? value : 1;

        XElement? lvlText = lvl.Element(w + "lvlText");
        string template = (string?)lvlText?.Attribute(w + "val") ?? (format == ListNumberFormat.Bullet ? string.Empty : "%" + (ilvl + 1) + ".");
        if (format == ListNumberFormat.Bullet)
            template = BulletText(template);

        int? restartAfter = null;
        if (DocxValueReader.TryReadInt(lvl.Element(w + "lvlRestart")?.Attribute(w + "val"), out int restart))
            restartAfter = restart <= 0 ? -1 : restart - 1;

        XElement? isLgl = lvl.Element(w + "isLgl");
        bool legal = isLgl is not null && DocxValueReader.WordValue(isLgl) is not ("false" or "0" or "off");

        return new ListLevel
        {
            Format = format,
            Template = template,
            Start = start,
            Legal = legal,
            RestartAfter = restartAfter,
        };
    }

    private static LevelIndent? ReadIndent(XElement? ind)
    {
        if (ind is null)
            return null;

        XNamespace w = DocxNamespaces.Wordprocessing;
        int? left = DocxValueReader.TryReadInt(ind.Attribute(w + "start"), out int start) ||
            DocxValueReader.TryReadInt(ind.Attribute(w + "left"), out start)
            ? start
            : null;
        int? hanging = DocxValueReader.TryReadInt(ind.Attribute(w + "hanging"), out int hang)
            ? hang
            : DocxValueReader.TryReadInt(ind.Attribute(w + "firstLine"), out int firstLine) ? -firstLine : null;
        return left is null && hanging is null ? null : new LevelIndent(left, hanging);
    }

    /// <summary>
    /// A bullet's text as Unicode. Word writes the common bullets as code points
    /// in the private-use area of the Symbol and Wingdings fonts, which draw as
    /// nothing in any other face, so the ones those fonts are used for are
    /// mapped to the character each draws.
    /// </summary>
    private static string BulletText(string text)
    {
        if (text.Length == 0)
            return ((char)0x2022).ToString();

        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                (char)0xF0B7 => (char)0x2022, // Symbol: bullet
                (char)0xF0A7 => (char)0x25AA, // Wingdings: small square
                (char)0xF0A8 => (char)0x25A1, // Wingdings: white square
                (char)0xF06E => (char)0x25A0, // Wingdings: black square
                (char)0xF0D8 => (char)0x27A2, // Wingdings: arrowhead
                (char)0xF076 => (char)0x2756, // Wingdings: diamond
                (char)0xF0FC => (char)0x2713, // Wingdings: check mark
                (char)0xF02D => '-',          // Symbol: minus
                >= (char)0xF000 and <= (char)0xF0FF => (char)0x2022,
                _ => chars[i],
            };
        }

        return new string(chars);
    }

    /// <summary>A level's indent: where its text starts, and how far its first line hangs back from there.</summary>
    internal readonly record struct LevelIndent(int? Left, int? Hanging);

    private sealed class AbstractEntry(
        ListLevel?[] levels,
        LevelIndent?[] indents,
        Dictionary<string, int> styles,
        string? name,
        string? numStyleLink)
    {
        public ListDefinition Definition { get; } = new(levels.Select((level, i) => level ?? ListLevel.DecimalAt(i)).ToArray(), name);

        public LevelIndent?[] Indents { get; } = indents;

        public Dictionary<string, int> Styles { get; } = styles;

        public string? NumStyleLink { get; } = numStyleLink;
    }

    private sealed record NumEntry(ListDefinition Definition, LevelIndent?[] Indents, Dictionary<string, int> Styles);
}
