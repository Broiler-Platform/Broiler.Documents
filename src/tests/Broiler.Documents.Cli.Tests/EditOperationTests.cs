using Broiler.Documents.Cli.Documents;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Tests;

/// <summary>The edit grammar, exercised against the model directly.</summary>
public sealed class EditOperationTests
{
    private static RichTextDocument Apply(string text, params string[] operations) =>
        EditOperations.Apply(RichTextDocument.FromPlainText(text), operations);

    [Fact]
    public void Append_Adds_A_Paragraph_At_The_End()
    {
        RichTextDocument document = Apply("one", "append:two");

        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal("two", document.Paragraphs[1].Text);
    }

    [Fact]
    public void A_Text_Tail_Keeps_Its_Colons()
    {
        // The reason text-tail verbs exist: "12:30" is ordinary prose, and having
        // to escape it would make the common case the awkward one.
        RichTextDocument document = Apply("one", "append:meeting at 12:30 sharp");

        Assert.Equal("meeting at 12:30 sharp", document.Paragraphs[1].Text);
    }

    [Fact]
    public void An_Escaped_Colon_Is_Literal_In_A_Structural_Field()
    {
        RichTextDocument document = Apply("a:b and more", "replace:a\\:b:X");

        Assert.Equal("X and more", document.PlainText);
    }

