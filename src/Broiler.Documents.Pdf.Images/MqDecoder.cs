using System;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// The MQ arithmetic decoder JBIG2 codes almost everything with.
/// </summary>
/// <remarks>
/// <para>
/// Procedure only. The probability table this drives is
/// <see cref="MqStates"/>, kept in its own file because it is a
/// transcribed normative constant and this is not: the part an engineer can be
/// held to and the part a reviewer has to weigh are worth keeping apart.
/// </para>
/// <para>
/// Written from the decoding procedures T.88 Annex E defines — INITDEC, DECODE,
/// the MPS and LPS exchanges, RENORMD and BYTEIN.
/// </para>
/// <para>
/// <strong>What the tests do not prove.</strong> The suite round-trips this
/// against an encoder written beside it, which establishes that the two agree —
/// not that either matches the standard. A shared misreading passes every test.
/// </para>
/// </remarks>
internal sealed class MqDecoder
{
    private static (ushort Qe, byte Nmps, byte Nlps, byte Switch)[] States => MqStates.All;

    private readonly ReadOnlyMemory<byte> _data;
    private int _bp;
    private uint _c;
    private uint _a;
    private int _ct;

    public MqDecoder(ReadOnlyMemory<byte> data)
    {
        _data = data;

        // INITDEC.
        _bp = 0;
        _c = (uint)ByteAt(_bp) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>
    /// Decodes one bit against the context <paramref name="cx"/> holds, updating
    /// that context's state.
    /// </summary>
    public int Decode(MqContexts contexts, int cx)
    {
        ref byte state = ref contexts.State(cx);
        ref byte mps = ref contexts.Mps(cx);

        (ushort qe, byte nmps, byte nlps, byte exchange) = States[state];
        int d;

        _a -= qe;

        if (((_c >> 16) & 0xFFFF) < qe)
        {
            // LPS_EXCHANGE, then renormalize.
            if (_a < qe)
            {
                d = mps;
                state = nmps;
            }
            else
            {
                d = 1 - mps;
                if (exchange == 1)
                    mps = (byte)(1 - mps);
                state = nlps;
            }

            _a = qe;
            Renormalize();
            return d;
        }

        _c -= (uint)qe << 16;

        if ((_a & 0x8000) != 0)
            return mps;

        // MPS_EXCHANGE, then renormalize.
        if (_a < qe)
        {
            d = 1 - mps;
            if (exchange == 1)
                mps = (byte)(1 - mps);
            state = nlps;
        }
        else
        {
            d = mps;
            state = nmps;
        }

        Renormalize();
        return d;
    }

    private void Renormalize()
    {
        do
        {
            if (_ct == 0)
                ByteIn();

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);
    }

    /// <summary>
    /// BYTEIN. The marker rule is the part worth reading twice: a 0xFF followed
    /// by a byte above 0x8F is a marker, and the decoder feeds itself 1-bits
    /// forever rather than consuming it, which is what lets a truncated or
    /// marker-terminated stream end without a special case.
    /// </summary>
    private void ByteIn()
    {
        if (ByteAt(_bp) == 0xFF)
        {
            if (ByteAt(_bp + 1) > 0x8F)
            {
                _c += 0xFF00;
                _ct = 8;
                return;
            }

            _bp++;
            _c += (uint)ByteAt(_bp) << 9;
            _ct = 7;
            return;
        }

        _bp++;
        _c += (uint)ByteAt(_bp) << 8;
        _ct = 8;
    }

    /// <summary>
    /// The byte at a position, or 0xFF past the end. Running off the end is not
    /// an error: the marker rule above already treats 0xFF as "no more data", so
    /// a truncated region decodes to whatever it had rather than faulting.
    /// </summary>
    private byte ByteAt(int index)
    {
        ReadOnlySpan<byte> data = _data.Span;
        return index >= 0 && index < data.Length ? data[index] : (byte)0xFF;
    }
}

/// <summary>
/// The adaptive contexts one arithmetic-coded procedure keeps: a state index and
/// an MPS sense per context value.
/// </summary>
/// <remarks>
/// Held apart from the decoder because JBIG2 runs several independent context
/// sets through one decoder — a generic region's, and one per integer procedure —
/// and mixing them would decode one procedure's bits against another's statistics.
/// </remarks>
internal sealed class MqContexts
{
    private readonly byte[] _states;
    private readonly byte[] _mps;

    public MqContexts(int bits)
    {
        int size = 1 << bits;
        _states = new byte[size];
        _mps = new byte[size];
    }

    public ref byte State(int cx) => ref _states[cx];

    public ref byte Mps(int cx) => ref _mps[cx];
}
