using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Security;

/// <summary>The outcome of opening an encrypted document.</summary>
internal sealed class PdfSecurityResult
{
    private PdfSecurityResult()
    {
    }

    /// <summary>True when the document opened and <see cref="Decryptor"/> is set.</summary>
    public bool IsOpen => Decryptor is not null;

    /// <summary>The rejection's code when the document did not open.</summary>
    public string? Code { get; private init; }

    public string? Message { get; private init; }

    public PdfDecryptor? Decryptor { get; private init; }

    public PdfEncryptionInfo? Info { get; private init; }

    /// <summary>
    /// The object number of the encryption dictionary, which is never decrypted,
    /// or -1 when the trailer holds it directly.
    /// </summary>
    public int EncryptionObject { get; private init; } = -1;

    /// <summary>What opening noticed that the read should report and survive.</summary>
    public List<(string Code, string Message)> Warnings { get; } = [];

    public static PdfSecurityResult Failed(string code, string message) => new() { Code = code, Message = message };

    public static PdfSecurityResult Opened(PdfDecryptor decryptor, PdfEncryptionInfo info, int encryptionObject) =>
        new() { Decryptor = decryptor, Info = info, EncryptionObject = encryptionObject };
}

/// <summary>
/// Opens an encrypted document: reads its encryption dictionary, authenticates
/// with what the read was given, and builds the decryptor the object store runs
/// every later object through.
/// </summary>
/// <remarks>
/// <para>
/// This runs after the cross-reference data is known and before any object that
/// could be ciphertext is resolved - the order ADR 0015 keeps from ADR 0009.
/// Everything it reads is exempt from encryption by the format: the encryption
/// dictionary, the crypt filter dictionaries inside it, and the file identifier.
/// </para>
/// <para>
/// What opens, and under which record: the standard handler at revisions 2, 3
/// and 4 (ISO 32000-1, IP-015) and 6 (ISO 32000-2, IP-015 under IP-002); the
/// public-key handler (IP-025) through a composed recipient decryptor.
/// Revision 5 - an Adobe extension deprecated by ISO 32000-2 - and the
/// unpublished algorithm of <c>/V 3</c> are refused by name.
/// </para>
/// </remarks>
internal static class PdfSecurity
{
    /// <summary>The most envelopes a public-key document may have opened on its behalf.</summary>
    private const int MaxRecipientAttempts = 256;

