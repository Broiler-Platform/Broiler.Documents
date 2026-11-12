using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Broiler.Documents.Resources;

/// <summary>
/// Rasterizes a Windows Metafile (a DOCX <c>.wmf</c> media part) to a PNG by
/// translating its records into the matching Enhanced Metafile records and
/// drawing those with <see cref="EmfRasterizer"/>: the 16-bit WMF records are
/// the EMF ones with their parameters reversed and narrowed. Covered are the
/// window mapping, saved states, pens, brushes and fonts in the WMF object
/// table, polygons, polylines, poly-polygons, rectangles (and rounded ones, as
/// rectangles), ellipses, MOVETO/LINETO, PATBLT, TEXTOUT and EXTTEXTOUT, and
/// device-independent bitmaps (STRETCHDIB, DIBSTRETCHBLT, DIBBITBLT).
/// Arcs, regions, clipping and pattern brushes are not. Returns null when the
/// data is not a WMF or draws nothing this subset understands.
/// </summary>
/// <remarks>
/// A placeable WMF (key <c>0x9AC6CDD7</c>) states its bounding box in logical
/// units and how many of them make an inch. The picture is that box: as a
/// player does, the viewport is the box and the window starts as the box, so a
/// SETWINDOWORG/SETWINDOWEXT in the file maps its own logical space onto it. A
/// WMF without that header takes its box from its window records, at 1440
/// units an inch.
/// </remarks>
internal static class WmfRasterizer
{
    private const uint PlaceableKey = 0x9AC6CDD7;
    private const int MaxRecords = 200_000;
    private const int MaxObjects = 4096;

    public static bool IsWmf(ReadOnlySpan<byte> data)
    {
        int at = data.Length >= 22 && BinaryPrimitives.ReadUInt32LittleEndian(data) == PlaceableKey ? 22 : 0;
        if (data.Length < at + 18)
            return false;
        ushort type = U16(data, at), headerWords = U16(data, at + 2), version = U16(data, at + 4);
        return type is 1 or 2 && headerWords == 9 && version is 0x0100 or 0x0300;
    }

    public static byte[]? RenderPng(ReadOnlySpan<byte> data) =>
        ToEmf(data) is byte[] emf ? EmfRasterizer.RenderPng(emf) : null;

