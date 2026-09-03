using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Decodes the part of <c>JBIG2Decode</c> that is in reach — generic regions
/// under both coding methods, and the symbol dictionaries and text regions a
/// scanned page is actually made of — and reports precisely what a stream holds
/// when it is not. Not composed by default.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What this decodes, and why that boundary.</strong> A JBIG2 page is
/// built from independently coded regions. A generic region coded with MMR is a
/// self-contained ITU-T T.6 bitmap and reuses the fax decoder in this assembly;
/// one coded arithmetically goes through the MQ decoder and the generic
/// templates. A symbol dictionary and the text regions that draw from it are the
/// shape almost every real JBIG2 in a PDF has — the whole reason the format
/// exists for scanned text — and they decode here in their arithmetic form. What
/// is left is the Huffman-coded forms of both, refinement, and the halftone
/// regions, each refused by name.
/// </para>
/// <para>
/// <strong>All or nothing per page.</strong> A page whose segments are not all
/// supported is refused whole rather than composited from the parts that
/// decoded. Half a page is not a worse picture, it is a misleading one: the text
/// a symbol region would have drawn is exactly the content a reader would assume
/// was absent from the original.
/// </para>
/// <para>
/// <strong>Globals.</strong> <c>JBIG2Globals</c> arrives decoded through
/// <see cref="PdfFilterParameters.GetBytes"/>, and it is where a PDF usually
/// keeps the symbol dictionary shared by every image in the file. Its segments
/// are walked before the page's own and their exports are available to the page's
/// text regions, because segment numbers are one space across both.
/// </para>
/// <para>
/// <strong>Still unproven against a real file.</strong> Everything here is
/// round-tripped against an encoder in the test suite and nothing else. The
/// standard's test sequences are official test material and no third-party JBIG2
/// file may be committed (IP-020), so a shared misreading of T.88 passes every
/// test in this repository. That is why no JBIG2-derived capability may be
/// claimed as supported, quite apart from SRC-019.
/// </para>
/// </remarks>
public sealed class Jbig2StreamFilter : IPdfStreamFilter
{
    /// <summary>The default region combination operator, and the only one composited.</summary>
    private const int CombineOr = 0;

    /// <summary>Replace, which for a single region onto a blank page is the same thing.</summary>
    private const int CombineReplace = 4;

    public string Name => PdfFilterNames.Jbig2;

    public string? Abbreviation => null;

    /// <summary>False: the output is image samples.</summary>
    public bool ProducesByteStream => false;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        context.CancellationToken.ThrowIfCancellationRequested();

        if (!Jbig2SegmentReader.TryRead(input, out List<Jbig2Segment> segments, out string? error))
            return PdfFilterResult.Malformed(error!);

        var globals = new List<Jbig2Segment>();
        ReadOnlyMemory<byte> globalData = default;
        if (parameters.GetBytes("JBIG2Globals") is ReadOnlyMemory<byte> globalBytes &&
            Jbig2SegmentReader.TryRead(globalBytes.Span, out List<Jbig2Segment> parsed, out _))
        {
            globals = parsed;
            globalData = globalBytes;
        }

        if (Unsupported(segments, globals, input) is string refusal)
            return PdfFilterResult.Unsupported(PdfDiagnosticCodes.FilterJbig2Unsupported, Refuse(refusal, segments));

