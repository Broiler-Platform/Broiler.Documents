namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// An MQ arithmetic encoder, written from the encoding procedures so the decoder
/// has something to be round-tripped against.
/// </summary>
/// <remarks>
/// <para>
/// It exists only here. Nothing ships an encoder: this repository writes no
/// JBIG2, and adding one to the product would be a separate decision with its own
/// register row — the roadmap's Post-V1 note about symbol-substitution encoding
/// says why that is not an incidental addition.
/// </para>
/// <para>
/// It shares the decoder's probability table, which is the honest arrangement: a
/// second transcription would be a second chance to make the same mistake while
/// looking like corroboration.
/// </para>
/// </remarks>
internal sealed class Jbig2ArithmeticEncoder
{
    private readonly byte[] _states;
    private readonly byte[] _mps;
    private readonly List<byte> _output = [];

    private uint _a = 0x8000;
    private uint _c;
    private int _ct = 12;
    private int _b = -1;

    public Jbig2ArithmeticEncoder(int contextBits)
    {
        int size = 1 << contextBits;
        _states = new byte[size];
        _mps = new byte[size];
    }

    public void Encode(int cx, int d)
    {
        (ushort qe, byte nmps, byte nlps, byte exchange) = Jbig2ArithmeticProbe.State(_states[cx]);

        if (d == _mps[cx])
        {
            // CODEMPS.
            _a -= qe;
            if ((_a & 0x8000) == 0)
            {
                if (_a < qe)
                    _a = qe;
                else
                    _c += qe;

                _states[cx] = nmps;
                Renormalize();
                return;
            }

            _c += qe;
            return;
        }

        // CODELPS.
        _a -= qe;
        if (_a < qe)
            _c += qe;
        else
            _a = qe;

        if (exchange == 1)
            _mps[cx] = (byte)(1 - _mps[cx]);

        _states[cx] = nlps;
        Renormalize();
    }

    /// <summary>FLUSH: settle the remaining interval and emit what is buffered.</summary>
    public byte[] Flush()
    {
        // SETBITS.
        uint temp = _c + _a;
        _c |= 0xFFFF;
        if (_c >= temp)
            _c -= 0x8000;

        _c <<= _ct;
        ByteOut();
        _c <<= _ct;
        ByteOut();

        if (_b != 0xFF)
            Emit(0xFF);

        Emit(0xAC);
        return [.. _output];
    }

    private void Renormalize()
    {
        do
        {
            if (_ct == 0)
                ByteOut();

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);

        // The encoder's registers are 32-bit with the carry living above bit 27;
        // masking here keeps the shifts from carrying rubbish upward.
        _c &= 0xFFFFFFF;
    }

    private void ByteOut()
    {
        if (_b == 0xFF)
        {
            Stuff();
            return;
        }

        if (_c > 0x7FFFFFF)
        {
            // A carry propagates into the byte already emitted.
            if (_b >= 0)
            {
                _b++;
                _output[^1] = (byte)_b;
            }

            _c &= 0x7FFFFFF;

            if (_b == 0xFF)
            {
                Stuff();
                return;
            }
        }

        Emit((byte)(_c >> 19));
        _c &= 0x7FFFF;
        _ct = 8;
    }

    /// <summary>The stuffing path: after a 0xFF only seven bits may follow.</summary>
    private void Stuff()
    {
        Emit((byte)(_c >> 20));
        _c &= 0xFFFFF;
        _ct = 7;
    }

    private void Emit(byte value)
    {
        _output.Add(value);
        _b = value;
    }
}

/// <summary>
/// Reaches the decoder's probability table so the encoder and the tests can use
/// the same one.
/// </summary>
internal static class Jbig2ArithmeticProbe
{
    public static int StateCount => 47;

    public static (ushort Qe, byte Nmps, byte Nlps, byte Switch) State(int index) =>
        Jbig2ArithmeticStates.All[index];
}
