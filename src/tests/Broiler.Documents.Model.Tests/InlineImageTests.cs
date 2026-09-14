namespace Broiler.Documents.Model.Tests;

/// <summary>
/// How an image behaves as a document character: it occupies one position, edits
/// like any other character, and never merges with a different picture.
/// </summary>
public sealed class InlineImageTests
{
    private static readonly byte[] Bytes = [1, 2, 3, 4];

    private static InlineImage Image(string? altText = null) =>
        new(Bytes, "image/png", 40, 20, altText);

    [Fact]
    public void An_Image_Run_Occupies_Exactly_One_Character()
    {
        InlineImage image = Image();
        RichTextParagraph paragraph = RichTextParagraph.Create("ab", InlineStyle.Default)
            .InsertText(1, InlineImage.PlaceholderText, InlineStyle.Default with { Image = image });

        Assert.Equal("a" + InlineImage.PlaceholderText + "b", paragraph.Text);
        Assert.Equal(3, paragraph.Length);
        Assert.Equal(3, paragraph.Runs.Count);
        Assert.Same(image, paragraph.Runs[1].Style.Image);
        Assert.Equal(1, paragraph.Runs[1].Length);
    }

    [Fact]
    public void Two_Different_Images_Stay_In_Separate_Runs()
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty
            .InsertText(0, InlineImage.PlaceholderText, InlineStyle.Default with { Image = Image() })
            .InsertText(1, InlineImage.PlaceholderText, InlineStyle.Default with { Image = Image() });

        // The two images hold identical bytes; identity is what keeps them apart,
        // so run normalization does not silently fuse two pictures into one run.
        Assert.Equal(2, paragraph.Runs.Count);
    }

    [Fact]
    public void Deleting_The_Image_Character_Removes_The_Image()
    {
        RichTextParagraph paragraph = RichTextParagraph.Create("ab", InlineStyle.Default)
            .InsertText(1, InlineImage.PlaceholderText, InlineStyle.Default with { Image = Image() })
            .RemoveRange(1, 1);

        Assert.Equal("ab", paragraph.Text);
        Assert.DoesNotContain(paragraph.Runs, run => run.Style.IsImage);
    }

    [Fact]
    public void Clearing_Formatting_Over_An_Image_Keeps_The_Image()
    {
        InlineImage image = Image();
        RichTextParagraph paragraph = RichTextParagraph.Create(
                InlineImage.PlaceholderText,
                InlineStyle.Default with { Image = image, Bold = true })
            .ApplyInlineStyle(0, 1, InlineStyleDelta.Clear);

        StyleRun run = Assert.Single(paragraph.Runs);
        Assert.Same(image, run.Style.Image);
        Assert.False(run.Style.Bold);
    }

    [Fact]
    public void Slicing_A_Range_Carries_The_Image_With_It()
    {
        InlineImage image = Image();
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("ab", InlineStyle.Default)
                .InsertText(1, InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }),
        ]);

        RichTextDocument slice = document.Slice(
            new RichTextRange(new RichTextPosition(0, 1), new RichTextPosition(0, 2)));

        StyleRun run = Assert.Single(Assert.Single(slice.Paragraphs).Runs);
        Assert.Same(image, run.Style.Image);
    }

    [Fact]
    public void An_Image_Reports_Whether_Its_Size_Was_Stated()
    {
        Assert.True(Image().HasExplicitSize);
        Assert.False(new InlineImage(Bytes, "image/png", 0, 0).HasExplicitSize);
    }

    [Fact]
    public void Resizing_Keeps_The_Bytes_And_The_Description()
    {
        InlineImage resized = Image("a logo").WithSize(80, 40);

        Assert.Equal(80, resized.Width);
        Assert.Equal(40, resized.Height);
        Assert.Equal("a logo", resized.AltText);
        Assert.Equal(Bytes, resized.Data.ToArray());
    }

    [Fact]
    public void An_Image_Needs_A_Content_Type_And_A_Non_Negative_Size()
    {
        Assert.Throws<ArgumentException>(() => new InlineImage(Bytes, " ", 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InlineImage(Bytes, "image/png", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InlineImage(Bytes, "image/png", 1, -1));
    }
}
