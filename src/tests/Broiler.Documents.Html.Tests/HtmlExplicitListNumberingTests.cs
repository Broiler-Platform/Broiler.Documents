using System.Text;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlExplicitListNumberingTests
{
    private static IReadOnlyList<string?> Labels(string html)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var document = new HtmlDocumentCodec().Read(stream).Document;
        var numberer = new ListNumberer();
        return document.Paragraphs.Select(p => numberer.Advance(p.Style)).ToArray();
    }

    [Theory]
    [InlineData(7, "7.", "8.")]
    [InlineData(0, "0.", "1.")]
    [InlineData(-2, "-2.", "-1.")]
    public void Ordered_Lists_Retain_Explicit_Starts(int start, string first, string second)
    {
        Assert.Equal(new string?[] { first, second }, Labels("<ol start='" + start + "'><li>one</li><li>two</li></ol>"));
    }

    [Fact]
    public void Item_Value_Restarts_The_Counter_And_Following_Items_Continue()
    {
        Assert.Equal(new string?[] { "1.", "10.", "11." }, Labels("<ol><li>one</li><li value='10'>ten</li><li>eleven</li></ol>"));
    }

    [Fact]
    public void Explicit_Lists_Keep_Their_Own_Counters_And_Nested_Bullets_Do_Not_Inherit_The_Number_Definition()
    {
        Assert.Equal(new string?[] { "4.", "◦", "5.", "1." }, Labels(
            "<ol start='4'><li>four<ul><li>bullet</li></ul></li><li>five</li></ol><ol start='1'><li>one</li></ol>"));
    }

    [Theory]
    [InlineData("a", "c.", "d.")]
    [InlineData("I", "III.", "IV.")]
    public void Explicit_Starts_Keep_The_Ordered_List_Number_Format(string type, string first, string second)
    {
        Assert.Equal(new string?[] { first, second }, Labels("<ol type='" + type + "' start='3'><li>one</li><li>two</li></ol>"));
    }
}
