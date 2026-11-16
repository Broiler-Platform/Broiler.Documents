namespace Broiler.Documents.Model.Tests;

/// <summary>
/// Office conformance issue 249, ODT text: the numbering types LibreOffice's
/// numbering provider writes (i18npool's defaultnumberingprovider.cxx and
/// nativenumbersupplier.cxx, 24.2), and its rules for a level that writes no
/// number and for a legal level (SwNumRule::MakeNumString,
/// SvxNumberType::GetNumStr).
/// </summary>
public sealed class ListNumbererIssue249Tests
{
    [Theory]
    [InlineData(ListNumberFormat.DecimalZero3, 1, "001")]
    [InlineData(ListNumberFormat.DecimalZero3, 1000, "1000")]
    [InlineData(ListNumberFormat.DecimalZero4, 12, "0012")]
    [InlineData(ListNumberFormat.DecimalZero5, 1, "00001")]
    [InlineData(ListNumberFormat.CircledNumber, 1, "①")]
    [InlineData(ListNumberFormat.CircledNumber, 21, "㉑")]
    [InlineData(ListNumberFormat.CircledNumber, 50, "㊿")]
    [InlineData(ListNumberFormat.CircledNumber, 51, "51")]
    [InlineData(ListNumberFormat.HangulCircledJamo, 1, "㉠")]
    [InlineData(ListNumberFormat.HangulCircledJamo, 15, "㉠")]
    [InlineData(ListNumberFormat.HangulCircledSyllable, 2, "㉯")]
    [InlineData(ListNumberFormat.GreekUpper, 1, "Α")]
    [InlineData(ListNumberFormat.GreekUpper, 6, "ΣΤ")]
    [InlineData(ListNumberFormat.GreekLower, 24, "κδ")]
    [InlineData(ListNumberFormat.GreekLower, 1001, "͵αα")]
    [InlineData(ListNumberFormat.Thai, 3, "ค")]
    [InlineData(ListNumberFormat.Persian, 3, "پ")]
    [InlineData(ListNumberFormat.HebrewNumber, 1, "א")]
    [InlineData(ListNumberFormat.HebrewNumber, 11, "יא")]
    [InlineData(ListNumberFormat.HebrewNumber, 15, "טו")]
    [InlineData(ListNumberFormat.HebrewNumber, 16, "טז")]
    [InlineData(ListNumberFormat.HebrewNumber, 1000, "אלף")]
    [InlineData(ListNumberFormat.CyrillicUpperBulgarian, 1, "А")]
    [InlineData(ListNumberFormat.CyrillicUpperBulgarian, 29, "Аа")]
    [InlineData(ListNumberFormat.CyrillicUpperBulgarian, 30, "Аб")]
    [InlineData(ListNumberFormat.CyrillicUpperBulgarianRepeated, 30, "Бб")]
    [InlineData(ListNumberFormat.CyrillicLowerRussian, 30, "аа")]
    [InlineData(ListNumberFormat.CyrillicLowerRussianRepeated, 31, "бб")]
    [InlineData(ListNumberFormat.CyrillicUpperSerbian, 6, "Ђ")]
    [InlineData(ListNumberFormat.CyrillicLowerUkrainian, 5, "ґ")]
    public void Writes_LibreOffices_Numbering_Types(ListNumberFormat format, int number, string label) =>
        Assert.Equal(label, ListNumberer.Format(number, format));

    [Fact]
    public void A_Level_Above_That_Writes_No_Number_Is_Left_Out_With_Its_Separator()
    {
        // tdf146257.odt: "%1%.%2%.%3%" with the second level unnumbered reads
        // "1.1", not "1..1".
        var definition = new ListDefinition(
        [
            new ListLevel { Template = "%1" },
            new ListLevel { Template = "%1.%2", Format = ListNumberFormat.None },
            new ListLevel { Template = "%1.%2.%3" },
        ]);
        var numberer = new ListNumberer();

        Assert.Equal(["1", null, "1.1"], Labels(numberer, At(definition, 0), At(definition, 1), At(definition, 2)));
    }

    [Fact]
    public void A_Level_That_Writes_No_Number_Keeps_Only_The_Text_Outside_Its_Marks()
    {
        // tdf156105_percentSuffix.odt: "(%)%1%.%2%[%]" with its own level
        // unnumbered keeps what is before the first "%" and after the last.
        var definition = new ListDefinition(
        [
            new ListLevel { Template = "(%)%1[%]" },
            new ListLevel { Template = "(%)%1.%2[%]", Format = ListNumberFormat.None },
        ]);
        var numberer = new ListNumberer();

        Assert.Equal(["(%)1[%]", "(]"], Labels(numberer, At(definition, 0), At(definition, 1)));
    }

    [Fact]
    public void A_Legal_Level_Keeps_Arabic_Numbers_With_Leading_Zeros()
    {
        // IsLegal.fodt: "Sect %1%.%2%" over levels I and 01 reads "Sect 1.01".
        var definition = new ListDefinition(
        [
            new ListLevel { Template = "CH %1", Format = ListNumberFormat.UpperRoman },
            new ListLevel { Template = "Sect %1.%2", Format = ListNumberFormat.DecimalZero, Legal = true },
        ]);
        var numberer = new ListNumberer();

        Assert.Equal(["CH I", "Sect 1.01"], Labels(numberer, At(definition, 0), At(definition, 1)));
    }

    private static ParagraphStyle At(ListDefinition definition, int level)
    {
        var reference = new ListReference(definition, level);
        return ParagraphStyle.Default with { List = reference, ListKind = reference.Kind };
    }

    private static List<string?> Labels(ListNumberer numberer, params ParagraphStyle[] styles) =>
        [.. styles.Select(numberer.Advance)];
}
