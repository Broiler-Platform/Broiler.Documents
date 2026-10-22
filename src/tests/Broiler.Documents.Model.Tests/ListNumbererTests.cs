namespace Broiler.Documents.Model.Tests;

public sealed class ListNumbererTests
{
    [Fact]
    public void A_Shared_Restart_Resets_The_Family_And_Its_Unused_Level_Starts()
    {
        var definition = new ListDefinition([new ListLevel { Template = "%1" }, new ListLevel { Template = "%1.%2" }]);
        var restart = definition.WithLevel(0, definition.LevelAt(0) with { Start = 7 })
            .WithLevel(1, definition.LevelAt(1) with { Start = 5 }).WithSharedCounterRestart(definition);
        var numberer = new ListNumberer();
        Assert.Equal("1", numberer.Advance(At(definition, 0)));
        Assert.Equal("7", numberer.Advance(At(restart, 0)));
        Assert.Equal("7.5", numberer.Advance(At(definition, 1)));
        Assert.Equal("8", numberer.Snapshot().Advance(At(definition, 0)));
        Assert.Equal("7.6", numberer.Advance(At(definition, 1)));
        Assert.Equal("8", numberer.ForNestedFlow().Advance(At(definition, 0)));
        Assert.Equal("9", numberer.Advance(At(restart, 0)));
    }

    private static readonly ListDefinition Outline = new(
    [
        new ListLevel { Template = "%1" },
        new ListLevel { Template = "%1.%2" },
        new ListLevel { Template = "%1.%2.%3" },
    ]);

    [Fact]
    public void A_List_Counts_On_Across_Paragraphs_That_Are_In_No_List()
    {
        var numberer = new ListNumberer();

        Assert.Equal("1", numberer.Advance(At(Outline, 0)));
        Assert.Null(numberer.Advance(ParagraphStyle.Default));
        Assert.Equal("2", numberer.Advance(At(Outline, 0)));
    }

    [Fact]
    public void A_Deeper_Level_Starts_Again_Under_Each_New_Item_Above_It()
    {
        var numberer = new ListNumberer();

        Assert.Equal(
            ["1", "1.1", "1.2", "2", "2.1", "2.1.1"],
            Labels(numberer, At(Outline, 0), At(Outline, 1), At(Outline, 1), At(Outline, 0), At(Outline, 1), At(Outline, 2)));
    }

    [Fact]
    public void A_Level_With_No_Item_Above_It_Yet_Counts_That_Level_As_Started()
    {
        var numberer = new ListNumberer();

        // LibreOffice's numbering of tdf142694-1.odt, which opens at level two.
        Assert.Equal(["1.1", "1.2", "2", "2.1"],
            Labels(numberer, At(Outline, 1), At(Outline, 1), At(Outline, 0), At(Outline, 1)));
    }

    [Fact]
    public void A_Level_That_Restarts_Only_After_A_Shallower_One_Counts_On_Past_Its_Parent()
    {
        var definition = new ListDefinition(
        [
            new ListLevel { Template = "%1." },
            new ListLevel { Template = "%2." },
            new ListLevel { Template = "%3)", Format = ListNumberFormat.LowerLetter, RestartAfter = 0 },
        ]);
        var numberer = new ListNumberer();

        Assert.Equal(["1.", "1.", "a)", "2.", "b)", "2.", "a)"],
            Labels(numberer, At(definition, 0), At(definition, 1), At(definition, 2), At(definition, 1), At(definition, 2), At(definition, 0), At(definition, 2)));
    }

    [Fact]
    public void Two_Definitions_Count_Apart_Even_When_They_Look_Alike()
    {
        ListDefinition copy = Outline.Copy();
        var numberer = new ListNumberer();

        Assert.Equal(["1", "1", "2"], Labels(numberer, At(Outline, 0), At(copy, 0), At(Outline, 0)));
    }

    [Fact]
    public void A_Restart_Sets_The_Count_For_The_Items_After_It()
    {
        var numberer = new ListNumberer();

        Assert.Equal(["1", "5", "6"],
            Labels(numberer, At(Outline, 0), At(Outline, 0) with { List = new ListReference(Outline, 0, 5) }, At(Outline, 0)));
    }

    [Theory]
    [InlineData(ListNumberFormat.Decimal, 4, "4")]
    [InlineData(ListNumberFormat.DecimalZero, 4, "04")]
    [InlineData(ListNumberFormat.LowerLetter, 28, "bb")]
    [InlineData(ListNumberFormat.UpperLetter, 3, "C")]
    [InlineData(ListNumberFormat.LowerRoman, 14, "xiv")]
    [InlineData(ListNumberFormat.UpperRoman, 1999, "MCMXCIX")]
    [InlineData(ListNumberFormat.None, 3, "")]
    public void Each_Format_Writes_Its_Number(ListNumberFormat format, int value, string expected) =>
        Assert.Equal(expected, ListNumberer.Format(value, format));

    [Fact]
    public void A_Legal_Level_Writes_The_Levels_Above_It_In_Decimal()
    {
        var definition = new ListDefinition(
        [
            new ListLevel { Template = "%1", Format = ListNumberFormat.UpperRoman },
            new ListLevel { Template = "%1.%2", Legal = true },
        ]);
        var numberer = new ListNumberer();

        Assert.Equal(["I", "1.1"], Labels(numberer, At(definition, 0), At(definition, 1)));
    }

    [Fact]
    public void A_List_Known_Only_By_Its_Kind_Is_Numbered_As_It_Always_Was()
    {
        var numberer = new ListNumberer();
        ParagraphStyle numbered = ParagraphStyle.Default with { ListKind = ListKind.Numbered, IndentLevel = 1 };
        ParagraphStyle nested = numbered with { IndentLevel = 2 };

        Assert.Equal(["1.", "a.", "2.", null, "1."],
            Labels(numberer, numbered, nested, numbered, ParagraphStyle.Default, numbered));
    }

    [Fact]
    public void A_Nested_Flow_Counts_On_In_The_Same_Lists()
    {
        var numberer = new ListNumberer();
        Assert.Equal("1", numberer.Advance(At(Outline, 0)));

        ListNumberer cell = numberer.ForNestedFlow();
        Assert.Equal("2", cell.Advance(At(Outline, 0)));
        Assert.Equal("3", numberer.Advance(At(Outline, 0)));
    }

    [Fact]
    public void A_List_Kind_Edit_Takes_The_Paragraph_Out_Of_Its_List()
    {
        ParagraphStyle style = At(Outline, 0);

        Assert.NotNull(ParagraphStyleDelta.WithListKind(ListKind.Numbered).Apply(style).List);
        Assert.Null(ParagraphStyleDelta.WithListKind(ListKind.Bullet).Apply(style).List);
        Assert.Null(ParagraphStyleDelta.WithListKind(ListKind.None).Apply(style).List);
    }

    private static ParagraphStyle At(ListDefinition definition, int level)
    {
        var reference = new ListReference(definition, level);
        return ParagraphStyle.Default with { List = reference, ListKind = reference.Kind };
    }

    private static List<string?> Labels(ListNumberer numberer, params ParagraphStyle[] styles) =>
        [.. styles.Select(numberer.Advance)];
}
