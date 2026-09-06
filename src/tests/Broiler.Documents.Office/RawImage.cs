using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace Broiler.Documents.Office;

/// <summary>
/// One rendered page, held as a byte of grey per pixel and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The point of this class is what it does not reference. This suite compares
/// LibreOffice's rendering of a document against Broiler.Documents' own, and
/// the two arrive as a PPM out of <c>pdftoppm</c> and a BMP out of
/// <c>broilerdoc render --image-format bmp</c>. If the harness decoded either
/// of those through an image library, and the tool under test used the same
/// library, a fault in that library would appear on both sides and cancel
/// itself out - the run would report agreement it had never actually observed.
/// Both formats are a small header followed by rows of pixels, and both
/// parsers below fit on a screen, which is the entire argument for writing
/// them by hand rather than taking the dependency.
/// </para>
/// <para>
/// Both readers hand back the same thing: top-down rows, one byte per pixel,
/// 0 for black and 255 for white. A BMP is normally stored bottom-up and a PPM
/// is always top-down, so somebody has to absorb that difference. Doing it
/// here, once, at the only two places that touch a file format, means no
/// metric downstream ever has to remember which way up the image it was handed
/// is - and a metric that had to remember would eventually forget, on the one
/// page nobody looked at closely, and report a difference that was really just
/// a flip.
/// </para>
/// </remarks>
internal sealed class RawImage
{
    private const int FileHeaderLength = 14;
    private const int InfoHeaderLength = 40;

    private RawImage(int width, int height, byte[] luma)
    {
        Width = width;
        Height = height;
        Luma = luma;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Row-major, top-down, one byte per pixel: 0 is black, 255 is white.
    /// Length is always <see cref="Width"/> times <see cref="Height"/>.
    /// </summary>
    public byte[] Luma { get; }

    /// <summary>
    /// Wraps a luma buffer that already exists, for the two callers that have
    /// pixels and no file: the blur, which produces a second image from a
    /// first, and the self test, which builds its page in memory. The buffer
    /// is taken as-is rather than copied, because both callers have just
    /// allocated it and neither keeps a second reference.
    /// </summary>
    public static RawImage FromLuma(int width, int height, byte[] luma)
    {
        ArgumentNullException.ThrowIfNull(luma);
        if (width < 0 || height < 0)
            throw new ArgumentException(
                "An image cannot have a negative dimension; got " +
                width + "x" + height + ".", nameof(width));
        if ((long)width * height != luma.LongLength)
            throw new ArgumentException(
                "A " + width + "x" + height + " image needs " +
                ((long)width * height) + " luma bytes but " + luma.LongLength +
                " were supplied.", nameof(luma));

        return new RawImage(width, height, luma);
    }

    /// <summary>
    /// Picks the reader from the file's first bytes rather than its extension.
    /// The extension is what somebody typed; the magic is what the tool wrote,
    /// and when those two disagree it is the extension that is wrong.
    /// </summary>
    public static RawImage Read(string path)
    {
        byte[] data = ReadFile(path);
        if (LooksLikeBmp(data))
            return ParseBmp(data, path);
        if (LooksLikePnm(data))
            return ParsePnm(data, path);

        throw new InvalidDataException(
            path + ": this is neither a BMP nor a binary PNM. It begins " +
            Describe(data) + ".");
    }

    public static RawImage ReadBmp(string path) => ParseBmp(ReadFile(path), path);

    public static RawImage ReadPnm(string path) => ParsePnm(ReadFile(path), path);

    /// <summary>
    /// The whole file at once, deliberately. A page at the resolutions this
    /// suite renders is a few megabytes; streaming it would save memory nobody
    /// is short of and would spread every bounds check across a buffer
    /// boundary, which is exactly where an off-by-one hides.
    /// </summary>
    private static byte[] ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return File.ReadAllBytes(path);
    }

    private static bool LooksLikeBmp(byte[] data) =>
        data.Length >= 2 && data[0] == 'B' && data[1] == 'M';

    private static bool LooksLikePnm(byte[] data) =>
        data.Length >= 2 && data[0] == 'P' && (data[1] == '5' || data[1] == '6');

    /// <summary>
    /// BT.601 luma, in integers on purpose. The obvious floating-point form of
    /// this expression is a shade more accurate and completely unsuitable: two
    /// machines that rounded a half differently would disagree about a page
    /// neither of them had got wrong, and the disagreement would be a fraction
    /// of a percent of pixels, which is precisely the size of the effects this
    /// suite is trying to measure. Integer arithmetic has one answer.
    /// </summary>
    private static byte Luminance(byte r, byte g, byte b) =>
        (byte)((299 * r + 587 * g + 114 * b) / 1000);

