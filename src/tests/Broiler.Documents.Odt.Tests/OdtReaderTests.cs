using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtReaderTests
{
    [Fact]
    public void Reads_Paragraphs_In_Document_Order()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.Paragraph("First") + OdtTestPackage.Paragraph("Second"));

        Assert.True(result.IsUsable);
        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.Equal("First", result.Document.Paragraphs[0].Text);
        Assert.Equal("Second", result.Document.Paragraphs[1].Text);
    }

    [Fact]
    public void Reads_A_Heading_As_A_Paragraph()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:h text:outline-level=\"1\">Title</text:h>");

        Assert.Equal("Title", result.Document.Paragraphs[0].Text);
    }

    [Theory]
    // A run of white space is one space, and white space at either edge of the
    // paragraph is nothing at all (ODF 1.3 part 3 section 3.17).
    [InlineData("<text:p>  Hello   world  </text:p>", "Hello world")]
    [InlineData("<text:p>\n    Indented markup\n  </text:p>", "Indented markup")]
    [InlineData("<text:p>a<text:span>b</text:span></text:p>", "ab")]
    [InlineData("<text:p><text:span>a</text:span> <text:span>b</text:span></text:p>", "a b")]
    public void Collapses_White_Space_The_Way_Odf_Defines_It(string bodyXml, string expected)
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(bodyXml);

        Assert.Equal(expected, result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Reads_Text_S_As_Literal_Spaces()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:p>a<text:s text:c=\"3\"/>b</text:p>");

        Assert.Equal("a   b", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Reads_A_Bare_Text_S_As_One_Space()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:p>a<text:s/>b</text:p>");

        Assert.Equal("a b", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Reads_Tabs_And_Line_Breaks_As_Single_Characters()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>a<text:tab/>b<text:line-break/>c</text:p>");

        Assert.Equal("a\tb\u2028c", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Reads_A_Hyperlink_Onto_The_Runs_It_Covers()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>see <text:a xlink:href=\"https://example.com/\">the site</text:a></text:p>");

        RichTextParagraph paragraph = result.Document.Paragraphs[0];
        Assert.Equal("see the site", paragraph.Text);
        Assert.Null(paragraph.StyleAt(0).LinkHref);
        Assert.Equal("https://example.com/", paragraph.StyleAt(5).LinkHref);
    }

    [Fact]
    public void Reads_An_Internal_Anchor_Link()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:a xlink:href=\"#chapter\">jump</text:a></text:p>");

        Assert.Equal("#chapter", result.Document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void Drops_A_Hyperlink_With_A_Disallowed_Scheme()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:a xlink:href=\"javascript:alert(1)\">click</text:a></text:p>");

        Assert.Equal("click", result.Document.Paragraphs[0].Text);
        Assert.Null(result.Document.Paragraphs[0].StyleAt(0).LinkHref);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.link");
    }

    [Fact]
    public void Reads_A_Bullet_List_With_Its_Level()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:list text:style-name=\"L1\">" +
            "<text:list-item><text:p>one</text:p></text:list-item>" +
            "<text:list-item><text:p>two</text:p></text:list-item>" +
            "</text:list>",
            OdtTestPackage.BulletListStyle("L1"));

        Assert.Equal(2, result.Document.ParagraphCount);
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            Assert.Equal(ListKind.Bullet, paragraph.Style.ListKind);
            Assert.Equal(1, paragraph.Style.IndentLevel);
        }
    }

    [Fact]
    public void Reads_A_Numbered_List_From_Its_List_Style()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:list text:style-name=\"L2\">" +
            "<text:list-item><text:p>one</text:p></text:list-item>" +
            "</text:list>",
            OdtTestPackage.NumberListStyle("L2"));

        Assert.Equal(ListKind.Numbered, result.Document.Paragraphs[0].Style.ListKind);
    }

    [Fact]
    public void Reads_A_Nested_List_As_A_Deeper_Level_Of_The_Same_Style()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:list text:style-name=\"L1\">" +
            "<text:list-item><text:p>outer</text:p>" +
            "<text:list><text:list-item><text:p>inner</text:p></text:list-item></text:list>" +
            "</text:list-item>" +
            "</text:list>",
            OdtTestPackage.BulletListStyle("L1"));

        Assert.Equal(1, result.Document.Paragraphs[0].Style.IndentLevel);
        Assert.Equal(2, result.Document.Paragraphs[1].Style.IndentLevel);
        Assert.Equal(ListKind.Bullet, result.Document.Paragraphs[1].Style.ListKind);
    }

    [Fact]
    public void Reads_A_List_Header_Without_A_Bullet()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:list text:style-name=\"L1\">" +
            "<text:list-header><text:p>lead in</text:p></text:list-header>" +
            "<text:list-item><text:p>item</text:p></text:list-item>" +
            "</text:list>",
            OdtTestPackage.BulletListStyle("L1"));

        Assert.Equal(ListKind.None, result.Document.Paragraphs[0].Style.ListKind);
        Assert.Equal(ListKind.Bullet, result.Document.Paragraphs[1].Style.ListKind);
    }

    [Fact]
    public void Flattens_A_Table_Into_Its_Cell_Paragraphs_In_Row_Order()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.Table(
                [OdtTestPackage.Paragraph("a"), OdtTestPackage.Paragraph("b")],
                [OdtTestPackage.Paragraph("c"), OdtTestPackage.Paragraph("d")]));

        Assert.Equal(["a", "b", "c", "d"], result.Document.Paragraphs.Select(p => p.Text));
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.table.flattened");
    }

    [Fact]
    public void Reads_The_Rows_A_Header_Row_Group_Wraps()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<table:table table:name=\"T\">" +
            "<table:table-header-rows><table:table-row><table:table-cell>" +
            OdtTestPackage.Paragraph("head") +
            "</table:table-cell></table:table-row></table:table-header-rows>" +
            "<table:table-row><table:table-cell>" +
            OdtTestPackage.Paragraph("body") +
            "</table:table-cell></table:table-row>" +
            "</table:table>");

        Assert.Equal(["head", "body"], result.Document.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void Reads_The_Paragraphs_Inside_A_Section()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:section text:name=\"S\">" + OdtTestPackage.Paragraph("inside") + "</text:section>");

        Assert.Equal("inside", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Reads_The_Body_Of_A_Generated_Index()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:table-of-content text:name=\"TOC\">" +
            "<text:index-body>" + OdtTestPackage.Paragraph("Chapter one") + "</text:index-body>" +
            "</text:table-of-content>");

        Assert.Equal("Chapter one", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Reads_The_Value_A_Field_Displays()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>on <text:date text:date-value=\"2024-01-01\">01/01/2024</text:date></text:p>");

        Assert.Equal("on 01/01/2024", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Skips_Comment_Bodies()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>kept<office:annotation><text:p>a note</text:p></office:annotation></text:p>");

        Assert.Equal("kept", result.Document.Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.annotation");
    }

    [Fact]
    public void Skips_Footnote_Bodies()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>kept<text:note text:note-class=\"footnote\">" +
            "<text:note-body><text:p>the note</text:p></text:note-body>" +
            "</text:note></text:p>");

        Assert.Equal("kept", result.Document.Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.note");
    }

    [Fact]
    public void Reports_An_Unsupported_Block_By_Name()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:page-sequence/>");

        DocumentDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Code == "odt.block.unsupported");
        Assert.Contains("page-sequence", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Read_Ends_With_A_Summary()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(OdtTestPackage.Paragraph("one"));

        DocumentDiagnostic summary = Assert.Single(result.Diagnostics, d => d.Code == "odt.read.summary");
        Assert.Equal(DocumentDiagnosticSeverity.Info, summary.Severity);
        Assert.Contains("1 paragraph(s)", summary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_An_Encrypted_Package()
    {
        byte[] package = OdtTestPackage.FromBody(
            OdtTestPackage.Paragraph("secret"),
            manifestInnerXml:
                "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\">" +
                "<manifest:encryption-data manifest:checksum=\"x\"/>" +
                "</manifest:file-entry>");

        using var stream = new MemoryStream(package, writable: false);
        DocumentReadResult result = new OdtDocumentCodec().Read(stream);

        Assert.False(result.IsUsable);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.package.encrypted");
    }

    [Fact]
    public void Rejects_A_Package_With_No_Content_Part()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            byte[] bytes = Encoding.ASCII.GetBytes(OdtTestPackage.TextMediaType);
            stream.Write(bytes, 0, bytes.Length);
        }

        using var source = new MemoryStream(buffer.ToArray(), writable: false);
        DocumentReadResult result = new OdtDocumentCodec().Read(source);

        Assert.False(result.IsUsable);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.package.content");
    }

    [Fact]
    public void Rejects_Bytes_That_Are_Not_A_Zip_Package()
    {
        using var source = new MemoryStream("not a package"u8.ToArray(), writable: false);
        DocumentReadResult result = new OdtDocumentCodec().Read(source);

        Assert.False(result.IsUsable);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.package.zip");
    }

    [Fact]
    public void Rejects_Input_Over_The_Document_Byte_Limit()
    {
        byte[] package = OdtTestPackage.FromBody(OdtTestPackage.Paragraph("body"));
        var options = new DocumentReadOptions(new DocumentLimits(maxDocumentBytes: 16));

        using var source = new MemoryStream(package, writable: false);
        DocumentReadResult result = new OdtDocumentCodec().Read(source, options);

        Assert.False(result.IsUsable);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.limit.bytes");
    }

    [Fact]
    public void Stops_At_The_Paragraph_Count_Limit()
    {
        string body = string.Concat(Enumerable.Range(0, 5).Select(i => OdtTestPackage.Paragraph("p" + i)));
        var options = new DocumentReadOptions(new DocumentLimits(maxParagraphCount: 2));

        DocumentReadResult result = OdtTestPackage.ReadBody(body, options: options);

        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.limit.paragraphs");
    }

    [Fact]
    public void Truncates_A_Paragraph_Over_The_Run_Length_Limit()
    {
        var options = new DocumentReadOptions(new DocumentLimits(maxRunLength: 4));

        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.Paragraph("abcdefgh"),
            options: options);

        Assert.Equal("abcd", result.Document.Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.limit.run");
    }

    [Fact]
    public void An_Empty_Body_Produces_An_Empty_Document_Without_A_Warning()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:sequence-decls/>");

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.document.empty");
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }
}
