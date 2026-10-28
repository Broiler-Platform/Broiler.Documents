using System;
using System.IO;
using System.Threading;
using Broiler.Documents.Model;

namespace Broiler.Documents;

/// <summary>
/// Everything a codec needs to read one document: where the bytes come from,
/// which options apply, and how to stop.
/// </summary>
/// <remarks>
/// <para>
/// Cross-format values live here and nowhere else. Limits and cancellation are
/// the request's, not the format options'; a format option object that tried to
/// carry its own copy would create a precedence question with no good answer, so
/// the contract is one owner rather than a rule about which copy wins
/// (PDF roadmap §6.1).
/// </para>
/// <para>
/// The request does not own <see cref="Input"/>. Whoever created the input
/// disposes it, which keeps a request cheap to build, pass, and discard.
/// </para>
/// </remarks>
public sealed class DocumentReadRequest(DocumentInput input, DocumentReadOptions? options = null, CancellationToken cancellationToken = default, DocumentSourceHints? hints = null)
{

    /// <summary>The source, probed and read through the same replayable input.</summary>
    public DocumentInput Input { get; } = input ?? throw new ArgumentNullException(nameof(input));

    /// <summary>
    /// The options. A codec that needs its own option type validates this before
    /// touching the input and rejects a mismatch rather than downcasting
    /// opportunistically.
    /// </summary>
    public DocumentReadOptions Options { get; } = options ?? DocumentReadOptions.Default;

    /// <summary>The limits in force, taken from the options.</summary>
    public DocumentLimits Limits => Options.Limits;

    /// <summary>Cancellation token to abort the read operation.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>Source hints (filename, MIME media type) that accompanied the read request.</summary>
    public DocumentSourceHints Hints { get; } = hints ?? DocumentSourceHints.Empty;

    /// <summary>Builds a request over a stream the caller keeps.</summary>
    public static DocumentReadRequest FromStream(Stream source, DocumentReadOptions? options = null, CancellationToken cancellationToken = default, DocumentSourceHints? hints = null) =>
        new(DocumentInput.FromStream(source), options, cancellationToken, hints);

    /// <summary>Builds a request over bytes the caller already holds.</summary>
    public static DocumentReadRequest FromBytes(ReadOnlyMemory<byte> bytes, DocumentReadOptions? options = null, CancellationToken cancellationToken = default, DocumentSourceHints? hints = null) =>
        new(DocumentInput.FromBytes(bytes), options, cancellationToken, hints);
}

/// <summary>
/// Everything a codec needs to write one document: the content, the destination,
/// which options apply, and how to stop.
/// </summary>
/// <remarks>
/// The destination is the caller's stream and stays the caller's: a codec neither
/// closes it nor rewinds it. When a write stops after bytes have already reached
/// an unstaged stream, the result says so through
/// <see cref="DocumentDestinationState.PartialDestination"/> rather than
/// pretending the destination is clean.
/// </remarks>
public sealed class DocumentWriteRequest
{
    /// <summary>
    /// Initializes a new instance of <see cref="DocumentWriteRequest"/>.
    /// </summary>
    /// <param name="document">The document to write.</param>
    /// <param name="destination">The destination stream, owned by the caller.</param>
    /// <param name="options">Options governing the write operation. Defaults to <see cref="DocumentWriteOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancellation token to abort the write operation.</param>
    /// <param name="requireAtomic">Whether the write must commit atomically or emit zero bytes.</param>
    /// <param name="isStaged">Whether the destination stream represents a staged target capable of atomic replacement or buffering.</param>
    public DocumentWriteRequest(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null,
        CancellationToken cancellationToken = default,
        bool requireAtomic = false,
        bool isStaged = false)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
        if (!destination.CanWrite)
            throw new ArgumentException("A document destination needs a writable stream.", nameof(destination));

        Options = options ?? DocumentWriteOptions.Default;
        CancellationToken = cancellationToken;
        RequireAtomic = requireAtomic;
        IsStaged = isStaged;
    }

    /// <summary>The document being written.</summary>
    public RichTextDocument Document { get; }

    /// <summary>The destination, owned by the caller.</summary>
    public Stream Destination { get; }

    /// <summary>The options governing the write operation.</summary>
    public DocumentWriteOptions Options { get; }

    /// <summary>Cancellation token to abort the write operation.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Whether the write must commit atomically. When true, the write must either
    /// succeed completely or reject before any bytes reach the destination.
    /// Writing directly to an unstaged stream without staging capability will reject
    /// before output with <see cref="DocumentDiagnosticCodes.CommitAtomicRequired"/>.
    /// </summary>
    public bool RequireAtomic { get; }

    /// <summary>
    /// Whether the destination stream is a staged target (such as a temporary sibling file
    /// or in-memory buffer) that the host or caller will atomically replace upon success.
    /// </summary>
    public bool IsStaged { get; }

    /// <summary>Returns a copy of this request with the specified <see cref="RequireAtomic"/> setting.</summary>
    public DocumentWriteRequest WithRequireAtomic(bool requireAtomic = true) =>
        new(Document, Destination, Options, CancellationToken, requireAtomic, IsStaged);

    /// <summary>Returns a copy of this request with the specified <see cref="IsStaged"/> setting.</summary>
    public DocumentWriteRequest WithStaged(bool isStaged = true) =>
        new(Document, Destination, Options, CancellationToken, RequireAtomic, isStaged);

    /// <summary>Creates a direct write request without staging.</summary>
    public static DocumentWriteRequest Direct(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        new(document, destination, options, cancellationToken, requireAtomic: false, isStaged: false);

    /// <summary>Creates a staged write request where destination is known to be staged.</summary>
    public static DocumentWriteRequest Staged(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null,
        bool requireAtomic = true,
        CancellationToken cancellationToken = default) =>
        new(document, destination, options, cancellationToken, requireAtomic: requireAtomic, isStaged: true);
}