    /// <summary>The metafile as an EMF of the same drawing, or null when it is not a WMF or has no box.</summary>
    internal static byte[]? ToEmf(ReadOnlySpan<byte> data)
    {
        if (!IsWmf(data))
            return null;

        bool placeable = BinaryPrimitives.ReadUInt32LittleEndian(data) == PlaceableKey;
        int start = placeable ? 22 : 0;
        int offset = start + U16(data, start + 2) * 2;

        double left, top, right, bottom, inch;
        if (placeable)
        {
            (left, top, right, bottom) = (I16(data, 6), I16(data, 8), I16(data, 10), I16(data, 12));
            inch = U16(data, 14);
        }
        else if (WindowBox(data, offset) is (double, double, double, double) box)
        {
            (left, top, right, bottom) = box;
            inch = 1440;
        }
        else
        {
            return null;
        }

        if (right < left)
            (left, right) = (right, left);
        if (bottom < top)
            (top, bottom) = (bottom, top);
        if (inch <= 0 || right - left < 1 || bottom - top < 1)
            return null;

        var emf = new EmfWriter();
        // The device is the box, ten device units to the 254 mm of the header's
        // millimetre size: one device unit per logical unit of the box.
        emf.Header(
            Hundredths(left, inch), Hundredths(top, inch), Hundredths(right, inch), Hundredths(bottom, inch),
            deviceX: (int)Math.Round(inch * 10), deviceY: (int)Math.Round(inch * 10));
        int boxLeft = (int)left, boxTop = (int)top, boxWidth = (int)(right - left), boxHeight = (int)(bottom - top);
        emf.Record(17, 8); // SETMAPMODE MM_ANISOTROPIC
        emf.Record(12, boxLeft, boxTop); // SETVIEWPORTORGEX
        emf.Record(11, boxWidth, boxHeight); // SETVIEWPORTEXTEX
        emf.Record(10, boxLeft, boxTop); // SETWINDOWORGEX
        emf.Record(9, boxWidth, boxHeight); // SETWINDOWEXTEX

        // WMF objects take the lowest free slot of the table; EMF names them.
        var slots = new List<bool>();
        uint currentPen = 0x80000007; // BLACK_PEN
        var penStack = new Stack<uint>();
        int Allocate()
        {
            int slot = slots.IndexOf(false);
            if (slot < 0)
            {
                if (slots.Count >= MaxObjects)
                    return -1;
                slots.Add(true);
                return slots.Count - 1;
            }
            slots[slot] = true;
            return slot;
        }

        for (int record = 0; record < MaxRecords && offset + 6 <= data.Length; record++)
        {
            long words = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            ushort function = U16(data, offset + 4);
            if (words < 3 || words * 2 > data.Length - offset)
                break;
            ReadOnlySpan<byte> p = data.Slice(offset + 6, (int)(words * 2) - 6);
            offset += (int)(words * 2);
            if (function == 0)
                break;

            switch (function)
            {
                case 0x020B when p.Length >= 4: emf.Record(10, I16(p, 2), I16(p, 0)); break; // SETWINDOWORG
                case 0x020C when p.Length >= 4: emf.Record(9, I16(p, 2), I16(p, 0)); break; // SETWINDOWEXT
                case 0x001E: // SAVEDC
                    emf.Record(33);
                    penStack.Push(currentPen);
                    break;
                case 0x0127 when p.Length >= 2: // RESTOREDC
                    int restore = I16(p, 0);
                    emf.Record(34, restore);
                    for (int pop = restore; pop < 0 && penStack.Count > 0; pop++)
                        currentPen = penStack.Pop();
                    break;
                case 0x02FA when p.Length >= 10: // CREATEPENINDIRECT
                {
                    int slot = Allocate();
                    if (slot >= 0)
                        emf.Record(38, slot + 1, U16(p, 0), I16(p, 2), 0, (int)U32(p, 6));
                    break;
                }
                case 0x02FC when p.Length >= 6: // CREATEBRUSHINDIRECT
                {
                    int slot = Allocate();
                    if (slot >= 0)
                        emf.Record(39, slot + 1, U16(p, 0), (int)U32(p, 2), p.Length >= 8 ? U16(p, 6) : 0);
                    break;
                }
                case 0x02FB when p.Length >= 18: // CREATEFONTINDIRECT
                {
                    int slot = Allocate();
                    if (slot >= 0)
                        emf.Font(slot + 1, p);
                    break;
                }
                case 0x00F7 or 0x01F9 or 0x0142 or 0x06FF: // CREATEPALETTE, pattern brushes, CREATEREGION: a slot, nothing drawn
                    Allocate();
                    break;
                case 0x012D when p.Length >= 2: // SELECTOBJECT
                {
                    int slot = U16(p, 0);
                    if (slot < slots.Count && slots[slot])
                    {
                        emf.Record(37, slot + 1);
                        if (emf.IsPen(slot + 1))
                            currentPen = (uint)(slot + 1);
                    }
                    break;
                }
                case 0x01F0 when p.Length >= 2: // DELETEOBJECT
                {
                    int slot = U16(p, 0);
                    if (slot < slots.Count && slots[slot])
                    {
                        slots[slot] = false;
                        emf.Record(40, slot + 1);
                        emf.Forget(slot + 1);
                    }
                    break;
                }
                case 0x0209 when p.Length >= 4: emf.Record(24, (int)U32(p, 0)); break; // SETTEXTCOLOR
                case 0x0201 when p.Length >= 4: emf.Record(25, (int)U32(p, 0)); break; // SETBKCOLOR
                case 0x012E when p.Length >= 2: emf.Record(22, U16(p, 0)); break; // SETTEXTALIGN
                case 0x0214 when p.Length >= 4: emf.Record(27, I16(p, 2), I16(p, 0)); break; // MOVETO
                case 0x0213 when p.Length >= 4: emf.Record(54, I16(p, 2), I16(p, 0)); break; // LINETO
                case 0x0324 or 0x0325 when p.Length >= 2: // POLYGON, POLYLINE
                {
                    int count = I16(p, 0);
                    if (count >= 2 && p.Length >= 2 + count * 4)
                        emf.Poly(function == 0x0324 ? 86u : 87u, p.Slice(2, count * 4), count);
                    break;
                }
                case 0x0538 when p.Length >= 2: // POLYPOLYGON
                {
                    int polygons = U16(p, 0);
                    if (polygons <= 0 || p.Length < 2 + polygons * 2)
                        break;
                    var counts = new int[polygons];
                    int total = 0;
                    for (int i = 0; i < polygons; i++)
                        total += counts[i] = U16(p, 2 + i * 2);
                    int at = 2 + polygons * 2;
                    if (p.Length >= at + total * 4)
                        emf.PolyPolygon(counts, p.Slice(at, total * 4));
                    break;
                }
                case 0x041B or 0x0418 when p.Length >= 8: // RECTANGLE, ELLIPSE
                    emf.Record(function == 0x041B ? 43u : 42u, I16(p, 6), I16(p, 4), I16(p, 2), I16(p, 0));
                    break;
                case 0x061C when p.Length >= 12: // ROUNDRECT, drawn square-cornered
                    emf.Record(43, I16(p, 10), I16(p, 8), I16(p, 6), I16(p, 4));
                    break;
                case 0x061D when p.Length >= 12: // PATBLT: the brush over the rectangle, no outline
                {
                    uint rop = U32(p, 0);
                    if (rop != PatCopy)
                        break;
                    int height = I16(p, 4), width = I16(p, 6), y = I16(p, 8), x = I16(p, 10);
                    emf.Record(37, unchecked((int)0x80000008)); // NULL_PEN
                    emf.Record(43, x, y, x + width, y + height);
                    emf.Record(37, unchecked((int)currentPen));
                    break;
                }
                case 0x0521 when p.Length >= 2: // TEXTOUT
                {
                    int length = I16(p, 0);
                    int padded = (length + 1) & ~1;
                    if (length > 0 && p.Length >= 2 + padded + 4)
                        emf.Text(I16(p, 2 + padded + 2), I16(p, 2 + padded), 0, p.Slice(2, length), default, null);
                    break;
                }
                case 0x0A32 when p.Length >= 8: // EXTTEXTOUT
                {
                    int y = I16(p, 0), x = I16(p, 2), length = I16(p, 4);
                    ushort options = U16(p, 6);
                    int at = 8;
                    (int, int, int, int) rectangle = default;
                    if ((options & 0x6) != 0 && p.Length >= at + 8)
                    {
                        rectangle = (I16(p, at), I16(p, at + 2), I16(p, at + 4), I16(p, at + 6));
                        at += 8;
                    }
                    if (length <= 0 || p.Length < at + length)
                        break;
                    int padded = (length + 1) & ~1;
                    int[]? dx = null;
                    if (p.Length >= at + padded + length * 2)
                    {
                        dx = new int[length];
                        for (int i = 0; i < length; i++)
                            dx[i] = I16(p, at + padded + i * 2);
                    }
                    emf.Text(x, y, options & 0x6, p.Slice(at, length), rectangle, dx);
                    break;
                }
                case 0x0F43 when p.Length >= 22: // STRETCHDIB
                    emf.Dib(rop: U32(p, 0), xDest: I16(p, 20), yDest: I16(p, 18), cxDest: I16(p, 16), cyDest: I16(p, 14),
                        xSrc: I16(p, 12), ySrc: I16(p, 10), cxSrc: I16(p, 8), cySrc: I16(p, 6), p[22..], usage: U16(p, 4));
                    break;
                case 0x0B41 when p.Length >= 20 && words != (function >> 8) + 3: // DIBSTRETCHBLT with its bitmap
                    emf.Dib(rop: U32(p, 0), xDest: I16(p, 18), yDest: I16(p, 16), cxDest: I16(p, 14), cyDest: I16(p, 12),
                        xSrc: I16(p, 10), ySrc: I16(p, 8), cxSrc: I16(p, 6), cySrc: I16(p, 4), p[20..], usage: 0);
                    break;
                case 0x0940 when p.Length >= 16 && words != (function >> 8) + 3: // DIBBITBLT with its bitmap
                    emf.Dib(rop: U32(p, 0), xDest: I16(p, 14), yDest: I16(p, 12), cxDest: I16(p, 10), cyDest: I16(p, 8),
                        xSrc: I16(p, 6), ySrc: I16(p, 4), cxSrc: I16(p, 10), cySrc: I16(p, 8), p[16..], usage: 0);
                    break;
            }
        }

        emf.Record(14, 0, 0, 20); // EOF
        return emf.ToArray();
    }

