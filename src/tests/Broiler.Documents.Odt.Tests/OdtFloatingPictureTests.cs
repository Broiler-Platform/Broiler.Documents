namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Floating pictures in ODT: a frame anchored beside the text rather than in it.
/// One inside a paragraph used to be placed in the text; one standing between
/// paragraphs was skipped outright, which lost the picture altogether.
/// </summary>
public sealed class OdtFloatingPictureTests
{
    private static Dictionary<string, byte[]> Pictures() =>
        new(StringComparer.Ordinal) { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng };

    private static string Frame(
        string anchor = "paragraph",
        string x = "-1in",
        string y = "0.5in",
        string width = "1in",
        string height = "0.5in") =>
        "<draw:frame draw:name=\"Image1\" text:anchor-type=\"" + anchor + "\" " +
        "svg:x=\"" + x + "\" svg:y=\"" + y + "\" " +
        (width.Length == 0 ? string.Empty : "svg:width=\"" + width + "\" ") +
        (height.Length == 0 ? string.Empty : "svg:height=\"" + height + "\" ") + ">" +
        "<draw:image xlink:href=\"Pictures/logo.png\" xlink:type=\"simple\" xlink:show=\"embed\"/>" +
        "</draw:frame>";

    private static DocumentReadResult ReadInParagraph(string frame) =>
        OdtTestPackage.ReadWithPictures("<text:p>" + frame + "body</text:p>", Pictures());

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

    [Fact]
    public void A_Floating_Picture_Round_Trips_As_A_Paragraph_Anchored_Frame()
    {
        RichTextDocument source = ReadInParagraph(Frame()).Document;

        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument actual = new OdtDocumentCodec().Read(stream).Document;

        DocumentShape shape = Assert.Single(actual.Shapes);
        Assert.Equal(-72, shape.OffsetX, 1);
        Assert.Equal(36, shape.OffsetY, 1);
        Assert.Equal(72, shape.Width, 1);
        Assert.Equal(36, shape.Height, 1);
        Assert.Equal(OdtTestPackage.OnePixelPng, shape.Image!.Data.ToArray());
        Assert.Equal("body", actual.PlainText);
    }
}
