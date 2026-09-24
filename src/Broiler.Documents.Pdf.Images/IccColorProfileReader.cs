using System;
using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// Converts the colour of an <c>/ICCBased</c> space through the ICC profile it
/// carries, to the sRGB the model's pixels are drawn in. Not composed by
/// default: a caller opts in by putting it into <see cref="PdfCodecServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What it converts.</strong> Input, display, output, and colour-space
/// profiles, versions 2 and 4, over Gray, RGB, and CMYK, connecting in CIEXYZ
/// or CIELAB: a matrix profile's colorants and tone curves, and a lookup table
/// - <c>lut8Type</c>, <c>lut16Type</c>, or <c>lutAtoBType</c> - chosen by the
/// rendering intent. That covers the profiles PDF producers embed: the sRGB,
/// Adobe RGB, and Display P3 families on screen images, gray gamma profiles,
/// and the press profiles CMYK pictures come with.
/// </para>
/// <para>
/// <strong>What it declines, by name.</strong> Device links, abstract and
/// named-colour profiles, version 5, colour spaces beyond those three, a
/// table with more than four inputs, and any structure that runs past the
/// bytes the profile carries. Optional tags a conversion does not need - the
/// <c>outputResponseTag</c> among them, the one construct with a patent
/// declaration recorded against it (IP-024) - are never read.
/// </para>
/// <para>
/// <strong>Provenance.</strong> An independent implementation, written from the
/// structure of ICC.1 / ISO 15076-1 (SRC-022) with the colorimetric facts that
/// define sRGB and the linear Bradford adaptation (SRC-023). No colour
/// management library was consulted, no profile is bundled, and every number
/// a conversion uses comes out of the profile it reads - which is document
/// content, carried by the document, and never kept past the read.
/// </para>
/// </remarks>
public sealed class IccColorProfileReader : IPdfColorProfileReader
{
    public PdfColorTransform? Read(
        ReadOnlySpan<byte> profile,
        int components,
        PdfRenderingIntent intent,
        PdfColorProfileContext context,
        out string? declined)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (profile.Length > context.MaxBytes)
        {
            declined = "a profile past the reader's byte ceiling";
            return null;
        }

        if (!IccProfile.TryParse(profile, out IccProfile? parsed, out declined))
            return null;

        if (parsed!.Components != components)
        {
            declined = "a profile over a colour space without the components the document declares";
            return null;
        }

        return IccTransform.TryCreate(parsed, intent, out declined);
    }
}
