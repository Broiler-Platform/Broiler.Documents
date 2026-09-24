using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Security;

/// <summary>
/// The PDF half of the public-key security handler (ISO 32000-1 §7.6.4,
/// ISO 32000-2 §7.6.5): finding the recipient envelopes, deriving each crypt
/// filter's key from the seed an envelope yields, and reading the permissions
/// it carries. Opening an envelope is a composed
/// <see cref="IPdfRecipientDecryptor"/>'s.
/// </summary>
/// <remarks>
/// <para>
/// The sub-filters <c>adbe.pkcs7.s3</c> and <c>adbe.pkcs7.s4</c> keep their
/// recipient lists in the encryption dictionary and encrypt everything with
/// RC4. <c>adbe.pkcs7.s5</c> keeps them in each crypt filter, whose method may
/// be RC4, AES-128 or - under ISO 32000-2 - AES-256.
/// </para>
/// <para>
/// A key is the SHA-1 digest (SHA-256 for AES-256) of the seed, every recipient
/// list's bytes in order, and four bytes of 0xFF when the metadata is left
/// unencrypted, cut to the key's length. The envelope's content is the 20-byte
/// seed followed by the permissions, read as a big-endian 32-bit value.
/// </para>
/// <para>
/// <b>The evidence here is thinner than for the standard handler, and is
/// recorded as such.</b> No document encrypted by another producer for a
/// certificate has been read; the tests build their own envelopes with the
/// platform's <c>EnvelopedCms</c>, which proves the codec and those tests agree
/// and not that either agrees with Acrobat. The byte order of the permissions
/// is the reading most exposed to that gap.
/// </para>
/// </remarks>
internal static class PdfPublicKeySecurityHandler
{
    /// <summary>A seed and permissions: what one opened envelope yields.</summary>
    private readonly record struct Envelope(byte[] Seed, int Permissions);

    /// <summary>What an envelope's content may be at most, told to the decryptor.</summary>
    private const int MaxEnvelopeContent = 4096;

