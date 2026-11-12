namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 240, ODT pictures and frames: a paragraph-anchored
/// object aligned left, centered or right in its paragraph area, and the
/// margin Writer keeps above an object set at the top of its paragraph or
/// line (fdo68839.odt, tdf169024_verticalTopFrame.odt).
/// </summary>
public sealed class OdtIssue240OdtImagesTests
{
    // A 400 pt page with 50 pt margins: a 300 pt text column.
    private const string PageStyles =
        "<office:styles><style:style style:name='Frame' style:family='graphic'>" +
        "<style:graphic-properties fo:padding='0cm' fo:margin-left='6pt' fo:margin-right='8pt' fo:margin-top='5pt' " +
        "fo:margin-bottom='4pt'/></style:style></office:styles>" +
        "<office:automatic-styles><style:page-layout style:name='pm1'><style:page-layout-properties fo:page-width='400pt' " +
        "fo:page-height='600pt' fo:margin-left='50pt' fo:margin-right='50pt' fo:margin-top='50pt' fo:margin-bottom='50pt'/>" +
        "</style:page-layout></office:automatic-styles><office:master-styles>" +
        "<style:master-page style:name='Standard' style:page-layout-name='pm1'/></office:master-styles>";

    private static string Graphic(string name, string properties) =>
        OdtTestPackage.Style(name, "<style:graphic-properties " + properties + "/>", "graphic", "Frame");

    private static string TextFrame(string style, string anchor = "paragraph") =>
        "<draw:frame draw:style-name='" + style + "' text:anchor-type='" + anchor + "' svg:x='33pt' svg:y='44pt' " +
        "svg:width='100pt' svg:height='50pt'><draw:text-box><text:p>frame</text:p></draw:text-box></draw:frame>";

    [Theory]
    [InlineData("center", 100)]
    [InlineData("left", 6)]
    [InlineData("right", 192)]
    public void A_Paragraph_Anchored_Frame_Aligned_In_Its_Paragraph_Area_Ignores_Its_Offset(string position, double left)
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p>" + TextFrame("fr1") + "Anchor</text:p>",
            PageStyles,
            Graphic("fr1", "style:horizontal-pos='" + position + "' style:horizontal-rel='paragraph' " +
                "style:vertical-pos='from-top' style:vertical-rel='paragraph'")).Document;

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal(left, shape.OffsetX, 3);
        Assert.Equal(44, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Frame_Placed_From_The_Left_Keeps_Its_Offset()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p>" + TextFrame("fr1") + "Anchor</text:p>",
            PageStyles,
            Graphic("fr1", "style:horizontal-pos='from-left' style:horizontal-rel='paragraph'")).Document;

        Assert.Equal(33, Assert.Single(document.Shapes).OffsetX, 3);
    }

    [Fact]
    public void A_Frame_At_The_Top_Of_Its_Paragraph_Keeps_Its_Upper_Margin()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p>" + TextFrame("fr1") + "Anchor</text:p>",
            PageStyles,
            Graphic("fr1", "style:horizontal-pos='center' style:vertical-pos='top' style:vertical-rel='paragraph-content'")).Document;

        Assert.Equal(5, Assert.Single(document.Shapes).OffsetY, 3);
    }

    [Fact]
    public void A_Frame_Above_Its_Line_Keeps_Its_Lower_Margin_From_It()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p><draw:frame draw:style-name='fr1' text:anchor-type='char' svg:width='100pt' svg:height='50pt'>" +
            "<draw:text-box><text:p>frame</text:p></draw:text-box></draw:frame>Anchor</text:p>",
            PageStyles,
            Graphic("fr1", "style:horizontal-pos='center' style:horizontal-rel='paragraph' style:vertical-pos='top' " +
                "style:vertical-rel='line'")).Document;

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal(-54, shape.OffsetY, 3);
        Assert.Equal(100, shape.OffsetX, 3);
    }

    [Fact]
    public void A_Centered_Frame_In_A_Table_Cell_Is_Aligned_By_The_Layout()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<table:table><table:table-column/><table:table-row><table:table-cell><text:p>" + TextFrame("fr1") +
            "</text:p></table:table-cell></table:table-row></table:table>",
            PageStyles,
            Graphic("fr1", "style:horizontal-pos='center' style:horizontal-rel='paragraph'")).Document;

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal(33, shape.OffsetX, 3);
        Assert.Equal(TextAlignment.Center, shape.CellHorizontalAlignment);
    }

    [Fact]
    public void An_Object_Replacement_That_Draws_Nothing_Leaves_An_Unmarked_Empty_Frame()
    {
        // fdo68839.odt's empty embedded spreadsheet: its replacement metafile
        // only pushes, pops and sets clip regions, and LibreOffice draws nothing there.
        const string replacement = "ObjectReplacements/Object 1";
        string manifest =
            "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
            "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>" +
            "<manifest:file-entry manifest:full-path=\"ObjectReplacements/Object 1\" " +
            "manifest:media-type=\"application/x-openoffice-gdimetafile\"/>";
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p><draw:frame draw:name=\"Object1\" text:anchor-type=\"as-char\" svg:width=\"2in\" svg:height=\"1in\">" +
            "<draw:object xlink:href=\"./Object 1\"/><draw:image xlink:href=\"./" + replacement + "\"/></draw:frame></text:p>",
            new Dictionary<string, byte[]> { [replacement] = StateOnlySvm() }, manifest);

        InlineImage image = Assert.IsType<InlineImage>(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);
        Assert.True(image.IsMissingPayload);
        Assert.False(image.MarksMissingPayload);
        Assert.Equal(144, image.Width);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    /// <summary>A VCLMTF in 1/100 mm whose only actions are PUSH, LINECOLOR and POP.</summary>
    private static byte[] StateOnlySvm()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("VCLMTF"u8);
        w.Write((ushort)1);
        w.Write(49u);
        w.Write(0u);                                // compression
        w.Write((ushort)1); w.Write(27u);           // map mode compat header
        w.Write((ushort)0);                         // 1/100 mm
        w.Write(0); w.Write(0);                     // origin
        w.Write(1); w.Write(1); w.Write(1); w.Write(1);
        w.Write((byte)1);
        w.Write(2000); w.Write(1000);               // preferred size
        w.Write(3u);                                // actions
        w.Write((ushort)139); w.Write((ushort)1); w.Write(2u); w.Write((ushort)0xFFFF);       // PUSH all
        w.Write((ushort)132); w.Write((ushort)1); w.Write(5u); w.Write(0u); w.Write((byte)1); // LINECOLOR
        w.Write((ushort)140); w.Write((ushort)1); w.Write(0u);                                // POP
        w.Flush();
        return stream.ToArray();
    }
}
