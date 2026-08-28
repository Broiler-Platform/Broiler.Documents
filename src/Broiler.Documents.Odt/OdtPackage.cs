using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Broiler.Documents.Odt;

/// <summary>
/// OpenDocument package plumbing: locating an entry, loading it as XML within
/// the read limits, and reading <c>META-INF/manifest.xml</c>.
/// </summary>
/// <remarks>
/// An ODF package is a plain ZIP with fixed part names, not an OPC package: there
/// are no relationship parts and nothing to resolve an id through. A part is
/// found by its normative path (<c>content.xml</c>, <c>styles.xml</c>) and a
/// picture by the path in its <c>xlink:href</c>, so this file is much smaller
/// than its DOCX counterpart. The manifest is read for media types and for the
/// one thing that must stop a read: an encrypted package.
/// </remarks>
internal static class OdtPackage
{
    public static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        string normalized = NormalizePath(path);
        if (normalized.Length == 0)
            return null;

        return archive.Entries.FirstOrDefault(entry =>
            NormalizePath(entry.FullName).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Loads a package entry as XML within the read limits.
    /// <paramref name="loadOptions"/> is <see cref="LoadOptions.PreserveWhitespace"/>
    /// for <c>content.xml</c>: a space between two <c>text:span</c> elements is
    /// document text, and dropping whitespace-only nodes would run the two words
    /// together.
    /// </summary>
    public static XDocument? LoadEntryXml(
        ZipArchiveEntry entry,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        string diagnosticCode,
        LoadOptions loadOptions = LoadOptions.None)
    {
        byte[]? bytes = ReadEntryBytes(entry, limits.MaxBinBytes);
        if (bytes is null)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                diagnosticCode + ".limit",
                "An ODT XML part exceeded MaxBinBytes and was skipped."));
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            // DtdProcessing.Prohibit is the point of going through XmlReader here:
            // an ODF part is untrusted input and an inline DTD is an entity
            // expansion vector (ADR 0004).
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = false,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            return XDocument.Load(reader, loadOptions);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                diagnosticCode,
                "An ODT XML part could not be parsed: " + ex.GetType().Name + "."));
            return null;
        }
    }

    /// <summary>
    /// Reads an entry with the limit applied to what actually decompresses, not
    /// to the size its ZIP header claims — a compressed part can lie about its
    /// length (ADR 0004). Returns null when the limit is hit.
    /// </summary>
    public static byte[]? ReadEntryBytes(ZipArchiveEntry entry, long maxBytes)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        long total = 0;

        while (true)
        {
            int read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0)
                return buffer.ToArray();

            total += read;
            if (total > maxBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }
    }

    /// <summary>
    /// Normalizes a package path the way an <c>xlink:href</c> inside the package
    /// has to be resolved: backslashes to slashes, no leading slash, and
    /// <c>.</c>/<c>..</c> segments removed so a href can never escape the package.
    /// </summary>
    public static string NormalizePath(string path)
    {
        string value = path.Replace('\\', '/');
        var parts = new List<string>();
        foreach (string part in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
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

    public static OdtManifest ReadManifest(
        ZipArchive archive,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        ZipArchiveEntry? entry = FindEntry(archive, OdtNamespaces.ManifestPart);
        if (entry is null)
            return OdtManifest.Empty;

        XDocument? xml = LoadEntryXml(entry, limits, diagnostics, "odt.manifest");
        if (xml?.Root is null)
            return OdtManifest.Empty;

        var mediaTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool encrypted = false;
        foreach (XElement file in xml.Root.Elements(OdtNamespaces.Manifest + "file-entry"))
        {
            string? path = (string?)file.Attribute(OdtNamespaces.Manifest + "full-path");
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string? mediaType = (string?)file.Attribute(OdtNamespaces.Manifest + "media-type");
            if (!string.IsNullOrWhiteSpace(mediaType))
                mediaTypes[NormalizePath(path)] = mediaType.Trim();

            encrypted |= file.Element(OdtNamespaces.Manifest + "encryption-data") is not null;
        }

        return new OdtManifest(mediaTypes, encrypted);
    }
}

/// <summary>What <c>META-INF/manifest.xml</c> says about the package's parts.</summary>
internal sealed class OdtManifest
{
    private readonly Dictionary<string, string> _mediaTypes;

    public OdtManifest(Dictionary<string, string> mediaTypes, bool isEncrypted)
    {
        _mediaTypes = mediaTypes;
        IsEncrypted = isEncrypted;
    }

    public static OdtManifest Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), isEncrypted: false);

    /// <summary>
    /// True when at least one part carries <c>manifest:encryption-data</c>. ODF
    /// password protection really encrypts the parts, so there is nothing to read
    /// and nothing this codec may do about it.
    /// </summary>
    public bool IsEncrypted { get; }

    /// <summary>The declared media type of a part, or null when the manifest does not name it.</summary>
    public string? MediaTypeFor(string path) =>
        _mediaTypes.TryGetValue(OdtPackage.NormalizePath(path), out string? mediaType) ? mediaType : null;
}
