using System;
using System.Globalization;
using Broiler.Documents.Pdf.Filters;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Decodes <c>DCTDecode</c> streams by composing the managed JPEG decoder from
/// <c>Broiler.Media.Image</c>. Not composed by default: a caller opts in by
/// putting it into <see cref="PdfCodecServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The cleared subset.</strong> IP-005 is approved for JPEG-1
/// (ISO/IEC 10918-1:1994 / ITU-T T.81) <em>baseline sequential</em> and
/// <em>progressive</em> DCT, Huffman entropy coding, 8-bit precision, one or
/// three components. Every other tuple is recognized and refused by name.
/// Progressive was widened into the row on 2026-09-02 rather than implemented
/// then: the decoder behind this filter had done progressive all along, and what
/// changed is which frame markers this gate admits. Arithmetic coding stays out,
/// and the two facts are the same fact seen from either side — the row is written
/// as tuples because the entropy coder is what the historical RAND terms attached
/// to, so a Huffman process is inside it and an arithmetic one is not, whatever
/// spectral order either happens to use.
/// </para>
/// <para>
/// <strong>Colour is declared, not assumed.</strong> An Adobe <c>APP14</c> marker
/// and a PDF <c>/ColorTransform</c> parameter each say how the components are to
/// be read, and IP-006 clears interpreting them: <c>1</c> is YCbCr, <c>0</c> is
/// no transform, <c>2</c> is YCCK. Where neither is present the format's own
/// default applies — YCbCr for three components, none for one. Where both are
/// present and disagree, the image is refused as uncertain rather than decoded
/// under whichever one an implementation happened to prefer.
/// </para>
/// <para>
/// <strong>One declaration is understood and still refused.</strong> Transform
/// <c>0</c> on a three-component frame means the samples are already RGB, and the
/// composed decoder applies the YCbCr conversion unconditionally, so honouring
/// that declaration would need a decoder change rather than a register row. It is
/// refused with the tuple code and a message that says so, because "we may not"
/// and "we cannot" are different answers and a host should be able to tell them
/// apart.
/// </para>
/// <para>
/// <strong>The budget comes first.</strong> The frame header is read before any
/// decode, the output size is computed from it, and a frame that would exceed the
/// ceiling is refused without the decoder ever seeing the data — which is what
/// the extension contract asks for and what a decompression bomb requires.
/// </para>
/// <para>
/// <strong>Residual security condition.</strong> The Broiler.Graphics human
/// review records that its managed image codecs are security-sensitive and
/// should not process untrusted input without resource limits and further
/// review. This adapter supplies the resource limits and converts a decoder
/// fault into a skipped image; it does not discharge the rest of that condition,
/// which is why the filter is opt-in and why the default build still composes no
/// image decoder at all.
/// </para>
/// </remarks>
public sealed class JpegStreamFilter : IPdfStreamFilter
{
    /// <summary>The decoder writes 8-bit RGBA, so four bytes per pixel.</summary>
    private const int BytesPerPixel = 4;

    /// <summary>Sample precision this filter accepts, in bits.</summary>
    private const int SupportedPrecision = 8;

    /// <summary>SOF0 — baseline sequential DCT, Huffman coded.</summary>
    private const byte BaselineSequentialFrame = 0xC0;

    /// <summary>SOF2 — progressive DCT, Huffman coded.</summary>
    private const byte ProgressiveFrame = 0xC2;

    /// <summary>Adobe colour transform 0: the components are already in their output space.</summary>
    private const int NoTransform = 0;

    /// <summary>Adobe colour transform 1: three components carrying YCbCr.</summary>
    private const int YCbCrTransform = 1;

    /// <summary>Adobe colour transform 2: four components carrying YCCK.</summary>
    private const int YcckTransform = 2;

