using Broiler.Graphics;

namespace Broiler.Documents.FormatCodes.Tests;

public sealed class FormatCodeEditIntentTests
{
    [Fact(Timeout = 600000)]
    public void Projected_Codes_Carry_Typed_Removal_Semantics()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("x", new InlineStyle { Bold = true })]);

        FormatCodeToken token = new FormatCodeProjector().Project(document).Tokens[0];

        Assert.Equal(FormatCodeProperty.Bold, token.EditDescriptor?.Property);
        ApplyFormatCodeInlineIntent intent = Assert.IsType<ApplyFormatCodeInlineIntent>(
            token.EditDescriptor?.RemovalIntent);
        Assert.False(intent.Delta.Bold);
        Assert.True(token.EditCapabilities.HasFlag(FormatCodeEditCapabilities.Remove));
    }

    [Theory]
    [InlineData("https://example.test", true)]
    [InlineData("mailto:test@example.test", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("relative/path", false)]
    public void Link_Validation_Uses_The_Safe_Scheme_Policy(string href, bool expected)
    {
        RichTextDocument document = RichTextDocument.FromPlainText("link");
        var intent = new ApplyFormatCodeInlineIntent(
            new RichTextRange(document.Start, document.End),
            InlineStyleDelta.WithLink(href));

        Assert.Equal(expected, FormatCodeEditValidator.Validate(document, intent).IsValid);
    }

    [Fact(Timeout = 600000)]
    public void Invalid_Metrics_And_Document_Limits_Are_Rejected()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("x");
        var size = new ApplyFormatCodeInlineIntent(
            new RichTextRange(document.Start, document.End),
            InlineStyleDelta.WithFontSize(float.PositiveInfinity));
        var text = new ReplaceFormatCodeTextIntent(
            RichTextRange.Caret(document.End),
            "too long");

        Assert.Equal("FCEDIT021", FormatCodeEditValidator.Validate(document, size).ErrorCode);
        Assert.Equal("FCEDIT010", FormatCodeEditValidator.Validate(
            document, text, new FormatCodeEditLimits { MaxInsertedCharacters = 2 }).ErrorCode);
    }

    [Fact(Timeout = 600000)]
    public void Insert_Palette_Produces_Typed_Color_And_Structure_Intents()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("x");
        RichTextRange range = RichTextRange.Caret(document.End);

        var color = Assert.IsType<ApplyFormatCodeInlineIntent>(
            FormatCodeInsertPalette.Create(
                FormatCodePaletteEntry.Foreground,
                range,
                BColor.FromArgb(255, 1, 2, 3)));
        var paragraph = Assert.IsType<ReplaceFormatCodeTextIntent>(
            FormatCodeInsertPalette.Create(FormatCodePaletteEntry.ParagraphBreak, range));

        Assert.NotNull(color.Delta.Foreground);
        Assert.Equal("\n", paragraph.Text);
    }
}
