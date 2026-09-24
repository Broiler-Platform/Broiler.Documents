using System;
using System.Threading;

namespace Broiler.Documents.Pdf.Security;

/// <summary>
/// Opens the recipient envelopes of a document encrypted for certificates (the
/// public-key security handler, ISO 32000-1 §7.6.4), with private keys the
/// composing application holds.
/// </summary>
/// <remarks>
/// <para>
/// Such a document stores, for each list of recipients, a CMS
/// <c>EnvelopedData</c> object (RFC 5652) whose content is a 20-byte seed and
/// four bytes of permissions. The codec does everything that is PDF - finding the
/// envelopes, deriving the file key from the seed, applying the permissions -
/// and asks an implementation of this interface for the one thing it cannot do
/// itself: undo the envelope with a recipient's private key.
/// </para>
/// <para>
/// No implementation ships in this repository, and that is deliberate. The
/// cryptographic message syntax is an external standard of its own (IP-025),
/// and the ready implementation - <c>EnvelopedCms</c> in
/// <c>System.Security.Cryptography.Pkcs</c> - is a package this component's
/// dependency rule keeps out of anything it ships (ADR 0001). A host composes
/// one, typically in a few lines over that type, with the certificates of whoever
/// it runs for. The codec never searches a certificate store itself.
/// </para>
/// <para>
/// An implementation returns null for an envelope none of its keys opens, and
/// for one it cannot parse; the codec catches anything thrown and treats it the
/// same way. It must not return a partial or guessed content.
/// </para>
/// </remarks>
public interface IPdfRecipientDecryptor
{
    /// <summary>
    /// Opens one envelope: a DER-encoded CMS <c>ContentInfo</c> carrying
    /// <c>EnvelopedData</c>.
    /// </summary>
    /// <returns>
    /// The enveloped content, or null when no key this decryptor holds opens it.
    /// </returns>
    byte[]? Open(ReadOnlySpan<byte> envelope, PdfRecipientContext context);
}

/// <summary>The ceiling and cancellation handed to a recipient decryptor.</summary>
public sealed class PdfRecipientContext
{
    public PdfRecipientContext(int maxContentBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxContentBytes);
        MaxContentBytes = maxContentBytes;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// The most content an envelope may yield. What the handler stores is 24
    /// bytes; anything much larger is not an envelope this format made.
    /// </summary>
    public int MaxContentBytes { get; }

    public CancellationToken CancellationToken { get; }
}