    [Fact]
    public void Insert_Places_A_Paragraph_Before_The_Index()
    {
        RichTextDocument document = Apply("one\ntwo", "insert:1:middle");

        Assert.Equal(new[] { "one", "middle", "two" }, document.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void Delete_Accepts_A_Range()
    {
        RichTextDocument document = Apply("a\nb\nc\nd", "delete:1-2");

        Assert.Equal(new[] { "a", "d" }, document.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void Deleting_Every_Paragraph_Leaves_The_Empty_Document_The_Model_Requires()
    {
        RichTextDocument document = Apply("a\nb", "delete:*");

        Assert.Equal(1, document.ParagraphCount);
        Assert.Equal(string.Empty, document.PlainText);
    }

    [Fact]
    public void Inline_Applies_To_The_Character_Range_Only()
    {
        RichTextDocument document = Apply("hello world", "inline:0:0-5:bold=on");
        RichTextParagraph paragraph = document.Paragraphs[0];

        Assert.True(paragraph.StyleAt(0).Bold);
        Assert.True(paragraph.StyleAt(4).Bold);
        Assert.False(paragraph.StyleAt(6).Bold);
    }

    [Fact]
    public void A_Dollar_Means_The_End_Of_The_Paragraph()
    {
        RichTextDocument document = Apply("hello world", "inline:0:6-$:italic=on");

        Assert.False(document.Paragraphs[0].StyleAt(0).Italic);
        Assert.True(document.Paragraphs[0].StyleAt(10).Italic);
    }

    [Fact]
    public void A_Star_Means_The_Whole_Range()
    {
        RichTextDocument document = Apply("a\nb\nc", "para:*:align=center");

        Assert.All(document.Paragraphs, p => Assert.Equal(TextAlignment.Center, p.Style.Alignment));
    }

    [Fact]
    public void Colors_Accept_Hex_And_Css_Names()
    {
        RichTextDocument document = Apply(
            "hello",
            "inline:0:*:color=#FF0000,highlight=yellow");

        InlineStyle style = document.Paragraphs[0].StyleAt(0);
        Assert.Equal(new BColor(0xFF, 0x00, 0x00), style.Foreground);
        Assert.Equal(new BColor(0xFF, 0xFF, 0x00), style.Background);
    }

    [Fact]
    public void Replace_Keeps_The_Style_At_Each_Hit()
    {
        RichTextDocument document = Apply(
            "keep DRAFT keep",
            "inline:0:5-10:bold=on",
            "replace:DRAFT:FINAL");

        Assert.Equal("keep FINAL keep", document.PlainText);
        Assert.True(document.Paragraphs[0].StyleAt(5).Bold);
        Assert.False(document.Paragraphs[0].StyleAt(0).Bold);
    }

    [Fact]
    public void Replace_Terminates_When_The_Replacement_Contains_The_Search()
    {
        RichTextDocument document = Apply("aaa", "replace:a:aa");

        Assert.Equal("aaaaaa", document.PlainText);
    }

    [Fact]
    public void An_Out_Of_Range_Paragraph_Is_An_Error_Rather_Than_A_Clamp()
    {
        // Clamping would style paragraph 1 and report success for work the
        // script did not ask for.
        UsageException exception = Assert.Throws<UsageException>(() => Apply("a\nb", "para:9:align=right"));

        Assert.Contains("out of range", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_Unknown_Property_Names_The_Ones_That_Exist()
    {
        UsageException exception = Assert.Throws<UsageException>(() => Apply("a", "inline:0:*:blod=on"));

        Assert.Contains("bold", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_Failing_Operation_Names_Its_Position_And_Text()
    {
        UsageException exception = Assert.Throws<UsageException>(
            () => Apply("a", "para:0:align=center", "para:0:align=sideways"));

        Assert.Contains("Operation 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sideways", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_And_Merge_Are_Inverses()
    {
        RichTextDocument document = Apply("hello world", "split:0:5", "merge:0");

        Assert.Equal(1, document.ParagraphCount);
        Assert.Equal("hello world", document.PlainText);
    }

    [Fact]
    public void Clear_Removes_Inline_Formatting()
    {
        RichTextDocument document = Apply(
            "hello",
            "inline:0:*:bold=on,italic=on,color=red",
            "clear:0:*");

        InlineStyle style = document.Paragraphs[0].StyleAt(0);
        Assert.False(style.Bold);
        Assert.False(style.Italic);
        Assert.True(style.Foreground.IsEmpty);
    }

    [Fact]
    public void A_Quoted_Property_Value_May_Contain_A_Comma()
    {
        RichTextDocument document = Apply("hello", "inline:0:*:font=\"Times New Roman, serif\",size=14");

        InlineStyle style = document.Paragraphs[0].StyleAt(0);
        Assert.Equal("Times New Roman, serif", style.FontFamily);
        Assert.Equal(14f, style.FontSize);
    }

    [Fact]
    public void Escapes_Produce_Newlines_And_Tabs()
    {
        RichTextDocument document = Apply("start", "append:one\\ntwo");

        Assert.Equal(3, document.ParagraphCount);
        Assert.Equal("two", document.Paragraphs[2].Text);
    }

    [Fact]
    public void A_Backslash_Before_An_Ordinary_Letter_Keeps_Both_Characters()
    {
        // A rule that swallowed every backslash would quietly corrupt any prose
        // containing one. Both characters survive whenever the second is not one
        // of the escapes - here \U, \p, and \d are all left alone.
        RichTextDocument document = Apply("x", @"append:C:\Users\proof\docs");

        Assert.Equal(@"C:\Users\proof\docs", document.Paragraphs[1].Text);
    }

    [Fact]
    public void A_Tab_Escape_Still_Works_In_Prose()
    {
        RichTextDocument document = Apply("x", @"append:before\tafter");

        Assert.Equal("before\tafter", document.Paragraphs[1].Text);
    }

    [Fact]
    public void A_Doubled_Backslash_Produces_One()
    {
        RichTextDocument document = Apply("x", @"append:one\\two");

        Assert.Equal(@"one\two", document.Paragraphs[1].Text);
    }

    [Fact]
    public void An_Image_Path_Survives_Directories_Named_Temp_Or_New()
    {
        // Both would be mangled if the image props field were unescaped: \t is a
        // tab and \n is a newline. The field is taken literally for exactly this
        // reason, and the error message proves the path arrived intact.
        DocumentIoException exception = Assert.Throws<DocumentIoException>(
            () => Apply("x", @"image:0:$:file=C:\temp\new\0logo.png"));

        Assert.Contains(@"C:\temp\new\0logo.png", exception.Message, StringComparison.Ordinal);
    }

    // PROPS is the last field of `inline`, so it takes the rest of the line the
    // way every other text tail does. Read as one colon-delimited field it could
    // not carry a URL at all: `link=https://example.org/` arrived as
    // `link=https`, which is a relative target, which every writer refuses - so
    // the tool emitted a plausible link diagnostic about a value nobody wrote.
    // These assert the stored href rather than a writer's verdict, because the
    // verdict was the part that looked right while the value was wrong.

    [Theory]
    [InlineData("https://example.org/")]
    [InlineData("https://example.org/path?q=1&r=2#frag")]
    [InlineData("mailto:someone@example.org")]
    [InlineData("HTTPS://EXAMPLE.ORG/")]
    public void A_Link_Keeps_Every_Colon_In_Its_Url(string href)
    {
        RichTextDocument document = Apply("link here", "inline:0:0-4:link=" + href);

        Assert.Equal(href, document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void A_Link_Url_May_Be_Written_With_The_Documented_Colon_Escape()
    {
        // Escaping is unnecessary now that PROPS is a tail, but the grammar
        // documents `\:` and a caller who escapes defensively must not be
        // punished for it.
        RichTextDocument document = Apply("link here", @"inline:0:0-4:link=https\://example.org/");

        Assert.Equal("https://example.org/", document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void A_Quoted_Link_Url_Loses_Its_Quotes_And_Keeps_Its_Commas()
    {
        RichTextDocument document = Apply(
            "link here", "inline:0:0-4:link=\"https://example.org/a,b\",bold=on");

        Assert.Equal("https://example.org/a,b", document.Paragraphs[0].StyleAt(0).LinkHref);
        Assert.True(document.Paragraphs[0].StyleAt(0).Bold);
    }

    [Fact]
    public void A_Relative_Link_Target_Reaches_The_Model_Unchanged()
    {
        // Whether a writer will emit it is the writer's decision, and a
        // different one per format. The grammar's job is to carry what was
        // written, so that the writer refuses the caller's value rather than a
        // truncation of it.
        RichTextDocument document = Apply("link here", "inline:0:0-4:link=/docs/page");

        Assert.Equal("/docs/page", document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void A_Refused_Scheme_Reaches_The_Model_Whole()
    {
        // The edit language does not police schemes - the codecs do, per format,
        // and they need the real value to police. Storing `javascript` here
        // would make every writer refuse it as a relative target and report the
        // right diagnostic for the wrong reason.
        RichTextDocument document = Apply("link here", "inline:0:0-4:link=javascript:alert(1)");

        Assert.Equal("javascript:alert(1)", document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void A_Link_Sits_Alongside_Other_Properties_Whichever_Order_They_Come_In()
    {
        RichTextDocument left = Apply("link here", "inline:0:0-4:bold=on,link=https://example.org/");
        RichTextDocument right = Apply("link here", "inline:0:0-4:link=https://example.org/,bold=on");

        foreach (RichTextDocument document in new[] { left, right })
        {
            Assert.Equal("https://example.org/", document.Paragraphs[0].StyleAt(0).LinkHref);
            Assert.True(document.Paragraphs[0].StyleAt(0).Bold);
        }
    }

    [Fact]
    public void Link_Off_Still_Removes_The_Link()
    {
        RichTextDocument document = Apply(
            "link here",
            "inline:0:0-4:link=https://example.org/",
            "inline:0:0-4:link=off");

        Assert.Null(document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void A_Paragraph_Props_Field_Is_A_Tail_Too()
    {
        // `para` has no property that carries a colon today. It is a tail
        // anyway, because the grammar is one grammar and a caller should not
        // have to remember which verb the rule holds for.
        RichTextDocument document = Apply("one", "para:0:align=center");

        Assert.Equal(TextAlignment.Center, document.Paragraphs[0].Style.Alignment);
    }

    [Fact]
    public void A_Missing_Props_Field_Is_Still_Named_In_The_Error()
    {
        UsageException failure = Assert.Throws<UsageException>(
            () => Apply("one", "inline:0:*"));

        Assert.Contains("PROPS", failure.Message, StringComparison.Ordinal);
    }
}
