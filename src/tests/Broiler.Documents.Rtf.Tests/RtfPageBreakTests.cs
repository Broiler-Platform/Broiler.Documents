using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// The break a document asks for by name. Nothing in the model could carry one,
/// so both of RTF's spellings were read as nothing at all - \pagebb fell through
/// the paragraph switch as an unknown word and \page through the body switch as
/// an unknown entity - and a document whose second page exists because it said so
/// came back one page long, with no diagnostic, because there was nowhere for the
/// break to be lost from.
/// </summary>
public sealed class RtfPageBreakTests
{
    private static DocumentReadResult ReadResult(string rtf) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(rtf));

    private static RichTextDocument Read(string rtf) => ReadResult(rtf).Document;

    private static string Write(RichTextDocument document) =>
        Encoding.ASCII.GetString(RtfWriter.WriteToArray(document));

    private static RichTextDocument RoundTrip(RichTextDocument document) =>
        RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

    private static RichTextParagraph Para(string text, bool breaks = false) =>
        RichTextParagraph.Plain(text)
            .WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = breaks });

    private static RichTextDocument Doc(params RichTextParagraph[] paragraphs) =>
        RichTextDocument.FromParagraphs(paragraphs);

    private static bool BreaksAt(RichTextDocument document, int index) =>
        document.Paragraphs[index].Style.PageBreakBefore;

    // ---- reading \page, which RTF states in the text ----

    [Theory(Timeout = 600000)]
    // The break between the two paragraphs, written both ways round. A \page
    // that interrupts a paragraph has to end it, or the break would land in
    // front of the text it was written after instead of the text it precedes.
    [InlineData("{\\rtf1 first\\page second\\par}")]
    [InlineData("{\\rtf1 first\\par\\page second\\par}")]
    public void A_Page_Breaks_Before_The_Paragraph_That_Follows_It(string rtf)
    {
        RichTextDocument document = Read(rtf);

        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal("first", document.Paragraphs[0].Text);
        Assert.Equal("second", document.Paragraphs[1].Text);
        Assert.False(BreaksAt(document, 0));
        Assert.True(BreaksAt(document, 1));
    }

    [Fact(Timeout = 600000)]
    public void A_Page_Before_Any_Text_Belongs_To_The_First_Paragraph()
    {
        RichTextDocument document = Read("{\\rtf1\\page only\\par}");

        Assert.Equal(1, document.ParagraphCount);
        Assert.Equal("only", document.Paragraphs[0].Text);
        Assert.True(BreaksAt(document, 0));
    }

    [Fact(Timeout = 600000)]
    public void A_Page_Between_Two_Pars_Keeps_The_Blank_Paragraph_And_Breaks_On_It()
    {
        RichTextDocument document = Read("{\\rtf1 a\\par\\page\\par b\\par}");

        Assert.Equal(3, document.ParagraphCount);
        Assert.Equal(string.Empty, document.Paragraphs[1].Text);
        Assert.True(BreaksAt(document, 1));
        Assert.False(BreaksAt(document, 2));
    }

    [Fact(Timeout = 600000)]
    public void One_Page_Breaks_Once()
    {
        // The flag is spent by the paragraph that takes it. Left set, one break
        // in the source would start every later paragraph on a page of its own.
        RichTextDocument document = Read("{\\rtf1 a\\par\\page b\\par c\\par}");

        Assert.True(BreaksAt(document, 1));
        Assert.False(BreaksAt(document, 2));
    }

    // ---- reading \pagebb, the paragraph property ----

    [Fact(Timeout = 600000)]
    public void Pagebb_Breaks_Before_Its_Own_Paragraph()
    {
        RichTextDocument document = Read("{\\rtf1 first\\par\\pagebb second\\par}");

        Assert.Equal(2, document.ParagraphCount);
        Assert.False(BreaksAt(document, 0));
        Assert.True(BreaksAt(document, 1));
    }

    [Fact(Timeout = 600000)]
    public void Pard_Resets_Pagebb_As_It_Resets_Every_Paragraph_Property()
    {
        RichTextDocument document = Read("{\\rtf1\\pagebb a\\par\\pard b\\par}");

        Assert.True(BreaksAt(document, 0));
        Assert.False(BreaksAt(document, 1));
    }

    [Fact(Timeout = 600000)]
    public void Pagebb0_Turns_The_Break_Off()
    {
        Assert.False(BreaksAt(Read("{\\rtf1\\pagebb\\pagebb0 a\\par}"), 0));
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Break_Out_Of_A_Writer_Style_Paragraph_Block()
    {
        // The shape LibreOffice exports it in: \pagebb sits among the other
        // paragraph properties, after \pard\plain and a style reference, with no
        // space of its own before the next control word.
        RichTextDocument document = Read(
            "{\\rtf1\\ansi\\pard\\plain \\s26\\sb0\\sa283\\ql\\ltrpar{\nbefore}\n" +
            "\\par \\pard\\plain \\s26\\sb0\\sa283\\pagebb\\ql\\fi0\\li0\\ltrpar{\nafter}\n\\par }");

        Assert.Equal(2, document.ParagraphCount);
        Assert.False(BreaksAt(document, 0));
        Assert.True(BreaksAt(document, 1));
    }

    // ---- no false positives ----

    [Theory(Timeout = 600000)]
    [InlineData("{\\rtf1 a\\par b\\par}")]
    // The word in the text is text, and the two control words that merely begin
    // with it are not this one.
    [InlineData("{\\rtf1 a\\par page b\\par}")]
    [InlineData("{\\rtf1\\pgnstart1 a\\par\\pgncont b\\par}")]
    public void A_Document_That_Asks_For_No_Break_Gets_None(string rtf)
    {
        RichTextDocument document = Read(rtf);

        Assert.All(document.Paragraphs, p => Assert.False(p.Style.PageBreakBefore));
    }

    [Fact(Timeout = 600000)]
    public void Pagebb_Is_Not_Read_As_A_Page_Followed_By_Text()
    {
        // \pagebb is one control word. Split into \page and "bb" it would both
        // break in the wrong place and put two letters in the document.
        RichTextDocument document = Read("{\\rtf1\\pagebb a\\par}");

        Assert.Equal(1, document.ParagraphCount);
        Assert.Equal("a", document.Paragraphs[0].Text);
    }

    // ---- a break with no paragraph to land on ----

    [Theory(Timeout = 600000)]
    // At the end of the document, and twice over with nothing in between. Both
    // ask for a blank page, which a flag on a paragraph cannot express.
    [InlineData("{\\rtf1 a\\par\\page}")]
    [InlineData("{\\rtf1 a\\page}")]
    [InlineData("{\\rtf1 a\\par\\page\\page b\\par}")]
    public void A_Break_With_No_Paragraph_After_It_Is_Dropped_And_Reported(string rtf)
    {
        DocumentReadResult result = ReadResult(rtf);

        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.pagebreak.empty");
        Assert.Equal("a", result.Document.Paragraphs[0].Text);
        Assert.False(BreaksAt(result.Document, 0));
    }

    [Fact(Timeout = 600000)]
    public void Two_Breaks_In_A_Row_Still_Break_Once_Before_What_Follows()
    {
        RichTextDocument document = Read("{\\rtf1 a\\par\\page\\page b\\par}");

        Assert.Equal(2, document.ParagraphCount);
        Assert.True(BreaksAt(document, 1));
    }

    [Fact(Timeout = 600000)]
    public void A_Document_With_A_Break_That_Lands_Says_Nothing()
    {
        DocumentReadResult result = ReadResult("{\\rtf1 a\\par\\page b\\par}");

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "rtf.pagebreak.empty");
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    // ---- writing ----

    [Fact(Timeout = 600000)]
    public void A_Break_Is_Written_As_Page_In_The_Paragraph_It_Precedes()
    {
        string rtf = Write(Doc(Para("first"), Para("second", breaks: true)));

        int page = rtf.IndexOf("\\page ", StringComparison.Ordinal);
        Assert.True(page > 0, "the break is written as \\page");
        Assert.True(page > rtf.IndexOf("first", StringComparison.Ordinal));
        Assert.True(page < rtf.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void A_Break_On_The_First_Paragraph_Is_Written_As_Pagebb()
    {
        // \page at the head of the body would draw a blank first page in a reader
        // that honours it, which is a page the document never asked for.
        string rtf = Write(Doc(Para("only", breaks: true)));

        Assert.Contains("\\pagebb", rtf, StringComparison.Ordinal);
        Assert.DoesNotContain("\\page ", rtf, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Without_A_Break_Writes_Neither_Word()
    {
        string rtf = Write(Doc(Para("first"), Para("second")));

        Assert.DoesNotContain("\\page", rtf, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_In_A_Header_Is_Read_Even_Though_It_Cannot_Be_Written_Back()
    {
        // The asymmetry the conformance document records. \pagebb is a paragraph
        // property wherever a paragraph is, so a header carrying one arrives in
        // the model; there is still no page for it to start, so it does not go
        // back out.
        RichTextDocument document = Read("{\\rtf1{\\header\\pagebb letterhead\\par}body\\par}");

        Assert.True(document.RunningContent.Header(PageSelection.Default)[0].Style.PageBreakBefore);
        Assert.False(BreaksAt(document, 0));
    }

    [Fact(Timeout = 600000)]
    public void A_Break_On_A_Header_Paragraph_Is_Dropped_And_Reported()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("body")
            .WithRunningContent(RunningContent.Empty.WithHeader(
                PageSelection.Default,
                [Para("letterhead", breaks: true)]));

        using var stream = new MemoryStream();
        DocumentWriteResult result = RtfWriter.Write(document, stream);

        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.pagebreak.dropped");
        Assert.DoesNotContain(
            "\\page", Encoding.ASCII.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    // ---- round trip ----

    [Fact(Timeout = 600000)]
    public void The_Break_Survives_A_Round_Trip()
    {
        RichTextDocument document = Doc(Para("first"), Para("second", breaks: true), Para("third"));

        RichTextDocument round = RoundTrip(document);

        DocumentAssert.Equivalent(document, round);
        Assert.True(BreaksAt(round, 1));
    }

    [Fact(Timeout = 600000)]
    public void A_Break_On_The_First_Paragraph_Survives_A_Round_Trip()
    {
        RichTextDocument document = Doc(Para("only", breaks: true), Para("next"));

        RichTextDocument round = RoundTrip(document);

        DocumentAssert.Equivalent(document, round);
        Assert.True(BreaksAt(round, 0));
    }

    [Fact(Timeout = 600000)]
    public void A_Document_With_No_Break_Round_Trips_Without_Gaining_One()
    {
        RichTextDocument document = Doc(Para("first"), Para("second"));

        DocumentAssert.Equivalent(document, RoundTrip(document));
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Keeps_The_Rest_Of_The_Paragraph_Style()
    {
        RichTextDocument document = Doc(
            Para("first"),
            RichTextParagraph.Plain("second").WithParagraphStyle(ParagraphStyle.Default with
            {
                PageBreakBefore = true,
                Alignment = TextAlignment.Center,
                IndentLevel = 2,
                SpacingBefore = 6f,
            }));

        DocumentAssert.Equivalent(document, RoundTrip(document));
    }
}
