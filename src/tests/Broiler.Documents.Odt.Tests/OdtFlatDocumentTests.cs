using System.Text;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// The flat single-file form of an ODF text document (<c>.fodt</c>): one XML
/// file whose <c>office:document</c> root holds what a package spreads over
/// <c>content.xml</c>, <c>styles.xml</c> and <c>meta.xml</c>. Every one used to
/// fail with <c>odt.package.zip</c>, because the reader only knew the ZIP form.
/// </summary>
public sealed class OdtFlatDocumentTests
{
    private const string StylesXml =
        "<office:styles>" +
        "<style:style style:name=\"Standard\" style:family=\"paragraph\">" +
        "<style:text-properties fo:font-size=\"11pt\"/>" +
        "</style:style>" +
        "</office:styles>";

    private const string AutomaticStylesXml =
        "<office:automatic-styles>" +
        "<style:style style:name=\"T1\" style:family=\"text\">" +
        "<style:text-properties fo:font-weight=\"bold\"/>" +
        "</style:style>" +
        "<style:page-layout style:name=\"pm1\">" +
        "<style:page-layout-properties fo:page-width=\"21cm\" fo:page-height=\"29.7cm\" " +
        "fo:margin-left=\"2cm\" fo:margin-right=\"2cm\" fo:margin-top=\"2cm\" fo:margin-bottom=\"2cm\"/>" +
        "</style:page-layout>" +
        "<style:style style:name=\"MP1\" style:family=\"paragraph\" style:parent-style-name=\"Standard\"/>" +
        "</office:automatic-styles>";

    private const string MasterStylesXml =
        "<office:master-styles>" +
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\">" +
        "<style:header><text:p text:style-name=\"MP1\">running head</text:p></style:header>" +
        "</style:master-page>" +
        "</office:master-styles>";

    private const string MetaXml =
        "<office:meta><dc:title>Flat title</dc:title></office:meta>";

    private static string Flat(
        string bodyXml,
        string? mediaType = OdtTestPackage.TextMediaType,
        string meta = MetaXml,
        string styles = StylesXml,
        string automaticStyles = AutomaticStylesXml,
        string masterStyles = MasterStylesXml) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<office:document " + OdtTestPackage.NamespaceDeclarations + " " +
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" office:version=\"1.3\"" +
        (mediaType is null ? string.Empty : " office:mimetype=\"" + mediaType + "\"") + ">\n" +
        "  " + meta + "\n" +
        "  " + styles + "\n" +
        "  " + automaticStyles + "\n" +
        "  " + masterStyles + "\n" +
        "  <office:body>\n    <office:text>\n      " + bodyXml + "\n    </office:text>\n  </office:body>\n" +
        "</office:document>\n";

    private static DocumentReadResult Read(string xml) => Read(Encoding.UTF8.GetBytes(xml));

