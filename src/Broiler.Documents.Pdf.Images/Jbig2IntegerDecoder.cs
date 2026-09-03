using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>What one arithmetic integer decoding produced.</summary>
internal enum Jbig2IntegerOutcome
{
    /// <summary>A value, in the out parameter.</summary>
    Value,

    /// <summary>
    /// OOB. Not an error: it is how the format says "no more" — the end of a
    /// height class in a symbol dictionary, the end of a strip in a text region.
    /// </summary>
    OutOfBand,

    /// <summary>
    /// A value too large to be anything but a malformed or hostile stream. The
    /// procedure's last branch can encode up to 2^32 + 4436, which no field this
    /// decoder reads could legitimately hold.
    /// </summary>
    OutOfRange,
}

/// <summary>
/// The arithmetic integer decoding procedure, T.88 Annex A.
/// </summary>
/// <remarks>
/// <para>
/// Every number a symbol dictionary or text region needs — a height difference, a
/// width, a coordinate, a run length — arrives through this one procedure, coded
/// against the decoder's own adaptive contexts. Each procedure in a segment keeps
/// its own instance: the standard names them IADH, IADW, IAEX, IAAI, IADT, IAFS,
/// IADS, IAIT and IARI, and they are separate so that the statistics of a width
/// never answer a question about a coordinate.
/// </para>
/// <para>
/// <strong>Procedure, not table.</strong> The prefix below decides how many
/// magnitude bits follow and what to add to them, and both the shape and the
/// offsets are stated in Annex A's own decision tree rather than in a table that
/// had to be copied. What it drives — the MQ decoder's probability table — is the
/// transcribed part, and SRC-019 carries that question.
/// </para>
/// <para>
/// <strong>The context is the path.</strong> PREV accumulates the bits decoded so
/// far, which is what makes the coder adaptive: the estimator for the third bit
/// of a small positive number is not the estimator for the third bit of a large
/// negative one. It saturates at nine bits by the standard's own rule, and that
/// rule is why the contexts are 512 wide and not larger.
/// </para>
/// </remarks>
internal sealed class Jbig2IntegerDecoder
{
    /// <summary>Nine bits of accumulated path, which is where PREV saturates.</summary>
    private const int ContextBits = 9;

    private readonly MqContexts _contexts = new(ContextBits);

    public Jbig2IntegerOutcome Decode(MqDecoder decoder, out int value)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        value = 0;
        int prev = 1;

        int Bit()
        {
            int bit = decoder.Decode(_contexts, prev);

            // PREV grows by one bit until it reaches nine, then keeps the low
            // eight and pins the ninth. Without the pin the context index would
            // run past the array; with it, a long number's later bits share the
            // estimators of its earlier ones, which is the standard's intent.
            prev = prev < 256
                ? (prev << 1) | bit
                : ((((prev << 1) | bit) & 511) | 256);

            return bit;
        }

        long Magnitude(int bits)
        {
            long magnitude = 0;
            for (int i = 0; i < bits; i++)
                magnitude = (magnitude << 1) | (uint)Bit();

            return magnitude;
        }

        int sign = Bit();
        long magnitudeValue;

        // The prefix: each 1 bit moves to a wider field with a larger offset, so
        // that small numbers — which is most of them — cost few bits.
        if (Bit() == 0)
            magnitudeValue = Magnitude(2);
        else if (Bit() == 0)
            magnitudeValue = Magnitude(4) + 4;
        else if (Bit() == 0)
            magnitudeValue = Magnitude(6) + 20;
        else if (Bit() == 0)
            magnitudeValue = Magnitude(8) + 84;
        else if (Bit() == 0)
            magnitudeValue = Magnitude(12) + 340;
        else
            magnitudeValue = Magnitude(32) + 4436;

        // A negative zero is the format's out-of-band signal rather than a
        // number, which is the one piece of this procedure that a caller must
        // handle instead of the decoder.
        if (sign == 1 && magnitudeValue == 0)
            return Jbig2IntegerOutcome.OutOfBand;

        if (magnitudeValue > int.MaxValue)
            return Jbig2IntegerOutcome.OutOfRange;

        value = sign == 1 ? (int)-magnitudeValue : (int)magnitudeValue;
        return Jbig2IntegerOutcome.Value;
    }
}

/// <summary>
/// The IAID decoding procedure, T.88 Annex A.3: which symbol an instance refers
/// to.
/// </summary>
/// <remarks>
/// A symbol identifier is coded as a fixed number of bits rather than through the
/// integer procedure, because the number of symbols is known before decoding
/// starts and a fixed-width code needs no prefix. The context is the path
/// through those bits — a binary tree over the symbol set — so the coder learns
/// which symbols a document actually uses, which is the whole economy of the
/// format for scanned text.
/// </remarks>
internal sealed class Jbig2SymbolIdDecoder
{
    private readonly MqContexts _contexts;
    private readonly int _codeLength;

    public Jbig2SymbolIdDecoder(int codeLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(codeLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(codeLength, Jbig2Limits.MaxSymbolCodeLength);

        _codeLength = codeLength;
        _contexts = new MqContexts(codeLength + 1);
    }

    public int Decode(MqDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        int prev = 1;
        for (int i = 0; i < _codeLength; i++)
            prev = (prev << 1) | decoder.Decode(_contexts, prev);

        // The leading 1 was the tree's root rather than part of the identifier.
        return prev - (1 << _codeLength);
    }
}
