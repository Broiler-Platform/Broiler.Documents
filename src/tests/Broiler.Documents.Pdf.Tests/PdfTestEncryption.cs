using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Encrypts fixtures the way a producer does: the encryption dictionary, the
/// file identifier, and every string and stream under its object's key.
/// </summary>
/// <remarks>
/// <para>
/// Written in the test suite, from the same clauses as the codec's decryption
/// but as separate code running the other way - the hashes, RC4 and the
/// revision 6 hash are implemented again here rather than borrowed - so a slip
/// in either shows up as a disagreement. What it cannot catch is a misreading
/// of the specification shared by both, and the tests say so: a document this
/// class encrypts proves the codec and this class agree. That they agree with
/// other producers rests on the files LibreOffice made for revisions 3 and 6,
/// which are checked outside the tree (the approved-sources similarity log).
/// </para>
/// <para>
/// Everything is deterministic: identifiers, salts, initialization vectors and
/// the revision 6 file key come from a seeded generator, so a fixture is the
/// same bytes on every run.
/// </para>
/// </remarks>
internal sealed class PdfTestEncryption
{
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    private readonly Random _random;

    private PdfTestEncryption(int seed)
    {
        _random = new Random(seed);
        FileIdentifier = Next(16);
    }

    /// <summary>What streams and strings are encrypted with.</summary>
    public Method StreamMethod { get; private set; }

    public Method StringMethod { get; private set; }

    public byte[] FileKey { get; private set; } = [];

    public byte[] FileIdentifier { get; }

    /// <summary>The body of the <c>/Encrypt</c> dictionary, ready to write.</summary>
    public string Dictionary { get; private set; } = string.Empty;

    /// <summary>False when the metadata stream is left in the clear.</summary>
    public bool EncryptMetadata { get; private set; } = true;

    /// <summary>When false, the builder leaves the file identifier out of the trailer.</summary>
    public bool WriteIdentifier { get; set; } = true;

    public enum Method
    {
        Identity,
        Rc4,
        Aes128,
        Aes256,
    }

    /// <summary>Permissions granting everything, as a producer usually writes them.</summary>
    public const int AllPermissions = -4;

    /// <summary>Every permission but bit 5, copy and extract.</summary>
    public const int NoExtraction = -4 & ~(1 << 4);

    /// <summary>
    /// Revisions 2 and 3 with RC4: <c>/V 1</c> at 40 bits, <c>/V 2</c> above.
    /// </summary>
    public static PdfTestEncryption Rc4(int revision, int keyBits, string user, string owner, int permissions = AllPermissions, int seed = 1) =>
        Rc4(revision, keyBits, Latin1(user), Latin1(owner), permissions, seed);

    public static PdfTestEncryption Rc4(int revision, int keyBits, byte[] user, byte[] owner, int permissions = AllPermissions, int seed = 1)
    {
        var encryption = new PdfTestEncryption(seed);
        int version = keyBits == 40 && revision == 2 ? 1 : 2;
        int length = revision == 2 ? 5 : keyBits / 8;
        encryption.Legacy(revision, length, user, owner, permissions, encryptMetadata: true);
        encryption.StreamMethod = encryption.StringMethod = Method.Rc4;
        encryption.Dictionary = string.Create(CultureInfo.InvariantCulture,
            $"<< /Filter /Standard /V {version} /R {revision} /Length {keyBits} /P {permissions} /O <{encryption.OwnerHex}> /U <{encryption.UserHex}> >>");
        return encryption;
    }

    /// <summary>Revision 4 with one crypt filter, AES-128 or RC4-128.</summary>
    public static PdfTestEncryption Revision4(bool aes, string user, string owner, int permissions = AllPermissions, bool encryptMetadata = true, string streams = "StdCF", string strings = "StdCF", int seed = 1)
    {
        var encryption = new PdfTestEncryption(seed);
        encryption.Legacy(4, 16, Latin1(user), Latin1(owner), permissions, encryptMetadata);
        Method method = aes ? Method.Aes128 : Method.Rc4;
        encryption.StreamMethod = streams == "Identity" ? Method.Identity : method;
        encryption.StringMethod = strings == "Identity" ? Method.Identity : method;
        encryption.EncryptMetadata = encryptMetadata;
        string metadata = encryptMetadata ? string.Empty : " /EncryptMetadata false";
        encryption.Dictionary = string.Create(CultureInfo.InvariantCulture,
            $"<< /Filter /Standard /V 4 /R 4 /Length 128 /P {permissions} /O <{encryption.OwnerHex}> /U <{encryption.UserHex}>" +
            $" /CF << /StdCF << /Type /CryptFilter /CFM /{(aes ? "AESV2" : "V2")} /AuthEvent /DocOpen /Length 16 >> >> /StmF /{streams} /StrF /{strings}{metadata} >>");
        return encryption;
    }

