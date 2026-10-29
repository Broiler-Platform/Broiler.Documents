using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// What a document of Word 2010 or earlier changes in its layout, and the
/// page count a section restarts at.
/// </summary>
/// <remarks>
/// <c>tdf77417.docx</c> (<c>compatibilityMode</c> 14) holds tables of amounts
/// whose cells end in a tab and no-break spaces and whose tab stops lie past
/// the cells' right indents, in a section that starts on an even page and
/// numbers it 2. It ran to three pages, the first blank, where LibreOffice
/// draws one.
/// </remarks>
public sealed class DocxWord2010CompatibilityTests
{
    private static readonly char NoBreakSpace = (char)0x00A0;

    /// <summary>A cell paragraph: an amount after a tab, then a tab and two no-break spaces.</summary>
    private const string AmountCell =
        "<w:p><w:r><w:t>$</w:t></w:r><w:r><w:tab/><w:t>00,000</w:t></w:r>" +
        "<w:r><w:tab/><w:t xml:space=\"preserve\">&#160;&#160; </w:t></w:r></w:p>";

    private static DocumentReadResult Read(string body, string? mode)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mode is not null)
        {
            parts["word/settings.xml"] =
                "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" +
                "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"" + mode + "\"/>" +
                "</w:compat></w:settings>";
        }

        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    [Theory]
    [InlineData("11")]
    [InlineData("14")]
    public void Word_2010_Drops_White_Space_Ending_A_Cell_Paragraph(string mode)
    {
        DocumentReadResult result = Read(DocxTestPackage.Table([AmountCell]), mode);

        Assert.Equal("$\t00,000", result.Document.Paragraphs[0].Text);
        DocumentDiagnostic note = Assert.Single(result.Diagnostics, d => d.Code == "docx.table.trailingspace");
        Assert.Equal(DocumentDiagnosticSeverity.Info, note.Severity);
    }

    [Theory]
    [InlineData("15")]
    [InlineData(null)]
    public void Word_2013_And_An_Unstated_Mode_Keep_It(string? mode)
    {
        DocumentReadResult result = Read(DocxTestPackage.Table([AmountCell]), mode);

        Assert.Equal("$\t00,000\t" + NoBreakSpace + NoBreakSpace + " ", result.Document.Paragraphs[0].Text);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.table.trailingspace");
    }

    [Fact]
    public void Only_Paragraphs_In_Cells_Lose_It()
    {
        DocumentReadResult result = Read(DocxTestPackage.Paragraph("body  ") + DocxTestPackage.Table([AmountCell]), "14");

        Assert.Equal("body  ", result.Document.Paragraphs[0].Text);
        Assert.Equal("$\t00,000", result.Document.Paragraphs[1].Text);
    }

    [Fact]
    public void A_Cell_Paragraph_Of_White_Space_Alone_Becomes_Empty()
    {
        string blank = "<w:p><w:r><w:t xml:space=\"preserve\">&#160;</w:t></w:r><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p>";

        DocumentReadResult result = Read(DocxTestPackage.Table([blank, "<w:p><w:r><w:t xml:space=\"preserve\"> x </w:t></w:r></w:p>"]), "14");

        Assert.Equal(string.Empty, result.Document.Paragraphs[0].Text);
        Assert.Equal(" x", result.Document.Paragraphs[1].Text);
    }

    [Theory]
    [InlineData("12", true)]
    [InlineData("14", true)]
    [InlineData(null, true)]
    [InlineData("15", false)]
    public void Tabs_Go_Over_The_Margin_Before_Word_2013(string? mode, bool overMargin)
    {
        Assert.Equal(overMargin, Read(DocxTestPackage.Paragraph("x"), mode).Document.StyleDefaults.TabsOverMargin);
    }

    [Fact]
    public void A_Section_Restarting_The_Page_Count_Is_Read_And_Written()
    {
        string body = DocxTestPackage.Paragraph("x") +
            "<w:sectPr><w:type w:val=\"evenPage\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
            "<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>" +
            "<w:pgNumType w:start=\"2\"/></w:sectPr>";

        RichTextDocument document = Read(body, "14").Document;
        DocumentSection section = Assert.Single(document.Sections);
        Assert.Equal(2, section.PageNumberStart);
        Assert.Equal(SectionBreakType.EvenPage, section.BreakType);

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            using var part = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
            Assert.Contains("<w:pgNumType w:start=\"2\"", part.ReadToEnd());
        }

        using var stream = new MemoryStream(bytes, writable: false);
        Assert.Equal(2, Assert.Single(new DocxDocumentCodec().Read(stream).Document.Sections).PageNumberStart);
    }
}
