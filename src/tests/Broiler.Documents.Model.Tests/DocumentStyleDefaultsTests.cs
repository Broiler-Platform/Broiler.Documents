namespace Broiler.Documents.Model.Tests;

/// <summary>
/// The document's answer for a run that states no size and no family, and the
/// difference between the two halves of that answer (PDF roadmap §6.4).
/// </summary>
public sealed class DocumentStyleDefaultsTests
{
    [Fact]
    public void A_Document_That_States_Nothing_Still_Has_A_Size()
    {
        // Twelve points is a real default, not a placeholder. Before this each
        // consumer picked its own — the CLI renderer 11, the PDF writer 12 — and
        // a document rendered one way and written the other changed size.
        Assert.Equal(12f, DocumentStyleDefaults.Default.FontSizePoints);
        Assert.Equal(12f, DocumentStyleDefaults.Default.FontSizeOf(InlineStyle.Default));
        Assert.True(DocumentStyleDefaults.Default.IsDefault);
    }

    [Fact]
    public void A_Document_That_States_Nothing_Has_No_Family()
    {
        // There is no neutral typeface, so absent is the honest answer and the
        // caller decides what to do about it.
        Assert.Null(DocumentStyleDefaults.Default.FontFamily);
        Assert.Null(DocumentStyleDefaults.Default.FontFamilyOf(InlineStyle.Default));
    }

    [Fact]
    public void A_Run_That_States_Its_Own_Keeps_It()
    {
        var defaults = new DocumentStyleDefaults { FontSizePoints = 10f, FontFamily = "Georgia" };
        var style = new InlineStyle { FontSize = 18f, FontFamily = "Courier New" };

        Assert.Equal(18f, defaults.FontSizeOf(style));
        Assert.Equal("Courier New", defaults.FontFamilyOf(style));
    }

    [Fact]
    public void A_Run_That_States_Nothing_Inherits_The_Documents()
    {
        var defaults = new DocumentStyleDefaults { FontSizePoints = 10f, FontFamily = "Georgia" };

        Assert.Equal(10f, defaults.FontSizeOf(InlineStyle.Default));
        Assert.Equal("Georgia", defaults.FontFamilyOf(InlineStyle.Default));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-12f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_Size_That_Is_Not_A_Size_Is_Treated_As_Absent(float stated)
    {
        // Zero-point text is not something an author asks for, and a NaN would
        // propagate into every measurement downstream of it.
        var defaults = new DocumentStyleDefaults { FontSizePoints = 10f };

        Assert.Equal(10f, defaults.FontSizeOf(new InlineStyle { FontSize = stated }));
    }

    [Fact]
    public void An_Empty_Family_Is_Treated_As_Absent()
    {
        var defaults = new DocumentStyleDefaults { FontFamily = "Georgia" };

        Assert.Equal("Georgia", defaults.FontFamilyOf(new InlineStyle { FontFamily = "" }));
    }

    [Fact]
    public void A_Document_Carries_Its_Defaults_Through_An_Edit()
    {
        // Editing the body is not a reason to lose the document's type, the same
        // way it is not a reason to lose its page or its letterhead.
        RichTextDocument document = RichTextDocument
            .FromPlainText("one")
            .WithStyleDefaults(new DocumentStyleDefaults { FontSizePoints = 9f, FontFamily = "Georgia" });

        RichTextDocument edited = document.ApplyInlineStyle(
            new RichTextRange(document.Start, document.End),
            new InlineStyleDelta { Bold = true });

        Assert.Equal(9f, edited.StyleDefaults.FontSizePoints);
        Assert.Equal("Georgia", edited.StyleDefaults.FontFamily);
    }

    [Fact]
    public void A_Document_Given_No_Defaults_Carries_The_Shared_Ones()
    {
        Assert.Same(DocumentStyleDefaults.Default, RichTextDocument.FromPlainText("body").StyleDefaults);
    }
}
