using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Broiler.Documents.Pdf.Security;

/// <summary>What one revision 2-4 encryption dictionary states for the key computations.</summary>
internal readonly record struct PdfLegacyKeyParameters(
    int Revision,
    int KeyLength,
    byte[] Owner,
    byte[] User,
    int Permissions,
    byte[] FileIdentifier,
    bool EncryptMetadata);

/// <summary>What one revision 6 encryption dictionary states for the key computations.</summary>
internal readonly record struct PdfRevision6KeyParameters(
    byte[] Owner,
    byte[] User,
    byte[] OwnerKey,
    byte[] UserKey);

/// <summary>
/// The password algorithms of the standard security handler: ISO 32000-1
/// §7.6.3.3 for revisions 2 to 4, and ISO 32000-2 §7.6.4.3 for revision 6.
/// </summary>
/// <remarks>
/// <para>
/// Authentication and key recovery are one step. A password is right exactly
/// when it reproduces the verification value the document stores, and the key
/// computed on the way is the file key - so no key is ever used that the
/// document did not confirm, and a wrong password cannot produce plausible
/// plaintext.
/// </para>
/// <para>
/// The hashes and AES are the .NET runtime's (SRC-011); RC4 is
/// <see cref="Rc4"/>. Comparisons against stored values are fixed-time, which a
/// local reader does not strictly need and costs nothing.
/// </para>
/// </remarks>
internal static class PdfStandardSecurityHandler
{
    /// <summary>
    /// The 32 bytes ISO 32000-1 §7.6.3.3 (Algorithm 2, step a) pads every
    /// password with. An arbitrary constant: an implementation reproduces it or
    /// opens nothing, so it is transcribed, and recorded as such in SRC-025.
    /// </summary>
    private static ReadOnlySpan<byte> PasswordPadding =>
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    // ---- revisions 2 to 4 ------------------------------------------------------

    /// <summary>
    /// Authenticates <paramref name="password"/> as the user password
    /// (Algorithm 6), returning the file key or null.
    /// </summary>
    public static byte[]? AuthenticateUser(ReadOnlySpan<byte> password, in PdfLegacyKeyParameters parameters)
    {
        byte[] key = ComputeFileKey(password, parameters);

        // Algorithm 4 for revision 2 checks all 32 bytes; Algorithm 5 for later
        // revisions produces 16 and leaves the rest of /U arbitrary.
        byte[] check = parameters.Revision == 2 ? Rc4.Apply(key, PasswordPadding) : ComputeUserCheck(key, parameters.FileIdentifier);
        int length = parameters.Revision == 2 ? 32 : 16;

        return CryptographicOperations.FixedTimeEquals(check.AsSpan(0, length), parameters.User.AsSpan(0, length))
            ? key
            : null;
    }

    /// <summary>
    /// Authenticates <paramref name="password"/> as the owner password
    /// (Algorithm 7): the owner password decrypts <c>/O</c> to the padded user
    /// password, which must then authenticate. Returns the file key or null.
    /// </summary>
    public static byte[]? AuthenticateOwner(ReadOnlySpan<byte> password, in PdfLegacyKeyParameters parameters)
    {
        byte[] key = ComputeOwnerKey(password, parameters.Revision, parameters.KeyLength);

        byte[] userPassword;
        if (parameters.Revision == 2)
        {
            userPassword = Rc4.Apply(key, parameters.Owner.AsSpan(0, 32));
        }
        else
        {
            // Algorithm 3 ran RC4 twenty times with the key XORed by 0..19;
            // undoing it runs the same keys in the opposite order.
            userPassword = parameters.Owner[..32];
            for (int i = 19; i >= 0; i--)
                userPassword = Rc4.Apply(Xor(key, i), userPassword);
        }

        // Already 32 bytes, so the padding step leaves it as it is.
        return AuthenticateUser(userPassword, parameters);
    }

    /// <summary>The file key for a password (Algorithm 2).</summary>
    internal static byte[] ComputeFileKey(ReadOnlySpan<byte> password, in PdfLegacyKeyParameters parameters)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(Pad(password));
        md5.AppendData(parameters.Owner.AsSpan(0, 32));

        Span<byte> permissions = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(permissions, parameters.Permissions);
        md5.AppendData(permissions);
        md5.AppendData(parameters.FileIdentifier);

