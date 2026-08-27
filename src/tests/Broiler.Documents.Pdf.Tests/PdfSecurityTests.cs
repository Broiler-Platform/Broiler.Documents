using System.IO.Compression;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// The hostile-input gate: every one of these inputs must terminate within its
/// budget and produce a decision, never a hang, a crash, or an unbounded
/// allocation.
/// </summary>
public sealed class PdfSecurityTests
{
    private static PdfReadResult Read(byte[] pdf, PdfReadOptions? options = null)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, options);
    }

    [Fact]
    public void Every_Truncation_Of_A_Valid_File_Terminates_With_A_Decision()
    {
        byte[] complete = PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Truncate me"));

        for (int length = 0; length <= complete.Length; length += 7)
        {
            byte[] truncated = complete[..length];
            PdfReadResult result = Read(truncated);

            // Any status is acceptable; hanging or throwing is not.
            Assert.True(Enum.IsDefined(result.Status));
        }
    }

    [Fact]
    public void Deterministic_Byte_Mutations_Terminate_With_A_Decision()
    {
        byte[] complete = PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Mutate me"));

        for (int index = 0; index < complete.Length; index += 11)
        {
            byte[] mutated = (byte[])complete.Clone();
            // A fixed transform keeps every failure reproducible from its index.
            mutated[index] ^= 0xFF;
            PdfReadResult result = Read(mutated);
            Assert.True(Enum.IsDefined(result.Status));
        }
    }

    [Fact]
    public void A_Self_Referential_Object_Does_Not_Recurse_Forever()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int loop = builder.Reserve();

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{loop} 0 R] /Count 1 >>");
        builder.SetObject(loop, $"<< /Type /Page /Parent {pages} 0 R /Contents {loop} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));
        Assert.True(Enum.IsDefined(result.Status));
    }

    [Fact]
    public void A_Cross_Reference_Chain_That_Loops_Is_Cut()
    {
        byte[] original = PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Looping"));
        string text = System.Text.Encoding.Latin1.GetString(original);

        int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        long xrefOffset = long.Parse(
            text[(startxref + 9)..].TrimStart().Split('\n')[0].Trim(),
            System.Globalization.CultureInfo.InvariantCulture);

        // Point the trailer's /Prev at its own section.
        string looped = text.Replace(
            "/Root ",
            $"/Prev {xrefOffset} /Root ",
            StringComparison.Ordinal);

        PdfReadResult result = Read(PdfFileBuilder.Latin1(looped));
        Assert.True(Enum.IsDefined(result.Status));
    }

    [Fact]
    public void A_Decompression_Bomb_In_A_Content_Stream_Is_Rejected_Not_Absorbed()
    {
        byte[] bomb = Deflate(new byte[8 * 1024 * 1024]);

        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int content = builder.AddStream(string.Empty, bomb, filter: "FlateDecode");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /Contents {content} 0 R >>");

        var options = new PdfReadOptions(pdfLimits: new PdfLimits(
            maxDecodedStreamBytes: 256 * 1024,
            maxSingleStreamBytes: 256 * 1024));

        PdfReadResult result = Read(builder.Build(catalog), options);

        Assert.Contains(
            result.Diagnostics,
            d => d.Code is PdfDiagnosticCodes.FilterLimit or PdfDiagnosticCodes.Limit);
    }

    [Fact]
    public void A_Page_Flood_Is_Stopped_By_The_Page_Limit()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        var kids = new List<int>();
        for (int i = 0; i < 40; i++)
            kids.Add(builder.Reserve());

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>");
        foreach (int kid in kids)
            builder.SetObject(kid, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] >>");

        var options = new PdfReadOptions(pdfLimits: new PdfLimits(maxPageCount: 5));
        PdfReadResult result = Read(builder.Build(catalog), options);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.Limit);
    }

    [Fact]
    public void An_Operator_Flood_Is_Stopped_By_The_Operator_Limit()
    {
        string content = string.Concat(Enumerable.Repeat("q Q ", 20000));
        var options = new PdfReadOptions(pdfLimits: new PdfLimits(maxContentOperators: 100));

        PdfReadResult result = Read(PdfFileBuilder.SinglePage(content), options);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.Limit);
    }

    [Fact]
    public void A_Limit_Never_Downgrades_Into_A_Successful_Empty_Document()
    {
        var options = new PdfReadOptions(pdfLimits: new PdfLimits(maxExtractedCharacters: 4));
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Much longer than four")), options);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.NotEqual(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void Cancellation_Before_Completion_Rejects_Rather_Than_Returning_A_Fragment()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var input = DocumentInput.FromBytes(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Cancelled")));
        var request = new DocumentReadRequest(input, PdfReadOptions.Default, cancellation.Token);
        DocumentReadResult result = new PdfDocumentCodec().Read(request);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.Cancelled);
    }

    [Fact]
    public void An_Inline_Image_Without_Its_Terminator_Does_Not_Hang()
    {
        // No EI keyword at all: the scan must stop at the end of the stream.
        PdfReadResult result = Read(PdfFileBuilder.SinglePage("BI /W 4 /H 4 ID " + new string('ª', 4096)));
        Assert.True(Enum.IsDefined(result.Status));
    }

    [Fact]
    public void Diagnostics_Never_Carry_Document_Content()
    {
        const string Secret = "TOPSECRETVALUE";
        byte[] pdf = PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText(Secret),
            extraCatalogEntries: " /OpenAction << /S /JavaScript /JS (app.alert('x')) >> ");

        PdfReadResult result = Read(pdf);

        Assert.All(result.Diagnostics, d => Assert.DoesNotContain(Secret, d.Message));
        Assert.All(result.Diagnostics, d => Assert.DoesNotContain("app.alert", d.Message));
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
    }

    [Fact]
    public void Two_Codec_Instances_Do_Not_Share_State()
    {
        var first = new PdfDocumentCodec(PdfCodecServices.Base);
        var second = new PdfDocumentCodec(new PdfCodecServices(uriPolicy: new PdfUriPolicy(allowHttp: true)));

        Assert.False(first.Services.UriPolicy.AllowHttp);
        Assert.True(second.Services.UriPolicy.AllowHttp);

        byte[] pdf = PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Shared nothing"));
        using var a = new MemoryStream(pdf);
        using var b = new MemoryStream(pdf);

        Assert.Equal(
            first.ReadPdf(a).Document.PlainText,
            second.ReadPdf(b).Document.PlainText);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(data, 0, data.Length);
        return output.ToArray();
    }
}

