using Broiler.Documents.Model;

namespace Broiler.Documents.Tests;

/// <summary>
/// The request, status, and option-validation contracts that every codec shares.
/// </summary>
public sealed class DocumentContractTests
{
    /// <summary>A codec that only implements the stream methods, as every codec did before.</summary>
    private sealed class LegacyCodec : DocumentCodec
    {
        public LegacyCodec()
            : base(new DocumentFormatDescriptor("LEGACY", ["text/legacy"], [".legacy"]))
        {
        }

        public int StreamReads { get; private set; }

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override DocumentProbeResult Probe(DocumentProbeRequest request) =>
            DocumentProbeResult.Match(DocumentProbeConfidence.Certain, Descriptor.Name);

        public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
        {
            StreamReads++;
            using var reader = new StreamReader(source);
            return new DocumentReadResult(RichTextDocument.FromPlainText(reader.ReadToEnd()));
        }

        public override DocumentWriteResult Write(
            RichTextDocument document,
            Stream destination,
            DocumentWriteOptions? options = null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(document.PlainText);
            destination.Write(bytes);
            return new DocumentWriteResult(bytes.Length);
        }
    }

    /// <summary>A codec with its own option type, which it validates.</summary>
    private sealed class TypedCodec : DocumentCodec
    {
        public sealed class Options : DocumentReadOptions
        {
            public Options(string marker = "typed") => Marker = marker;

            public string Marker { get; }
        }

        public sealed class WriteOptions : DocumentWriteOptions;

        public TypedCodec()
            : base(new DocumentFormatDescriptor("TYPED", ["text/typed"], [".typed"]))
        {
        }

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override DocumentProbeResult Probe(DocumentProbeRequest request) =>
            DocumentProbeResult.Match(DocumentProbeConfidence.Certain, Descriptor.Name);

        public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
        {
            if (!TryValidateOptions(options ?? DocumentReadOptions.Default, Name, out Options? typed, out DocumentReadResult? rejection))
                return rejection!;

            return new DocumentReadResult(RichTextDocument.FromPlainText(typed?.Marker ?? "default"));
        }

        public override DocumentWriteResult Write(
            RichTextDocument document,
            Stream destination,
            DocumentWriteOptions? options = null)
        {
            if (!TryValidateOptions(options ?? DocumentWriteOptions.Default, Name, out WriteOptions? _, out DocumentWriteResult? rejection))
                return rejection!;

            return new DocumentWriteResult(0);
        }
    }

    /// <summary>Another codec's option types, to prove a mismatch is rejected.</summary>
    private sealed class ForeignReadOptions : DocumentReadOptions;

    private sealed class ForeignWriteOptions : DocumentWriteOptions;

    /// <summary>A codec that never claims anything, so selection can fail honestly.</summary>
    private sealed class NeverMatchesCodec : DocumentCodec
    {
        public NeverMatchesCodec()
            : base(new DocumentFormatDescriptor("NEVER", ["text/never"], [".never"]))
        {
        }

        public override bool CanRead => true;

        public override bool CanWrite => false;

        public override DocumentProbeResult Probe(DocumentProbeRequest request) => DocumentProbeResult.NoMatch();

        public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null) =>
            throw new InvalidOperationException("A codec that never matches must never be asked to read.");

