using System;

namespace Broiler.Documents.Pdf;

/// <summary>What an encrypted document was protected with, and what opening it granted.</summary>
/// <remarks>
/// <para>
/// Present on a <see cref="PdfReadResult"/> once a security handler has opened
/// the document, and on a rejection that happened after that - a document whose
/// permissions withhold extraction is refused with the reason stated here - and
/// absent for a document that is not encrypted or could not be opened at all.
/// </para>
/// <para>
/// The document a read returns is never encrypted. A host that saves it writes
/// it in the clear unless it protects the output itself, and one that honours
/// the other permissions - printing, modifying - reads them here: this codec
/// enforces only the one its own output depends on, copying and extracting.
/// </para>
/// </remarks>
public sealed class PdfEncryptionInfo
{
    internal PdfEncryptionInfo(
        PdfSecurityHandlerKind handler,
        int version,
        int revision,
        PdfCipher cipher,
        int keyBits,
        PdfAccessLevel access,
        PdfPermissions permissions,
        bool metadataEncrypted,
        bool userPasswordEmpty)
    {
        Handler = handler;
        Version = version;
        Revision = revision;
        Cipher = cipher;
        KeyBits = keyBits;
        Access = access;
        Permissions = permissions;
        MetadataEncrypted = metadataEncrypted;
        UserPasswordEmpty = userPasswordEmpty;
    }

    /// <summary>The security handler the document names.</summary>
    public PdfSecurityHandlerKind Handler { get; }

    /// <summary>The encryption dictionary's <c>/V</c>: the algorithm family.</summary>
    public int Version { get; }

    /// <summary>
    /// The standard handler's <c>/R</c>, or zero for the public-key handler,
    /// which has no revision number of its own.
    /// </summary>
    public int Revision { get; }

    /// <summary>The cipher the document's streams are encrypted with.</summary>
    public PdfCipher Cipher { get; }

    /// <summary>The length of the key that cipher uses, in bits.</summary>
    public int KeyBits { get; }

    /// <summary>The authority the document was opened with.</summary>
    public PdfAccessLevel Access { get; }

    /// <summary>
    /// What the document permits the reader it opened for: everything, for its
    /// owner, and otherwise what it declares.
    /// </summary>
    public PdfPermissions Permissions { get; }

    /// <summary>
    /// False when the document left its metadata stream unencrypted on purpose
    /// (<c>/EncryptMetadata false</c>), so that a search index can read it.
    /// </summary>
    public bool MetadataEncrypted { get; }

    /// <summary>
    /// True when the document's user password is empty: anyone can open it, and
    /// its protection is only the permissions it declares.
    /// </summary>
    public bool UserPasswordEmpty { get; }

    /// <summary>
    /// Whether the document may be copied from and extracted - which is what
    /// every output of this codec is.
    /// </summary>
    public bool MayExtract => Access == PdfAccessLevel.Owner || Permissions.HasFlag(PdfPermissions.CopyOrExtract);
}

/// <summary>The security handlers ISO 32000 defines.</summary>
public enum PdfSecurityHandlerKind
{
    /// <summary>The password-based standard security handler (<c>/Standard</c>).</summary>
    Standard,

    /// <summary>The certificate-based public-key security handler (<c>/Adobe.PubSec</c>).</summary>
    PublicKey,
}

/// <summary>The ciphers an encrypted document's strings and streams can use.</summary>
public enum PdfCipher
{
    /// <summary>Nothing is encrypted: every crypt filter in use is <c>Identity</c>.</summary>
    None,

    /// <summary>RC4, 40 to 128 bits. Legacy: a document opened with it is not strongly protected.</summary>
    Rc4,

    /// <summary>AES-128 in CBC mode (<c>AESV2</c>).</summary>
    Aes128,

    /// <summary>AES-256 in CBC mode (<c>AESV3</c>).</summary>
    Aes256,
}

/// <summary>The authority an encrypted document was opened with.</summary>
public enum PdfAccessLevel
{
    /// <summary>The user password, which may be empty: the document's permissions apply.</summary>
    User,

    /// <summary>The owner password: every permission applies.</summary>
    Owner,

    /// <summary>A certificate recipient's key: the permissions of that recipient's list apply.</summary>
    Recipient,
}

/// <summary>
/// The user access permissions of ISO 32000-1 Table 22, as flags. The values
/// are the bits' positions in <c>/P</c>, so a flag and its bit are one number.
/// </summary>
[Flags]
public enum PdfPermissions
{
    None = 0,

    /// <summary>Bit 3: print.</summary>
    Print = 1 << 2,

    /// <summary>Bit 4: modify the contents other than by the operations bits 6, 9 and 11 govern.</summary>
    Modify = 1 << 3,

    /// <summary>Bit 5: copy or otherwise extract text and graphics. This codec's output depends on it.</summary>
    CopyOrExtract = 1 << 4,

    /// <summary>Bit 6: add or modify annotations and fill forms.</summary>
    Annotate = 1 << 5,

    /// <summary>Bit 9: fill in existing form fields.</summary>
    FillForms = 1 << 8,

    /// <summary>Bit 10: extract text and graphics in support of accessibility.</summary>
    ExtractForAccessibility = 1 << 9,

    /// <summary>Bit 11: assemble the document - insert, rotate or delete pages.</summary>
    Assemble = 1 << 10,

    /// <summary>Bit 12: print to a faithful digital representation.</summary>
    PrintHighQuality = 1 << 11,

    /// <summary>Every permission.</summary>
    All = Print | Modify | CopyOrExtract | Annotate | FillForms | ExtractForAccessibility | Assemble | PrintHighQuality,
}