    /// <summary>Revision 6: AES-256 under a random file key (ISO 32000-2).</summary>
    public static PdfTestEncryption Revision6(string user, string owner, int permissions = AllPermissions, bool encryptMetadata = true, int? sealedPermissions = null, int seed = 1) =>
        Revision6(Encoding.UTF8.GetBytes(user), Encoding.UTF8.GetBytes(owner), permissions, encryptMetadata, sealedPermissions, seed);

    public static PdfTestEncryption Revision6(byte[] user, byte[] owner, int permissions = AllPermissions, bool encryptMetadata = true, int? sealedPermissions = null, int seed = 1)
    {
        var encryption = new PdfTestEncryption(seed)
        {
            StreamMethod = Method.Aes256,
            StringMethod = Method.Aes256,
            EncryptMetadata = encryptMetadata,
        };

        encryption.FileKey = encryption.Next(32);

        // Algorithms 8 and 9, forwards: a hash and two salts per password, and
        // the file key encrypted under a second hash of each.
        byte[] userValidation = encryption.Next(8);
        byte[] userKeySalt = encryption.Next(8);
        byte[] u = [.. Hash6(user, userValidation, []), .. userValidation, .. userKeySalt];
        byte[] ue = AesCbcNoPadding(Hash6(user, userKeySalt, []), encryption.FileKey);

        byte[] ownerValidation = encryption.Next(8);
        byte[] ownerKeySalt = encryption.Next(8);
        byte[] o = [.. Hash6(owner, ownerValidation, u), .. ownerValidation, .. ownerKeySalt];
        byte[] oe = AesCbcNoPadding(Hash6(owner, ownerKeySalt, u), encryption.FileKey);

        // Algorithm 10: /P again, sealed under the file key.
        byte[] perms = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(perms, sealedPermissions ?? permissions);
        perms[4] = perms[5] = perms[6] = perms[7] = 0xFF;
        perms[8] = (byte)(encryptMetadata ? 'T' : 'F');
        perms[9] = (byte)'a';
        perms[10] = (byte)'d';
        perms[11] = (byte)'b';
        encryption.Next(4).CopyTo(perms, 12);
        using (Aes aes = Aes.Create())
        {
            aes.Key = encryption.FileKey;
            perms = aes.EncryptEcb(perms, PaddingMode.None);
        }

        string metadata = encryptMetadata ? string.Empty : " /EncryptMetadata false";
        encryption.Dictionary = string.Create(CultureInfo.InvariantCulture,
            $"<< /Filter /Standard /V 5 /R 6 /Length 256 /P {permissions} /O <{Hex(o)}> /U <{Hex(u)}> /OE <{Hex(oe)}> /UE <{Hex(ue)}> /Perms <{Hex(perms)}>" +
            $" /CF << /StdCF << /Type /CryptFilter /CFM /AESV3 /AuthEvent /DocOpen /Length 32 >> >> /StmF /StdCF /StrF /StdCF{metadata} >>");
        return encryption;
    }

    /// <summary>
    /// A document encrypted for certificate recipients: the caller supplies the
    /// dictionary, the file key, and the method the key is used with.
    /// </summary>
    public static PdfTestEncryption PublicKey(string dictionary, byte[] fileKey, Method method, bool encryptMetadata = true, int seed = 1) =>
        new(seed)
        {
            Dictionary = dictionary,
            FileKey = fileKey,
            StreamMethod = method,
            StringMethod = method,
            EncryptMetadata = encryptMetadata,
        };

    /// <summary>Replaces the dictionary, to state something the algorithms did not produce.</summary>
    public PdfTestEncryption WithDictionary(string dictionary)
    {
        Dictionary = dictionary;
        return this;
    }

    public byte[] EncryptString(byte[] plain, int objectNumber, int generation) =>
        Encrypt(StringMethod, plain, objectNumber, generation);

    public byte[] EncryptStream(byte[] plain, int objectNumber, int generation) =>
        Encrypt(StreamMethod, plain, objectNumber, generation);

    private byte[] Encrypt(Method method, byte[] plain, int objectNumber, int generation) => method switch
    {
        Method.Identity => plain,
        Method.Rc4 => ApplyRc4(ObjectKey(objectNumber, generation, aes: false), plain),
        Method.Aes128 => EncryptAes(ObjectKey(objectNumber, generation, aes: true), plain),
        _ => EncryptAes(FileKey, plain),
    };

