using System.Text;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers block-level DOCX content: everything a <c>w:body</c> may hold besides
/// a bare <c>w:p</c>. The reader used to walk only the direct <c>w:p</c>
/// children of the body, so a document whose content lived inside a layout table
/// — the shape every CV and letterhead template uses — opened completely empty.
/// </summary>
public sealed class DocxReaderBlockContentTests
{
    [Fact(Timeout = 600000)]
    public void Reads_Paragraphs_Held_By_A_Table()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Table(
                [DocxTestPackage.Paragraph("left"), DocxTestPackage.Paragraph("right")]));

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.Equal("left", result.Document.Paragraphs[0].Text);
        Assert.Equal("right", result.Document.Paragraphs[1].Text);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Table_Cells_In_Row_Major_Order()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Table(
                [DocxTestPackage.Paragraph("r1c1"), DocxTestPackage.Paragraph("r1c2")],
                [DocxTestPackage.Paragraph("r2c1"), DocxTestPackage.Paragraph("r2c2")]));

        Assert.Equal(
            new[] { "r1c1", "r1c2", "r2c1", "r2c2" },
            result.Document.Paragraphs.Select(paragraph => paragraph.Text).ToArray());
    }

    /// <summary>
    /// The reported document: one layout table holding the whole CV, followed by
    /// the single empty paragraph Word requires before <c>w:sectPr</c>. Before the
    /// fix this read as one empty paragraph and no text at all.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Reads_A_Cv_Shaped_Document_Whose_Body_Is_One_Layout_Table()
    {
        string body =
            DocxTestPackage.Table(
                [
                    DocxTestPackage.Paragraph("Elene Bartky") + DocxTestPackage.Paragraph("Hebamme"),
                    string.Empty,
                    DocxTestPackage.Paragraph("Berufserfahrung") + DocxTestPackage.Paragraph("Seit 11/2020"),
                ]) +
            "<w:p/>" +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr>";

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        Assert.Contains("Elene Bartky", result.Document.PlainText, StringComparison.Ordinal);
        Assert.Contains("Berufserfahrung", result.Document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "docx.document.empty");
    }

    [Fact(Timeout = 600000)]
    public void Reads_Tables_Nested_Inside_A_Cell()
    {
        string inner = DocxTestPackage.Table([[DocxTestPackage.Paragraph("inner")]]);
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Table([[DocxTestPackage.Paragraph("outer") + inner]]));

        Assert.Equal(
            new[] { "outer", "inner" },
            result.Document.Paragraphs.Select(paragraph => paragraph.Text).ToArray());
    }

    [Fact(Timeout = 600000)]
    public void Reads_Paragraph_Styles_Inside_Table_Cells()
    {
        string cell =
            "<w:p><w:pPr><w:jc w:val=\"center\"/><w:numPr>" +
            "<w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/>" +
            "</w:numPr></w:pPr><w:r><w:t>bullet</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadBody(DocxTestPackage.Table([[cell]]));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("bullet", paragraph.Text);
        Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment);
        Assert.Equal(ListKind.Bullet, paragraph.Style.ListKind);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Run_Styles_Inside_Table_Cells()
    {
        string cell =
            "<w:p><w:r><w:rPr><w:b/><w:sz w:val=\"36\"/></w:rPr><w:t>Name</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadBody(DocxTestPackage.Table([[cell]]));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        InlineStyle style = paragraph.StyleAt(0);
        Assert.True(style.Bold);
        Assert.Equal(18f, style.FontSize);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Block_Level_Structured_Document_Tags()
    {
        string body =
            "<w:sdt><w:sdtPr><w:id w:val=\"1\"/></w:sdtPr><w:sdtContent>" +
            DocxTestPackage.Paragraph("placeholder") +
            "</w:sdtContent></w:sdt>";

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        Assert.Equal("placeholder", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Table_Rows_Wrapped_In_A_Structured_Document_Tag()
    {
        string row =
            "<w:sdt><w:sdtPr/><w:sdtContent><w:tr><w:tc>" +
            DocxTestPackage.Paragraph("row in a content control") +
            "</w:tc></w:tr></w:sdtContent></w:sdt>";

        DocumentReadResult result = DocxTestPackage.ReadBody("<w:tbl><w:tblPr/>" + row + "</w:tbl>");

        Assert.Equal("row in a content control", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Inserted_Revisions_And_Skips_Deleted_Ones()
    {
        string body =
            "<w:ins w:id=\"1\" w:author=\"a\">" + DocxTestPackage.Paragraph("kept") + "</w:ins>" +
            "<w:del w:id=\"2\" w:author=\"a\">" + DocxTestPackage.Paragraph("gone") + "</w:del>";

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        Assert.Equal("kept", Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.revision.delete");
    }

    [Fact(Timeout = 600000)]
    public void Reads_Only_One_Branch_Of_Alternate_Content()
    {
        string body =
            "<mc:AlternateContent>" +
            "<mc:Choice Requires=\"wps\">" + DocxTestPackage.Paragraph("choice") + "</mc:Choice>" +
            "<mc:Fallback>" + DocxTestPackage.Paragraph("fallback") + "</mc:Fallback>" +
            "</mc:AlternateContent>";

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        Assert.Equal("choice", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact(Timeout = 600000)]
    public void Falls_Back_When_Alternate_Content_Has_No_Choice()
    {
        string body =
            "<mc:AlternateContent><mc:Fallback>" +
            DocxTestPackage.Paragraph("fallback") +
            "</mc:Fallback></mc:AlternateContent>";

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        Assert.Equal("fallback", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact(Timeout = 600000)]
    public void Skips_Structural_Blocks_Without_A_Diagnostic()
    {
        string body =
            "<w:bookmarkStart w:id=\"0\" w:name=\"_top\"/>" +
            DocxTestPackage.Paragraph("text") +
            "<w:bookmarkEnd w:id=\"0\"/>" +
            "<w:sectPr/>";

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        Assert.Equal("text", Assert.Single(result.Document.Paragraphs).Text);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "docx.block.unsupported");
    }

    [Fact(Timeout = 600000)]
    public void Reports_An_Unsupported_Block_Once_Per_Element_Name()
    {
        string body =
            "<w:someFutureBlock/><w:someFutureBlock/><w:anotherFutureBlock/>" +
            DocxTestPackage.Paragraph("text");

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        DocumentDiagnostic[] unsupported = result.Diagnostics
            .Where(diagnostic => diagnostic.Code == "docx.block.unsupported")
            .ToArray();

        Assert.Equal(2, unsupported.Length);
        Assert.Contains(unsupported, diagnostic => diagnostic.Message.Contains("someFutureBlock", StringComparison.Ordinal));
        Assert.Contains(unsupported, diagnostic => diagnostic.Message.Contains("anotherFutureBlock", StringComparison.Ordinal));
        Assert.Equal("text", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact(Timeout = 600000)]
    public void Warns_When_Block_Content_Produces_No_Paragraphs()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody("<w:tbl><w:tblPr/><w:tblGrid/></w:tbl>");

        Assert.Equal(string.Empty, result.Document.PlainText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.document.empty");
    }

    [Fact(Timeout = 600000)]
    public void Does_Not_Warn_When_The_Body_Is_Genuinely_Empty()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody("<w:sectPr/>");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "docx.document.empty");
    }

    [Fact(Timeout = 600000)]
    public void Reports_A_Read_Summary_With_Paragraph_And_Table_Counts()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Table([[DocxTestPackage.Paragraph("a"), DocxTestPackage.Paragraph("b")]]));

        DocumentDiagnostic summary = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "docx.read.summary");

        Assert.Equal(DocumentDiagnosticSeverity.Info, summary.Severity);
        Assert.Contains("2 paragraph(s)", summary.Message, StringComparison.Ordinal);
        Assert.Contains("1 table(s)", summary.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Reports_Flattened_Tables_Once()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Table([[DocxTestPackage.Paragraph("a")]]) +
            DocxTestPackage.Table([[DocxTestPackage.Paragraph("b")]]));

        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "docx.table.flattened");
    }

    [Fact(Timeout = 600000)]
    public void Stops_At_MaxGroupDepth_Instead_Of_Recursing_Without_Bound()
    {
        var body = new StringBuilder(DocxTestPackage.Paragraph("deepest"));
        for (int i = 0; i < 32; i++)
            body = new StringBuilder(DocxTestPackage.Table([[body.ToString()]]));

        DocumentReadResult result = DocxTestPackage.ReadBody(
            body.ToString(),
            new DocumentReadOptions(new DocumentLimits(maxGroupDepth: 4)));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.limit.depth");
        Assert.Equal(string.Empty, result.Document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Deeply_Nested_Tables_Within_MaxGroupDepth()
    {
        var body = new StringBuilder(DocxTestPackage.Paragraph("deepest"));
        for (int i = 0; i < 8; i++)
            body = new StringBuilder(DocxTestPackage.Table([[body.ToString()]]));

        DocumentReadResult result = DocxTestPackage.ReadBody(body.ToString());

        Assert.Equal("deepest", Assert.Single(result.Document.Paragraphs).Text);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "docx.limit.depth");
    }

    [Fact(Timeout = 600000)]
    public void Notes_Headers_And_Footers_That_Were_Not_Imported()
    {
        var extraParts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId2\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
                "Target=\"header1.xml\"/>" +
                "</Relationships>",
            ["word/header1.xml"] =
                "<w:hdr " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
                DocxTestPackage.Paragraph("page header") +
                "</w:hdr>",
        };

        byte[] bytes = DocxTestPackage.FromBody(DocxTestPackage.Paragraph("body"), extraParts);
        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);

        Assert.Equal("body", Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.part.headerfooter");
    }

    /// <summary>A DOCX Broiler wrote must still round-trip through the block walker.</summary>
    [Fact(Timeout = 600000)]
    public void Round_Trips_A_Document_Written_By_The_Docx_Writer()
    {
        RichTextDocument original = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("first"),
            RichTextParagraph.Plain("second"),
        ]);

        byte[] bytes = DocxDocumentCodec.WriteToArray(original);
        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);

        Assert.Equal(
            new[] { "first", "second" },
            result.Document.Paragraphs.Select(paragraph => paragraph.Text).ToArray());
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "docx.block.unsupported");
    }
}
