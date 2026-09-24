using System;

namespace Broiler.Documents.Pdf.Security;

/// <summary>
/// The RC4 stream cipher, which ISO 32000-1 §7.6.2 uses for security handlers
/// of revision 2 to 4 and for the key computations of those revisions.
/// </summary>
/// <remarks>
/// <para>
/// The .NET runtime has no RC4, so it is written here: a 256-byte permutation
/// scheduled from the key, then a keystream XORed into the data. Encrypting and
/// decrypting are the same operation. Written from the algorithm's public
/// description (SRC-024), not from any implementation.
/// </para>
/// <para>
/// RC4 is broken as a cipher and is here only because documents made with it
/// have to be read (IP-015). Nothing in this codec encrypts with it.
/// </para>
/// </remarks>
internal static class Rc4
{
    public static byte[] Apply(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        if (key.IsEmpty || key.Length > 256)
            throw new ArgumentException("An RC4 key is 1 to 256 bytes.", nameof(key));

        Span<byte> state = stackalloc byte[256];
        for (int i = 0; i < 256; i++)
            state[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[data.Length];
        int x = 0;
        int y = 0;
        for (int k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[k] = (byte)(data[k] ^ state[(state[x] + state[y]) & 0xFF]);
        }

        return output;
    }
}
