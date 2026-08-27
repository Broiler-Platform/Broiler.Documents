using System.Text;
using Broiler.Documents.Pdf.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfWriterTests
{
    private static (byte[] Bytes, PdfWriteResult Result) Write(
        RichTextDocument document,
        PdfWriteOptions? options = null,
        PdfCodecServices? services = null)
    {
        using var stream = new MemoryStream();
        PdfWriteResult result = new PdfDocumentCodec(services ?? PdfCodecServices.Base)
            .WritePdf(document, stream, options);
        return (stream.ToArray(), result);
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact]
    public void Emits_A_Well_Formed_Skeleton()
    {
        (byte[] bytes, PdfWriteResult result) = Write(RichTextDocument.FromPlainText("Hello, PDF."));
        string text = Latin1(bytes);

        Assert.Equal(DocumentDestinationState.Committed, result.DestinationState);
        Assert.Equal(1, result.PageCount);
        Assert.StartsWith("%PDF-1.7", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Pages", text);
        Assert.Contains("/Type /Page ", text);
        Assert.Contains("xref", text);
        Assert.Contains("startxref", text);
        Assert.Contains("trailer", text);
    }

    [Fact]
    public void Cross_Reference_Offsets_Point_At_Their_Objects()
    {
        (byte[] bytes, _) = Write(RichTextDocument.FromPlainText("Offsets matter."));
        string text = Latin1(bytes);

        // "startxref" also ends in "xref", so anchor on the line start.
        int xrefIndex = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        string[] lines = text[xrefIndex..].Split('\n');

        // lines[0] is "xref", lines[1] the subsection header, lines[2] object zero.
        int objectCount = int.Parse(lines[1].Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(objectCount > 3);

        for (int number = 1; number < objectCount; number++)
        {
            long offset = long.Parse(lines[number + 2][..10], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(offset > 0 && offset < bytes.Length, $"object {number} offset {offset}");
            Assert.StartsWith($"{number} 0 obj", text[(int)offset..]);
        }
    }

    [Fact]
    public void Startxref_Points_At_The_Cross_Reference_Table()
    {
        (byte[] bytes, _) = Write(RichTextDocument.FromPlainText("Where is the table?"));
        string text = Latin1(bytes);

        int marker = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        long offset = long.Parse(
            text[(marker + 10)..].Split('\n')[0],
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.StartsWith("xref", text[(int)offset..]);
    }

    [Fact]
    public void Two_Writes_Of_The_Same_Document_Are_Byte_Identical()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("Determinism is the point.\nSecond paragraph.");

        (byte[] first, _) = Write(document);
        (byte[] second, _) = Write(document);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_Caller_Supplied_Identifier_Replaces_The_Derived_One()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("Identified.");

        (byte[] derived, _) = Write(document);
        (byte[] supplied, _) = Write(document, new PdfWriteOptions(fileIdentifier: "0123456789abcdef0123456789abcdef"));

        Assert.Contains("0123456789ABCDEF0123456789ABCDEF", Latin1(supplied));
        Assert.NotEqual(Latin1(derived), Latin1(supplied));
    }

    [Fact]
    public void Long_Text_Wraps_And_Overflows_Onto_Further_Pages()
    {
        string paragraph = string.Join(" ", Enumerable.Repeat("wordy", 4000));
        (byte[] bytes, PdfWriteResult result) = Write(RichTextDocument.FromPlainText(paragraph));

        Assert.True(result.PageCount > 1, $"expected several pages, got {result.PageCount}");
        Assert.Contains($"/Count {result.PageCount}", Latin1(bytes));
    }

    [Fact]
    public void Uses_A_Distinct_Font_Resource_Per_Style()
    {
        RichTextParagraph paragraph = RichTextParagraph.Plain("plain ");
        paragraph = paragraph.InsertText(paragraph.Length, "bold", new InlineStyle { Bold = true });

        string text = Latin1(Write(RichTextDocument.FromParagraphs([paragraph])).Bytes);

        Assert.Contains("/BaseFont /Helvetica ", text);
        Assert.Contains("/BaseFont /Helvetica-Bold ", text);
    }

    [Fact]
    public void Maps_A_Serif_Family_Onto_The_Standard_Serif_Face()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Serif text", new InlineStyle { FontFamily = "Times New Roman" }),
        ]);

        Assert.Contains("/BaseFont /Times-Roman", Latin1(Write(document).Bytes));
    }

    [Fact]
    public void Emits_A_Link_Annotation_For_An_Admitted_Target()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Broiler", new InlineStyle { LinkHref = "https://example.org/x" }),
        ]);

        string text = Latin1(Write(document).Bytes);

        Assert.Contains("/Subtype /Link", text);
        Assert.Contains("/S /URI /URI (https://example.org/x)", text);
    }

    [Fact]
    public void Refuses_To_Emit_A_Denied_Link_Target()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Click", new InlineStyle { LinkHref = "javascript:alert(1)" }),
        ]);

        (byte[] bytes, PdfWriteResult result) = Write(document, new PdfWriteOptions(compressStreams: false));
        string text = Latin1(bytes);

        Assert.DoesNotContain("javascript", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Subtype /Link", text);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.UriRejected);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);

        // The text itself is still written; only the activation is withheld.
        Assert.Contains("(Click)", text);
    }

    [Fact]
    public void An_Http_Target_Needs_An_Explicit_Opt_In()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Plain", new InlineStyle { LinkHref = "http://example.org/" }),
        ]);

        Assert.DoesNotContain("/Subtype /Link", Latin1(Write(document).Bytes));

        var permissive = new PdfWriteOptions(uriPolicy: new PdfUriPolicy(allowHttp: true));
        Assert.Contains("/Subtype /Link", Latin1(Write(document, permissive).Bytes));
    }

    [Fact]
    public void Writes_Only_Caller_Supplied_Metadata()
    {
        var metadata = new PdfDocumentMetadata(
            title: "Title",
            authors: ["Ada", "Grace"],
            producer: "Broiler",
            creationDate: PdfDate.WithOffset(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5))));

        string text = Latin1(Write(RichTextDocument.FromPlainText("x"), new PdfWriteOptions(metadata: metadata)).Bytes);

        Assert.Contains("/Title (Title)", text);
        Assert.Contains("/Author (Ada; Grace)", text);
        Assert.Contains("/CreationDate (D:20260102030405-05'00')", text);

        // Nothing is invented for a field the caller did not supply.
        Assert.DoesNotContain("/ModDate", text);
        Assert.DoesNotContain("/Subject", text);
    }

    [Fact]
    public void Omits_The_Info_Dictionary_When_No_Metadata_Is_Supplied()
    {
        Assert.DoesNotContain("/Info", Latin1(Write(RichTextDocument.FromPlainText("x")).Bytes));
    }

    [Fact]
    public void Writes_A_Zone_Less_Date_Back_Without_A_Zone()
    {
        string formatted = Writing.PdfWriter.FormatDate(
            PdfDate.WithoutOffset(new DateTime(2026, 8, 25, 13, 30, 0)));

        Assert.Equal("D:20260825133000", formatted);
    }

    [Fact]
    public void Escapes_Parentheses_And_Backslashes_In_Page_Text()
    {
        string text = Latin1(Write(RichTextDocument.FromPlainText(@"a(b)c\d")).Bytes);

        // The content stream is compressed by default, so check the uncompressed form.
        string uncompressed = Latin1(Write(
            RichTextDocument.FromPlainText(@"a(b)c\d"),
            new PdfWriteOptions(compressStreams: false)).Bytes);

        Assert.Contains(@"(a\(b\)c\\d)", uncompressed);
        Assert.Contains("%PDF-1.7", text);
    }

    [Fact]
    public void Substitutes_And_Reports_Characters_Outside_WinAnsi()
    {
        (byte[] bytes, PdfWriteResult result) = Write(
            RichTextDocument.FromPlainText("Greek: αβγ"),
            new PdfWriteOptions(compressStreams: false));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.WriteCharacterUnsupported);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
        Assert.Contains("(Greek: ???)", Latin1(bytes));
    }

    [Fact]
    public void Keeps_A_Non_Latin_Title_Through_The_Utf16_Text_String_Form()
    {
        var metadata = new PdfDocumentMetadata(title: "Ελληνικά");
        byte[] bytes = Write(RichTextDocument.FromPlainText("x"), new PdfWriteOptions(metadata: metadata)).Bytes;

        // UTF-16BE text strings begin with the FE FF byte-order mark, escaped octally.
        Assert.Contains(@"/Title (\376\377", Latin1(bytes));
    }

    [Fact]
    public void Drops_An_Inline_Image_And_Says_So()
    {
        var image = new InlineImage(new byte[] { 1, 2, 3 }, "image/png", 10, 10);
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(InlineImage.PlaceholderText, new InlineStyle { Image = image }),
        ]);

        PdfWriteResult result = Write(document).Result;

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.WriteImageNotComposed);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void Reports_That_Line_Breaking_Used_Approximate_Metrics()
    {
        PdfWriteResult result = Write(RichTextDocument.FromPlainText("Measured.")).Result;

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.WriteMetricsApproximate);
    }

    [Fact]
    public void A_Composed_Metrics_Provider_Changes_Line_Breaking_And_The_Report()
    {
        var services = PdfCodecServices.Base.WithFontMetrics(new WideMetrics());
        string paragraph = string.Join(" ", Enumerable.Repeat("word", 200));

        int narrowPages = Write(RichTextDocument.FromPlainText(paragraph)).Result.PageCount;
        (byte[] _, PdfWriteResult wide) = Write(RichTextDocument.FromPlainText(paragraph), services: services);

        Assert.True(wide.PageCount > narrowPages);
        Assert.DoesNotContain(wide.Diagnostics, d => d.Code == PdfDiagnosticCodes.WriteMetricsApproximate);
    }

    [Fact]
    public void Rejects_Before_Writing_A_Byte_When_The_Output_Budget_Is_Exhausted()
    {
        var options = new PdfWriteOptions(pdfLimits: new PdfLimits(maxOutputBytes: 64));
        (byte[] bytes, PdfWriteResult result) = Write(RichTextDocument.FromPlainText("Too big for the budget."), options);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDestinationState.NotStarted, result.DestinationState);
        Assert.Empty(bytes);
    }

    [Fact]
    public void Reports_A_Partial_Destination_When_The_Stream_Fails()
    {
        using var failing = new FailingStream();
        PdfWriteResult result = new PdfDocumentCodec()
            .WritePdf(RichTextDocument.FromPlainText("Doomed."), failing);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDestinationState.PartialDestination, result.DestinationState);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.WritePartialDestination);
    }

    [Fact]
    public void Renders_Decorations_And_Colour_As_Content_Operators()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(
                "Styled",
                new InlineStyle
                {
                    Underline = true,
                    Strikethrough = true,
                    Foreground = new BColor(255, 0, 0),
                    Background = new BColor(255, 255, 0),
                }),
        ]);

        string text = Latin1(Write(document, new PdfWriteOptions(compressStreams: false)).Bytes);

        Assert.Contains("1 0 0 rg", text);   // the red fill
        Assert.Contains("1 1 0 rg", text);   // the yellow highlight
        Assert.Contains("re f", text);       // decoration and highlight rectangles
        Assert.Contains("(Styled) Tj", text);
    }

    [Fact]
    public void Numbers_A_Numbered_List_And_Restarts_It_After_A_Break()
    {
        ParagraphStyle numbered = ParagraphStyle.Default with { ListKind = ListKind.Numbered, IndentLevel = 1 };
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("first", InlineStyle.Default, numbered),
            RichTextParagraph.Create("second", InlineStyle.Default, numbered),
            RichTextParagraph.Plain("interruption"),
            RichTextParagraph.Create("restarted", InlineStyle.Default, numbered),
        ]);

        string text = Latin1(Write(document, new PdfWriteOptions(compressStreams: false)).Bytes);

        // The marker and the first word share a style, so they emit as one run.
        Assert.Contains("(1. first)", text);
        Assert.Contains("(2. second)", text);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, @"\(1\. ").Count);
    }

    private sealed class WideMetrics : IPdfFontMetricsProvider
    {
        public bool IsApproximate => false;

        public double GetAdvanceWidth(PdfStandardFont font, char character) => 2000;

        public double GetAscent(PdfStandardFont font) => 800;

        public double GetDescent(PdfStandardFont font) => 200;
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position { get => 0; set { } }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("The destination is unavailable.");
    }
}