    /// <summary>
    /// Stands in for a <c>/ColorTransform</c> entry that is present but not a
    /// number. It is deliberately not a legal transform value, so it falls
    /// through to the "not one of the values the format defines" refusal instead
    /// of quietly becoming 0.
    /// </summary>
    private const int UnreadableTransform = -1;

    private readonly JpegImageCodec _codec = new();

    public string Name => PdfFilterNames.Dct;

    public string? Abbreviation => "DCT";

    /// <summary>
    /// False: the output is image samples. The object layer must never try to
    /// read pixels as PDF syntax, so the pipeline stops the chain here.
    /// </summary>
    public bool ProducesByteStream => false;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        context.CancellationToken.ThrowIfCancellationRequested();

        if (!JpegFrameReader.TryRead(input, out JpegFrameHeader frame, out string? malformed))
            return PdfFilterResult.Malformed(malformed!);

        if (Refuse(frame, parameters) is PdfFilterResult refusal)
            return refusal;

        long ceiling = context.CeilingFor(input.Length);
        long required = (long)frame.Width * frame.Height * BytesPerPixel;
        if (required > ceiling)
        {
            return PdfFilterResult.LimitExceeded(string.Create(
                CultureInfo.InvariantCulture,
                $"Decoding the {frame.Width}x{frame.Height} JPEG would produce {required} bytes, past this stage's ceiling of {ceiling}."));
        }

        ImageBuffer buffer;
        try
        {
            buffer = _codec.Decode(input);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // Deliberately broad. This is the boundary between an untrusted
            // document and a codec whose own review calls it security-sensitive:
            // a fault while decoding one image must cost that image, not the
            // document, and must never surface as an unhandled exception to a
            // caller who only asked to read some text. The exception type is
            // reported; its message is not, because a decoder message can quote
            // the data it choked on.
            return PdfFilterResult.Malformed($"The JPEG data could not be decoded ({ex.GetType().Name}).");
        }

        if (buffer.Width != frame.Width || buffer.Height != frame.Height)
        {
            return PdfFilterResult.Malformed(
                "The decoded JPEG does not match the frame header it declared, so the budget it was measured against did not describe it.");
        }

