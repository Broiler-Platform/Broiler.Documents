using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// Reads the picture markup a run can hold — DrawingML (<c>w:drawing</c>) and
/// the legacy VML shape (<c>w:pict</c>) — into <see cref="InlineImage"/> values,
/// loading each referenced <c>word/media</c> part at most once.
/// </summary>
internal sealed class DocxImageLoader
{
    /// <summary>English Metric Units per point: 914400 per inch over 72 points per inch.</summary>
    private const double EmusPerPoint = 12700.0;

    /// <summary>
    /// A ceiling on what one picture may draw as. Word stores the display size,
    /// and a corrupt or hostile extent would otherwise ask the layout for a line
    /// millions of units tall.
    /// </summary>
    private const double MaxDisplayExtent = 20000.0;

    private readonly ZipArchive _archive;
    private readonly DocumentLimits _limits;
    private readonly Dictionary<string, MediaPart?> _parts = new(StringComparer.OrdinalIgnoreCase);

    public DocxImageLoader(ZipArchive archive, DocumentLimits limits)
    {
        _archive = archive;
        _limits = limits;
    }

    /// <summary>How many pictures this read turned into inline images.</summary>
    public int ImageCount { get; private set; }

    /// <summary>
    /// Reads a <c>w:drawing</c> or <c>w:pict</c> element. Returns null — after
    /// recording why — when the element holds no picture this codec can carry.
    /// </summary>
    public InlineImage? Read(
        XElement element,
        DocxRelationships relationships,
        IDocxImageDiagnostics diagnostics)
    {
        InlineImage? image = element.Name == DocxNamespaces.Wordprocessing + "drawing"
            ? ReadDrawing(element, relationships, diagnostics)
            : ReadVmlPicture(element, relationships, diagnostics);

        if (image is not null)
            ImageCount++;

        return image;
    }

    /// <summary>
    /// Reads the DrawingML shape: <c>wp:inline</c> or <c>wp:anchor</c>, then the
    /// <c>pic:pic</c> under its graphic data. Anchored (floating) pictures are
    /// read as inline ones — the model has no float, and dropping them would lose
    /// the logo at the top of most letterhead templates.
    /// </summary>
    private InlineImage? ReadDrawing(
        XElement drawing,
        DocxRelationships relationships,
        IDocxImageDiagnostics diagnostics)
    {
        foreach (XElement wrapper in drawing.Elements())
        {
            bool isInline = wrapper.Name == DocxNamespaces.WordDrawing + "inline";
            bool isAnchor = wrapper.Name == DocxNamespaces.WordDrawing + "anchor";
            if (!isInline && !isAnchor)
                continue;

            if (isAnchor)
            {
                diagnostics.AddDiagnosticOnce(
                    "docx.image.anchored",
                    "A floating DOCX picture was placed inline; text wrapping is not represented.");
            }

            XElement? blip = FindDescendant(wrapper, DocxNamespaces.Drawing + "blip");
            if (blip is null)
            {
                diagnostics.AddDiagnosticOnce(
                    "docx.image.shape",
                    "A DOCX drawing held no embedded picture and was skipped.");
                return null;
            }

            string? relationshipId = (string?)blip.Attribute(DocxNamespaces.Relationships + "embed");
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                // r:link points at an image outside the package. Reading it would
                // be a network or file fetch from document content (ADR 0004).
                diagnostics.AddDiagnosticOnce(
                    "docx.image.external",
                    "A DOCX picture linked to an external image, which is not fetched.");
                return null;
            }

            (double width, double height) = ReadExtent(wrapper);
            (string? altText, string? name) = ReadDescription(wrapper);
            return Load(relationshipId, relationships, width, height, altText, name, diagnostics);
        }

