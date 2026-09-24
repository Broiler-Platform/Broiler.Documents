using System;
using System.Threading;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// The budget and cancellation handed to a colour-profile reader.
/// </summary>
/// <remarks>
/// An ICC profile is untrusted input from the document: its tags are offsets
/// and lengths into itself, and a lookup table's size is a product of numbers
/// the profile states. The ceiling is checked by the codec before the profile is
/// handed over and is meant to be checked again by the reader before it
/// allocates.
/// </remarks>
public sealed class PdfColorProfileContext
{
    public PdfColorProfileContext(long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        MaxBytes = maxBytes;
        CancellationToken = cancellationToken;
    }

    /// <summary>Hard ceiling on the profile bytes a reader may examine.</summary>
    public long MaxBytes { get; }

    public CancellationToken CancellationToken { get; }
}

/// <summary>
/// The four rendering intents PDF 32000-1 8.6.5.8 names, which select the table
/// an ICC profile converts through.
/// </summary>
/// <remarks>
/// Relative colorimetric comes first because it is the format's default: a
/// page that names no intent is converted with it, and the default value of
/// this type is the answer for one.
/// </remarks>
public enum PdfRenderingIntent
{
    RelativeColorimetric,
    AbsoluteColorimetric,
    Saturation,
    Perceptual,
}

/// <summary>
/// A conversion from one ICC profile's colour values to the RGB the model
/// carries.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a colour-management system. The codec has one question to
/// ask of a profile - what colour a stored value is - and one answer it can
/// hold: an RGB triple in the colour space the model's pixels are drawn in.
/// Everything a profile can do beyond that, from device links to proofing, is
/// not asked for here.
/// </para>
/// <para>
/// Implementations are immutable once built, so one conversion can serve every
/// image that names the same profile.
/// </para>
/// </remarks>
public abstract class PdfColorTransform
{
    /// <summary>How many components a colour value in the profile's own space has.</summary>
    public abstract int Components { get; }

    /// <summary>
    /// Converts one colour: <paramref name="components"/> holds
    /// <see cref="Components"/> values in [0, 1], and <paramref name="rgb"/>
    /// receives three bytes.
    /// </summary>
    public abstract void ToRgb(ReadOnlySpan<double> components, Span<byte> rgb);
}

/// <summary>
/// Reads an ICC profile a document embeds far enough to convert the colours it
/// describes.
/// </summary>
/// <remarks>
/// <para>
/// The codec's third composition point, after <see cref="Filters.IPdfStreamFilter"/>
/// and <see cref="IPdfFontProgramReader"/>. An <c>/ICCBased</c> colour space
/// says what its values mean only through the profile it carries, and a
/// reader without one either refuses the image or draws a plausible wrong
/// picture. The base build refuses it, and says so.
/// </para>
/// <para>
/// Implementations must be pure and instance-owned, must respect
/// <see cref="PdfColorProfileContext.MaxBytes"/> before allocating, must observe
/// the cancellation token, and must return <see langword="null"/> rather than
/// throwing for a profile they cannot read, with a reason in
/// <c>declined</c> that names the construct and never repeats a value from the
/// profile - a description, a copyright notice, or a vendor tag - because a
/// diagnostic carries it (ADR 0009).
/// </para>
/// </remarks>
public interface IPdfColorProfileReader
{
    /// <summary>
    /// Builds the conversion for one profile, or returns null and says why.
    /// </summary>
    /// <param name="profile">The decoded profile bytes.</param>
    /// <param name="components">
    /// The <c>/N</c> the colour space declares; a profile over a different
    /// number of components contradicts it and is declined.
    /// </param>
    /// <param name="intent">The rendering intent in force where the colour is used.</param>
    /// <param name="context">The byte ceiling and cancellation for this read.</param>
    /// <param name="declined">Null on success; otherwise the construct that stopped the read.</param>
    PdfColorTransform? Read(
        ReadOnlySpan<byte> profile,
        int components,
        PdfRenderingIntent intent,
        PdfColorProfileContext context,
        out string? declined);
}
