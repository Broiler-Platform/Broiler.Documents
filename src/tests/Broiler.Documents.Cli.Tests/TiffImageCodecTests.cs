using Broiler.Documents.Cli.Composition;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// The TIFF strips Word keeps logos in: tdf115094.docx stores two RGB pictures
/// LZW-compressed with horizontal differencing, which the codec refused, so the
/// page drew no picture at all.
/// </summary>
public sealed class TiffImageCodecTests
{
    // A 4 x 2 RGB TIFF, LZW with predictor 2, as an image library writes one.
    private const string LzwPredictedTiff =
        "SUkqACAAAACAP8AAAAwKCP8FBQPsgyDKBw8ADlPNGAgLAAABAwABAAAABAAAAAEBAwABAAAAAgAAAAIBAwADAAAAqgAAAAMBAwABAAAABQAAAAYBAwABAAAAAgAAABEBBAABAAAACAAAABUBAwABAAAAAwAAABYBAwABAAAAAgAAABcBBAABAAAAGAAAABwBAwABAAAAAQAAAD0BAwABAAAAAgAAAAAAAAAIAAgACAA=";

    [Fact]
    public void An_Lzw_Tiff_With_Horizontal_Differencing_Decodes()
    {
        CodecComposition.RegisterImageCodecs();
        using BBitmap bitmap = BBitmap.Decode(Convert.FromBase64String(LzwPredictedTiff));

        Assert.Equal(4, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
        byte[] rgba = bitmap.ToPixelBuffer(copy: false).Rgba;
        byte[][] expected =
        [
            [255, 0, 0], [0, 255, 0], [0, 0, 255], [10, 20, 30],
            [200, 100, 50], [200, 100, 50], [200, 100, 50], [1, 2, 3],
        ];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], rgba.AsSpan(i * 4, 3).ToArray());
            Assert.Equal(255, rgba[i * 4 + 3]);
        }
    }

    [Fact]
    public void Lzw_Codes_Grow_Wider_One_Code_Early()
    {
        // Enough distinct strings to take the table past 511, 1023 and 2047 entries.
        var data = new byte[20000];
        var random = new Random(236);
        random.NextBytes(data);
        for (int i = 0; i < data.Length; i += 7)
            data[i] = (byte)(i % 5);

        Assert.Equal(data, TiffImageCodec.DecodeLzw(EncodeLzw(data), data.Length));
    }

    [Fact]
    public void PackBits_Expands_Runs_And_Literals()
    {
        byte[] packed = [2, 1, 2, 3, unchecked((byte)-3), 9, 0x80, 0, 7];

        Assert.Equal(new byte[] { 1, 2, 3, 9, 9, 9, 9, 7 }, TiffImageCodec.DecodePackBits(packed, 8));
    }

    /// <summary>A TIFF LZW encoder: MSB-first codes, a clear first, early width change, a clear when the table fills.</summary>
    private static byte[] EncodeLzw(byte[] input)
    {
        var output = new List<byte>();
        int buffer = 0, bits = 0, width = 9, next = 258;
        void Emit(int code)
        {
            buffer = (buffer << width) | code;
            bits += width;
            while (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
            buffer &= (1 << bits) - 1;
        }

        var table = new Dictionary<(int, byte), int>();
        Emit(256);
        int prefix = input[0];
        for (int i = 1; i < input.Length; i++)
        {
            byte value = input[i];
            if (table.TryGetValue((prefix, value), out int code))
            {
                prefix = code;
                continue;
            }
            Emit(prefix);
            table[(prefix, value)] = next++;
            if (next == 1 << width && width < 12)
                width++;
            if (next >= 4094)
            {
                Emit(256);
                table.Clear();
                (next, width) = (258, 9);
            }
            prefix = value;
        }
        Emit(prefix);
        Emit(257);
        if (bits > 0)
            output.Add((byte)(buffer << (8 - bits)));
        return [.. output];
    }
}
