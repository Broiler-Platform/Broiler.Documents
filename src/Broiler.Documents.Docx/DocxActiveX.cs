using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Broiler.Documents.Docx;

/// <summary>
/// Reads the caption and state of a Forms 2.0 check box or option button held
/// as an ActiveX control (<c>w:object</c> &gt; <c>w:control</c> naming a
/// <c>word/activeX/activeXN.xml</c> part whose binary is a compound file).
/// </summary>
/// <remarks>
/// The control's persisted <c>contents</c> stream is MS-OFORMS
/// <c>MorphDataControl</c>; only its <c>DisplayStyle</c>, <c>Value</c> and
/// <c>Caption</c> are read. Anything else - other control classes, compound
/// files using a DIFAT beyond the header, malformed data - reads as null.
/// </remarks>
internal static class DocxActiveX
{
    /// <summary>A check box or option button: its caption and whether it is set.</summary>
    public sealed record Control(bool IsOptionButton, bool IsChecked, string Caption)
    {
        /// <summary>A ballot box (check box) or a circle (option button), filled when set.</summary>
        public string Glyph => IsOptionButton ? (IsChecked ? "◉" : "○") : (IsChecked ? "☒" : "☐");
    }

    private const string CheckBoxClassId = "{8BD21D40-EC42-11CE-9E0D-00AA006002F3}";
    private const string OptionButtonClassId = "{8BD21D50-EC42-11CE-9E0D-00AA006002F3}";

    private const int DisplayStyleCheckBox = 4;
    private const int DisplayStyleOptionButton = 5;

    /// <summary>Reads a control of class <paramref name="classId"/> from its persisted compound file.</summary>
    public static Control? Read(string? classId, byte[] compoundFile)
    {
        bool checkBox = string.Equals(classId, CheckBoxClassId, StringComparison.OrdinalIgnoreCase);
        bool optionButton = string.Equals(classId, OptionButtonClassId, StringComparison.OrdinalIgnoreCase);
        if (!checkBox && !optionButton)
            return null;
        byte[]? contents = ReadCompoundStream(compoundFile, "contents");
        return contents is null ? null : ReadMorphData(contents, optionButton);
    }

