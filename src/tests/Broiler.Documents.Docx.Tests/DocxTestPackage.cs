using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Builds minimal DOCX packages around a hand-written <c>w:body</c> so reader
/// tests can state the exact WordprocessingML they exercise. Only the parts the
/// reader needs are written: the content types, the package relationship that
/// points at the main document, and the parts a test asks for.
/// </summary>
internal static class DocxTestPackage
{
    public const string BodyNamespaceDeclarations =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";

    /// <summary>Wraps <paramref name="bodyXml"/> in a document part and zips a package around it.</summary>
    public static byte[] FromBody(
        string bodyXml,
        IReadOnlyDictionary<string, string>? extraParts = null,
        IReadOnlyDictionary<string, byte[]>? binaryParts = null)
    {
        string documentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document " + BodyNamespaceDeclarations + ">" +
            "<w:body>" + bodyXml + "</w:body>" +
            "</w:document>";

        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["[Content_Types].xml"] =
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"" +
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>",
            ["_rels/.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" " +
                "Target=\"word/document.xml\"/>" +
                "</Relationships>",
            ["word/document.xml"] = documentXml,
        };

        if (extraParts is not null)
        {
            foreach (KeyValuePair<string, string> part in extraParts)
                parts[part.Key] = part.Value;
        }

        var binary = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> part in parts)
            binary[part.Key] = Encoding.UTF8.GetBytes(part.Value);

        if (binaryParts is not null)
        {
            foreach (KeyValuePair<string, byte[]> part in binaryParts)
                binary[part.Key] = part.Value;
        }

