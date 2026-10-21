namespace Broiler.Documents.Pagination.Tests;

public sealed class LineMetricsTests
{
    [Fact]
    public void Supplied_Metrics_Control_Text_Tabs_And_Empty_Lines()
    {
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings
        {
            FontVerticalMetrics = _ => (8.0, 3.0),
        }, images);
        LayoutResult result = layout.Layout(RichTextDocument.FromPlainText("one\ttwo\u2028\u2028three"),
            DocumentPageBox.Default);
        Assert.Equal(3, result.Pages[0].Lines.Count);
        Assert.All(result.Pages[0].Lines, line =>
        {
            Assert.Equal(11, line.Height);
            Assert.Equal(8, line.Baseline);
            Assert.All(line.Pieces, piece =>
            {
                Assert.Equal(8, piece.Ascent);
                Assert.Equal(3, piece.Descent);
            });
        });
        Assert.Equal(22, result.Pages[0].Lines[2].Top - result.Pages[0].Lines[0].Top, 6);
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(8, -1)]
    [InlineData(0, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    public void Invalid_Supplied_Metrics_Use_The_Existing_Fallback(double ascent, double descent)
    {
        using var images = new ImageStore();
        RichTextDocument document = RichTextDocument.FromPlainText("one");
        LayoutLine expected = new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, DocumentPageBox.Default).Pages[0].Lines[0];
        LayoutLine actual = new DocumentLayout(new LayoutSettings
        {
            FontVerticalMetrics = _ => (ascent, descent),
        }, images).Layout(document, DocumentPageBox.Default).Pages[0].Lines[0];
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Baseline, actual.Baseline);
    }

    [Fact]
    public void Separate_Layouts_Keep_Their_Own_Metrics()
    {
        using var images = new ImageStore();
        RichTextDocument document = RichTextDocument.FromPlainText("one");
        var first = new DocumentLayout(new LayoutSettings { FontVerticalMetrics = _ => (8.0, 2.0) }, images);
        var second = new DocumentLayout(new LayoutSettings { FontVerticalMetrics = _ => (16.0, 4.0) }, images);
        Assert.Equal(10, first.Layout(document, DocumentPageBox.Default).Pages[0].Lines[0].Height);
        Assert.Equal(20, second.Layout(document, DocumentPageBox.Default).Pages[0].Lines[0].Height);
        Assert.Equal(10, first.Layout(document, DocumentPageBox.Default).Pages[0].Lines[0].Height);
    }

    [Fact]
    public void Supplied_Line_Gap_Advances_Lines_Without_Moving_The_First_Baseline()
    {
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings
        {
            FontVerticalMetrics = _ => (8.0, 2.0),
            FontLineGap = _ => 1.5,
        }, images);
        LayoutResult result = layout.Layout(RichTextDocument.FromPlainText("one\u2028two"), DocumentPageBox.Default);

        Assert.Equal(2, result.Pages[0].Lines.Count);
        Assert.All(result.Pages[0].Lines, line => Assert.Equal(11.5, line.Height, 6));
        Assert.All(result.Pages[0].Lines, line => Assert.Equal(8, line.Baseline, 6));
        Assert.Equal(11.5, result.Pages[0].Lines[1].Top - result.Pages[0].Lines[0].Top, 6);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_Supplied_Line_Gap_Uses_No_Extra_Leading(double gap)
    {
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings
        {
            FontVerticalMetrics = _ => (8.0, 2.0),
            FontLineGap = _ => gap,
        }, images);

        Assert.Equal(10, layout.Layout(RichTextDocument.FromPlainText("one"), DocumentPageBox.Default)
            .Pages[0].Lines[0].Height, 6);
    }

    [Fact]
    public void Explicitly_Sized_Text_Does_Not_Inherit_An_Unused_Default_Fonts_Height()
    {
        var document = RichTextDocument.FromPlainText("first\u2028second\u2028third");
        document = document.ApplyInlineStyle(new RichTextRange(RichTextDocument.Start, document.End),
            new InlineStyleDelta { SetFontSize = true, FontSize = 10 });
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings { DefaultFontSizePoints = 24 }, images)
            .Layout(document, DocumentPageBox.Default);
        Assert.Equal(3, result.Pages[0].Lines.Count);
        foreach (LayoutLine line in result.Pages[0].Lines)
        {
            double ascent = line.Pieces.Max(piece => piece.Ascent);
            double descent = line.Pieces.Max(piece => piece.Descent);
            Assert.Equal(ascent + descent, line.Height, 6);
            Assert.Equal(ascent, line.Baseline, 6);
        }
    }

    [Fact]
    public void Empty_Lines_Still_Use_Default_Font_Metrics()
    {
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings(), images);
        LayoutLine empty = layout.Layout(RichTextDocument.Empty, DocumentPageBox.Default).Pages[0].Lines[0];
        LayoutLine text = layout.Layout(RichTextDocument.FromPlainText("text"), DocumentPageBox.Default).Pages[0].Lines[0];
        Assert.Equal(text.Height, empty.Height, 6);
        Assert.Equal(text.Baseline, empty.Baseline, 6);
        Assert.True(empty.Height > 0);
    }

    [Fact]
    public void An_Empty_Paragraph_Is_As_Tall_As_Its_Mark()
    {
        // A blank 10-point line under a 12-point default is a 10-point line.
        // Measured at the default, tdf168017.docx's 36 blank lines pushed the
        // rest of its first page onto the second.
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings
        {
            DefaultFontSizePoints = 12,
            FontVerticalMetrics = font => (font.Size * 0.8, font.Size * 0.2),
        }, images);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [
                RichTextParagraph.Empty.WithMarkStyle(new InlineStyle { FontSize = 10f }),
                RichTextParagraph.Empty,
            ]);

        IReadOnlyList<LayoutLine> lines = layout.Layout(document, DocumentPageBox.Default).Pages[0].Lines;

        Assert.Equal(10, lines[0].Height, 6);
        Assert.Equal(8, lines[0].Baseline, 6);
        Assert.Equal(12, lines[1].Height, 6);
    }

    [Fact]
    public void A_Mark_Does_Not_Change_A_Line_That_Has_Text()
    {
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings
        {
            FontVerticalMetrics = font => (font.Size * 0.8, font.Size * 0.2),
        }, images);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("text", new InlineStyle { FontSize = 10f })
                .WithMarkStyle(new InlineStyle { FontSize = 30f })]);

        Assert.Equal(10, layout.Layout(document, DocumentPageBox.Default).Pages[0].Lines[0].Height, 6);
    }

    [Fact]
    public void Mixed_Sizes_And_Line_Spacing_Still_Reserve_Room_For_The_Largest_Run()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("small LARGE");
        RichTextPosition start = RichTextDocument.Start;
        for (int i = 0; i < 6; i++)
            start = document.PositionRightOf(start);
        document = document.ApplyInlineStyle(new RichTextRange(start, document.End),
            new InlineStyleDelta { SetFontSize = true, FontSize = 30 });
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings(), images);
        LayoutLine single = layout.Layout(document, DocumentPageBox.Default).Pages[0].Lines[0];
        RichTextDocument doubled = document.ApplyParagraphStyle(new RichTextRange(RichTextDocument.Start, document.End),
            new ParagraphStyleDelta { LineSpacing = 2 });
        LayoutLine doubleSpaced = layout.Layout(doubled, DocumentPageBox.Default).Pages[0].Lines[0];
        Assert.All(single.Pieces, piece => Assert.True(single.Height >= piece.Ascent + piece.Descent));
        Assert.Equal(single.Height * 2, doubleSpaced.Height, 6);
        Assert.Equal(single.Baseline, doubleSpaced.Baseline, 6);
    }
}