        return Compose(input.ToArray(), segments, globalData, globals, context);
    }

    /// <summary>
    /// Why this page is outside the supported subset, or null when every segment
    /// in it is one this filter can honour.
    /// </summary>
    /// <remarks>
    /// This is the structural scan, made from segment types and headers without
    /// decoding anything. The refusals a header cannot state — a Huffman-coded
    /// dictionary, a text region that refines — are raised by the decoders
    /// themselves and reported through the same message.
    /// </remarks>
    private static string? Unsupported(List<Jbig2Segment> segments, List<Jbig2Segment> globals, ReadOnlySpan<byte> data)
    {
        var reasons = new List<string>();
        bool anyRegion = false;

        foreach (Jbig2Segment segment in segments)
        {
            if (segment.IsGenericRegion)
            {
                anyRegion = true;
                if (!Jbig2SegmentReader.TryReadGenericRegion(data, segment, out Jbig2GenericRegion region, out _))
                    continue;

                // Both coding methods decode now, so the only thing a generic
                // region is still refused for is how it composites.
                if (region.CombinationOperator is not (CombineOr or CombineReplace))
                {
                    reasons.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"a generic region composites with operator {region.CombinationOperator}"));
                }

                continue;
            }

            if (segment.IsImmediateTextRegion)
            {
                anyRegion = true;
                continue;
            }

            if (segment.IsStructural || segment.IsSymbolDictionary)
                continue;

            reasons.Add($"it holds a {segment.Describe()}, whose decoder is not written");
            break;
        }

        foreach (Jbig2Segment segment in globals)
        {
            if (segment.IsStructural || segment.IsSymbolDictionary)
                continue;

            reasons.Add($"its JBIG2Globals hold a {segment.Describe()}, whose decoder is not written");
            break;
        }

        if (!anyRegion && reasons.Count == 0)
            reasons.Add("it holds no region to decode");

        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }

    /// <summary>The refusal a host reads, with the inventory it was made from.</summary>
    private static string Refuse(string reasons, List<Jbig2Segment> segments) =>
        $"The page draws a JBIG2 image this build cannot decode: {reasons}. " +
        $"The stream holds {Jbig2SegmentReader.Describe(segments)}. IP-008 clears JBIG2, and generic regions " +
        "decode under both coding methods with symbol dictionaries and text regions under the arithmetic coder — " +
        "the Huffman-coded forms of those, refinement, and the halftone regions are outstanding work rather than " +
        "a pending approval.";

    /// <summary>Decodes every segment that draws, and composites the result onto the page.</summary>
    private static PdfFilterResult Compose(
        ReadOnlyMemory<byte> data,
        List<Jbig2Segment> segments,
        ReadOnlyMemory<byte> globalData,
        List<Jbig2Segment> globals,
        PdfFilterContext context)
    {
        int pageWidth = 0;
        int pageHeight = 0;
        byte pageDefault = 0;

        foreach (Jbig2Segment segment in segments)
        {
            if (segment.Type == 48 &&
                Jbig2SegmentReader.TryReadPageSize(data.Span, segment, out int width, out int height, out byte fill))
            {
                pageWidth = width;
                pageHeight = height;
                pageDefault = fill;
                break;
            }
        }

        // Segment numbers are one space across the globals and the page, so the
        // exports are held in one map and the globals are walked first — which is
        // also the order a decoder must use, since the page refers to them.
        var exports = new Dictionary<uint, Jbig2Bitmap[]>();
        var placements = new List<(int X, int Y, Jbig2Bitmap Bitmap)>();

        foreach ((ReadOnlyMemory<byte> buffer, List<Jbig2Segment> list) in
            new[] { (globalData, globals), (data, segments) })
        {
            foreach (Jbig2Segment segment in list)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (segment.IsSymbolDictionary)
                {
                    PdfFilterResult? failure = ReadDictionary(buffer, segment, exports, segments, context);
                    if (failure is PdfFilterResult dictionaryRefusal)
                        return dictionaryRefusal;

                    continue;
                }

                if (segment.IsImmediateTextRegion)
                {
                    PdfFilterResult? failure = ReadTextRegion(buffer, segment, exports, segments, placements, context);
                    if (failure is PdfFilterResult textRefusal)
                        return textRefusal;

                    continue;
                }

                if (!segment.IsGenericRegion)
                    continue;

                PdfFilterResult? regionFailure = ReadGenericRegion(buffer, segment, placements, context);
                if (regionFailure is PdfFilterResult regionRefusal)
                    return regionRefusal;
            }
        }

        foreach ((int x, int y, Jbig2Bitmap bitmap) in placements)
        {
            pageWidth = Math.Max(pageWidth, x + bitmap.Width);
            pageHeight = Math.Max(pageHeight, y + bitmap.Height);
        }

        if (pageWidth <= 0 || pageHeight <= 0)
            return PdfFilterResult.Malformed("The JBIG2 stream declares no page size and no region to take one from.");

        int stride = (pageWidth + 7) / 8;
        long required = (long)stride * pageHeight;
        if (required > context.CeilingFor(data.Length))
            return PdfFilterResult.LimitExceeded("A JBIG2 page would exceed this stage's decoded-byte ceiling.");

        // Built with 1 meaning black, which is JBIG2's own convention, and
        // inverted at the end because PDF's filter output uses 0 for black.
        var page = new byte[required];
        if (pageDefault != 0)
            Array.Fill(page, byte.MaxValue);

        foreach ((int x, int y, Jbig2Bitmap bitmap) in placements)
            Draw(page, stride, pageWidth, pageHeight, x, y, bitmap);

        for (int i = 0; i < page.Length; i++)
            page[i] = (byte)~page[i];

        return PdfFilterResult.Success(page);
    }

    /// <summary>Decodes a symbol dictionary and records what it exports.</summary>
    private static PdfFilterResult? ReadDictionary(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        Dictionary<uint, Jbig2Bitmap[]> exports,
        List<Jbig2Segment> inventory,
        PdfFilterContext context)
    {
        // A dictionary may be built on the symbols of the dictionaries it refers
        // to, and its export flags count through those first.
        List<Jbig2Bitmap> input = Gather(segment, exports);

        Jbig2SymbolDictionaryResult result = Jbig2SymbolDictionary.Decode(
            buffer.Slice(segment.DataStart, segment.DataLength), input, context.MaxDecodedBytes);

        switch (result.Outcome)
        {
            case Jbig2DecodeOutcome.Decoded:
                exports[segment.Number] = result.Symbols;
                return null;

            case Jbig2DecodeOutcome.Unsupported:
                return PdfFilterResult.Unsupported(
                    PdfDiagnosticCodes.FilterJbig2Unsupported, Refuse($"it holds {result.Message}", inventory));

            case Jbig2DecodeOutcome.TooLarge:
                return PdfFilterResult.LimitExceeded(
                    "A JBIG2 symbol dictionary would exceed this stage's decoded-byte ceiling.");

            default:
                return PdfFilterResult.Malformed(result.Message!);
        }
    }

    /// <summary>Decodes a text region and queues it for the page.</summary>
    private static PdfFilterResult? ReadTextRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        Dictionary<uint, Jbig2Bitmap[]> exports,
        List<Jbig2Segment> inventory,
        List<(int X, int Y, Jbig2Bitmap Bitmap)> placements,
        PdfFilterContext context)
    {
        List<Jbig2Bitmap> symbols = Gather(segment, exports);

        Jbig2TextRegionResult result = Jbig2TextRegion.Decode(
            buffer.Slice(segment.DataStart, segment.DataLength), symbols, context.MaxDecodedBytes);

        switch (result.Outcome)
        {
            case Jbig2DecodeOutcome.Decoded:
                if (result.CombinationOperator is not (CombineOr or CombineReplace))
                {
                    return PdfFilterResult.Unsupported(
                        PdfDiagnosticCodes.FilterJbig2Unsupported,
                        Refuse(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"a text region composites with operator {result.CombinationOperator}"),
                            inventory));
                }

                placements.Add((result.X, result.Y, result.Bitmap!));
                return null;

            case Jbig2DecodeOutcome.Unsupported:
                return PdfFilterResult.Unsupported(
                    PdfDiagnosticCodes.FilterJbig2Unsupported, Refuse($"it holds {result.Message}", inventory));

            case Jbig2DecodeOutcome.TooLarge:
                return PdfFilterResult.LimitExceeded(
                    "A JBIG2 text region would exceed this stage's decoded-byte ceiling.");

            default:
                return PdfFilterResult.Malformed(result.Message!);
        }
    }

    /// <summary>Decodes a generic region under whichever coding method it declares.</summary>
    private static PdfFilterResult? ReadGenericRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        List<(int X, int Y, Jbig2Bitmap Bitmap)> placements,
        PdfFilterContext context)
    {
        if (!Jbig2SegmentReader.TryReadGenericRegion(buffer.Span, segment, out Jbig2GenericRegion region, out string? error))
            return PdfFilterResult.Malformed(error!);

        long pixels = (long)region.Width * region.Height;
        if (pixels > context.MaxDecodedBytes)
            return PdfFilterResult.LimitExceeded("A JBIG2 generic region would exceed this stage's decoded-byte ceiling.");

        if (region.UsesMmr)
        {
            // A generic region coded with MMR is a T.6 bitmap, which is the
            // decoder this assembly already carries for CCITTFaxDecode.
            var options = new CcittFaxOptions(
                CcittCoding.TwoDimensional, region.Width, region.Height,
                BlackIs1: true, EncodedByteAlign: false, ExpectsEndOfLine: false);

            CcittFaxResult decoded = CcittFaxDecoder.Decode(
                buffer.Span.Slice(region.DataStart, region.DataLength), options, context.MaxDecodedBytes);

            if (decoded.Outcome == CcittFaxOutcome.TooLarge)
                return PdfFilterResult.LimitExceeded("A JBIG2 generic region would exceed this stage's decoded-byte ceiling.");
            if (decoded.Outcome != CcittFaxOutcome.Decoded)
                return PdfFilterResult.Malformed(decoded.Failure ?? "A JBIG2 generic region could not be decoded.");

            placements.Add((region.X, region.Y, Unpack(decoded.Rows!, region.Width, region.Height)));
            return null;
        }

        byte[]? arithmetic = Jbig2GenericDecoder.Decode(
            buffer.Slice(region.DataStart, region.DataLength),
            region.Width, region.Height, region.Template, region.TypicalPrediction, region.Adaptive);

        if (arithmetic is null)
            return PdfFilterResult.Malformed("A JBIG2 generic region could not be decoded.");

        placements.Add((region.X, region.Y, new Jbig2Bitmap(region.Width, region.Height, arithmetic)));
        return null;
    }

    /// <summary>
    /// The symbols a segment inherits, in the order its referred-to segments name
    /// them.
    /// </summary>
    /// <remarks>
    /// Order is not a detail here. A text region's symbol identifiers are indices
    /// into this concatenation, so a referred-to dictionary read out of order
    /// draws the wrong glyph under the right identifier — a page of plausible,
    /// wrong text rather than a failure. A number that names no dictionary
    /// contributes nothing, and the region that meant to use it will run out of
    /// symbols and be refused rather than silently shifted.
    /// </remarks>
    private static List<Jbig2Bitmap> Gather(in Jbig2Segment segment, Dictionary<uint, Jbig2Bitmap[]> exports)
    {
        var symbols = new List<Jbig2Bitmap>();
        foreach (uint number in segment.Referred)
        {
            if (exports.TryGetValue(number, out Jbig2Bitmap[]? exported))
                symbols.AddRange(exported);
        }

        return symbols;
    }

    /// <summary>Rows of bits, as the fax decoder produces them, into a pixel per byte.</summary>
    private static Jbig2Bitmap Unpack(byte[] rows, int width, int height)
    {
        int stride = (width + 7) / 8;
        var pixels = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            int source = y * stride;
            int target = y * width;
            for (int x = 0; x < width; x++)
            {
                int at = source + (x >> 3);
                if (at < rows.Length && ((rows[at] >> (7 - (x & 7))) & 1) != 0)
                    pixels[target + x] = 1;
            }
        }

        return new Jbig2Bitmap(width, height, pixels);
    }

    private static void Draw(
        byte[] page,
        int stride,
        int pageWidth,
        int pageHeight,
        int originX,
        int originY,
        Jbig2Bitmap bitmap)
    {
        for (int row = 0; row < bitmap.Height; row++)
        {
            int y = originY + row;
            if (y < 0 || y >= pageHeight)
                continue;

            int source = row * bitmap.Width;

            for (int column = 0; column < bitmap.Width; column++)
            {
                int x = originX + column;
                if (x < 0 || x >= pageWidth)
                    continue;

                if (bitmap.Pixels[source + column] == 0)
                    continue;

                page[(y * stride) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
    }
}
