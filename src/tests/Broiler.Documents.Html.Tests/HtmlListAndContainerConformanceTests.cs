using System.Text;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlListAndContainerConformanceTests
{
    private static RichTextDocument Read(string html)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return new HtmlDocumentCodec().Read(stream).Document;
    }

    private static IReadOnlyList<string?> Labels(RichTextDocument document)
    {
        var numberer = new ListNumberer();
        return document.Paragraphs.Select(p => numberer.Advance(p.Style)).ToArray();
    }

    [Theory]
    [InlineData("a", "a.", "b.")]
    [InlineData("A", "A.", "B.")]
    [InlineData("i", "i.", "ii.")]
    [InlineData("I", "I.", "II.")]
    public void Ordered_List_Type_Applies_Without_An_Explicit_Start(string type, string first, string second)
    {
        Assert.Equal(new string?[] { first, second }, Labels(Read("<ol type='" + type + "'><li>first</li><li>second</li></ol>")));
    }

    [Fact]
    public void Independent_Adjacent_Lists_Restart_And_Nested_Ordered_Lists_Default_To_Decimal()
    {
        Assert.Equal(new string?[] { "1.", "1.", "2.", "1." }, Labels(Read(
            "<ol><li>parent<ol><li>child</li></ol></li><li>second</li></ol><ol><li>fresh</li></ol>")));
    }

    [Fact]
    public void Only_The_First_Paragraph_Of_An_Item_Advances_Its_Counter()
    {
        var document = Read("<ol><li><p>first</p><p>continuation</p></li><li value='7'><p>seven</p><p>continued seven</p></li><li>eight</li></ol>");
        Assert.Equal(new[] { "first", "continuation", "seven", "continued seven", "eight" }, document.Paragraphs.Select(p => p.Text));
        Assert.Equal(new string?[] { "1.", null, "7.", null, "8." }, Labels(document));
        Assert.Equal(1, document.Paragraphs[1].Style.IndentLevel);
    }

    [Fact]
    public void Parent_Continuation_After_A_Nested_List_Is_Not_Another_Parent_Item()
    {
        var document = Read("<ol><li>parent<ul><li>child</li></ul><p>continued parent</p></li><li>second parent</li></ol>");
        Assert.Equal(new string?[] { "1.", "◦", null, "2." }, Labels(document));
    }

    [Theory]
    [InlineData("div")]
    [InlineData("section")]
    [InlineData("blockquote")]
    public void Direct_Container_Text_And_Text_After_Child_Paragraphs_Keep_Their_Alignment(string tag)
    {
        var document = Read("<" + tag + " style='text-align:center'>before<p>inside</p>after</" + tag + ">");
        Assert.Equal(new[] { "before", "inside", "after" }, document.Paragraphs.Select(p => p.Text));
        Assert.All(document.Paragraphs, p => Assert.Equal(TextAlignment.Center, p.Style.Alignment));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(25.5)]
    [InlineData(0)]
    public void Css_Left_Margins_Are_Not_Rounded_To_Indent_Levels(double margin)
    {
        string value = margin.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var paragraph = Assert.Single(Read("<p style='margin-left:" + value + "pt'>offset</p>").Paragraphs);
        Assert.Equal(margin, paragraph.Style.LeftIndentIn(), 4);
        using var written = new MemoryStream(HtmlDocumentCodec.WriteToArray(RichTextDocument.FromParagraphs([paragraph])));
        var restored = Assert.Single(new HtmlDocumentCodec().Read(written).Document.Paragraphs);
        Assert.Equal(paragraph.Style, restored.Style);
    }

    [Theory]
    [InlineData(12, 25)]
    [InlineData(18, 31)]
    public void Child_Margins_Accumulate_Inside_The_Containing_Block_And_Do_Not_Leak_To_Siblings(int parent, int expected)
    {
        var document = Read("<div style='margin-left:" + parent + "pt'><p style='margin-left:13pt'>offset</p></div><p>plain</p>");
        Assert.Equal(expected, document.Paragraphs[0].Style.LeftIndentIn(), 4);
        Assert.Equal(0, document.Paragraphs[1].Style.LeftIndentIn(), 4);
    }
    [Theory]
    [InlineData(18, 36)]
    [InlineData(25, 43)]
    public void Container_Margins_Do_Not_Change_Logical_List_Depth_Or_Erase_List_Indent(int container, int expected)
    {
        var document = Read("<div style='margin-left:" + container + "pt'><ul><li>item</li></ul></div>");
        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(expected, paragraph.Style.LeftIndentIn(), 4);
        Assert.Equal(0, paragraph.Style.List!.Value.Level);
        Assert.Equal(new string?[] { "•" }, Labels(document));
    }

}