    private const uint PatCopy = 0x00F00021;

    /// <summary>The box a WMF without a placeable header draws in: its first window origin and extent.</summary>
    private static (double, double, double, double)? WindowBox(ReadOnlySpan<byte> data, int offset)
    {
        int? x = null, y = null, width = null, height = null;
        for (int record = 0; record < MaxRecords && offset + 6 <= data.Length; record++)
        {
            long words = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            ushort function = U16(data, offset + 4);
            if (words < 3 || words * 2 > data.Length - offset || function == 0)
                break;
            if (words >= 5 && function == 0x020B && x is null)
                (y, x) = (I16(data, offset + 6), I16(data, offset + 8));
            else if (words >= 5 && function == 0x020C && width is null)
                (height, width) = (I16(data, offset + 6), I16(data, offset + 8));
            if (width is not null && x is not null)
                break;
            offset += (int)(words * 2);
        }
        if (width is not int w || height is not int h)
            return null;
        double left = x ?? 0, top = y ?? 0;
        return (left, top, left + w, top + h);
    }

    private static int Hundredths(double units, double inch) => (int)Math.Round(units * 2540 / inch);

    private static ushort U16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);

    private static short I16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt16LittleEndian(data[at..]);

    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);

    /// <summary>Writes EMF records; every record is a type, its size, and 32-bit fields.</summary>
    private sealed class EmfWriter
    {
        private readonly MemoryStream _stream = new();
        private readonly HashSet<int> _pens = [];

        public bool IsPen(int handle) => _pens.Contains(handle);

        public void Forget(int handle) => _pens.Remove(handle);

        public void Header(int frameLeft, int frameTop, int frameRight, int frameBottom, int deviceX, int deviceY)
        {
            Span<byte> header = stackalloc byte[88];
            header.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(header, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 88);
            BinaryPrimitives.WriteInt32LittleEndian(header[24..], frameLeft);
            BinaryPrimitives.WriteInt32LittleEndian(header[28..], frameTop);
            BinaryPrimitives.WriteInt32LittleEndian(header[32..], frameRight);
            BinaryPrimitives.WriteInt32LittleEndian(header[36..], frameBottom);
            BinaryPrimitives.WriteUInt32LittleEndian(header[40..], 0x464D4520);
            BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 0x10000);
            BinaryPrimitives.WriteInt32LittleEndian(header[72..], deviceX);
            BinaryPrimitives.WriteInt32LittleEndian(header[76..], deviceY);
            BinaryPrimitives.WriteInt32LittleEndian(header[80..], 254);
            BinaryPrimitives.WriteInt32LittleEndian(header[84..], 254);
            _stream.Write(header);
        }

        public void Record(uint type, params int[] fields)
        {
            Begin(type, 8 + fields.Length * 4);
            foreach (int field in fields)
                Int(field);
            if (type == 38 && fields.Length > 0)
                _pens.Add(fields[0]);
            else if (type is 39 && fields.Length > 0)
                _pens.Remove(fields[0]);
        }

        public void Font(int handle, ReadOnlySpan<byte> logFont)
        {
            _pens.Remove(handle);
            Begin(82, 104);
            Int(handle);
            for (int i = 0; i < 5; i++)
                Int(I16(logFont, i * 2));
            _stream.Write(logFont.Slice(10, 8));
            int faceLength = Math.Min(32, logFont.Length - 18);
            int written = 0;
            for (; written < faceLength && logFont[18 + written] != 0; written++)
            {
                _stream.WriteByte(logFont[18 + written]);
                _stream.WriteByte(0);
            }
            for (; written < 32; written++)
            {
                _stream.WriteByte(0);
                _stream.WriteByte(0);
            }
        }

        public void Poly(uint type, ReadOnlySpan<byte> points, int count)
        {
            Begin(type, 28 + count * 4);
            Bounds();
            Int(count);
            _stream.Write(points);
        }

        public void PolyPolygon(int[] counts, ReadOnlySpan<byte> points)
        {
            int total = points.Length / 4;
            Begin(91, 32 + counts.Length * 4 + points.Length);
            Bounds();
            Int(counts.Length);
            Int(total);
            foreach (int count in counts)
                Int(count);
            _stream.Write(points);
        }

        public void Text(int x, int y, int options, ReadOnlySpan<byte> text, (int Left, int Top, int Right, int Bottom) rectangle, int[]? dx)
        {
            int padded = (text.Length + 3) & ~3;
            int size = 76 + padded + (dx is null ? 0 : dx.Length * 4);
            Begin(83, size);
            Bounds();
            Int(1); // GM_COMPATIBLE
            Int(0);
            Int(0);
            Int(x);
            Int(y);
            Int(text.Length);
            Int(76);
            Int(options);
            Int(rectangle.Left);
            Int(rectangle.Top);
            Int(rectangle.Right);
            Int(rectangle.Bottom);
            Int(dx is null ? 0 : 76 + padded);
            _stream.Write(text);
            for (int i = text.Length; i < padded; i++)
                _stream.WriteByte(0);
            if (dx is not null)
                foreach (int advance in dx)
                    Int(advance);
        }

        public void Dib(uint rop, int xDest, int yDest, int cxDest, int cyDest, int xSrc, int ySrc, int cxSrc, int cySrc,
            ReadOnlySpan<byte> dib, int usage)
        {
            if (dib.Length < 40)
                return;
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
            if (headerSize < 40 || headerSize > dib.Length)
                return;
            int bitCount = U16(dib, 14);
            uint compression = U32(dib, 16);
            int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
            int palette = bitCount <= 8 ? (colorsUsed > 0 ? Math.Min(colorsUsed, 1 << bitCount) : 1 << bitCount) : 0;
            int info = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0) + palette * (usage == 1 ? 2 : 4);
            if (info > dib.Length)
                return;
            int bits = dib.Length - info;
            Begin(81, 80 + dib.Length);
            Bounds();
            Int(xDest);
            Int(yDest);
            Int(xSrc);
            Int(ySrc);
            Int(cxSrc);
            Int(cySrc);
            Int(80);
            Int(info);
            Int(80 + info);
            Int(bits);
            Int(usage);
            Int(unchecked((int)rop));
            Int(cxDest);
            Int(cyDest);
            _stream.Write(dib);
            for (int i = dib.Length; i % 4 != 0; i++)
                _stream.WriteByte(0);
        }

        public byte[] ToArray()
        {
            byte[] bytes = _stream.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(48), bytes.Length);
            return bytes;
        }

        private void Begin(uint type, int size)
        {
            Int(unchecked((int)type));
            Int((size + 3) & ~3);
        }

        private void Bounds()
        {
            for (int i = 0; i < 4; i++)
                Int(0);
        }

        private void Int(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            _stream.Write(bytes);
        }
    }
}
