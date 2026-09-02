using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Odt;

/// <summary>
/// Reads the picture markup an ODF text document holds, <c>draw:frame</c>
/// wrapping a <c>draw:image</c>, into <see cref="InlineImage"/> values, loading
/// each referenced package entry at most once.
/// </summary>
/// <remarks>
/// ODF has exactly one picture shape, so this is a good deal simpler than the
/// DOCX loader with its DrawingML-or-VML fork. The two forms a
/// <c>draw:image</c> can take are both handled: an <c>xlink:href</c> into the
/// package (what every producer writes), and an inline
/// <c>office:binary-data</c> payload (what a flattened or clipboard-sourced
/// document carries).
/// </remarks>
internal sealed class OdtImageLoader
{
    private readonly ZipArchive _archive;
    private readonly OdtManifest _manifest;
    private readonly DocumentLimits _limits;
    private readonly DocumentConversionContextBuilder _resources;
    private readonly Dictionary<string, PicturePart?> _parts = new(StringComparer.OrdinalIgnoreCase);

    public OdtImageLoader(
        ZipArchive archive,
        OdtManifest manifest,
        DocumentLimits limits,
        DocumentConversionContextBuilder resources)
    {
        _archive = archive;
        _manifest = manifest;
        _limits = limits;
        _resources = resources;
    }

    /// <summary>How many pictures this read turned into inline images.</summary>
    public int ImageCount { get; private set; }

