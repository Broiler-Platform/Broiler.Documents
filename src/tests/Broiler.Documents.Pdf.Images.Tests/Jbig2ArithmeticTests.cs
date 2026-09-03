namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers the MQ arithmetic decoder and the generic regions it decodes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Read what these prove before relying on them.</strong> Every fixture
/// here is produced by an encoder written in this file, so a passing round trip
/// establishes that the encoder and the decoder implement the same state machine
/// and the same context templates. It does <em>not</em> establish that either
/// matches ITU-T T.88. A misreading shared by both halves passes every test
/// below.
/// </para>
/// <para>
/// That limit is not laziness, it is the corpus rule. The standard's own test
/// sequence is official test material and the source register excludes it; a
/// real-world <c>.jb2</c> file cannot be committed either, because possession is
/// not permission to redistribute (IP-020). What remains available is
/// self-consistency here plus a reviewer checking the transcribed table and the
/// templates against the standard by eye, which is why both are written out
/// line by line in the decoder.
/// </para>
/// <para>
/// The round trip is still worth having. It is a real exercise of the state
/// machine — renormalization, the MPS and LPS exchanges, the switch flag, and the
/// byte-stuffing rule all have to be right in both halves for arbitrary data to
/// survive, and an encoder written from the encoding procedures is an
/// independent enough path through the specification that agreeing by accident is
/// unlikely.
/// </para>
/// </remarks>
public sealed class Jbig2ArithmeticTests
{
    [Fact]
    public void The_Probability_Table_Has_The_States_The_Standard_Defines()
    {
        // The table is transcribed, so its shape is asserted rather than assumed:
        // 47 states, the first and last as the standard fixes them, and the one
        // state that loops to itself at the bottom of the estimator.
        Assert.Equal(47, Jbig2ArithmeticProbe.StateCount);
        Assert.Equal((0x5601, 1, 1, 1), Jbig2ArithmeticProbe.State(0));
        Assert.Equal((0x5601, 46, 46, 0), Jbig2ArithmeticProbe.State(46));

        // State 45 is the estimator's floor: an MPS keeps it there.
        Assert.Equal((0x0001, 45, 43, 0), Jbig2ArithmeticProbe.State(45));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(64)]
    [InlineData(4096)]
    public void Arbitrary_Bits_Survive_A_Round_Trip(int count)
    {
        // A fixed generator rather than a random one: a failure has to be
        // reproducible, and the point is coverage of the state machine rather
        // than of chance.
        var bits = new int[count];
        for (int i = 0; i < count; i++)
            bits[i] = (i * 7 % 11) < 4 ? 1 : 0;

        Assert.Equal(bits, RoundTrip(bits, contextBits: 1, _ => 0));
    }

    [Fact]
    public void A_Long_Run_Of_One_Value_Survives()
    {
        // The estimator walks to its floor on a long run, which is where the
        // renormalization and byte-stuffing paths get exercised hardest.
        var bits = new int[8192];
        Assert.Equal(bits, RoundTrip(bits, contextBits: 1, _ => 0));
    }

    [Fact]
    public void Bits_Coded_Against_Different_Contexts_Stay_Separate()
    {
        // Each context carries its own state. Coding against several and getting
        // them all back is what proves the decoder is indexing rather than
        // sharing one estimator.
        var bits = new int[512];
        for (int i = 0; i < bits.Length; i++)
            bits[i] = i % 3 == 0 ? 1 : 0;

        Assert.Equal(bits, RoundTrip(bits, contextBits: 4, i => i % 16));
    }

    // ---- generic regions --------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_Generic_Region_Survives_A_Round_Trip_On_Every_Template(int template)
    {
        byte[] image = Pattern(61, 23);
        byte[] encoded = Jbig2GenericEncoder.Encode(image, 61, 23, template);

        byte[]? decoded = Jbig2GenericDecoder.Decode(
            encoded, 61, 23, template, typicalPrediction: false, adaptive: []);

        Assert.NotNull(decoded);
        Assert.Equal(image, decoded);
    }