    private static DocumentReadResult Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return new OdtDocumentCodec().Read(stream);
    }

    private static DocumentProbeResult Probe(string xml, string? fileName = null) =>
        new OdtDocumentCodec().Probe(new DocumentProbeRequest(
            Encoding.UTF8.GetBytes(xml),
            new DocumentSourceHints(fileName)));

    [Fact]
    public void The_Body_Is_Read_From_The_Single_File()
    {
        DocumentReadResult result = Read(Flat(
            "<text:p>plain <text:span text:style-name=\"T1\">bold</text:span></text:p>\n      " +
            "<text:p>second</text:p>"));

        Assert.NotEqual(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(["plain bold", "second"], result.Document.Paragraphs.Select(p => p.Text));
        Assert.False(result.Document.Paragraphs[0].StyleAt(0).Bold);
        Assert.True(result.Document.Paragraphs[0].StyleAt(6).Bold);
    }

    [Fact]
    public void Named_Styles_Resolve_From_Office_Styles_In_The_Same_File()
    {
        DocumentReadResult result = Read(Flat("<text:p text:style-name=\"Standard\">eleven</text:p>"));

        Assert.Equal(11, result.Document.Paragraphs[0].StyleAt(0).FontSize);
    }

    [Fact]
    public void The_Page_And_The_Header_Come_From_The_Master_Styles_In_The_Same_File()
    {
        DocumentReadResult result = Read(Flat("<text:p>body</text:p>"));

        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);
        Assert.Equal(595.3, page.Width, 1);
        Assert.Equal(841.9, page.Height, 1);
        Assert.Equal(56.7, page.MarginLeft, 1);

        RichTextParagraph header = Assert.Single(result.Document.RunningContent.Header(PageSelection.Default));
        Assert.Equal("running head", header.Text);
        Assert.Equal(11, header.StyleAt(0).FontSize);
    }

    [Fact]
    public void Metadata_Comes_From_Office_Meta_In_The_Same_File()
    {
        DocumentReadResult result = Read(Flat("<text:p>body</text:p>"));

        Assert.Equal("Flat title", result.Metadata.Title);
    }

    [Fact]
    public void Indentation_Between_Elements_Is_Not_Read_As_Text()
    {
        // LibreOffice writes a flat file indented, and hand-written test
        // documents indent inside the paragraph too. The ODF white-space rule
        // applies to both exactly as it does inside content.xml.
        DocumentReadResult result = Read(Flat(
            "<text:p>\n        one\n        <text:span text:style-name=\"T1\">two</text:span>\n      </text:p>"));

        Assert.Equal("one two", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void A_Byte_Order_Mark_Is_Accepted()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Flat("<text:p>marked</text:p>"))];

        DocumentReadResult result = Read(bytes);

        Assert.Equal("marked", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void An_Inline_Picture_Is_Read_From_Its_Binary_Data()
    {
        string frame =
            "<draw:frame svg:width=\"1in\" svg:height=\"0.5in\"><draw:image>" +
            "<office:binary-data>" + Convert.ToBase64String(OdtTestPackage.OnePixelPng) + "</office:binary-data>" +
            "</draw:image></draw:frame>";

        DocumentReadResult result = Read(Flat("<text:p>a" + frame + "b</text:p>"));

        InlineImage image = Assert.IsType<InlineImage>(result.Document.Paragraphs[0].StyleAt(1).Image);
        Assert.Equal(OdtTestPackage.OnePixelPng, image.Data.ToArray());
    }

    [Fact]
    public void A_Picture_Named_By_Path_Is_Outside_The_File_And_Not_Fetched()
    {
        string frame =
            "<draw:frame svg:width=\"1in\" svg:height=\"0.5in\">" +
            "<draw:image xlink:href=\"Pictures/logo.png\"/></draw:frame>";

        DocumentReadResult result = Read(Flat("<text:p>a" + frame + "b</text:p>"));

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.external");
        Assert.Equal("a" + InlineImage.PlaceholderText + "b", result.Document.Paragraphs[0].Text);
        InlineImage image = result.Document.Paragraphs[0].StyleAt(1).Image!;
        Assert.True(image.IsMissingPayload);
        Assert.Equal(72, image.Width);
        Assert.Equal(36, image.Height);
    }

    [Fact]
    public void A_Flat_Spreadsheet_Is_Refused_By_Its_Media_Type()
    {
        DocumentReadResult result = Read(Flat(
            "<text:p>x</text:p>",
            mediaType: "application/vnd.oasis.opendocument.spreadsheet"));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.flat.media-type");
    }

    [Fact]
    public void A_Flat_Template_Reads_Like_A_Document()
    {
        DocumentReadResult result = Read(Flat(
            "<text:p>template</text:p>",
            mediaType: "application/vnd.oasis.opendocument.text-template"));

        Assert.Equal("template", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void Xml_With_Another_Root_Is_Refused()
    {
        DocumentReadResult result = Read("<?xml version=\"1.0\"?><html><body><p>x</p></body></html>");

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.flat.root");
    }

    [Fact]
    public void Malformed_Xml_Is_Refused_As_Xml_Not_As_A_Zip()
    {
        DocumentReadResult result = Read(Flat("<text:p>unclosed"));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.xml");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.package.zip");
    }

    [Fact]
    public void A_Prefix_The_File_Never_Declares_Is_Read_As_LibreOffice_Declares_It()
    {
        // LibreOffice's tdf142130.fodt uses fo: and style: without declaring
        // either, and LibreOffice reads it. An XML reader refuses the file.
        string xml = Flat("<text:p text:style-name=\"P1\">kept</text:p>", styles:
            "<office:styles>" +
            "<style:style style:name=\"P1\" style:family=\"paragraph\">" +
            "<style:text-properties fo:font-size=\"20pt\" loext:opacity=\"100%\"/>" +
            "</style:style>" +
            "</office:styles>");

        DocumentReadResult result = Read(xml);

        Assert.NotEqual(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal("kept", Assert.Single(result.Document.Paragraphs).Text);
        Assert.Equal(20, result.Document.Paragraphs[0].StyleAt(0).FontSize);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.xml.prefix");
    }

    [Fact]
    public void A_Declared_Prefix_Wins_Over_The_Implied_One()
    {
        // The file binds fo: to something else, so its font size is not an
        // XSL-FO property and the size stays the default.
        string xml = Flat("<text:p text:style-name=\"P1\">x</text:p>", styles:
            "<office:styles>" +
            "<style:style style:name=\"P1\" style:family=\"paragraph\">" +
            "<style:text-properties xmlns:fo=\"urn:example:other\" fo:font-size=\"20pt\" loext:opacity=\"100%\"/>" +
            "</style:style>" +
            "</office:styles>");

        DocumentReadResult result = Read(xml);

        Assert.NotEqual(DocumentResultStatus.Rejected, result.Status);
        Assert.NotEqual(20, result.Document.Paragraphs[0].StyleAt(0).FontSize);
    }

    [Fact]
    public void A_Prefix_LibreOffice_Does_Not_Declare_Still_Fails_The_Read()
    {
        DocumentReadResult result = Read(Flat("<text:p><nonsense:span>x</nonsense:span></text:p>"));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.xml");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.xml.prefix");
    }

    [Fact]
    public void A_Well_Formed_File_Reports_No_Implied_Prefix()
    {
        DocumentReadResult result = Read(Flat("<text:p>x</text:p>"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.xml.prefix");
    }

    [Fact]
    public void An_Inline_Dtd_Is_Prohibited()
    {
        string xml = Flat("<text:p>&e;</text:p>").Replace(
            "<office:document ",
            "<!DOCTYPE office:document [<!ENTITY e \"expanded\">]>\n<office:document ",
            StringComparison.Ordinal);

        DocumentReadResult result = Read(xml);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.xml");
    }

    [Fact]
    public void The_Probe_Is_Certain_Of_A_Flat_Text_Document_With_No_Hint()
    {
        DocumentProbeResult result = Probe(Flat("<text:p>x</text:p>"));

        Assert.Equal(DocumentProbeConfidence.Certain, result.Confidence);
        Assert.Equal("ODT", result.FormatName);
        Assert.Equal(OdtTestPackage.TextMediaType, result.MimeType);
    }

    [Fact]
    public void The_Probe_Does_Not_Claim_A_Flat_Spreadsheet_Even_With_A_Hint()
    {
        DocumentProbeResult result = Probe(
            Flat("<text:p>x</text:p>", mediaType: "application/vnd.oasis.opendocument.spreadsheet"),
            "sheet.fodt");

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void A_Root_That_States_No_Media_Type_Leans_On_The_Hint()
    {
        string xml = Flat("<text:p>x</text:p>", mediaType: null);

        Assert.Equal(DocumentProbeConfidence.Medium, Probe(xml).Confidence);
        Assert.Equal(DocumentProbeConfidence.High, Probe(xml, "doc.fodt").Confidence);
    }

    [Fact]
    public void A_Prefix_That_Ends_Before_The_Media_Type_Still_Sees_The_Root()
    {
        // LibreOffice states office:mimetype last, after some forty namespace
        // declarations; a short probe prefix can end before it.
        byte[] bytes = Encoding.UTF8.GetBytes(Flat("<text:p>x</text:p>"));
        int cut = Encoding.UTF8.GetString(bytes).IndexOf("office:mimetype", StringComparison.Ordinal);

        DocumentProbeResult result = new OdtDocumentCodec().Probe(new DocumentProbeRequest(
            bytes.AsMemory(0, cut),
            new DocumentSourceHints("doc.fodt")));

        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
    }

    [Fact]
    public void A_Fodt_Hint_Alone_Is_A_Low_Match()
    {
        DocumentProbeResult result = Probe("not xml at all", "doc.fodt");

        Assert.Equal(DocumentProbeConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Other_Xml_Is_Not_Claimed()
    {
        Assert.False(Probe("<?xml version=\"1.0\"?><html><body/></html>").IsMatch);
        Assert.False(Probe(
            "<?xml version=\"1.0\"?><document xmlns=\"urn:example\"/>").IsMatch);
    }

    [Fact]
    public void The_Descriptor_Still_Claims_Only_Odt_Because_The_Codec_Writes_Packages()
    {
        // Claiming .fodt would make "-o out.fodt" write a ZIP under a flat
        // file's name. The probe recognises the content instead.
        Assert.False(new OdtDocumentCodec().Descriptor.MatchesExtension(".fodt"));
    }
}