    /// <summary>Parses a MorphDataControl's DisplayStyle, Value and Caption.</summary>
    internal static Control? ReadMorphData(ReadOnlySpan<byte> data, bool optionButton)
    {
        if (data.Length < 12 || data[1] != 2)
            return null;
        int end = Math.Min(data.Length, 4 + BinaryPrimitives.ReadUInt16LittleEndian(data[2..]));
        ulong mask = BinaryPrimitives.ReadUInt64LittleEndian(data[4..]);

        // DataBlock sizes by property bit; zero for properties stored elsewhere or not at all.
        ReadOnlySpan<byte> sizes = [4, 4, 4, 4, 1, 1, 1, 1, 0, 2, 4, 2, 2, 2, 2, 2, 1, 1, 1, 0, 1, 1, 4, 4, 4, 4, 4, 2, 2, 2, 0, 0, 4];
        int pos = 12, displayStyle = optionButton ? DisplayStyleOptionButton : DisplayStyleCheckBox;
        uint valueCount = 0, captionCount = 0;
        for (int bit = 0; bit < sizes.Length; bit++)
        {
            if ((mask & (1UL << bit)) == 0 || sizes[bit] == 0)
                continue;
            int size = sizes[bit];
            pos = Align(pos, size);
            if (pos + size > end)
                return null;
            uint field = size switch
            {
                1 => data[pos],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]),
            };
            switch (bit)
            {
                case 6: displayStyle = (int)field; break;
                case 22: valueCount = field; break;
                case 23: captionCount = field; break;
            }
            pos += size;
        }

        // ExtraDataBlock: Size, then the strings in property order.
        pos = Align(pos, 4);
        if ((mask & (1UL << 8)) != 0)
            pos += 8;
        string? value = ReadString(data, ref pos, end, valueCount);
        string? caption = ReadString(data, ref pos, end, captionCount);
        if (value is null || caption is null)
            return null;
        if (displayStyle is not (DisplayStyleCheckBox or DisplayStyleOptionButton))
            return null;
        return new Control(displayStyle == DisplayStyleOptionButton, value == "1", caption);
    }

    private static int Align(int pos, int size) => size <= 1 ? pos : (pos + size - 1) / size * size;

    private static string? ReadString(ReadOnlySpan<byte> data, ref int pos, int end, uint count)
    {
        bool compressed = (count & 0x80000000) != 0;
        int length = (int)(count & 0x7FFFFFFF);
        if (length == 0)
            return "";
        if (length > end - pos)
            return null;
        ReadOnlySpan<byte> bytes = data.Slice(pos, length);
        pos = Align(pos + length, 4);
        return compressed ? Encoding.Latin1.GetString(bytes) : Encoding.Unicode.GetString(bytes[..(length & ~1)]);
    }

    /// <summary>
    /// Reads one stream of a compound file (MS-CFB) by name, from the mini
    /// stream or the regular sectors. Null when the file or the stream is not
    /// found or a chain is broken.
    /// </summary>
    internal static byte[]? ReadCompoundStream(byte[] file, string name)
    {
        if (file.Length < 512 || BinaryPrimitives.ReadUInt64LittleEndian(file) != 0xE11AB1A1E011CFD0)
            return null;
        int sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x1E));
        int miniShift = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x20));
        if (sectorShift is not (9 or 12) || miniShift != 6)
            return null;
        int sectorSize = 1 << sectorShift, miniSize = 1 << miniShift;
        int fatSectors = I32(file, 0x2C), firstDirectory = I32(file, 0x30), firstMiniFat = I32(file, 0x3C), miniFatSectors = I32(file, 0x40);
        uint cutoff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x38));
        if (fatSectors < 0 || fatSectors > 109)
            return null;

        int sectorCount = file.Length / sectorSize - 1;
        var fat = new List<int>();
        for (int i = 0; i < fatSectors; i++)
        {
            int sector = I32(file, 0x4C + i * 4);
            if (sector < 0 || sector >= sectorCount)
                return null;
            for (int at = 0; at < sectorSize; at += 4)
                fat.Add(I32(file, (sector + 1) * sectorSize + at));
        }

        byte[] miniStream = [];
        int Regular(int sector) => (sector + 1) * sectorSize;

        // Follows a sector chain of `unit`-byte sectors (regular, or mini sectors
        // in the mini stream) for `length` bytes.
        byte[]? Chain(int start, long length, IReadOnlyList<int> table, Func<int, int> offsetOf, int unit, int limit)
        {
            var bytes = new List<byte>();
            int sector = start, steps = 0;
            while (sector >= 0 && bytes.Count < length)
            {
                if (sector >= table.Count || ++steps > limit)
                    return null;
                int offset = offsetOf(sector);
                byte[] source = unit == sectorSize ? file : miniStream;
                if (offset < 0 || offset >= source.Length)
                    return null;
                bytes.AddRange(source.AsSpan(offset, Math.Min(unit, source.Length - offset)));
                sector = table[sector];
            }
            return length > bytes.Count ? null : bytes.GetRange(0, (int)length).ToArray();
        }

        // Reads a whole regular chain, for parts whose length is not stated.
        byte[]? ReadUntilEnd(int start)
        {
            var bytes = new List<byte>();
            int sector = start, steps = 0;
            while (sector >= 0 && sector < fat.Count && sector < sectorCount && ++steps <= sectorCount)
            {
                bytes.AddRange(file.AsSpan(Regular(sector), sectorSize));
                sector = fat[sector];
            }
            return bytes.Count == 0 ? null : bytes.ToArray();
        }

        byte[]? directory = ReadUntilEnd(firstDirectory);
        if (directory is null || directory.Length < 128)
            return null;

        long rootSize = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(0x78));
        int rootStart = I32(directory, 0x74);
        for (int entry = 0; entry + 128 <= directory.Length; entry += 128)
        {
            int nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(entry + 0x40));
            if (directory[entry + 0x42] != 2 || nameBytes < 2 || nameBytes > 64)
                continue;
            string entryName = Encoding.Unicode.GetString(directory, entry, nameBytes - 2);
            if (!string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
                continue;
            int start = I32(directory, entry + 0x74);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(entry + 0x78));
            if (size > file.Length)
                return null;
            if (size >= cutoff)
                return Chain(start, size, fat, Regular, sectorSize, sectorCount);

            if (rootSize > file.Length)
                return null;
            miniStream = ReadUntilEnd(rootStart) ?? [];
            byte[]? miniFatBytes = ReadUntilEnd(firstMiniFat);
            if (miniFatBytes is null || miniFatSectors <= 0)
                return null;
            var miniFat = new List<int>();
            for (int at = 0; at + 4 <= miniFatBytes.Length; at += 4)
                miniFat.Add(I32(miniFatBytes, at));
            return Chain(start, size, miniFat, sector => sector * miniSize < miniStream.Length ? sector * miniSize : -1, miniSize, miniFat.Count);
        }
        return null;
    }

    private static int I32(byte[] data, int at) =>
        at < 0 || at + 4 > data.Length ? -1 : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
}
