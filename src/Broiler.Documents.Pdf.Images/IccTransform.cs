using System;
using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// One profile's conversion from its own colour values to sRGB: through the
/// profile to the connection space, and from there to the colour space the
/// model's pixels are drawn in.
/// </summary>
/// <remarks>
/// <para>
/// A profile describes its colours in one of two ways, and the conversion
/// follows whichever it has. A <strong>lookup table</strong> - the
/// <c>A2B</c> tag its rendering intent selects - is preferred where one exists,
/// as ICC.1 has a reader prefer it. Otherwise a <strong>matrix profile</strong>
/// states three colorants and a tone curve for each channel, or a gray profile
/// one curve, and the connection-space colour is the colorants weighted by the
/// linearized channels.
/// </para>
/// <para>
/// The intent picks the table: perceptual <c>A2B0</c>, relative colorimetric
/// <c>A2B1</c>, saturation <c>A2B2</c>, with <c>A2B0</c> standing in for any
/// the profile leaves out. A matrix profile has one conversion for every
/// intent. Absolute colorimetric is relative colorimetric scaled by the
/// profile's media white, so a paper's own colour shows.
/// </para>
/// </remarks>
internal sealed class IccTransform : PdfColorTransform
{
    private readonly int _components;
    private readonly IccLut? _lut;
    private readonly IccCurve[]? _curves;
    private readonly double[]? _colorants;
    private readonly IccXyz _white;
    private readonly double[] _toSrgb;
    private readonly double[]? _absolute;

    private IccTransform(IccProfile profile, IccLut? lut, IccCurve[]? curves, double[]? colorants, PdfRenderingIntent intent)
    {
        _components = profile.Components;
        _lut = lut;
        _curves = curves;
        _colorants = colorants;
        _white = profile.Illuminant;
        _toSrgb = IccSrgb.FromConnectionSpace(profile.Illuminant);

        if (intent == PdfRenderingIntent.AbsoluteColorimetric &&
            profile.TryXyzTag("wtpt", out IccXyz media) && media.IsUsable)
        {
            _absolute = [media.X / _white.X, media.Y / _white.Y, media.Z / _white.Z];
        }
    }

    public override int Components => _components;

    public override void ToRgb(ReadOnlySpan<double> components, Span<byte> rgb)
    {
        Span<double> xyz = stackalloc double[3];
        ToConnectionSpace(components, xyz);

        if (_absolute is not null)
        {
            for (int c = 0; c < 3; c++)
                xyz[c] *= _absolute[c];
        }

        for (int r = 0; r < 3; r++)
        {
            double linear = (_toSrgb[r * 3] * xyz[0]) + (_toSrgb[(r * 3) + 1] * xyz[1]) + (_toSrgb[(r * 3) + 2] * xyz[2]);
            rgb[r] = IccSrgb.Encode(linear);
        }
    }

    /// <summary>
    /// Builds the conversion a profile describes for an intent, or declines
    /// with the construct that stopped it.
    /// </summary>
    public static PdfColorTransform? TryCreate(IccProfile profile, PdfRenderingIntent intent, out string? declined)
    {
        declined = null;
        string preferred = intent switch
        {
            PdfRenderingIntent.Perceptual => "A2B0",
            PdfRenderingIntent.Saturation => "A2B2",
            _ => "A2B1",
        };

        foreach (string signature in (string[])[preferred, "A2B0"])
        {
            if (!profile.TryTag(signature, out ReadOnlySpan<byte> tag))
                continue;

            if (!IccLut.TryRead(tag, profile.LabConnection, out IccLut? lut, out declined))
                return null;

            if (lut!.Inputs != profile.Components)
            {
                declined = "a lookup table whose inputs are not the profile's own components";
                return null;
            }

            return new IccTransform(profile, lut, null, null, intent);
        }

        if (profile.Components == 1 && profile.Has("kTRC"))
        {
            return TryCurve(profile, "kTRC", out IccCurve? gray, out declined)
                ? new IccTransform(profile, null, [gray!], null, intent)
                : null;
        }

        if (profile.Components == 3 && profile.Has("rXYZ"))
        {
            // Colorants are connection-space XYZ; a profile that connects in
            // CIELAB cannot state them.
            if (profile.LabConnection)
            {
                declined = "a matrix profile whose connection space is CIELAB";
                return null;
            }

            if (!profile.TryXyzTag("rXYZ", out IccXyz red) ||
                !profile.TryXyzTag("gXYZ", out IccXyz green) ||
                !profile.TryXyzTag("bXYZ", out IccXyz blue))
            {
                declined = "a matrix profile missing a colorant";
                return null;
            }

            if (!TryCurve(profile, "rTRC", out IccCurve? r, out declined) ||
                !TryCurve(profile, "gTRC", out IccCurve? g, out declined) ||
                !TryCurve(profile, "bTRC", out IccCurve? b, out declined))
            {
                return null;
            }

            double[] colorants =
            [
                red.X, green.X, blue.X,
                red.Y, green.Y, blue.Y,
                red.Z, green.Z, blue.Z,
            ];

            return new IccTransform(profile, null, [r!, g!, b!], colorants, intent);
        }

        declined = "a profile with neither a lookup table nor colorants and curves";
        return null;
    }