public sealed class PdfUriPolicyTests
{
    [Theory]
    [InlineData("https://example.org/")]
    [InlineData("https://example.org/path?query=1#fragment")]
    public void Admits_Absolute_Https(string uri)
    {
        Assert.True(PdfUriPolicy.Default.TryAdmit(uri, out string canonical, out _));
        Assert.StartsWith("https://", canonical);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html,<script>")]
    [InlineData("ms-word:ofe|u|https://example.org/x")]
    [InlineData("/relative/path")]
    [InlineData("example.org")]
    [InlineData("")]
    public void Rejects_Everything_Outside_The_Allow_List(string uri)
    {
        Assert.False(PdfUriPolicy.Default.TryAdmit(uri, out _, out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void Http_And_Mailto_Need_An_Explicit_Opt_In()
    {
        Assert.False(PdfUriPolicy.Default.IsAdmitted("http://example.org/"));
        Assert.False(PdfUriPolicy.Default.IsAdmitted("mailto:someone@example.org"));

        Assert.True(new PdfUriPolicy(allowHttp: true).IsAdmitted("http://example.org/"));
        Assert.True(new PdfUriPolicy(allowMailto: true).IsAdmitted("mailto:someone@example.org"));
    }

    [Fact]
    public void Rejects_User_Information_And_Control_Characters()
    {
        Assert.False(PdfUriPolicy.Default.IsAdmitted("https://user:pass@example.org/"));
        Assert.False(PdfUriPolicy.Default.IsAdmitted("https://example.org/\r\nInjected: header"));
        Assert.False(PdfUriPolicy.Default.IsAdmitted("https://example.org/ "));
    }

    [Fact]
    public void Enforces_A_Length_Bound()
    {
        string longUri = "https://example.org/" + new string('a', 5000);

        Assert.False(PdfUriPolicy.Default.IsAdmitted(longUri));
        Assert.True(new PdfUriPolicy(maxLength: 8192).IsAdmitted(longUri));
    }
}
