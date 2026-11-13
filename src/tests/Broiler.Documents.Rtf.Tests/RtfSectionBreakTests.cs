using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfSectionBreakTests
{
    private static DocumentReadResult Read(string body) => RtfReader.Read(Encoding.ASCII.GetBytes("{\\rtf1\\ansi " + body + "}"));

    [Fact]
    public void Section_Headers_Are_Replaced_And_Footers_Are_Inherited()
    {
        var original = Read(@"{\header First head}{\footer Shared foot}First\par" +
            @"\sect\sectd\sbkpage{\header Second head}Second\par" +
            @"\sect\sectd\sbkpage Third").Document;
        var result = RtfReader.Read(RtfWriter.WriteToArray(original)).Document;

        Assert.Equal(3, result.Sections.Count);
        Assert.Equal("First head", Assert.Single(result.Sections[0].RunningContent!.Header(PageSelection.Default)).Text);
        Assert.All(result.Sections.Skip(1), section =>
            Assert.Equal("Second head", Assert.Single(section.RunningContent!.Header(PageSelection.Default)).Text));
        Assert.All(result.Sections, section =>
            Assert.Equal("Shared foot", Assert.Single(section.RunningContent!.Footer(PageSelection.Default)).Text));
    }

    [Fact]
    public void Section_Paper_And_Margins_Roundtrip()
    {
        var original = Read(@"\paperw12240\paperh15840\sectd\pgwsxn12240\marglsxn1440 First\par" +
            @"\sect\sectd\sbkpage\pgwsxn14000\pghsxn10000\marglsxn1800 Second").Document;
        var result = RtfReader.Read(RtfWriter.WriteToArray(original)).Document;
        Assert.Equal(612, result.PageGeometry?.Width);
        Assert.Equal(612, result.Sections[0].Page?.Width);
        Assert.Equal(700, result.Sections[1].Page?.Width);
        Assert.Equal(500, result.Sections[1].Page?.Height);
        Assert.Equal(90, result.Sections[1].Page?.MarginLeft);
    }

    [Fact]
    public void Section_Columns_And_Break_Kinds_Roundtrip()
    {
        var document = Read(@"\sectd\cols2\colsx720 First\par\sect\sectd\sbknone\cols1 Second\par\sect\sectd\sbkpage\cols3\colsx360 Third").Document;
        Assert.Equal(new[] { 2, 1, 3 }, document.Sections.Select(section => section.ColumnCount));
        var result = RtfReader.Read(RtfWriter.WriteToArray(document)).Document;
        Assert.Equal(document.PlainText, result.PlainText);
        Assert.Equal(new[] { 2, 1, 3 }, result.Sections.Select(section => section.ColumnCount));
        Assert.Equal(36, result.Sections[0].ColumnGap);
        Assert.Equal(18, result.Sections[2].ColumnGap);
        Assert.Equal(SectionBreakType.Continuous, result.Sections[1].BreakType);
        Assert.Equal(SectionBreakType.NextPage, result.Sections[2].BreakType);
    }

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
    [InlineData("sbkodd", SectionBreakType.OddPage)]
    [InlineData("sbkeven", SectionBreakType.EvenPage)]
    public void Parity_Sections_Roundtrip(string kind, SectionBreakType expected)
    {
        var result = Read("first\\sect\\sectd\\" + kind + " second");
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Equal(expected, result.Document.Sections[1].BreakType);
        var roundtrip = RtfReader.Read(RtfWriter.WriteToArray(result.Document));
        Assert.Equal(expected, roundtrip.Document.Sections[1].BreakType);
    }
}
