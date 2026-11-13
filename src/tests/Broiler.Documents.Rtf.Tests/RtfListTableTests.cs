using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfListTableTests
{
    private static DocumentReadResult Read(string text, DocumentLimits? limits = null) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(text), new DocumentReadOptions(limits));

    [Theory]
    [InlineData(@"\'a7", "§")]
    [InlineData("*", "*")]
    public void Reads_Literal_And_Ansi_Bullet_Markers(string marker, string expected)
    {
        var result = Read(@"{\rtf1\ansi\ansicpg1252{\*\listtable{\list" +
            @"{\listlevel\levelnfc23{\leveltext \'01" + marker + @";}}\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}}\pard\ls1 Item}");
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Item", result.Document.PlainText);
        Assert.Equal(expected, new ListNumberer().Advance(result.Document.Paragraphs[0].Style));
    }

    [Fact]
    public void Invalid_Numbered_Template_Reports_Loss_And_Keeps_The_Item()
    {
        var result = Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0" +
            @"{\leveltext \'02\'09.;}}\listid1}}{\*\listoverridetable" +
            @"{\listoverride\listid1\listoverridecount0\ls1}}\pard\ls1 Item}");
        Assert.Equal("Item", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.list");
        Assert.Equal("1.", new ListNumberer().Advance(result.Document.Paragraphs[0].Style));
    }

    [Fact]
    public void Cached_Roman_And_Decimal_Labels_Preserve_Producer_Numbering()
    {
        var input = Read(@"{\rtf1{\*\listtable" +
            @"{\list{\listlevel\levelnfc0\levelstartat1}\listid1}" +
            @"{\list{\listlevel\levelnfc2\levelstartat4{\leveltext \'02\'00);}}\listid2}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}" +
            @"{\listoverride\listid2\listoverridecount0\ls2}}" +
            @"\pard{\listtext 1.\tab}\ls1 First\par\pard{\listtext ii)\tab}\ls2 Override\par" +
            @"\pard{\listtext iii)\tab}\ls2 Next\par\pard{\listtext 4.\tab}\ls1 Continue\par}");
        var output = RtfReader.Read(RtfWriter.WriteToArray(input.Document));
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "1.", "ii)", "iii)", "4." },
            System.Linq.Enumerable.Select(output.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }

    [Fact]
    public void A_Format_Override_Changes_Only_Its_Own_List_Instance()
    {
        var result = Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0\levelstartat1}\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}" +
            @"{\listoverride\listid1\listoverridecount1{\lfolevel\listoverrideformat1\listoverridestartat" +
            @"{\listlevel\levelnfc2\levelstartat4{\leveltext \'02\'00);}{\levelnumbers\'01;}}}\ls2}}" +
            @"\pard\ls1 First\par\pard\ls2 Override\par\pard\ls2 Next\par\pard\ls1 Continue}");
        Assert.Empty(result.Diagnostics);
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "1.", "iv)", "v)", "2." },
            System.Linq.Enumerable.Select(result.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }

    [Fact]
    public void A_Note_List_Keeps_Its_Start_And_Continuation()
    {
        var definition = new ListDefinition([ListLevel.DecimalAt(0) with { Start = 7 }]);
        var style = ParagraphStyle.Default with { List = new ListReference(definition, 0) };
        var note = new DocumentNote(NoteKind.Footnote,
            [RichTextParagraph.Plain("First").WithParagraphStyle(style),
             RichTextParagraph.Plain("Second").WithParagraphStyle(style)]);
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("Body")
            .InsertText(4, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note })]);
        var result = RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

        var numberer = new ListNumberer();
        var flow = Assert.Single(result.Notes).Paragraphs;
        Assert.Equal("7.", numberer.Advance(flow[0].Style));
        Assert.Equal("8.", numberer.Advance(flow[1].Style));
    }

    [Fact]
    public void List_Counters_Roundtrip_Independently_In_Running_Content_And_Text_Boxes()
    {
        var definition = new ListDefinition([ListLevel.DecimalAt(0) with { Start = 4 }]);
        var style = ParagraphStyle.Default with { List = new ListReference(definition, 0) };
        var first = RichTextParagraph.Plain("First").WithParagraphStyle(style);
        var second = RichTextParagraph.Plain("Second").WithParagraphStyle(style);
        var document = RichTextDocument.FromParagraphs([first, second])
            .WithRunningContent(RunningContent.Empty
                .WithHeader(PageSelection.Default, [first, second])
                .WithFooter(PageSelection.Default, [first, second]))
            .WithShapes([new DocumentShape(0, 10, 10, 120, 60, paragraphs: [first, second])]);

        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, new RtfDocumentCodec().Write(document, stream).Status);
        var result = RtfReader.Read(stream.ToArray()).Document;
        var numberer = new ListNumberer();
        foreach (var flow in new[] { result.Paragraphs, result.RunningContent.Header(PageSelection.Default),
            result.RunningContent.Footer(PageSelection.Default), Assert.Single(result.Shapes).Paragraphs })
        {
            Assert.Equal(2, flow.Count);
            Assert.Equal("4.", numberer.Advance(flow[0].Style));
            Assert.Equal("5.", numberer.Advance(flow[1].Style));
        }
    }

    [Fact]
    public void Numbering_Continues_Through_Table_Cell_Paragraphs()
    {
        var definition = new ListDefinition([ListLevel.DecimalAt(0) with { Start = 4 }]);
        var style = ParagraphStyle.Default with { List = new ListReference(definition, 0) };
        var table = new DocumentTable(1, 2, [new TableRow([new TableCell(1, 2, 0)])]);
        var document = RichTextDocument.FromParagraphs(
            System.Linq.Enumerable.Select(new[] { "Before", "Cell first", "Cell last", "After" },
                text => RichTextParagraph.Plain(text).WithParagraphStyle(style))).WithTables([table]);

        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, new RtfDocumentCodec().Write(document, stream).Status);
        var result = RtfReader.Read(stream.ToArray());
        Assert.Equal(document.PlainText, result.Document.PlainText);
        Assert.Single(result.Document.Tables);
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "4.", "5.", "6.", "7." },
            System.Linq.Enumerable.Select(result.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }

    [Fact]
    public void Roundtrip_Preserves_Numbered_Prefixes_And_Multiple_Level_Placeholders()
    {
        var definition = new ListDefinition(
            [ListLevel.DecimalAt(0) with { Start = 4, Template = "Part %1)" },
                ListLevel.DecimalAt(1) with { Template = "(%1.%2)" }]);
        var document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("First").WithParagraphStyle(
                ParagraphStyle.Default with { List = new ListReference(definition, 0) }),
            RichTextParagraph.Plain("Nested").WithParagraphStyle(
                ParagraphStyle.Default with { List = new ListReference(definition, 1) }),
        ]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, new RtfDocumentCodec().Write(document, stream).Status);
        var result = RtfReader.Read(stream.ToArray());
        Assert.Empty(result.Diagnostics);
        var numberer = new ListNumberer();
        Assert.Equal("Part 4)", numberer.Advance(result.Document.Paragraphs[0].Style));
        Assert.Equal("(4.1)", numberer.Advance(result.Document.Paragraphs[1].Style));
    }

    [Fact]
    public void Reads_A_Start_Override_As_An_Independent_Counter()
    {
        var result = Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0\levelstartat1}\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}" +
            @"{\listoverride\listid1\listoverridecount1{\lfolevel\listoverridestartat\levelstartat7}\ls2}}" +
            @"\pard\ls1 First\par\pard\ls2 Restart\par\pard\ls2 Next\par\pard\ls1 Continue}");
        Assert.Empty(result.Diagnostics);
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "1.", "7.", "8.", "2." },
            System.Linq.Enumerable.Select(result.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }

    [Fact]
    public void Reads_Modern_List_Levels_Starts_And_Independent_Instances()
    {
        DocumentReadResult result = Read(
            @"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0\levelstartat4{\leveltext \'02\'00.;}}" +
            @"{\listlevel\levelnfc23\levelstartat1{\leveltext \'01\u8226 ?;}}\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}" +
            @"{\listoverride\listid1\listoverridecount0\ls2}}" +
            @"\pard{\listtext 4.\tab}\ilvl0\ls1 Outer\par" +
            @"\pard{\listtext \u8226?\tab}\ilvl1\ls1 Nested\par" +
            @"\pard{\listtext 9.\tab}\ilvl0\ls1 Restart\par" +
            @"\pard{\listtext 10.\tab}\ilvl0\ls1 Continue\par" +
            @"\pard{\listtext 4.\tab}\ls2 Independent\par\pard Plain}");

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Outer\nNested\nRestart\nContinue\nIndependent\nPlain", result.Document.PlainText);
        var paragraphs = result.Document.Paragraphs;
        Assert.Equal(1, paragraphs[1].Style.List?.Level);
        Assert.Equal(ListKind.Bullet, paragraphs[1].Style.ListKind);
        Assert.Null(paragraphs[5].Style.List);
        var numberer = new ListNumberer();
        Assert.Equal("4.", numberer.Advance(paragraphs[0].Style));
        numberer.Advance(paragraphs[1].Style);
        Assert.Equal("9.", numberer.Advance(paragraphs[2].Style));
        Assert.Equal(9, paragraphs[2].Style.List?.RestartAt);
        Assert.Equal("10.", numberer.Advance(paragraphs[3].Style));
        Assert.Null(paragraphs[3].Style.List?.RestartAt);
        Assert.Equal("4.", numberer.Advance(paragraphs[4].Style));
    }

    [Fact]
    public void Roundtrip_Preserves_Numbering_After_A_Top_Level_Restart()
    {
        var definition = new ListDefinition([ListLevel.DecimalAt(0) with { Start = 4 },
            ListDefinition.PlainBullets.LevelAt(1) with { Template = "◆" }]);
        RichTextParagraph Item(string text, int level, int? restart = null) =>
            RichTextParagraph.Plain(text).WithParagraphStyle(ParagraphStyle.Default with
            {
                List = new ListReference(definition, level, restart),
            });
        var source = RichTextDocument.FromParagraphs(
            [Item("First", 0), Item("Nested", 1), Item("Restart", 0, 9), Item("Next", 0)]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, new RtfDocumentCodec().Write(source, stream).Status);
        var result = RtfReader.Read(stream.ToArray());
        Assert.Empty(result.Diagnostics);
        Assert.Equal(source.PlainText, result.Document.PlainText);
        var numberer = new ListNumberer();
        Assert.Equal("4.", numberer.Advance(result.Document.Paragraphs[0].Style));
        Assert.Equal(1, result.Document.Paragraphs[1].Style.List?.Level);
        Assert.Equal("◆", numberer.Advance(result.Document.Paragraphs[1].Style));
        Assert.Equal("9.", numberer.Advance(result.Document.Paragraphs[2].Style));
        Assert.Equal("10.", numberer.Advance(result.Document.Paragraphs[3].Style));
    }

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