    public static PdfSecurityResult Open(
        PdfObjectStore store,
        PdfDictionary encrypt,
        int encryptionObject,
        IPdfRecipientDecryptor? recipients,
        int maxAttempts)
    {
        if (recipients is null)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionRecipientNotComposed,
                "The document is encrypted for certificate recipients, and no recipient decryptor is composed to open an envelope. Nothing was decrypted.");
        }

        int version = PdfSecurity.Integer(store, encrypt["V"], 0);
        int attempts = 0;

        if (version is 1 or 2)
            return OpenWithoutCryptFilters(store, encrypt, encryptionObject, recipients, version, ref attempts, maxAttempts);

        if (version is not (4 or 5))
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionUnsupported,
                string.Create(CultureInfo.InvariantCulture,
                    $"The document uses the public-key security handler at /V {version}, which this build does not open. Nothing was decrypted."));
        }

        PdfSecurity.CryptFilterSet? filters = PdfSecurity.ReadCryptFilters(store, encrypt, version, out string? problem);
        if (filters is null)
            return PdfSecurityResult.Failed(PdfDiagnosticCodes.EncryptionUnsupported, problem!);

        // Every defined filter is opened now rather than when a stream first
        // names it, so a document that cannot be opened is refused before any
        // of it is read. A filter no envelope opens stays unusable, and only a
        // stream or string that selects it is lost.
        var named = new Dictionary<string, PdfCryptFilter>(StringComparer.Ordinal);
        Envelope? documentEnvelope = null;
        bool documentMetadataEncrypted = true;

        foreach ((string name, PdfCryptMethod method) in filters.Methods)
        {
            if (method == PdfCryptMethod.Identity)
            {
                named[name] = PdfCryptFilter.Identity;
                continue;
            }

            PdfDictionary definition = filters.Dictionaries[name];
            List<byte[]> lists = RecipientLists(store, definition["Recipients"]);
            bool encryptMetadata = store.Resolve(definition["EncryptMetadata"]) is not PdfBoolean { Value: false };

            Envelope? envelope = OpenFirst(lists, recipients, store.Budget, ref attempts, maxAttempts);
            if (envelope is null)
            {
                named[name] = new PdfCryptFilter(method, null);
                continue;
            }

            int keyLength = method switch
            {
                PdfCryptMethod.AesV3 => 32,
                PdfCryptMethod.AesV2 => 16,
                _ => KeyBytes(filters.Lengths.GetValueOrDefault(name, 128)),
            };

            if (keyLength <= 0)
            {
                return PdfSecurityResult.Failed(
                    PdfDiagnosticCodes.EncryptionUnsupported,
                    "A crypt filter of the document declares a key length the public-key handler does not allow. Nothing was decrypted.");
            }

            byte[] key = DeriveKey(envelope.Value.Seed, lists, encryptMetadata, method == PdfCryptMethod.AesV3, keyLength);
            named[name] = new PdfCryptFilter(method, key);

            // The permissions and the metadata answer are the document's, and
            // the document's filter is the one its streams use.
            if (name == filters.Streams || documentEnvelope is null)
            {
                documentEnvelope = envelope;
                documentMetadataEncrypted = encryptMetadata;
            }
        }

        PdfCryptFilter streams = PdfSecurity.Select(named, filters.Streams);
        PdfCryptFilter strings = PdfSecurity.Select(named, filters.Strings);
        PdfCryptFilter embedded = PdfSecurity.Select(named, filters.EmbeddedFiles);

        if (documentEnvelope is null || !streams.IsUsable || !strings.IsUsable)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionRecipientNotFound,
                "The composed recipient decryptor holds no key that opens any of the document's recipient envelopes. Nothing was decrypted.");
        }

        var warnings = new List<(string, string)>();
        PdfSecurity.NotePartialEncryption(warnings, version, streams, strings);

        PdfCryptFilter reported = streams.Method == PdfCryptMethod.Identity ? strings : streams;
        var decryptor = new PdfDecryptor(named, streams, strings, embedded, documentMetadataEncrypted, store.Budget, store.Diagnostics, store.Resolve);
        var info = new PdfEncryptionInfo(
            PdfSecurityHandlerKind.PublicKey,
            version,
            0,
            PdfSecurity.CipherOf(reported),
            PdfSecurity.KeyBits(reported),
            PdfAccessLevel.Recipient,
            PdfSecurity.PermissionsFrom(documentEnvelope.Value.Permissions, revision: 3),
            documentMetadataEncrypted,
            userPasswordEmpty: false);

        PdfSecurityResult result = PdfSecurityResult.Opened(decryptor, info, encryptionObject);
        result.Warnings.AddRange(warnings);
        return result;
    }

    /// <summary>The sub-filters before crypt filters: one recipient list set, RC4 throughout.</summary>
    private static PdfSecurityResult OpenWithoutCryptFilters(
        PdfObjectStore store,
        PdfDictionary encrypt,
        int encryptionObject,
        IPdfRecipientDecryptor recipients,
        int version,
        ref int attempts,
        int maxAttempts)
    {
        int keyLength = version == 1 ? 5 : KeyBytes(PdfSecurity.Integer(store, encrypt["Length"], 40));
        if (keyLength <= 0)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionUnsupported,
                "The document's encryption dictionary declares a key length the public-key handler does not allow. Nothing was decrypted.");
        }

        List<byte[]> lists = RecipientLists(store, encrypt["Recipients"]);
        Envelope? envelope = OpenFirst(lists, recipients, store.Budget, ref attempts, maxAttempts);
        if (envelope is null)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionRecipientNotFound,
                "The composed recipient decryptor holds no key that opens any of the document's recipient envelopes. Nothing was decrypted.");
        }

        var filter = new PdfCryptFilter(PdfCryptMethod.Rc4, DeriveKey(envelope.Value.Seed, lists, encryptMetadata: true, sha256: false, keyLength));
        var decryptor = new PdfDecryptor(
            new Dictionary<string, PdfCryptFilter>(StringComparer.Ordinal), filter, filter, filter, true, store.Budget, store.Diagnostics, store.Resolve);
        var info = new PdfEncryptionInfo(
            PdfSecurityHandlerKind.PublicKey,
            version,
            0,
            PdfCipher.Rc4,
            keyLength * 8,
            PdfAccessLevel.Recipient,
            PdfSecurity.PermissionsFrom(envelope.Value.Permissions, revision: 3),
            metadataEncrypted: true,
            userPasswordEmpty: false);

        return PdfSecurityResult.Opened(decryptor, info, encryptionObject);
    }

    /// <summary>
    /// Opens the first envelope a held key opens. The lists are in the order the
    /// document gives them, and a recipient in more than one list takes the
    /// permissions of the first (ISO 32000-1 Table 27).
    /// </summary>
    private static Envelope? OpenFirst(
        List<byte[]> lists,
        IPdfRecipientDecryptor recipients,
        PdfWorkBudget budget,
        ref int attempts,
        int maxAttempts)
    {
        foreach (byte[] list in lists)
        {
            if (++attempts > maxAttempts)
                return null;

            budget.ThrowIfCancelled();
            budget.ChargeWork(list.Length / 64 + 64);

            byte[]? content;
            try
            {
                content = recipients.Open(list, new PdfRecipientContext(MaxEnvelopeContent, budget.Cancellation));
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
            {
                content = null;
            }

            if (content is null || content.Length < 20 || content.Length > MaxEnvelopeContent)
                continue;

            // Without the four permission bytes nothing is granted, which is the
            // reading that cannot extract more than the document allowed.
            int permissions = content.Length >= 24 ? BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(20, 4)) : 0;
            return new Envelope(content[..20], permissions);
        }

        return null;
    }

    private static byte[] DeriveKey(byte[] seed, List<byte[]> lists, bool encryptMetadata, bool sha256, int keyLength)
    {
        using var hash = IncrementalHash.CreateHash(sha256 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1);
        hash.AppendData(seed);
        foreach (byte[] list in lists)
            hash.AppendData(list);
        if (!encryptMetadata)
            hash.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);

        return hash.GetHashAndReset()[..keyLength];
    }

    /// <summary>
    /// The recipient lists: one string, or an array of them, each a DER-encoded
    /// envelope.
    /// </summary>
    private static List<byte[]> RecipientLists(PdfObjectStore store, PdfObject? value)
    {
        var lists = new List<byte[]>();
        switch (store.Resolve(value))
        {
            case PdfString single:
                lists.Add(single.Bytes);
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                {
                    if (store.Resolve(entry) is PdfString list)
                        lists.Add(list.Bytes);
                }

                break;
        }

        return lists;
    }

    /// <summary>
    /// A public-key key length in bytes. The handler states lengths in bits
    /// ("128 means 128"), and a value of 16 or less is taken as bytes, which
    /// producers write as well. Zero outside 40 to 128 bits.
    /// </summary>
    private static int KeyBytes(int declared)
    {
        int bytes = declared <= 16 ? declared : declared / 8;
        return bytes is >= 5 and <= 16 ? bytes : 0;
    }
}
