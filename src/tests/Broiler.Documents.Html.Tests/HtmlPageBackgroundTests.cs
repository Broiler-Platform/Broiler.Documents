using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// The page colour an HTML document states on its <c>&lt;body&gt;</c> or root,
/// which CSS paints over the whole canvas.
/// </summary>
/// <remarks>
/// Found by the ranking of LibreOffice's own test documents: <c>fdo86857.html</c>
/// is a red page that LibreOffice drew red and this codec drew white.
/// </remarks>
public sealed class HtmlPageBackgroundTests
{
    private static readonly BColor Red = BColor.FromArgb(255, 0, 0);

    [Theory]
    [InlineData("<body bgcolor=\"#ff0000\">")]
    [InlineData("<body style=\"background: #ff0000\">")]
    [InlineData("<body style=\"background-color: red\">")]
    [InlineData("<body bgcolor=\"#0000ff\" style=\"background: url(x.png) #ff0000 repeat\">")]
    public void The_Body_Colour_Is_The_Page_Background(string body)
    {
        RichTextDocument document = Read("<html>" + body + "<p>text</p></body></html>");

        Assert.Equal(ShapeFill.Solid(Red), document.PageBackground);
    }

    [Fact]
    public void A_Stylesheet_Rule_For_The_Body_Counts()
    {
        RichTextDocument document = Read(
            "<html><head><style>body { background-color: #ff0000 }</style></head><body><p>text</p></body></html>");

        Assert.Equal(ShapeFill.Solid(Red), document.PageBackground);
    }

    [Fact]
    public void The_Root_Background_Wins_Over_The_Body()
    {
        // CSS 2.1 §14.2: the canvas takes the root's background, and the
        // body's only where the root has none.
        RichTextDocument document = Read(
            "<html style=\"background: #ff0000\"><body bgcolor=\"#0000ff\"><p>text</p></body></html>");

        Assert.Equal(ShapeFill.Solid(Red), document.PageBackground);
    }

    [Fact]
    public void The_Page_Colour_Does_Not_Highlight_The_Text()
    {
        RichTextDocument document = Read("<html><body bgcolor=\"#ff0000\"><p>text</p></body></html>");

        Assert.All(document.Paragraphs[0].Runs, run => Assert.True(run.Style.Background.IsEmpty));
    }

    [Theory]
    [InlineData("<body>")]
    [InlineData("<body style=\"background: transparent\">")]
    public void A_Page_With_No_Colour_Has_No_Background(string body)
    {
        Assert.Null(Read("<html>" + body + "<p>text</p></body></html>").PageBackground);
    }

    [Fact]
    public void A_Background_Is_Written_On_The_Body_And_Read_Back()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("text").WithPageBackground(ShapeFill.Solid(Red));

        Assert.Equal(ShapeFill.Solid(Red), Read(Write(source)).PageBackground);
    }

    [Fact]
    public void A_Gradient_Is_Written_As_Its_Middle_Colour_And_Reported()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("text")
            .WithPageBackground(new ShapeFill(BColor.FromArgb(0, 0, 0), BColor.FromArgb(200, 100, 50), 0));

        using var stream = new MemoryStream();
        DocumentWriteResult result = new HtmlDocumentCodec().Write(source, stream);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.background.gradient");
        Assert.Equal(
            ShapeFill.Solid(BColor.FromArgb(100, 50, 25)),
            Read(Encoding.UTF8.GetString(stream.ToArray())).PageBackground);
    }

    private static RichTextDocument Read(string html)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return new HtmlDocumentCodec().Read(stream).Document;
    }

    private static string Write(RichTextDocument document) =>
        Encoding.UTF8.GetString(HtmlWriter.WriteToArray(document));
}
