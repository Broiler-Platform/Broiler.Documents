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
            @"{\list{\listlevel\levelnfc0\levelstartat1{\leveltext \'02\'00.;}{\levelnumbers\'01;}}\listid1}" +
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
        var result = Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0\levelstartat1{\leveltext \'02\'00.;}{\levelnumbers\'01;}}\listid1}}" +
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
        var result = Read(@"{\rtf1{\*\listtable{\list{\listlevel\levelnfc0\levelstartat1{\leveltext \'02\'00.;}{\levelnumbers\'01;}}\listid1}}" +
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

    private static string Label(string listTable, string paragraph = @"\pard\ls1 Item")
    {
        DocumentReadResult result = Read(@"{\rtf1\ansi{\*\listtable{\list" + listTable + @"\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}}" + paragraph + "}");
        return new ListNumberer().Advance(result.Document.Paragraphs[0].Style) ?? string.Empty;
    }

    [Theory]
    // tdf122455 and tdf116265: LibreOffice draws no label for a numbered level
    // with no \leveltext, or an empty one.
    [InlineData(@"{\listlevel\levelnfc0\levelstartat1}", "")]
    [InlineData(@"{\listlevel\levelnfc0\levelstartat1{\leveltext}{\levelnumbers\'01;}}", "")]
    // A bullet level with none still draws LibreOffice's default bullet.
    [InlineData(@"{\listlevel\levelnfc23\levelstartat1}", "\u2022")]
    // invalidParagraphStyle: no \levelnfc is decimal.
    [InlineData(@"{\listlevel\levelstartat1{\leveltext\'02\'00.;}{\levelnumbers\'01;}}", "1.")]
    // tdf108416: a control word inside the level text is not part of it.
    [InlineData(@"{\listlevel\levelnfc0\levelstartat1{\leveltext\leveltemplateid510954734\'03(\'00);}{\levelnumbers\'02;}}", "(1)")]
    // A \levelnfc LibreOffice does not map draws no number but keeps the text.
    [InlineData(@"{\listlevel\levelnfc9\levelstartat1{\leveltext\'02\'00.;}{\levelnumbers\'01;}}", ".")]
    // arabic-zero-numbering: \levelnfc22 is decimal with a leading zero.
    [InlineData(@"{\listlevel\levelnfc22\levelstartat1{\leveltext\'02\'00.;}{\levelnumbers\'01;}}", "01.")]
    // cjklist12, cjklist34, cjklist38.
    [InlineData(@"{\listlevel\levelnfc12\levelstartat1{\leveltext\'02\'00.;}{\levelnumbers\'01;}}", "ｱ.")]
    [InlineData(@"{\listlevel\levelnfc34\levelstartat10{\leveltext\'02\'00.;}{\levelnumbers\'01;}}", "壹拾.")]
    [InlineData(@"{\listlevel\levelnfc38\levelstartat2{\leveltext\'02\'00.;}{\levelnumbers\'01;}}", "贰.")]
    // tdf106953 and tdf78506: a ';' given as \u59 in \levelnumbers leaves
    // LibreOffice 24.2 with no label for the level.
    [InlineData(@"{\listlevel\levelnfc0\levelstartat1{\leveltext\'02\'00.;}{\levelnumbers \'01{\uc1 \u59 ?}}}", "")]
    [InlineData(@"{\listlevel\levelnfc0\levelstartat1{\leveltext{\uc1 \u2 ?}{\uc1 \u0 ?}{\uc1 \u46 ?}{\uc1 \u59 ?}}{\levelnumbers\'01;}}", "1.")]
    // tdf104016: a bullet in the Symbol font is the character it draws.
    [InlineData(@"{\listlevel\levelnfc23\levelstartat1{\leveltext\'01\'b7 ;}{\levelnumbers;}\f2}", "•")]
    public void Reads_Level_Labels_As_LibreOffice_Does(string level, string expected)
    {
        string document = @"{\fonttbl{\f2\fnil\fcharset0 Symbol;}}";
        DocumentReadResult result = Read(@"{\rtf1\ansi" + document + @"{\*\listtable{\list" + level + @"\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}}\pard\ls1 Item}");
        Assert.Equal(expected, new ListNumberer().Advance(result.Document.Paragraphs[0].Style) ?? string.Empty);
    }

    [Fact]
    public void Level_Numbers_Are_Counted_Back_From_The_Level_Itself()
    {
        // LibreOffice numbers the n-th of k positions at level l as %(n + l + 1 - k):
        // one position at the second level is that level's own number.
        const string table = @"{\listlevel\levelnfc3\levelstartat1{\leveltext\'02\'00.;}{\levelnumbers\'01;}}" +
            @"{\listlevel\levelnfc0\levelstartat1{\leveltext\'03\'00.\'01;}{\levelnumbers\'01\'03;}}" +
            @"{\listlevel\levelnfc2\levelstartat1{\leveltext\'02\'02);}{\levelnumbers\'01;}}";
        DocumentReadResult result = Read(@"{\rtf1\ansi{\*\listtable{\list" + table + @"\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}}" +
            @"\pard\ls1 A\par\pard\ls1\ilvl1 B\par\pard\ls1\ilvl2 C}");
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "A.", "A.1", "i)" },
            System.Linq.Enumerable.Select(result.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }

    [Fact]
    public void A_Double_Byte_Level_Text_Is_Decoded_Before_It_Is_Counted()
    {
        // cp950listleveltext2: the length and the positions count Big5 characters,
        // each written as a lead byte and a trail byte that may be a letter.
        DocumentReadResult result = Read(@"{\rtf1\ansi\ansicpg950{\*\listtable{\list{\listlevel\levelnfc30" +
            @"{\leveltext\leveltemplateid2028996680\'06\'abe\'b8m\'00\'a1B\'ab\'e1\'b8m;}{\levelnumbers\'03;}}" +
            @"\listid1}}{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}}\pard\ls1 Item}");
        Assert.Equal("前置甲、後置",
            new ListNumberer().Advance(result.Document.Paragraphs[0].Style));
    }

    [Fact]
    public void List_And_Instance_Zero_Are_Lists()
    {
        // tdf141964_numId0 and arabic-zero-numbering: \listid0 and \ls0 name lists.
        DocumentReadResult result = Read(@"{\rtf1\ansi{\*\listtable{\list{\listlevel\levelnfc0\levelstartat1" +
            @"{\leveltext\'02\'00.;}{\levelnumbers\'01;}}\listid0}}" +
            @"{\*\listoverridetable{\listoverride\listid0\listoverridecount0\ls0}}\pard\ls0 A\par\pard\ls0 B}");
        Assert.Empty(result.Diagnostics);
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "1.", "2." },
            System.Linq.Enumerable.Select(result.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }

    [Fact]
    public void Reads_Word_95_Paragraph_Numbering()
    {
        // fdo47107, tdf104085 and tdf116841: \pn numbers the paragraphs it stands
        // in, and a bullet is the first character of \pntxtb.
        DocumentReadResult result = Read(@"{\rtf1\ansi{\fonttbl{\f1\fcharset2 Symbol;}}" +
            @"\pard{\*\pn\pnlvlbody\pndec\pnstart1{\pntxta .}}{Alpha\par Beta\par}" +
            @"\pard{\*\pn\pnlvlblt\pnf1\pnstart1{\pntxtb \'b7}}{Gamma\par}" +
            @"\pard{\*\pn\pnlvlblt\pnstart0{\pntxtb\bullet}}{Delta\par}" +
            @"\pard{\*\pn\pnlvlbody\pnlcrm\pnstart3{\pntxtb (}{\pntxta )}}Epsilon\par\pard Plain}");
        Assert.Equal("Alpha\nBeta\nGamma\nDelta\nEpsilon\nPlain", result.Document.PlainText);
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "1.", "2.", "•", "•", "(iii)", null },
            System.Linq.Enumerable.Select(result.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }

    [Fact]
    public void A_Pard_Before_The_Paragraph_Mark_Keeps_The_Paragraphs_Numbering()
    {
        // tdf141964_numId0: the last item's \pard comes after its text and before
        // its \par; LibreOffice still numbers it, and the paragraph after it not.
        DocumentReadResult result = Read(@"{\rtf1\ansi{\*\listtable{\list{\listlevel\levelnfc0\levelstartat1" +
            @"{\leveltext\'02\'00.;}{\levelnumbers\'01;}}\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}}" +
            @"\pard\ls1 First\par\pard\ls1 Last\pard\li68 \par After\par}");
        Assert.Equal("First\nLast\nAfter", result.Document.PlainText);
        var numberer = new ListNumberer();
        Assert.Equal(new[] { "1.", "2.", null },
            System.Linq.Enumerable.Select(result.Document.Paragraphs, p => numberer.Advance(p.Style)));
    }
}
