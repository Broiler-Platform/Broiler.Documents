using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers the ICC reader on its own: what a profile's own numbers turn a colour
/// into, and which profiles it declines and why.
/// </summary>
/// <remarks>
/// <para>
/// The expectations are worked out from the definitions the reader implements
/// - a curve's formula, the connection space's encodings, and sRGB's transfer
/// function - rather than read off another colour-management system, which
/// would make the tests an echo of whatever that system chose. Each is stated
/// in the test's comment so it can be checked by hand.
/// </para>
/// <para>
/// One number is compared with a published value: the red colorant of an sRGB
/// profile, which is the check that the colorimetric facts and the Bradford
/// adaptation were put together the way every sRGB profile was.
/// </para>
/// </remarks>
public sealed class IccColorProfileReaderTests
{
    // ---- matrix profiles -------------------------------------------------------

    [Fact]
    public void The_sRGB_Colorants_Sum_To_The_Connection_White()
    {
        // The adaptation maps sRGB's white onto the connection space's, so the
        // three colorants of an sRGB profile add up to D50 - which is the
        // property that makes a white picture come back white.
        double[] toXyz = IccSrgb.Invert(IccSrgb.FromConnectionSpace(IccXyz.D50));

        Assert.Equal(0.9642, toXyz[0] + toXyz[1] + toXyz[2], 4);
        Assert.Equal(1.0000, toXyz[3] + toXyz[4] + toXyz[5], 4);
        Assert.Equal(0.8249, toXyz[6] + toXyz[7] + toXyz[8], 4);
    }

    [Fact]
    public void The_sRGB_Red_Colorant_Is_The_One_sRGB_Profiles_Carry()
    {
        double[] toXyz = IccSrgb.Invert(IccSrgb.FromConnectionSpace(IccXyz.D50));

        Assert.Equal(0.4361, toXyz[0], 3);
        Assert.Equal(0.2225, toXyz[3], 3);
        Assert.Equal(0.0139, toXyz[6], 3);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(12, 200, 77)]
    [InlineData(128, 128, 128)]
    [InlineData(1, 2, 3)]
    public void A_Picture_Tagged_sRGB_Comes_Back_As_It_Was_Stored(int r, int g, int b)
    {
        // The case almost every document is: an sRGB profile on a picture, and
        // the model's pixels drawn as sRGB. The conversion is the identity, up
        // to the fixed-point colorants a profile stores.
        PdfColorTransform transform = Transform(SrgbProfile(), 3);

        Near((r, g, b), Convert(transform, r / 255d, g / 255d, b / 255d));
    }

    [Fact]
    public void A_Gray_Profile_Of_Gamma_One_Is_Linear_Light()
    {
        // Half the light is linear 0.5, which sRGB's transfer function encodes
        // as 0.7354, or 188.
        byte[] profile = new IccProfileBuilder("GRAY", "XYZ ", "mntr")
            .With("kTRC", IccProfileBuilder.Gamma(1.0))
            .Build();

        Near((188, 188, 188), Convert(Transform(profile, 1), 0.5));
    }

    [Fact]
    public void A_Sampled_Curve_Is_Interpolated_Between_Its_Samples()
    {
        // Samples 0, 0.25, 1: an input of 0.25 lies halfway between the first
        // two and reads 0.125 of the light, which encodes as 0.3886, or 99.
        byte[] profile = new IccProfileBuilder("GRAY", "XYZ ", "mntr")
            .With("kTRC", IccProfileBuilder.Sampled(0, 0.25, 1))
            .Build();

        Near((99, 99, 99), Convert(Transform(profile, 1), 0.25));
    }

    [Theory]
    [InlineData(0, new double[] { 2 }, 0.5, 0.25)]
    [InlineData(1, new double[] { 1, 2, -0.5 }, 0.5, 0.5)]
    [InlineData(1, new double[] { 1, 2, -0.5 }, 0.2, 0.0)]
    [InlineData(2, new double[] { 1, 1, 0, 0.25 }, 0.5, 0.75)]
    [InlineData(3, new double[] { 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045 }, 0.02, 0.02 / 12.92)]
    [InlineData(3, new double[] { 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045 }, 0.5, 0.21404)]
    [InlineData(4, new double[] { 1, 1, 0, 0.5, 0.5, 0.1, 0.2 }, 0.25, 0.325)]
    [InlineData(4, new double[] { 1, 1, 0, 0.5, 0.5, 0.1, 0.2 }, 0.75, 0.85)]
    public void The_Parametric_Forms_Follow_Their_Definitions(int type, double[] parameters, double input, double expected)
    {
        Assert.True(IccCurve.TryRead(IccProfileBuilder.Parametric(type, parameters), out IccCurve? curve, out _, out string? declined), declined);

        Assert.Equal(expected, curve!.Apply(input), 3);
    }

    // ---- lookup tables ---------------------------------------------------------

