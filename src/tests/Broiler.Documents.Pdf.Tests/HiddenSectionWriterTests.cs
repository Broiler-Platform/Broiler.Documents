namespace Broiler.Documents.Pdf.Tests;

public sealed class HiddenSectionWriterTests
{
    [Fact]
    public void Unsupported_Hidden_Section_Visibility_Is_Reported_By_The_Pdf_Layout()
    {
        var document = RichTextDocument.FromPlainText("retained")
            .WithSections([new DocumentSection(0, 1, 1, 0, hidden: true)]);
        using var stream = new MemoryStream();
        var result = new PdfDocumentCodec().Write(document, stream);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("Hidden section visibility"));
    }
}
