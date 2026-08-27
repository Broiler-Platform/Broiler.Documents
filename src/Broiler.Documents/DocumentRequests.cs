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
public sealed class DocumentReadRequest
{
    public DocumentReadRequest(
        DocumentInput input,
        DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Options = options ?? DocumentReadOptions.Default;
        CancellationToken = cancellationToken;
    }

    /// <summary>The source, probed and read through the same replayable input.</summary>
    public DocumentInput Input { get; }

    /// <summary>
    /// The options. A codec that needs its own option type validates this before
    /// touching the input and rejects a mismatch rather than downcasting
    /// opportunistically.
    /// </summary>
    public DocumentReadOptions Options { get; }

    /// <summary>The limits in force, taken from the options.</summary>
    public DocumentLimits Limits => Options.Limits;

    public CancellationToken CancellationToken { get; }

    /// <summary>Builds a request over a stream the caller keeps.</summary>
    public static DocumentReadRequest FromStream(
        Stream source,
        DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        new(DocumentInput.FromStream(source), options, cancellationToken);

    /// <summary>Builds a request over bytes the caller already holds.</summary>
    public static DocumentReadRequest FromBytes(
        ReadOnlyMemory<byte> bytes,
        DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        new(DocumentInput.FromBytes(bytes), options, cancellationToken);
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
    public DocumentWriteRequest(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
        if (!destination.CanWrite)
            throw new ArgumentException("A document destination needs a writable stream.", nameof(destination));

        Options = options ?? DocumentWriteOptions.Default;
        CancellationToken = cancellationToken;
    }

    public RichTextDocument Document { get; }

    /// <summary>The destination, owned by the caller.</summary>
    public Stream Destination { get; }

    public DocumentWriteOptions Options { get; }

    public CancellationToken CancellationToken { get; }
}
