namespace Broiler.Documents.Docx.Tests;

public sealed class HiddenSectionWriterTests
{
    [Fact]
    public void Writing_Unsupported_Section_Visibility_Is_Reported()
    {
        var document = RichTextDocument.FromPlainText("retained")
            .WithSections([new DocumentSection(0, 1, 1, 0, hidden: true)]);
        using var stream = new MemoryStream();
        var result = new DocxDocumentCodec().Write(document, stream);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.section.hidden");
    }
}
