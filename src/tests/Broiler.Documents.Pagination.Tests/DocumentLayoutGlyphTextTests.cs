namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Characters a word processor draws differently from how they are stored: a
/// soft hyphen, nothing inside a line and a hyphen where the line breaks at
/// it, and text set in the Symbol font, drawn as the characters it stands for.
/// </summary>
/// <remarks>
/// <c>wDateValueFormat.docx</c>'s table headers break "Reinraum&#173;klasse"
/// at its soft hyphen and write "≥ 0,5µm" as U+F0B3 in Symbol; LibreOffice
/// shows "Reinraum-" over "klasse" and a "≥".
/// </remarks>
public sealed class DocumentLayoutGlyphTextTests
{
    private static readonly string SoftHyphen = ((char)0x00AD).ToString();

    private static LayoutPage Layout(string text, double width = 468, string? family = null, DocumentStyleDefaults? defaults = null)
    {
        using var images = new ImageStore();
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(text, InlineStyle.Default with { FontSize = 12f, FontFamily = family }),
        ]).WithStyleDefaults(defaults ?? DocumentStyleDefaults.Default);
        double margin = (612 - width) / 2;
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(612, 792, 72, margin, 72, margin)).Pages[0];
    }

    private static string LineText(LayoutLine line) => string.Concat(line.Pieces.Select(piece => piece.Text));

    [Fact]
    public void A_Soft_Hyphen_Inside_A_Line_Takes_No_Room_And_Draws_Nothing()
    {
        LayoutLine plain = Assert.Single(Layout("Reinraumklasse end").Lines);
        LayoutLine hyphenated = Assert.Single(Layout("Reinraum" + SoftHyphen + "klasse end").Lines);

        Assert.Equal("Reinraumklasse end", LineText(hyphenated));
        // The halves are measured apart, which can round a hundredth of a
        // point away from the whole word; a hyphen would be points wide.
        Assert.Equal(plain.Pieces[^1].X, hyphenated.Pieces[^1].X, 0.05);
    }

    [Fact]
    public void A_Line_That_Breaks_At_A_Soft_Hyphen_Shows_A_Hyphen()
    {
        LayoutPage page = Layout("Reinraum" + SoftHyphen + "klasse", width: 60);

        Assert.Equal(2, page.Lines.Count);
        Assert.Equal("Reinraum-", LineText(page.Lines[0]));
        Assert.Equal("klasse", LineText(page.Lines[1]));
    }

    [Fact]
    public void Text_Set_In_Symbol_Is_Drawn_As_The_Characters_It_Stands_For()
    {
        LayoutLine line = Assert.Single(Layout(((char)0xF0B3).ToString() + "a", family: "Symbol").Lines);

        Assert.Equal("≥α", LineText(line));
        Assert.All(line.Pieces, piece => Assert.NotEqual("Symbol", piece.FontFamily));
    }

    [Fact]
    public void Another_Face_Keeps_Its_Private_Use_Text()
    {
        string text = ((char)0xF0B3).ToString() + "a";

        Assert.Equal(text, LineText(Assert.Single(Layout(text, family: "Wingdings").Lines)));
    }
}
