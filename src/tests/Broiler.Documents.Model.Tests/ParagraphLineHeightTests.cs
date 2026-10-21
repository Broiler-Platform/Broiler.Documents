namespace Broiler.Documents.Model.Tests;

/// <summary>
/// A paragraph's lines follow their text, or a height it states: exactly, or
/// at least.
/// </summary>
public sealed class ParagraphLineHeightTests
{
    [Fact]
    public void A_Proportional_Line_Is_Its_Text_Times_The_Multiplier()
    {
        Assert.Equal(30, (ParagraphStyle.Default with { LineSpacing = 2f }).LineBoxHeight(15), 6);
    }

    [Fact]
    public void An_Exact_Line_Is_Its_Height_Whatever_The_Text()
    {
        ParagraphStyle style = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10f };

        Assert.Equal(10, style.LineBoxHeight(15), 6);
        Assert.Equal(10, style.LineBoxHeight(5), 6);
    }

    [Fact]
    public void An_At_Least_Line_Is_The_Taller_Of_The_Two()
    {
        ParagraphStyle style = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.AtLeast, LineHeight = 10f };

        Assert.Equal(15, style.LineBoxHeight(15), 6);
        Assert.Equal(10, style.LineBoxHeight(5), 6);
    }

    [Fact]
    public void An_Exact_Rule_With_No_Height_Falls_Back_To_The_Text()
    {
        Assert.Equal(15, (ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact }).LineBoxHeight(15), 6);
    }

    [Fact]
    public void A_Multiplier_Alone_In_A_Delta_Makes_The_Lines_Proportional_Again()
    {
        ParagraphStyle exact = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10f };

        ParagraphStyle changed = new ParagraphStyleDelta { LineSpacing = 2f }.Apply(exact);

        Assert.Equal(LineHeightRule.Proportional, changed.LineHeightRule);
        Assert.Equal(0f, changed.LineHeight);
        Assert.Equal(2f, changed.LineSpacing);
    }

    [Fact]
    public void A_Delta_That_Leaves_Spacing_Alone_Keeps_The_Height()
    {
        ParagraphStyle exact = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10f };

        ParagraphStyle changed = ParagraphStyleDelta.WithAlignment(TextAlignment.Center).Apply(exact);

        Assert.Equal(LineHeightRule.Exact, changed.LineHeightRule);
        Assert.Equal(10f, changed.LineHeight);
    }

    [Fact]
    public void A_Delta_Can_Set_A_Height()
    {
        ParagraphStyle changed = new ParagraphStyleDelta
        {
            LineHeightRule = LineHeightRule.AtLeast,
            LineHeight = 14f,
        }.Apply(ParagraphStyle.Default with { LineSpacing = 2f });

        Assert.Equal(LineHeightRule.AtLeast, changed.LineHeightRule);
        Assert.Equal(14f, changed.LineHeight);
    }
}
