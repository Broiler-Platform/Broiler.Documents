using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfSectionBreakTests
{
    private static DocumentReadResult Read(string body) => RtfReader.Read(Encoding.ASCII.GetBytes("{\\rtf1\\ansi " + body + "}"));

    [Theory]
    [InlineData("first\\sect\\sectd\\sbknone second")]
    [InlineData("first\\par\\sect\\sectd\\sbknone second")]
    public void A_Continuous_Section_Separates_Text_Without_Adding_An_Empty_Paragraph(string body)
    {
        var document = Read(body).Document;
        Assert.Equal(new[] { "first", "second" }, document.Paragraphs.Select(p => p.Text));
        Assert.False(document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Theory]
    [InlineData("first\\sect second")]
    [InlineData("first\\sect\\sectd\\sbkpage second")]
    [InlineData("first\\sect\\sbknone\\sectd second")]
    [InlineData("first\\sect{\\header heading}second")]
    public void A_New_Section_Defaults_To_A_New_Page(string body)
    {
        var document = Read(body).Document;
        Assert.Equal(new[] { "first", "second" }, document.Paragraphs.Select(p => p.Text));
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Theory]
    [InlineData("first\\sect", false)]
    [InlineData("first\\sect\\sectd\\sbkpage", true)]
    [InlineData("first\\sect\\sectd\\sbknone", false)]
    public void An_Empty_Last_Section_Keeps_Its_Paragraph_Without_Inventing_A_Page(string body, bool pageBreak)
    {
        var document = Read(body).Document;
        Assert.Equal(new[] { "first", "" }, document.Paragraphs.Select(p => p.Text));
        Assert.Equal(pageBreak, document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void Repeated_Section_Breaks_Retain_The_Empty_Section()
    {
        var document = Read("first\\sect\\sect second").Document;
        Assert.Equal(new[] { "first", "", "second" }, document.Paragraphs.Select(p => p.Text));
        Assert.All(document.Paragraphs.Skip(1), p => Assert.True(p.Style.PageBreakBefore));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_Column_Section_Break_Uses_The_Available_Columns(int columns)
    {
        var document = Read($"\\cols{columns} first\\sect\\sectd\\sbkcol second").Document;
        Assert.Equal(columns == 1, document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Equal(columns > 1, document.Paragraphs[1].Style.ColumnBreakBefore);
    }

    [Theory]
    [InlineData("sbkodd")]
    [InlineData("sbkeven")]
    public void Parity_Sections_Keep_A_Page_Break_And_Report_The_Unsupported_Parity(string kind)
    {
        var result = Read("first\\sect\\sectd\\" + kind + " second");
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.section.parity");
    }
}
