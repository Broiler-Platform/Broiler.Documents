using Broiler.Documents.Resources;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

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
internal sealed class OdtImageLoader(
    ZipArchive? archive,
    OdtManifest manifest,
    DocumentLimits limits,
    DocumentConversionContextBuilder resources)
{
    private readonly ZipArchive? _archive = archive;
    private readonly OdtManifest _manifest = manifest;
    private readonly DocumentLimits _limits = limits;
    private readonly DocumentConversionContextBuilder _resources = resources;
    private enum LoadResult { Success, Missing, Limit, Format, External, Blank }
    private readonly Dictionary<string, (PicturePart? Part, LoadResult Result)> _parts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<BImageResource> AudioPreview = new(CreateAudioPreview);
    private static readonly Lazy<BImageResource> ObjectPreview = new(CreateObjectPreview);

    /// <summary>How many pictures this read turned into inline images.</summary>
    public int ImageCount { get; private set; }

    /// <summary>
    /// Reads a <c>draw:frame</c>. Returns null, after recording why, when the
    /// frame holds no picture this codec can carry.
    /// </summary>
    public InlineImage? Read(XElement frame, IOdtImageDiagnostics diagnostics, PageGeometry? inlinePage = null,
        double? frameTextWidth = null)
    {
        PicturePart? part = null;
        bool foundImage = false;
        LoadResult lastResult = LoadResult.Missing;
        foreach (XElement image in ImageAlternatives(frame))
        {
            foundImage = true;
            part = ReadPicture(image, diagnostics, out lastResult);
            if (part is not null)
                break;
        }

        (double width, double height) = ReadExtent(frame, inlinePage, frameTextWidth);
        (string? altText, string? name) = ReadDescription(frame);

        if (part is null)
        {
            if (!foundImage)
            {
                if (width > 0 && height > 0 && frame.Element(OdtNamespaces.Draw + "plugin") is XElement plugin && IsAudio(plugin))
                    return ReadAudioPreview(width, height, altText, name, diagnostics);

                // An OLE object with no replacement picture (a linked object, or one
                // whose preview is absent) keeps its frame's extent: LibreOffice
                // draws its replacement graphic there rather than nothing.
                if (width > 0 && height > 0 &&
                    (frame.Element(OdtNamespaces.Draw + "object") ?? frame.Element(OdtNamespaces.Draw + "object-ole"))
                        ?.Attribute(OdtNamespaces.XLink + "href") is not null)
                {
                    diagnostics.AddDiagnosticOnce(
                        "odt.image.object",
                        "An ODT embedded object without a replacement picture was drawn as a placeholder icon.");
                    return ReadGeneratedPreview(ObjectPreview.Value, width, height, altText, name);
                }
                else
                {
                    diagnostics.AddDiagnosticOnce(
                        "odt.image.shape",
                        "An ODT frame held no embedded picture and was skipped.");
                    return null;
                }
            }

            if (lastResult is LoadResult.Missing or LoadResult.External or LoadResult.Format or LoadResult.Blank && width > 0 && height > 0)
            {
                // A picture the package does not hold - an entry that is absent, or a
                // relative link out of the package that LibreOffice resolves against
                // the file system and does not find - prints as an empty frame in
                // LibreOffice: its replacement graphic is not part of the printed or
                // exported page. The box keeps its place and draws nothing. A URL or
                // an undecodable format keeps the mark, because LibreOffice draws a
                // picture there that this codec does not. A replacement metafile
                // that draws nothing at all is as empty in LibreOffice.
                var placeholder = InlineImage.CreateMissingPayload(width, height, altText, name,
                    marked: lastResult is not (LoadResult.Missing or LoadResult.Blank));
                if (!_resources.TryAdmit(
                        new DocumentResourceRequest(
                            placeholder.Resource,
                            DocumentResourceProvenance.ReadFromSource,
                            lastResult == LoadResult.External ? DocumentResourceDisposition.Linked : DocumentResourceDisposition.Embedded,
                            placeholder.Name,
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
                return placeholder.WithResourceId(id);
            }

            return null;
        }

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
                out DocumentResourceId id2,
                out string? denial2))
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.denied",
                "A picture was not read into the document because " + denial2 + ".");
            return null;
        }

        ImageCount++;
        return picture.WithResourceId(id2);
    }

    private static bool IsAudio(XElement plugin) =>
        ((string?)plugin.Attribute(OdtNamespaces.Draw + "mime-type"))?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

    private InlineImage? ReadAudioPreview(double width, double height, string? altText, string? name, IOdtImageDiagnostics diagnostics)
    {
        diagnostics.AddDiagnosticOnce("odt.media.audio", "An ODT audio object was represented by a static audio icon.");
        BImageResource resource = AudioPreview.Value;
        if (!_resources.TryAdmit(new DocumentResourceRequest(resource, DocumentResourceProvenance.ReadFromSource,
                DocumentResourceDisposition.Generated, name, "ODT"), DocumentResourceOperations.ExtractToModel,
                out DocumentResourceId id, out _))
            return null;
        ImageCount++;
        return new InlineImage(resource, id, width, height, altText, name);
    }

    private InlineImage? ReadGeneratedPreview(BImageResource resource, double width, double height, string? altText, string? name)
    {
        if (!_resources.TryAdmit(new DocumentResourceRequest(resource, DocumentResourceProvenance.ReadFromSource,
                DocumentResourceDisposition.Generated, name, "ODT"), DocumentResourceOperations.ExtractToModel,
                out DocumentResourceId id, out _))
            return null;
        ImageCount++;
        return new InlineImage(resource, id, width, height, altText, name);
    }

    /// <summary>
    /// LibreOffice's replacement graphic for an OLE object it cannot show: a
    /// blue puzzle piece in the frame's top-left corner.
    /// </summary>
    private static BImageResource CreateObjectPreview()
    {
        const int size = 144;
        using var bitmap = new BBitmap(size, size);
        using (var canvas = bitmap.OpenCanvas())
        {
            var fill = new BColor(125, 182, 240);
            var edge = new BColor(52, 101, 164);
            var white = new BColor(255, 255, 255);
            canvas.FillRect(new RectangleF(0, 0, size, size), white);
            canvas.FillRoundedRect(new RectangleF(0, 22, 108, 92), edge, 6, 6);
            canvas.FillRoundedRect(new RectangleF(3, 25, 102, 86), fill, 4, 4);
            canvas.FillRoundedRect(new RectangleF(36, 0, 32, 30), edge, 15, 15);
            canvas.FillRoundedRect(new RectangleF(39, 3, 26, 28), fill, 13, 13);
            canvas.FillRoundedRect(new RectangleF(-12, 52, 28, 28), edge, 14, 14);
            canvas.FillRoundedRect(new RectangleF(-12, 55, 25, 22), white, 11, 11);
        }
        return BImageResource.FromPixels(bitmap.ToPixelBuffer());
    }

    private static BImageResource CreateAudioPreview()
    {
        const int size = 144;
        using var bitmap = new BBitmap(size, size);
        using (var canvas = bitmap.OpenCanvas())
        {
            var blue = new BColor(30, 139, 205);
            canvas.FillRect(new RectangleF(0, 0, size, size), new BColor(250, 250, 250));
            canvas.DrawRectangleStroke(new RectangleF(0.5f, 0.5f, size - 1, size - 1), new BColor(112, 112, 112));
            for (int i = 0; i < 4; i++)
                canvas.FillRoundedRect(new RectangleF(43, 42 + i * 14.5f, i == 3 ? 16 : 29, 5), blue, 2.5f, 2.5f);
            canvas.FillRoundedRect(new RectangleF(72, 85, 17, 20), blue, 8.5f, 10);
            canvas.FillRoundedRect(new RectangleF(85, 37, 4, 59), blue, 2, 2);
            canvas.FillPolygon([new(87, 39), new(91, 48), new(97, 55), new(101, 64), new(101, 72), new(98, 79),
                new(99, 69), new(96, 61), new(90, 55), new(87, 54)], blue);
        }
        return BImageResource.FromPixels(bitmap.ToPixelBuffer());
    }

    /// <summary>
    /// Alternative representations of one picture, in document preference order.
    /// The first readable one wins; an unsupported vector representation must
    /// not hide an embedded raster fallback. Clickable images may be wrapped in
    /// draw:a, but nested frames hold separate objects and are not searched.
    /// </summary>
    private static IEnumerable<XElement> ImageAlternatives(XElement frame)
    {
        foreach (XElement child in frame.Elements())
        {
            if (child.Name == OdtNamespaces.Draw + "image")
                yield return child;
            else if (child.Name == OdtNamespaces.Draw + "a")
            {
                foreach (XElement nested in child.Elements(OdtNamespaces.Draw + "image"))
                    yield return nested;
            }
        }
    }

    private PicturePart? ReadPicture(XElement image, IOdtImageDiagnostics diagnostics, out LoadResult result)
    {
        string? href = (string?)image.Attribute(OdtNamespaces.XLink + "href");
        if (!string.IsNullOrWhiteSpace(href))
            return LoadFromPackage(href, diagnostics, out result);

        XElement? binary = image.Element(OdtNamespaces.Office + "binary-data");
        if (binary is not null)
            return LoadFromBinaryData(binary, diagnostics, out result);

        diagnostics.AddDiagnosticOnce(
            "odt.image.shape",
            "An ODT frame held no embedded picture and was skipped.");
        result = LoadResult.Missing;
        return null;
    }

    /// <summary>
    /// True when <paramref name="href"/> names a picture outside the package: a
    /// URL, or any path at all in a flat document, which has no package.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(_archive))]
    public bool LinksOutsidePackage(string href) =>
        _archive is null || (Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute) && !absolute.IsFile);

    /// <summary>
    /// Loads a picture the frame names by path, once per path. A part that fails
    /// is cached as a failure, so a document that shows the same picture a
    /// hundred times reports once and re-reads nothing.
    /// </summary>
    private PicturePart? LoadFromPackage(string href, IOdtImageDiagnostics diagnostics, out LoadResult result)
    {
        if (LinksOutsidePackage(href))
        {
            // A picture stored outside the package. Reading it would be a network
            // or file fetch driven by document content (ADR 0004). A flat
            // document has no package, so every path it names is outside one: it
            // carries the pictures it holds as office:binary-data instead.
            diagnostics.AddDiagnosticOnce(
                "odt.image.external",
                "An ODT picture linked to an image outside the package, which is not fetched.");
            result = LoadResult.External;
            return null;
        }

        string path = OdtPackage.NormalizePath(href);
        if (path.Length == 0)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.missing",
                "An ODT picture referenced an entry the package does not contain.");
            result = LoadResult.Missing;
            return null;
        }

        if (_parts.TryGetValue(path, out (PicturePart? Part, LoadResult Result) cached))
        {
            result = cached.Result;
            return cached.Part;
        }

        PicturePart? part = ReadPackageEntry(_archive, path, diagnostics, out result);
        _parts[path] = (part, result);
        return part;
    }

    private PicturePart? ReadPackageEntry(ZipArchive archive, string path, IOdtImageDiagnostics diagnostics, out LoadResult result)
    {
        ZipArchiveEntry? entry = OdtPackage.FindEntry(archive, path);
        if (entry is null)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.missing",
                "An ODT picture referenced an entry the package does not contain.");
            result = LoadResult.Missing;
            return null;
        }

        byte[]? data = OdtPackage.ReadEntryBytes(entry, _limits.MaxBinBytes);
        if (data is null)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.limit",
                "An ODT picture exceeded MaxBinBytes and was skipped.");
            result = LoadResult.Limit;
            return null;
        }

        string? contentType = 
            OdtImageFormats.ContentTypeForMediaType(_manifest.MediaTypeFor(path)) ??
            DocumentImageFormats.ContentTypeForExtension(Path.GetExtension(path)) ??
            DocumentImageFormats.ContentTypeForSignature(data);
        if (contentType is null && RasterizeMetafile(data) is byte[] png)
            (data, contentType) = (png, "image/png");

        if (contentType is null && SvmRasterizer.IsBlank(data))
        {
            result = LoadResult.Blank;
            return null;
        }

        if (contentType is null)
        {
            // SVG and WMF land here, as do metafiles that draw nothing the
            // rasterizers understand and anything stored under an extension this
            // codec does not decode.
            diagnostics.AddDiagnosticOnce(
                "odt.image.format",
                "An ODT picture used an image format this codec does not carry and was skipped.");
            result = LoadResult.Format;
            return null;
        }

        result = LoadResult.Success;
        return new PicturePart(data, contentType, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Rasterizes the replacement picture LibreOffice stores for a chart or OLE
    /// object (an <c>ObjectReplacements/</c> entry, which has no extension and a
    /// GDI-metafile media type): its own StarView metafile, or an EMF for a
    /// Windows OLE object. Null for anything else, or a metafile that draws
    /// nothing the rasterizers understand.
    /// </summary>
    internal static byte[]? RasterizeMetafile(byte[] data)
    {
        if (SvmRasterizer.IsSvm(data))
            return SvmRasterizer.RenderPng(data);
        if (EmfRasterizer.IsEmf(data))
            return EmfRasterizer.RenderPng(data);
        return null;
    }

    /// <summary>
    /// Decodes an <c>office:binary-data</c> payload. The size is checked from the
    /// encoded length before decoding, so an oversized payload never allocates.
    /// </summary>
    private PicturePart? LoadFromBinaryData(XElement binary, IOdtImageDiagnostics diagnostics, out LoadResult result)
    {
        string raw = binary.Value;
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }
        int rem = sb.Length % 4;
        if (rem == 2)
            sb.Append("==");
        else if (rem == 3)
            sb.Append('=');
        string encoded = sb.ToString();

        if ((long)encoded.Length / 4 * 3 > _limits.MaxBinBytes)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.limit",
                "An ODT picture exceeded MaxBinBytes and was skipped.");
            result = LoadResult.Limit;
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
            result = LoadResult.Format;
            return null;
        }

        if (data.LongLength > _limits.MaxBinBytes)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.limit",
                "An ODT picture exceeded MaxBinBytes and was skipped.");
            result = LoadResult.Limit;
            return null;
        }

        string? contentType = DocumentImageFormats.ContentTypeForSignature(data);
        if (contentType is null && RasterizeMetafile(data) is byte[] png)
            (data, contentType) = (png, "image/png");

        if (contentType is null && SvmRasterizer.IsBlank(data))
        {
            result = LoadResult.Blank;
            return null;
        }

        if (contentType is null)
        {
            diagnostics.AddDiagnosticOnce(
                "odt.image.format",
                "An ODT picture used an image format this codec does not carry and was skipped.");
            result = LoadResult.Format;
            return null;
        }

        result = LoadResult.Success;
        return new PicturePart(data, contentType, "image");
    }

    /// <summary>
    /// The frame's declared size, adjusted by relative sizing for an inline frame.
    /// A missing or unusable extent yields zero, which means draw the image at
    /// its own size.
    /// </summary>
    private static (double Width, double Height) ReadExtent(XElement frame, PageGeometry? inlinePage, double? frameTextWidth)
    {
        double width = ReadLength(frame, "width");
        double height = ReadLength(frame, "height");
        if (inlinePage is null)
            return (width, height);

        string? relativeWidth = (string?)frame.Attribute(OdtNamespaces.Style + "rel-width");
        string? relativeHeight = (string?)frame.Attribute(OdtNamespaces.Style + "rel-height");
        double resolvedWidth = ResolveRelative(relativeWidth, frameTextWidth ?? inlinePage.ColumnWidth, width);
        double resolvedHeight = ResolveRelative(relativeHeight, inlinePage.ContentHeight, height);
        if (width > 0 && height > 0)
        {
            if (relativeHeight == "scale")
                resolvedHeight = Bounded(resolvedWidth * (height / width), height);
            else if (relativeWidth == "scale")
                resolvedWidth = Bounded(resolvedHeight * (width / height), width);
        }

        return (resolvedWidth, resolvedHeight);

        static double ResolveRelative(string? value, double reference, double fallback)
        {
            if (!OdtUnits.TryParsePercentage(value, out double multiplier))
                return fallback;
            return Bounded(reference * multiplier, fallback);
        }

        static double Bounded(double extent, double fallback) =>
            extent > 0 ? Math.Min(extent, OdtUnits.MaxLengthPoints) : fallback;
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
