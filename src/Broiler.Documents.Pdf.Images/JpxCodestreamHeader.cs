using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// What a JPEG 2000 codestream's headers declare, read without decoding it.
/// </summary>
/// <remarks>
/// This is the tuple IP-007 is written against. Its most useful field is
/// <see cref="Capability"/>: the <c>Rsiz</c> value separates a Part 1 core
/// codestream, which the row clears, from a Part 2 extended one, which it does
/// not.
/// </remarks>
internal readonly record struct JpxCodestreamHeader(
    int Capability,
    int Width,
    int Height,
    int Components,
    int BitDepth,
    bool Signed,
    bool Subsampled,
    int TileWidth,
    int TileHeight,
    int DecompositionLevels,
    int Layers,
    bool MultipleComponentTransform,
    bool ReversibleTransform,
    bool HasCodingStyle,
    bool IsJp2Container)
{
    /// <summary>True when <c>Rsiz</c> names only Part 1 capabilities.</summary>
    public bool IsPartOneCore => Capability is 0 or 1 or 2;

    /// <summary>The tuple as one phrase, for a diagnostic.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"{Width}x{Height}, {Components} component{(Components == 1 ? string.Empty : "s")}, {BitDepth}-bit {(Signed ? "signed" : "unsigned")}");

        if (Subsampled)
            text.Append(", with subsampled components");

        text.Append(CultureInfo.InvariantCulture, $", tiles of {TileWidth}x{TileHeight}");

        if (HasCodingStyle)
        {
            text.Append(CultureInfo.InvariantCulture,
                $", {DecompositionLevels} decomposition level{(DecompositionLevels == 1 ? string.Empty : "s")}");
            text.Append(ReversibleTransform ? ", 5/3 reversible wavelet" : ", 9/7 irreversible wavelet");
            text.Append(CultureInfo.InvariantCulture, $", {Layers} quality layer{(Layers == 1 ? string.Empty : "s")}");
            if (MultipleComponentTransform)
                text.Append(", component transform applied");
        }

        text.Append(CultureInfo.InvariantCulture, $", Rsiz {Capability}");
        return text.ToString();
    }
}

/// <summary>
/// Reads the marker segments at the head of a JPEG 2000 codestream, and the JP2
/// box structure that may wrap one.
/// </summary>
/// <remarks>
/// <para>
/// Markers only. This walks segment lengths to find <c>SIZ</c> and <c>COD</c>,
/// stops at the first tile, and never touches a packet, a code-block, or the
/// arithmetic coder. It is the same shape as the JPEG frame reader beside it, and
/// for the same reason: knowing what an image <em>is</em> costs nothing like
/// decoding it, and it is what an approval decision needs.
/// </para>
/// <para>
/// It matters more here than for JPEG. A <c>JPXDecode</c> stream's colour space
/// and bit depth are allowed to be absent from the PDF image dictionary precisely
/// because the codestream is authoritative for them, so without reading this a
/// reader cannot say what the image even is.
/// </para>
/// </remarks>
internal static class JpxCodestreamReader
{
    /// <summary>Start of codestream.</summary>
    private const ushort MarkerSoc = 0xFF4F;

    /// <summary>Image and tile size.</summary>
    private const ushort MarkerSiz = 0xFF51;

    /// <summary>Coding style default.</summary>
    private const ushort MarkerCod = 0xFF52;

    /// <summary>Start of tile-part: everything this reader wants is above it.</summary>
    private const ushort MarkerSot = 0xFF90;

    /// <summary>End of codestream.</summary>
    private const ushort MarkerEoc = 0xFFD9;

    /// <summary>Boxes examined before a container is refused as unreasonable.</summary>
    private const int MaxBoxes = 64;

    /// <summary>Marker segments examined before the header is refused.</summary>
    private const int MaxSegments = 256;

