namespace Broiler.Documents.Model.Tests;

/// <summary>
/// <see cref="PageField"/>: the text a page field shows on a page, and text
/// typed beside one staying text.
/// </summary>
public sealed class PageFieldTests
{
    [Theory]
    [InlineData(PageFieldKind.PageNumber, ListNumberFormat.Decimal, 0, 4, 9, "4")]
    [InlineData(PageFieldKind.PageNumber, ListNumberFormat.LowerRoman, 0, 4, 9, "iv")]
    [InlineData(PageFieldKind.PageNumber, ListNumberFormat.UpperLetter, 1, 4, 9, "E")]
    [InlineData(PageFieldKind.PageNumber, ListNumberFormat.None, 0, 4, 9, "")]
    [InlineData(PageFieldKind.PageCount, ListNumberFormat.Decimal, 0, 4, 9, "9")]
    public void A_Field_Shows_Its_Count_In_Its_Format(
        PageFieldKind kind, ListNumberFormat format, int adjust, int page, int count, string expected)
    {
        Assert.Equal(expected, new PageField(kind, format, adjust).Text(page, count));
    }

    [Fact]
    public void A_Field_Shows_Nothing_Before_The_First_Page_And_Has_No_Count_Before_One_Is_Known()
    {
        Assert.Equal("", new PageField(PageFieldKind.PageNumber, Adjust: -1).Text(1, 5));
        Assert.Null(new PageField(PageFieldKind.PageCount).Text(1, null));
    }

    [Fact]
    public void Text_Typed_After_A_Field_Is_Not_Part_Of_It()
    {
        var field = InlineStyle.Default with { Bold = true, PageField = new PageField(PageFieldKind.PageNumber) };
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Create("3", field)]);

        Assert.Null(document.InlineStyleAt(document.End).PageField);
        Assert.True(document.InlineStyleAt(document.End).Bold);

        RichTextParagraph typed = document.InsertText(document.End, " of").Document.Paragraphs[0];
        Assert.Equal("3 of", typed.Text);
        Assert.NotNull(typed.StyleAt(0).PageField);
        Assert.Null(typed.StyleAt(1).PageField);
    }
}