        return Zip(binary);
    }

    /// <summary>
    /// Reads a package whose document part declares relationships and carries
    /// media parts — the shape a package with pictures has.
    /// </summary>
    public static DocumentReadResult ReadWithMedia(
        string bodyXml,
        string relationshipsInnerXml,
        IReadOnlyDictionary<string, byte[]> mediaParts,
        DocumentReadOptions? options = null)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                relationshipsInnerXml +
                "</Relationships>",
        };

        using var stream = new MemoryStream(FromBody(bodyXml, parts, mediaParts), writable: false);
        return new DocxDocumentCodec().Read(stream, options);
    }

    /// <summary>An image relationship pointing at a part under <c>word/media</c>.</summary>
    public static string ImageRelationship(string id, string target) =>
        "<Relationship Id=\"" + id + "\" " +
        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
        "Target=\"" + target + "\"/>";

    /// <summary>
    /// A run holding one DrawingML picture sized in EMUs, as Word writes it.
    /// </summary>
    public static string DrawingRun(
        string relationshipId,
        long widthEmus,
        long heightEmus,
        string? altText = null) =>
        "<w:r><w:drawing>" +
        "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
        "<wp:extent cx=\"" + widthEmus + "\" cy=\"" + heightEmus + "\"/>" +
        "<wp:docPr id=\"1\" name=\"Picture 1\"" +
        (altText is null ? string.Empty : " descr=\"" + Escape(altText) + "\"") + "/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
        "<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
        "<pic:nvPicPr><pic:cNvPr id=\"1\" name=\"Picture 1\"/><pic:cNvPicPr/></pic:nvPicPr>" +
        "<pic:blipFill><a:blip r:embed=\"" + relationshipId + "\"/>" +
        "<a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
        "<pic:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
        "</pic:pic></a:graphicData></a:graphic>" +
        "</wp:inline></w:drawing></w:r>";

    /// <summary>A run holding the legacy VML picture shape, sized by CSS.</summary>
    public static string VmlPictureRun(string relationshipId, string style) =>
        "<w:r><w:pict>" +
        "<v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\" id=\"logo\" style=\"" + style + "\">" +
        "<v:imagedata r:id=\"" + relationshipId + "\"/>" +
        "</v:shape></w:pict></w:r>";

    /// <summary>
    /// The smallest valid PNG this codec will accept: a one-pixel image, used
    /// wherever a test needs real image bytes rather than their content.
    /// </summary>
    public static byte[] OnePixelPng { get; } =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    /// <summary>Reads a package built by <see cref="FromBody"/> through the public codec.</summary>
    public static DocumentReadResult ReadBody(string bodyXml, DocumentReadOptions? options = null)
    {
        using var stream = new MemoryStream(FromBody(bodyXml), writable: false);
        return new DocxDocumentCodec().Read(stream, options);
    }

    /// <summary>
    /// Reads a package carrying a <c>word/styles.xml</c> — and optionally a
    /// theme — alongside the body. Both are found by convention; the test
    /// packages declare no document-level relationships.
    /// </summary>
    public static DocumentReadResult ReadStyled(string bodyXml, string stylesInnerXml, string? themeXml = null)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/styles.xml"] = "<w:styles " + BodyNamespaceDeclarations + ">" + stylesInnerXml + "</w:styles>",
        };

        if (themeXml is not null)
            parts["word/theme/theme1.xml"] = themeXml;

        using var stream = new MemoryStream(FromBody(bodyXml, parts), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    /// <summary>A <c>w:style</c> definition; pass the inner <c>w:pPr</c>/<c>w:rPr</c> as <paramref name="propertiesXml"/>.</summary>
    public static string Style(
        string styleId,
        string propertiesXml,
        string type = "paragraph",
        string? basedOn = null,
        bool isDefault = false) =>
        "<w:style w:type=\"" + type + "\" w:styleId=\"" + styleId + "\"" +
        (isDefault ? " w:default=\"1\"" : string.Empty) + ">" +
        "<w:name w:val=\"" + styleId + "\"/>" +
        (basedOn is null ? string.Empty : "<w:basedOn w:val=\"" + basedOn + "\"/>") +
        propertiesXml +
        "</w:style>";

    /// <summary>A <c>w:docDefaults</c> block wrapping the given run properties.</summary>
    public static string DocumentDefaults(string runPropertiesXml, string paragraphPropertiesXml = "") =>
        "<w:docDefaults>" +
        "<w:rPrDefault>" + runPropertiesXml + "</w:rPrDefault>" +
        "<w:pPrDefault>" + paragraphPropertiesXml + "</w:pPrDefault>" +
        "</w:docDefaults>";

    /// <summary>A theme part declaring only the major/minor latin typefaces.</summary>
    public static string ThemeXml(string majorLatin, string minorLatin) =>
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"test\">" +
        "<a:themeElements><a:fontScheme name=\"test\">" +
        "<a:majorFont><a:latin typeface=\"" + majorLatin + "\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"" + minorLatin + "\"/></a:minorFont>" +
        "</a:fontScheme></a:themeElements></a:theme>";

    /// <summary>A <c>w:p</c> that names a paragraph style and holds one unformatted run.</summary>
    public static string StyledParagraph(string styleId, string text) =>
        "<w:p><w:pPr><w:pStyle w:val=\"" + styleId + "\"/></w:pPr>" +
        "<w:r><w:t xml:space=\"preserve\">" + Escape(text) + "</w:t></w:r></w:p>";

    /// <summary>A <c>w:p</c> holding a single run of <paramref name="text"/>.</summary>
    public static string Paragraph(string text) =>
        "<w:p><w:r><w:t xml:space=\"preserve\">" + Escape(text) + "</w:t></w:r></w:p>";

    /// <summary>A table whose rows are given as arrays of cell contents.</summary>
    public static string Table(params string[][] rows)
    {
        var builder = new StringBuilder("<w:tbl><w:tblPr><w:tblW w:w=\"9000\" w:type=\"dxa\"/></w:tblPr><w:tblGrid/>");
        foreach (string[] row in rows)
        {
            builder.Append("<w:tr><w:trPr/>");
            foreach (string cell in row)
                builder.Append("<w:tc><w:tcPr/>").Append(cell).Append("</w:tc>");
            builder.Append("</w:tr>");
        }

        return builder.Append("</w:tbl>").ToString();
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static byte[] Zip(IReadOnlyDictionary<string, byte[]> parts)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (KeyValuePair<string, byte[]> part in parts)
            {
                ZipArchiveEntry entry = archive.CreateEntry(part.Key, CompressionLevel.NoCompression);
                using Stream stream = entry.Open();
                stream.Write(part.Value, 0, part.Value.Length);
            }
        }

        return buffer.ToArray();
    }
}