        diagnostics.AddDiagnosticOnce(
            "docx.image.shape",
            "A DOCX drawing held no embedded picture and was skipped.");
        return null;
    }

    /// <summary>
    /// Reads the legacy VML picture: <c>w:pict/v:shape/v:imagedata</c>, sized by
    /// the shape's CSS <c>style</c>. Word wrote this shape for images before
    /// DrawingML and still writes it inside fields and some headers.
    /// </summary>
    private InlineImage? ReadVmlPicture(
        XElement pict,
        DocxRelationships relationships,
        IDocxImageDiagnostics diagnostics)
    {
        XElement? imageData = FindDescendant(pict, DocxNamespaces.Vml + "imagedata");
        if (imageData is null)
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.shape",
                "A DOCX drawing held no embedded picture and was skipped.");
            return null;
        }

        string? relationshipId = (string?)imageData.Attribute(DocxNamespaces.Relationships + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.external",
                "A DOCX picture linked to an external image, which is not fetched.");
            return null;
        }

        XElement? shape = imageData.Parent;
        (double width, double height) = ReadVmlStyleExtent((string?)shape?.Attribute("style"));
        string? altText = (string?)shape?.Attribute("alt") ?? (string?)imageData.Attribute("title");
        string? name = (string?)shape?.Attribute("id");
        return Load(relationshipId, relationships, width, height, altText, name, diagnostics);
    }

    private InlineImage? Load(
        string relationshipId,
        DocxRelationships relationships,
        double width,
        double height,
        string? altText,
        string? name,
        IDocxImageDiagnostics diagnostics)
    {
        if (!relationships.TryGet(relationshipId, out DocxRelationship? relationship) || relationship is null)
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.relationship",
                "A DOCX picture named a relationship the package does not define.");
            return null;
        }

        if (relationship.TargetModeExternal)
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.external",
                "A DOCX picture linked to an external image, which is not fetched.");
            return null;
        }

        MediaPart? part = LoadPart(relationship.Target, diagnostics);
        if (part is null)
            return null;

        return new InlineImage(
            part.Data,
            part.ContentType,
            width,
            height,
            altText,
            string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(relationship.Target) : name);
    }

    /// <summary>
    /// Loads a media part's bytes, once per part path. A part that fails — too
    /// large, missing, or not a raster format — is cached as a failure so a
    /// document that references it a hundred times reports once and does not
    /// re-read the entry a hundred times.
    /// </summary>
    private MediaPart? LoadPart(string partPath, IDocxImageDiagnostics diagnostics)
    {
        if (_parts.TryGetValue(partPath, out MediaPart? cached))
            return cached;

        MediaPart? part = ReadPart(partPath, diagnostics);
        _parts[partPath] = part;
        return part;
    }

    private MediaPart? ReadPart(string partPath, IDocxImageDiagnostics diagnostics)
    {
        ZipArchiveEntry? entry = DocxPackage.FindEntry(_archive, partPath);
        if (entry is null)
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.missing",
                "A DOCX picture referenced a media part the package does not contain.");
            return null;
        }

        if (entry.Length > _limits.MaxBinBytes)
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.limit",
                "A DOCX image part exceeded MaxBinBytes and was skipped.");
            return null;
        }

        byte[]? data = ReadEntryBytes(entry, _limits.MaxBinBytes);
        if (data is null)
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.limit",
                "A DOCX image part exceeded MaxBinBytes and was skipped.");
            return null;
        }

        string? contentType =
            DocxImageFormats.ContentTypeForExtension(Path.GetExtension(partPath)) ??
            DocxImageFormats.ContentTypeForSignature(data);
        if (contentType is null)
        {
            // EMF/WMF metafiles land here, as does anything Word stored under an
            // extension this codec does not decode.
            diagnostics.AddDiagnosticOnce(
                "docx.image.format",
                "A DOCX picture used an image format this codec does not carry and was skipped.");
            return null;
        }

        return new MediaPart(data, contentType);
    }

    /// <summary>
    /// Reads an entry with the limit applied to what actually decompresses, not
    /// to the size its ZIP header claims — a compressed part can lie about its
    /// length (ADR 0004).
    /// </summary>
    private static byte[]? ReadEntryBytes(ZipArchiveEntry entry, long maxBytes)
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
    /// The display size of a drawing, from <c>wp:extent</c> (EMUs). A missing or
    /// unusable extent yields zero, which means "draw at the image's own size".
    /// </summary>
    private static (double Width, double Height) ReadExtent(XElement wrapper)
    {
        XElement? extent = wrapper.Element(DocxNamespaces.WordDrawing + "extent") ??
            FindDescendant(wrapper, DocxNamespaces.Drawing + "ext");
        if (extent is null)
            return (0, 0);

        double width = EmuToPoints((string?)extent.Attribute("cx"));
        double height = EmuToPoints((string?)extent.Attribute("cy"));
        return (width, height);
    }

    private static double EmuToPoints(string? value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long emus) || emus <= 0)
            return 0;

        return Math.Min(emus / EmusPerPoint, MaxDisplayExtent);
    }

    /// <summary>
    /// The alternative text and name of a drawing, from <c>wp:docPr</c> — the
    /// same pair Word's "Edit Alt Text" writes.
    /// </summary>
    private static (string? AltText, string? Name) ReadDescription(XElement wrapper)
    {
        XElement? docPr = wrapper.Element(DocxNamespaces.WordDrawing + "docPr") ??
            FindDescendant(wrapper, DocxNamespaces.Picture + "cNvPr");
        if (docPr is null)
            return (null, null);

        return ((string?)docPr.Attribute("descr"), (string?)docPr.Attribute("name"));
    }

    /// <summary>
    /// The display size of a VML shape, from the CSS lengths in its
    /// <c>style</c> attribute.
    /// </summary>
    private static (double Width, double Height) ReadVmlStyleExtent(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
            return (0, 0);

        double width = 0;
        double height = 0;
        foreach (string declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = declaration.IndexOf(':');
            if (colon <= 0)
                continue;

            string property = declaration[..colon].Trim();
            string value = declaration[(colon + 1)..].Trim();
            if (property.Equals("width", StringComparison.OrdinalIgnoreCase))
                width = CssLengthToPoints(value);
            else if (property.Equals("height", StringComparison.OrdinalIgnoreCase))
                height = CssLengthToPoints(value);
        }

        return (width, height);
    }

    /// <summary>
    /// Converts a VML CSS length to points. VML's own default unit is the point,
    /// which is what a bare number means here.
    /// </summary>
    private static double CssLengthToPoints(string value)
    {
        int end = 0;
        while (end < value.Length && (char.IsAsciiDigit(value[end]) || value[end] is '.' or '-' or '+'))
            end++;

        if (end == 0 ||
            !double.TryParse(value[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
            number <= 0)
        {
            return 0;
        }

        double points = value[end..].Trim().ToLowerInvariant() switch
        {
            "pt" or "" => number,
            "in" => number * 72,
            "cm" => number * 72 / 2.54,
            "mm" => number * 72 / 25.4,
            "pc" => number * 12,
            "px" => number * 72 / 96,
            _ => 0,
        };

        return Math.Min(points, MaxDisplayExtent);
    }

    /// <summary>
    /// The first descendant with <paramref name="name"/>. Picture markup nests
    /// several levels deep through namespaces the reader does not otherwise
    /// model, and matching the leaf is more robust than walking every wrapper.
    /// </summary>
    private static XElement? FindDescendant(XElement root, XName name)
    {
        foreach (XElement descendant in root.Descendants(name))
            return descendant;

        return null;
    }

    private sealed record MediaPart(byte[] Data, string ContentType);
}

/// <summary>The once-per-code diagnostic sink the image loader reports through.</summary>
internal interface IDocxImageDiagnostics
{
    void AddDiagnosticOnce(string code, string message);
}