    private void ToConnectionSpace(ReadOnlySpan<double> input, Span<double> xyz)
    {
        if (_lut is not null)
        {
            Span<double> pcs = stackalloc double[3];
            _lut.Evaluate(input, pcs);
            Decode(pcs, _lut.Encoding, xyz);
            return;
        }

        if (_colorants is null)
        {
            // Gray: a level along the neutral axis, the white scaled by it.
            double level = _curves![0].Apply(input[0]);
            xyz[0] = _white.X * level;
            xyz[1] = _white.Y * level;
            xyz[2] = _white.Z * level;
            return;
        }

        double red = _curves![0].Apply(input[0]);
        double green = _curves[1].Apply(input[1]);
        double blue = _curves[2].Apply(input[2]);
        for (int c = 0; c < 3; c++)
            xyz[c] = (_colorants[c * 3] * red) + (_colorants[(c * 3) + 1] * green) + (_colorants[(c * 3) + 2] * blue);
    }

    /// <summary>Reads a table's normalized output as connection-space XYZ.</summary>
    private void Decode(ReadOnlySpan<double> pcs, IccPcsEncoding encoding, Span<double> xyz)
    {
        switch (encoding)
        {
            case IccPcsEncoding.Xyz:
                // The full sixteen-bit range stands for 1 + 32767/32768.
                for (int c = 0; c < 3; c++)
                    xyz[c] = pcs[c] * (65535.0 / 32768.0);
                return;

            case IccPcsEncoding.Lab:
                FromLab(pcs[0] * 100, (pcs[1] * 255) - 128, (pcs[2] * 255) - 128, xyz);
                return;

            default:
                // lut16Type keeps the older encoding: 0xFF00 is L* = 100, and
                // a* and b* are the high byte less 128.
                FromLab(pcs[0] * (65535.0 / 65280.0) * 100, (pcs[1] * (65535.0 / 256.0)) - 128, (pcs[2] * (65535.0 / 256.0)) - 128, xyz);
                return;
        }
    }

    /// <summary>CIELAB to CIEXYZ relative to the connection space's white, by the CIE definition.</summary>
    private void FromLab(double lightness, double a, double b, Span<double> xyz)
    {
        double fy = (lightness + 16) / 116;
        xyz[0] = _white.X * Inverse(fy + (a / 500));
        xyz[1] = _white.Y * Inverse(fy);
        xyz[2] = _white.Z * Inverse(fy - (b / 200));
    }

    private static double Inverse(double t)
    {
        const double Delta = 6.0 / 29.0;
        return t > Delta ? t * t * t : 3 * Delta * Delta * (t - (4.0 / 29.0));
    }

    private static bool TryCurve(IccProfile profile, string signature, out IccCurve? curve, out string? declined)
    {
        curve = null;
        declined = null;
        if (!profile.TryTag(signature, out ReadOnlySpan<byte> tag))
        {
            declined = "a matrix profile missing a tone curve";
            return false;
        }

        return IccCurve.TryRead(tag, out curve, out _, out declined);
    }
}
