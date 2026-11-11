using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX pages one side drew blank (office issue #236): EMF text in an OLE
/// preview, a field instruction that runs over paragraph marks, and a picture
/// in a table cell positioned against the page.
/// </summary>
public sealed class DocxOfficeIssue236Tests
{
    [Fact]
    public void An_Emf_Media_Part_Draws_Its_Text()
    {
        // fdo80523_pptm.docx: a presentation's preview whose only ink is
        // EXTTEXTOUTW in an EXTCREATEFONTINDIRECTW font. A white rectangle
        // gives the rasterizer something it drew before text was read.
        var emf = new List<byte>();
        void Int(int value) => emf.AddRange(BitConverter.GetBytes(value));
        void Short(short value) => emf.AddRange(BitConverter.GetBytes(value));
        // Header: one device unit per 0.1 mm, a 40 x 20 mm frame.
        Int(1); Int(88); Int(0); Int(0); Int(400); Int(200); Int(0); Int(0); Int(4000); Int(2000);
        Int(0x464D4520); Int(0x10000); Int(0); Int(7); Int(1); Int(0); Int(0); Int(0);
        Int(1000); Int(1000); Int(100); Int(100);
        // CREATEBRUSHINDIRECT 1 (white, solid), SELECTOBJECT, NULL_PEN, RECTANGLE over the frame.
        Int(39); Int(24); Int(1); Int(0); Int(0x00FFFFFF); Int(0);
        Int(37); Int(12); Int(1);
        Int(37); Int(12); Int(unchecked((int)0x80000008));
        Int(43); Int(24); Int(0); Int(0); Int(400); Int(200);
        // EXTCREATEFONTINDIRECTW 2: an 80-unit em, regular, face "Arial".
        int fontStart = emf.Count;
        Int(82); Int(368); Int(2); Int(-80); Int(0); Int(0); Int(0); Int(400);
        emf.AddRange(new byte[8]); // italic, underline, strike-out, charset and the precision bytes
        byte[] face = new byte[64];
        System.Text.Encoding.Unicode.GetBytes("Arial").CopyTo(face, 0);
        emf.AddRange(face);
        emf.AddRange(new byte[368 - (emf.Count - fontStart)]);
        Int(37); Int(12); Int(2);
        Int(22); Int(12); Int(24);          // SETTEXTALIGN: TA_BASELINE
        Int(24); Int(12); Int(0x000000);    // SETTEXTCOLOR: black
        // EXTTEXTOUTW "HH" at (40, 150), no spacing array.
        Int(84); Int(84); Int(0); Int(0); Int(0); Int(0); Int(1);
        Int(0); Int(0);                      // exScale, eyScale
        Int(40); Int(150); Int(2); Int(76); Int(0); Int(0); Int(0); Int(-1); Int(-1); Int(0);
        Short((short)'H'); Short((short)'H'); Short(0); Short(0);
        Int(14); Int(20); Int(0); Int(0); Int(20); // EOF

        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.VmlPictureRun("rId7", "width:113pt;height:57pt") + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.emf"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.emf"] = [.. emf] });

        InlineImage image = Assert.IsType<InlineImage>(
            Assert.Single(Assert.Single(result.Document.Paragraphs).Runs).Style.Image);
        Assert.Equal("image/png", image.ContentType);
        (int width, int height, byte[] rgba) = DecodeRasterizedPng(image.Data.ToArray());
        int dark = 0;
        for (int i = 0; i < width * height; i++)
            if (rgba[i * 4 + 3] > 0 && rgba[i * 4] < 128)
                dark++;
        Assert.True(dark > 20, "the EMF's text drew no ink");

        // The ink lies left of the frame's middle and above the baseline at 75% of its height.
        int right = 0, bottom = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (rgba[(y * width + x) * 4 + 3] > 0 && rgba[(y * width + x) * 4] < 128)
                    (right, bottom) = (Math.Max(right, x), Math.Max(bottom, y));
        Assert.True(right < width / 2, "the text ran past where two letters end");
        Assert.True(bottom <= height * 0.8, "the text hangs below its baseline");
    }

    [Fact]
    public void A_Paragraph_Mark_Inside_A_Field_Instruction_Is_Not_A_Paragraph()
    {
        // tdf125038b.docx: an IF whose instruction runs over a paragraph mark.
        // The mark is field code; the result starts the first line.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r>" +
            "<w:r><w:instrText xml:space='preserve'> IF \"1234\" = \"\" \"\" \"phone: 1234</w:instrText></w:r></w:p>" +
            "<w:p><w:r><w:instrText xml:space='preserve'>\" </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>phone: 1234</w:t></w:r></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>");

        Assert.Equal(["phone: 1234", ""], result.Document.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void A_Paragraph_Mark_In_A_Fields_Result_Stays_A_Paragraph()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r>" +
            "<w:r><w:instrText>QUOTE \"a\"</w:instrText></w:r><w:r><w:fldChar w:fldCharType='separate'/></w:r>" +
            "<w:r><w:t>one</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>two</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>");

        Assert.Equal(["one", "two"], result.Document.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void A_Field_Instruction_That_Never_Ends_Joins_A_Bounded_Run_Of_Paragraphs()
    {
        string body = "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText>QUOTE</w:instrText></w:r></w:p>" +
            string.Concat(Enumerable.Range(0, 200).Select(i => "<w:p><w:r><w:t>p" + i + "</w:t></w:r></w:p>"));

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        Assert.True(result.Document.Paragraphs.Count > 100, "an unterminated instruction folded the document into a few paragraphs");
    }

    [Fact]
    public void A_Page_Relative_Picture_Laid_Out_In_A_Cell_Hangs_From_The_Cells_First_Paragraph()
    {
        // tdf115094.docx: a logo in a cell's second paragraph, 30 pt below the
        // "page", sits 30 pt below the cell's top in LibreOffice and Word.
        string picture = DocxTestPackage.AnchoredDrawingRun("rId7", 914400, 457200, offsetXEmus: 127000, offsetYEmus: 381000)
            .Replace("<wp:positionV relativeFrom=\"paragraph\">", "<wp:positionV relativeFrom=\"page\">")
            .Replace("simplePos=\"0\"", "simplePos=\"0\" layoutInCell=\"1\"");
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p><w:r><w:t>before</w:t></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>first</w:t></w:r></w:p><w:p>" + picture + "</w:p></w:tc></w:tr></w:tbl>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(1, shape.ParagraphIndex);
        Assert.False(shape.OffsetFromPage);
        Assert.True(shape.OffsetXFromCell);
        Assert.Equal(30, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Page_Relative_Picture_Outside_A_Table_Stays_On_The_Page()
    {
        string picture = DocxTestPackage.AnchoredDrawingRun("rId7", 914400, 457200, offsetYEmus: 381000)
            .Replace("<wp:positionV relativeFrom=\"paragraph\">", "<wp:positionV relativeFrom=\"page\">");
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p><w:r><w:t>first</w:t></w:r></w:p><w:p>" + picture + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(1, shape.ParagraphIndex);
        Assert.True(shape.OffsetFromPage);
        Assert.False(shape.OffsetXFromCell);
    }

    /// <summary>Decodes the unfiltered RGBA PNG the EMF rasterizer writes.</summary>
    private static (int Width, int Height, byte[] Rgba) DecodeRasterizedPng(byte[] png)
    {
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        using var idat = new MemoryStream();
        for (int at = 8; at + 8 <= png.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (png.AsSpan(at + 4, 4).SequenceEqual("IDAT"u8))
                idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        byte[] rows = raw.ToArray();
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            Array.Copy(rows, y * (width * 4 + 1) + 1, rgba, y * width * 4, width * 4);
        return (width, height, rgba);
    }
}
