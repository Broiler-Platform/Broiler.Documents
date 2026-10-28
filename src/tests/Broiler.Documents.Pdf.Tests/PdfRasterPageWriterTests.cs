using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Documents.Pdf.Writing;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Paging;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// The spool writer print paths hand to CUPS and browsers: one image per page, every object
/// where the cross-reference table says it is, and pages this component's own reader opens.
/// </summary>
public sealed partial class PdfRasterPageWriterTests
{
    private static BPage Page(BSize size, BColor fill)
    {
        var builder = new BPageBuilder(size);
        builder.Content.FillRect(new BRect(0, 0, size.Width / 2, size.Height / 2), fill);
        return builder.Build();
    }

    private static byte[] Write(IEnumerable<BPage> pages, double dpi = 36, string title = "Spool")
    {
        using var output = new NonSeekableStream();
        new PdfRasterPageWriter(dpi, bandHeight: 7).Write(pages, title, output);
        return output.ToArray();
    }

    [Fact]
    public void The_Reader_Opens_What_The_Writer_Spools_With_Every_Page()
    {
        byte[] pdf = Write([Page(BPaperSize.A4.Size, BColor.Red), Page(BPaperSize.Letter.Oriented(BPageOrientation.Landscape), BColor.Blue)]);

        using var stream = new MemoryStream(pdf);
        PdfReadResult result = new PdfDocumentCodec().ReadPdf(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.PageCount);
        Assert.NotNull(result.Document.PageGeometry);
        Assert.Equal(BPaperSize.A4.Width, result.Document.PageGeometry!.Width, 2);
        Assert.Equal(BPaperSize.A4.Height, result.Document.PageGeometry.Height, 2);
    }

    [Fact]
    public void Every_Cross_Reference_Entry_Points_At_Its_Object()
    {
        byte[] pdf = Write([Page(new BSize(100, 50), BColor.Red), Page(new BSize(60, 60), BColor.Blue)]);
        string text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4\n%", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);

        int startxref = int.Parse(StartXref().Match(text).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref\n", text[startxref..], StringComparison.Ordinal);

        MatchCollection entries = XrefEntry().Matches(text[startxref..]);
        Assert.True(entries.Count >= 10, $"Expected every object in the table, found {entries.Count}.");
        for (int number = 1; number <= entries.Count; number++)
        {
            int offset = int.Parse(entries[number - 1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.StartsWith($"{number} 0 obj\n", text[offset..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_Page_Image_Holds_The_Pixels_At_The_Resolution_Asked_For()
    {
        // 72 x 36 pt at 36 dpi is 36 x 18 pixels; the left half of the top half is red.
        byte[] pdf = Write([Page(new BSize(72, 36), BColor.Red)], dpi: 36);
        string text = Encoding.Latin1.GetString(pdf);

        Match image = ImageObject().Match(text);
        Assert.True(image.Success, "No image XObject was written.");
        Assert.Equal("36", image.Groups["w"].Value);
        Assert.Equal("18", image.Groups["h"].Value);

        int lengthObject = int.Parse(image.Groups["len"].Value, System.Globalization.CultureInfo.InvariantCulture);
        int length = int.Parse(Regex.Match(text, $@"\n{lengthObject} 0 obj\n(\d+)\nendobj").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        int dataStart = image.Index + image.Length;

        using var compressed = new MemoryStream(pdf, dataStart, length);
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var pixels = new MemoryStream();
        inflater.CopyTo(pixels);
        byte[] rgb = pixels.ToArray();

        Assert.Equal(36 * 18 * 3, rgb.Length);
        Assert.Equal(new byte[] { 255, 0, 0 }, rgb[..3]);
        int bottomRight = ((17 * 36) + 35) * 3;
        Assert.Equal(new byte[] { 255, 255, 255 }, rgb[bottomRight..(bottomRight + 3)]);
        Assert.Contains("/MediaBox [0 0 72 36]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Title_Survives_Any_Character()
    {
        byte[] pdf = Write([Page(new BSize(10, 10), BColor.Red)], title: "Brief (Ä) \\ ✓");
        string text = Encoding.Latin1.GetString(pdf);

        Assert.Contains("/Title <FEFF004200720069006500660020002800C400290020005C00202713>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_To_Print_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => Write([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfRasterPageWriter(dpi: 0));
        Assert.Equal("application/pdf", new PdfRasterPageWriter().MediaType);
    }

    [GeneratedRegex(@"startxref\n(\d+)\n%%EOF")]
    private static partial Regex StartXref();

    [GeneratedRegex(@"(\d{10}) 00000 n \n")]
    private static partial Regex XrefEntry();

    [GeneratedRegex(@"/Subtype /Image /Width (?<w>\d+) /Height (?<h>\d+) [^\n]*/Length (?<len>\d+) 0 R >>\nstream\n")]
    private static partial Regex ImageObject();

    /// <summary>A spooler's pipe: the writer must not seek or ask for a length.</summary>
    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();

        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }
    }
}
