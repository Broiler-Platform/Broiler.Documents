using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Documents.Model;

namespace Broiler.Documents;

/// <summary>
/// A document-format codec: identifies its format (<see cref="Descriptor"/>),
/// probes a byte prefix, and reads/writes the rich-text document model. Codecs
/// are registered explicitly into a <see cref="DocumentCodecCatalog"/> — there is
/// no hidden global registration (ADR 0003).
/// </summary>
/// <remarks>
/// <para>
/// There are two entry points per direction, and they are not alternatives of
/// equal standing. The request forms (<see cref="Read(DocumentReadRequest)"/> and
/// <see cref="Write(DocumentWriteRequest)"/>) are the contract: they carry a
/// replayable input, typed options, limits, and cancellation in one place, with
/// exactly one owner per value. The stream forms are compatibility adapters kept
/// for existing callers, and they are what a codec implements — so a codec
/// written before the request contract existed keeps working unchanged, and one
/// that can do better overrides the request form (PDF roadmap §6.1).
/// </para>
/// <para>
/// The asynchronous forms exist so a host with real async I/O never has to block.
/// Their default implementations complete synchronously rather than pushing work
/// to a thread pool, and no adapter here calls <c>.Result</c>, <c>.Wait()</c>, or
/// <c>GetAwaiter().GetResult()</c> in either direction: sync-over-async is how a
/// UI thread deadlocks.
/// </para>
/// </remarks>
public abstract class DocumentCodec
{
    protected DocumentCodec(DocumentFormatDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public DocumentFormatDescriptor Descriptor { get; }

    public string Name => Descriptor.Name;

    /// <summary>True when <see cref="Read(Stream, DocumentReadOptions)"/> is implemented for this format.</summary>
    public abstract bool CanRead { get; }

    /// <summary>True when <see cref="Write(RichTextDocument, Stream, DocumentWriteOptions)"/> is implemented for this format.</summary>
    public abstract bool CanWrite { get; }

    /// <summary>Judge whether a byte prefix is this codec's format.</summary>
    public abstract DocumentProbeResult Probe(DocumentProbeRequest request);

    /// <summary>
    /// Read a document into the model. Recoverable problems surface as
    /// diagnostics on the result rather than exceptions; only hard I/O or
    /// limit-exceeded conditions throw (<see cref="DocumentException"/>).
    /// </summary>
    public abstract DocumentReadResult Read(Stream source, DocumentReadOptions? options = null);

    /// <summary>Write a model document to the destination in this format.</summary>
    public abstract DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null);

    /// <summary>
    /// Read through the request contract. The default implementation opens the
    /// request's input and calls <see cref="Read(Stream, DocumentReadOptions)"/>,
    /// which is correct for any codec that reads a stream front to back; a codec
    /// that wants the input's length, random access, or cancellation checkpoints
    /// overrides this instead.
    /// </summary>
    public virtual DocumentReadResult Read(DocumentReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CancellationToken.IsCancellationRequested)
        {
            return DocumentReadResult.Rejected(
                DocumentDiagnosticCodes.Cancelled,
                "The read was cancelled before it began.");
        }

        try
        {
            using Stream source = request.Input.OpenStream();
            return Read(source, request.Options);
        }
        catch (DocumentException e)
        {
            return DocumentReadResult.Rejected(DocumentDiagnosticCodes.InputTooLarge, e.Message);
        }
        catch (OperationCanceledException)
        {
            return DocumentReadResult.Rejected(
                DocumentDiagnosticCodes.Cancelled,
                "The read was cancelled before it produced a usable document.");
        }
    }

    /// <summary>
    /// Write through the request contract. The default implementation calls
    /// <see cref="Write(RichTextDocument, Stream, DocumentWriteOptions)"/>.
    /// </summary>
    public virtual DocumentWriteResult Write(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CancellationToken.IsCancellationRequested)
        {
            return DocumentWriteResult.Rejected(
                DocumentDiagnosticCodes.Cancelled,
                "The write was cancelled before any byte reached the destination.");
        }

        try
        {
            return Write(request.Document, request.Destination, request.Options);
        }
        catch (OperationCanceledException)
        {
            return DocumentWriteResult.Rejected(
                DocumentDiagnosticCodes.Cancelled,
                "The write was cancelled before any byte reached the destination.");
        }
    }

    /// <summary>
    /// The asynchronous read. The default completes synchronously; a codec with
    /// genuinely asynchronous I/O overrides it.
    /// </summary>
    public virtual ValueTask<DocumentReadResult> ReadAsync(DocumentReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.FromResult(Read(request));
    }

    /// <summary>
    /// The asynchronous write. The default completes synchronously; a codec with
    /// genuinely asynchronous I/O overrides it.
    /// </summary>
    public virtual ValueTask<DocumentWriteResult> WriteAsync(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.FromResult(Write(request));
    }

    /// <summary>
    /// Validates that <paramref name="options"/> is the type this codec needs,
    /// returning the rejection to hand back when it is not.
    /// </summary>
    /// <remarks>
    /// A codec calls this <em>before</em> touching its input or destination, so a
    /// wrong option object costs nothing and changes nothing. Options that merely
    /// come from the shared base are fine — they carry the shared settings and the
    /// codec fills in its own defaults for the rest. Only an object of a
    /// <em>different</em> codec's option type is a rejection.
    /// </remarks>
    protected static bool TryValidateOptions<TOptions>(
        DocumentReadOptions options,
        string codecName,
        out TOptions? typed,
        out DocumentReadResult? rejection)
        where TOptions : DocumentReadOptions
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options is TOptions match)
        {
            typed = match;
            rejection = null;
            return true;
        }

        typed = null;

        // The exact base type carries only shared settings, so it is always
        // acceptable: the codec supplies its own defaults for the rest. Any other
        // type belongs to a different codec.
        if (options.GetType() == typeof(DocumentReadOptions))
        {
            rejection = null;
            return true;
        }

        rejection = DocumentReadResult.InvalidOptions(codecName, typeof(TOptions), options.GetType());
        return false;
    }

    /// <summary>The write-side counterpart of <see cref="TryValidateOptions{TOptions}(DocumentReadOptions, string, out TOptions, out DocumentReadResult)"/>.</summary>
    protected static bool TryValidateOptions<TOptions>(
        DocumentWriteOptions options,
        string codecName,
        out TOptions? typed,
        out DocumentWriteResult? rejection)
        where TOptions : DocumentWriteOptions
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options is TOptions match)
        {
            typed = match;
            rejection = null;
            return true;
        }

        typed = null;

        if (options.GetType() == typeof(DocumentWriteOptions))
        {
            rejection = null;
            return true;
        }

        rejection = DocumentWriteResult.InvalidOptions(codecName, typeof(TOptions), options.GetType());
        return false;
    }
}
