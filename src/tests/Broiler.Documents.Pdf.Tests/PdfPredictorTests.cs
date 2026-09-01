using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers the predictor post-processing FlateDecode and LZWDecode may declare.
/// </summary>
/// <remarks>
/// Predictors are reversible by construction, so every test here differences a
/// generated image forward and asserts the reverse recovers it exactly. That is
/// worth more than a fixed vector: a predictor that is subtly wrong still
/// produces plausible bytes, and only comparing against the original catches it.
/// </remarks>
public sealed class PdfPredictorTests
{
    // ---- TIFF predictor 2 -----------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    [InlineData(16, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(4, 3)]
    [InlineData(8, 3)]
    [InlineData(16, 3)]
    [InlineData(8, 4)]
    public void A_Tiff_Predicted_Image_Round_Trips_At_Every_Component_Size(int bits, int colors)
    {
        const int columns = 9;
        const int rows = 5;

        byte[] original = Image(columns, rows, colors, bits);
        byte[] predicted = TiffPredict(original, columns, rows, colors, bits);

        Assert.True(PdfPredictor.TryReverse(
            predicted, PdfPredictor.Tiff, colors, bits, columns, out byte[] reversed, out string? error), error);

        Assert.Equal(original, reversed);
    }

    [Fact]
    public void Sub_Byte_Components_Are_No_Longer_Refused()
    {
        // This path used to report an unsupported component size. IP-011 named
        // TIFF predictor 2 without qualification, and a generated fixture is all
        // the path ever needed.
        byte[] original = Image(columns: 16, rows: 3, colors: 1, bits: 4);
        byte[] predicted = TiffPredict(original, 16, 3, 1, 4);

        Assert.True(PdfPredictor.TryReverse(
            predicted, PdfPredictor.Tiff, colors: 1, bitsPerComponent: 4, columns: 16,
            out byte[] reversed, out string? error), error);

        Assert.Equal(original, reversed);
    }

    // ---- PNG predictors -------------------------------------------------------

    [Theory]
    [InlineData(0)]     // None
    [InlineData(1)]     // Sub
    [InlineData(2)]     // Up
    [InlineData(3)]     // Average
    [InlineData(4)]     // Paeth
    public void A_Png_Predicted_Image_Round_Trips_For_Every_Filter(int tag)
    {
        const int columns = 12;
        const int rows = 6;
        const int colors = 3;

        byte[] original = Image(columns, rows, colors, bits: 8);
        byte[] predicted = PngPredict(original, columns, rows, colors, row => tag);

        Assert.True(PdfPredictor.TryReverse(
            predicted, 15, colors, 8, columns, out byte[] reversed, out string? error), error);

        Assert.Equal(original, reversed);
    }

    [Fact]
    public void Rows_May_Each_Choose_A_Different_Filter()
    {
        // What "optimum selection" means on the encoding side. A decoder has
        // nothing to select, so the test is that every row's own tag is honoured.
        const int columns = 12;
        const int rows = 10;
        const int colors = 3;

        byte[] original = Image(columns, rows, colors, bits: 8);
        byte[] predicted = PngPredict(original, columns, rows, colors, row => row % 5);

        Assert.True(PdfPredictor.TryReverse(
            predicted, 15, colors, 8, columns, out byte[] reversed, out string? error), error);

        Assert.Equal(original, reversed);
    }

    [Fact]
    public void An_Unknown_Png_Filter_Tag_Is_Refused()
    {
        byte[] predicted = PngPredict(Image(4, 2, 1, 8), 4, 2, 1, row => 0);
        predicted[0] = 9;

        Assert.False(PdfPredictor.TryReverse(predicted, 15, 1, 8, 4, out _, out string? error));
        Assert.Contains("unknown filter tag", error!, StringComparison.Ordinal);
    }

    // ---- parameter validation -------------------------------------------------