        public override DocumentWriteResult Write(
            RichTextDocument document,
            Stream destination,
            DocumentWriteOptions? options = null) =>
            throw new InvalidOperationException("This codec cannot write.");
    }

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    // ---- request adapters -----------------------------------------------------

    [Fact]
    public void A_Codec_That_Only_Implements_Streams_Still_Serves_Requests()
    {
        var codec = new LegacyCodec();
        using DocumentInput input = DocumentInput.FromBytes(Bytes("legacy content"));

        DocumentReadResult result = codec.Read(new DocumentReadRequest(input));

        Assert.Equal("legacy content", result.Document.PlainText);
        Assert.Equal(1, codec.StreamReads);
    }

    [Fact]
    public async Task The_Default_Async_Read_Matches_The_Synchronous_One()
    {
        var codec = new LegacyCodec();
        using DocumentInput input = DocumentInput.FromBytes(Bytes("async content"));

        DocumentReadResult result = await codec.ReadAsync(new DocumentReadRequest(input));

        Assert.Equal("async content", result.Document.PlainText);
    }

    [Fact]
    public void A_Request_Cancelled_Before_It_Starts_Rejects_Without_Reading()
    {
        var codec = new LegacyCodec();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using DocumentInput input = DocumentInput.FromBytes(Bytes("never read"));

        DocumentReadResult result = codec.Read(new DocumentReadRequest(input, null, cancellation.Token));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == DocumentDiagnosticCodes.Cancelled);
        Assert.Equal(0, codec.StreamReads);
    }

    [Fact]
    public void A_Write_Request_Cancelled_Before_It_Starts_Leaves_The_Destination_Untouched()
    {
        var codec = new LegacyCodec();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var destination = new MemoryStream();

        DocumentWriteResult result = codec.Write(
            new DocumentWriteRequest(RichTextDocument.FromPlainText("x"), destination, null, cancellation.Token));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDestinationState.NotStarted, result.DestinationState);
        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public void A_Write_Request_Refuses_A_Read_Only_Destination()
    {
        using var destination = new MemoryStream(new byte[8], writable: false);

        Assert.Throws<ArgumentException>(() =>
            new DocumentWriteRequest(RichTextDocument.Empty, destination));
    }

    // ---- typed options --------------------------------------------------------

    [Fact]
    public void A_Codec_Accepts_Its_Own_Option_Type()
    {
        var codec = new TypedCodec();
        using var source = new MemoryStream(Bytes("x"));

        DocumentReadResult result = codec.Read(source, new TypedCodec.Options("chosen"));

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal("chosen", result.Document.PlainText);
    }

    [Fact]
    public void A_Codec_Accepts_The_Plain_Shared_Options_And_Applies_Its_Own_Defaults()
    {
        var codec = new TypedCodec();
        using var source = new MemoryStream(Bytes("x"));

        // A caller that knows nothing about this format is not an error.
        DocumentReadResult result = codec.Read(source, new DocumentReadOptions());

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal("default", result.Document.PlainText);
    }

    [Fact]
    public void A_Codec_Rejects_Another_Codecs_Option_Type_Rather_Than_Ignoring_It()
    {
        var codec = new TypedCodec();
        using var source = new MemoryStream(Bytes("x"));

        DocumentReadResult result = codec.Read(source, new ForeignReadOptions());

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        DocumentDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DocumentDiagnosticCodes.OptionsInvalid, diagnostic.Code);

        // The message names both types so the caller can see what it passed.
        Assert.Contains(nameof(TypedCodec.Options), diagnostic.Message);
        Assert.Contains(nameof(ForeignReadOptions), diagnostic.Message);
    }

    [Fact]
    public void The_Write_Side_Rejects_A_Foreign_Option_Type_Too()
    {
        var codec = new TypedCodec();
        using var destination = new MemoryStream();

        DocumentWriteResult result = codec.Write(RichTextDocument.Empty, destination, new ForeignWriteOptions());

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDestinationState.NotStarted, result.DestinationState);
        Assert.Empty(destination.ToArray());
    }

    // ---- status derivation ----------------------------------------------------

    [Fact]
    public void Informational_Diagnostics_Alone_Leave_A_Result_Successful()
    {
        DocumentDiagnostic[] diagnostics = [DocumentDiagnostic.Info("x.note", "Nothing was lost.")];

        Assert.Equal(DocumentResultStatus.Success, DocumentReadResult.StatusFrom(diagnostics));
        Assert.Equal(DocumentResultStatus.Success, DocumentWriteResult.StatusFrom(diagnostics));
    }

    [Theory]
    [InlineData(DocumentDiagnosticSeverity.Warning)]
    [InlineData(DocumentDiagnosticSeverity.Error)]
    public void Anything_Above_Informational_Makes_A_Result_Partial(DocumentDiagnosticSeverity severity)
    {
        DocumentDiagnostic[] diagnostics = [new(severity, "x.skipped", "A construct was skipped.")];

        Assert.Equal(DocumentResultStatus.Partial, DocumentReadResult.StatusFrom(diagnostics));
    }

    [Fact]
    public void Status_And_Severity_Are_Independent()
    {
        // The point of the separation: a result can carry an error diagnostic and
        // still be usable, and a host must branch on the status rather than on
        // whether any diagnostic happens to be an error.
        var result = new DocumentReadResult(
            RichTextDocument.FromPlainText("recovered"),
            [DocumentDiagnostic.Error("x.recovered", "A construct was recovered.")],
            DocumentResultStatus.Partial);

        Assert.True(result.HasErrors);
        Assert.True(result.IsUsable);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void A_Rejection_Is_Never_Usable()
    {
        DocumentReadResult result = DocumentReadResult.Rejected(DocumentDiagnosticCodes.InputUnreadable, "No.");

        Assert.False(result.IsUsable);
        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
    }

    // ---- diagnostic locations -------------------------------------------------

    [Fact]
    public void A_Diagnostic_May_Carry_A_Location_And_Reads_Well_Without_One()
    {
        var located = DocumentDiagnostic.Warning(
            "x.here",
            "Something happened.",
            new DocumentDiagnosticLocation(byteOffset: 42, pageNumber: 3, part: "/word/document.xml"));

        Assert.Contains("offset 42", located.ToString());
        Assert.Contains("page 3", located.ToString());
        Assert.Contains("/word/document.xml", located.ToString());

        Assert.Null(DocumentDiagnostic.Info("x.plain", "No location.").Location);
    }

    // ---- catalog selection ----------------------------------------------------

    [Fact]
    public void SelectAndRead_Probes_And_Reads_Through_One_Non_Seekable_Pass()
    {
        var catalog = new DocumentCodecCatalog([new LegacyCodec()]);
        using var source = new DocumentInputTests_ForwardOnly(Bytes("selected content"));
        using DocumentInput input = DocumentInput.FromStream(source);

        DocumentCodecSelection selection = catalog.SelectAndRead(input);

        Assert.Equal("LEGACY", selection.Codec!.Name);
        Assert.True(selection.IsUsable);
        Assert.Equal("selected content", selection.Result.Document.PlainText);
    }

    [Fact]
    public void SelectAndRead_Reports_When_Nothing_Recognizes_The_Source()
    {
        var catalog = new DocumentCodecCatalog([new NeverMatchesCodec()]);
        using DocumentInput input = DocumentInput.FromBytes(Bytes("not a document"));

        DocumentCodecSelection selection = catalog.SelectAndRead(input);

        Assert.Null(selection.Codec);
        Assert.False(selection.IsUsable);
        Assert.Contains(
            selection.Result.Diagnostics,
            d => d.Code == DocumentDiagnosticCodes.InputUnreadable);
    }

    [Fact]
    public async Task SelectAndReadAsync_Matches_The_Synchronous_Path()
    {
        var catalog = new DocumentCodecCatalog([new LegacyCodec()]);
        using DocumentInput input = DocumentInput.FromBytes(Bytes("async selection"));

        DocumentCodecSelection selection = await catalog.SelectAndReadAsync(input);

        Assert.Equal("LEGACY", selection.Codec!.Name);
        Assert.Equal("async selection", selection.Result.Document.PlainText);
    }

    /// <summary>A non-seekable stream, so the selection path is exercised honestly.</summary>
    private sealed class DocumentInputTests_ForwardOnly : Stream
    {
        private readonly MemoryStream _inner;

        public DocumentInputTests_ForwardOnly(byte[] data) => _inner = new MemoryStream(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
