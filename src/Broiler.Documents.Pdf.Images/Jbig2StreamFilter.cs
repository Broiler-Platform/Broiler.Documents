using System;
using Broiler.Documents.Pdf.Filters;
using Broiler.Media.Image.Managed.Jbig2;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Decodes the part of <c>JBIG2Decode</c> that is in reach — generic regions
/// under both coding methods, the symbol dictionaries and text regions a scanned
/// page is actually made of, and the refinement that corrects them — and reports
/// precisely what a stream holds when it is not. Not composed by default.
/// </summary>
public sealed class Jbig2StreamFilter : IPdfStreamFilter
{
    public string Name => PdfFilterNames.Jbig2;

    public string? Abbreviation => null;

    /// <summary>False: the output is image samples.</summary>
    public bool ProducesByteStream => false;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        context.CancellationToken.ThrowIfCancellationRequested();

        ReadOnlySpan<byte> globals = default;
        if (parameters.GetBytes("JBIG2Globals") is ReadOnlyMemory<byte> globalBytes)
            globals = globalBytes.Span;

        Jbig2Result result = Jbig2Decoder.Decode(
            input,
            globals,
            context.MaxDecodedBytes,
            context.CancellationToken);

        switch (result.Outcome)
        {
            case Jbig2DecodeOutcome.Decoded:
                byte[] packed = result.GetPackedBits(invert: true)!;
                if (packed.Length > context.CeilingFor(input.Length))
                    return PdfFilterResult.LimitExceeded("A JBIG2 page would exceed this stage's decoded-byte ceiling.");
                return PdfFilterResult.Success(packed);

            case Jbig2DecodeOutcome.Unsupported:
                return PdfFilterResult.Unsupported(PdfDiagnosticCodes.FilterJbig2Unsupported, result.Failure ?? "Unsupported JBIG2 stream.");

            case Jbig2DecodeOutcome.TooLarge:
                return PdfFilterResult.LimitExceeded(result.Failure ?? "A JBIG2 page would exceed this stage's decoded-byte ceiling.");

            default:
                return PdfFilterResult.Malformed(result.Failure ?? "A JBIG2 stream could not be decoded.");
        }
    }
}
