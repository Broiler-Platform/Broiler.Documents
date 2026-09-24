using System;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// The colour spaces whose own samples this build turns into pixels.
/// </summary>
/// <remarks>
/// Exactly the raw-sample subset PDF roadmap §9.3 approved, and an ICC-based
/// space where a composed reader converts its profile (IP-024). Everything else
/// a PDF may declare — CalGray, CalRGB, Lab, DeviceCMYK, Separation, DeviceN,
/// and any pattern space — is refused by name rather than approximated, because
/// each of them needs a colour transform this project does not own and guessing
/// one produces a plausible wrong picture instead of an error.
/// </remarks>
internal enum PdfSampleSpace
{
    /// <summary>`/DeviceGray` at 1, 2, 4, or 8 bits per component.</summary>
    Gray,

    /// <summary>`/DeviceRGB` at 8 bits per component.</summary>
    Rgb,

    /// <summary>
    /// `/Indexed` at 1, 2, 4, or 8 bits, over a palette already expanded to RGB
    /// triples from a `/DeviceGray`, `/DeviceRGB`, or converted ICC-based base.
    /// </summary>
    Indexed,

    /// <summary>
    /// `/ICCBased` through a composed reader's conversion: one component at 1, 2,
    /// 4, or 8 bits, or three or four at 8.
    /// </summary>
    Profile,
}

/// <summary>
/// The colour-space names ISO 32000-1 reserves, as against the ones a document
/// invents in its own resource dictionary.
/// </summary>
/// <remarks>
/// The distinction decides what a diagnostic may repeat. A reserved name is a
/// construct of the format — the same class of fact as a filter name, which the
/// image inventory already reports. A resource label is a string the document's
/// author chose, and ADR 0009 keeps document values out of diagnostics, so a
/// refusal names the family it recognized or says nothing at all.
/// </remarks>
internal static class PdfColorSpaces
{
    private static readonly string[] Reserved =
    [
        "DeviceGray", "DeviceRGB", "DeviceCMYK", "CalGray", "CalRGB", "Lab",
        "ICCBased", "Indexed", "Separation", "DeviceN", "Pattern",
    ];

    /// <summary>True when the format defines this name rather than a document.</summary>
    public static bool IsReserved(string name) => Array.IndexOf(Reserved, name) >= 0;
}

/// <summary>
/// An image's sample layout, resolved from its dictionary: enough to turn the
/// bytes a filter chain produced into pixels, and nothing more.
/// </summary>
/// <param name="Width">Width in samples.</param>
/// <param name="Height">Height in samples.</param>
/// <param name="BitsPerComponent">1, 2, 4, or 8.</param>
/// <param name="Space">The colour interpretation.</param>
/// <param name="Palette">
/// For <see cref="PdfSampleSpace.Indexed"/>, the palette as RGB triples, three
/// bytes per entry; null for the device spaces.
/// </param>
/// <param name="Decode">
/// The validated `/Decode` array as component pairs in [0, 1], or null where the
/// image uses the default mapping. Only the device and ICC-based spaces carry
/// one: an <see cref="PdfSampleSpace.Indexed"/> image with a non-default
/// `/Decode` remaps indices, which is a different operation and is refused
/// rather than half-applied.
/// </param>
/// <param name="Transform">
/// For <see cref="PdfSampleSpace.Profile"/>, the conversion the profile's
/// composed reader built; null for every other space.
/// </param>
internal readonly record struct PdfSampleFormat(
    int Width,
    int Height,
    int BitsPerComponent,
    PdfSampleSpace Space,
    byte[]? Palette,
    double[]? Decode,
    PdfColorTransform? Transform = null)
{
    /// <summary>
    /// Components per sample: one for gray and indexed, three for RGB, and the
    /// profile's own count for an ICC-based space.
    /// </summary>
    public int Components => Space switch
    {
        PdfSampleSpace.Rgb => 3,
        PdfSampleSpace.Profile => Transform!.Components,
        _ => 1,
    };

    /// <summary>
    /// The bytes a decode of this image must produce. Each row is packed at the
    /// declared depth and padded to a byte boundary, which is why this is not
    /// simply a pixel count times a component count.
    /// </summary>
    public long ExpectedBytes =>
        (((long)Width * Components * BitsPerComponent) + 7) / 8 * Height;

    /// <summary>The bytes the projected pixels will occupy.</summary>
    public long RgbaBytes => (long)Width * Height * BPixelBuffer.BytesPerPixel;
}

