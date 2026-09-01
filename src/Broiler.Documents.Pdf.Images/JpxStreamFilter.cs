using System;
using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Recognizes <c>JPXDecode</c> streams and reports exactly what they are. It does
/// not decode them: IP-007 clears the JPEG 2000 Part 1 core coding system, and no
/// entropy decoder for it is written yet.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a filter that never succeeds is still worth composing.</strong>
/// Two reasons, and neither is decoding. First, a <c>JPXDecode</c> image is
/// allowed to omit <c>/ColorSpace</c> and <c>/BitsPerComponent</c> from its PDF
/// dictionary, because the codestream is the authority for them — so without
/// reading the codestream a reader genuinely cannot say what the image is, and
/// the dictionary-derived tuple every other image reports is, for this one,
/// frequently blank. Second, the decision the register now faces is how much
/// decoder to write, and that decision is made from the tuples a real corpus
/// uses: how many components, what depth, how many decomposition levels, which
/// wavelet, Part 1 or Part 2.
/// </para>
/// <para>
/// <strong>Part 1 only.</strong> The <c>Rsiz</c> capability field separates a Part
/// 1 core codestream from a Part 2 extended one. IP-007 clears the former; the
/// latter is a different standard with its own patent position, and is refused by
/// name rather than folded in.
/// </para>
/// <para>
/// <strong>What is missing, precisely.</strong> Everything below the headers: the
/// MQ arithmetic coder, EBCOT tier-1's three coding passes and their context
/// models, tier-2's packet headers and tag trees, the 5/3 and 9/7 wavelet
/// transforms, and the component transforms. JPEG 2000 has no baseline subset
/// that avoids any of them, which is why this filter reports rather than
/// half-decodes: a partial JPEG 2000 decoder does not produce a worse picture, it
/// produces a wrong one.
/// </para>
/// </remarks>
public sealed class JpxStreamFilter : IPdfStreamFilter
{
    public string Name => PdfFilterNames.Jpx;

    public string? Abbreviation => null;

    /// <summary>False: were this to decode, its output would be image samples.</summary>
    public bool ProducesByteStream => false;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        context.CancellationToken.ThrowIfCancellationRequested();

        if (!JpxCodestreamReader.TryRead(input, out JpxCodestreamHeader header, out string? error))
            return PdfFilterResult.Malformed(error!);

        string container = header.IsJp2Container ? "a JP2 container holding " : string.Empty;

        if (!header.IsPartOneCore)
        {
            return PdfFilterResult.Unsupported(
                PdfDiagnosticCodes.FilterJpxUnsupported,
                $"The page draws {container}a JPEG 2000 codestream declaring Rsiz {header.Capability}, which names " +
                "Part 2 extensions. IP-007 clears the Part 1 core coding system only, so this is outside the row " +
                $"rather than merely undecoded. The codestream is {header.Describe()}.");
        }

        return PdfFilterResult.Unsupported(
            PdfDiagnosticCodes.FilterJpxUnsupported,
            $"The page draws {container}a JPEG 2000 Part 1 codestream: {header.Describe()}. IP-007 clears this " +
            "coding system, and no entropy decoder for it is composed — the arithmetic coder, EBCOT, and the " +
            "wavelet transforms are outstanding work rather than a pending approval.");
    }
}
