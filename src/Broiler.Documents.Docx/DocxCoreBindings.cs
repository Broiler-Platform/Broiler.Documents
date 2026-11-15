using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// Refreshes the cached text of content controls bound to a data store: the
/// core properties, the extended (application) properties and the package's
/// custom XML parts.
/// </summary>
/// <remarks>
/// LibreOffice's import (writerfilter's <c>SdtHelper::getValueFromDataBinding</c>)
/// loads those stores and evaluates a control's <c>w:dataBinding</c> XPath in
/// the store its <c>w:storeItemID</c> names, or, without one, in each store in
/// turn until one has a match. LibreOffice 24.2's text export shows the bound
/// value, not the text Word saved: tdf104823.docx's "True Extended Property
/// Value" and "True Custom XML Value" over a saved "INVALID VALUE", and
/// customxml-two-data-stores.docx's two controls bound by one path to two
/// stores, "ABC" and "HERUNTERLADEN".
/// </remarks>
internal static class DocxCoreBindings
{
    private static readonly XNamespace Word = DocxNamespaces.Wordprocessing;
    private static readonly XNamespace DataStore = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly Guid CoreStore = new("6C3C8BC8-F283-45AE-878A-BAB7291924A1");
    private static readonly Guid ExtendedStore = new("6668398D-A668-4E3E-A5EB-62B293D839F1");

    /// <summary>The longest binding path evaluated; Word writes paths of a few hundred characters.</summary>
    private const int MaxPathLength = 2048;

    /// <summary>The most custom XML parts loaded as stores.</summary>
    private const int MaxCustomStores = 64;

    public static void Apply(XDocument document, ZipArchive archive, DocumentLimits limits)
    {
        List<(Guid Id, XElement Root)>? stores = null;
        long expansionBudget = limits.MaxDocumentBytes;
        foreach (XElement binding in document.Descendants(Word + "dataBinding").ToArray())
        {
            XElement? properties = binding.Parent;
            XElement? content = properties?.Parent?.Element(Word + "sdtContent");
            if (properties?.Name != Word + "sdtPr" || content is null || properties.Element(Word + "date") is not null)
                continue;

            Guid? store = Guid.TryParse((string?)binding.Attribute(Word + "storeItemID"), out Guid id) ? id : null;
            if (store is null && binding.Attribute(Word + "storeItemID") is not null)
                continue;

            // A text control can contain a single paragraph or an inline story.
            // Keep structured controls (such as a repeating table row) intact.
            // Proofing and bookmark marks are not content (tdf104823.docx
            // closes a spelling mark inside the control).
            XElement? paragraph = content.Element(Word + "p");
            XElement target = paragraph ?? content;
            if ((paragraph is not null && content.Elements().Skip(1).Any()) ||
                target.Elements().Any(child => child.Name.Namespace != Word ||
                    child.Name.LocalName is not ("r" or "pPr" or "proofErr" or "bookmarkStart" or "bookmarkEnd")))
                continue;

            stores ??= LoadStores(archive, limits);
            string? text = null;
            foreach ((Guid storeId, XElement root) in stores)
            {
                if (store is Guid wanted && storeId != wanted)
                    continue;
                if (Resolve(root, binding) is string value)
                {
                    text = value;
                    break;
                }
            }

            if (text is null)
                continue;

            // An empty built-in property can coexist with a populated cached
            // control. Keep the cached display text in that case.
            if (text.Length == 0 && target.Descendants(Word + "t").Any(run => run.Value.Length > 0))
                continue;
            if (text.Length > limits.MaxRunLength)
                continue;
            // Bound the extra nodes and text even if many controls reference
            // the same value or the value contains many empty lines.
            long cost = text.Length * 2L + 128L * (1 + text.Count(c => c is '\r' or '\n'));
            if (cost > expansionBudget)
                continue;
            expansionBudget -= cost;

            XElement? runProperties = target.Element(Word + "r")?.Element(Word + "rPr");
            var run = new XElement(Word + "r", runProperties);
            string[] lines = text.ReplaceLineEndings("\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    run.Add(new XElement(Word + "br"));
                run.Add(new XElement(Word + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[i]));
            }

            target.Elements(Word + "r").Remove();
            target.Add(run);
        }
    }