    [Fact]
    public void A_Press_Profile_Interpolates_Its_Four_Dimensional_Table()
    {
        // No ink is paper white and full black ink is black; half the black ink
        // lands halfway, at L* 50 - a luminance of 0.1842, encoded as 119.
        byte[] profile = PressProfile();
        PdfColorTransform transform = Transform(profile, 4);

        Near((255, 255, 255), Convert(transform, 0, 0, 0, 0));
        Near((0, 0, 0), Convert(transform, 0, 0, 0, 1));
        Near((119, 119, 119), Convert(transform, 0, 0, 0, 0.5));
    }

    [Fact]
    public void An_Eight_Bit_Table_Reads_Its_Own_Lab_Encoding()
    {
        // lut8Type states L* over 255 and a* and b* from -128: halfway along a
        // gray ramp from L* 0 to L* 100 is L* 50 again, or 119.
        byte[] profile = new IccProfileBuilder("GRAY", "Lab ", "scnr", 2)
            .With("A2B0", IccProfileBuilder.Lut8(1, 2, index => [index[0], 128 / 255d, 128 / 255d]))
            .Build();

        Near((119, 119, 119), Convert(Transform(profile, 1), 0.5));
    }

    [Theory]
    [InlineData(12, 200, 77)]
    [InlineData(255, 255, 255)]
    [InlineData(40, 40, 200)]
    public void A_Version_Four_Matrix_Pipeline_Converts_As_The_Matrix_Profile_Does(int r, int g, int b)
    {
        // The same sRGB, stated the version-4 way: M curves, then a matrix, in
        // a lutAToBType with no grid. The matrix works in the table's own
        // encoding, where XYZ's full range stands for just under two.
        double[] toXyz = IccSrgb.Invert(IccSrgb.FromConnectionSpace(IccXyz.D50));
        double[] matrix = [.. toXyz.Select(value => value / (65535.0 / 32768.0)), 0, 0, 0];

        byte[] profile = new IccProfileBuilder("RGB ", "XYZ ", "mntr")
            .With("A2B0", IccProfileBuilder.MatrixAtoB(IccProfileBuilder.Parametric(3, IccProfileBuilder.SrgbCurve), matrix))
            .Build();

        Near((r, g, b), Convert(Transform(profile, 3), r / 255d, g / 255d, b / 255d));
    }

    [Fact]
    public void The_Rendering_Intent_Chooses_The_Table()
    {
        // A2B0 paints white and A2B1 black. Perceptual reads the first,
        // relative colorimetric the second, and saturation - which this
        // profile leaves out - falls back to the perceptual table.
        byte[] white = IccProfileBuilder.Lut8(1, 2, _ => [1, 128 / 255d, 128 / 255d]);
        byte[] black = IccProfileBuilder.Lut8(1, 2, _ => [0, 128 / 255d, 128 / 255d]);
        byte[] profile = new IccProfileBuilder("GRAY", "Lab ", "scnr")
            .With("A2B0", white)
            .With("A2B1", black)
            .Build();

        Near((255, 255, 255), Convert(Transform(profile, 1, PdfRenderingIntent.Perceptual), 0.5));
        Near((0, 0, 0), Convert(Transform(profile, 1, PdfRenderingIntent.RelativeColorimetric), 0.5));
        Near((255, 255, 255), Convert(Transform(profile, 1, PdfRenderingIntent.Saturation), 0.5));
    }

    [Fact]
    public void Absolute_Colorimetric_Shows_The_Media_White()
    {
        // A medium half as bright as the connection white: relative colorimetric
        // maps it to white, absolute keeps it at half the light, or 188.
        byte[] profile = new IccProfileBuilder("GRAY", "XYZ ", "mntr")
            .With("kTRC", IccProfileBuilder.Gamma(1.0))
            .With("wtpt", IccProfileBuilder.Xyz(0.9642 / 2, 0.5, 0.8249 / 2))
            .Build();

        Near((255, 255, 255), Convert(Transform(profile, 1, PdfRenderingIntent.RelativeColorimetric), 1));
        Near((188, 188, 188), Convert(Transform(profile, 1, PdfRenderingIntent.AbsoluteColorimetric), 1));
    }

    // ---- what is declined ------------------------------------------------------

    [Fact]
    public void Bytes_That_Are_Not_A_Profile_Are_Declined() =>
        Assert.Equal("bytes that are not an ICC profile", Declined(new byte[200], 3));

    [Fact]
    public void A_Version_Five_Profile_Is_Declined() =>
        Assert.Equal(
            "a profile of a version this reader does not convert",
            Declined(new IccProfileBuilder(version: 5).Build(), 3));

    [Theory]
    [InlineData("link")]
    [InlineData("abst")]
    [InlineData("nmcl")]
    public void A_Profile_That_Describes_No_Device_Colour_Is_Declined(string deviceClass) =>
        Assert.Equal(
            "a device-link, abstract, or named-colour profile",
            Declined(new IccProfileBuilder(deviceClass: deviceClass).Build(), 3));

    [Fact]
    public void A_Profile_Over_Lab_Is_Declined() =>
        Assert.Equal(
            "a profile over a colour space this reader does not convert",
            Declined(new IccProfileBuilder(colorSpace: "Lab ").Build(), 3));