    private byte[] EncryptAes(byte[] key, byte[] plain)
    {
        byte[] iv = Next(16);
        using Aes aes = Aes.Create();
        aes.Key = key;
        return [.. iv, .. aes.EncryptCbc(plain, iv, PaddingMode.PKCS7)];
    }

    /// <summary>Algorithm 1, forwards: the same key the reader derives.</summary>
    private byte[] ObjectKey(int objectNumber, int generation, bool aes)
    {
        var input = new List<byte>(FileKey)
        {
            (byte)objectNumber,
            (byte)(objectNumber >> 8),
            (byte)(objectNumber >> 16),
            (byte)generation,
            (byte)(generation >> 8),
        };

        if (aes)
            input.AddRange("sAlT"u8.ToArray());

        byte[] hash = MD5.HashData([.. input]);
        return hash[..Math.Min(FileKey.Length + 5, 16)];
    }

    // ---- revisions 2 to 4, forwards ------------------------------------------------

    private string OwnerHex { get; set; } = string.Empty;

    private string UserHex { get; set; } = string.Empty;

    private void Legacy(int revision, int length, byte[] user, byte[] owner, int permissions, bool encryptMetadata)
    {
        // Algorithm 3: /O from the owner password, or the user's when there is none.
        byte[] ownerHash = MD5.HashData(Pad(owner.Length > 0 ? owner : user));
        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                ownerHash = MD5.HashData(ownerHash);
        }

        byte[] ownerKey = ownerHash[..(revision == 2 ? 5 : length)];
        byte[] o = ApplyRc4(ownerKey, Pad(user));
        if (revision >= 3)
        {
            for (int i = 1; i <= 19; i++)
                o = ApplyRc4(ownerKey.Select(b => (byte)(b ^ i)).ToArray(), o);
        }

        // Algorithm 2: the file key.
        var input = new List<byte>(Pad(user));
        input.AddRange(o);
        input.AddRange(BitConverter.GetBytes(permissions));
        input.AddRange(FileIdentifier);
        if (revision >= 4 && !encryptMetadata)
            input.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);

        byte[] key = MD5.HashData([.. input]);
        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                key = MD5.HashData(key[..length]);
        }

        FileKey = key[..length];

        // Algorithms 4 and 5: /U.
        byte[] u;
        if (revision == 2)
        {
            u = ApplyRc4(FileKey, PasswordPadding);
        }
        else
        {
            u = ApplyRc4(FileKey, MD5.HashData([.. PasswordPadding, .. FileIdentifier]));
            for (int i = 1; i <= 19; i++)
                u = ApplyRc4(FileKey.Select(b => (byte)(b ^ i)).ToArray(), u);
            u = [.. u, .. Next(16)];
        }

        OwnerHex = Hex(o);
        UserHex = Hex(u);
    }

    private static byte[] Pad(byte[] password)
    {
        byte[] padded = new byte[32];
        int length = Math.Min(password.Length, 32);
        Array.Copy(password, padded, length);
        Array.Copy(PasswordPadding, 0, padded, length, 32 - length);
        return padded;
    }

    /// <summary>RC4, written again here: the key schedule, then the keystream.</summary>
    private static byte[] ApplyRc4(byte[] key, byte[] data)
    {
        byte[] s = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) % 256;
            (s[i], s[j]) = (s[j], s[i]);
        }

        byte[] output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) % 256;
            j = (j + s[i]) % 256;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) % 256]);
        }

        return output;
    }

    // ---- revision 6, forwards ------------------------------------------------------

    /// <summary>Algorithm 2.B, written again here.</summary>
    private static byte[] Hash6(byte[] password, byte[] salt, byte[] userKey)
    {
        byte[] k = SHA256.HashData([.. password, .. salt, .. userKey]);
        for (int round = 1; ; round++)
        {
            var repeated = new List<byte>();
            for (int i = 0; i < 64; i++)
            {
                repeated.AddRange(password);
                repeated.AddRange(k);
                repeated.AddRange(userKey);
            }

            byte[] e;
            using (Aes aes = Aes.Create())
            {
                aes.Key = k[..16];
                e = aes.EncryptCbc(repeated.ToArray(), k[16..32], PaddingMode.None);
            }

            int selector = (int)(new System.Numerics.BigInteger(e[..16], isUnsigned: true, isBigEndian: true) % 3);
            k = selector switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e),
            };

            if (round >= 64 && e[^1] <= round - 32)
                return k[..32];
        }
    }

    private static byte[] AesCbcNoPadding(byte[] key, byte[] data)
    {
        using Aes aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, new byte[16], PaddingMode.None);
    }

    // ---- small helpers -------------------------------------------------------------

    private byte[] Next(int count)
    {
        byte[] bytes = new byte[count];
        _random.NextBytes(bytes);
        return bytes;
    }

    internal static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