    /// <summary>
    /// The data stores in the order LibreOffice searches them: the core
    /// properties, the extended properties, then each custom XML part with the
    /// item ID its properties part gives it.
    /// </summary>
    private static List<(Guid Id, XElement Root)> LoadStores(ZipArchive archive, DocumentLimits limits)
    {
        var stores = new List<(Guid, XElement)>();
        void Add(Guid id, string path)
        {
            if (DocxPackage.FindEntry(archive, path) is { } entry &&
                DocxPackage.LoadEntryXml(entry, limits, [], "docx.binding.xml")?.Root is XElement root)
                stores.Add((id, root));
        }

        Add(CoreStore, DocxMetadata.CorePart);
        Add(ExtendedStore, DocxMetadata.AppPart);

        var items = archive.Entries
            .Select(entry => (Entry: entry, Match: Regex.Match(entry.FullName, @"\AcustomXml/item(\d{1,6})\.xml\z", RegexOptions.IgnoreCase)))
            .Where(item => item.Match.Success)
            .OrderBy(item => int.Parse(item.Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Take(MaxCustomStores);
        foreach ((ZipArchiveEntry entry, Match match) in items)
        {
            if (ItemId(archive, entry.FullName, match.Groups[1].Value, limits) is Guid id)
                Add(id, entry.FullName);
        }

        return stores;
    }

    /// <summary>The <c>ds:itemID</c> of a custom XML part, from the properties part its relationships name.</summary>
    private static Guid? ItemId(ZipArchive archive, string itemPath, string number, DocumentLimits limits)
    {
        string propertiesPath = "customXml/itemProps" + number + ".xml";
        string relationshipsPath = "customXml/_rels/" + itemPath["customXml/".Length..] + ".rels";
        if (DocxPackage.FindEntry(archive, relationshipsPath) is { } relsEntry &&
            DocxPackage.LoadEntryXml(relsEntry, limits, [], "docx.binding.xml")?.Root is XElement rels &&
            rels.Elements(Relationships + "Relationship").Select(relationship => (string?)relationship.Attribute("Target"))
                .FirstOrDefault(target => target is not null && target.Contains("itemProps", StringComparison.OrdinalIgnoreCase)) is string target)
        {
            propertiesPath = "customXml/" + target.TrimStart('/').Replace("customXml/", "", StringComparison.OrdinalIgnoreCase);
        }

        return DocxPackage.FindEntry(archive, propertiesPath) is { } entry &&
            DocxPackage.LoadEntryXml(entry, limits, [], "docx.binding.xml")?.Root?.Attribute(DataStore + "itemID") is XAttribute itemId &&
            Guid.TryParse(itemId.Value, out Guid id)
            ? id
            : null;
    }

    /// <summary>
    /// The text <paramref name="binding"/>'s path selects in <paramref name="root"/>:
    /// an attribute's value or a leaf element's text, or null where it selects
    /// nothing or something with element children.
    /// </summary>
    /// <remarks>
    /// Only absolute paths of child steps are worked out, which is what Word
    /// writes: each step a name, <c>prefix:name</c> or <c>*</c>, with predicates
    /// that are a position, <c>name()='qname'</c> or <c>@attribute='value'</c>
    /// joined by <c>and</c>, and a last step that may be an attribute. The
    /// package's XPath is not handed to an XPath engine.
    /// </remarks>
    internal static string? Resolve(XElement root, XElement binding)
    {
        string? xpath = (string?)binding.Attribute(Word + "xpath");
        if (xpath is null || xpath.Length > MaxPathLength || !xpath.StartsWith('/'))
            return null;

        string mappings = (string?)binding.Attribute(Word + "prefixMappings") ?? "";
        var prefixes = new Dictionary<string, XNamespace>(StringComparer.Ordinal);
        foreach (Match mapping in Regex.Matches(mappings, @"xmlns:([\w.-]+)\s*=\s*(?:'([^']*)'|""([^""]*)"")",
            RegexOptions.NonBacktracking))
        {
            prefixes[mapping.Groups[1].Value] = mapping.Groups[2].Success ? mapping.Groups[2].Value : mapping.Groups[3].Value;
        }

        List<string>? steps = SplitSteps(xpath);
        if (steps is null || steps.Count == 0)
            return null;

        // The first step is matched against the store's root element itself.
        IEnumerable<XElement> current = [root];
        for (int i = 0; i < steps.Count; i++)
        {
            Match step = Regex.Match(steps[i], @"\A(@?)(\*|[\w.-]+(?::[\w.-]+)?)((?:\[[^\[\]]*\])*)\z", RegexOptions.NonBacktracking);
            if (!step.Success)
                return null;

            bool attribute = step.Groups[1].Length > 0;
            string name = step.Groups[2].Value;
            List<string> predicates = [.. Regex.Matches(step.Groups[3].Value, @"\[([^\[\]]*)\]", RegexOptions.NonBacktracking)
                .Select(predicate => predicate.Groups[1].Value)];
            if (attribute)
            {
                if (i == 0 || i != steps.Count - 1 || predicates.Count > 0 || QualifiedName(name, prefixes) is not XName attributeName)
                    return null;
                return current.Select(element => element.Attribute(attributeName)).FirstOrDefault(found => found is not null)?.Value;
            }

            XName? elementName = name == "*" ? null : QualifiedName(name, prefixes);
            if (name != "*" && elementName is null)
                return null;

            var selected = new List<XElement>();
            foreach (XElement parent in current)
            {
                IEnumerable<XElement> children = i == 0 ? [parent] : parent.Elements();
                List<XElement> candidates = [.. children.Where(child => elementName is null || child.Name == elementName)];
                foreach (string predicate in predicates)
                {
                    candidates = Filter(candidates, predicate, prefixes);
                    if (candidates.Count == 0)
                        break;
                }

                selected.AddRange(candidates);
            }

            current = selected;
        }

        return current.FirstOrDefault() is XElement { HasElements: false } leaf ? leaf.Value : null;
    }

    /// <summary>The steps of an absolute path, or null where a predicate is not closed.</summary>
    private static List<string>? SplitSteps(string xpath)
    {
        var steps = new List<string>();
        int depth = 0;
        char quote = '\0';
        int start = 1;
        for (int i = 1; i < xpath.Length; i++)
        {
            char c = xpath[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
            }
            else if (c is '\'' or '"')
                quote = c;
            else if (c == '[')
                depth++;
            else if (c == ']')
                depth--;
            else if (c == '/' && depth == 0)
            {
                if (i == start)
                    return null; // "//" is not a child step
                steps.Add(xpath[start..i].Trim());
                start = i + 1;
            }
        }

        if (depth != 0 || quote != '\0' || start >= xpath.Length)
            return null;
        steps.Add(xpath[start..].Trim());
        return steps;
    }

    private static List<XElement> Filter(List<XElement> candidates, string predicate, Dictionary<string, XNamespace> prefixes)
    {
        predicate = predicate.Trim();
        if (int.TryParse(predicate, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int position))
            return position >= 1 && position <= candidates.Count ? [candidates[position - 1]] : [];

        var tests = new List<Func<XElement, bool>>();
        foreach (string term in Regex.Split(predicate, @"\s+and\s+", RegexOptions.NonBacktracking))
        {
            Match test = Regex.Match(term.Trim(), @"\A(?:name\(\)|@([\w.-]+(?::[\w.-]+)?))\s*=\s*(?:'([^']*)'|""([^""]*)"")\z",
                RegexOptions.NonBacktracking);
            if (!test.Success)
                return [];
            string expected = test.Groups[2].Success ? test.Groups[2].Value : test.Groups[3].Value;
            if (test.Groups[1].Success)
            {
                if (QualifiedName(test.Groups[1].Value, prefixes) is not XName attributeName)
                    return [];
                tests.Add(element => (string?)element.Attribute(attributeName) == expected);
            }
            else
            {
                // name() is the name as the store spells it, its own prefix
                // included, whatever namespace the binding maps that prefix to.
                tests.Add(element => SourceName(element) == expected);
            }
        }

        return [.. candidates.Where(candidate => tests.TrueForAll(test => test(candidate)))];
    }

    private static string SourceName(XElement element)
    {
        string? prefix = element.Name.Namespace == XNamespace.None ? null : element.GetPrefixOfNamespace(element.Name.Namespace);
        return string.IsNullOrEmpty(prefix) ? element.Name.LocalName : prefix + ":" + element.Name.LocalName;
    }

    private static XName? QualifiedName(string name, Dictionary<string, XNamespace> prefixes)
    {
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return XNamespace.None + name;
        return prefixes.TryGetValue(name[..colon], out XNamespace? ns) ? ns + name[(colon + 1)..] : null;
    }
}