/// <summary>
/// Turns an image's own samples into the straight-alpha RGBA the model carries.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a colour-management layer. It unpacks the declared
/// bit depth, applies the declared `/Decode` mapping, and looks entries up in a
/// palette — three mechanical operations over a subset chosen so that none of
/// them needs a profile, a white point, or a rendering intent to be correct.
/// </para>
/// <para>
/// The one transparency decided here is a colour-key `/Mask`, because it is a
/// statement about the samples as stored and they are only visible here. A
/// soft mask and an explicit mask are pictures of their own and are read into
/// the alpha channel by the caller, and a stencil is painted in the fill
/// colour rather than looked up at all. Nothing is composited against a
/// backdrop: the model carries the alpha, and whoever draws the picture blends
/// it.
/// </para>
/// </remarks>
internal static class PdfImageSamples
{
    /// <summary>
    /// The samples as RGBA, or null when their length does not match what the
    /// dictionary declared — which is a document contradicting itself, not a
    /// layout to infer from a byte count.
    /// </summary>
    /// <param name="colourKey">
    /// A colour-key mask: a minimum and a maximum for each component, over the
    /// samples as stored - before `/Decode`, and over the index for an Indexed
    /// image - or null for none. A pixel whose every component lies in its range
    /// is not painted (PDF 32000-1 8.9.6.4), and so is transparent here.
    /// </param>
    public static byte[]? ToRgba(in PdfSampleFormat format, byte[] samples, int[]? colourKey = null)
    {
        if (samples.LongLength != format.ExpectedBytes)
            return null;

        long pixels = (long)format.Width * format.Height;
        if (pixels <= 0 || pixels > int.MaxValue / BPixelBuffer.BytesPerPixel)
            return null;

        byte[] rgba = new byte[pixels * BPixelBuffer.BytesPerPixel];
        int maximum = (1 << format.BitsPerComponent) - 1;
        int components = format.Components;
        long stride = (((long)format.Width * components * format.BitsPerComponent) + 7) / 8;
        int output = 0;
        Span<double> levels = stackalloc double[4];
        Span<byte> rgb = stackalloc byte[3];

        for (int y = 0; y < format.Height; y++)
        {
            // Each row restarts at a byte boundary, so the bit cursor is per row
            // rather than continuous: a 1-bit 5-pixel row occupies a whole byte
            // and the next row begins in the next one.
            long row = y * stride;

            for (int x = 0; x < format.Width; x++)
            {
                bool keyed;
                switch (format.Space)
                {
                    case PdfSampleSpace.Gray:
                    {
                        int raw = Sample(samples, row, x, format.BitsPerComponent);
                        byte level = Component(raw, maximum, format.Decode, 0);
                        rgba[output] = level;
                        rgba[output + 1] = level;
                        rgba[output + 2] = level;
                        keyed = colourKey is not null && InRange(colourKey, 0, raw);
                        break;
                    }

                    case PdfSampleSpace.Rgb:
                    {
                        // Eight bits per component, so a component is a byte and
                        // the three sit side by side.
                        long at = row + ((long)x * 3);
                        rgba[output] = Component(samples[at], maximum, format.Decode, 0);
                        rgba[output + 1] = Component(samples[at + 1], maximum, format.Decode, 1);
                        rgba[output + 2] = Component(samples[at + 2], maximum, format.Decode, 2);
                        keyed = colourKey is not null &&
                            InRange(colourKey, 0, samples[at]) &&
                            InRange(colourKey, 1, samples[at + 1]) &&
                            InRange(colourKey, 2, samples[at + 2]);
                        break;
                    }

                    case PdfSampleSpace.Profile:
                    {
                        // One component may be packed below a byte; three and
                        // four are eight bits each, side by side.
                        keyed = colourKey is not null;
                        for (int c = 0; c < components; c++)
                        {
                            int raw = components == 1
                                ? Sample(samples, row, x, format.BitsPerComponent)
                                : samples[row + ((long)x * components) + c];

                            levels[c] = Level(raw, maximum, format.Decode, c);
                            keyed = keyed && InRange(colourKey!, c, raw);
                        }

                        format.Transform!.ToRgb(levels[..components], rgb);
                        rgba[output] = rgb[0];
                        rgba[output + 1] = rgb[1];
                        rgba[output + 2] = rgb[2];
                        break;
                    }

                    default:
                    {
                        int index = Sample(samples, row, x, format.BitsPerComponent);
                        byte[] palette = format.Palette!;

                        // An index past the palette is out of range rather than
                        // fatal: the entry is black, which is what the format
                        // says an unfilled palette entry holds.
                        int at = index * 3;
                        if (at + 2 < palette.Length)
                        {
                            rgba[output] = palette[at];
                            rgba[output + 1] = palette[at + 1];
                            rgba[output + 2] = palette[at + 2];
                        }

                        keyed = colourKey is not null && InRange(colourKey, 0, index);
                        break;
                    }
                }

                rgba[output + 3] = keyed ? (byte)0 : (byte)255;
                output += BPixelBuffer.BytesPerPixel;
            }
        }

        return rgba;
    }

    /// <summary>Whether a stored component value lies in its colour-key range.</summary>
    private static bool InRange(int[] colourKey, int component, int value) =>
        value >= colourKey[component * 2] && value <= colourKey[(component * 2) + 1];

    /// <summary>
    /// The <paramref name="x"/>th single-component sample in a row, at 1, 2, 4,
    /// or 8 bits, most significant bit first.
    /// </summary>
    private static int Sample(byte[] samples, long row, int x, int bits)
    {
        if (bits == 8)
            return samples[row + x];

        int perByte = 8 / bits;
        byte packed = samples[row + (x / perByte)];
        int shift = 8 - bits - (x % perByte * bits);
        return (packed >> shift) & ((1 << bits) - 1);
    }

    /// <summary>
    /// One component as an eight-bit level: the raw sample normalized to [0, 1],
    /// mapped linearly onto the `/Decode` interval where the image states one,
    /// and scaled. `/Decode` running from 1 to 0 is the ordinary way a PDF says
    /// "inverted", and falls out of the same arithmetic rather than needing a
    /// case of its own.
    /// </summary>
    private static byte Component(int raw, int maximum, double[]? decode, int component)
    {
        double eight = Level(raw, maximum, decode, component) * 255;
        return eight <= 0 ? (byte)0 : eight >= 255 ? (byte)255 : (byte)Math.Round(eight, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// One component as a level in [0, 1] - the raw sample normalized, and mapped
    /// onto the `/Decode` interval where the image states one - which is what a
    /// colour conversion takes in.
    /// </summary>
    private static double Level(int raw, int maximum, double[]? decode, int component)
    {
        double value = (double)raw / maximum;

        if (decode is not null)
        {
            double min = decode[component * 2];
            double max = decode[(component * 2) + 1];
            value = min + (value * (max - min));
        }

        return value <= 0 ? 0 : value >= 1 ? 1 : value;
    }
}
