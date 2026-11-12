using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Pagination.Fonts;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office issue #242, DOCX pages one side drew blank: a text box's last line
/// that fills it to the twip, a symbol character in a font there is no face
/// for, and a character a face has no glyph for.
/// </summary>
public sealed class DocumentLayoutIssue242BlankTests
{
    private static readonly DocumentPageBox Box = new(400, 300, 10, 10, 10, 10);

    private const string HostSans = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
    private const string HostSerif = "/usr/share/fonts/truetype/liberation/LiberationSerif-Regular.ttf";

    private static RichTextParagraph Line(string text) =>
        RichTextParagraph.Empty.InsertText(0, text, InlineStyle.Default);

    private static LayoutShape LayOut(double height)
    {
        var shape = new DocumentShape(0, 0, 0, 200, height, null, default, [Line("one"), Line("two")], offsetFromPage: true);
        var document = RichTextDocument.FromPlainText("").WithShapes([shape]);
        using var images = new ImageStore();
        return Assert.Single(Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document, Box).Pages).Shapes);
    }

    [Fact]
    public void A_Text_Box_Keeps_A_Line_That_Ends_Within_A_Twip_A_Line_Of_Its_Foot()
    {
        // test_msword_hang.docx: the picture line ends 0.05 points past the
        // frame here, and fills it to the twip in Writer, which rounds each
        // line's proportional height down to a whole twip.
        LayoutShape roomy = LayOut(200);
        double needed = roomy.Lines[^1].Top + roomy.Lines[^1].Height - roomy.Bounds.Top;

        Assert.Equal(2, LayOut(needed - 0.06).Lines.Count);
        Assert.Single(LayOut(needed - 0.5).Lines);
    }

    private static LayoutSettings Fonts(DocumentFontService service) =>
        new() { TextMeasurer = new PaginationFontAdapter(service) };

    [Fact]
    public void A_Symbol_Character_In_A_Font_With_No_Face_Is_Set_In_The_Serif_Face()
    {
        if (!File.Exists(HostSerif) || !File.Exists(HostSans))
            return;
        using var service = new DocumentFontService(HostSans);
        service.AddFontFile("serif", HostSerif);
        service.AddFontFile("DejaVu Sans", HostSans);
        var symbol = InlineStyle.Default with { FontFamily = "Wingdings", IsSymbolCharacter = true };
        var typed = InlineStyle.Default with { FontFamily = "Wingdings" };
        var mapped = InlineStyle.Default with { FontFamily = "DejaVu Sans", IsSymbolCharacter = true };
        var paragraph = RichTextParagraph.Empty.InsertText(0, "", symbol).InsertText(1, "", typed).InsertText(2, "", mapped);
        using var images = new ImageStore();

        LayoutPage page = Assert.Single(new DocumentLayout(Fonts(service), images)
            .Layout(RichTextDocument.FromParagraphs([paragraph]), Box).Pages);

        Assert.Equal(["serif", "Wingdings", "DejaVu Sans"], page.Lines[0].Pieces.Select(piece => piece.FontFamily));
    }

    private static IReadOnlyList<BRenderCommand> Paint(string family, string text, string file)
    {
        using var service = new DocumentFontService();
        service.AddFontFile(family, file);
        LayoutSettings settings = Fonts(service);
        var paragraph = RichTextParagraph.Empty.InsertText(0, text, InlineStyle.Default with { FontFamily = family, FontSize = 40 });
        using var images = new ImageStore();
        LayoutPage page = Assert.Single(new DocumentLayout(settings, images).Layout(RichTextDocument.FromParagraphs([paragraph]), Box).Pages);
        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(page, Box, list, _ => null);
        return list.Commands;
    }

    [Fact]
    public void A_Character_The_Face_Lacks_Is_Drawn_As_Its_Missing_Glyph()
    {
        if (!File.Exists(HostSans))
            return;

        IReadOnlyList<BRenderCommand> commands = Paint("DejaVu Sans", "AB", HostSans);

        // The text either side is drawn as text, the private-use character as
        // DejaVu Sans's missing-glyph box, filled as triangles, not as the
        // renderer's block question mark.
        Assert.Equal(["A", "B"], commands.OfType<BRenderCommand.DrawText>().Select(command => command.Text.Text));
        Assert.Contains(commands, command => command is BRenderCommand.FillTriangle);
    }

    [Fact]
    public void A_Face_Whose_Missing_Glyph_Is_Empty_Draws_Nothing_For_The_Character()
    {
        if (!File.Exists(HostSerif))
            return;

        IReadOnlyList<BRenderCommand> commands = Paint("Liberation Serif", "", HostSerif);

        Assert.DoesNotContain(commands, command => command is BRenderCommand.DrawText or BRenderCommand.FillTriangle);
    }

    [Fact]
    public void Contours_Are_Filled_By_The_Non_Zero_Rule()
    {
        // A square with a square hole wound the other way: the ring is filled,
        // the hole is not.
        var list = new BRenderList();
        BPoint[] outer = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        BPoint[] inner = [new(3, 3), new(3, 7), new(7, 7), new(7, 3)];
        DocumentPagePainter.FillContours(list, [outer, inner], BColor.Black);

        double area = list.Commands.OfType<BRenderCommand.FillTriangle>().Sum(triangle =>
            Math.Abs((triangle.B.X - triangle.A.X) * (triangle.C.Y - triangle.A.Y) - (triangle.C.X - triangle.A.X) * (triangle.B.Y - triangle.A.Y)) / 2);
        Assert.Equal(100 - 16, area, 6);
    }
}