        return PdfFilterResult.Success(ExactSamples(buffer));
    }

    /// <summary>
    /// The refusal this frame earns, or null when it is inside the cleared subset.
    /// </summary>
    /// <remarks>
    /// Each refusal names the tuple it found and the row that would have to move
    /// for it to be decoded, so the message answers the question a reviewer
    /// actually has: which tuples does the corpus in front of me use, and which
    /// approval would each one need?
    /// </remarks>
    private static PdfFilterResult? Refuse(in JpegFrameHeader frame, PdfFilterParameters parameters)
    {
        if (frame.FrameMarker is not (BaselineSequentialFrame or ProgressiveFrame))
        {
            return Tuple(
                $"The image is {frame.Describe()}. Only Huffman-coded baseline sequential and progressive DCT are " +
                "cleared (IP-005); arithmetic coding, lossless, hierarchical, and differential processes are not.");
        }

        if (frame.Precision != SupportedPrecision)
            return Tuple($"The image is {frame.Describe()}. Only {SupportedPrecision}-bit sample precision is cleared (IP-005).");

        if (frame.Components is not (1 or 3))
        {
            return Tuple($"The image is {frame.Describe()}. Only 1-component greyscale and 3-component colour are " +
                "decoded; four-component CMYK and YCCK conversion is outside this release's scope (roadmap §1.1), " +
                "and the composed decoder reads only 1- or 3-component frames.");
        }

        return RefuseColour(frame, parameters);

        static PdfFilterResult Tuple(string message) =>
            PdfFilterResult.Unsupported(PdfDiagnosticCodes.FilterDctUnsupported, message);
    }

    /// <summary>
    /// Resolves the frame's colour transform from its two possible declarations,
    /// and refuses the image when the answer is unclear or unreachable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two places can state the transform: the Adobe <c>APP14</c> marker inside
    /// the JPEG, and the <c>/ColorTransform</c> entry in the stream's
    /// <c>DecodeParms</c>. Implementations differ on which wins when both are
    /// present, and the difference is visible as wrong colour rather than as an
    /// error, so a disagreement is reported instead of resolved. A file that
    /// contradicts itself about how to read its own samples is telling a reader
    /// something worth passing on.
    /// </para>
    /// <para>
    /// With neither present the default is the format's own: three components are
    /// YCbCr, one component has nothing to transform.
    /// </para>
    /// </remarks>
    private static PdfFilterResult? RefuseColour(in JpegFrameHeader frame, PdfFilterParameters parameters)
    {
        int? fromMarker = frame.HasAdobeMarker ? frame.AdobeTransform : null;
        int? fromParameters = parameters.ContainsKey("ColorTransform")
            ? parameters.GetInt32("ColorTransform", UnreadableTransform)
            : null;

        if (fromMarker is int marker && fromParameters is int parameter && marker != parameter)
        {
            return PdfFilterResult.Unsupported(
                PdfDiagnosticCodes.FilterDctColorTransformUncertain,
                $"The image is {frame.Describe()}. Its Adobe APP14 marker declares colour transform {marker} and its " +
                $"DecodeParms declare {parameter}. The document contradicts itself about how its own samples are to be " +
                "read, so no colour rule was assumed.");
        }

        int transform = fromMarker ?? fromParameters ?? DefaultTransform(frame.Components);

        // Greyscale has one plane and no transform to apply, so the decoder
        // produces the same pixels whatever the declaration says.
        if (frame.Components == 1)
            return null;

        return transform switch
        {
            YCbCrTransform => null,
            NoTransform => Uncleared(
                $"The image is {frame.Describe()} and declares colour transform 0, meaning its samples are already RGB. " +
                "The composed decoder applies the YCbCr conversion unconditionally, so decoding it would report colours " +
                "the document does not contain. This is a limit of the composed decoder, not of IP-006."),
            YcckTransform => Uncleared(
                $"The image is {frame.Describe()} and declares colour transform 2 (YCCK), which is a four-component " +
                "rule on a three-component frame. YCCK conversion is outside this release's scope (roadmap §1.1)."),
            _ => PdfFilterResult.Unsupported(
                PdfDiagnosticCodes.FilterDctColorTransformUncertain,
                $"The image is {frame.Describe()} and declares colour transform {transform}, which is not one of the " +
                "values the format defines (0, 1, or 2), so no colour rule was assumed."),
        };

        static PdfFilterResult Uncleared(string message) =>
            PdfFilterResult.Unsupported(PdfDiagnosticCodes.FilterDctUnsupported, message);
    }

    /// <summary>
    /// The transform that applies when nothing declares one: Adobe Technical Note
    /// #5116's default, which is also the PDF default for <c>/ColorTransform</c>.
    /// </summary>
    private static int DefaultTransform(int components) =>
        components == 3 ? YCbCrTransform : NoTransform;

    /// <summary>
    /// The samples with no row padding. The decoder produces a tight buffer
    /// today; repacking rather than asserting keeps this correct if it ever
    /// produces a padded one.
    /// </summary>
    private static byte[] ExactSamples(ImageBuffer buffer)
    {
        int rowBytes = buffer.Width * BytesPerPixel;
        if (buffer.Stride == rowBytes && buffer.Rgba.Length == rowBytes * buffer.Height)
            return buffer.Rgba;

        byte[] packed = new byte[(long)rowBytes * buffer.Height <= int.MaxValue
            ? rowBytes * buffer.Height
            : throw new InvalidOperationException("The decoded image is larger than one array can hold.")];

        for (int row = 0; row < buffer.Height; row++)
            Array.Copy(buffer.Rgba, row * buffer.Stride, packed, row * rowBytes, rowBytes);

        return packed;
    }
}
