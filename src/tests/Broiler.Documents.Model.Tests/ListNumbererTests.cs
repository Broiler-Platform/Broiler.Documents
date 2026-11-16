namespace Broiler.Documents.Model.Tests;

public sealed class ListNumbererTests
{
    [Theory]
    [InlineData(ListNumberFormat.CardinalText, 21, "Twenty-one")]
    [InlineData(ListNumberFormat.OrdinalText, 20, "Twentieth")]
    [InlineData(ListNumberFormat.OrdinalText, 121, "One hundred twenty-first")]
    [InlineData(ListNumberFormat.Ordinal, 112, "112th")]
    [InlineData(ListNumberFormat.Ordinal, 123, "123rd")]
    [InlineData(ListNumberFormat.Chicago, 7, "‡‡")]
    [InlineData(ListNumberFormat.IdeographLegalTraditional, 10021, "壹萬零貳拾壹")]
    [InlineData(ListNumberFormat.OldHungarian, 205, "\U00010CFA\U00010CFA\U00010CFE\U00010CFB")]
    public void Formats_Compound_Numbers(ListNumberFormat format, int number, string label) =>
        Assert.Equal(label, ListNumberer.Format(number, format));

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
    // LibreOffice 24.2's ARABIC_ZERO3 to ARABIC_ZERO5, CIRCLE_NUMBER and
    // NUMBER_HEBREW, as its text export writes them for cut-down copies of
    // arabic-zero3-numbering.docx, numbering-circle.docx and FDO74106.docx.
    [InlineData(ListNumberFormat.DecimalZero3, 1, "001")]
    [InlineData(ListNumberFormat.DecimalZero3, 1234, "1234")]
    [InlineData(ListNumberFormat.DecimalZero4, 12, "0012")]
    [InlineData(ListNumberFormat.DecimalZero5, 1, "00001")]
    [InlineData(ListNumberFormat.CircledNumber, 0, "0")]
    [InlineData(ListNumberFormat.CircledNumber, 1, "①")]
    [InlineData(ListNumberFormat.CircledNumber, 20, "⑳")]
    [InlineData(ListNumberFormat.CircledNumber, 35, "㉟")]
    [InlineData(ListNumberFormat.CircledNumber, 50, "㊿")]
    [InlineData(ListNumberFormat.CircledNumber, 51, "51")]
    [InlineData(ListNumberFormat.HebrewNumber, 1, "א")]
    [InlineData(ListNumberFormat.HebrewNumber, 11, "יא")]
    [InlineData(ListNumberFormat.HebrewNumber, 15, "טו")]
    [InlineData(ListNumberFormat.HebrewNumber, 16, "טז")]
    [InlineData(ListNumberFormat.HebrewNumber, 120, "קכ")]
    [InlineData(ListNumberFormat.HebrewNumber, 499, "תצט")]
    [InlineData(ListNumberFormat.HebrewNumber, 500, "תק")]
    public void Each_Format_Writes_Its_Number(ListNumberFormat format, int value, string expected) =>
        Assert.Equal(expected, ListNumberer.Format(value, format));

    [Fact]
    public void A_Legal_Level_Keeps_A_Zero_Padded_Level_Padded()
    {
        // listWithLgl.docx: a decimalZero level under isLgl is "Sect 1.01" in
        // LibreOffice 24.2, and an upper-Roman chapter turned decimalZero
        // reads "01.01".
        var definition = new ListDefinition(
        [
            new ListLevel { Template = "%1", Format = ListNumberFormat.UpperRoman },
            new ListLevel { Template = "%1.%2", Format = ListNumberFormat.DecimalZero, Legal = true },
        ]);
        var numberer = new ListNumberer();

        Assert.Equal(["I", "1.01"], Labels(numberer, At(definition, 0), At(definition, 1)));
    }

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

