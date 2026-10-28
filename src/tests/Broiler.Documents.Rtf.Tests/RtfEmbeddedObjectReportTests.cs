using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// What an RTF read says when the document carries a picture or an object this
/// reader does not import.
/// </summary>
public sealed class RtfEmbeddedObjectReportTests
{
    /// <summary>An RTF carrying a picture destination, which this reader skips.</summary>
    private const string WithPicture =
        @"{\rtf1\ansi Text before {\pict\pngblip 89504e47} text after.}";

    private static DocumentReadResult Read(DocumentReadOptions? options = null)
    {
        using var stream = new MemoryStream(
            Encoding.ASCII.GetBytes(WithPicture),
            writable: false);

        return new RtfDocumentCodec().Read(stream, options ?? DocumentReadOptions.Default);
    }

    [Fact]
    public void A_Skipped_Picture_Is_Reported_As_A_Note()
    {
        DocumentReadResult result = Read();

        DocumentDiagnostic note = Assert.Single(
            result.Diagnostics,
            d => d.Code == "rtf.embedded");
        Assert.Equal(DocumentDiagnosticSeverity.Info, note.Severity);
    }

    [Fact]
    public void The_Text_Around_A_Skipped_Picture_Still_Reads()
    {
        // A picture this reader will not import costs the picture, never the document.
        DocumentReadResult result = Read();

        string text = string.Concat(result.Document.Paragraphs.Select(static p => p.Text));
        Assert.Contains("Text before", text, StringComparison.Ordinal);
        Assert.Contains("text after", text, StringComparison.Ordinal);
    }

    [Fact]
    public void One_Report_However_Many_Pictures()
    {
        // A document with a picture on every page should not produce a diagnostic
        // per page; the reader reports the capability once.
        var many = new StringBuilder(@"{\rtf1\ansi");
        for (int i = 0; i < 8; i++)
            many.Append(@" p{\pict\pngblip 89504e47}");
        many.Append('}');

        using var stream = new MemoryStream(
            Encoding.ASCII.GetBytes(many.ToString()),
            writable: false);
        DocumentReadResult result = new RtfDocumentCodec().Read(stream);

        Assert.Single(result.Diagnostics, d => d.Code == "rtf.embedded");
    }
}
