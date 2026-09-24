using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Images;

/// <summary>A CIE XYZ triple, as ICC profiles state colorants and white points.</summary>
internal readonly record struct IccXyz(double X, double Y, double Z)
{
    /// <summary>
    /// The CIE D50 illuminant the profile connection space is defined against,
    /// for a profile that states none a reader could use.
    /// </summary>
    public static IccXyz D50 { get; } = new(0.9642, 1.0, 0.8249);

    public bool IsUsable => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) && X > 0 && Y > 0 && Z > 0;
}

/// <summary>
/// One ICC profile, read far enough to find its tags: the header, the tag
/// table, and bounded access to each tag's bytes.
/// </summary>
/// <remarks>
/// <para>
/// Written from the structure ICC.1 / ISO 15076-1 defines (SRC-022). Nothing
/// here is a table from the specification: the header fields and the tag
/// table are offsets and signatures the format defines, and every number the
/// conversion uses comes out of the profile being read.
/// </para>
/// <para>
/// A profile is untrusted input. Every tag is an offset and a length into the
/// profile, and each is checked against the bytes actually present before it
/// is used; the size the header declares may trim trailing bytes but never
/// reach past the ones the document carried.
/// </para>
/// </remarks>
internal sealed class IccProfile
{
    private const int HeaderLength = 128;

    private const uint Acsp = 0x61637370;

    private readonly byte[] _data;
    private readonly Dictionary<uint, (int Offset, int Length)> _tags;

    private IccProfile(byte[] data, int version, int components, bool labConnection, IccXyz illuminant, Dictionary<uint, (int Offset, int Length)> tags)
    {
        _data = data;
        Version = version;
        Components = components;
        LabConnection = labConnection;
        Illuminant = illuminant;
        _tags = tags;
    }

    /// <summary>The profile's major version: 2 or 4.</summary>
    public int Version { get; }

    /// <summary>How many components the profile's own colour space has: 1, 3, or 4.</summary>
    public int Components { get; }

    /// <summary>True where the connection space is CIELAB rather than CIEXYZ.</summary>
    public bool LabConnection { get; }

    /// <summary>The illuminant the connection space is relative to - D50 in every profile a reader meets.</summary>
    public IccXyz Illuminant { get; }

    /// <summary>
    /// Reads the header and the tag table, or declines with the construct that
    /// stopped the read.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> profile, out IccProfile? parsed, out string? declined)
    {
        parsed = null;
        declined = null;

        if (profile.Length < HeaderLength + 4)
        {
            declined = "bytes too short to be an ICC profile";
            return false;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(profile[36..]) != Acsp)
        {
            declined = "bytes that are not an ICC profile";
            return false;
        }

        uint declared = BinaryPrimitives.ReadUInt32BigEndian(profile);
        if (declared < HeaderLength + 4 || declared > (uint)profile.Length)
        {
            declined = "a profile whose declared size does not fit the bytes the document carries";
            return false;
        }

        int length = (int)declared;
        int version = profile[8];
        if (version is not (2 or 4))
        {
            declined = "a profile of a version this reader does not convert";
            return false;
        }

        switch (Signature(profile, 12))
        {
            case "scnr" or "mntr" or "prtr" or "spac":
                break;
            default:
                declined = "a device-link, abstract, or named-colour profile";
                return false;
        }

        int components = Signature(profile, 16) switch
        {
            "GRAY" => 1,
            "RGB " => 3,
            "CMYK" => 4,
            _ => 0,
        };

        if (components == 0)
        {
            declined = "a profile over a colour space this reader does not convert";
            return false;
        }

        bool lab;
        switch (Signature(profile, 20))
        {
            case "XYZ ":
                lab = false;
                break;
            case "Lab ":
                lab = true;
                break;
            default:
                declined = "a profile with a connection space this reader does not convert";
                return false;
        }

        IccXyz illuminant = ReadXyz(profile, 68);
        if (!illuminant.IsUsable)
            illuminant = IccXyz.D50;

        uint count = BinaryPrimitives.ReadUInt32BigEndian(profile[HeaderLength..]);
        if (count > (uint)((length - HeaderLength - 4) / 12))
        {
            declined = "a profile whose tag table runs past its end";
            return false;
        }

        var tags = new Dictionary<uint, (int Offset, int Length)>((int)count);
        for (int i = 0; i < count; i++)
        {
            int entry = HeaderLength + 4 + (i * 12);
            uint signature = BinaryPrimitives.ReadUInt32BigEndian(profile[entry..]);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(profile[(entry + 4)..]);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(profile[(entry + 8)..]);

            if ((ulong)offset + size > (ulong)length || size < 8)
            {
                declined = "a profile whose tags run past its end";
                return false;
            }

            // The first entry for a signature is the one a reader takes.
            tags.TryAdd(signature, ((int)offset, (int)size));
        }

        parsed = new IccProfile(profile[..length].ToArray(), version, components, lab, illuminant, tags);
        return true;
    }

    /// <summary>The bytes of one tag, from its type signature on, or false where the profile has none.</summary>
    public bool TryTag(string signature, out ReadOnlySpan<byte> tag)
    {
        if (_tags.TryGetValue(SignatureOf(signature), out (int Offset, int Length) at))
        {
            tag = _data.AsSpan(at.Offset, at.Length);
            return true;
        }

        tag = default;
        return false;
    }

    /// <summary>Whether the profile carries a tag.</summary>
    public bool Has(string signature) => _tags.ContainsKey(SignatureOf(signature));

    /// <summary>
    /// An <c>XYZ</c>-typed tag's first value - a colorant or a white point - or
    /// false where the tag is absent or is not of that type.
    /// </summary>
    public bool TryXyzTag(string signature, out IccXyz value)
    {
        value = default;
        if (!TryTag(signature, out ReadOnlySpan<byte> tag) || tag.Length < 20 || Signature(tag, 0) != "XYZ ")
            return false;

        value = ReadXyz(tag, 8);
        return double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    }

    /// <summary>The four characters of a signature at <paramref name="at"/>.</summary>
    public static string Signature(ReadOnlySpan<byte> data, int at) =>
        string.Create(4, data.Slice(at, 4).ToArray(), static (chars, bytes) =>
        {
            for (int i = 0; i < 4; i++)
                chars[i] = (char)bytes[i];
        });

    /// <summary>A signed 15.16 fixed-point number.</summary>
    public static double S15Fixed16(ReadOnlySpan<byte> data, int at) =>
        BinaryPrimitives.ReadInt32BigEndian(data[at..]) / 65536.0;

    private static IccXyz ReadXyz(ReadOnlySpan<byte> data, int at) =>
        new(S15Fixed16(data, at), S15Fixed16(data, at + 4), S15Fixed16(data, at + 8));

    private static uint SignatureOf(string signature) =>
        ((uint)signature[0] << 24) | ((uint)signature[1] << 16) | ((uint)signature[2] << 8) | signature[3];
}
