using System;
using System.Linq;
using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// What an RTF read says when the document carries a picture or an object this
/// reader does not import.
/// </summary>
/// <remarks>
/// Written because the behaviour had no test and the option that drives it is
/// announced for removal (ADR 0014). Retiring an untested behaviour means nobody
/// can tell whether the removal changed anything, so this pins what the two paths
/// currently say. When <c>DecodeEmbeddedObjects</c> goes, one of these tests goes
/// with it, deliberately and visibly.
/// </remarks>
public sealed class RtfEmbeddedObjectReportTests
{
    /// <summary>An RTF carrying a picture destination, which this reader skips.</summary>
    private const string WithPicture =
        @"{\rtf1\ansi Text before {\pict\pngblip 89504e47} text after.}";

    private static DocumentReadResult Read(bool decodeEmbeddedObjects)
    {
        using var stream = new System.IO.MemoryStream(
            Encoding.ASCII.GetBytes(WithPicture),
            writable: false);

#pragma warning disable CS0618 // The member under test is announced for removal.
        var options = new DocumentReadOptions(decodeEmbeddedObjects: decodeEmbeddedObjects);
#pragma warning restore CS0618

        return new RtfDocumentCodec().Read(stream, options);
    }

    [Fact]
    public void A_Skipped_Picture_Is_Reported_As_A_Note_By_Default()
    {
        DocumentReadResult result = Read(decodeEmbeddedObjects: false);

        DocumentDiagnostic note = Assert.Single(
            result.Diagnostics,
            d => d.Code == "rtf.embedded");
        Assert.Equal(DocumentDiagnosticSeverity.Info, note.Severity);
    }

    [Fact]
    public void Asking_For_Decoding_Escalates_It_To_The_Shared_Capability_Code()
    {
        // The one thing the announced option does. A caller that asked for image
        // decoding is told this reader composes none, under the code every codec
        // reports that with — not the bland note, which reads like a property of
        // the document rather than an answer to what was asked.
        DocumentReadResult result = Read(decodeEmbeddedObjects: true);

        DocumentDiagnostic escalated = Assert.Single(
            result.Diagnostics,
            d => d.Code == DocumentDiagnosticCodes.CapabilityNotComposed);
        Assert.Equal(DocumentDiagnosticSeverity.Warning, escalated.Severity);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "rtf.embedded");
    }

    [Fact]
    public void The_Text_Around_A_Skipped_Picture_Still_Reads()
    {
        // Whichever way the option is set. A picture this reader will not import
        // costs the picture, never the document.
        foreach (bool requested in new[] { false, true })
        {
            DocumentReadResult result = Read(requested);

            string text = string.Concat(result.Document.Paragraphs.Select(static p => p.Text));
            Assert.Contains("Text before", text, StringComparison.Ordinal);
            Assert.Contains("text after", text, StringComparison.Ordinal);
        }
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

        using var stream = new System.IO.MemoryStream(
            Encoding.ASCII.GetBytes(many.ToString()),
            writable: false);
        DocumentReadResult result = new RtfDocumentCodec().Read(stream);

        Assert.Single(result.Diagnostics, d => d.Code == "rtf.embedded");
    }
}
