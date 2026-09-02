using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers anchored (floating) pictures: the logo a letterhead hangs over its
/// stripe. Every one of them used to be placed in the text, which pushed the
/// whole letter down by the height of the picture; they are floating shapes
/// carrying the image now, at the box the anchor states.
/// </summary>
public sealed class DocxFloatingPictureTests
{
    /// <summary>914400 EMUs per inch.</summary>
    private const long OneInchEmus = 914400;

    private static Dictionary<string, byte[]> Media() =>
        new(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng };

    private static DocumentReadResult Read(string runXml) =>
        DocxTestPackage.ReadWithMedia(
            "<w:p>" + runXml + "<w:r><w:t>body</w:t></w:r></w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media(),
            RoundTripReadOptions);

    private static string Anchored(
        long offsetXEmus = -OneInchEmus,
        long offsetYEmus = OneInchEmus / 2,
        bool withExtent = true,
        string? altText = null,
        string? behindDoc = "1") =>
        DocxTestPackage.AnchoredDrawingRun(
            "rId7",
            OneInchEmus,
            OneInchEmus / 2,
            offsetXEmus,
            offsetYEmus,
            withExtent,
            altText,
            behindDoc);

    [Fact(Timeout = 600000)]
    public void Reads_An_Anchored_Picture_As_A_Floating_Shape()
    {
        DocumentShape shape = Assert.Single(Read(Anchored()).Document.Shapes);

        InlineImage image = Assert.IsType<InlineImage>(shape.Image);
        Assert.True(shape.HasImage);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(DocxTestPackage.OnePixelPng, image.Data.ToArray());
    }

    [Fact(Timeout = 600000)]
    public void Places_A_Floating_Picture_In_Points_From_The_Text_Column()
    {
        DocumentShape shape = Assert.Single(Read(Anchored()).Document.Shapes);

        // 12700 EMU to the point, and a negative offset puts it in the margin.
        Assert.Equal(-72, shape.OffsetX, 3);
        Assert.Equal(36, shape.OffsetY, 3);
        Assert.Equal(72, shape.Width, 3);
        Assert.Equal(36, shape.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void Keeps_A_Floating_Picture_Out_Of_The_Text()
    {
        RichTextDocument document = Read(Anchored()).Document;

        // The letter reads as the letter: no object replacement character, and no
        // line whose height is the height of the logo.
        Assert.Equal("body", document.PlainText);
        Assert.DoesNotContain(
            Assert.Single(document.Paragraphs).Runs,
            run => run.Style.Image is not null);
    }

    [Fact(Timeout = 600000)]
    public void Anchors_A_Floating_Picture_To_The_Paragraph_It_Sits_In()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            DocxTestPackage.Paragraph("first") + "<w:p>" + Anchored() + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media());

        Assert.Equal(1, Assert.Single(result.Document.Shapes).ParagraphIndex);
    }

