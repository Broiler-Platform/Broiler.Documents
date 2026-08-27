namespace Broiler.Documents.Tests;

/// <summary>A minimal codec that reports a fixed probe confidence — for catalog tests.</summary>
internal sealed class FakeDocumentCodec : DocumentCodec
{
    private readonly DocumentProbeConfidence _confidence;

    public FakeDocumentCodec(
        string name,
        DocumentProbeConfidence confidence,
        string extension,
        string mimeType)
        : base(new DocumentFormatDescriptor(name, new[] { mimeType }, new[] { extension }))
    {
        _confidence = confidence;
    }

    public override bool CanRead => false;

    public override bool CanWrite => false;

    public override DocumentProbeResult Probe(DocumentProbeRequest request) =>
        _confidence == DocumentProbeConfidence.None
            ? DocumentProbeResult.NoMatch()
            : DocumentProbeResult.Match(_confidence, Descriptor.Name);

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null) =>
        throw new NotSupportedException();

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null) =>
        throw new NotSupportedException();
}
