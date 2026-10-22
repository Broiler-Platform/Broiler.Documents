using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfExactIndentTests
{
    [Fact]
    public void A_List_Label_Hangs_Outside_Its_Body_Indent()
    {
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Create("one\u2028two", InlineStyle.Default,
            ParagraphStyle.Default with { ListKind = ListKind.Numbered, LeftIndent = 36, FirstLineIndent = -18 })])
            .WithPageGeometry(new PageGeometry(200, 200, 10, 10, 10, 10));
        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        string pdf = Encoding.Latin1.GetString(stream.ToArray());
        Assert.Equal(28, XOf(pdf, "1. "), 3);
        Assert.Equal(46, XOf(pdf, "one"), 3);
        Assert.Equal(46, XOf(pdf, "two"), 3);
    }

    [Theory]
    [InlineData(5.25f, 22.75)]
    [InlineData(-5.25f, 12.25)]
    public void Exact_First_Line_Offsets_Apply_Once_In_Body_And_Cells(float first, double expected)
    {
        var paragraph = RichTextParagraph.Create("one\u2028two", InlineStyle.Default,
            ParagraphStyle.Default with { LeftIndent = 7.5f, RightIndent = 9.5f, FirstLineIndent = first });
        foreach (bool inCell in new[] { false, true })
        {
            var document = RichTextDocument.FromParagraphs([paragraph])
                .WithPageGeometry(new PageGeometry(200, 200, 10, 10, 10, 10));
            if (inCell)
                document = document.WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], cellPadding: 0)]);
            using var stream = new MemoryStream();
            new PdfDocumentCodec().WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
            string pdf = Encoding.Latin1.GetString(stream.ToArray());
            Assert.Equal(expected, XOf(pdf, "one"), 3);
            Assert.Equal(17.5, XOf(pdf, "two"), 3);
        }
    }

    private static double XOf(string pdf, string text)
    {
        int position = pdf.IndexOf("(" + text + ")", StringComparison.Ordinal);
        Assert.True(position > 0);
        string prefix = pdf[..position];
        int tm = prefix.LastIndexOf(" Tm", StringComparison.Ordinal);
        return double.Parse(prefix[..tm].Split('\n')[^1].Split(' ')[^2], CultureInfo.InvariantCulture);
    }
}
