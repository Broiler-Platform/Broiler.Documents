using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>Refreshes the cached text of controls bound to built-in core properties.</summary>
internal static class DocxCoreBindings
{
    private static readonly XNamespace Word = DocxNamespaces.Wordprocessing;
    private static readonly Guid CoreStore = new("6C3C8BC8-F283-45AE-878A-BAB7291924A1");

    public static void Apply(XDocument document, ZipArchive archive, DocumentLimits limits)
    {
        XElement? core = null;
        bool loaded = false;
        long expansionBudget = limits.MaxDocumentBytes;
        foreach (XElement binding in document.Descendants(Word + "dataBinding").ToArray())
        {
            XElement? properties = binding.Parent;
            XElement? content = properties?.Parent?.Element(Word + "sdtContent");
            if (properties?.Name != Word + "sdtPr" || content is null || properties.Element(Word + "date") is not null ||
                !Guid.TryParse((string?)binding.Attribute(Word + "storeItemID"), out Guid store) || store != CoreStore)
                continue;

            // A text control can contain a single paragraph or an inline story.
            // Keep structured controls (such as a repeating table row) intact.
            XElement? paragraph = content.Element(Word + "p");
            XElement target = paragraph ?? content;
            if ((paragraph is not null && content.Elements().Skip(1).Any()) ||
                target.Elements().Any(child => child.Name != Word + "r" && child.Name != Word + "pPr"))
                continue;

            if (!loaded)
            {
                loaded = true;
                if (DocxPackage.FindEntry(archive, DocxMetadata.CorePart) is { } entry)
                    core = DocxPackage.LoadEntryXml(entry, limits, [], "docx.binding.xml")?.Root;
            }

            if (core is null || Resolve(core, binding) is not XElement { HasElements: false } value)
                continue;

            string text = value.Value;
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

    private static XElement? Resolve(XElement core, XElement binding)
    {
        // Built-in properties use absolute, two-step child paths. Resolve their
        // namespace prefixes without evaluating arbitrary package-supplied XPath.
        string? xpath = (string?)binding.Attribute(Word + "xpath");
        if (xpath is null)
            return null;
        Match path = Regex.Match(xpath, @"\A/([\w.-]+):([\w.-]+)(?:\[1\])?/([\w.-]+):([\w.-]+)(?:\[1\])?\z",
            RegexOptions.NonBacktracking);
        if (!path.Success)
            return null;

        string mappings = (string?)binding.Attribute(Word + "prefixMappings") ?? "";
        var prefixes = new Dictionary<string, XNamespace>(StringComparer.Ordinal);
        foreach (Match mapping in Regex.Matches(mappings, @"xmlns:([\w.-]+)\s*=\s*(?:'([^']+)'|""([^""]+)"")",
            RegexOptions.NonBacktracking))
        {
            prefixes[mapping.Groups[1].Value] = mapping.Groups[2].Success ? mapping.Groups[2].Value : mapping.Groups[3].Value;
        }

        return prefixes.TryGetValue(path.Groups[1].Value, out XNamespace? rootNamespace) &&
            core.Name == rootNamespace + path.Groups[2].Value &&
            prefixes.TryGetValue(path.Groups[3].Value, out XNamespace? valueNamespace)
            ? core.Element(valueNamespace + path.Groups[4].Value)
            : null;
    }
}
