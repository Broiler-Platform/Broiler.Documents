using System;

namespace Broiler.Documents.Pdf.Filters;

/// <summary>
/// The predictor post-processing that FlateDecode and LZWDecode may declare
/// through <c>DecodeParms</c> (clause 7.4.4.4): TIFF predictor 2 and the PNG
/// predictors 10–15.
/// </summary>
/// <remarks>
/// Predictors are a property of the <em>stream</em>, not of the compression
/// algorithm, so they live here rather than inside a filter. That is why LZW got
/// predictor support the moment its filter landed, without a line of change here.
/// </remarks>
/// <remarks>
/// The PNG predictors are per-row: an encoder picks a filter for each row and
/// tags it, which is what "optimum selection" names on the encoding side. A
/// decoder has nothing to select — it honours whichever tag each row carries, so
/// all five are implemented and none is a mode this build can be missing.
/// </remarks>
internal static class PdfPredictor
{
    public const int None = 1;
    public const int Tiff = 2;
    public const int PngNone = 10;

    /// <summary>
    /// Reverses the declared prediction. Returns <see langword="false"/> with a
    /// reason when the parameters or the data do not describe a whole number of
    /// rows — predictors are one of the easiest places to smuggle an
    /// out-of-bounds read, so every arithmetic step is checked before use.
    /// </summary>
    public static bool TryReverse(
        byte[] data,
        int predictor,
        int colors,
        int bitsPerComponent,
        int columns,
        out byte[] result,
        out string? error)
    {
        result = data;
        error = null;

        if (predictor <= None)
            return true;

        if (colors is < 1 or > 32)
        {
            error = "A stream predictor declared an out-of-range /Colors value.";
            return false;
        }

        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
        {
            error = "A stream predictor declared an unsupported /BitsPerComponent value.";
            return false;
        }

        if (columns < 1)
        {
            error = "A stream predictor declared an out-of-range /Columns value.";
            return false;
        }

        long bitsPerPixel = (long)colors * bitsPerComponent;
        long rowBits = bitsPerPixel * columns;
        long rowBytes = (rowBits + 7) / 8;
        if (rowBytes is <= 0 or > int.MaxValue)
        {
            error = "A stream predictor described a row larger than the addressable limit.";
            return false;
        }

        if (predictor == Tiff)
            return TryReverseTiff(data, colors, bitsPerComponent, columns, (int)rowBytes, out result, out error);

        if (predictor < PngNone)
        {
            error = "A stream declared an unknown predictor.";
            return false;
        }

        return TryReversePng(data, (int)rowBytes, Math.Max(1, (int)(bitsPerPixel / 8)), out result, out error);
    }

    /// <summary>
    /// Undoes TIFF predictor 2, which differences each component against the same
    /// component of the pixel to its left.
    /// </summary>
    /// <remarks>
    /// Every component size the format allows is handled. One, two, and four bits
    /// divide eight exactly, so a component of those sizes never straddles a byte
    /// and the same bit accessor serves them and eight-bit components alike;
    /// sixteen-bit components are the one case that spans two bytes and has its
    /// own loop. The arithmetic is modulo the component size in all of them,
    /// which is what makes the difference reversible.
    /// </remarks>
    private static bool TryReverseTiff(
        byte[] data,
        int colors,
        int bitsPerComponent,
        int columns,
        int rowBytes,
        out byte[] result,
        out string? error)
    {
        result = data;
        error = null;

        int rows = data.Length / rowBytes;
        var output = (byte[])data.Clone();
        int componentsPerRow = columns * colors;

        for (int row = 0; row < rows; row++)
        {
            int rowStart = row * rowBytes;

            if (bitsPerComponent == 16)
            {
                int step = colors * 2;
                for (int i = step; i + 1 < rowBytes; i += 2)
                {
                    int previous = (output[rowStart + i - step] << 8) | output[rowStart + i - step + 1];
                    int current = (output[rowStart + i] << 8) | output[rowStart + i + 1];
                    int sum = (current + previous) & 0xFFFF;
                    output[rowStart + i] = (byte)(sum >> 8);
                    output[rowStart + i + 1] = (byte)sum;
                }

                continue;
            }

            int mask = (1 << bitsPerComponent) - 1;
            for (int component = colors; component < componentsPerRow; component++)
            {
                int previous = ReadComponent(output, rowStart, component - colors, bitsPerComponent, rowBytes);
                int current = ReadComponent(output, rowStart, component, bitsPerComponent, rowBytes);
                WriteComponent(output, rowStart, component, bitsPerComponent, (current + previous) & mask, rowBytes);
            }
        }

        result = output;
        return true;
    }

    /// <summary>
    /// One packed component. Only called for sizes that divide a byte, so the
    /// value never spans two of them.
    /// </summary>
    private static int ReadComponent(byte[] data, int rowStart, int component, int bits, int rowBytes)
    {
        int bitOffset = component * bits;
        int index = rowStart + (bitOffset >> 3);
        if (index >= data.Length || (bitOffset >> 3) >= rowBytes)
            return 0;

        int shift = 8 - bits - (bitOffset & 7);
        return (data[index] >> shift) & ((1 << bits) - 1);
    }

    private static void WriteComponent(byte[] data, int rowStart, int component, int bits, int value, int rowBytes)
    {
        int bitOffset = component * bits;
        int index = rowStart + (bitOffset >> 3);
        if (index >= data.Length || (bitOffset >> 3) >= rowBytes)
            return;

        int shift = 8 - bits - (bitOffset & 7);
        int mask = ((1 << bits) - 1) << shift;
        data[index] = (byte)((data[index] & ~mask) | ((value << shift) & mask));
    }

    private static bool TryReversePng(
        byte[] data,
        int rowBytes,
        int bytesPerPixel,
        out byte[] result,
        out string? error)
    {
        result = data;
        error = null;

        // PNG prediction prefixes every row with a one-byte filter tag.
        int stride = rowBytes + 1;
        int rows = data.Length / stride;
        if (rows == 0)
        {
            // Nothing decodable; an empty result is correct and keeps callers simple.
            result = [];
            return true;
        }

        var output = new byte[(long)rows * rowBytes <= int.MaxValue ? rows * rowBytes : 0];
        if (output.Length == 0 && rows > 0)
        {
            error = "A PNG predictor described more output than the addressable limit.";
            return false;
        }

        Span<byte> previous = new byte[rowBytes];

        for (int row = 0; row < rows; row++)
        {
            int inputStart = row * stride;
            byte tag = data[inputStart];
            int outputStart = row * rowBytes;
            Span<byte> current = output.AsSpan(outputStart, rowBytes);
            data.AsSpan(inputStart + 1, rowBytes).CopyTo(current);

            switch (tag)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (int i = bytesPerPixel; i < rowBytes; i++)
                        current[i] += current[i - bytesPerPixel];
                    break;
                case 2: // Up
                    for (int i = 0; i < rowBytes; i++)
                        current[i] += previous[i];
                    break;
                case 3: // Average
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        current[i] += (byte)((left + previous[i]) / 2);
                    }

                    break;
                case 4: // Paeth
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        int up = previous[i];
                        int upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                        current[i] += (byte)Paeth(left, up, upperLeft);
                    }

                    break;
                default:
                    error = "A PNG predictor row declared an unknown filter tag.";
                    return false;
            }

            current.CopyTo(previous);
        }

        result = output;
        return true;
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        int estimate = left + up - upperLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpperLeft = Math.Abs(estimate - upperLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpperLeft)
            return left;
        return distanceUp <= distanceUpperLeft ? up : upperLeft;
    }
}