    public static PdfSecurityResult Open(
        PdfObjectStore store,
        PdfDecryptionCredentials? credentials,
        IPdfRecipientDecryptor? recipients)
    {
        PdfDictionary? encrypt = store.ReadEncryptionDictionary(out int encryptionObject);
        if (encrypt is null)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionUnsupported,
                "The document is encrypted, and its encryption dictionary could not be read. Nothing was decrypted.");
        }

        string? filter = Name(store, encrypt["Filter"]);
        string? subFilter = Name(store, encrypt["SubFilter"]);

        if (filter == "Standard")
            return OpenStandard(store, encrypt, encryptionObject, credentials);

        if (filter == "Adobe.PubSec" || subFilter is "adbe.pkcs7.s3" or "adbe.pkcs7.s4" or "adbe.pkcs7.s5")
            return PdfPublicKeySecurityHandler.Open(store, encrypt, encryptionObject, recipients, MaxRecipientAttempts);

        return PdfSecurityResult.Failed(
            PdfDiagnosticCodes.EncryptionUnsupported,
            $"The document is encrypted with a security handler this build does not implement{Describe(filter)}. The standard and public-key handlers are the ones it opens; nothing was decrypted.");
    }

    // ---- the standard handler ---------------------------------------------------

    private static PdfSecurityResult OpenStandard(
        PdfObjectStore store,
        PdfDictionary encrypt,
        int encryptionObject,
        PdfDecryptionCredentials? credentials)
    {
        int version = Integer(store, encrypt["V"], 0);
        int revision = Integer(store, encrypt["R"], 0);

        if (revision == 5)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionUnsupported,
                "The document uses revision 5 of the standard security handler, an Adobe extension that ISO 32000-2 deprecated and that no approved record covers (IP-003). Nothing was decrypted.");
        }

        bool legacy = revision is >= 2 and <= 4 && version is 1 or 2 or 4;
        bool revision6 = revision == 6 && version == 5;
        if (!legacy && !revision6)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionUnsupported,
                string.Create(CultureInfo.InvariantCulture,
                    $"The document uses the standard security handler at /V {version} /R {revision}, a combination this build does not open. Revisions 2, 3, 4 and 6 are the ones it reads; nothing was decrypted."));
        }

        // A method this build does not implement is named before anything is
        // checked that a document using it could get right: with it, no
        // password would open the document anyway.
        CryptFilterSet? filters = ReadCryptFilters(store, encrypt, version, out string? filterProblem);
        if (filters is null)
            return PdfSecurityResult.Failed(PdfDiagnosticCodes.EncryptionUnsupported, filterProblem!);

        byte[]? owner = Bytes(store, encrypt["O"]);
        byte[]? user = Bytes(store, encrypt["U"]);
        int stored = legacy ? 32 : 48;
        if (owner is null || user is null || owner.Length < stored || user.Length < stored)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionUnsupported,
                "The document's encryption dictionary lacks the password verification values its revision needs. Nothing was decrypted.");
        }

        int permissions = Permissions(store, encrypt["P"]);
        bool encryptMetadata = version < 4 || store.Resolve(encrypt["EncryptMetadata"]) is not PdfBoolean { Value: false };

        int keyLength = KeyLength(store, encrypt, version, filters);
        if (keyLength <= 0)
        {
            return PdfSecurityResult.Failed(
                PdfDiagnosticCodes.EncryptionUnsupported,
                "The document's encryption dictionary declares a key length the standard security handler does not allow. Nothing was decrypted.");
        }

        byte[]? fileKey;
        PdfAccessLevel access;
        bool userPasswordEmpty;

        if (legacy)
        {
            var parameters = new PdfLegacyKeyParameters(
                revision, keyLength, owner!, user!, permissions, FileIdentifier(store), encryptMetadata);

            (fileKey, access) = Authenticate(
                credentials,
                password => PdfPasswordEncoding.LegacyCandidates(password),
                candidate => PdfStandardSecurityHandler.AuthenticateOwner(candidate, parameters),
                candidate => PdfStandardSecurityHandler.AuthenticateUser(candidate, parameters));

            userPasswordEmpty = fileKey is not null &&
                (access == PdfAccessLevel.User && (credentials is null || credentials.Password.Length == 0)
                 || PdfStandardSecurityHandler.AuthenticateUser([], parameters) is not null);
        }
        else
        {
            byte[]? ownerKey = Bytes(store, encrypt["OE"]);
            byte[]? userKey = Bytes(store, encrypt["UE"]);
            if (ownerKey is null || userKey is null || ownerKey.Length < 32 || userKey.Length < 32)
            {
                return PdfSecurityResult.Failed(
                    PdfDiagnosticCodes.EncryptionUnsupported,
                    "The document's revision 6 encryption dictionary lacks the encrypted file keys it needs. Nothing was decrypted.");
            }

            var parameters = new PdfRevision6KeyParameters(owner!, user!, ownerKey, userKey);
            PdfWorkBudget budget = store.Budget;

            (fileKey, access) = Authenticate(
                credentials,
                password => PdfPasswordEncoding.Revision6Candidates(password),
                candidate => PdfStandardSecurityHandler.AuthenticateOwner6(candidate, parameters, budget),
                candidate => PdfStandardSecurityHandler.AuthenticateUser6(candidate, parameters, budget));

            userPasswordEmpty = fileKey is not null &&
                (access == PdfAccessLevel.User && (credentials is null || credentials.Password.Length == 0)
                 || PdfStandardSecurityHandler.AuthenticateUser6([], parameters, budget) is not null);
        }

        if (fileKey is null)
        {
            return credentials is null || credentials.Password.Length == 0
                ? PdfSecurityResult.Failed(
                    PdfDiagnosticCodes.EncryptionPasswordRequired,
                    "The document is protected by a user password, and the read supplied none. Nothing was decrypted.")
                : PdfSecurityResult.Failed(
                    PdfDiagnosticCodes.EncryptionPasswordIncorrect,
                    "The supplied password is neither the document's user password nor its owner password. Nothing was decrypted.");
        }

        var warnings = new List<(string, string)>();
        PdfPermissions granted = access == PdfAccessLevel.Owner
            ? PdfPermissions.All
            : PermissionsFrom(permissions, revision);

        if (revision6)
        {
            // /P is outside the encryption, so revision 6 states it a second
            // time inside it. Where the two disagree, someone changed one of
            // them, and only what both grant can be trusted.
            (int Permissions, bool EncryptMetadata)? sealedPermissions =
                Bytes(store, encrypt["Perms"]) is { } perms ? PdfStandardSecurityHandler.ReadPermissions(fileKey, perms) : null;

            if (sealedPermissions is null)
            {
                warnings.Add((PdfDiagnosticCodes.EncryptionPermissionsInconsistent,
                    "The document's encrypted copy of its permissions (/Perms) is missing or does not decrypt to a valid record, so the unprotected /P was honoured as it stands."));
            }
            else if (sealedPermissions.Value.Permissions != permissions || sealedPermissions.Value.EncryptMetadata != encryptMetadata)
            {
                warnings.Add((PdfDiagnosticCodes.EncryptionPermissionsInconsistent,
                    "The document's encrypted copy of its permissions (/Perms) disagrees with /P or /EncryptMetadata; only what both grant was honoured."));
                if (access != PdfAccessLevel.Owner)
                    granted &= PermissionsFrom(sealedPermissions.Value.Permissions, revision);
            }
        }

        PdfCryptFilter keyed = legacy && version < 4
            ? new PdfCryptFilter(PdfCryptMethod.Rc4, fileKey)
            : PdfCryptFilter.Identity;

        var named = new Dictionary<string, PdfCryptFilter>(StringComparer.Ordinal);
        foreach ((string name, PdfCryptMethod method) in filters.Methods)
            named[name] = new PdfCryptFilter(method, method == PdfCryptMethod.Identity ? null : fileKey);

        PdfCryptFilter streams = version < 4 ? keyed : Select(named, filters.Streams);
        PdfCryptFilter strings = version < 4 ? keyed : Select(named, filters.Strings);
        PdfCryptFilter embedded = version < 4 ? keyed : Select(named, filters.EmbeddedFiles);

        NotePartialEncryption(warnings, version, streams, strings);

        var decryptor = new PdfDecryptor(named, streams, strings, embedded, encryptMetadata, store.Budget, store.Diagnostics, store.Resolve);
        var info = new PdfEncryptionInfo(
            PdfSecurityHandlerKind.Standard,
            version,
            revision,
            CipherOf(streams.Method == PdfCryptMethod.Identity ? strings : streams),
            KeyBits(streams.Method == PdfCryptMethod.Identity ? strings : streams),
            access,
            granted,
            encryptMetadata,
            userPasswordEmpty);

        PdfSecurityResult result = PdfSecurityResult.Opened(decryptor, info, encryptionObject);
        result.Warnings.AddRange(warnings);
        return result;
    }

    /// <summary>
    /// Tries each byte form of the supplied password - or the empty password,
    /// when none was supplied - as the owner password first, whose authority is
    /// the wider, and then as the user password.
    /// </summary>
    private static (byte[]? Key, PdfAccessLevel Access) Authenticate(
        PdfDecryptionCredentials? credentials,
        Func<string, IReadOnlyList<byte[]>> encode,
        Func<byte[], byte[]?> asOwner,
        Func<byte[], byte[]?> asUser)
    {
        IReadOnlyList<byte[]> candidates = encode(credentials?.Password ?? string.Empty);

        foreach (byte[] candidate in candidates)
        {
            if (asOwner(candidate) is { } key)
                return (key, PdfAccessLevel.Owner);
        }

        foreach (byte[] candidate in candidates)
        {
            if (asUser(candidate) is { } key)
                return (key, PdfAccessLevel.User);
        }

        return (null, PdfAccessLevel.User);
    }

    // ---- crypt filters ------------------------------------------------------------

    /// <summary>The crypt filters a <c>/V 4</c> or <c>/V 5</c> dictionary defines, and which ones it selects.</summary>
    internal sealed class CryptFilterSet
    {
        public Dictionary<string, PdfCryptMethod> Methods { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> Lengths { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PdfDictionary> Dictionaries { get; } = new(StringComparer.Ordinal);

        public string Streams { get; set; } = "Identity";

        public string Strings { get; set; } = "Identity";

        public string EmbeddedFiles { get; set; } = "Identity";
    }

    /// <summary>
    /// Reads <c>/CF</c>, <c>/StmF</c>, <c>/StrF</c> and <c>/EFF</c>. Before
    /// <c>/V 4</c> there are none and the set is empty. Null, with the reason,
    /// when a filter names a method this build does not implement.
    /// </summary>
    internal static CryptFilterSet? ReadCryptFilters(PdfObjectStore store, PdfDictionary encrypt, int version, out string? problem)
    {
        problem = null;
        var set = new CryptFilterSet();
        if (version < 4)
            return set;

        if (store.Resolve(encrypt["CF"]) is PdfDictionary definitions)
        {
            foreach (string name in definitions.Keys)
            {
                if (name == "Identity" || store.Resolve(definitions[name]) is not PdfDictionary definition)
                    continue;

                string method = Name(store, definition["CFM"]) ?? "None";
                PdfCryptMethod? mapped = method switch
                {
                    "None" => PdfCryptMethod.Identity,
                    "V2" when version == 4 => PdfCryptMethod.Rc4,
                    "AESV2" when version == 4 => PdfCryptMethod.AesV2,
                    "AESV3" when version == 5 => PdfCryptMethod.AesV3,
                    _ => null,
                };

                if (mapped is null)
                {
                    problem = $"The document's crypt filter uses a method this build does not implement at /V {version.ToString(CultureInfo.InvariantCulture)}{Describe(method)}. Nothing was decrypted.";
                    return null;
                }

                set.Methods[name] = mapped.Value;
                set.Dictionaries[name] = definition;
                if (store.Resolve(definition["Length"]) is PdfNumber length)
                    set.Lengths[name] = length.ToInt32();
            }
        }

        set.Streams = Name(store, encrypt["StmF"]) ?? "Identity";
        set.Strings = Name(store, encrypt["StrF"]) ?? "Identity";
        set.EmbeddedFiles = Name(store, encrypt["EFF"]) ?? set.Streams;

        foreach (string selected in new[] { set.Streams, set.Strings, set.EmbeddedFiles })
        {
            if (selected != "Identity" && !set.Methods.ContainsKey(selected))
            {
                problem = "The document selects a crypt filter its encryption dictionary does not define. Nothing was decrypted.";
                return null;
            }
        }

        set.Methods["Identity"] = PdfCryptMethod.Identity;
        return set;
    }

    /// <summary>
    /// The file key's length in bytes: 5 for <c>/V 1</c>, <c>/Length</c> for
    /// <c>/V 2</c>, the selected crypt filter's for <c>/V 4</c> - 16 for AES -
    /// and 32 for <c>/V 5</c>. Zero for a length outside 40 to 128 bits.
    /// </summary>
    private static int KeyLength(PdfObjectStore store, PdfDictionary encrypt, int version, CryptFilterSet filters)
    {
        switch (version)
        {
            case 1:
                return 5;
            case 5:
                return 32;
        }

        if (version == 4)
        {
            foreach (string selected in new[] { filters.Streams, filters.Strings })
            {
                if (filters.Methods.GetValueOrDefault(selected) == PdfCryptMethod.AesV2)
                    return 16;
            }

            foreach (string selected in new[] { filters.Streams, filters.Strings })
            {
                if (filters.Lengths.TryGetValue(selected, out int declared))
                    return Bytes(declared);
            }
        }

        int bits = Integer(store, encrypt["Length"], version == 4 ? 128 : 40);
        return bits is >= 40 and <= 128 && bits % 8 == 0 ? bits / 8 : 0;

        // The standard handler states a crypt filter's length in bytes ("16
        // means 128"), and producers write bits there as often as not.
        static int Bytes(int declared)
        {
            int bytes = declared <= 16 ? declared : declared / 8;
            return bytes is >= 5 and <= 16 ? bytes : 0;
        }
    }

    internal static PdfCryptFilter Select(Dictionary<string, PdfCryptFilter> named, string name) =>
        named.TryGetValue(name, out PdfCryptFilter? filter) ? filter : PdfCryptFilter.Identity;

    internal static void NotePartialEncryption(List<(string, string)> warnings, int version, PdfCryptFilter streams, PdfCryptFilter strings)
    {
        if (version < 4)
            return;

        bool streamsClear = streams.Method == PdfCryptMethod.Identity;
        bool stringsClear = strings.Method == PdfCryptMethod.Identity;
        if (!streamsClear && !stringsClear)
            return;

        warnings.Add((PdfDiagnosticCodes.EncryptionPartiallyUnencrypted, (streamsClear, stringsClear) switch
        {
            (true, true) => "The document declares encryption but selects the Identity crypt filter for both strings and streams, so nothing in it is encrypted and anyone could have changed any of it.",
            (true, false) => "The document selects the Identity crypt filter for its streams, so its content streams are not encrypted and anyone could have changed them.",
            _ => "The document selects the Identity crypt filter for its strings, so they are not encrypted and anyone could have changed them.",
        }));
    }

    // ---- what a read reports ------------------------------------------------------

    /// <summary>The Info diagnostic's text: what opened the document, and under what terms.</summary>
    public static string Describe(PdfEncryptionInfo info)
    {
        var text = new StringBuilder("The document was encrypted with the ");
        text.Append(info.Handler == PdfSecurityHandlerKind.Standard ? "standard security handler" : "public-key security handler");
        if (info.Revision > 0)
            text.Append(CultureInfo.InvariantCulture, $", revision {info.Revision}");

        text.Append(info.Cipher switch
        {
            PdfCipher.Rc4 => string.Create(CultureInfo.InvariantCulture, $", RC4 {info.KeyBits}-bit"),
            PdfCipher.Aes128 => ", AES 128-bit",
            PdfCipher.Aes256 => ", AES 256-bit",
            _ => ", with nothing actually encrypted",
        });

        text.Append(info.Access switch
        {
            PdfAccessLevel.Owner => ". It was opened with the owner password, so every permission applies.",
            PdfAccessLevel.Recipient => ". It was opened with a recipient's key, under the permissions of that recipient's list.",
            _ when info.UserPasswordEmpty => ". It opens for anyone - the user password is empty - under the permissions it declares.",
            _ => ". It was opened with the user password, under the permissions it declares.",
        });

        if (info.Access != PdfAccessLevel.Owner)
        {
            text.Append(" Granted: ").Append(Permissions(info.Permissions)).Append('.');
        }

        if (!info.MetadataEncrypted)
            text.Append(" Its metadata stream was left unencrypted by the document itself.");

        text.Append(" The document read here is not encrypted: anything written from it is written in the clear unless the writer protects it.");
        return text.ToString();
    }

    private static string Permissions(PdfPermissions permissions)
    {
        var parts = new List<string>();
        void Add(PdfPermissions flag, string name) => parts.Add(name + (permissions.HasFlag(flag) ? " yes" : " no"));

        Add(PdfPermissions.Print, "print");
        Add(PdfPermissions.PrintHighQuality, "print faithfully");
        Add(PdfPermissions.Modify, "modify");
        Add(PdfPermissions.CopyOrExtract, "copy and extract");
        Add(PdfPermissions.Annotate, "annotate");
        Add(PdfPermissions.FillForms, "fill forms");
        Add(PdfPermissions.Assemble, "assemble");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The permissions <c>/P</c> grants. Revision 2 has only bits 3 to 6, and
    /// each of the later bits refines one of them, so a revision 2 document's
    /// answer for a later bit is the answer of the bit it refines.
    /// </summary>
    internal static PdfPermissions PermissionsFrom(int p, int revision)
    {
        var granted = (PdfPermissions)(p & (int)PdfPermissions.All);
        if (revision != 2)
            return granted;

        granted &= PdfPermissions.Print | PdfPermissions.Modify | PdfPermissions.CopyOrExtract | PdfPermissions.Annotate;
        if (granted.HasFlag(PdfPermissions.Print))
            granted |= PdfPermissions.PrintHighQuality;
        if (granted.HasFlag(PdfPermissions.Modify))
            granted |= PdfPermissions.Assemble;
        if (granted.HasFlag(PdfPermissions.CopyOrExtract))
            granted |= PdfPermissions.ExtractForAccessibility;
        if (granted.HasFlag(PdfPermissions.Annotate))
            granted |= PdfPermissions.FillForms;
        return granted;
    }

    internal static PdfCipher CipherOf(PdfCryptFilter filter) => filter.Method switch
    {
        PdfCryptMethod.Rc4 => PdfCipher.Rc4,
        PdfCryptMethod.AesV2 => PdfCipher.Aes128,
        PdfCryptMethod.AesV3 => PdfCipher.Aes256,
        _ => PdfCipher.None,
    };

    internal static int KeyBits(PdfCryptFilter filter) => filter.Method switch
    {
        PdfCryptMethod.AesV2 => 128,
        PdfCryptMethod.AesV3 => 256,
        PdfCryptMethod.Rc4 => (filter.FileKey?.Length ?? 0) * 8,
        _ => 0,
    };

    // ---- small readers ------------------------------------------------------------

    /// <summary>The first element of the trailer's <c>/ID</c>, or nothing when the file has none.</summary>
    internal static byte[] FileIdentifier(PdfObjectStore store) =>
        store.Resolve(store.Trailer["ID"]) is PdfArray { Count: > 0 } id && store.Resolve(id[0]) is PdfString first
            ? first.Bytes
            : [];

    /// <summary>
    /// <c>/P</c> as the signed 32-bit value the key computation hashes. Producers
    /// write it signed and unsigned alike, so an unsigned value is folded back.
    /// </summary>
    internal static int Permissions(PdfObjectStore store, PdfObject? value)
    {
        if (store.Resolve(value) is not PdfNumber number)
            return 0;

        long raw = number.ToInt64();
        return unchecked((int)(uint)(raw & 0xFFFFFFFF));
    }

    internal static byte[]? Bytes(PdfObjectStore store, PdfObject? value) => (store.Resolve(value) as PdfString)?.Bytes;

    internal static string? Name(PdfObjectStore store, PdfObject? value) => (store.Resolve(value) as PdfName)?.Value;

    internal static int Integer(PdfObjectStore store, PdfObject? value, int fallback) =>
        store.Resolve(value) is PdfNumber number ? number.ToInt32() : fallback;

    /// <summary>
    /// A handler or method name for a message, when it is short and plain enough
    /// to be one. A name is a construct the format defines, not document content,
    /// but a hostile file can put anything in one, and this keeps the message a
    /// statement about the file rather than a place to carry text out of it.
    /// </summary>
    internal static string Describe(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 48)
            return string.Empty;

        foreach (char c in name)
        {
            if (c is < '!' or > '~')
                return string.Empty;
        }

        return " (/" + name + ")";
    }
}