    private static ReadOnlySpan<byte> Jp2Signature => [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    public static bool TryRead(ReadOnlySpan<byte> data, out JpxCodestreamHeader header, out string? error)
    {
        header = default;

        bool container = false;
        if (data.Length >= Jp2Signature.Length && data[..Jp2Signature.Length].SequenceEqual(Jp2Signature))
        {
            container = true;
            if (!TryFindCodestream(data, out data, out error))
                return false;
        }

        if (data.Length < 4 || ReadUInt16(data, 0) != MarkerSoc)
        {
            error = "The stream does not begin with a JPEG 2000 codestream or JP2 signature.";
            return false;
        }

        return TryReadMarkers(data, container, out header, out error);
    }

    /// <summary>
    /// Walks the JP2 box structure to the contiguous codestream box. Boxes are a
    /// length-prefixed tree, so this is a bounded walk over the top level.
    /// </summary>
    private static bool TryFindCodestream(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> codestream, out string? error)
    {
        codestream = default;
        int position = 0;

        for (int box = 0; box < MaxBoxes; box++)
        {
            if (position + 8 > data.Length)
            {
                error = "The JP2 container holds no contiguous codestream box.";
                return false;
            }

            long length = ReadUInt32(data, position);
            uint type = ReadUInt32(data, position + 4);
            int headerLength = 8;

            if (length == 1)
            {
                // An extended length follows the type as a 64-bit value.
                if (position + 16 > data.Length)
                {
                    error = "A JP2 box declares an extended length that does not fit the stream.";
                    return false;
                }

                length = (long)BinaryPrimitives.ReadUInt64BigEndian(data.Slice(position + 8, 8));
                headerLength = 16;
            }
            else if (length == 0)
            {
                // Runs to the end of the file.
                length = data.Length - position;
            }

            if (length < headerLength || position + length > data.Length)
            {
                error = "A JP2 box declares a length that does not fit the stream.";
                return false;
            }

            // 'jp2c'
            if (type == 0x6A703263u)
            {
                codestream = data.Slice(position + headerLength, (int)(length - headerLength));
                error = null;
                return true;
            }

            position += (int)length;
        }

        error = "The JP2 container nests further than this reader will walk.";
        return false;
    }

    private static bool TryReadMarkers(
        ReadOnlySpan<byte> data,
        bool container,
        out JpxCodestreamHeader header,
        out string? error)
    {
        header = default;

        int capability = 0, width = 0, height = 0, components = 0, bitDepth = 0;
        int tileWidth = 0, tileHeight = 0, levels = 0, layers = 0;
        bool signed = false, subsampled = false, mct = false, reversible = false;
        bool haveSiz = false, haveCod = false;

        int position = 2;
        for (int segment = 0; segment < MaxSegments; segment++)
        {
            if (position + 2 > data.Length)
                break;

            ushort marker = ReadUInt16(data, position);
            position += 2;

            if (marker is MarkerSot or MarkerEoc)
                break;

            if (marker < 0xFF00)
            {
                error = "The codestream holds a marker segment that is not a marker.";
                return false;
            }

            if (position + 2 > data.Length)
                break;

            int length = ReadUInt16(data, position);
            if (length < 2 || position + length > data.Length)
            {
                error = "A codestream marker segment declares a length that does not fit the stream.";
                return false;
            }

            ReadOnlySpan<byte> body = data.Slice(position + 2, length - 2);

            if (marker == MarkerSiz)
            {
                if (body.Length < 36)
                {
                    error = "The SIZ marker segment is too short to describe an image.";
                    return false;
                }

                capability = ReadUInt16(body, 0);
                long right = ReadUInt32(body, 2);
                long bottom = ReadUInt32(body, 6);
                long left = ReadUInt32(body, 10);
                long top = ReadUInt32(body, 14);
                tileWidth = (int)Math.Min(int.MaxValue, ReadUInt32(body, 18));
                tileHeight = (int)Math.Min(int.MaxValue, ReadUInt32(body, 22));
                components = ReadUInt16(body, 34);

                width = (int)Math.Max(0, right - left);
                height = (int)Math.Max(0, bottom - top);

                if (components <= 0 || body.Length < 36 + (components * 3))
                {
                    error = "The SIZ marker segment declares more components than it describes.";
                    return false;
                }

                // Every component states its own depth and sampling; the tuple
                // reports the first and whether the rest differ.
                bitDepth = (body[36] & 0x7F) + 1;
                signed = (body[36] & 0x80) != 0;
                for (int c = 0; c < components; c++)
                {
                    if (body[36 + (c * 3) + 1] != 1 || body[36 + (c * 3) + 2] != 1)
                        subsampled = true;
                }

                haveSiz = true;
            }
            else if (marker == MarkerCod && body.Length >= 10)
            {
                int codingStyle = body[0];
                layers = ReadUInt16(body, 2);
                mct = body[4] != 0;
                levels = body[5];
                reversible = body[9] != 0;
                haveCod = true;
                _ = codingStyle;
            }

            position += length;
        }

        if (!haveSiz)
        {
            error = "The codestream carries no SIZ marker, so its size is unknown.";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = "The codestream declares an empty image.";
            return false;
        }

        header = new JpxCodestreamHeader(
            capability, width, height, components, bitDepth, signed, subsampled,
            tileWidth, tileHeight, levels, layers, mct, reversible, haveCod, container);
        error = null;
        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
}