    [Fact(Timeout = 600000)]
    public void Says_Only_What_Is_Still_Approximated()
    {
        DocumentDiagnostic note = Assert.Single(
            Read(Anchored()).Diagnostics.Where(d => d.Code == "docx.image.anchored"));

        // The anchor asked for square wrapping and to sit behind the text, and it
        // gets both now. What it does not get is the picture's outline, so that
        // is what the note is left saying.
        Assert.Equal(DocumentDiagnosticSeverity.Warning, note.Severity);
        Assert.Contains("outline", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("z-order", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not represented", note.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Square_Wrap_The_Anchor_States()
    {
        // The fixture's anchor states wrapSquare with wrapText="bothSides".
        DocumentShape shape = Assert.Single(Read(Anchored()).Document.Shapes);

        Assert.Equal(ShapeWrap.Square, shape.Wrap);
        Assert.Equal(WrapSide.Largest, shape.WrapSide);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Picture_Stacked_Behind_The_Text_As_Behind_It()
    {
        Assert.True(Assert.Single(Read(Anchored(behindDoc: "1")).Document.Shapes).BehindText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Picture_Stacked_In_Front_Of_The_Text_As_In_Front_Of_It()
    {
        Assert.False(Assert.Single(Read(Anchored(behindDoc: "0")).Document.Shapes).BehindText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_An_Anchor_That_States_No_Stacking_As_Behind_The_Text()
    {
        // behindDoc is required on wp:anchor, so this is a malformed producer. The
        // letterhead answer is the safe one: a box over the text hides the text.
        Assert.True(Assert.Single(Read(Anchored(behindDoc: null)).Document.Shapes).BehindText);
    }

    [Theory(Timeout = 600000)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void Keeps_The_Stacking_Of_A_Floating_Picture_Through_A_Round_Trip(
        string behindDoc,
        bool expected)
    {
        // The bug this covers: the writer stated behindDoc="0" whatever it was
        // given, so a letterhead stripe read from behind the text came back from
        // Word painted over the letter.
        DocumentReadResult read = Read(Anchored(behindDoc: behindDoc));
        RichTextDocument source = read.Document;
        var writeOptions = new DocumentWriteOptions(resources: read.Resources);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source, writeOptions), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        Assert.Equal(expected, Assert.Single(source.Shapes).BehindText);
        Assert.Equal(expected, Assert.Single(actual.Shapes).BehindText);
    }

    [Theory(Timeout = 600000)]
    [InlineData("1")]
    [InlineData("0")]
    public void Writes_The_Stacking_It_Read_Into_The_Anchor(string behindDoc)
    {
        DocumentReadResult read = Read(Anchored(behindDoc: behindDoc));
        RichTextDocument source = read.Document;
        var writeOptions = new DocumentWriteOptions(resources: read.Resources);

        using var package = new ZipArchive(
            new MemoryStream(DocxDocumentCodec.WriteToArray(source, writeOptions), writable: false),
            ZipArchiveMode.Read);
        using var reader = new StreamReader(package.GetEntry("word/document.xml")!.Open());
        string documentXml = reader.ReadToEnd();

        Assert.Contains("behindDoc=\"" + behindDoc + "\"", documentXml, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Counts_A_Floating_Picture_As_An_Image()
    {
        DocumentDiagnostic summary = Assert.Single(
            Read(Anchored()).Diagnostics.Where(d => d.Code == "docx.read.summary"));

        Assert.Contains("embedded 1 image(s)", summary.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Keeps_Alternative_Text_On_A_Floating_Picture()
    {
        DocumentShape shape = Assert.Single(Read(Anchored(altText: "the logo")).Document.Shapes);

        Assert.Equal("the logo", shape.Image!.AltText);
    }

    [Fact(Timeout = 600000)]
    public void Leaves_An_Inline_Picture_In_The_Text()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media());

        Assert.Empty(result.Document.Shapes);
        Assert.NotNull(Assert.Single(Assert.Single(result.Document.Paragraphs).Runs).Style.Image);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.anchored");
    }

    [Fact(Timeout = 600000)]
    public void Keeps_An_Anchored_Picture_In_The_Text_When_It_States_No_Box()
    {
        // wp:extent is where the box comes from. Without one there is nothing to
        // float the picture at, so it stays where it always was - and the note is
        // still reported, because the wrapping is still gone.
        DocumentReadResult result = Read(Anchored(withExtent: false));

        Assert.Empty(result.Document.Shapes);
        Assert.Contains(InlineImage.PlaceholderText, result.Document.PlainText, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.anchored");
    }

    [Fact(Timeout = 600000)]
    public void A_Floating_Picture_Survives_A_Round_Trip()
    {
        DocumentReadResult read = Read(Anchored(altText: "the logo"));
        RichTextDocument source = read.Document;
        var writeOptions = new DocumentWriteOptions(resources: read.Resources);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source, writeOptions), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        DocumentShape shape = Assert.Single(actual.Shapes);
        Assert.Equal(-72, shape.OffsetX, 3);
        Assert.Equal(36, shape.OffsetY, 3);
        Assert.Equal(72, shape.Width, 3);
        Assert.Equal(36, shape.Height, 3);
        Assert.Equal("the logo", shape.Image!.AltText);
        Assert.Equal(DocxTestPackage.OnePixelPng, shape.Image.Data.ToArray());
        Assert.Equal("body", actual.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Writes_A_Floating_Picture_As_An_Anchored_Picture()
    {
        DocumentReadResult read = Read(Anchored());
        RichTextDocument source = read.Document;
        var writeOptions = new DocumentWriteOptions(resources: read.Resources);

        using var package = new ZipArchive(
            new MemoryStream(DocxDocumentCodec.WriteToArray(source, writeOptions), writable: false),
            ZipArchiveMode.Read);
        using var reader = new StreamReader(package.GetEntry("word/document.xml")!.Open());
        string documentXml = reader.ReadToEnd();

        // A picture, not a shape filled with one: Word writes pic:pic under an
        // anchor, and a wps:wsp here would be a construct it does not.
        Assert.Contains("wp:anchor", documentXml, StringComparison.Ordinal);
        Assert.Contains("pic:pic", documentXml, StringComparison.Ordinal);
        Assert.DoesNotContain("wsp", documentXml, StringComparison.Ordinal);
        Assert.NotNull(package.GetEntry("word/media/image1.png"));
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