        if (parameters.Revision >= 4 && !parameters.EncryptMetadata)
            md5.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);

        byte[] hash = md5.GetHashAndReset();
        int length = parameters.Revision == 2 ? 5 : parameters.KeyLength;

        if (parameters.Revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = MD5.HashData(hash.AsSpan(0, length));
        }

        return hash[..length];
    }

    /// <summary>The RC4 key the owner password yields (Algorithm 3, steps a-d).</summary>
    internal static byte[] ComputeOwnerKey(ReadOnlySpan<byte> password, int revision, int keyLength)
    {
        byte[] hash = MD5.HashData(Pad(password));
        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = MD5.HashData(hash);
        }

        return hash[..(revision == 2 ? 5 : keyLength)];
    }

    /// <summary>The first 16 bytes of <c>/U</c> for revisions 3 and 4 (Algorithm 5, steps b-e).</summary>
    internal static byte[] ComputeUserCheck(byte[] key, byte[] fileIdentifier)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(PasswordPadding);
        md5.AppendData(fileIdentifier);

        byte[] value = Rc4.Apply(key, md5.GetHashAndReset());
        for (int i = 1; i <= 19; i++)
            value = Rc4.Apply(Xor(key, i), value);

        return value;
    }

    /// <summary>Pads or truncates a password to 32 bytes (Algorithm 2, step a).</summary>
    internal static byte[] Pad(ReadOnlySpan<byte> password)
    {
        var padded = new byte[32];
        int length = Math.Min(password.Length, 32);
        password[..length].CopyTo(padded);
        PasswordPadding[..(32 - length)].CopyTo(padded.AsSpan(length));
        return padded;
    }

    private static byte[] Xor(byte[] key, int value)
    {
        var result = new byte[key.Length];
        for (int i = 0; i < key.Length; i++)
            result[i] = (byte)(key[i] ^ value);
        return result;
    }

    // ---- revision 6 ------------------------------------------------------------

    /// <summary>
    /// Authenticates <paramref name="password"/> as the owner password
    /// (Algorithm 12), returning the file key recovered from <c>/OE</c> or null.
    /// </summary>
    public static byte[]? AuthenticateOwner6(ReadOnlySpan<byte> password, in PdfRevision6KeyParameters parameters, PdfWorkBudget budget)
    {
        ReadOnlySpan<byte> user = parameters.User.AsSpan(0, 48);
        byte[] hash = Hash(password, parameters.Owner.AsSpan(32, 8), user, budget);
        if (!CryptographicOperations.FixedTimeEquals(hash, parameters.Owner.AsSpan(0, 32)))
            return null;

        byte[] intermediate = Hash(password, parameters.Owner.AsSpan(40, 8), user, budget);
        return DecryptFileKey(intermediate, parameters.OwnerKey);
    }

    /// <summary>
    /// Authenticates <paramref name="password"/> as the user password
    /// (Algorithm 11), returning the file key recovered from <c>/UE</c> or null.
    /// </summary>
    public static byte[]? AuthenticateUser6(ReadOnlySpan<byte> password, in PdfRevision6KeyParameters parameters, PdfWorkBudget budget)
    {
        byte[] hash = Hash(password, parameters.User.AsSpan(32, 8), [], budget);
        if (!CryptographicOperations.FixedTimeEquals(hash, parameters.User.AsSpan(0, 32)))
            return null;

        byte[] intermediate = Hash(password, parameters.User.AsSpan(40, 8), [], budget);
        return DecryptFileKey(intermediate, parameters.UserKey);
    }

    /// <summary>
    /// Reads <c>/Perms</c> (Algorithm 13): the permissions and the metadata flag,
    /// AES-256 encrypted under the file key in ECB mode, or null when the bytes
    /// <c>adb</c> that mark a correct decryption are not there.
    /// </summary>
    public static (int Permissions, bool EncryptMetadata)? ReadPermissions(byte[] fileKey, byte[] perms)
    {
        if (perms.Length < 16)
            return null;

        using Aes aes = Aes.Create();
        aes.Key = fileKey;
        byte[] plain = aes.DecryptEcb(perms.AsSpan(0, 16), PaddingMode.None);

        if (plain[9] != (byte)'a' || plain[10] != (byte)'d' || plain[11] != (byte)'b')
            return null;

        return (BinaryPrimitives.ReadInt32LittleEndian(plain), plain[8] == (byte)'T');
    }

    /// <summary>
    /// The revision 6 hash (Algorithm 2.B): SHA-256 of the input, then rounds
    /// of AES-128-CBC over 64 repetitions of password, hash and user key, each
    /// round's hash chosen by the encryption's first 16 bytes modulo 3. At least
    /// 64 rounds, then on until the last byte of the encryption is no more than
    /// the round count less 32 - which a byte reaches by round 288 at the latest.
    /// </summary>
    internal static byte[] Hash(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> userKey, PdfWorkBudget budget)
    {
        byte[] input = new byte[password.Length + salt.Length + userKey.Length];
        password.CopyTo(input);
        salt.CopyTo(input.AsSpan(password.Length));
        userKey.CopyTo(input.AsSpan(password.Length + salt.Length));

        byte[] k = SHA256.HashData(input);
        using Aes aes = Aes.Create();

        int round = 0;
        while (true)
        {
            int unit = password.Length + k.Length + userKey.Length;
            byte[] repeated = new byte[unit * 64];
            for (int i = 0; i < 64; i++)
            {
                Span<byte> slot = repeated.AsSpan(i * unit, unit);
                password.CopyTo(slot);
                k.CopyTo(slot[password.Length..]);
                userKey.CopyTo(slot[(password.Length + k.Length)..]);
            }

            aes.Key = k[..16];
            byte[] e = aes.EncryptCbc(repeated, k.AsSpan(16, 16), PaddingMode.None);
            budget.ChargeWork(repeated.Length / 64 + 1);

            // 256 is 1 modulo 3, so the first 16 bytes read as one big-endian
            // number leave the same remainder as their sum.
            int sum = 0;
            for (int i = 0; i < 16; i++)
                sum += e[i];

            k = (sum % 3) switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e),
            };

            round++;
            if (round >= 64 && e[^1] <= round - 32)
                break;
        }

        return k[..32];
    }

    private static byte[] DecryptFileKey(byte[] intermediate, byte[] encryptedKey)
    {
        using Aes aes = Aes.Create();
        aes.Key = intermediate;
        return aes.DecryptCbc(encryptedKey.AsSpan(0, 32), new byte[16], PaddingMode.None);
    }
}
