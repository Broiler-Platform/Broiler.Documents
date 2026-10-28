using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers <c>wpg:wgp</c> drawing groups: several boxes under one anchor, each
/// stated in the group's own coordinate space. The reader took the first box
/// only, at the size of the whole group, so tdf167527's backdrop of a hatched
/// rectangle with a fade over it came back as the hatch alone.
/// </summary>
public sealed class DocxShapeGroupTests
{
    [Theory]
    [InlineData("<wps:bodyPr/>", 7.2, 3.6)]
    [InlineData("<wps:bodyPr lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\"/>", 0, 0)]
    public void Text_Box_Insets_Use_Their_Defaults_Or_Explicit_Zero(string properties, double left, double top)
    {
        string box = Box("2", 0, 0, 2540000, 1270000, "ffffff").Replace("<wps:bodyPr/>", properties);
        var shape = Assert.Single(Read(Group(Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000), box)).Document.Shapes);
        Assert.Equal(left, shape.TextPadding.Left, 3);
        Assert.Equal(top, shape.TextPadding.Top, 3);
    }

    [Fact]
    public void Grouped_Text_Box_Tables_Keep_Their_Own_Paragraph_Indices_And_Round_Trip()
    {
        string table = DocxTestPackage.Table([DocxTestPackage.Paragraph("A"), DocxTestPackage.Paragraph("B")]);
        string box = Box("2", 0, 0, 2540000, 1270000, "ffffff").Replace("<wps:bodyPr/>",
            "<wps:txbx><w:txbxContent>" + table + "</w:txbxContent></wps:txbx><wps:bodyPr/>");
        RichTextDocument document = Read(Group(Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000), box)).Document;
        DocumentShape shape = Assert.Single(document.Shapes);
        DocumentTable grid = Assert.Single(shape.Tables);
        Assert.Equal(0, grid.ParagraphIndex);
        Assert.Equal(2, grid.ParagraphCount);
        Assert.Equal(["A", "B"], shape.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.Empty(document.Tables);
        Assert.Equal("body", document.PlainText);

        using var written = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        DocumentShape restored = Assert.Single(new DocxDocumentCodec().Read(written).Document.Shapes);
        Assert.Equal(2, Assert.Single(restored.Tables).Rows[0].Cells.Count);
        Assert.Equal(["A", "B"], restored.Paragraphs.Select(paragraph => paragraph.Text));
    }

    private static string Box(string id, long x, long y, long cx, long cy, string color) =>
        "<wps:wsp><wps:cNvPr id=\"" + id + "\" name=\"r" + id + "\"/><wps:cNvSpPr/>" +
        "<wps:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm>" +
        "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
        "<a:solidFill><a:srgbClr val=\"" + color + "\"/></a:solidFill><a:ln><a:noFill/></a:ln></wps:spPr>" +
        "<wps:bodyPr/></wps:wsp>";

    private static string Transform(long x, long y, long cx, long cy, long chx, long chy, long chcx, long chcy) =>
        "<a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/>" +
        "<a:chOff x=\"" + chx + "\" y=\"" + chy + "\"/><a:chExt cx=\"" + chcx + "\" cy=\"" + chcy + "\"/></a:xfrm>";

    /// <summary>
    /// An anchored group 200 by 100 points at (10, 20) points from the column and
    /// its paragraph, holding <paramref name="childrenXml"/>.
    /// </summary>
    private static string Group(string transformXml, string childrenXml, string anchorAttributes = "") =>
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" behindDoc=\"1\" relativeHeight=\"7\"" + anchorAttributes + ">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>127000</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>254000</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"2540000\" cy=\"1270000\"/><wp:wrapNone/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\">" +
        "<wpg:wgp xmlns:wpg=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
        "<wpg:cNvGrpSpPr/><wpg:grpSpPr>" + transformXml + "</wpg:grpSpPr>" +
        childrenXml +
        "</wpg:wgp></a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static DocumentReadResult Read(string runXml) =>
        DocxTestPackage.ReadBody("<w:p>" + runXml + "<w:r><w:t>body</w:t></w:r></w:p>");

    [Fact]
    public void Reads_Every_Box_In_A_Group()
    {
        // Child space one to one with the group's box: 2540000 EMU across.
        RichTextDocument document = Read(Group(
            Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000),
            Box("2", 0, 0, 2540000, 1270000, "007929") + Box("3", 0, 0, 1270000, 635000, "5EAD35"))).Document;

        Assert.Equal(2, document.Shapes.Count);
        DocumentShape back = document.Shapes[0];
        DocumentShape front = document.Shapes[1];
        Assert.Equal(BColor.FromArgb(0x00, 0x79, 0x29), back.Fill!.Start);
        Assert.Equal(BColor.FromArgb(0x5E, 0xAD, 0x35), front.Fill!.Start);
        Assert.Equal((10, 20, 200, 100), (back.OffsetX, back.OffsetY, back.Width, back.Height));
        Assert.Equal((10, 20, 100, 50), (front.OffsetX, front.OffsetY, front.Width, front.Height));
        Assert.Equal("body", document.PlainText);
    }

    [Fact]
    public void Maps_Each_Box_Through_The_Groups_Child_Space()
    {
        // The group draws child space (1000, 2000)-(1400, 2200) into its 200 by
        // 100 point box: half a point to a unit across and down. A child at
        // (1200, 2100), 100 by 50 units, is (100, 50) points in, 50 by 25.
        DocumentShape shape = Assert.Single(Read(Group(
            Transform(0, 0, 2540000, 1270000, 1000, 2000, 400, 200),
            Box("2", 1200, 2100, 100, 50, "C0504D"))).Document.Shapes);

        Assert.Equal(10 + 100, shape.OffsetX, 6);
        Assert.Equal(20 + 50, shape.OffsetY, 6);
        Assert.Equal(50, shape.Width, 6);
        Assert.Equal(25, shape.Height, 6);
    }

    [Fact]
    public void Maps_A_Box_Through_A_Nested_Group()
    {
        // The outer space is 400 by 200 units for 200 by 100 points. The inner
        // group sits at (200, 0) there, 200 by 200 units (100 by 100 points), and
        // draws its own space (0, 0)-(10, 10) into that. Its child at (5, 5),
        // 5 by 5, is at (150, 50) points in, 50 by 50.
        DocumentShape shape = Assert.Single(Read(Group(
            Transform(0, 0, 2540000, 1270000, 0, 0, 400, 200),
            "<wpg:grpSp><wpg:cNvGrpSpPr/><wpg:grpSpPr>" + Transform(200, 0, 200, 200, 0, 0, 10, 10) + "</wpg:grpSpPr>" +
            Box("4", 5, 5, 5, 5, "4F81BD") + "</wpg:grpSp>")).Document.Shapes);

        Assert.Equal(10 + 150, shape.OffsetX, 6);
        Assert.Equal(20 + 50, shape.OffsetY, 6);
        Assert.Equal(50, shape.Width, 6);
        Assert.Equal(50, shape.Height, 6);
    }

    [Fact]
    public void A_Groups_Boxes_Keep_Its_Anchor_Stacking()
    {
        RichTextDocument document = Read(Group(
            Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000),
            Box("2", 0, 0, 2540000, 1270000, "007929") + Box("3", 0, 0, 1270000, 635000, "5EAD35"))).Document;

        Assert.All(document.Shapes, shape =>
        {
            Assert.True(shape.BehindText);
            Assert.Equal(7, shape.ZOrder);
            Assert.Equal(0, shape.ParagraphIndex);
        });
    }

    [Fact]
    public void A_Text_Box_In_A_Group_Keeps_Its_Text()
    {
        string textBox =
            "<wps:wsp><wps:cNvSpPr txBox=\"1\"/><wps:spPr>" +
            "<a:xfrm><a:off x=\"0\" y=\"635000\"/><a:ext cx=\"2540000\" cy=\"635000\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></wps:spPr>" +
            "<wps:txbx><w:txbxContent><w:p><w:r><w:t>Title</w:t></w:r></w:p></w:txbxContent></wps:txbx>" +
            "<wps:bodyPr/></wps:wsp>";

        RichTextDocument document = Read(Group(
            Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000),
            Box("2", 0, 0, 2540000, 1270000, "007929") + textBox)).Document;

        Assert.Equal(2, document.Shapes.Count);
        DocumentShape title = document.Shapes[1];
        Assert.Equal("Title", Assert.Single(title.Paragraphs).Text);
        Assert.Equal(20 + 50, title.OffsetY, 6);
        Assert.Equal("body", document.PlainText);
    }

    [Fact]
    public void A_Picture_In_A_Group_Is_Reported()
    {
        DocumentReadResult result = Read(Group(
            Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000),
            Box("2", 0, 0, 2540000, 1270000, "007929") + "<pic:pic/>"));

        Assert.Single(result.Document.Shapes);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.group");
    }

    private static string Picture(string attributes = "", string id = "rId7") =>
        "<pic:pic><pic:nvPicPr><pic:cNvPr id='8' name='logo' descr='group logo'/><pic:cNvPicPr/></pic:nvPicPr>" +
        "<pic:blipFill><a:blip r:embed='" + id + "'/><a:srcRect l='25000'/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
        "<pic:spPr><a:xfrm " + attributes + "><a:off x='127000' y='254000'/><a:ext cx='635000' cy='381000'/></a:xfrm>" +
        "<a:prstGeom prst='ellipse'/></pic:spPr></pic:pic>";

    private static DocumentReadResult ReadPictureGroup(string children, DocumentReadOptions? options = null,
        string? relationship = null, byte[]? bytes = null) => DocxTestPackage.ReadWithMedia(
            "<w:p>" + Group(Transform(0, 0, 2540000, 1270000, 0, 0, 1270000, 635000),
                Box("1", 0, 0, 1270000, 635000, "ffffff") + children) + "<w:r><w:t>body</w:t></w:r></w:p>",
            relationship ?? DocxTestPackage.ImageRelationship("rId7", "media/logo.png"),
            new Dictionary<string, byte[]> { ["word/media/logo.png"] = bytes ?? DocxTestPackage.OnePixelPng }, options);

    [Fact]
    public void Grouped_Picture_Uses_Mapped_Box_Crop_Metadata_And_Anchor()
    {
        var result = ReadPictureGroup(Picture(), new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments));
        var shape = Assert.Single(result.Document.Shapes, shape => shape.Image is not null);
        Assert.Equal(30, shape.OffsetX, 6);
        Assert.Equal(60, shape.OffsetY, 6);
        Assert.Equal(100, shape.Width, 6);
        Assert.Equal(60, shape.Height, 6);
        Assert.True(shape.BehindText);
        Assert.Equal(7, shape.ZOrder);
        var image = Assert.IsType<InlineImage>(shape.Image);
        Assert.Equal("group logo", image.AltText);
        Assert.Equal(100, image.Width);
        Assert.Equal(0.25, image.Presentation.CropLeft);
        Assert.Equal(ImageMask.Ellipse, image.Presentation.Mask);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "docx.shape.group");
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(result.Document, options: new DocumentWriteOptions(resources: result.Resources)));
        var restored = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes, shape => shape.Image is not null);
        Assert.Equal(shape.OffsetX, restored.OffsetX, 6);
        Assert.Equal(image.Data.ToArray(), restored.Image!.Data.ToArray());
        Assert.Equal(image.Presentation, restored.Image.Presentation);
    }

    [Fact]
    public void Nested_Picture_Keeps_Child_Order_And_Mapped_Origin()
    {
        string nested = "<wpg:grpSp><wpg:grpSpPr>" + Transform(127000, 0, 635000, 635000, 0, 0, 1270000, 1270000) +
            "</wpg:grpSpPr>" + Picture() + "</wpg:grpSp>";
        var shapes = ReadPictureGroup(Box("2", 0, 0, 100000, 100000, "ff0000") + nested + Picture()).Document.Shapes;
        Assert.Equal(4, shapes.Count);
        Assert.Null(shapes[1].Image);
        Assert.Equal(40, shapes[2].OffsetX, 6);
        Assert.Equal(40, shapes[2].OffsetY, 6);
        Assert.Equal(50, shapes[2].Width, 6);
        Assert.NotNull(shapes[3].Image);
    }

    [Theory]
    [InlineData("rot='5400000'")]
    [InlineData("flipH='1'")]
    [InlineData("flipV='true'")]
    public void Unsupported_Picture_Transforms_Do_Not_Fall_Back_To_The_Whole_Group(string attributes)
    {
        var result = ReadPictureGroup(Picture(attributes));
        Assert.DoesNotContain(result.Document.Shapes, shape => shape.Image is not null);
        Assert.DoesNotContain(result.Document.Paragraphs.SelectMany(p => p.Runs), run => run.Style.Image is not null);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.group");
    }

    [Fact]
    public void Grouped_Picture_Admission_Denial_Does_Not_Extract_An_Inline_Fallback()
    {
        var result = ReadPictureGroup(Picture(), new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.DenyAll));
        Assert.DoesNotContain(result.Document.Shapes, shape => shape.Image is not null);
        Assert.DoesNotContain(result.Document.Paragraphs.SelectMany(p => p.Runs), run => run.Style.Image is not null);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.denied");
    }

    [Fact]
    public void Missing_Grouped_Picture_Relationship_Is_Diagnosed()
    {
        var result = ReadPictureGroup(Picture(), relationship: "");
        Assert.DoesNotContain(result.Document.Shapes, shape => shape.Image is not null);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.relationship");
    }

    [Fact]
    public void External_Grouped_Pictures_Retain_Placeholder_And_Are_Diagnosed()
    {
        string relationship = DocxTestPackage.ImageRelationship("rId7", "https://example.test/logo.png").Replace("/>", " TargetMode='External'/>");
        var result = ReadPictureGroup(Picture(), relationship: relationship);
        DocumentShape shape = Assert.Single(result.Document.Shapes, s => s.Image is not null);
        Assert.True(shape.Image!.IsMissingPayload);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.external");
    }

    [Fact]
    public void Grouped_Picture_Size_Limit_Is_Enforced()
    {
        var result = ReadPictureGroup(Picture(), new DocumentReadOptions(new DocumentLimits(maxBinBytes: 4096)), bytes: new byte[4097]);
        Assert.DoesNotContain(result.Document.Shapes, shape => shape.Image is not null);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.limit");
    }

    [Fact]
    public void Picture_Only_Group_Retains_The_Diagnosed_Legacy_Fallback()
    {
        var result = DocxTestPackage.ReadWithMedia("<w:p>" +
            Group(Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000), Picture()) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/logo.png"),
            new Dictionary<string, byte[]> { ["word/media/logo.png"] = DocxTestPackage.OnePixelPng });
        var shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(200, shape.Width, 6);
        Assert.Equal(100, shape.Height, 6);
        Assert.Equal(10, shape.OffsetX, 6);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.group");
    }

    [Theory]
    [InlineData("cx='0'")]
    [InlineData("cx='999999999999'")]
    public void Invalid_Grouped_Picture_Extents_Are_Skipped(string extent)
    {
        var result = ReadPictureGroup(Picture().Replace("cx='635000'", extent));
        Assert.DoesNotContain(result.Document.Shapes, shape => shape.Image is not null);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.group");
    }

    [Fact]
    public void A_Group_In_A_Header_Is_Read_Onto_The_Running_Content()
    {
        string group = Group(
            Transform(0, 0, 2540000, 1270000, 0, 0, 2540000, 1270000),
            Box("2", 0, 0, 2540000, 1270000, "007929") + Box("3", 0, 635000, 2540000, 635000, "5EAD35"));
        DocumentReadResult result = DocxRunningContentTests.ReadWithHeader("<w:p>" + group + "</w:p>");

        IReadOnlyList<DocumentShape> shapes = result.Document.RunningContent.HeaderShapes(PageSelection.Default);
        Assert.Equal(2, shapes.Count);
        Assert.Equal(shapes[0].OffsetY + 50, shapes[1].OffsetY, 6);
        Assert.DoesNotContain(result.Document.Shapes, shape => shape.Image is not null);
    }
}
