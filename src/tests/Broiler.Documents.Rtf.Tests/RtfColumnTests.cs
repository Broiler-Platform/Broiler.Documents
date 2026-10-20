using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// The section's text columns: <c>\cols</c>, a count, and <c>\colsx</c>, the gap
/// between them, which the specification puts at 720 twips when it is absent.
/// </summary>
public sealed class RtfColumnTests
{
    private const string Letter = "\\paperw12240\\paperh15840\\margl1440\\margr1440\\margt1440\\margb1440";

    private static PageGeometry PageOf(string controlWords) =>
        Assert.IsType<PageGeometry>(
            RtfReader.Read(Encoding.ASCII.GetBytes("{\\rtf1" + Letter + controlWords + " body\\par}")).Document.PageGeometry);

    [Fact]
    public void Reads_The_Column_Count_And_Gap()
    {
        PageGeometry page = PageOf("\\cols3\\colsx360");

        Assert.Equal(3, page.ColumnCount);
        Assert.Equal(18, page.ColumnGap, 3);
    }

    [Fact]
    public void An_Absent_Gap_Is_Half_An_Inch()
    {
        Assert.Equal(36, PageOf("\\cols2").ColumnGap, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\\cols1")]
    [InlineData("\\cols0")]
    [InlineData("\\colsx720")]
    public void A_Section_That_Is_Not_Divided_Has_One_Column(string controlWords)
    {
        Assert.Equal(1, PageOf(controlWords).ColumnCount);
    }

    [Fact]
    public void Columns_That_Leave_No_Room_Keep_The_Page_And_Drop_The_Columns()
    {
        DocumentReadResult result = RtfReader.Read(
            Encoding.ASCII.GetBytes("{\\rtf1" + Letter + "\\cols2\\colsx20000 body\\par}"));

        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);
        Assert.Equal(612, page.Width, 3);
        Assert.Equal(1, page.ColumnCount);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.columns.geometry");
    }

    [Fact]
    public void Columns_Survive_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 27));

        byte[] written = RtfWriter.WriteToArray(source);
        PageGeometry page = Assert.IsType<PageGeometry>(RtfReader.Read(written).Document.PageGeometry);

        Assert.Contains("\\cols2\\colsx540", Encoding.ASCII.GetString(written), StringComparison.Ordinal);
        Assert.Equal(2, page.ColumnCount);
        Assert.Equal(27, page.ColumnGap, 3);
    }
}
