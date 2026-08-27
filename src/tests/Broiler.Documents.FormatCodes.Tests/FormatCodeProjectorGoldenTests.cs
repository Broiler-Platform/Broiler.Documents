using Broiler.Graphics;

namespace Broiler.Documents.FormatCodes.Tests;

public sealed class FormatCodeProjectorGoldenTests
{
    private readonly FormatCodeProjector _projector = new();

    [Fact(Timeout = 600000)]
    public void Canonical_User_Example_Is_Exact()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("Hello World!", new InlineStyle { Bold = true })]);

        FormatCodeProjection projection = _projector.Project(document);

        Assert.Equal(1, projection.GrammarVersion);
        Assert.Equal("[Bold ON]Hello World![Bold OFF]", projection.Text);
    }

    [Fact(Timeout = 600000)]
    public void Changed_Inline_Properties_Close_Reverse_Then_Open_Forward()
    {
        RichTextParagraph paragraph = RichTextParagraph
            .Create("a", new InlineStyle { Bold = true, FontFamily = "A" })
            .Append(RichTextParagraph.Create("b", new InlineStyle { Italic = true, FontFamily = "B" }));

        FormatCodeProjection projection = _projector.Project(
            RichTextDocument.FromParagraphs([paragraph]));

        Assert.Equal(
            "[Bold ON][Font \"A\"]a" +
            "[Font DEFAULT][Bold OFF][Italic ON][Font \"B\"]b" +
            "[Font DEFAULT][Italic OFF]",
            projection.Text);
    }

    [Fact(Timeout = 600000)]
    public void Every_Inline_Field_Uses_The_Frozen_Vocabulary_And_Order()
    {
        var style = new InlineStyle
        {
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            FontFamily = "A\"[B]\\C",
            FontSize = 17f,
            Foreground = new BColor(0x11, 0x22, 0x33, 0x44),
            Background = new BColor(0xFF, 0xF2, 0xA8, 0xFF),
            LinkHref = "https://example.test/[x]",
            Capitalization = TextCapitalization.SmallCaps,
        };

        FormatCodeProjection projection = _projector.Project(
            RichTextDocument.FromParagraphs([RichTextParagraph.Create("x", style)]));

        Assert.Equal(
            "[Bold ON][Italic ON][Underline ON][Strike ON]" +
            "[Font \"A\\\"\\[B\\]\\\\C\"][Size 17]" +
            "[Text Color #11223344][Highlight #FFF2A8FF]" +
            "[Link \"https://example.test/\\[x\\]\"][Small Caps ON]x" +
            "[Caps OFF][Link OFF][Highlight NONE][Text Color DEFAULT][Size DEFAULT]" +
            "[Font DEFAULT][Strike OFF][Underline OFF][Italic OFF][Bold OFF]",
            projection.Text);
    }

    [Fact(Timeout = 600000)]
    public void Capitalization_Projects_Its_Kind_And_Closes_As_One_Property()
    {
        RichTextParagraph paragraph = RichTextParagraph
            .Create("a", new InlineStyle { Capitalization = TextCapitalization.AllCaps })
            .Append(RichTextParagraph.Create("b", new InlineStyle { Capitalization = TextCapitalization.SmallCaps }));

        FormatCodeProjection projection = _projector.Project(
            RichTextDocument.FromParagraphs([paragraph]));

        Assert.Equal(
            "[All Caps ON]a[Caps OFF][Small Caps ON]b[Caps OFF]",
            projection.Text);
    }

    [Fact(Timeout = 600000)]
    public void Paragraph_Properties_Precede_Empty_Paragraph_Structure()
    {
        ParagraphStyle style = ParagraphStyle.Default with
        {
            Alignment = TextAlignment.Center,
            ListKind = ListKind.Bullet,
            IndentLevel = 2,
            LineSpacing = 1.5f,
            SpacingBefore = 8f,
            SpacingAfter = 9f,
        };

        FormatCodeProjection projection = _projector.Project(
            RichTextDocument.FromParagraphs(
                [RichTextParagraph.Create(string.Empty, InlineStyle.Default, style)]));

        Assert.Equal(
            "[Align CENTER][List BULLET][Indent 2][Line Spacing 1.5]" +
            "[Space Before 8][Space After 9][Empty Paragraph]",
            projection.Text);
        Assert.All(
            projection.Tokens.Take(projection.Tokens.Count - 1),
            token => Assert.Equal(FormatCodeTokenKind.ParagraphCode, token.Kind));
        Assert.Equal(FormatCodeTokenKind.StructureCode, projection.Tokens[^1].Kind);
    }

    [Fact(Timeout = 600000)]
    public void Content_Syntax_And_Nonprinting_Characters_Cannot_Be_Commands()
    {
        string content = "A" + "\\" + "[" + "]" + "\t" + "\u2028" + "\u0001" + "\u200E" + "\u2029" + "😀";

        FormatCodeProjection projection = _projector.Project(RichTextDocument.FromPlainText(content));

        Assert.Equal(
            "A" + "\\\\" + "\\[" + "\\]" + "[Tab]" + "[Line Break]" +
            "\\u{0001}" + "\\u{200E}" + "\\u{2029}" + "😀",
            projection.Text);
        Assert.Contains(projection.Tokens, token => token.Kind == FormatCodeTokenKind.Escape);
        Assert.Contains(projection.Tokens, token => token.DisplayText == "[Tab]");
        Assert.Contains(projection.Tokens, token => token.DisplayText == "[Line Break]");
    }

    [Fact(Timeout = 600000)]
    public void An_Embedded_Image_Projects_As_An_Image_Structure_Code()
    {
        var image = new InlineImage(new byte[] { 1, 2, 3 }, "image/png", 40, 20, "a logo");
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("a", InlineStyle.Default)
                .InsertText(1, InlineImage.PlaceholderText, InlineStyle.Default with { Image = image })
                .InsertText(2, "b", InlineStyle.Default),
        ]);

        FormatCodeProjection projection = _projector.Project(document);

        Assert.Equal("a[Image]b", projection.Text);
        FormatCodeToken token = Assert.Single(
            projection.Tokens.Where(t => t.DisplayText == "[Image]"));
        Assert.Equal(FormatCodeTokenKind.StructureCode, token.Kind);
        Assert.Equal(FormatCodeProperty.Image, token.EditDescriptor?.Property);
    }

    [Fact(Timeout = 600000)]
    public void Paragraphs_Reset_Inline_State_And_Use_One_Canonical_Boundary()
    {
        InlineStyle bold = new() { Bold = true };
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("a", bold),
            RichTextParagraph.Create("b", bold),
        ]);

        Assert.Equal(
            "[Bold ON]a[Bold OFF][Paragraph Break]\n[Bold ON]b[Bold OFF]",
            _projector.Project(document).Text);
    }

    [Fact(Timeout = 600000)]
    public void Canonical_Tokens_Concatenate_Exactly_To_Text()
    {
        FormatCodeProjection projection = _projector.Project(
            RichTextDocument.FromPlainText("one\ntwo\t[three]"));

        Assert.Equal(projection.Text, string.Concat(projection.Tokens.Select(token => token.DisplayText)));
        Assert.All(projection.Tokens, token => Assert.Equal(token.DisplayText.Length, token.ProjectedLength));
    }
}
