using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfListTableTests
{
    private static DocumentReadResult Read(string text, DocumentLimits? limits = null) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(text), new DocumentReadOptions(limits));

    [Theory]
    [InlineData(@"{\listtable{\list{\listlevel\levelnfc0{\leveltext hidden;}}\listid1}}")]
    [InlineData(@"{\*\listtable{\list\listid1}}")]
    [InlineData(@"{\listoverridetable{\listoverride\listid1\ls1}}")]
    [InlineData(@"{\*\listoverridetable{\listoverride\listid1\ls1}}")]
    public void Detects_Tables_Without_Importing_Their_Text_Or_List_Semantics(string table)
    {
        DocumentReadResult result = Read(@"{\rtf1 Before" + table + @"\ls1\ilvl0 After}");

        Assert.Equal("BeforeAfter", result.Document.PlainText);
        Assert.Equal(ParagraphStyle.Default.ListKind, result.Document.Paragraphs[0].Style.ListKind);
        DocumentDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("rtf.list", diagnostic.Code);
        Assert.Equal(DocumentDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void Repeated_And_Nested_Tables_Report_Once_Per_Read()
    {
        string text = @"{\rtf1{\*\listtable{\listtable hidden}}{\listoverridetable hidden}{\listtable hidden}Body}";
        for (int i = 0; i < 2; i++)
        {
            DocumentReadResult result = Read(text);
            Assert.Equal("Body", result.Document.PlainText);
            Assert.Equal("rtf.list", Assert.Single(result.Diagnostics).Code);
        }
    }

    [Theory]
    [InlineData(@"{\field{\*\fldinst HYPERLINK ""https://example.com""}{\fldrslt hidden}}")]
    [InlineData(@"{\header hidden}{\footer hidden}")]
    [InlineData(@"{\fonttbl{\f0 hidden;}}\f0 hidden")]
    [InlineData(@"\b\qc\paperw100\u65?\'42\~\{\}\\ hidden")]
    public void Nested_Content_Cannot_Escape_Or_Change_Body_State(string payload)
    {
        DocumentReadResult result = Read(@"{\rtf1 Before{\*\listtable " + payload + "}After}");
        RichTextDocument expected = Read(@"{\rtf1 BeforeAfter}").Document;

        DocumentAssert.Equivalent(expected, result.Document);
        Assert.Equal(expected.RunningContent, result.Document.RunningContent);
        Assert.Equal(expected.PageGeometry, result.Document.PageGeometry);
        Assert.Equal("rtf.list", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Truncated_Table_Does_Not_Leak_Its_Payload()
    {
        DocumentReadResult result = Read(@"{\rtf1 Before{\listtable{\list hidden");
        Assert.Equal("Before", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.list");
    }

    [Fact]
    public void List_Tables_Still_Respect_Tokenizer_Limits()
    {
        string text = @"{\rtf1 Before{\listtable" + new string('{', 100) + "hidden";
        DocumentReadResult depth = Read(text, new DocumentLimits(maxGroupDepth: 4));
        Assert.Equal("Before", depth.Document.PlainText);
        Assert.Contains(depth.Diagnostics, d => d.Code == "rtf.depth");
        Assert.Contains(depth.Diagnostics, d => d.Code == "rtf.list");

        DocumentReadResult size = Read(@"{\rtf1 Before{\listtable " + new string('x', 100) + "}}",
            new DocumentLimits(maxDocumentBytes: 40));
        Assert.Equal("Before", size.Document.PlainText);
        Assert.Contains(size.Diagnostics, d => d.Code == "rtf.size");
        Assert.Contains(size.Diagnostics, d => d.Code == "rtf.list");
    }

    [Fact]
    public void Ordinary_Documents_Do_Not_Report_List_Tables()
    {
        Assert.Empty(Read(@"{\rtf1 Body}").Diagnostics);
    }
}
