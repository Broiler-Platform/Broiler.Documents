using System;
using System.Globalization;
using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Decodes <c>CCITTFaxDecode</c> streams — ITU-T T.4 and T.6 fax coding — into
/// packed one-bit-per-pixel rows. Not composed by default: a caller opts in by
/// putting it into <see cref="PdfCodecServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Composed rather than built in.</strong> LZW went into the base build
/// when its row cleared, because a bounded byte-stream decompressor is not what
/// the composition boundary is for. This one stays out, and the difference is
/// real: fax data is a bit-level entropy-coded stream that produces a pixel
/// buffer whose size comes from the <em>dictionary</em> rather than from the
/// data, which is the same shape of attack surface as an image codec and is
/// exactly what extension points §1 keeps out of the default build.
/// </para>
/// <para>
/// <strong>What the parameters decide.</strong> Fax data carries no header at
/// all: the column count, the row count, the coding scheme, and even which bit
/// value means black are all in the PDF dictionary. A decoder that guessed any of
/// them would produce a plausible picture of nothing, so each is read and honoured
/// (<c>K</c>, <c>Columns</c>, <c>Rows</c>, <c>BlackIs1</c>,
/// <c>EncodedByteAlign</c>, <c>EndOfLine</c>).
/// </para>
/// <para>
/// <strong>Provenance.</strong> The decoder is this repository's. Its code tables
/// are transcribed from ITU-T T.4, which is the one place in this codebase where a
/// normative table could not be authored instead — see
/// <see cref="CcittFaxTables"/> and the open source item that records it.
/// </para>
/// </remarks>
public sealed class CcittFaxStreamFilter : IPdfStreamFilter
{
    /// <summary>The default page width in fax pixels, when the dictionary states none.</summary>
    private const int DefaultColumns = 1728;

    public string Name => PdfFilterNames.CcittFax;

    public string? Abbreviation => "CCF";

    /// <summary>False: the output is image samples, not a byte stream to parse.</summary>
    public bool ProducesByteStream => false;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        context.CancellationToken.ThrowIfCancellationRequested();

        if (input.Length == 0)
            return PdfFilterResult.Malformed("A CCITTFaxDecode stream carried no data.");

        int k = parameters.GetInt32("K", 0);
        var options = new CcittFaxOptions(
            k < 0 ? CcittCoding.TwoDimensional : k == 0 ? CcittCoding.OneDimensional : CcittCoding.Mixed,
            parameters.GetInt32("Columns", DefaultColumns),
            parameters.GetInt32("Rows", 0),
            parameters.GetBoolean("BlackIs1", false),
            parameters.GetBoolean("EncodedByteAlign", false),
            parameters.GetBoolean("EndOfLine", false));

        CcittFaxResult result;
        try
        {
            result = CcittFaxDecoder.Decode(input, options, context.CeilingFor(input.Length));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // The same boundary the other image filters keep. A fault decoding one
            // image costs that image, not the document.
            return PdfFilterResult.Malformed($"The CCITTFaxDecode data could not be decoded ({ex.GetType().Name}).");
        }

        return result.Outcome switch
        {
            CcittFaxOutcome.Decoded => PdfFilterResult.Success(result.Rows!),
            CcittFaxOutcome.TooLarge => PdfFilterResult.LimitExceeded(string.Create(
                CultureInfo.InvariantCulture,
                $"A CCITTFaxDecode image of {options.Columns} columns would exceed this stage's decoded-byte ceiling ({result.Failure}).")),
            _ => PdfFilterResult.Malformed(result.Failure ?? "A CCITTFaxDecode stream could not be decoded."),
        };
    }
}
