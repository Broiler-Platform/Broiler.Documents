using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// Reads ISO/IEC 29500 Strict packages by renaming their namespaces to the
/// Transitional ones the rest of the reader asks for.
/// </summary>
/// <remarks>
/// <para>
/// Strict is the same WordprocessingML under different namespace names:
/// <c>http://purl.oclc.org/ooxml/wordprocessingml/main</c> where Transitional
/// has <c>http://schemas.openxmlformats.org/wordprocessingml/2006/main</c>, and
/// the same for relationships, DrawingML and the property parts. A reader that
/// asked for <c>w:body</c> by its Transitional name found nothing in a Strict
/// file and reported an empty document. Word saves Strict when a user picks
/// "Strict Open XML Document".
/// </para>
/// <para>
/// Renaming at load, in one place, is what keeps the rest of the reader
/// ignorant of it. The names map by rule: <c>purl.oclc.org/ooxml/X/Y</c> is
/// <c>schemas.openxmlformats.org/X/2006/Y</c>, with two property parts whose
/// last segment is also spelled differently. Relationship types follow the
/// same rule. Content types and the package's own relationship namespace are
/// unchanged in Strict, so they need nothing.
/// </para>
/// <para>
/// What the rename does not cover is the other half of Strict: lengths with a
/// unit (<c>w:before="18pt"</c>) where Transitional writes twips. That is read
/// where the lengths are, by <see cref="DocxValueReader"/>, because
/// Transitional allows the same spelling and a file need not be Strict to use it.
/// </para>
/// </remarks>
internal static class DocxStrict
{
    private const string StrictPrefix = "http://purl.oclc.org/ooxml/";

    private const string TransitionalPrefix = "http://schemas.openxmlformats.org/";

    /// <summary>The two names whose last segment Transitional spells differently.</summary>
    private static readonly Dictionary<string, string> RenamedSegments = new(StringComparer.Ordinal)
    {
        ["extendedProperties"] = "extended-properties",
        ["customProperties"] = "custom-properties",
    };

    /// <summary>
    /// The Transitional name for a Strict namespace or relationship type, or
    /// <paramref name="name"/> itself when it is not a Strict name.
    /// </summary>
    public static string ToTransitional(string name)
    {
        if (!name.StartsWith(StrictPrefix, StringComparison.Ordinal))
            return name;

        string rest = name[StrictPrefix.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
            return name;

        string tail = rest[(slash + 1)..];
        int last = tail.LastIndexOf('/');
        string lastSegment = tail[(last + 1)..];
        if (RenamedSegments.TryGetValue(lastSegment, out string? renamed))
            tail = tail[..(last + 1)] + renamed;

        return TransitionalPrefix + rest[..slash] + "/2006/" + tail;
    }

    /// <summary>
    /// Renames every Strict namespace in <paramref name="document"/> to its
    /// Transitional equivalent, in place. Returns true when it found any.
    /// </summary>
    /// <remarks>
    /// Namespace declarations are renamed too, not just element and attribute
    /// names, so that a prefix written in an attribute value - the ones
    /// <c>mc:Ignorable</c> and <c>mc:Choice Requires</c> list - still resolves
    /// to the namespace the renamed elements are in.
    /// </remarks>
    public static bool Normalize(XDocument document)
    {
        XElement? root = document.Root;
        if (root is null || !UsesStrict(root))
            return false;

        foreach (XElement element in root.DescendantsAndSelf())
        {
            string elementNamespace = element.Name.NamespaceName;
            if (elementNamespace.StartsWith(StrictPrefix, StringComparison.Ordinal))
                element.Name = XNamespace.Get(ToTransitional(elementNamespace)) + element.Name.LocalName;

            if (!element.HasAttributes)
                continue;

            List<XAttribute>? renamed = null;
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    if (attribute.Value.StartsWith(StrictPrefix, StringComparison.Ordinal))
                        attribute.Value = ToTransitional(attribute.Value);
                    continue;
                }

                if (attribute.Name.NamespaceName.StartsWith(StrictPrefix, StringComparison.Ordinal))
                    (renamed ??= []).Add(attribute);
            }

            if (renamed is null)
                continue;

            // An attribute's name cannot be changed, so it is replaced, and the
            // replacements go back in the same order.
            XAttribute[] all = [.. element.Attributes()];
            element.RemoveAttributes();
            foreach (XAttribute attribute in all)
            {
                element.Add(renamed.Contains(attribute)
                    ? new XAttribute(
                        XNamespace.Get(ToTransitional(attribute.Name.NamespaceName)) + attribute.Name.LocalName,
                        attribute.Value)
                    : attribute);
            }
        }

        return true;
    }

    /// <summary>
    /// True when the root is in a Strict namespace or declares one. A Strict
    /// part declares its namespaces on the root, so this is enough to tell
    /// without walking a Transitional part at all.
    /// </summary>
    private static bool UsesStrict(XElement root) =>
        root.Name.NamespaceName.StartsWith(StrictPrefix, StringComparison.Ordinal) ||
        root.Attributes().Any(attribute =>
            attribute.IsNamespaceDeclaration &&
            attribute.Value.StartsWith(StrictPrefix, StringComparison.Ordinal));
}