    [Fact]
    public void A_Moved_Adaptive_Pixel_Is_Looked_Up_Where_The_Header_Says()
    {
        // The adaptive pixel is what a header moves to suit the image. Encoder and
        // decoder must agree on the new position, and a decoder that ignored the
        // header would still produce a bitmap — the wrong one.
        (int X, int Y)[] adaptive = [(-2, -1), (-3, -1), (2, -2), (-2, -2)];

        byte[] image = Pattern(37, 19);
        byte[] encoded = Jbig2GenericEncoder.Encode(image, 37, 19, template: 0, adaptive: adaptive);

        byte[]? decoded = Jbig2GenericDecoder.Decode(
            encoded, 37, 19, template: 0, typicalPrediction: false, adaptive);

        Assert.Equal(image, decoded);

        // And the nominal positions decode it differently, which is what makes
        // the assertion above mean something.
        byte[]? nominal = Jbig2GenericDecoder.Decode(
            encoded, 37, 19, template: 0, typicalPrediction: false, adaptive: []);

        Assert.NotEqual(image, nominal);
    }

    [Fact]
    public void A_Region_Whose_Rows_Repeat_Survives_Typical_Prediction()
    {
        // Typical prediction codes a row identical to its predecessor as one bit.
        // The fixture repeats deliberately, so the path is actually taken.
        var image = new byte[40 * 12];
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 40; x++)
                image[(y * 40) + x] = (byte)((y / 4) % 2 == 0 && x % 5 == 0 ? 1 : 0);
        }

        byte[] encoded = Jbig2GenericEncoder.Encode(image, 40, 12, template: 0, typicalPrediction: true);

        byte[]? decoded = Jbig2GenericDecoder.Decode(
            encoded, 40, 12, template: 0, typicalPrediction: true, adaptive: []);

        Assert.Equal(image, decoded);
    }

    [Fact]
    public void A_Truncated_Region_Decodes_What_It_Has_Rather_Than_Faulting()
    {
        // The marker rule feeds 1-bits past the end, so a cut stream produces a
        // bitmap instead of an exception. A decoder that threw here would let a
        // malformed image cost the document.
        byte[] image = Pattern(32, 16);
        byte[] encoded = Jbig2GenericEncoder.Encode(image, 32, 16, template: 0);

        byte[]? decoded = Jbig2GenericDecoder.Decode(
            encoded[..(encoded.Length / 3)], 32, 16, template: 0, typicalPrediction: false, adaptive: []);

        Assert.NotNull(decoded);
        Assert.Equal(32 * 16, decoded!.Length);
    }

    [Fact]
    public void An_Unsupported_Template_Is_Refused_Rather_Than_Guessed()
    {
        Assert.Null(Jbig2GenericDecoder.Decode(
            new byte[8], 8, 8, template: 4, typicalPrediction: false, adaptive: []));
    }

    // ---- fixtures ---------------------------------------------------------------

    private static int[] RoundTrip(int[] bits, int contextBits, Func<int, int> context)
    {
        var encoder = new Jbig2ArithmeticEncoder(contextBits);
        for (int i = 0; i < bits.Length; i++)
            encoder.Encode(context(i), bits[i]);

        byte[] data = encoder.Flush();

        var decoder = new Jbig2ArithmeticDecoder(data);
        var contexts = new Jbig2ArithmeticContexts(contextBits);
        var decoded = new int[bits.Length];
        for (int i = 0; i < bits.Length; i++)
            decoded[i] = decoder.Decode(contexts, context(i));

        return decoded;
    }

    /// <summary>A bitmap with enough structure that a template failure shows.</summary>
    private static byte[] Pattern(int width, int height)
    {
        var image = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                image[(y * width) + x] = (byte)(((x * x) + (y * 3)) % 7 < 3 ? 1 : 0);
        }

        return image;
    }
}
