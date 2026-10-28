using Broiler.Documents.Resources;
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
internal sealed class DocxImageLoader(ZipArchive archive, DocumentLimits limits, DocumentConversionContextBuilder resources)
{
    /// <summary>English Metric Units per point: 914400 per inch over 72 points per inch.</summary>
    private const double EmusPerPoint = 12700.0;

    /// <summary>
    /// A ceiling on what one picture may draw as. Word stores the display size,
    /// and a corrupt or hostile extent would otherwise ask the layout for a line
    /// millions of units tall.
    /// </summary>
    private const double MaxDisplayExtent = 20000.0;
    private readonly DocxDiagram.ReadState _diagrams = new();
    private enum LoadResult { Success, Missing, Limit, Format }
    private readonly Dictionary<string, (MediaPart? Part, LoadResult Result)> _parts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Puts a loaded picture through the caller's resource policy and, when it
    /// permits extraction, returns the image bound to its context entry.
    /// </summary>
    /// <remarks>
    /// Constructing an <see cref="InlineImage"/> with reachable bytes <em>is</em>
    /// durable extraction into the result model — a caller who has the model has
    /// the picture — so the decision happens here, before the object exists,
    /// rather than after it is already in a document.
    /// </remarks>
    private InlineImage? Admit(InlineImage image, DocumentResourceDisposition disposition, IDocxImageDiagnostics diagnostics)
    {
        if (!resources.TryAdmit(new DocumentResourceRequest(image.Resource, DocumentResourceProvenance.ReadFromSource,
                    disposition, image.Name, "DOCX"), DocumentResourceOperations.ExtractToModel, out DocumentResourceId id, out string? denial))
        {
            diagnostics.AddDiagnosticOnce(
                "docx.image.denied",
                "A picture was not read into the document because " + denial + ".");
            return null;
        }

        return image.WithResourceId(id);
    }


    /// <summary>Reads an embedded diagram cache through the package's existing XML limits.</summary>
    public DocxDiagram.Cache? ReadDiagram(XElement references, XElement anchor, DocxRelationships relationships,
        DocxDocumentBuilder builder, DocxTheme theme) => DocxDiagram.Load(archive, references, anchor, relationships, builder, theme, _diagrams);

    /// <summary>How many pictures this read turned into inline images.</summary>
    public int ImageCount { get; private set; }

    /// <summary>Loads a VML fill through the same limits and resource admission as ordinary pictures.</summary>
    public InlineImage? ReadFillPicture(XElement fill, DocxRelationships relationships, Action<string, string> report)
    {
        string? id = (string?)fill.Attribute(DocxNamespaces.Relationships + "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            report("docx.image.external", "A DOCX picture fill has no embedded relationship; external images are not fetched.");
            return null;
        }
        return Load(id, relationships, 0, 0, null, null, new FillDiagnostics(report));
    }

    private sealed class FillDiagnostics(Action<string, string> report) : IDocxImageDiagnostics
    {
        public void AddDiagnosticOnce(string code, string message) => report(code, message);
    }

    /// <summary>
    /// Reads a <c>w:drawing</c> or <c>w:pict</c> element. Returns null — after
    /// recording why — when the element holds no picture this codec can carry.
    /// </summary>
    public InlineImage? Read(XElement element, DocxRelationships relationships, IDocxImageDiagnostics diagnostics)
    {
        InlineImage? image = element.Name == DocxNamespaces.Wordprocessing + "drawing"
            ? ReadDrawing(element, relationships, diagnostics)
            : ReadVmlPicture(element, relationships, diagnostics);

        if (image is not null)
            ImageCount++;

        return image;
    }

