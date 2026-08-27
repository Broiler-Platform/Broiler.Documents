using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// Open Packaging Conventions plumbing shared by the DOCX parts: locating an
/// entry, loading it as XML within the read limits, resolving relationship
/// targets, and normalizing package paths.
/// </summary>
internal static class DocxPackage
{
    public static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        string normalized = path.TrimStart('/').Replace('\\', '/');
        return archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Replace('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static XDocument? LoadEntryXml(
        ZipArchiveEntry entry,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        string diagnosticCode)
    {
        if (entry.Length > limits.MaxBinBytes)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                diagnosticCode + ".limit",
                "A DOCX XML part exceeded MaxBinBytes and was skipped."));
            return null;
        }

        try
        {
            using Stream stream = entry.Open();
            return XDocument.Load(stream, LoadOptions.None);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                diagnosticCode,
                "A DOCX XML part could not be parsed: " + ex.GetType().Name + "."));
            return null;
        }
    }

    public static DocxRelationships ReadRelationships(
        ZipArchive archive,
        string path,
        string baseDirectory,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        ZipArchiveEntry? entry = FindEntry(archive, path);
        if (entry is null)
            return DocxRelationships.Empty;

        XDocument? rels = LoadEntryXml(entry, limits, diagnostics, "docx.relationships");
        if (rels?.Root is null)
            return DocxRelationships.Empty;

        var relationships = new List<DocxRelationship>();
        foreach (XElement element in rels.Root.Elements(DocxNamespaces.PackageRelationships + "Relationship"))
        {
            string? id = (string?)element.Attribute("Id");
            string? type = (string?)element.Attribute("Type");
            string? target = (string?)element.Attribute("Target");
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(type) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            bool external = string.Equals((string?)element.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
            string resolved = external ? target : NormalizePackagePath(baseDirectory, target);
            relationships.Add(new DocxRelationship(id, type, resolved, external));
        }

        return new DocxRelationships(relationships);
    }

    /// <summary>The path of the relationships part that belongs to <paramref name="partPath"/>.</summary>
    public static string RelationshipsPartPath(string partPath)
    {
        string normalized = partPath.TrimStart('/').Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        if (slash < 0)
            return "_rels/" + normalized + ".rels";

        return normalized[..slash] + "/_rels/" + normalized[(slash + 1)..] + ".rels";
    }

    public static string BasePartDirectory(string partPath)
    {
        string normalized = partPath.TrimStart('/').Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    public static string NormalizePackagePath(string baseDirectory, string target)
    {
        target = target.Replace('\\', '/');
        if (target.StartsWith("/", StringComparison.Ordinal))
            return target.TrimStart('/');

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(baseDirectory))
            parts.AddRange(baseDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (string part in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join("/", parts);
    }

    /// <summary>
    /// Resolves the path of a part referenced by relationship type, falling back
    /// to the conventional file name beside the main document when the
    /// relationship is absent.
    /// </summary>
    public static string ResolvePartPath(
        DocxRelationships relationships,
        string relationshipType,
        string baseDirectory,
        string conventionalName)
    {
        foreach (DocxRelationship relationship in relationships.All)
        {
            if (relationship.Type.Equals(relationshipType, StringComparison.Ordinal) && !relationship.TargetModeExternal)
                return relationship.Target;
        }

        return NormalizePackagePath(baseDirectory, conventionalName);
    }
}

internal sealed class DocxRelationships
{
    private readonly Dictionary<string, DocxRelationship> _byId;

    public DocxRelationships(IEnumerable<DocxRelationship> relationships)
    {
        All = relationships.ToArray();
        _byId = new Dictionary<string, DocxRelationship>(StringComparer.Ordinal);
        foreach (DocxRelationship relationship in All)
            _byId[relationship.Id] = relationship;
    }

    public static DocxRelationships Empty { get; } = new(Array.Empty<DocxRelationship>());

    public IReadOnlyList<DocxRelationship> All { get; }

    public bool TryGet(string id, out DocxRelationship? relationship) =>
        _byId.TryGetValue(id, out relationship);
}

internal sealed record DocxRelationship(
    string Id,
    string Type,
    string Target,
    bool TargetModeExternal);
