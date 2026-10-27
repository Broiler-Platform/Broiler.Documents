using Broiler.Documents.Resources;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Floating pictures in ODT: a frame anchored beside the text rather than in it.
/// One inside a paragraph used to be placed in the text; one standing between
/// paragraphs was skipped outright, which lost the picture altogether.
/// </summary>
public sealed class OdtFloatingPictureTests
{
    [Theory]
    [InlineData("left", TextAlignment.Left)]
    [InlineData("center", TextAlignment.Center)]
    [InlineData("right", TextAlignment.Right)]
    public void Cell_Picture_Alignment_Is_Read_And_Round_Trips(string position, TextAlignment alignment)
    {
        string styles = OdtTestPackage.Style("image", "<style:graphic-properties style:horizontal-rel=\"paragraph\" " +
            "style:horizontal-pos=\"" + position + "\"/>", family: "graphic");
        var result = OdtTestPackage.ReadWithPictures(
            "<table:table><table:table-row><table:table-cell><text:p>" + Frame(styleName: "image") +
            "</text:p></table:table-cell></table:table-row></table:table>", Pictures(),
            options: RoundTripReadOptions, automaticStylesXml: styles);
        Assert.Equal(alignment, Assert.Single(result.Document.Shapes).CellHorizontalAlignment);
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(result.Document,
            new DocumentWriteOptions(resources: result.Resources)));
        var restored = new OdtDocumentCodec().Read(stream, RoundTripReadOptions).Document;
        Assert.Equal(alignment, Assert.Single(restored.Shapes).CellHorizontalAlignment);
    }

    [Theory]
    [InlineData("paragraph", true)]
    [InlineData("paragraph-content", true)]
    [InlineData("page", false)]
    [InlineData("page-content", false)]
    public void A_Table_Pictures_Horizontal_Reference_Is_Preserved(string reference, bool fromCell)
    {
        string styles = OdtTestPackage.Style("image", "<style:graphic-properties style:horizontal-rel=\"" +
            reference + "\"/>", family: "graphic");
        var document = OdtTestPackage.ReadWithPictures(
            "<table:table><table:table-row><table:table-cell><text:p>" + Frame(styleName: "image") +
            "</text:p></table:table-cell></table:table-row></table:table>", Pictures(),
            options: RoundTripReadOptions, automaticStylesXml: styles).Document;

        Assert.Equal(fromCell, Assert.Single(document.Shapes).OffsetXFromCell);
    }

    [Fact]
    public void Missing_And_Empty_Graphic_Styles_Default_To_Top_And_Bottom_Wrapping()
    {
        Assert.Equal(ShapeWrap.TopAndBottom, Assert.Single(ReadInParagraph(Frame()).Document.Shapes).Wrap);
        Assert.Equal(ShapeWrap.TopAndBottom, Assert.Single(ReadInParagraph(Frame(styleName: "empty"),
            OdtTestPackage.Style("empty", "<style:graphic-properties/>", family: "graphic")).Document.Shapes).Wrap);
    }

    [Theory]
    [InlineData(false, ShapeWrap.None)]
    [InlineData(true, ShapeWrap.TopAndBottom)]
    public void Graphic_Defaults_Apply_Below_Named_Style_Overrides(bool overrides, ShapeWrap expected)
    {
        string styles = "<office:styles><style:default-style style:family=\"graphic\">" +
            "<style:graphic-properties style:wrap=\"run-through\"/></style:default-style></office:styles>";
        string named = OdtTestPackage.Style("named", "<style:graphic-properties" +
            (overrides ? " style:wrap=\"none\"" : "") + "/>", family: "graphic");
        using var stream = new MemoryStream(OdtTestPackage.FromBody("<text:p>" + Frame(styleName: "named") +
            "body</text:p>", named, stylesPartInnerXml: styles, binaryParts: Pictures()));
        Assert.Equal(expected, Assert.Single(new OdtDocumentCodec().Read(stream, RoundTripReadOptions).Document.Shapes).Wrap);
    }

    [Theory]
    [InlineData("500pt", "450pt", "432pt", "648pt", -75, 0)]
    [InlineData("500pt", "700pt", "100pt", "100pt", 257, 548)]
    [InlineData("-20pt", "-30pt", "100pt", "100pt", -75, -30)]
    [InlineData("80pt", "90pt", "100pt", "100pt", 5, 90)]
    public void Page_anchored_pictures_are_constrained_to_the_paper(
        string x, string y, string width, string height, double expectedX, double expectedY)
    {
        string styles = "<office:automatic-styles><style:page-layout style:name=\"page\">" +
            "<style:page-layout-properties fo:page-width=\"432pt\" fo:page-height=\"648pt\" " +
            "fo:margin-left=\"75pt\" fo:margin-right=\"68pt\" fo:margin-top=\"68pt\" fo:margin-bottom=\"75pt\"/>" +
            "</style:page-layout></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"page\"/></office:master-styles>";
        string frame = Frame("page", x, y, width, height).Replace(
            "text:anchor-type=\"page\"", "text:anchor-type=\"page\" text:anchor-page-number=\"1\"", StringComparison.Ordinal);
        using var stream = new MemoryStream(OdtTestPackage.FromBody(
            frame + "<text:p/>", stylesPartInnerXml: styles, binaryParts: Pictures()));
        DocumentShape shape = Assert.Single(new OdtDocumentCodec().Read(stream).Document.Shapes);
        Assert.Equal(expectedX, shape.OffsetX, 2);
        Assert.Equal(expectedY, shape.OffsetY, 2);
    }

    [Fact]
    public void A_Character_Anchor_Floats_Instead_Of_Taking_Space_In_The_Line()
    {
        DocumentReadResult result = ReadInParagraph(Frame(anchor: "char", x: "12pt", y: "18pt",
            width: "100pt", height: "200pt"));
        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(12, shape.OffsetX);
        Assert.Equal(18, shape.OffsetY);
        Assert.Equal(100, shape.Width);
        Assert.Equal(200, shape.Height);
        Assert.False(shape.IsPageAnchored);
        Assert.DoesNotContain(result.Document.Paragraphs.SelectMany(paragraph => paragraph.Runs), run => run.Style.Image is not null);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.anchored");
    }

    private static Dictionary<string, byte[]> Pictures() =>
        new(StringComparer.Ordinal) { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng };

    /// <summary>A graphic style stating only which side of the text it sits on.</summary>
    private static string RunThroughStyle(string runThrough) =>
        OdtTestPackage.Style(
            "gr1",
            "<style:graphic-properties style:run-through=\"" + runThrough + "\"/>",
            family: "graphic");

    private static string Frame(
        string anchor = "paragraph",
        string x = "-1in",
        string y = "0.5in",
        string width = "1in",
        string height = "0.5in",
        string? styleName = null) =>
        "<draw:frame draw:name=\"Image1\" text:anchor-type=\"" + anchor + "\" " +
        (styleName is null ? string.Empty : "draw:style-name=\"" + styleName + "\" ") +
        "svg:x=\"" + x + "\" svg:y=\"" + y + "\" " +
        (width.Length == 0 ? string.Empty : "svg:width=\"" + width + "\" ") +
        (height.Length == 0 ? string.Empty : "svg:height=\"" + height + "\" ") + ">" +
        "<draw:image xlink:href=\"Pictures/logo.png\" xlink:type=\"simple\" xlink:show=\"embed\"/>" +
        "</draw:frame>";

    private static DocumentReadResult ReadInParagraph(string frame, string automaticStylesXml = "") =>
        OdtTestPackage.ReadWithPictures(
            "<text:p>" + frame + "body</text:p>",
            Pictures(),
            options: RoundTripReadOptions,
            automaticStylesXml: automaticStylesXml);

    [Fact]
    public void Reads_A_Paragraph_Anchored_Frame_As_A_Floating_Picture()
    {
        DocumentShape shape = Assert.Single(ReadInParagraph(Frame()).Document.Shapes);

        Assert.True(shape.HasImage);
        Assert.Equal("image/png", shape.Image!.ContentType);
        Assert.Equal(OdtTestPackage.OnePixelPng, shape.Image.Data.ToArray());
        Assert.Equal(-72, shape.OffsetX, 1);
        Assert.Equal(36, shape.OffsetY, 1);
        Assert.Equal(72, shape.Width, 1);
        Assert.Equal(36, shape.Height, 1);
    }

    [Fact]
    public void Keeps_A_Floating_Picture_Out_Of_The_Text()
    {
        RichTextDocument document = ReadInParagraph(Frame()).Document;

        Assert.Equal("body", document.PlainText);
        Assert.DoesNotContain(
            Assert.Single(document.Paragraphs).Runs,
            run => run.Style.Image is not null);
    }

    [Fact]
    public void Reads_A_Frame_Between_Paragraphs_Rather_Than_Skipping_It()
    {
        // A page-anchored frame at block level held no body text, so it was
        // skipped - and the picture went with it.
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            OdtTestPackage.Paragraph("first") + Frame(anchor: "page"),
            Pictures());

        Assert.True(Assert.Single(result.Document.Shapes).HasImage);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.frame.block");
    }

    [Fact]
    public void Leaves_A_Character_Anchored_Picture_In_The_Text()
    {
        DocumentReadResult result = ReadInParagraph(Frame(anchor: "as-char"));

        Assert.Empty(result.Document.Shapes);
        Assert.Contains(InlineImage.PlaceholderText, result.Document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.image.anchored");
    }

    [Fact]
    public void Keeps_A_Floating_Picture_In_The_Text_When_It_States_No_Box()
    {
        DocumentReadResult result = ReadInParagraph(Frame(width: "", height: ""));

        Assert.Empty(result.Document.Shapes);
        Assert.Contains(InlineImage.PlaceholderText, result.Document.PlainText, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.anchored");
    }

    [Theory]
    [InlineData("background", true)]
    [InlineData("foreground", false)]
    public void Reads_The_Side_Of_The_Text_A_Frame_Was_Authored_On(string runThrough, bool expected)
    {
        DocumentReadResult result = ReadInParagraph(
            Frame(styleName: "gr1"),
            RunThroughStyle(runThrough));

        Assert.Equal(expected, Assert.Single(result.Document.Shapes).BehindText);
    }

    [Fact]
    public void Reads_A_Frame_Whose_Style_States_No_Run_Through_As_Behind_The_Text()
    {
        // Not ODF's own default, which is in front. It is the answer this reader
        // has always given, and the one that leaves the letter readable when the
        // guess is wrong.
        Assert.True(Assert.Single(ReadInParagraph(Frame()).Document.Shapes).BehindText);
    }

    [Theory]
    [InlineData("background", true)]
    [InlineData("foreground", false)]
    public void Keeps_The_Side_Of_The_Text_Through_A_Round_Trip(string runThrough, bool expected)
    {
        // The bug this covers on the DOCX side: the writer stated one layer
        // whatever it was given, so a stamp over a letter came back under it.
        DocumentReadResult read = ReadInParagraph(
            Frame(styleName: "gr1"),
            RunThroughStyle(runThrough));
        RichTextDocument source = read.Document;
        var writeOptions = new DocumentWriteOptions(resources: read.Resources);

        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source, writeOptions), writable: false);
        RichTextDocument actual = new OdtDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        Assert.Equal(expected, Assert.Single(source.Shapes).BehindText);
        Assert.Equal(expected, Assert.Single(actual.Shapes).BehindText);
    }

    [Fact]
    public void Says_Only_What_Is_Still_Approximated()
    {
        DocumentDiagnostic note = Assert.Single(
            ReadInParagraph(Frame()).Diagnostics.Where(d => d.Code == "odt.image.anchored"));

        // The frame keeps its layer and its wrapping now. What it does not keep
        // is the picture's outline, so that is what the note is left saying.
        Assert.Contains("outline", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("z-order", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not represented", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Floating_Picture_Round_Trips_As_A_Paragraph_Anchored_Frame()
    {
        DocumentReadResult read = ReadInParagraph(Frame());
        RichTextDocument source = read.Document;
        var writeOptions = new DocumentWriteOptions(resources: read.Resources);

        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source, writeOptions), writable: false);
        RichTextDocument actual = new OdtDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        DocumentShape shape = Assert.Single(actual.Shapes);
        Assert.Equal(-72, shape.OffsetX, 1);
        Assert.Equal(36, shape.OffsetY, 1);
        Assert.Equal(72, shape.Width, 1);
        Assert.Equal(36, shape.Height, 1);
        Assert.Equal(OdtTestPackage.OnePixelPng, shape.Image!.Data.ToArray());
        Assert.Equal("body", actual.PlainText);
    }

    /// <summary>
    /// Admits <paramref name="image"/> under a policy that permits writing it,
    /// and returns the image bound to that decision together with the options a
    /// writer needs.
    /// </summary>
    /// <remarks>
    /// A writer refuses a picture nobody decided on, so a write test has to say
    /// which decision it is testing under. Reading a document is not that
    /// decision: it grants extraction into the model and nothing that puts the
    /// bytes into an output.
    /// </remarks>
    private static (InlineImage Image, DocumentWriteOptions Options) Writable(InlineImage image)
    {
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage admitted = builder.AdmitImage(
            image,
            DocumentResourceProvenance.CallerSupplied,
            DocumentResourceDisposition.Embedded);

        return (admitted, new DocumentWriteOptions(resources: builder.Build()));
    }

    /// <summary>Read options that also permit writing what was read back out.</summary>
    private static DocumentReadOptions RoundTripReadOptions { get; } =
        new(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments);
}