    /// <summary>Loads one grouped picture at its mapped size, with ordinary image admission and limits.</summary>
    public InlineImage? ReadGroupedPicture(XElement picture, double width, double height,
        DocxRelationships relationships, IDocxImageDiagnostics diagnostics)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            width > MaxDisplayExtent || height > MaxDisplayExtent)
        {
            diagnostics.AddDiagnosticOnce("docx.shape.group", "A grouped DOCX picture had an unsupported display extent and was skipped.");
            return null;
        }
        XElement? fill = picture.Element(DocxNamespaces.Picture + "blipFill");
        string? id = (string?)fill?.Element(DocxNamespaces.Drawing + "blip")?.Attribute(DocxNamespaces.Relationships + "embed");
        (string? altText, string? name) = ReadDescription(picture);
        ImagePresentation? presentation = ReadPresentation(fill, picture);
        if (string.IsNullOrWhiteSpace(id))
        {
            diagnostics.AddDiagnosticOnce("docx.shape.group", "A grouped DOCX picture held no embedded image; external images are not fetched.");
            InlineImage? placeholder = Admit(InlineImage.CreateMissingPayload(width, height, altText, name, presentation),
                DocumentResourceDisposition.Linked, diagnostics);
            if (placeholder is not null)
                ImageCount++;
            return placeholder;
        }
        InlineImage? image = Load(id, relationships, width, height, altText, name, diagnostics, presentation);
        if (image is not null)
            ImageCount++;
        return image;
    }

    /// <summary>
    /// Reads the DrawingML shape: <c>wp:inline</c> or <c>wp:anchor</c>, then the
    /// <c>pic:pic</c> under its graphic data. Both wrappers yield the same image;
    /// where an anchored one is placed — floating beside the text, or in it — is
    /// the reader's decision, not this loader's.
    /// </summary>
    private InlineImage? ReadDrawing(XElement drawing, DocxRelationships relationships, IDocxImageDiagnostics diagnostics)
    {
        foreach (XElement wrapper in drawing.Elements())
        {
            if (wrapper.Name != DocxNamespaces.WordDrawing + "inline" &&
                wrapper.Name != DocxNamespaces.WordDrawing + "anchor")
            {
                continue;
            }

            (double width, double height) = ReadExtent(wrapper);
            (string? altText, string? name) = ReadDescription(wrapper);

            XElement? blip = FindDescendant(wrapper, DocxNamespaces.Drawing + "blip");
            if (blip is null)
            {
                diagnostics.AddDiagnosticOnce("docx.image.shape", "A DOCX drawing held no embedded picture and was skipped.");
                return null;
            }

            ImagePresentation? presentation = ReadPresentation(blip.Parent, wrapper);
            string? relationshipId = (string?)blip.Attribute(DocxNamespaces.Relationships + "embed");
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                // r:link points at an image outside the package. Reading it would
                // be a network or file fetch from document content (ADR 0004).
                diagnostics.AddDiagnosticOnce(
                    "docx.image.external",
                    "A DOCX picture linked to an external image, which is not fetched.");
                if (width > 0 && height > 0)
                {
                    return Admit(InlineImage.CreateMissingPayload(width, height, altText, name, presentation),
                        DocumentResourceDisposition.Linked, diagnostics);
                }
                return null;
            }

            return Load(relationshipId, relationships, width, height, altText, name, diagnostics, presentation);
        }

        diagnostics.AddDiagnosticOnce("docx.image.shape", "A DOCX drawing held no embedded picture and was skipped.");
        return null;
    }

    /// <summary>
    /// Reads the legacy VML picture: <c>w:pict/v:shape/v:imagedata</c>, sized by
    /// the shape's CSS <c>style</c>. Word wrote this shape for images before
    /// DrawingML and still writes it inside fields and some headers.
    /// </summary>
    private InlineImage? ReadVmlPicture(XElement pict, DocxRelationships relationships, IDocxImageDiagnostics diagnostics)
    {
        XElement? imageData = FindDescendant(pict, DocxNamespaces.Vml + "imagedata");
        if (imageData is null)
        {
            diagnostics.AddDiagnosticOnce("docx.image.shape", "A DOCX drawing held no embedded picture and was skipped.");
            return null;
        }

        XElement? shape = imageData.Parent;
        (double width, double height) = ReadVmlStyleExtent((string?)shape?.Attribute("style"));
        string? altText = (string?)shape?.Attribute("alt") ?? (string?)imageData.Attribute("title");
        string? name = (string?)shape?.Attribute("id");

        string? relationshipId = (string?)imageData.Attribute(DocxNamespaces.Relationships + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            diagnostics.AddDiagnosticOnce("docx.image.external", "A DOCX picture linked to an external image, which is not fetched.");
            if (width > 0 && height > 0)
            {
                return Admit(InlineImage.CreateMissingPayload(width, height, altText, name),
                    DocumentResourceDisposition.Linked, diagnostics);
            }
            return null;
        }

        return Load(relationshipId, relationships, width, height, altText, name, diagnostics);
    }

    /// <summary>
    /// How the picture is shown: the part of the source it uses, and the shape it
    /// is masked to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>a:srcRect</c> states each edge's crop in thousandths of a percent, so
    /// 14330 is 14.33% off that side. It sits on the <c>a:blipFill</c>, which is
    /// the blip's parent, and it crops the <em>source</em> rather than the box —
    /// the picture still fills the extent it states.
    /// </para>
    /// <para>
    /// <c>a:prstGeom</c> is the shape the fill is drawn through. Only
    /// <c>ellipse</c> is represented, because it is the one that changes what a
    /// document looks like rather than decorating it: the round portrait is what
    /// every CV template puts its photograph in, and drawn square it is the first
    /// thing anyone notices. Every other preset draws as the rectangle it is
    /// inscribed in, which is what this codec did for all of them before.
    /// </para>
    /// </remarks>
    private static ImagePresentation? ReadPresentation(XElement? blipFill, XElement wrapper)
    {
        double left = CropEdge(blipFill, "l");
        double top = CropEdge(blipFill, "t");
        double right = CropEdge(blipFill, "r");
        double bottom = CropEdge(blipFill, "b");

        XElement? geometry = FindDescendant(wrapper, DocxNamespaces.Drawing + "prstGeom");
        ImageMask mask = string.Equals((string?)geometry?.Attribute("prst"), "ellipse", StringComparison.Ordinal)
            ? ImageMask.Ellipse
            : ImageMask.None;

        var presentation = new ImagePresentation
        {
            CropLeft = left,
            CropTop = top,
            CropRight = right,
            CropBottom = bottom,
            Mask = mask,
        };

        return presentation.IsDefault ? null : presentation;
    }

    /// <summary>One edge of an <c>a:srcRect</c>, as a fraction of the source.</summary>
    private static double CropEdge(XElement? blipFill, string edge)
    {
        XElement? crop = blipFill?.Element(DocxNamespaces.Drawing + "srcRect");
        if (crop is null ||
            !int.TryParse((string?)crop.Attribute(edge), NumberStyles.Integer, CultureInfo.InvariantCulture, out int thousandthsOfAPercent) ||
            thousandthsOfAPercent <= 0)
        {
            return 0;
        }

        return thousandthsOfAPercent / 100000d;
    }

    /// <summary>Loads a picture from an a:blipFill element.</summary>
    public InlineImage? ReadBlipFill(XElement blipFill, DocxRelationships relationships, IDocxImageDiagnostics diagnostics)
    {
        XElement? blip = blipFill.Element(DocxNamespaces.Drawing + "blip");
        if (blip is null)
            return null;

        string? embed = (string?)blip.Attribute(DocxNamespaces.Relationships + "embed");
        if (string.IsNullOrWhiteSpace(embed))
        {
            diagnostics.AddDiagnosticOnce("docx.image.external", "A DOCX picture fill linked to an external image, which is not fetched.");
            return null;
        }

        ImagePresentation? presentation = ReadPresentation(blipFill, blipFill);
        return Load(embed, relationships, 0, 0, null, null, diagnostics, presentation);
    }

    private InlineImage? Load(string relationshipId, DocxRelationships relationships, double width, double height, string? altText, string? name,
        IDocxImageDiagnostics diagnostics, ImagePresentation? presentation = null)
    {
        if (!relationships.TryGet(relationshipId, out DocxRelationship? relationship) || relationship is null)
        {
            diagnostics.AddDiagnosticOnce("docx.image.relationship", "A DOCX picture named a relationship the package does not define.");
            return null;
        }

        if (relationship.TargetModeExternal)
        {
            diagnostics.AddDiagnosticOnce("docx.image.external", "A DOCX picture linked to an external image, which is not fetched.");
            if (width > 0 && height > 0)
            {
                return Admit(InlineImage.CreateMissingPayload(width, height, altText, name, presentation),
                    DocumentResourceDisposition.Linked, diagnostics);
            }
            return null;
        }

        MediaPart? part = LoadPart(relationship.Target, diagnostics, out LoadResult result);
        if (part is null)
        {
            if (result is LoadResult.Missing or LoadResult.Format && width > 0 && height > 0)
            {
                return Admit(InlineImage.CreateMissingPayload(width, height, altText, name, presentation),
                    DocumentResourceDisposition.Embedded, diagnostics);
            }
            return null;
        }

        return Admit(new InlineImage(part.Data, part.ContentType, width, height, altText,
            string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(relationship.Target) : name, presentation),
            DocumentResourceDisposition.Embedded, diagnostics);
    }

    /// <summary>
    /// Loads a media part's bytes, once per part path. A part that fails — too
    /// large, missing, or not a raster format — is cached as a failure so a
    /// document that references it a hundred times reports once and does not
    /// re-read the entry a hundred times.
    /// </summary>
    private MediaPart? LoadPart(string partPath, IDocxImageDiagnostics diagnostics, out LoadResult result)
    {
        if (_parts.TryGetValue(partPath, out (MediaPart? Part, LoadResult Result) cached))
        {
            result = cached.Result;
            return cached.Part;
        }

        MediaPart? part = ReadPart(partPath, diagnostics, out result);
        _parts[partPath] = (part, result);
        return part;
    }

    private MediaPart? ReadPart(string partPath, IDocxImageDiagnostics diagnostics, out LoadResult result)
    {
        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, partPath);
        if (entry is null)
        {
            diagnostics.AddDiagnosticOnce("docx.image.missing", "A DOCX picture referenced a media part the package does not contain.");
            result = LoadResult.Missing;
            return null;
        }

        if (entry.Length > limits.MaxBinBytes)
        {
            diagnostics.AddDiagnosticOnce("docx.image.limit", "A DOCX image part exceeded MaxBinBytes and was skipped.");
            result = LoadResult.Limit;
            return null;
        }

        byte[]? data = ReadEntryBytes(entry, limits.MaxBinBytes);
        if (data is null)
        {
            diagnostics.AddDiagnosticOnce("docx.image.limit", "A DOCX image part exceeded MaxBinBytes and was skipped.");
            result = LoadResult.Limit;
            return null;
        }

        string? contentType = DocumentImageFormats.ContentTypeForExtension(Path.GetExtension(partPath)) ?? DocumentImageFormats.ContentTypeForSignature(data);
        if (contentType is null)
        {
            // EMF/WMF metafiles land here, as does anything Word stored under an
            // extension this codec does not decode.
            diagnostics.AddDiagnosticOnce("docx.image.format", "A DOCX picture used an image format this codec does not carry and was skipped.");
            result = LoadResult.Format;
            return null;
        }

        result = LoadResult.Success;
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
        XElement? extent = wrapper.Element(DocxNamespaces.WordDrawing + "extent") ?? FindDescendant(wrapper, DocxNamespaces.Drawing + "ext");
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
        XElement? docPr = wrapper.Element(DocxNamespaces.WordDrawing + "docPr") ?? FindDescendant(wrapper, DocxNamespaces.Picture + "cNvPr");
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
