using Broiler.Graphics;

namespace Broiler.Documents.FormatCodes.Tests;

/// <summary>
/// The host's Insert Code palette: every entry it declares can be created.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FormatCodePaletteEntry.PageBreakBefore"/> was declared in the
/// public enum and had no arm in the switch, so it fell to the default and threw
/// "The palette entry requires a typed value" - for an entry that takes no
/// value. It was the only one of the twenty-five that did, and nothing caught it
/// because nothing tested this type at all.
/// </para>
/// <para>
/// So the first test here is exhaustive over the enum rather than a case per
/// entry. A case per entry tests the entries somebody remembered; iterating the
/// declaration tests the ones they did not, which is the failure this class
/// exists for. An entry added later with no arm fails it, and one added with a
/// typed value fails it too until <see cref="ValueFor"/> names that value -
/// which is the right prompt, because a palette entry nobody can call is not
/// finished.
/// </para>
/// </remarks>
public sealed class FormatCodeInsertPaletteTests
{
    private static RichTextRange Caret()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("text");
        return RichTextRange.Caret(document.Start);
    }

    /// <summary>The typed value an entry needs, or null for the ones that take none.</summary>
    private static object? ValueFor(FormatCodePaletteEntry entry) => entry switch
    {
        FormatCodePaletteEntry.FontFamily => "Arial",
        FormatCodePaletteEntry.FontSize => 12f,
        FormatCodePaletteEntry.Foreground => BColor.Black,
        FormatCodePaletteEntry.Background => BColor.White,
        FormatCodePaletteEntry.Link => "https://example.test",
        FormatCodePaletteEntry.Indent => 1,
        FormatCodePaletteEntry.LineSpacing => 1.5f,
        FormatCodePaletteEntry.SpacingBefore => 6f,
        FormatCodePaletteEntry.SpacingAfter => 6f,
        _ => null,
    };

    [Fact(Timeout = 600000)]
    public void Every_Declared_Entry_Can_Be_Created()
    {
        RichTextRange range = Caret();

        var refused = new List<string>();
        foreach (FormatCodePaletteEntry entry in Enum.GetValues<FormatCodePaletteEntry>())
        {
            try
            {
                Assert.NotNull(FormatCodeInsertPalette.Create(entry, range, ValueFor(entry)));
            }
            catch (ArgumentException e)
            {
                refused.Add(entry + ": " + e.Message);
            }
        }

        Assert.True(refused.Count == 0, "the palette refused " + string.Join("; ", refused));
    }

    [Fact(Timeout = 600000)]
    public void A_Page_Break_Is_A_Paragraph_Intent_That_Takes_No_Value()
    {
        ApplyFormatCodeParagraphIntent intent = Assert.IsType<ApplyFormatCodeParagraphIntent>(
            FormatCodeInsertPalette.Create(FormatCodePaletteEntry.PageBreakBefore, Caret()));

        Assert.True(intent.Delta.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void Inserting_A_Page_Break_Mirrors_The_Removal_The_Projector_Offers()
    {
        // The two halves of one code. The projector draws [Page Break] on a
        // paragraph that states one and hands back a delta clearing it; the
        // palette is what states it in the first place. While this arm was
        // missing a caller could delete a page break and never insert one.
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(
                "x", InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = true })]);

        FormatCodeToken drawn = Assert.Single(
            new FormatCodeProjector().Project(document).Tokens,
            token => token.DisplayText == "[Page Break]");

        var removal = Assert.IsType<ApplyFormatCodeParagraphIntent>(drawn.EditDescriptor?.RemovalIntent);
        var insertion = Assert.IsType<ApplyFormatCodeParagraphIntent>(
            FormatCodeInsertPalette.Create(FormatCodePaletteEntry.PageBreakBefore, Caret()));

        Assert.Equal(FormatCodeProperty.PageBreakBefore, drawn.EditDescriptor?.Property);
        Assert.False(removal.Delta.PageBreakBefore);
        Assert.True(insertion.Delta.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void An_Inserted_Page_Break_Passes_Validation()
    {
        // A created intent that the validator then refuses would be no better
        // than the throw this replaced.
        RichTextDocument document = RichTextDocument.FromPlainText("text");
        FormatCodeEditIntent intent = FormatCodeInsertPalette.Create(
            FormatCodePaletteEntry.PageBreakBefore,
            RichTextRange.Caret(document.Start));

        Assert.True(FormatCodeEditValidator.Validate(document, intent).IsValid);
    }

    [Fact(Timeout = 600000)]
    public void An_Entry_Needing_A_Value_Still_Refuses_One_Without()
    {
        // The default arm is not dead: it is what an entry that genuinely takes a
        // value hits when the caller supplies none, and its message is true there.
        Assert.Throws<ArgumentException>(
            () => FormatCodeInsertPalette.Create(FormatCodePaletteEntry.FontSize, Caret()));
    }
}
