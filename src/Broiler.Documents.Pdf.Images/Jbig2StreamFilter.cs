using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Decodes the part of <c>JBIG2Decode</c> that is in reach — generic regions
/// under both coding methods, the symbol dictionaries and text regions a scanned
/// page is actually made of, and the refinement that corrects them — and reports
/// precisely what a stream holds when it is not. Not composed by default.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What this decodes, and why that boundary.</strong> A JBIG2 page is
/// built from independently coded regions. A generic region coded with MMR is a
/// self-contained ITU-T T.6 bitmap and reuses the fax decoder in this assembly;
/// one coded arithmetically goes through the MQ decoder and the generic
/// templates. A symbol dictionary and the text regions that draw from it are the
/// shape almost every real JBIG2 in a PDF has, and they decode in their
/// arithmetic form — including the refinement that corrects a symbol before it is
/// drawn, and the refinement regions that correct the page itself. What is left
/// is the Huffman-coded forms, aggregate symbol coding, the intermediate regions
/// that need auxiliary buffers, and the halftone regions, each refused by name.
/// </para>
/// <para>
/// <strong>Order is part of the format.</strong> Regions are composited onto the
/// page as they are read rather than collected and drawn at the end, because a
/// refinement region refines what is under it: the page has to exist, and to hold
/// everything earlier in the stream, before the segment correcting it is decoded.
/// The page is therefore sized first from the page information and the region
/// headers, which state their extents without anything being decoded.
/// </para>
/// <para>
/// <strong>All or nothing per page.</strong> A page whose segments are not all
/// supported is refused whole rather than composited from the parts that
/// decoded. Half a page is not a worse picture, it is a misleading one: what a
/// halftone region would have drawn is exactly the content a reader would assume
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
    /// <summary>The default region combination operator.</summary>
    private const int CombineOr = 0;

    /// <summary>Replace, which is how a refinement corrects what it refines.</summary>
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
    /// dictionary, an aggregated symbol — are raised by the decoders themselves
    /// and reported through the same message.
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

                if (region.CombinationOperator is not (CombineOr or CombineReplace))
                {
                    reasons.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"a generic region composites with operator {region.CombinationOperator}"));
                }

                continue;
            }

            if (segment.IsImmediateTextRegion || segment.IsImmediateRefinementRegion)
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
        "decode under both coding methods with symbol dictionaries, text regions and refinement under the " +
        "arithmetic coder — the Huffman-coded forms, aggregate symbol coding, the intermediate regions and the " +
        "halftone regions are outstanding work rather than a pending approval.";

    /// <summary>Decodes every segment that draws, compositing in the order they arrive.</summary>
    private static PdfFilterResult Compose(
        ReadOnlyMemory<byte> data,
        List<Jbig2Segment> segments,
        ReadOnlyMemory<byte> globalData,
        List<Jbig2Segment> globals,
        PdfFilterContext context)
    {
        (ReadOnlyMemory<byte> Buffer, List<Jbig2Segment> Segments)[] sources =
            [(globalData, globals), (data, segments)];

        if (Size(data.Span, segments, sources) is not (int pageWidth, int pageHeight, byte pageDefault))
            return PdfFilterResult.Malformed("The JBIG2 stream declares no page size and no region to take one from.");

        // A pixel per byte while the page is being built, because a refinement
        // region reads the page back before correcting it. It is packed once, on
        // the way out.
        long area = (long)pageWidth * pageHeight;
        if (area > context.MaxDecodedBytes)
            return PdfFilterResult.LimitExceeded("A JBIG2 page would exceed this stage's decoded-byte ceiling.");

        var page = Jbig2Bitmap.Blank(pageWidth, pageHeight, pageDefault);

        // Segment numbers are one space across the globals and the page, so the
        // exports are held in one map and the globals are walked first — which is
        // also the order a decoder must use, since the page refers to them.
        var exports = new Dictionary<uint, Jbig2Bitmap[]>();

        foreach ((ReadOnlyMemory<byte> buffer, List<Jbig2Segment> list) in sources)
        {
            foreach (Jbig2Segment segment in list)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                PdfFilterResult? failure =
                    segment.IsSymbolDictionary ? ReadDictionary(buffer, segment, exports, segments, context)
                    : segment.IsImmediateTextRegion ? ReadTextRegion(buffer, segment, exports, segments, page, context)
                    : segment.IsImmediateRefinementRegion ? ReadRefinementRegion(buffer, segment, segments, page, context)
                    : segment.IsGenericRegion ? ReadGenericRegion(buffer, segment, page, context)
                    : null;

                if (failure is PdfFilterResult refused)
                    return refused;
            }
        }

        int stride = (pageWidth + 7) / 8;
        long required = (long)stride * pageHeight;
        if (required > context.CeilingFor(data.Length))
            return PdfFilterResult.LimitExceeded("A JBIG2 page would exceed this stage's decoded-byte ceiling.");

        // Built with 1 meaning black, which is JBIG2's own convention, and
        // inverted here because PDF's filter output uses 0 for black.
        var packed = new byte[required];
        for (int y = 0; y < pageHeight; y++)
        {
            int row = y * stride;
            int source = y * pageWidth;
            for (int x = 0; x < pageWidth; x++)
            {
                if (page.Pixels[source + x] != 0)
                    packed[row + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        for (int i = 0; i < packed.Length; i++)
            packed[i] = (byte)~packed[i];

        return PdfFilterResult.Success(packed);
    }

    /// <summary>
    /// The page's size and starting colour, from the page information where it
    /// states them and from the region headers where it does not.
    /// </summary>
    /// <remarks>
    /// Every region segment begins with its extent, whatever it draws it from, so
    /// this needs nothing decoded — which is what lets the page be allocated
    /// before the first region is read.
    /// </remarks>
    private static (int Width, int Height, byte Default)? Size(
        ReadOnlySpan<byte> data,
        List<Jbig2Segment> segments,
        (ReadOnlyMemory<byte> Buffer, List<Jbig2Segment> Segments)[] sources)
    {
        int width = 0;
        int height = 0;
        byte fill = 0;

        foreach (Jbig2Segment segment in segments)
        {
            if (segment.Type == 48 &&
                Jbig2SegmentReader.TryReadPageSize(data, segment, out int declaredWidth, out int declaredHeight, out byte declaredFill))
            {
                width = declaredWidth;
                height = declaredHeight;
                fill = declaredFill;
                break;
            }
        }

        foreach ((ReadOnlyMemory<byte> buffer, List<Jbig2Segment> list) in sources)
        {
            foreach (Jbig2Segment segment in list)
            {
                if (!segment.IsRegion ||
                    !Jbig2SegmentReader.TryReadRegionInfo(buffer.Span, segment, out Jbig2RegionInfo info))
                {
                    continue;
                }

                width = Math.Max(width, info.X + info.Width);
                height = Math.Max(height, info.Y + info.Height);
            }
        }

        return width > 0 && height > 0 ? (width, height, fill) : null;
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

    /// <summary>Decodes a text region and draws it onto the page.</summary>
    private static PdfFilterResult? ReadTextRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        Dictionary<uint, Jbig2Bitmap[]> exports,
        List<Jbig2Segment> inventory,
        Jbig2Bitmap page,
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

                Draw(page, result.Bitmap!, result.X, result.Y, result.CombinationOperator);
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

    /// <summary>
    /// Decodes a refinement region, whose reference is the page it corrects.
    /// </summary>
    /// <remarks>
    /// This is the one region type that reads the page before writing to it, and
    /// the reason composition happens in stream order. What it refines is whatever
    /// earlier segments left under its rectangle — usually a text region drawn
    /// from a lossy dictionary, which the refinement then makes exact.
    /// </remarks>
    private static PdfFilterResult? ReadRefinementRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        List<Jbig2Segment> inventory,
        Jbig2Bitmap page,
        PdfFilterContext context)
    {
        if (!Jbig2SegmentReader.TryReadRefinementRegion(
            buffer.Span, segment, out Jbig2RefinementRegion region, out string? error))
        {
            return PdfFilterResult.Malformed(error!);
        }

        Jbig2RegionInfo info = region.Info;
        if (info.CombinationOperator is not (CombineOr or CombineReplace))
        {
            return PdfFilterResult.Unsupported(
                PdfDiagnosticCodes.FilterJbig2Unsupported,
                Refuse(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"a refinement region composites with operator {info.CombinationOperator}"),
                    inventory));
        }

        long pixels = (long)info.Width * info.Height;
        if (pixels > context.MaxDecodedBytes)
            return PdfFilterResult.LimitExceeded("A JBIG2 refinement region would exceed this stage's decoded-byte ceiling.");

        // The reference is the page under the region, taken before anything is
        // written back over it.
        Jbig2Bitmap reference = Extract(page, info.X, info.Y, info.Width, info.Height);

        var decoder = new MqDecoder(buffer.Slice(region.DataStart, region.DataLength));
        var contexts = new MqContexts(Jbig2RefinementDecoder.RefinementContextBits);

        byte[]? refined = Jbig2RefinementDecoder.Decode(
            decoder, contexts, info.Width, info.Height, region.Template, region.TypicalPrediction,
            reference, referenceDx: 0, referenceDy: 0, region.Adaptive);

        if (refined is null)
            return PdfFilterResult.Malformed("A JBIG2 refinement region could not be decoded.");

        Draw(page, new Jbig2Bitmap(info.Width, info.Height, refined), info.X, info.Y, info.CombinationOperator);
        return null;
    }

    /// <summary>Decodes a generic region under whichever coding method it declares.</summary>
    private static PdfFilterResult? ReadGenericRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        Jbig2Bitmap page,
        PdfFilterContext context)
    {
        if (!Jbig2SegmentReader.TryReadGenericRegion(buffer.Span, segment, out Jbig2GenericRegion region, out string? error))
            return PdfFilterResult.Malformed(error!);

        long pixels = (long)region.Width * region.Height;
        if (pixels > context.MaxDecodedBytes)
            return PdfFilterResult.LimitExceeded("A JBIG2 generic region would exceed this stage's decoded-byte ceiling.");

        Jbig2Bitmap bitmap;
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

            bitmap = Unpack(decoded.Rows!, region.Width, region.Height);
        }
        else
        {
            byte[]? arithmetic = Jbig2GenericDecoder.Decode(
                buffer.Slice(region.DataStart, region.DataLength),
                region.Width, region.Height, region.Template, region.TypicalPrediction, region.Adaptive);

            if (arithmetic is null)
                return PdfFilterResult.Malformed("A JBIG2 generic region could not be decoded.");

            bitmap = new Jbig2Bitmap(region.Width, region.Height, arithmetic);
        }

        Draw(page, bitmap, region.X, region.Y, region.CombinationOperator);
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

    /// <summary>A rectangle of the page, as the reference a refinement corrects.</summary>
    private static Jbig2Bitmap Extract(Jbig2Bitmap page, int x, int y, int width, int height)
    {
        var pixels = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
                pixels[(row * width) + column] = page.At(x + column, y + row);
        }

        return new Jbig2Bitmap(width, height, pixels);
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

    /// <summary>
    /// Draws a region onto the page under its combination operator.
    /// </summary>
    /// <remarks>
    /// The two are not interchangeable, whatever a blank page suggests. OR can
    /// only add black, so a refinement correcting a pixel back to white would have
    /// no way to say so; REPLACE writes what the region decoded, including where
    /// it decoded nothing.
    /// </remarks>
    private static void Draw(Jbig2Bitmap page, Jbig2Bitmap region, int originX, int originY, int combination)
    {
        for (int row = 0; row < region.Height; row++)
        {
            int y = originY + row;
            if (y < 0 || y >= page.Height)
                continue;

            int target = y * page.Width;
            int source = row * region.Width;

            for (int column = 0; column < region.Width; column++)
            {
                int x = originX + column;
                if (x < 0 || x >= page.Width)
                    continue;

                byte value = region.Pixels[source + column];
                if (combination == CombineReplace)
                    page.Pixels[target + x] = value;
                else if (value != 0)
                    page.Pixels[target + x] = 1;
            }
        }
    }
}