    /// <summary>
    /// Reads the BMP shapes the renderer actually emits, plus the one it might.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What <c>broilerdoc render --image-format bmp</c> writes today was read
    /// off a hexdump: "BM", a 14-byte file header, a 40-byte
    /// BITMAPINFOHEADER, 32 bits per pixel, BI_RGB, a positive height and so
    /// bottom-up rows, and the pixels at the offset bfOffBits names. 24bpp is
    /// read as well, because it is the classic layout and a future change to
    /// the writer could produce it without anybody thinking to come back here;
    /// a negative height is read as well, because top-down is legal and a
    /// reader that quietly mirrored the page would be worse than one that
    /// refused it. Everything else is refused by name. A message that says
    /// "biBitCount 16" tells whoever reads the log what to do next; "bad BMP"
    /// tells them to open a hex editor.
    /// </para>
    /// </remarks>
    private static RawImage ParseBmp(byte[] data, string path)
    {
        int minimum = FileHeaderLength + InfoHeaderLength;
        if (data.Length < minimum)
            throw Truncated(path, "the file and information headers", minimum, data.Length);
        if (!LooksLikeBmp(data))
            throw new InvalidDataException(
                path + ": a BMP must begin \"BM\" but this begins " + Describe(data) + ".");

        uint offBits = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10, 4));
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(14, 4));
        if (headerSize < InfoHeaderLength)
            throw new InvalidDataException(
                path + ": the DIB header claims to be " + headerSize + " bytes. Only " +
                "BITMAPINFOHEADER (40) and the longer headers that begin with it are " +
                "read; a 12-byte BITMAPCOREHEADER is not.");

        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18, 4));
        int declaredHeight = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22, 4));
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30, 4));

        if (compression != 0)
            throw new InvalidDataException(
                path + ": biCompression is " + CompressionName(compression) +
                ". Only BI_RGB (0) is read, which is what the renderer writes.");
        if (bitCount != 24 && bitCount != 32)
            throw new InvalidDataException(
                path + ": biBitCount is " + bitCount + ". Only 24 and 32 are read; " +
                "a palette or a 16-bit layout would need a colour table this reader " +
                "deliberately does not carry.");
        if (width <= 0)
            throw new InvalidDataException(
                path + ": biWidth is " + width + ", which cannot describe a page.");
        if (declaredHeight == 0)
            throw new InvalidDataException(path + ": biHeight is 0, which cannot describe a page.");

        // A negative biHeight means the rows were stored top-down, already in
        // the order this class hands out. A positive one means bottom-up, and
        // the copy below reverses it. int.MinValue has no positive counterpart,
        // so the magnitude is taken in long and range-checked rather than
        // handed to Math.Abs, which would throw on exactly one input.
        bool topDown = declaredHeight < 0;
        long height = declaredHeight == int.MinValue ? -(long)int.MinValue : Math.Abs(declaredHeight);

        long stride = (((long)width * bitCount + 31L) / 32L) * 4L;
        long pixels = (long)width * height;
        if (pixels > int.MaxValue)
            throw new InvalidDataException(
                path + ": " + width + "x" + height + " is more pixels than this reader " +
                "will hold in one array. Render a smaller page or a lower resolution.");
        if (offBits < FileHeaderLength + headerSize)
            throw new InvalidDataException(
                path + ": bfOffBits is " + offBits + ", which points back inside the " +
                (FileHeaderLength + headerSize) + " bytes of header. The pixel data cannot " +
                "start there.");

        long needed = offBits + (stride * height);
        if (needed > data.LongLength)
            throw Truncated(path, "the pixel data", needed, data.LongLength);

        int rows = (int)height;
        int bytesPerPixel = bitCount / 8;
        byte[] luma = new byte[(int)pixels];

        for (int row = 0; row < rows; row++)
        {
            // Both channel orders here are BGR in memory, which is what
            // little-endian BGRX comes out as when read a byte at a time; the
            // fourth byte of a 32bpp pixel is padding or alpha and the renderer
            // writes it opaque, so it is skipped rather than composited.
            int source = (int)(offBits + (stride * row));
            int target = (topDown ? row : rows - 1 - row) * width;
            for (int x = 0; x < width; x++)
            {
                int offset = source + (x * bytesPerPixel);
                luma[target + x] = Luminance(data[offset + 2], data[offset + 1], data[offset]);
            }
        }

        return new RawImage(width, rows, luma);
    }

    /// <summary>
    /// Reads the binary Netpbm formats, which is what poppler hands over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>pdftoppm -png</c> writes a PNG, but <c>pdftoppm</c> with no format
    /// flag writes a P6 PPM, and that is the form this suite asks for: an
    /// ASCII magic, three whitespace-separated numbers, one whitespace byte,
    /// then raw RGB triples with no compression and nothing to get wrong. P5,
    /// which <c>-gray</c> produces, is one byte per pixel and is read too.
    /// </para>
    /// <para>
    /// The fiddly part of the format is that a '#' comment may appear anywhere
    /// in the header, including between the width and the height, and runs to
    /// the end of the line. Writers do use it - poppler has shipped a version
    /// banner there. The token reader below therefore skips comments wherever
    /// whitespace is allowed, rather than only after the magic, because the
    /// version of this code that only handled the easy position would parse a
    /// banner as a height.
    /// </para>
    /// </remarks>
    private static RawImage ParsePnm(byte[] data, string path)
    {
        if (!LooksLikePnm(data))
            throw new InvalidDataException(
                path + ": a binary PNM must begin \"P5\" or \"P6\" but this begins " +
                Describe(data) + ".");

        bool colour = data[1] == '6';
        int index = 2;
        int width = ReadHeaderInteger(data, ref index, path, "width");
        int height = ReadHeaderInteger(data, ref index, path, "height");
        int maxValue = ReadHeaderInteger(data, ref index, path, "maxval");

        if (width <= 0 || height <= 0)
            throw new InvalidDataException(
                path + ": the header describes a " + width + "x" + height +
                " image, which cannot be a page.");
        if (maxValue != 255)
            throw new InvalidDataException(
                path + ": maxval is " + maxValue + ". Only 255 is read; anything above " +
                "it stores two bytes per sample and anything below it scales, and " +
                "pdftoppm emits neither for the pages this suite renders.");

        // Exactly one whitespace byte separates the header from the pixels.
        // Consuming a run of them instead would eat the first row of a page
        // whose top-left pixel happened to be 0x0A, which is a plausible dark
        // pixel and therefore a bug that would only ever appear on real output.
        if (index >= data.Length)
            throw Truncated(path, "the byte after maxval", index + 1, data.Length);
        if (!IsWhitespace(data[index]))
            throw new InvalidDataException(
                path + ": the byte after maxval is 0x" +
                data[index].ToString("X2", CultureInfo.InvariantCulture) +
                ", but the format requires a single whitespace byte there.");
        index++;

        int samples = colour ? 3 : 1;
        long needed = (long)width * height * samples;
        if (data.LongLength - index < needed)
            throw Truncated(path, "the pixel data", index + needed, data.LongLength);

        byte[] luma = new byte[width * height];
        if (colour)
        {
            for (int i = 0; i < luma.Length; i++)
            {
                int offset = index + (i * 3);
                luma[i] = Luminance(data[offset], data[offset + 1], data[offset + 2]);
            }
        }
        else
        {
            // A P5 sample is already the grey this class stores. Passing it
            // through Luminance would return the same number, because the three
            // coefficients sum to exactly 1000, but relying on that is relying
            // on a coincidence in a constant somebody may one day tune.
            Array.Copy(data, index, luma, 0, luma.Length);
        }

        return new RawImage(width, height, luma);
    }

    private static int ReadHeaderInteger(byte[] data, ref int index, string path, string what)
    {
        while (true)
        {
            if (index >= data.Length)
                throw new InvalidDataException(
                    path + ": the PNM header ended before the " + what + ".");

            byte current = data[index];
            if (current == '#')
            {
                while (index < data.Length && data[index] != '\n' && data[index] != '\r')
                    index++;
                continue;
            }

            if (!IsWhitespace(current))
                break;

            index++;
        }

        if (data[index] < '0' || data[index] > '9')
            throw new InvalidDataException(
                path + ": expected the " + what + " in the PNM header but found byte 0x" +
                data[index].ToString("X2", CultureInfo.InvariantCulture) + " at offset " +
                index + ".");

        long value = 0;
        while (index < data.Length && data[index] >= '0' && data[index] <= '9')
        {
            value = (value * 10) + (data[index] - '0');
            if (value > int.MaxValue)
                throw new InvalidDataException(
                    path + ": the " + what + " in the PNM header does not fit in an integer.");
            index++;
        }

        return (int)value;
    }

    private static bool IsWhitespace(byte value) =>
        value == ' ' || value == '\t' || value == '\n' || value == '\r' || value == '\v' || value == '\f';

    /// <summary>
    /// The one exception shape this file raises for a damaged file, and it
    /// always says how much was wanted and how much there was. Letting an index
    /// run off the end instead would raise IndexOutOfRangeException, which
    /// names a variable in this file rather than the file that is actually
    /// broken, and which reads in a log like a defect in the harness.
    /// </summary>
    private static InvalidDataException Truncated(string path, string what, long needed, long actual) =>
        new(path + ": truncated. " + what + " needs " + needed +
            " bytes but the file is " + actual + " bytes long.");

    private static string CompressionName(uint compression) => compression switch
    {
        1 => "BI_RLE8 (1)",
        2 => "BI_RLE4 (2)",
        3 => "BI_BITFIELDS (3)",
        4 => "BI_JPEG (4)",
        5 => "BI_PNG (5)",
        _ => compression.ToString(CultureInfo.InvariantCulture),
    };

    private static string Describe(byte[] data)
    {
        if (data.Length == 0)
            return "nothing at all - the file is empty";

        int count = Math.Min(4, data.Length);
        var text = new StringBuilder(count * 5);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                text.Append(' ');
            text.Append("0x").Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}
