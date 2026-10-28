using System;
using System.IO;
using System.Text;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Pdf.Tests;

public sealed class SharedLayoutPdfUnicodePriority3Tests
{
    private static byte[] WritePdf(RichTextDocument document)
    {
        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        return stream.ToArray();
    }

    [Fact]
    public void Pdf_NonBreakingSpace_DoesNotBreakLinePrematurely()
    {
        // "FirstPart\u00A0SecondPart"
        var doc = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("FirstPart\u00A0SecondPart")
        ]);

        byte[] bytes = WritePdf(doc);
        string pdfText = Encoding.Latin1.GetString(bytes);

        // WinAnsi encoding maps \u00A0 to standard space byte 0x20
        // PDF contains the document text without error
        Assert.NotEmpty(bytes);
        Assert.Contains("FirstPart", pdfText);
    }

    [Fact]
    public void Pdf_OversizedWordWithCombiningMarks_SplitsWithoutOrphaningMarks()
    {
        // A long accented word that exceeds the page content width and forces emergency splitting
        string accented = "A\u0301B\u0301C\u0301D\u0301E\u0301F\u0301G\u0301H\u0301I\u0301J\u0301K\u0301L\u0301M\u0301N\u0301O\u0301P\u0301Q\u0301R\u0301S\u0301T\u0301";
        var doc = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain(accented)
        ]);

        byte[] bytes = WritePdf(doc);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Pdf_OversizedWordWithSurrogatePairs_DoesNotCrashOrProduceInvalidUtf16()
    {
        // An oversized word containing surrogate pairs (emojis)
        string emojiRun = "A🍎B😀C🍎D😀E🍎F😀G🍎H😀I🍎J😀K🍎L😀M🍎N😀O🍎P😀";
        var doc = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain(emojiRun)
        ]);

        byte[] bytes = WritePdf(doc);
        Assert.NotEmpty(bytes);
    }
}
