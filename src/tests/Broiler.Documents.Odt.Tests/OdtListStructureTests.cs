namespace Broiler.Documents.Odt.Tests;

public sealed class OdtListStructureTests
{
    private const string Levels = "<text:list-style style:name='N'>" +
        "<text:list-level-style-number text:level='1' style:num-format='1' style:num-suffix='.'/>" +
        "<text:list-level-style-number text:level='2' style:num-format='a' style:num-suffix=')'/>" +
        "</text:list-style>";

    private static string Paragraph(string text) => "<text:p>" + text + "</text:p>";
    private static string Item(string text) => "<text:list-item>" + Paragraph(text) + "</text:list-item>";
    private static DocumentReadResult Read(string body, string? levels = null) =>
        OdtTestPackage.ReadStyled(body, "<office:styles>" + (levels ?? Levels) + "</office:styles>");
    private static string Numbered(string text, string id = "x", string attributes = "") =>
        "<text:numbered-paragraph text:list-id='" + id + "' text:style-name='N' " + attributes + ">" +
        "<text:number>cached label</text:number>" + Paragraph(text) + "</text:numbered-paragraph>";
    private static IReadOnlyList<string?> Labels(RichTextDocument document)
    {
        var counter = new ListNumberer();
        return document.Paragraphs.Select(p => counter.Advance(p.Style)).ToArray();
    }

    [Fact]
    public void Numbered_Paragraphs_Keep_Content_And_Independent_List_Ids_Across_Body_Text()
    {
        var result = Read(Numbered("one") + Paragraph("between") + Numbered("two") + Numbered("fresh", "y") + Numbered("three"));
        Assert.Equal(["one", "between", "two", "fresh", "three"], result.Document.Paragraphs.Select(p => p.Text));
        Assert.Equal(["1.", null, "2.", "1.", "3."], Labels(result.Document));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.block.unsupported");
        using var stream = new MemoryStream();
        new OdtDocumentCodec().Write(result.Document, stream);
        stream.Position = 0;
        Assert.Equal(Labels(result.Document), Labels(new OdtDocumentCodec().Read(stream).Document));
    }

    [Fact]
    public void Numbered_Paragraph_Levels_And_Explicit_Restarts_Are_Retained()
    {
        var document = Read(Numbered("parent") + Numbered("child", attributes: "text:level='2'") +
            Numbered("next child", attributes: "text:level='2'") + Numbered("seven", attributes: "text:start-value='7'") +
            Numbered("eight")).Document;
        Assert.Equal(["1.", "a)", "b)", "7.", "8."], Labels(document));
    }

    [Theory]
    [InlineData("", "a)")]
    [InlineData("text:continue-numbering='false'", "a)")]
    [InlineData("text:continue-numbering='true'", "c)")]
    public void Sibling_Sub_Lists_Restart_Unless_They_Explicitly_Continue(string attributes, string expected)
    {
        var result = Read("<text:list text:style-name='N'><text:list-item>" + Paragraph("parent") +
            "<text:list>" + Item("first child") + Item("second child") + "</text:list>" + Paragraph("between") +
            "<text:list " + attributes + ">" + Item("next child") + "</text:list></text:list-item></text:list>");
        Assert.Equal(new string?[] { "1.", "a)", "b)", null, expected }, Labels(result.Document));
        using var stream = new MemoryStream();
        new OdtDocumentCodec().Write(result.Document, stream);
        stream.Position = 0;
        Assert.Equal(Labels(result.Document), Labels(new OdtDocumentCodec().Read(stream).Document));
    }

    [Fact]
    public void An_Explicit_Sub_List_Start_Wins_Over_The_Default_And_Is_Consumed_Once()
    {
        var result = Read("<text:list text:style-name='N'><text:list-item>" + Paragraph("parent") +
            "<text:list><text:list-item text:start-value='7'>" + Paragraph("seven") + "</text:list-item>" +
            Item("eight") + "</text:list></text:list-item></text:list>");
        Assert.Equal(new string?[] { "1.", "g)", "h)" }, Labels(result.Document));
    }

    [Fact]
    public void Empty_Sub_List_Items_Do_Not_Consume_The_Pending_Restart()
    {
        var result = Read("<text:list text:style-name='N'><text:list-item>" + Paragraph("parent") +
            "<text:list>" + Item("child") + "</text:list><text:list><text:list-item/>" + Item("restart") +
            "</text:list></text:list-item></text:list>");
        Assert.Equal(new string?[] { "1.", "a)", "a)" }, Labels(result.Document));
    }

    [Fact]
    public void Explicit_Continuation_Propagates_Through_Unnumbered_Structural_Wrappers()
    {
        string wrapper(string text) => "<text:list-item><text:list>" + Item(text) + "</text:list></text:list-item>";
        var result = Read("<text:list text:style-name='N' xml:id='first'>" + wrapper("first") + "</text:list>" +
            "<text:list text:style-name='N' text:continue-list='first'>" + wrapper("second") + "</text:list>");
        Assert.Equal(new string?[] { "a)", "b)" }, Labels(result.Document));
        using var stream = new MemoryStream();
        new OdtDocumentCodec().Write(result.Document, stream);
        stream.Position = 0;
        Assert.Equal(Labels(result.Document), Labels(new OdtDocumentCodec().Read(stream).Document));
    }

    [Theory]
    [InlineData(40, -15)]
    [InlineData(70, 0)]
    [InlineData(0, 0)]
    public void Explicit_Alignment_Overrides_Paragraph_Margins_And_Continuation_Loses_Only_The_Hang(int left, int first)
    {
        string levels = "<text:list-style style:name='N'><text:list-level-style-number text:level='1' style:num-format='1'>" +
            "<style:list-level-properties text:list-level-position-and-space-mode='label-alignment'>" +
            "<style:list-level-label-alignment text:label-followed-by='listtab' fo:margin-left='" + left +
            "pt' fo:text-indent='" + first + "pt'/></style:list-level-properties></text:list-level-style-number></text:list-style>";
        string styles = "<style:style style:name='P' style:family='paragraph'><style:paragraph-properties fo:margin-left='99pt'/></style:style>";
        var document = Read("<text:list text:style-name='N'><text:list-item><text:p text:style-name='P'>one</text:p>" +
            Paragraph("continued") + "</text:list-item></text:list>", levels + styles).Document;
        Assert.All(document.Paragraphs, p => Assert.Equal(left, p.Style.LeftIndent));
        Assert.Equal(first, document.Paragraphs[0].Style.FirstLineIndent);
        Assert.Equal(0, document.Paragraphs[1].Style.FirstLineIndent);
        Assert.Equal(ListKind.None, document.Paragraphs[1].Style.ListKind);
    }
}
