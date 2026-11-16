using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Office conformance issue #249 (rtf-layout): the font text that names none is
/// set in, the line above the footnotes, and the formatting a paragraph or run
/// gets from a style it does not repeat, each as LibreOffice 24.2 reads RTF.
/// </summary>
public sealed class RtfIssue249LayoutTests
{
    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    [Theory]
    // gutter-top.rtf, tdf118047.rtf: no font table and no \deff.
    [InlineData(@"{\rtf1 Hello\par}")]
    // \deff with no table to apply it from.
    [InlineData(@"{\rtf1\deff0 Hello\par}")]
    // A table, but no \deff: writerfilter does not imply font 0.
    [InlineData(@"{\rtf1{\fonttbl{\f0 Arial;}} Hello\par}")]
    public void Text_That_Names_No_Font_Is_Set_In_Times_New_Roman(string rtf)
    {
        RichTextDocument document = Read(rtf);

        Assert.Equal("Times New Roman", document.StyleDefaults.FontFamilyOf(document.Paragraphs[0].Runs[0].Style));
    }

    [Theory]
    // \deff names a font the table does not have, or names it blank: the
    // unstated family, the same fallback face.
    [InlineData(@"{\rtf1\deff3{\fonttbl{\f1 Arial;}} Hello\par}")]
    [InlineData(@"{\rtf1\deff0{\fonttbl{\f0 ;}} Hello\par}")]
    public void A_Default_Font_The_Table_Does_Not_Name_Is_Left_Unstated(string rtf)
    {
        Assert.Null(Read(rtf).StyleDefaults.FontFamily);
    }

    [Theory]
    // char-color.rtf: a run's \f names a font no table has.
    [InlineData(@"{\rtf1\deff0{\f31502 Hello}\par}")]
    [InlineData(@"{\rtf1\deff1{\fonttbl{\f1 Arial;}}{\f5 Hello}\par}")]
    public void A_Font_The_Table_Does_Not_Name_Is_The_Fallback_Sans_Serif(string rtf)
    {
        RichTextDocument document = Read(rtf);

        Assert.Equal("sans-serif", document.StyleDefaults.FontFamilyOf(document.Paragraphs[0].Runs[0].Style));
    }

    [Fact]
    public void A_Default_Font_The_Table_Names_Is_Kept()
    {
        RichTextDocument document = Read(@"{\rtf1\deff0{\fonttbl{\f0 Arial;}} Hello\par}");

        Assert.Equal("Arial", document.StyleDefaults.FontFamily);
    }

    [Fact]
    public void Without_A_Footnote_Separator_The_Line_Above_The_Notes_Is_Not_Drawn()
    {
        // copypaste-footnote.rtf and tdf122430.rtf have notes and no \ftnsep.
        RichTextDocument document = Read(@"{\rtf1 aaa{\super\chftn{\*\footnote\chftn\pard\plain note}}\par}");

        Assert.False(document.NoteNumbering.DrawsSeparatorLine);
    }

