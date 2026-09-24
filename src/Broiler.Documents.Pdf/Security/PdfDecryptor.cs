using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Security;

/// <summary>The methods a crypt filter can name (ISO 32000-1 Table 25, ISO 32000-2 Table 25).</summary>
internal enum PdfCryptMethod
{
    /// <summary>No encryption: the data is plaintext.</summary>
    Identity,

    /// <summary>RC4 under a per-object key (<c>/V2</c>, and every revision before crypt filters).</summary>
    Rc4,

    /// <summary>AES-128-CBC under a per-object key (<c>/AESV2</c>).</summary>
    AesV2,

    /// <summary>AES-256-CBC under the file key itself (<c>/AESV3</c>).</summary>
    AesV3,
}

/// <summary>One crypt filter: a method and the file key it runs under.</summary>
internal sealed class PdfCryptFilter
{
    public PdfCryptFilter(PdfCryptMethod method, byte[]? fileKey)
    {
        Method = method;
        FileKey = fileKey;
    }

    public static PdfCryptFilter Identity { get; } = new(PdfCryptMethod.Identity, null);

    public PdfCryptMethod Method { get; }

    /// <summary>
    /// The key, or null for <c>Identity</c> - and for a public-key crypt filter
    /// whose recipients this read could not open, which is then unusable.
    /// </summary>
    public byte[]? FileKey { get; }

    public bool IsUsable => Method == PdfCryptMethod.Identity || FileKey is not null;
}

/// <summary>
/// Decrypts the objects an encrypted document stores directly in the file, one
/// at a time, as the object store loads them.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-1 §7.6.1 encrypts every string and stream in the file with three
/// exceptions: the file identifier, the strings of the encryption dictionary,
/// and anything inside a stream that is itself encrypted - a content stream's
/// operands and an object stream's members. The object store keeps the first
/// two away from here and never passes the third, so this class decrypts
/// everything it is handed except a cross-reference stream, which the format
/// forbids encrypting (§7.5.8).
/// </para>
/// <para>
/// A stream selects its crypt filter by naming <c>/Crypt</c> first in its
/// filter chain; that stage is consumed here and removed from the dictionary, so
/// the filter pipeline sees what the stream holds once decrypted. Otherwise the
/// metadata stream follows <c>/EncryptMetadata</c>, an embedded file follows
/// <c>/EFF</c>, and every other stream follows <c>/StmF</c>. Strings follow
/// <c>/StrF</c>.
/// </para>
/// </remarks>
internal sealed class PdfDecryptor
{
    private static ReadOnlySpan<byte> AesSalt => "sAlT"u8;

    private readonly IReadOnlyDictionary<string, PdfCryptFilter> _named;
    private readonly PdfCryptFilter _streams;
    private readonly PdfCryptFilter _strings;
    private readonly PdfCryptFilter _embeddedFiles;
    private readonly bool _encryptMetadata;
    private readonly PdfWorkBudget _budget;
    private readonly PdfDiagnosticSink _diagnostics;
    private readonly Func<PdfObject?, PdfObject?> _resolve;

    public PdfDecryptor(
        IReadOnlyDictionary<string, PdfCryptFilter> named,
        PdfCryptFilter streams,
        PdfCryptFilter strings,
        PdfCryptFilter embeddedFiles,
        bool encryptMetadata,
        PdfWorkBudget budget,
        PdfDiagnosticSink diagnostics,
        Func<PdfObject?, PdfObject?> resolve)
    {
        _named = named;
        _streams = streams;
        _strings = strings;
        _embeddedFiles = embeddedFiles;
        _encryptMetadata = encryptMetadata;
        _budget = budget;
        _diagnostics = diagnostics;
        _resolve = resolve;
    }

    /// <summary>
    /// Returns <paramref name="value"/>, the object numbered
    /// <paramref name="objectNumber"/> and <paramref name="generation"/> in its
    /// header, with its strings and its stream data decrypted.
    /// </summary>
    public PdfObject Decrypt(PdfObject value, int objectNumber, int generation)
    {
        if (value is PdfStream stream)
        {
            if (IsType(stream.Dictionary, "XRef"))
                return value;

            DecryptStrings(stream.Dictionary, objectNumber, generation);
            byte[] data = DecryptStreamData(stream, objectNumber, generation);
            return new PdfStream(stream.Dictionary, data);
        }

        if (value is PdfString text)
            return DecryptString(text, objectNumber, generation);

        DecryptStrings(value, objectNumber, generation);
        return value;
    }

    // ---- strings --------------------------------------------------------------

