namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Line numbering: the <c>w:lnNumType</c> a section states (ECMA-376 §17.6.8).
/// </summary>
/// <remarks>
/// LibreOffice's <c>tdf121664.docx</c> numbers the four empty lines of a
/// two-page document "1, 2" on each page; it drew blank here because the
/// element was not read.
/// </remarks>
public sealed class DocxLineNumberingTests
{
    private const string Page =
        "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
        "<w:pgMar w:left=\"1440\" w:right=\"1440\" w:top=\"1440\" w:bottom=\"1440\"/>";

    private static LineNumbering? NumberingOf(string lnNumType)
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Paragraph("body") + "<w:sectPr>" + Page + lnNumType + "</w:sectPr>");
        return Assert.IsType<PageGeometry>(result.Document.PageGeometry).LineNumbering;
    }

    [Fact]
    public void A_Count_Of_One_Numbers_Every_Line_From_One_Restarting_On_Each_Page()
    {
        LineNumbering numbering = Assert.IsType<LineNumbering>(NumberingOf("<w:lnNumType w:countBy=\"1\"/>"));

        Assert.Equal(1, numbering.CountBy);
        Assert.Equal(1, numbering.Start);
        Assert.Equal(LineNumberRestart.NewPage, numbering.Restart);
        Assert.Equal(LineNumbering.DefaultDistance, numbering.Distance, 6);
    }

    [Fact]
    public void Start_Is_Stored_One_Below_The_First_Number_And_Distance_In_Twips()
    {
        LineNumbering numbering = Assert.IsType<LineNumbering>(NumberingOf(
            "<w:lnNumType w:countBy=\"5\" w:start=\"9\" w:distance=\"360\" w:restart=\"continuous\"/>"));

        Assert.Equal(5, numbering.CountBy);
        Assert.Equal(10, numbering.Start);
        Assert.Equal(18, numbering.Distance, 6);
        Assert.Equal(LineNumberRestart.Continuous, numbering.Restart);
    }

    [Fact]
    public void A_Section_Restart_Is_Read()
    {
        Assert.Equal(LineNumberRestart.NewSection,
            NumberingOf("<w:lnNumType w:countBy=\"1\" w:restart=\"newSection\"/>")?.Restart);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<w:lnNumType/>")]
    [InlineData("<w:lnNumType w:countBy=\"0\"/>")]
    [InlineData("<w:lnNumType w:start=\"3\"/>")]
    public void Without_A_Positive_Count_There_Is_No_Numbering(string lnNumType)
    {
        Assert.Null(NumberingOf(lnNumType));
    }
}
