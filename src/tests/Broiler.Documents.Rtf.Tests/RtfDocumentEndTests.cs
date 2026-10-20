using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// The document is the <c>{\rtf1 ...}</c> group, and it ends where that group
/// does. Word and LibreOffice both stop reading at its closing brace; this
/// reader used to carry on and read whatever followed into the body.
/// </summary>
public sealed class RtfDocumentEndTests
{
    private static DocumentReadResult Read(string text) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(text));

    [Theory]
    [InlineData(@"{\rtf1 Hello\par} After\par")]
    [InlineData(@"{\rtf1 Hello\par}}} After}")]
    [InlineData(@"{\rtf1 Hello\par}{\rtf1 After\par}")]
    [InlineData(@"{\rtf1 Hello\par}\'41\u66?\~")]
    public void Content_After_The_Root_Group_Is_Not_Read(string rtf)
    {
        DocumentReadResult result = Read(rtf);

        Assert.Equal("Hello", result.Document.PlainText);
        DocumentDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("rtf.trailing", diagnostic.Code);
        Assert.Equal(DocumentDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Theory]
    [InlineData("{\\rtf1 Hello}")]
    [InlineData("{\\rtf1 Hello}\r\n")]
    [InlineData("{\\rtf1 Hello}\0")]
    [InlineData("{\\rtf1 Hello} \t")]
    [InlineData("{\\rtf1 Hello}}")]
    public void A_Blank_Tail_Or_A_Spare_Brace_Is_Not_Reported(string rtf)
    {
        DocumentReadResult result = Read(rtf);

        Assert.Equal("Hello", result.Document.PlainText);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Content_After_The_End_Cannot_Change_The_Document()
    {
        const string Document = @"{\rtf1\paperw12240\paperh15840\margl1440\margr1440\margt1440\margb1440\qc Hello}";
        RichTextDocument expected = Read(Document).Document;

        RichTextDocument actual = Read(
            Document + @"\paperw100\ql{\header Head\par}{\shp{\*\shpinst\shpright1440\shpbottom1440{\shptxt Box}}}\b more\par").Document;

        DocumentAssert.Equivalent(expected, actual);
        Assert.Equal(TextAlignment.Center, actual.Paragraphs[0].Style.Alignment);
        Assert.Equal(expected.PageGeometry, actual.PageGeometry);
        Assert.True(actual.RunningContent.IsEmpty);
        Assert.Empty(actual.Shapes);
    }

    [Fact]
    public void A_Closing_Brace_With_No_Group_Open_Does_Not_End_The_Read()
    {
        DocumentReadResult result = Read(@"}{\rtf1 Hello}");

        Assert.Equal("Hello", result.Document.PlainText);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void A_Root_Group_That_Never_Closes_Is_Read_To_The_End()
    {
        Assert.Equal("Hello\nAfter", Read(@"{\rtf1 Hello\par After").Document.PlainText);
    }
}
