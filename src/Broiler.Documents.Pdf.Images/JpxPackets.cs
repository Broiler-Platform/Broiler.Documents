using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// A tag tree: the quad-tree code JPEG 2000 signals per-code-block values with.
/// </summary>
/// <remarks>
/// <para>
/// Two of them accompany every precinct — one saying which code-blocks are
/// included in a layer, one saying how many leading bit-planes are zero — and
/// both are decoded incrementally, so a tree keeps the state it reached and
/// continues from there when the next layer asks again.
/// </para>
/// <para>
/// This is the one piece of the tier-2 half that a test can genuinely check: an
/// encoder written from the coding procedure is short enough to be an independent
/// reading rather than a mirror, and the values are integers whose round trip
/// means something.
/// </para>
/// </remarks>
internal sealed class JpxTagTree
{
    private readonly int[] _value;
    private readonly int[] _low;
    private readonly bool[] _known;
    private readonly int[] _levelWidth;
    private readonly int[] _levelOffset;
    private readonly int _levels;

    public JpxTagTree(int width, int height)
    {
        var widths = new List<int>();
        var heights = new List<int>();

        int w = Math.Max(1, width);
        int h = Math.Max(1, height);
        while (true)
        {
            widths.Add(w);
            heights.Add(h);
            if (w == 1 && h == 1)
                break;
            w = (w + 1) / 2;
            h = (h + 1) / 2;
        }

        _levels = widths.Count;
        _levelWidth = new int[_levels];
        _levelOffset = new int[_levels];

        int total = 0;
        for (int i = 0; i < _levels; i++)
        {
            _levelWidth[i] = widths[i];
            _levelOffset[i] = total;
            total += widths[i] * heights[i];
        }

        _value = new int[total];
        _low = new int[total];
        _known = new bool[total];
    }

    /// <summary>
    /// Decodes the value at a leaf, reading only as many bits as it needs, or
    /// returns false when the reader ran out.
    /// </summary>
    /// <param name="threshold">
    /// Decoding stops once the value is known to be at least this, which is how
    /// inclusion is signalled without coding the value in full.
    /// </param>
    public bool TryDecode(JpxBitReader reader, int x, int y, int threshold, out int value)
    {
        value = 0;

        // Root downward: each level's value is a lower bound for the level below.
        int low = 0;
        for (int level = _levels - 1; level >= 0; level--)
        {
            int index = Index(level, x, y);
            if (_low[index] < low)
                _low[index] = low;

            while (!_known[index] && _low[index] < threshold)
            {
                if (!reader.TryReadBit(out int bit))
                    return false;

                if (bit == 1)
                    _known[index] = true;
                else
                    _low[index]++;
            }

            _value[index] = _low[index];
            low = _low[index];

            if (!_known[index])
            {
                // Not resolved within the threshold: the caller learns only that
                // the value is at least this much.
                value = _low[index];
                return true;
            }
        }

        value = low;
        return true;
    }

    /// <summary>Whether the leaf's value has been fully resolved.</summary>
    public bool IsKnown(int x, int y) => _known[Index(0, x, y)];

    private int Index(int level, int x, int y)
    {
        int shift = level;
        int lx = x >> shift;
        int ly = y >> shift;
        return _levelOffset[level] + (ly * _levelWidth[level]) + lx;
    }
}

/// <summary>
/// The bit reader packet headers are coded with, including the bit-stuffing rule
/// that keeps a header from containing a marker.
/// </summary>
/// <remarks>
/// After a byte of 0xFF only seven bits follow. Getting that wrong desynchronises
/// every subsequent header rather than failing, which is why it lives in one
/// place rather than being open-coded per field.
/// </remarks>
internal sealed class JpxBitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _position;
    private int _bits;
    private int _current;

    public JpxBitReader(ReadOnlyMemory<byte> data) => _data = data;

    public int Position => _position;

    public bool TryReadBit(out int bit)
    {
        bit = 0;

        if (_bits == 0)
        {
            if (_position >= _data.Length)
                return false;

            int previous = _current;
            _current = _data.Span[_position++];

            // The stuffing rule: a byte following 0xFF carries seven bits.
            _bits = previous == 0xFF ? 7 : 8;
        }

        _bits--;
        bit = (_current >> _bits) & 1;
        return true;
    }

    public bool TryReadBits(int count, out int value)
    {
        value = 0;
        for (int i = 0; i < count; i++)
        {
            if (!TryReadBit(out int bit))
                return false;
            value = (value << 1) | bit;
        }

        return true;
    }

    /// <summary>
    /// Ends the header, discarding the partial byte and the stuffed byte that
    /// follows a trailing 0xFF.
    /// </summary>
    public void AlignToByte()
    {
        if (_current == 0xFF && _bits == 0 && _position < _data.Length && _data.Span[_position] == 0x00)
            _position++;

        _bits = 0;
        _current = 0;
    }
}

/// <summary>One code-block's contribution to one layer.</summary>
internal sealed class JpxBlockContribution
{
    public int Passes { get; set; }

    public int Length { get; set; }

    public int Offset { get; set; }
}

/// <summary>
/// The state one code-block accumulates across layers: whether it has been
/// included yet, how many bit-planes are zero, and the data it has gathered.
/// </summary>
internal sealed class JpxCodeBlock
{
    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool Included { get; set; }

    public int MissingBitPlanes { get; set; }

    public int Passes { get; set; }

    public int LengthBits { get; set; } = 3;

    public List<JpxBlockContribution> Contributions { get; } = [];
}