    /// <summary>
    /// Reads a <c>draw:frame</c>. Returns null, after recording why, when the
    /// frame holds no picture this codec can carry.
    /// </summary>
    public InlineImage? Read(XElement frame, IOdtImageDiagnostics diagnostics)
    {
        XElement? image = FirstImage(frame);
        if (image is null)
        {
            // An object, applet, plugin, or text box frame lands here. A text box
            // is read as block content by the reader, not as a picture.
            diagnostics.AddDiagnosticOnce(
                "odt.image.shape",
                "An ODT frame held no embedded picture and was skipped.");
            return null;
        }

        (double width, double height) = ReadExtent(frame);
        (string? altText, string? name) = ReadDescription(frame);

        PicturePart? part = ReadPicture(image, diagnostics);
        if (part is null)
            return null;

        var picture = new InlineImage(
            part.Data,
            part.ContentType,
            width,
            height,
            altText,
            string.IsNullOrWhiteSpace(name) ? part.Name : name);

        // Constructing an image with reachable bytes is extraction into the
        // model, so the policy decides before the object exists.
        if (!_resources.TryAdmit(
                new DocumentResourceRequest(
                    picture.Resource,
                    DocumentResourceProvenance.ReadFromSource,
                    DocumentResourceDisposition.Embedded,
                    picture.Name,
                    "ODT"),
                DocumentResourceOperations.ExtractToModel,
                out DocumentResourceId id,
                out string? denial))
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.denied",
                "A picture was not read into the document because " + denial + ".");
            return null;
        }

        ImageCount++;
        return picture.WithResourceId(id);
    }

    /// <summary>
    /// The first <c>draw:image</c> in a frame. ODF lets a frame carry several
    /// alternative representations of one picture; the first is the preferred
    /// one, and taking any other would silently prefer a fallback. A clickable
    /// picture wraps the image in a <c>draw:a</c>, which is looked through: the
    /// link belongs to the frame, and the picture is still the picture.
    /// </summary>
    private static XElement? FirstImage(XElement frame)
    {
        foreach (XElement child in frame.Elements())
        {
            if (child.Name == OdtNamespaces.Draw + "image")
                return child;

            if (child.Name == OdtNamespaces.Draw + "a")
            {
                foreach (XElement nested in child.Elements(OdtNamespaces.Draw + "image"))
                    return nested;
            }
        }

        return null;
    }

    private PicturePart? ReadPicture(XElement image, IOdtImageDiagnostics diagnostics)
    {
        string? href = (string?)image.Attribute(OdtNamespaces.XLink + "href");
        if (!string.IsNullOrWhiteSpace(href))
            return LoadFromPackage(href, diagnostics);

        XElement? binary = image.Element(OdtNamespaces.Office + "binary-data");
        if (binary is not null)
            return LoadFromBinaryData(binary, diagnostics);

        diagnostics.AddDiagnosticOnce(
            "odt.image.shape",
            "An ODT frame held no embedded picture and was skipped.");
        return null;
    }

    /// <summary>
    /// Loads a picture the frame names by path, once per path. A part that fails
    /// is cached as a failure, so a document that shows the same picture a
    /// hundred times reports once and re-reads nothing.
    /// </summary>
    private PicturePart? LoadFromPackage(string href, IOdtImageDiagnostics diagnostics)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute) && !absolute.IsFile)
        {
            // A picture stored outside the package. Reading it would be a network
            // or file fetch driven by document content (ADR 0004).
            diagnostics.AddDiagnosticOnce(
                "odt.image.external",
                "An ODT picture linked to an image outside the package, which is not fetched.");
            return null;
        }

        string path = OdtPackage.NormalizePath(href);
        if (path.Length == 0)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.missing",
                "An ODT picture referenced an entry the package does not contain.");
            return null;
        }

        if (_parts.TryGetValue(path, out PicturePart? cached))
            return cached;

        PicturePart? part = ReadPackageEntry(path, diagnostics);
        _parts[path] = part;
        return part;
    }

    private PicturePart? ReadPackageEntry(string path, IOdtImageDiagnostics diagnostics)
    {
        ZipArchiveEntry? entry = OdtPackage.FindEntry(_archive, path);
        if (entry is null)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.missing",
                "An ODT picture referenced an entry the package does not contain.");
            return null;
        }

        byte[]? data = OdtPackage.ReadEntryBytes(entry, _limits.MaxBinBytes);
        if (data is null)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.limit",
                "An ODT picture exceeded MaxBinBytes and was skipped.");
            return null;
        }

        string? contentType =
            OdtImageFormats.ContentTypeForMediaType(_manifest.MediaTypeFor(path)) ??
            OdtImageFormats.ContentTypeForExtension(Path.GetExtension(path)) ??
            OdtImageFormats.ContentTypeForSignature(data);
        if (contentType is null)
        {
            // SVG, EMF, and WMF land here, as does anything stored under an
            // extension this codec does not decode.
            diagnostics.AddDiagnosticOnce(
                "odt.image.format",
                "An ODT picture used an image format this codec does not carry and was skipped.");
            return null;
        }

        return new PicturePart(data, contentType, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Decodes an <c>office:binary-data</c> payload. The size is checked from the
    /// encoded length before decoding, so an oversized payload never allocates.
    /// </summary>
    private PicturePart? LoadFromBinaryData(XElement binary, IOdtImageDiagnostics diagnostics)
    {
        string encoded = binary.Value;
        if ((long)encoded.Length / 4 * 3 > _limits.MaxBinBytes)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.limit",
                "An ODT picture exceeded MaxBinBytes and was skipped.");
            return null;
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.binary",
                "An ODT picture carried inline data that is not valid base64 and was skipped.");
            return null;
        }

        if (data.LongLength > _limits.MaxBinBytes)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.limit",
                "An ODT picture exceeded MaxBinBytes and was skipped.");
            return null;
        }

        string? contentType = OdtImageFormats.ContentTypeForSignature(data);
        if (contentType is null)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.format",
                "An ODT picture used an image format this codec does not carry and was skipped.");
            return null;
        }

        return new PicturePart(data, contentType, "image");
    }

    /// <summary>
    /// The display size of a frame, from <c>svg:width</c> and <c>svg:height</c>.
    /// A missing or unusable extent yields zero, which means draw the image at
    /// its own size.
    /// </summary>
    private static (double Width, double Height) ReadExtent(XElement frame)
    {
        double width = ReadLength(frame, "width");
        double height = ReadLength(frame, "height");
        return (width, height);
    }

    private static double ReadLength(XElement frame, string attributeName)
    {
        string? value = (string?)frame.Attribute(OdtNamespaces.Svg + attributeName);
        return OdtUnits.TryParseLength(value, out double points) && points > 0 ? points : 0;
    }

    /// <summary>
    /// The alternative text and name of a frame. ODF splits accessibility text in
    /// two, a short <c>svg:title</c> and a long <c>svg:desc</c>; the title is the
    /// one a consumer announces, and the description is the fallback.
    /// </summary>
    private static (string? AltText, string? Name) ReadDescription(XElement frame)
    {
        string? title = frame.Element(OdtNamespaces.Svg + "title")?.Value;
        string? description = frame.Element(OdtNamespaces.Svg + "desc")?.Value;
        string? altText = string.IsNullOrWhiteSpace(title) ? description : title;
        return (altText, (string?)frame.Attribute(OdtNamespaces.Draw + "name"));
    }

    private sealed record PicturePart(byte[] Data, string ContentType, string Name);
}

/// <summary>The once-per-code diagnostic sink the image loader reports through.</summary>
internal interface IOdtImageDiagnostics
{
    void AddDiagnosticOnce(string code, string message);
}