    private void DecryptStrings(PdfObject root, int objectNumber, int generation)
    {
        if (_strings.Method == PdfCryptMethod.Identity)
            return;

        // An explicit stack, as the parser uses: nesting is bounded there, and
        // a walk of what it built should not spend the call stack either.
        var pending = new Stack<PdfObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case PdfDictionary dictionary:
                    foreach (string key in new List<string>(dictionary.Keys))
                    {
                        switch (dictionary[key])
                        {
                            case PdfString text:
                                dictionary[key] = DecryptString(text, objectNumber, generation);
                                break;
                            case PdfDictionary or PdfArray:
                                pending.Push(dictionary[key]!);
                                break;
                        }
                    }

                    break;

                case PdfArray array:
                    for (int i = 0; i < array.Count; i++)
                    {
                        switch (array[i])
                        {
                            case PdfString text:
                                array[i] = DecryptString(text, objectNumber, generation);
                                break;
                            case PdfDictionary or PdfArray:
                                pending.Push(array[i]);
                                break;
                        }
                    }

                    break;
            }
        }
    }

    private PdfString DecryptString(PdfString text, int objectNumber, int generation)
    {
        if (!_strings.IsUsable)
        {
            Malformed("A string's crypt filter could not be opened with the recipient key this read holds; the string was dropped.");
            return new PdfString([], text.IsHexadecimal);
        }

        return new PdfString(Transform(_strings, text.Bytes, objectNumber, generation), text.IsHexadecimal);
    }

    // ---- streams --------------------------------------------------------------

    private byte[] DecryptStreamData(PdfStream stream, int objectNumber, int generation)
    {
        PdfCryptFilter? filter = SelectStreamFilter(stream.Dictionary, stream.RawData);
        if (filter is null || !filter.IsUsable)
        {
            Malformed(filter is null
                ? "A stream named a crypt filter the document does not define; the stream was dropped."
                : "A stream's crypt filter could not be opened with the recipient key this read holds; the stream was dropped.");
            return [];
        }

        return Transform(filter, stream.RawData, objectNumber, generation);
    }

    /// <summary>
    /// The crypt filter for a stream, consuming a leading <c>/Crypt</c> stage.
    /// Null when that stage names a filter the document does not define.
    /// </summary>
    private PdfCryptFilter? SelectStreamFilter(PdfDictionary dictionary, byte[] raw)
    {
        PdfObject? filters = _resolve(dictionary["Filter"]);
        PdfName? first = filters switch
        {
            PdfName single => single,
            PdfArray { Count: > 0 } array => _resolve(array[0]) as PdfName,
            _ => null,
        };

        if (first?.Value == Filters.PdfFilterNames.Crypt)
        {
            string name = CryptFilterName(dictionary, filters!) ?? "Identity";
            RemoveFirstStage(dictionary, filters!);

            if (name == "Identity")
            {
                if (!IsType(dictionary, "Metadata"))
                {
                    _diagnostics.Warning(
                        PdfDiagnosticCodes.EncryptionPartiallyUnencrypted,
                        "A stream in the encrypted document selects the Identity crypt filter, so it is stored unencrypted and anyone could have changed it. It was read as it stands.");
                }

                return PdfCryptFilter.Identity;
            }

            return _named.TryGetValue(name, out PdfCryptFilter? named) ? named : null;
        }

        if (IsType(dictionary, "Metadata"))
        {
            if (!_encryptMetadata)
                return PdfCryptFilter.Identity;

            // Some producers store the XMP packet in the clear although the
            // document declares it encrypted - LibreOffice does, under RC4.
            // Ciphertext that happens to open with a packet's own markup is not
            // a chance worth weighing, so the packet is read as it stands, and
            // said to be unprotected.
            if (IsPlainXmp(raw))
            {
                _diagnostics.Warning(
                    PdfDiagnosticCodes.EncryptionPartiallyUnencrypted,
                    "The document's metadata stream is stored unencrypted although the document declares it encrypted, as some producers write it. It was read as it stands, and anyone could have changed it.");
                return PdfCryptFilter.Identity;
            }
        }

        return IsType(dictionary, "EmbeddedFile") ? _embeddedFiles : _streams;
    }

    private static bool IsPlainXmp(byte[] raw)
    {
        ReadOnlySpan<byte> data = raw;
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            data = data[3..];

        return data.StartsWith("<?xpacket"u8) || data.StartsWith("<x:xmpmeta"u8);
    }

    private string? CryptFilterName(PdfDictionary dictionary, PdfObject filters)
    {
        PdfObject? parms = _resolve(dictionary["DecodeParms"]);
        PdfDictionary? first = filters is PdfArray
            ? parms is PdfArray { Count: > 0 } list ? _resolve(list[0]) as PdfDictionary : null
            : parms as PdfDictionary;

        return (_resolve(first?["Name"]) as PdfName)?.Value;
    }

    // The crypt stage is done once it is applied here. Leaving it in the chain
    // would send the filter pipeline looking for a /Crypt decoder, and the
    // pipeline has none on purpose.
    private void RemoveFirstStage(PdfDictionary dictionary, PdfObject filters)
    {
        if (filters is not PdfArray array)
        {
            dictionary.Remove("Filter");
            dictionary.Remove("DecodeParms");
            return;
        }

        var rest = new PdfArray();
        for (int i = 1; i < array.Count; i++)
            rest.Add(array[i]);

        if (rest.Count == 0)
            dictionary.Remove("Filter");
        else
            dictionary["Filter"] = rest;

        if (_resolve(dictionary["DecodeParms"]) is PdfArray parms)
        {
            var remaining = new PdfArray();
            for (int i = 1; i < parms.Count; i++)
                remaining.Add(parms[i]);

            if (remaining.Count == 0)
                dictionary.Remove("DecodeParms");
            else
                dictionary["DecodeParms"] = remaining;
        }
    }

    // ---- the ciphers ----------------------------------------------------------

    private byte[] Transform(PdfCryptFilter filter, byte[] data, int objectNumber, int generation)
    {
        _budget.ChargeWork(data.Length / 64 + 1);

        return filter.Method switch
        {
            PdfCryptMethod.Rc4 => Rc4.Apply(ObjectKey(filter.FileKey!, objectNumber, generation, aes: false), data),
            PdfCryptMethod.AesV2 => DecryptAes(ObjectKey(filter.FileKey!, objectNumber, generation, aes: true), data),
            PdfCryptMethod.AesV3 => DecryptAes(filter.FileKey!, data),
            _ => data,
        };
    }

    /// <summary>
    /// The key for one object (ISO 32000-1 §7.6.2, Algorithm 1): the file key
    /// extended by the low three bytes of the object number and the low two of
    /// the generation, low byte first - and by the bytes <c>sAlT</c> for AES -
    /// hashed with MD5 and cut to five more bytes than the file key, at most 16.
    /// </summary>
    internal static byte[] ObjectKey(byte[] fileKey, int objectNumber, int generation, bool aes)
    {
        Span<byte> input = stackalloc byte[fileKey.Length + 9];
        fileKey.CopyTo(input);
        int length = fileKey.Length;
        input[length++] = (byte)objectNumber;
        input[length++] = (byte)(objectNumber >> 8);
        input[length++] = (byte)(objectNumber >> 16);
        input[length++] = (byte)generation;
        input[length++] = (byte)(generation >> 8);
        if (aes)
        {
            AesSalt.CopyTo(input[length..]);
            length += AesSalt.Length;
        }

        byte[] hash = MD5.HashData(input[..length]);
        return hash[..Math.Min(fileKey.Length + 5, 16)];
    }

    /// <summary>
    /// AES in CBC mode as the format uses it: a 16-byte initialization vector
    /// first, then whole blocks, padded by the scheme of RFC 8018 §6.1.1.
    /// </summary>
    /// <remarks>
    /// Producers get the edges wrong, so the edges are read leniently and
    /// reported: a trailing partial block is dropped, and padding that does not
    /// check is left on the plaintext rather than guessed at.
    /// </remarks>
    private byte[] DecryptAes(byte[] key, byte[] data)
    {
        if (data.Length < 16)
        {
            // An empty string encrypts to sixteen bytes of vector and a block of
            // padding; less than a vector cannot have come from the cipher.
            if (data.Length > 0)
                Malformed("An AES-encrypted string or stream was shorter than its initialization vector; it was read as empty.");
            return [];
        }

        int whole = (data.Length - 16) / 16 * 16;
        if (data.Length - 16 != whole)
            Malformed("An AES-encrypted string or stream did not end on a block boundary; the partial block was dropped.");

        if (whole == 0)
            return [];

        using Aes aes = Aes.Create();
        aes.Key = key;
        byte[] plain = aes.DecryptCbc(data.AsSpan(16, whole), data.AsSpan(0, 16), PaddingMode.None);

        int pad = plain[^1];
        if (pad is >= 1 and <= 16 && pad <= plain.Length && plain.AsSpan(plain.Length - pad).IndexOfAnyExcept((byte)pad) < 0)
            return plain[..^pad];

        Malformed("An AES-encrypted string or stream carried padding that does not check; its last block was kept as it decrypted.");
        return plain;
    }

    private bool IsType(PdfDictionary dictionary, string type) =>
        _resolve(dictionary["Type"]) is PdfName name && name.Value == type;

    private void Malformed(string message) =>
        _diagnostics.Warning(PdfDiagnosticCodes.EncryptionObjectMalformed, message);
}
