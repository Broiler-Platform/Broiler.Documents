using System;
using System.IO;
using System.Threading.Tasks;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Composition;

/// <summary>
/// A codec this tool reads with and never writes with, whatever the codec
/// itself can do.
/// </summary>
/// <remarks>
/// <para>
/// PDF is the reason. <c>Broiler.Documents.Pdf</c> reads and writes, and the
/// roadmap (<c>docs/pdf-support-roadmap.md</c> §4.1) lets an application open
/// PDF files before it lets one produce them: writing is the later
/// write-preview gate. Composing the codec as it comes would have given this
/// tool PDF destinations in <c>convert</c>, <c>new</c>, <c>edit</c> and
/// <c>roundtrip</c> along with the reading that was wanted.
/// </para>
/// <para>
/// Reporting <see cref="CanWrite"/> as false is the whole mechanism: every
/// command already asks before it writes, so a PDF destination is refused as a
/// usage error, and <c>formats</c> reports the codec as reading only. The write
/// members refuse as well, so nothing that forgot to ask gets a PDF out of it.
/// </para>
/// </remarks>
public sealed class ReadOnlyCodec(DocumentCodec inner) : DocumentCodec((inner ?? throw new ArgumentNullException(nameof(inner))).Descriptor)
{
    /// <summary>The codec reads are delegated to.</summary>
    public DocumentCodec Inner { get; } = inner;

    public override bool CanRead => Inner.CanRead;

    public override bool CanWrite => false;

    public override DocumentProbeResult Probe(DocumentProbeRequest request) => Inner.Probe(request);

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null) => Inner.Read(source, options);

    public override DocumentReadResult Read(DocumentReadRequest request) => Inner.Read(request);

    public override ValueTask<DocumentReadResult> ReadAsync(DocumentReadRequest request) => Inner.ReadAsync(request);

    public override DocumentWriteResult Write(RichTextDocument document, Stream destination, DocumentWriteOptions? options = null) =>
        throw new NotSupportedException("This tool does not write " + Name + ".");
}
