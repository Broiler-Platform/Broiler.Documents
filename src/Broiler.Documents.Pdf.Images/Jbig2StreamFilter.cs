using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Decodes the part of <c>JBIG2Decode</c> that is fully in reach — generic
/// regions coded with MMR — and reports precisely what a stream holds when it is
/// not. Not composed by default.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What this decodes, and why that boundary.</strong> A JBIG2 page is
/// built from independently coded regions. A generic region coded with MMR is a
/// self-contained ITU-T T.6 bitmap, and T.6 is already implemented and tested
/// here for <c>CCITTFaxDecode</c> — so that region type decodes for real, reusing
/// that decoder rather than a second copy of it. Every other region type needs
/// the MQ arithmetic decoder and its context models, which are not written.
/// </para>
/// <para>
/// <strong>All or nothing per page.</strong> A page whose segments are not all
/// supported is refused whole rather than composited from the parts that
/// decoded. Half a page is not a worse picture, it is a misleading one: the text
/// a symbol region would have drawn is exactly the content a reader would assume
/// was absent from the original.
/// </para>
/// <para>
/// <strong>What the common case needs.</strong> Most JBIG2 in real documents is a
/// symbol dictionary plus text regions — that is the whole reason the format
/// exists for scanned text — and all of it is arithmetic-coded. So this filter
/// will refuse most real streams, and it names what it found so that the
/// remaining work is sized from evidence rather than guessed at.
/// </para>
/// <para>
/// <strong>Globals.</strong> <c>JBIG2Globals</c> arrives decoded through
/// <see cref="PdfFilterParameters.GetBytes"/> and its segments are inventoried
/// with the page's own. Nothing in the supported subset refers to them — a
/// generic region refers to no dictionary — but a stream that carries them is
/// saying it needs the part that is not written, and that is worth reporting.
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
        if (parameters.GetBytes("JBIG2Globals") is ReadOnlyMemory<byte> globalBytes &&
            Jbig2SegmentReader.TryRead(globalBytes.Span, out List<Jbig2Segment> parsed, out _))
        {
            globals = parsed;
        }

        if (Unsupported(segments, globals, input) is string refusal)
            return PdfFilterResult.Unsupported(PdfDiagnosticCodes.FilterJbig2Unsupported, refusal);

        return Compose(input, segments, context);
    }

    /// <summary>
    /// Why this page is outside the supported subset, or null when every segment
    /// in it is one this filter can honour.
    /// </summary>
    private static string? Unsupported(List<Jbig2Segment> segments, List<Jbig2Segment> globals, ReadOnlySpan<byte> data)
    {
        var reasons = new List<string>();

        if (globals.Count > 0)
            reasons.Add($"its JBIG2Globals hold {Jbig2SegmentReader.Describe(globals)}");

        bool anyRegion = false;
        foreach (Jbig2Segment segment in segments)
        {
            if (segment.IsStructural)
                continue;

            if (!segment.IsGenericRegion)
                continue;

            anyRegion = true;
            if (!Jbig2SegmentReader.TryReadGenericRegion(data, segment, out Jbig2GenericRegion region, out _))
                continue;

            if (!region.UsesMmr)
            {
                string prediction = region.TypicalPrediction ? " and typical prediction" : string.Empty;
                reasons.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"a {region.Width}x{region.Height} generic region is arithmetic-coded with template {region.Template}{prediction}"));
            }
            else if (region.CombinationOperator is not (CombineOr or CombineReplace))
            {
                reasons.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"a generic region composites with operator {region.CombinationOperator}"));
            }
        }

        foreach (Jbig2Segment segment in segments)
        {
            if (!segment.IsStructural && !segment.IsGenericRegion)
            {
                reasons.Add($"it holds a {segment.Describe()}, which needs the arithmetic decoder");
                break;
            }
        }

        if (!anyRegion && reasons.Count == 0)
            reasons.Add("it holds no region to decode");

        if (reasons.Count == 0)
            return null;

        return $"The page draws a JBIG2 image this build cannot decode: {string.Join("; ", reasons)}. " +
            $"The stream holds {Jbig2SegmentReader.Describe(segments)}. IP-008 clears JBIG2, and only generic " +
            "regions coded with MMR are implemented — the arithmetic decoder and the symbol, text, halftone, and " +
            "refinement regions that need it are outstanding work rather than a pending approval.";
    }

    /// <summary>Decodes every generic region and composites them onto the page.</summary>
    private static PdfFilterResult Compose(ReadOnlySpan<byte> data, List<Jbig2Segment> segments, PdfFilterContext context)
    {
        int pageWidth = 0;
        int pageHeight = 0;

        foreach (Jbig2Segment segment in segments)
        {
            if (segment.Type == 48 && Jbig2SegmentReader.TryReadPageSize(data, segment, out int width, out int height))
            {
                pageWidth = width;
                pageHeight = height;
                break;
            }
        }

        var regions = new List<(Jbig2GenericRegion Region, byte[] Bits)>();
        foreach (Jbig2Segment segment in segments)
        {
            if (!segment.IsGenericRegion)
                continue;

            if (!Jbig2SegmentReader.TryReadGenericRegion(data, segment, out Jbig2GenericRegion region, out string? error))
                return PdfFilterResult.Malformed(error!);

            // A generic region coded with MMR is a T.6 bitmap, which is the
            // decoder this assembly already carries for CCITTFaxDecode.
            var options = new CcittFaxOptions(
                CcittCoding.TwoDimensional, region.Width, region.Height,
                BlackIs1: true, EncodedByteAlign: false, ExpectsEndOfLine: false);

            CcittFaxResult decoded = CcittFaxDecoder.Decode(
                data.Slice(region.DataStart, region.DataLength), options, context.MaxDecodedBytes);

            if (decoded.Outcome == CcittFaxOutcome.TooLarge)
                return PdfFilterResult.LimitExceeded("A JBIG2 generic region would exceed this stage's decoded-byte ceiling.");
            if (decoded.Outcome != CcittFaxOutcome.Decoded)
                return PdfFilterResult.Malformed(decoded.Failure ?? "A JBIG2 generic region could not be decoded.");

            regions.Add((region, decoded.Rows!));
            pageWidth = Math.Max(pageWidth, region.X + region.Width);
            pageHeight = Math.Max(pageHeight, region.Y + region.Height);
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
        foreach ((Jbig2GenericRegion region, byte[] bits) in regions)
            Draw(page, stride, pageWidth, pageHeight, region, bits);

        for (int i = 0; i < page.Length; i++)
            page[i] = (byte)~page[i];

        return PdfFilterResult.Success(page);
    }

    private static void Draw(
        byte[] page,
        int stride,
        int pageWidth,
        int pageHeight,
        in Jbig2GenericRegion region,
        byte[] bits)
    {
        int regionStride = (region.Width + 7) / 8;

        for (int row = 0; row < region.Height; row++)
        {
            int y = region.Y + row;
            if (y < 0 || y >= pageHeight)
                continue;

            for (int column = 0; column < region.Width; column++)
            {
                int x = region.X + column;
                if (x < 0 || x >= pageWidth)
                    continue;

                int source = (row * regionStride) + (column >> 3);
                if (source >= bits.Length)
                    continue;

                if (((bits[source] >> (7 - (column & 7))) & 1) == 0)
                    continue;

                page[(y * stride) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
    }
}
