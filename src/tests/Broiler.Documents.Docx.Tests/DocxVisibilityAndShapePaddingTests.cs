namespace Broiler.Documents.Docx.Tests;

public sealed class DocxVisibilityAndShapePaddingTests
{
    [Theory]
    [InlineData(CellVerticalAlignment.Top, false)]
    [InlineData(CellVerticalAlignment.Center, true)]
    [InlineData(CellVerticalAlignment.Bottom, false)]
    public void Text_Box_Alignment_And_Overflow_Round_Trip(CellVerticalAlignment alignment, bool overflow)
    {
        var document = RichTextDocument.FromPlainText("body").WithShapes([
            new DocumentShape(0, 0, 0, 100, 60, paragraphs: [RichTextParagraph.Plain("box")],
                textVerticalAlignment: alignment, textOverflow: overflow)]);
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        var shape = Assert.Single(DocxReader.Read(stream.ToArray(), new DocumentReadOptions()).Document.Shapes);
        Assert.Equal(alignment, shape.TextVerticalAlignment);
        Assert.Equal(overflow, shape.TextOverflow);
    }

    [Fact]
    public void Hidden_Character_Styles_Can_Be_Overridden_And_Round_Trip()
    {
        byte[] bytes = DocxTestPackage.FromBody("<w:p><w:r><w:rPr><w:rStyle w:val=\"hidden\"/></w:rPr><w:t>secret</w:t></w:r>" +
            "<w:r><w:rPr><w:rStyle w:val=\"hidden\"/><w:vanish w:val=\"0\"/></w:rPr><w:t>visible</w:t></w:r></w:p>",
            new Dictionary<string, string> { ["word/styles.xml"] =
                "<w:styles " + DocxTestPackage.BodyNamespaceDeclarations + "><w:style w:type=\"character\" w:styleId=\"hidden\"><w:rPr><w:vanish/></w:rPr></w:style></w:styles>" });
        var result = DocxReader.Read(bytes, new DocumentReadOptions());
        var paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("secretvisible", paragraph.Text);
        Assert.True(paragraph.Runs[0].Style.Hidden);
        Assert.False(paragraph.Runs[1].Style.Hidden);
        using var stream = new MemoryStream();
        DocxWriter.Write(result.Document, stream);
        var written = DocxReader.Read(stream.ToArray(), new DocumentReadOptions()).Document.Paragraphs[0];
        Assert.True(written.Runs[0].Style.Hidden);
        Assert.False(written.Runs[1].Style.Hidden);
    }

    [Fact]
    public void Text_Box_Padding_Is_Written_Explicitly_And_Round_Trips()
    {
        var padding = new TableCellPadding(7, 3, 9, 4);
        var document = RichTextDocument.FromPlainText("body").WithShapes([
            new DocumentShape(0, 0, 0, 100, 60, paragraphs: [RichTextParagraph.Plain("box")], textPadding: padding)]);
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        var shape = Assert.Single(DocxReader.Read(stream.ToArray(), new DocumentReadOptions()).Document.Shapes);
        Assert.Equal(padding, shape.TextPadding);
    }
}