    [Fact]
    public void A_Profile_Without_The_Components_The_Document_Declares_Is_Declined() =>
        Assert.Equal(
            "a profile over a colour space without the components the document declares",
            Declined(SrgbProfile(), 1));

    [Fact]
    public void A_Tag_Past_The_Profiles_End_Is_Declined()
    {
        byte[] profile = SrgbProfile();

        // The first tag's length, grown past the bytes the profile carries.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(140), (uint)profile.Length);

        Assert.Equal("a profile whose tags run past its end", Declined(profile, 3));
    }

    [Fact]
    public void A_Table_With_Five_Inputs_Is_Declined() =>
        Assert.Equal(
            "a lookup table with more inputs than this reader converts",
            Declined(
                new IccProfileBuilder("CMYK", "Lab ", "prtr")
                    .With("A2B0", IccProfileBuilder.Lut16(5, 2, _ => [0, 0.5, 0.5]))
                    .Build(),
                4));

    [Fact]
    public void A_Table_Larger_Than_Its_Tag_Is_Declined()
    {
        byte[] table = IccProfileBuilder.Lut16(4, 2, _ => [0, 0.5, 0.5]);
        byte[] truncated = table[..(table.Length - 40)];

        Assert.Equal(
            "a lookup table larger than its tag",
            Declined(new IccProfileBuilder("CMYK", "Lab ", "prtr").With("A2B0", truncated).Build(), 4));
    }

    [Fact]
    public void A_Profile_With_Nothing_To_Convert_Through_Is_Declined() =>
        Assert.Equal(
            "a profile with neither a lookup table nor colorants and curves",
            Declined(new IccProfileBuilder().Build(), 3));

    [Fact]
    public void A_Profile_Past_The_Ceiling_Is_Declined_Before_It_Is_Read() =>
        Assert.Equal("a profile past the reader's byte ceiling", Declined(SrgbProfile(), 3, maxBytes: 100));

    // ---- fixtures --------------------------------------------------------------

    /// <summary>
    /// A matrix profile with sRGB's colorants and tone curve - built from the
    /// reader's own sRGB, so a round trip through it tests the pipeline rather
    /// than two copies of the same constants.
    /// </summary>
    internal static byte[] SrgbProfile()
    {
        double[] toXyz = IccSrgb.Invert(IccSrgb.FromConnectionSpace(IccXyz.D50));
        byte[] curve = IccProfileBuilder.Parametric(3, IccProfileBuilder.SrgbCurve);

        return new IccProfileBuilder()
            .With("rXYZ", IccProfileBuilder.Xyz(toXyz[0], toXyz[3], toXyz[6]))
            .With("gXYZ", IccProfileBuilder.Xyz(toXyz[1], toXyz[4], toXyz[7]))
            .With("bXYZ", IccProfileBuilder.Xyz(toXyz[2], toXyz[5], toXyz[8]))
            .With("rTRC", curve)
            .With("gTRC", curve)
            .With("bTRC", curve)
            .Build();
    }

    /// <summary>
    /// A CMYK press profile in the older sixteen-bit table: paper white where
    /// there is no ink, black at every other corner of the grid.
    /// </summary>
    internal static byte[] PressProfile()
    {
        // L* 100 is 0xFF00 in this encoding, and a neutral a* and b* 0x8000.
        double lightness = 0xFF00 / 65535d;
        double neutral = 0x8000 / 65535d;

        return new IccProfileBuilder("CMYK", "Lab ", "prtr", 2)
            .With("A2B0", IccProfileBuilder.Lut16(4, 2, index =>
                index.All(i => i == 0) ? [lightness, neutral, neutral] : [0, neutral, neutral]))
            .Build();
    }

    internal static PdfColorTransform Transform(byte[] profile, int components, PdfRenderingIntent intent = PdfRenderingIntent.RelativeColorimetric)
    {
        PdfColorTransform? transform = new IccColorProfileReader().Read(
            profile, components, intent, new PdfColorProfileContext(1 << 20), out string? declined);

        Assert.True(transform is not null, declined);
        return transform!;
    }

    private static string Declined(byte[] profile, int components, long maxBytes = 1 << 20)
    {
        PdfColorTransform? transform = new IccColorProfileReader().Read(
            profile, components, PdfRenderingIntent.RelativeColorimetric, new PdfColorProfileContext(maxBytes), out string? declined);

        Assert.Null(transform);
        return declined!;
    }

    private static (int R, int G, int B) Convert(PdfColorTransform transform, params double[] values)
    {
        byte[] rgb = new byte[3];
        transform.ToRgb(values, rgb);
        return (rgb[0], rgb[1], rgb[2]);
    }

    /// <summary>Equal to within one step of eight bits, per channel.</summary>
    private static void Near((int R, int G, int B) expected, (int R, int G, int B) actual)
    {
        Assert.True(
            Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1,
            $"Expected {expected} within one step, got {actual}.");
    }
}