    /// <summary>
    /// The labels LibreOffice 24.2 draws for items 1 to 60 of an RTF list in each
    /// of these formats (its <c>levelnfc</c> 12, 13, 16, 18, 20, 21, 24, 25 and
    /// 38), exported with <c>soffice --convert-to txt</c>.
    /// </summary>
    [Theory]
    [InlineData(ListNumberFormat.AiueoHalfWidth, "ｱ ｲ ｳ ｴ ｵ ｶ ｷ ｸ ｹ ｺ ｻ ｼ ｽ ｾ ｿ ﾀ ﾁ ﾂ ﾃ ﾄ ﾅ ﾆ ﾇ ﾈ ﾉ ﾊ ﾋ ﾌ ﾍ ﾎ ﾏ ﾐ ﾑ ﾒ ﾓ ﾔ ﾕ ﾖ ﾗ ﾘ ﾙ ﾚ ﾛ ﾜ ｦ ﾝ ｱ ｲ ｳ ｴ ｵ ｶ ｷ ｸ ｹ ｺ ｻ ｼ ｽ ｾ")]
    [InlineData(ListNumberFormat.IrohaHalfWidth, "ｲ ﾛ ﾊ ﾆ ﾎ ﾍ ﾄ ﾁ ﾘ ﾇ ﾙ ｦ ﾜ ｶ ﾖ ﾀ ﾚ ｿ ﾂ ﾈ ﾅ ﾗ ﾑ ｳ ヰ ﾉ ｵ ｸ ﾔ ﾏ ｹ ﾌ ｺ ｴ ﾃ ｱ ｻ ｷ ﾕ ﾒ ﾐ ｼ ヱ ﾋ ﾓ ｾ ｽ ﾝ ｲ ﾛ ﾊ ﾆ ﾎ ﾍ ﾄ ﾁ ﾘ ﾇ ﾙ ｦ")]
    [InlineData(ListNumberFormat.JapaneseLegal, "壱 弐 参 四 伍 六 七 八 九 壱零 壱壱 壱弐 壱参 壱四 壱伍 壱六 壱七 壱八 壱九 弐零 弐壱 弐弐 弐参 弐四 弐伍 弐六 弐七 弐八 弐九 参零 参壱 参弐 参参 参四 参伍 参六 参七 参八 参九 四零 四壱 四弐 四参 四四 四伍 四六 四七 四八 四九 伍零 伍壱 伍弐 伍参 伍四 伍伍 伍六 伍七 伍八 伍九 六零")]
    [InlineData(ListNumberFormat.CircledNumber, "① ② ③ ④ ⑤ ⑥ ⑦ ⑧ ⑨ ⑩ ⑪ ⑫ ⑬ ⑭ ⑮ ⑯ ⑰ ⑱ ⑲ ⑳ ㉑ ㉒ ㉓ ㉔ ㉕ ㉖ ㉗ ㉘ ㉙ ㉚ ㉛ ㉜ ㉝ ㉞ ㉟ ㊱ ㊲ ㊳ ㊴ ㊵ ㊶ ㊷ ㊸ ㊹ ㊺ ㊻ ㊼ ㊽ ㊾ ㊿ 51 52 53 54 55 56 57 58 59 60")]
    [InlineData(ListNumberFormat.AiueoFullWidth, "ア イ ウ エ オ カ キ ク ケ コ サ シ ス セ ソ タ チ ツ テ ト ナ ニ ヌ ネ ノ ハ ヒ フ ヘ ホ マ ミ ム メ モ ヤ ユ ヨ ラ リ ル レ ロ ワ ヲ ン ア イ ウ エ オ カ キ ク ケ コ サ シ ス セ")]
    [InlineData(ListNumberFormat.IrohaFullWidth, "イ ロ ハ ニ ホ ヘ ト チ リ ヌ ル ヲ ワ カ ヨ タ レ ソ ツ ネ ナ ラ ム ウ ヰ ノ オ ク ヤ マ ケ フ コ エ テ ア サ キ ユ メ ミ シ ヱ ヒ モ セ ス ン イ ロ ハ ニ ホ ヘ ト チ リ ヌ ル ヲ")]
    [InlineData(ListNumberFormat.HangulSyllable, "가 나 다 라 마 바 사 아 자 차 카 타 파 하 가 나 다 라 마 바 사 아 자 차 카 타 파 하 가 나 다 라 마 바 사 아 자 차 카 타 파 하 가 나 다 라 마 바 사 아 자 차 카 타 파 하 가 나 다 라")]
    [InlineData(ListNumberFormat.HangulJamo, "ㄱ ㄴ ㄷ ㄹ ㅁ ㅂ ㅅ ㅇ ㅈ ㅊ ㅋ ㅌ ㅍ ㅎ ㄱ ㄴ ㄷ ㄹ ㅁ ㅂ ㅅ ㅇ ㅈ ㅊ ㅋ ㅌ ㅍ ㅎ ㄱ ㄴ ㄷ ㄹ ㅁ ㅂ ㅅ ㅇ ㅈ ㅊ ㅋ ㅌ ㅍ ㅎ ㄱ ㄴ ㄷ ㄹ ㅁ ㅂ ㅅ ㅇ ㅈ ㅊ ㅋ ㅌ ㅍ ㅎ ㄱ ㄴ ㄷ ㄹ")]
    [InlineData(ListNumberFormat.ChineseLegalSimplified, "壹 贰 叁 肆 伍 陆 柒 捌 玖 壹拾 壹拾壹 壹拾贰 壹拾叁 壹拾肆 壹拾伍 壹拾陆 壹拾柒 壹拾捌 壹拾玖 贰拾 贰拾壹 贰拾贰 贰拾叁 贰拾肆 贰拾伍 贰拾陆 贰拾柒 贰拾捌 贰拾玖 叁拾 叁拾壹 叁拾贰 叁拾叁 叁拾肆 叁拾伍 叁拾陆 叁拾柒 叁拾捌 叁拾玖 肆拾 肆拾壹 肆拾贰 肆拾叁 肆拾肆 肆拾伍 肆拾陆 肆拾柒 肆拾捌 肆拾玖 伍拾 伍拾壹 伍拾贰 伍拾叁 伍拾肆 伍拾伍 伍拾陆 伍拾柒 伍拾捌 伍拾玖 陆拾")]
    public void East_Asian_Formats_Count_As_LibreOffice_Does(ListNumberFormat format, string expected)
    {
        Assert.Equal(expected, string.Join(' ', Enumerable.Range(1, 60).Select(value => ListNumberer.Format(value, format))));
    }
}
