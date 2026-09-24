using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers where a picture stands among the paragraphs of a page read column by
/// column: in the column it was drawn in, by height within that column.
/// </summary>
/// <remarks>
/// A voucher prompted this. Its right-hand panel is headed by a logo, and the
/// page is read a column at a time, so a picture ordered by its top edge alone
/// met the left-hand column's heading first and came out above it - a column
/// away from the panel it heads.
/// </remarks>
public sealed class PdfPicturePlacementTests
{
    [Fact]
    public void A_Picture_Heading_The_Second_Column_Is_Read_With_That_Column()
    {
        RichTextDocument document = Read(TwoColumns(picture: "q 100 0 0 40 320 720 cm /Im0 Do Q\n"));

        Assert.Equal(["left", "picture", "right"], Kinds(document));
    }

    [Fact]
    public void A_Picture_Below_A_Columns_Text_Closes_That_Column()
    {
        // Drawn under the left-hand column's last line, while the right-hand
        // column's lines run on. By height alone nothing on the page stood
        // below it, so it waited for the end and came out after the other
        // column.
        RichTextDocument document = Read(TwoColumns(picture: "q 100 0 0 40 72 500 cm /Im0 Do Q\n"));

        Assert.Equal(["left", "picture", "right"], Kinds(document));
    }

    [Fact]
    public void A_Picture_Across_The_Gutter_Is_Placed_By_Height_Alone()
    {
        // A banner over both columns belongs to neither, and stands where its
        // top edge puts it among all the page's lines: first.
        RichTextDocument document = Read(TwoColumns(picture: "q 440 0 0 40 72 720 cm /Im0 Do Q\n"));

        Assert.Equal(["picture", "left", "right"], Kinds(document));
    }

    [Fact]
    public void A_Picture_Inside_A_Column_Is_Placed_By_Height_Within_It()
    {
        // Between the second column's two paragraphs, which it separates.
        var content = new StringBuilder(TwoColumns(picture: string.Empty, rightLines: 5));
        for (int i = 0; i < 5; i++)
            content.Append(PdfFileBuilder.ShowText(Line("Right after", i), x: 320, y: 560 - (i * 14), size: 10));
        content.Append("q 100 0 0 40 320 585 cm /Im0 Do Q\n");

        RichTextDocument document = Read(content.ToString());

        Assert.Equal(["left", "right", "picture", "right"], Kinds(document));
    }

    /// <summary>
    /// Two columns of ten lines each, set level with each other on either side
    /// of a wide gutter, and <paramref name="picture"/> drawn after them.
    /// </summary>
    private static string TwoColumns(string picture, int rightLines = 10)
    {
        var content = new StringBuilder();
        for (int i = 0; i < 10; i++)
            content.Append(PdfFileBuilder.ShowText(Line("Left", i), x: 72, y: 700 - (i * 14), size: 10));
        for (int i = 0; i < rightLines; i++)
            content.Append(PdfFileBuilder.ShowText(Line("Right", i), x: 320, y: 700 - (i * 14), size: 10));

        return content.Append(picture).ToString();
    }

    private static string Line(string column, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{column} line {number}");

    /// <summary>What each paragraph is, in order: a picture, or text from the left or right column.</summary>
    private static string[] Kinds(RichTextDocument document) =>
        [.. document.Paragraphs
            .Where(paragraph => paragraph.Length > 0)
            .Select(paragraph =>
                paragraph.Runs.Any(run => run.Style.Image is not null) ? "picture" :
                paragraph.Text.StartsWith("Left", StringComparison.Ordinal) ? "left" :
                "right")];

    private static RichTextDocument Read(string content)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int image = builder.AddStream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", [0x80]);
        int stream = builder.AddStream(string.Empty, content);

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im0 {image} 0 R >> >> /Contents {stream} 0 R >>");

        using var input = new MemoryStream(builder.Build(catalog));
        return new PdfDocumentCodec().ReadPdf(input, null).Document;
    }
}
