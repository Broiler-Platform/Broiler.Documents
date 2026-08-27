using System;

namespace Broiler.Documents.Pdf.Filters;

/// <summary>
/// The predictor post-processing that FlateDecode and LZWDecode may declare
/// through <c>DecodeParms</c> (clause 7.4.4.4): TIFF predictor 2 and the PNG
/// predictors 10–15.
/// </summary>
/// <remarks>
/// Predictors are a property of the <em>stream</em>, not of the compression
/// algorithm, so they live here rather than inside a filter. That is also why a
/// later LZW implementation gets predictor support for free.
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

        // Sub-byte components would need bit-level accumulation to undo. They are
        // rare enough that rejecting them beats shipping a path with no fixture.
        if (bitsPerComponent is not (8 or 16))
        {
            error = "A TIFF predictor used a component size outside the supported 8- and 16-bit subset.";
            return false;
        }

        int rows = data.Length / rowBytes;
        var output = (byte[])data.Clone();
        int step = colors * (bitsPerComponent / 8);

        for (int row = 0; row < rows; row++)
        {
            int rowStart = row * rowBytes;
            if (bitsPerComponent == 8)
            {
                for (int i = step; i < rowBytes; i++)
                    output[rowStart + i] = (byte)(output[rowStart + i] + output[rowStart + i - step]);
                continue;
            }

            for (int i = step; i + 1 < rowBytes; i += 2)
            {
                int previous = (output[rowStart + i - step] << 8) | output[rowStart + i - step + 1];
                int current = (output[rowStart + i] << 8) | output[rowStart + i + 1];
                int sum = (current + previous) & 0xFFFF;
                output[rowStart + i] = (byte)(sum >> 8);
                output[rowStart + i + 1] = (byte)sum;
            }
        }

        result = output;
        return true;
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