    [Fact]
    public void A_Chftnsep_Separator_Draws_The_Line()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\*\ftnsep\pard\plain\chftnsep\par}aaa{\super\chftn{\*\footnote\chftn\pard\plain note}}\par}");

        Assert.True(document.NoteNumbering.DrawsSeparatorLine);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_Separator_Line_Survives_A_Round_Trip(bool drawn)
    {
        RichTextParagraph citing = RichTextParagraph.Plain("aaa").InsertText(3, DocumentNote.PlaceholderText,
            InlineStyle.Default with { Note = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("note")]) });
        RichTextDocument document = RichTextDocument.FromParagraphs([citing])
            .WithNoteNumbering(NoteNumbering.Default with { DrawsSeparatorLine = drawn });

        RichTextDocument restored = RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

        Assert.Equal(drawn, restored.NoteNumbering.DrawsSeparatorLine);
    }

    [Fact]
    public void A_Separator_Without_Chftnsep_Draws_No_Line()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\*\ftnsep\pard\plain\par}aaa{\super\chftn{\*\footnote\chftn\pard\plain note}}\par}");

        Assert.False(document.NoteNumbering.DrawsSeparatorLine);
    }

    [Theory]
    // tdf118047.rtf: a Normal entry without \s0, and text that names no style.
    [InlineData(@"{\rtf1{\stylesheet{\fs72\sa3200\qc\b Normal;}}Text\par}")]
    [InlineData(@"{\rtf1{\stylesheet{\s0\fs72\sa3200\qc\b Normal;}}\pard\plain\s0 Text\par}")]
    [InlineData(@"{\rtf1{\stylesheet{\s0 Normal;}{\s1\fs72\sa3200\qc\b Heading;}}\pard\plain\s1 Text\par}")]
    public void What_A_Paragraphs_Style_States_Is_Not_Applied_Unless_The_Paragraph_Repeats_It(string rtf)
    {
        RichTextParagraph paragraph = Read(rtf).Paragraphs[0];

        Assert.Equal(0, paragraph.Style.SpacingAfter);
        Assert.Equal(TextAlignment.Left, paragraph.Style.Alignment);
        InlineStyle run = paragraph.Runs[0].Style;
        Assert.Null(run.FontSize);
        Assert.False(run.Bold);
    }

    [Fact]
    public void A_Paragraph_That_Repeats_Its_Styles_Formatting_Keeps_It()
    {
        RichTextParagraph paragraph = Read(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\s1\fs72\sa3200\qc\b Heading;}}\pard\plain\s1\sa3200\qc\fs72\b Text\par}").Paragraphs[0];

        Assert.Equal(160, paragraph.Style.SpacingAfter);
        Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment);
        Assert.Equal(36, paragraph.Runs[0].Style.FontSize);
        Assert.True(paragraph.Runs[0].Style.Bold);
    }

    [Fact]
    public void What_A_Style_Inherits_Is_Applied()
    {
        // soffice: a style that states nothing of its own, based on a Normal
        // that states Courier New, 20 point and centred, sets its text so.
        RichTextParagraph paragraph = Read(
            @"{\rtf1{\fonttbl{\f1 Courier New;}}{\stylesheet{\s0\qc\f1\fs40 Normal;}{\s1\sbasedon0 Foo;}}\pard\plain\s1 Hello\par}").Paragraphs[0];

        Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment);
        Assert.Equal("Courier New", paragraph.Runs[0].Style.FontFamily);
        Assert.Equal(20, paragraph.Runs[0].Style.FontSize);
    }

    [Fact]
    public void What_A_Style_States_Outside_The_Reset_Properties_Is_Applied()
    {
        // The style's \caps and \strike stay, as LibreOffice keeps them.
        InlineStyle run = Read(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\s1\caps\strike\b Foo;}}\pard\plain\s1 Hello\par}").Paragraphs[0].Runs[0].Style;

        Assert.Equal(TextCapitalization.AllCaps, run.Capitalization);
        Assert.True(run.Strikethrough);
        Assert.False(run.Bold);
    }

    [Fact]
    public void A_Character_Styles_Own_Formatting_Is_Not_Applied_Unless_The_Run_Repeats_It()
    {
        RichTextParagraph paragraph = Read(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\*\cs2\additive\b\fs40 Strong;}}\pard\plain {\cs2 Hello}{\cs2\b\fs40 World}\par}").Paragraphs[0];

        Assert.False(paragraph.Runs[0].Style.Bold);
        Assert.Null(paragraph.Runs[0].Style.FontSize);
        InlineStyle repeated = paragraph.Runs[^1].Style;
        Assert.True(repeated.Bold);
        Assert.Equal(20, repeated.FontSize);
    }

    [Theory]
    // fdo74599.rtf, fdo80742.rtf, fdo83204.rtf: a heading style with no
    // \sbasedon is Writer's, based on "Heading".
    [InlineData("heading 3")]
    [InlineData("Heading 2")]
    [InlineData("Title")]
    [InlineData("Subtitle")]
    public void A_Writer_Heading_Style_Without_A_Parent_Takes_Writers_Heading_Formatting(string name)
    {
        RichTextParagraph paragraph = Read(
            @"{\rtf1{\stylesheet{\s3 " + name + @";}}\pard\plain \s3 This is gray\par}").Paragraphs[0];

        Assert.Equal("Liberation Sans", paragraph.Runs[0].Style.FontFamily);
        Assert.Equal(14, paragraph.Runs[0].Style.FontSize);
        Assert.Equal(12, paragraph.Style.SpacingBefore, 1);
        Assert.Equal(6, paragraph.Style.SpacingAfter, 1);
        Assert.True(paragraph.Style.KeepWithNext);
    }

    [Fact]
    public void A_Heading_Style_The_Paragraph_Does_Not_Repeat_Keeps_Heading_Font_But_Not_Its_Own_Size()
    {
        // fdo62044.rtf: {\s1\fs20 Heading 1;} sets the heading in 12 point
        // Liberation Sans, 12 points down.
        RichTextParagraph paragraph = Read(
            @"{\rtf1{\stylesheet{\s1\fs20 Heading 1;}}\s1 this is heading 1\par}").Paragraphs[0];

        Assert.Equal("Liberation Sans", paragraph.Runs[0].Style.FontFamily);
        Assert.Null(paragraph.Runs[0].Style.FontSize);
        Assert.Equal(12, paragraph.Style.SpacingBefore, 1);
    }

    [Theory]
    [InlineData(@"\sbasedon0 heading 1")]
    [InlineData("heading 1,h1")]
    [InlineData("Heading")]
    public void Other_Styles_Do_Not_Take_Writers_Heading_Formatting(string name)
    {
        RichTextParagraph paragraph = Read(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\s3 " + name + @";}}\pard\plain \s3 Text\par}").Paragraphs[0];

        Assert.Null(paragraph.Runs[0].Style.FontFamily);
        Assert.Equal(0, paragraph.Style.SpacingBefore);
        Assert.False(paragraph.Style.KeepWithNext);
    }

    [Fact]
    public void A_Notes_Last_Par_Opens_An_Empty_Paragraph()
    {
        // cp1000018.rtf: soffice sets a note ending "note\par" a line higher.
        RichTextDocument document = Read(
            @"{\rtf1 aaa{\super\chftn{\*\footnote\chftn\pard\plain\fs20 note\par}}\par}");

        DocumentNote note = Assert.Single(document.Notes);
        Assert.Equal(["note", ""], note.Paragraphs.Select(p => p.Text));
        Assert.Equal(10, note.Paragraphs[1].MarkStyle.FontSize);
    }

    [Theory]
    [InlineData(@"{\af1 x}")]
    [InlineData(@"{\loch\af1 x}")]
    [InlineData(@"{\hich\af1 x}")]
    [InlineData(@"{\ltrch\af1 x}")]
    [InlineData(@"{\rtlch\loch\af1 x}")]
    [InlineData(@"{\f0\af1 x}")]
    public void An_Associated_Font_Outside_Complex_And_East_Asian_Runs_Is_The_Latin_Font(string run)
    {
        RichTextDocument document = Read(@"{\rtf1\deff0{\fonttbl{\f0 Times New Roman;}{\f1 Courier New;}}\pard\plain " + run + @"\par}");

        Assert.Equal("Courier New", document.Paragraphs[0].Runs[0].Style.FontFamily);
    }

    [Theory]
    [InlineData(@"{\rtlch\af1 x}")]
    [InlineData(@"{\rtlch\fcs1 \af1 \ltrch\fcs0 x}")]
    [InlineData(@"{\dbch\af1 x}")]
    public void An_Associated_Font_For_Complex_Or_East_Asian_Text_Leaves_The_Latin_Font(string run)
    {
        RichTextDocument document = Read(@"{\rtf1\deff0{\fonttbl{\f0 Times New Roman;}{\f1 Courier New;}}\pard\plain " + run + @"\par}");

        Assert.Equal("Times New Roman", document.StyleDefaults.FontFamilyOf(document.Paragraphs[0].Runs[0].Style));
    }

    [Fact]
    public void An_Associated_Size_Outside_Complex_Runs_Is_The_Latin_Size()
    {
        RichTextDocument document = Read(@"{\rtf1\pard\plain {\afs40 x}{\rtlch\afs30 y}\par}");

        Assert.Equal(20, document.Paragraphs[0].Runs[0].Style.FontSize);
        Assert.Null(document.Paragraphs[0].Runs[^1].Style.FontSize);
    }

    [Fact]
    public void A_Paragraph_Mark_Stated_With_An_Associated_Font_Repeats_Its_Styles_Font()
    {
        // all_gaps_word.rtf: Word states an empty paragraph's font as
        // \loch\af31506, Normal's own font, so the mark keeps it.
        RichTextParagraph paragraph = Read(
            @"{\rtf1\deff0{\fonttbl{\f0 Times New Roman;}{\f1 Courier New;}}{\stylesheet{\loch\f1 Normal;}}" +
            @"\pard\plain \rtlch\fcs1 \af0 \ltrch\fcs0 \loch\af1\hich\af1\dbch\af0 \par}").Paragraphs[0];

        Assert.Equal("Courier New", paragraph.MarkStyle.FontFamily);
    }

    [Fact]
    public void Html_Auto_Spacing_Is_14_Points_None_Above_The_First_Paragraph_And_Collapses()
    {
        // tdf107480.rtf: \htmautsp, and heading paragraphs with \sbauto1\saauto1.
        RichTextDocument document = Read(@"{\rtf1\htmautsp\pard\sbauto1\saauto1 A\par\pard\sbauto1\saauto1 B\par}");

        Assert.True(document.StyleDefaults.CollapsesParagraphSpacing);
        Assert.Equal(0, document.Paragraphs[0].Style.SpacingBefore);
        Assert.Equal(14, document.Paragraphs[0].Style.SpacingAfter);
        Assert.Equal(14, document.Paragraphs[1].Style.SpacingBefore);
    }

    [Fact]
    public void Auto_Spacing_Without_Htmautsp_Is_5_Points_And_Adds_Up()
    {
        RichTextDocument document = Read(@"{\rtf1\pard\sbauto1\saauto1 A\par\pard\sbauto1\saauto1 B\par}");

        Assert.False(document.StyleDefaults.CollapsesParagraphSpacing);
        Assert.Equal(5, document.Paragraphs[0].Style.SpacingBefore);
        Assert.Equal(5, document.Paragraphs[1].Style.SpacingBefore);
    }

    [Fact]
    public void A_Styles_Auto_Spacing_Applies_To_Its_Paragraphs()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\stylesheet{\s3\sbauto1\saauto1 Foo;}}\htmautsp\pard\plain\s3 A\par\pard\plain\s3 B\par}");

        Assert.Equal(0, document.Paragraphs[0].Style.SpacingBefore);
        Assert.Equal(14, document.Paragraphs[1].Style.SpacingBefore);
        Assert.Equal(14, document.Paragraphs[1].Style.SpacingAfter);
    }

    [Theory]
    // gutter-left.rtf, gutter-top.rtf, rtl-gutter.rtf
    [InlineData(@"\gutter720", 108, 72, 72)]
    [InlineData(@"\gutter720\gutterprl", 72, 72, 108)]
    [InlineData(@"\gutter720\rtlgutter", 72, 108, 72)]
    public void The_Gutter_Widens_Its_Margin(string gutter, double left, double right, double top)
    {
        PageGeometry page = Assert.IsType<PageGeometry>(Read(
            @"{\rtf1\paperw12240\paperh15840\margl1440\margr1440\margt1440\margb1440" + gutter + @"\pard\plain Half\par}").PageGeometry);

        Assert.Equal(left, page.MarginLeft);
        Assert.Equal(right, page.MarginRight);
        Assert.Equal(top, page.MarginTop);
        Assert.Equal(72, page.MarginBottom);
    }

    [Fact]
    public void Contextual_Spacing_Removes_The_Space_Between_Paragraphs_Of_One_Style()
    {
        // tdf113202.rtf
        RichTextDocument document = Read(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\s15 List;}}\pard\plain\s15\sa200\sb100\contextualspace A\par B\par" +
            @"\pard\plain\s0\sa200\sb100\contextualspace C\par}");

        Assert.Equal(0, document.Paragraphs[0].Style.SpacingAfter);
        Assert.Equal(0, document.Paragraphs[1].Style.SpacingBefore);
        // C is in another style: the space between B and C stays.
        Assert.Equal(10, document.Paragraphs[1].Style.SpacingAfter);
        Assert.Equal(5, document.Paragraphs[2].Style.SpacingBefore);
    }

    [Fact]
    public void Without_Contextual_Spacing_Paragraphs_Of_One_Style_Keep_Their_Space()
    {
        RichTextDocument document = Read(@"{\rtf1\pard\plain\sa200 A\par B\par}");

        Assert.Equal(10, document.Paragraphs[0].Style.SpacingAfter);
    }

    [Theory]
    [InlineData("dptxbx{\\dptxbxtext\\pard\\plain inner\\par }", false)]
    [InlineData("dprect", true)]
    public void A_Legacy_Text_Box_Is_Not_Filled(string primitive, bool filled)
    {
        // tdf91074.rtf: \dpfillpat0 with black colours; Writer fills the
        // rectangle, not the text box.
        RichTextDocument document = Read(
            @"{\rtf1{\*\do\dobxpage\dobypage\dodhgt8192\" + primitive +
            @"\dpx674\dpy725\dpxsize1875\dpysize1020\dpfillbgcr0\dpfillbgcg0\dpfillbgcb0\dpfillpat0}\par outer\par}");

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal(filled, shape.Fill is not null);
    }

    [Theory]
    // tdf108123.rtf: a heading with \sb480 opening a section's page.
    [InlineData(@"A\par\sect\sectd\pard\sb480 B\par", true)]
    [InlineData(@"A\par\pard\sb480\page B\par", true)]
    [InlineData(@"A\par\page\pard\sb480 B\par", true)]
    [InlineData(@"A\par\pard\pagebb\sb480 B\par", true)]
    [InlineData(@"\pard\sb480 A\page B\par", false)]
    public void A_Page_A_Paragraph_Opens_Itself_Keeps_Its_Space_Above(string body, bool keeps)
    {
        RichTextParagraph b = Read(@"{\rtf1 " + body + "}").Paragraphs.Last(paragraph => paragraph.Text == "B");

        Assert.True(b.Style.PageBreakBefore);
        Assert.Equal(keeps, b.Style.PageBreakKeepsSpaceAbove);
    }

    [Fact]
    public void A_Page_Field_Without_A_Result_Shows_The_Page_Number()
    {
        // fdo48193.rtf
        RichTextDocument document = Read(@"{\rtf1\pard\plain foo{\field{\*\fldinst {\f8\fs16\f8  PAGE }}}bar\par}");

        RichTextParagraph paragraph = document.Paragraphs[0];
        Assert.Equal("foo1bar", paragraph.Text);
        Assert.Equal(PageFieldKind.PageNumber, paragraph.StyleAt(3).PageField?.Kind);
    }

    [Fact]
    public void A_Shape_Stating_No_Text_Inset_Takes_The_Default_One()
    {
        DocumentShape shape = Assert.Single(Read(
            @"{\rtf1{\shp{\*\shpinst\shpleft1440\shptop1440\shpright5040\shpbottom3600\shpbxpage\shpbypage\shpwr3\shpz0" +
            @"{\sp{\sn shapeType}{\sv 202}}{\shptxt Hello\par}}}\par}").Shapes);

        Assert.Equal(new TableCellPadding(7.2, 3.6, 7.2, 3.6), shape.TextPadding);
    }

    [Fact]
    public void A_Body_Shape_Is_Kept_Below_The_Pages_Top()
    {
        // soffice 24.2: a text box 50 points above a first paragraph 28 points
        // down starts at the page's top edge.
        DocumentShape shape = Assert.Single(Read(
            @"{\rtf1\margt567{\shp{\*\shpinst\shpleft567\shptop-1000\shpright3000\shpbottom1000\shpwr3\shpz0" +
            @"{\sp{\sn shapeType}{\sv 202}}{\shptxt Hello\par}}}\par}").Shapes);

        Assert.True(shape.KeptBelowPageTop);
        Assert.Equal(0, shape.TopOnPage(28.35));
        Assert.Equal(50, shape.WithParagraphIndex(0).TopOnPage(100), 3);
    }

    [Fact]
    public void Space_Above_The_Opening_Paragraph_Is_Kept()
    {
        Assert.True(Read(@"{\rtf1\pard\sb480 Foo\par}").StyleDefaults.PreserveOpeningParagraphSpacing);
    }

    [Fact]
    public void A_Note_Without_A_Last_Par_Has_No_Empty_Paragraph()
    {
        RichTextDocument document = Read(@"{\rtf1 aaa{\super\chftn{\*\footnote\chftn\pard\plain note}}\par}");

        Assert.Equal(["note"], Assert.Single(document.Notes).Paragraphs.Select(p => p.Text));
    }

    [Theory]
    // The \deff font is applied to all the text, the heading's included.
    [InlineData(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Courier New;}}{\stylesheet{\s0 Normal;}{\s1 heading 1;}}\pard\s1 H one\par}", "Courier New")]
    // No font table to name one: Writer's Heading font.
    [InlineData(@"{\rtf1\ansi{\stylesheet{\s0 Normal;}{\s1 heading 1;}}\pard\s1 H one\par}", "Liberation Sans")]
    // Even over a font of the style's own, which soffice 24.2 draws in
    // Liberation Mono too: \deff is direct formatting.
    [InlineData(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Courier New;}{\f1 Arial;}}{\stylesheet{\s0 Normal;}{\s1\f1 heading 1;}}\pard\s1 H one\par}", "Courier New")]
    public void A_Heading_Style_Without_A_Font_Takes_The_Default_Font(string rtf, string family)
    {
        RichTextDocument document = Read(rtf);
        InlineStyle run = document.Paragraphs[0].Runs[0].Style;

        Assert.Equal(family, document.StyleDefaults.FontFamilyOf(run));
        Assert.Equal(14, document.StyleDefaults.FontSizeOf(run), 3);
    }
}