    [Theory]
    [InlineData(0, 8, 4)]       // colours below the range
    [InlineData(33, 8, 4)]      // colours above it
    [InlineData(1, 3, 4)]       // a component size the format does not define
    [InlineData(1, 8, 0)]       // no columns
    public void Out_Of_Range_Parameters_Are_Refused(int colors, int bits, int columns)
    {
        Assert.False(PdfPredictor.TryReverse(
            new byte[64], PdfPredictor.Tiff, colors, bits, columns, out _, out string? error));

        Assert.NotNull(error);
    }

    [Fact]
    public void No_Predictor_Leaves_The_Data_Alone()
    {
        byte[] data = Image(8, 2, 1, 8);

        Assert.True(PdfPredictor.TryReverse(data, PdfPredictor.None, 1, 8, 8, out byte[] result, out _));
        Assert.Same(data, result);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>Packed component data with enough variety to catch a shifted read.</summary>
    private static byte[] Image(int columns, int rows, int colors, int bits)
    {
        int rowBytes = (((columns * colors * bits) + 7) / 8);
        var data = new byte[rowBytes * rows];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)((i * 37) ^ (i >> 3));
        return data;
    }

    /// <summary>Differences each component against the one a pixel to its left.</summary>
    private static byte[] TiffPredict(byte[] source, int columns, int rows, int colors, int bits)
    {
        int rowBytes = ((columns * colors * bits) + 7) / 8;
        var output = (byte[])source.Clone();
        int componentsPerRow = columns * colors;
        int mask = bits == 16 ? 0xFFFF : (1 << bits) - 1;

        for (int row = 0; row < rows; row++)
        {
            int rowStart = row * rowBytes;

            // Right to left, so each difference is taken against the untouched
            // original rather than an already-differenced neighbour.
            for (int component = componentsPerRow - 1; component >= colors; component--)
            {
                int current = Read(source, rowStart, component, bits);
                int previous = Read(source, rowStart, component - colors, bits);
                Write(output, rowStart, component, bits, (current - previous) & mask);
            }
        }

        return output;
    }

    /// <summary>Applies the PNG filter <paramref name="chooseTag"/> picks for each row.</summary>
    private static byte[] PngPredict(byte[] source, int columns, int rows, int colors, Func<int, int> chooseTag)
    {
        int rowBytes = columns * colors;
        int bytesPerPixel = colors;
        var output = new byte[(rowBytes + 1) * rows];
        var previous = new byte[rowBytes];

        for (int row = 0; row < rows; row++)
        {
            int tag = chooseTag(row);
            int sourceStart = row * rowBytes;
            int outputStart = row * (rowBytes + 1);
            output[outputStart] = (byte)tag;

            for (int i = 0; i < rowBytes; i++)
            {
                int raw = source[sourceStart + i];
                int left = i >= bytesPerPixel ? source[sourceStart + i - bytesPerPixel] : 0;
                int up = previous[i];
                int upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;

                int predicted = tag switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upperLeft),
                    _ => 0,
                };

                output[outputStart + 1 + i] = (byte)((raw - predicted) & 0xFF);
            }

            Array.Copy(source, sourceStart, previous, 0, rowBytes);
        }

        return output;
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

    private static int Read(byte[] data, int rowStart, int component, int bits)
    {
        if (bits == 16)
        {
            int index = rowStart + (component * 2);
            return (data[index] << 8) | data[index + 1];
        }

        int bitOffset = component * bits;
        int shift = 8 - bits - (bitOffset & 7);
        return (data[rowStart + (bitOffset >> 3)] >> shift) & ((1 << bits) - 1);
    }

    private static void Write(byte[] data, int rowStart, int component, int bits, int value)
    {
        if (bits == 16)
        {
            int index = rowStart + (component * 2);
            data[index] = (byte)(value >> 8);
            data[index + 1] = (byte)value;
            return;
        }

        int bitOffset = component * bits;
        int shift = 8 - bits - (bitOffset & 7);
        int mask = ((1 << bits) - 1) << shift;
        int target = rowStart + (bitOffset >> 3);
        data[target] = (byte)((data[target] & ~mask) | ((value << shift) & mask));
    }
}
