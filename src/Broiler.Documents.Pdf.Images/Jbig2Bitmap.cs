using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// The bounds this decoder refuses past, in one place so that a reviewer can see
/// the whole envelope at once.
/// </summary>
/// <remarks>
/// None of these comes from T.88, which sets no such limits: they are this
/// build's answer to a hostile stream. A JBIG2 segment states its own sizes and
/// counts before any of them is checked against the data that must supply them,
/// so a file can ask for a dictionary of four billion symbols in eight bytes. The
/// numbers below are chosen to be far above any real scanned page and far below
/// anything that costs the process its memory.
/// </remarks>
internal static class Jbig2Limits
{
    /// <summary>Symbols one dictionary may define, and one region may refer to.</summary>
    public const int MaxSymbols = 100_000;

    /// <summary>Bits in a symbol identifier, which bounds the ID decoder's contexts.</summary>
    public const int MaxSymbolCodeLength = 17;

    /// <summary>Rows or columns in one symbol.</summary>
    public const int MaxSymbolExtent = 8192;

    /// <summary>Symbol instances one text region may place.</summary>
    public const int MaxInstances = 1_000_000;
}

/// <summary>
/// One decoded JBIG2 bitmap: a symbol, or a region, one byte per pixel with 1
/// meaning black.
/// </summary>
/// <remarks>
/// A byte per pixel rather than a packed bit, because every use here reads
/// individual pixels — the generic decoder forms a context from ten to sixteen
/// neighbours per pixel, and a text region composites symbols at arbitrary
/// offsets. Packing happens once, on the way out of the filter.
/// </remarks>
internal sealed class Jbig2Bitmap
{
    public Jbig2Bitmap(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major, one byte per pixel.</summary>
    public byte[] Pixels { get; }

    public static Jbig2Bitmap Blank(int width, int height, byte value)
    {
        var pixels = new byte[width * height];
        if (value != 0)
            Array.Fill(pixels, value);

        return new Jbig2Bitmap(width, height, pixels);
    }

    public byte At(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height ? Pixels[(y * Width) + x] : (byte)0;
}

/// <summary>How one segment's decode ended.</summary>
internal enum Jbig2DecodeOutcome
{
    /// <summary>A bitmap, or a set of them.</summary>
    Decoded,

    /// <summary>
    /// The segment is well formed and states something outside this build's
    /// subset. The message names the construct met, and the caller reports it as
    /// a refusal rather than a fault in the file.
    /// </summary>
    Unsupported,

    /// <summary>The segment contradicts itself or the data it points at.</summary>
    Malformed,

    /// <summary>The segment asks for more decoded bytes than the read may spend.</summary>
    TooLarge,
}
