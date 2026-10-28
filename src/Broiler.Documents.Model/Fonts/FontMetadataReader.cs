using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Model.Fonts;

/// <summary>
/// Parsed metadata extracted from an OpenType / TrueType font binary.
/// </summary>
public sealed record FontMetadataInfo(
    string Family,
    string Subfamily,
    string? FullName,
    string? PostScriptName,
    int UnitsPerEm,
    int TypographicAscender,
    int TypographicDescender,
    int TypographicLineGap,
    int WinAscent,
    int WinDescent,
    int CapHeight,
    int XHeight,
    int UnderlinePosition,
    int UnderlineThickness,
    BFontEmbeddingRights EmbeddingRights,
    bool IsBold,
    bool IsItalic,
    int FaceIndex = 0,
    int TotalFacesInCollection = 1);

/// <summary>
/// Bounded, allocation-conscious reader for OpenType and TrueType (sfnt) table directories and metadata.
/// </summary>
/// <remarks>
/// Consolidates font enumeration and table extraction across Layout's SystemFontIndex, Graphics' FontNameTable,
/// and Documents CLI's FontResolution into a safe, bounded parser.
/// </remarks>
public static class FontMetadataReader
{
    private const int MaxFileSize = 64 * 1024 * 1024; // 64 MB cap
    private const int MaxTables = 512;
    private const int MaxCollectionFaces = 512;

    private const uint TagTrueType1 = 0x00010000;
    private const uint TagOpenTypeOtto = 0x4F54544F; // 'OTTO'
    private const uint TagTrueTypeTrue = 0x74727565; // 'true'
    private const uint TagCollection = 0x74746366;   // 'ttcf'

    private const uint TagName = 0x6E616D65; // 'name'
    private const uint TagHead = 0x68656164; // 'head'
    private const uint TagHhea = 0x68686561; // 'hhea'
    private const uint TagOs2  = 0x4F532F32; // 'OS/2'
    private const uint TagPost = 0x706F7374; // 'post'

    /// <summary>
    /// Reads font metadata from a stream with bounded length.
    /// </summary>
    public static FontMetadataInfo? Read(Stream stream, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || stream.Length == 0 || stream.Length > MaxFileSize)
            return null;

        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            stream.Position = 0;

            uint tag = ReadUInt32BigEndian(reader);
            long sfntOffset = 0;
            int totalFaces = 1;

            if (tag == TagCollection)
            {
                _ = ReadUInt32BigEndian(reader); // version
                uint faceCount = ReadUInt32BigEndian(reader);
                if (faceCount == 0 || faceCount > MaxCollectionFaces)
                    return null;

                totalFaces = (int)faceCount;
                if (faceIndex < 0 || faceIndex >= totalFaces)
                    return null;

                stream.Position = 12 + (faceIndex * 4);
                sfntOffset = ReadUInt32BigEndian(reader);
                if (sfntOffset < 0 || sfntOffset > stream.Length - 12)
                    return null;

                stream.Position = sfntOffset;
                tag = ReadUInt32BigEndian(reader);
            }

            if (tag != TagTrueType1 && tag != TagOpenTypeOtto && tag != TagTrueTypeTrue)
                return null;

            ushort numTables = ReadUInt16BigEndian(reader);
            if (numTables == 0 || numTables > MaxTables)
                return null;

            stream.Position += 6; // searchRange, entrySelector, rangeShift

            var tables = new Dictionary<uint, (long Offset, uint Length)>();
            for (int i = 0; i < numTables; i++)
            {
                uint tableTag = ReadUInt32BigEndian(reader);
                _ = ReadUInt32BigEndian(reader); // checksum
                uint offset = ReadUInt32BigEndian(reader);
                uint length = ReadUInt32BigEndian(reader);

                if (offset + length <= (ulong)stream.Length)
                {
                    tables[tableTag] = (offset, length);
                }
            }

            // 1. Read 'name' table
            string family = string.Empty;
            string subfamily = "Regular";
            string? fullName = null;
            string? postScriptName = null;

            if (tables.TryGetValue(TagName, out var nameTable))
            {
                ReadNameTable(stream, nameTable.Offset, nameTable.Length,
                    out family, out subfamily, out fullName, out postScriptName);
            }

            if (string.IsNullOrWhiteSpace(family))
                return null;

            // 2. Read 'head' table
            int unitsPerEm = 1000;
            if (tables.TryGetValue(TagHead, out var headTable) && headTable.Length >= 54)
            {
                stream.Position = headTable.Offset + 18;
                unitsPerEm = ReadUInt16BigEndian(reader);
                if (unitsPerEm <= 0)
                    unitsPerEm = 1000;
            }

            // 3. Read 'hhea' table
            int hheaAscender = (int)(unitsPerEm * 0.8);
            int hheaDescender = (int)(unitsPerEm * -0.2);
            int hheaLineGap = 0;
            if (tables.TryGetValue(TagHhea, out var hheaTable) && hheaTable.Length >= 36)
            {
                stream.Position = hheaTable.Offset + 4;
                hheaAscender = ReadInt16BigEndian(reader);
                hheaDescender = ReadInt16BigEndian(reader);
                hheaLineGap = ReadInt16BigEndian(reader);
            }

            // 4. Read 'OS/2' table
            int typoAscender = hheaAscender;
            int typoDescender = hheaDescender;
            int typoLineGap = hheaLineGap;
            int winAscent = hheaAscender;
            int winDescent = Math.Abs(hheaDescender);
            int capHeight = (int)(unitsPerEm * 0.7);
            int xHeight = (int)(unitsPerEm * 0.5);
            ushort fsType = 0;
            ushort fsSelection = 0;
            ushort usWeightClass = 400;

            if (tables.TryGetValue(TagOs2, out var os2Table) && os2Table.Length >= 78)
            {
                stream.Position = os2Table.Offset;
                ushort version = ReadUInt16BigEndian(reader);
                stream.Position = os2Table.Offset + 4;
                usWeightClass = ReadUInt16BigEndian(reader);
                stream.Position = os2Table.Offset + 8;
                fsType = ReadUInt16BigEndian(reader);

                stream.Position = os2Table.Offset + 62;
                fsSelection = ReadUInt16BigEndian(reader);

                if (os2Table.Length >= 78)
                {
                    stream.Position = os2Table.Offset + 68;
                    typoAscender = ReadInt16BigEndian(reader);
                    typoDescender = ReadInt16BigEndian(reader);
                    typoLineGap = ReadInt16BigEndian(reader);
                    winAscent = ReadUInt16BigEndian(reader);
                    winDescent = ReadUInt16BigEndian(reader);
                }

                if (version >= 2 && os2Table.Length >= 90)
                {
                    stream.Position = os2Table.Offset + 86;
                    xHeight = ReadInt16BigEndian(reader);
                    capHeight = ReadInt16BigEndian(reader);
                }
            }

            // 5. Read 'post' table
            int underlinePos = (int)(-unitsPerEm * 0.1);
            int underlineThickness = (int)(unitsPerEm * 0.05);
            if (tables.TryGetValue(TagPost, out var postTable) && postTable.Length >= 16)
            {
                stream.Position = postTable.Offset + 8;
                underlinePos = ReadInt16BigEndian(reader);
                underlineThickness = ReadInt16BigEndian(reader);
            }

            bool isBold = (fsSelection & 0x20) != 0 || usWeightClass >= 700 ||
                          subfamily.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            bool isItalic = (fsSelection & 0x01) != 0 ||
                            subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                            subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            BFontEmbeddingRights embeddingRights = BFontEmbeddingRights.FromFsType(fsType);

            bool useTypoMetrics = (fsSelection & 0x0080) != 0;
            int layoutAscender = useTypoMetrics && typoAscender != 0 ? typoAscender : (hheaAscender != 0 ? hheaAscender : typoAscender);
            int layoutDescender = useTypoMetrics && typoDescender != 0 ? typoDescender : (hheaDescender != 0 ? hheaDescender : typoDescender);
            int layoutLineGap = useTypoMetrics ? Math.Max(0, typoLineGap) : Math.Max(0, hheaLineGap);

            return new FontMetadataInfo(
                Family: family,
                Subfamily: subfamily,
                FullName: fullName,
                PostScriptName: postScriptName,
                UnitsPerEm: unitsPerEm,
                TypographicAscender: layoutAscender,
                TypographicDescender: layoutDescender,
                TypographicLineGap: layoutLineGap,
                WinAscent: winAscent,
                WinDescent: winDescent,
                CapHeight: capHeight > 0 ? capHeight : (int)(unitsPerEm * 0.7),
                XHeight: xHeight > 0 ? xHeight : (int)(unitsPerEm * 0.5),
                UnderlinePosition: underlinePos,
                UnderlineThickness: Math.Max(1, underlineThickness),
                EmbeddingRights: embeddingRights,
                IsBold: isBold,
                IsItalic: isItalic,
                FaceIndex: faceIndex,
                TotalFacesInCollection: totalFaces);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads font metadata from a file path.
    /// </summary>
    public static FontMetadataInfo? ReadFromFile(string filePath, int faceIndex = 0)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            using var stream = File.OpenRead(filePath);
            return Read(stream, faceIndex);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads font metadata from program bytes.
    /// </summary>
    public static FontMetadataInfo? ReadFromBytes(ReadOnlyMemory<byte> program, int faceIndex = 0)
    {
        if (program.Length == 0)
            return null;

        using var memoryStream = new MemoryStream(program.ToArray(), writable: false);
        return Read(memoryStream, faceIndex);
    }

    private static void ReadNameTable(
        Stream stream,
        long tableOffset,
        uint tableLength,
        out string family,
        out string subfamily,
        out string? fullName,
        out string? postScriptName)
    {
        family = string.Empty;
        subfamily = "Regular";
        fullName = null;
        postScriptName = null;

        if (tableLength < 6)
            return;

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Position = tableOffset;

        _ = ReadUInt16BigEndian(reader); // format
        ushort count = ReadUInt16BigEndian(reader);
        ushort stringOffset = ReadUInt16BigEndian(reader);

        long storageOffset = tableOffset + stringOffset;
        if (storageOffset >= stream.Length)
            return;

        string? legacyFamily = null;
        string? legacySubfamily = null;
        string? typoFamily = null;
        string? typoSubfamily = null;
        bool hasWinLegacyFamily = false;
        bool hasWinLegacySubfamily = false;
        bool hasWinFullName = false;
        bool hasWinPostScriptName = false;
        bool hasWinTypoFamily = false;
        bool hasWinTypoSubfamily = false;

        for (int i = 0; i < count; i++)
        {
            long recordPos = tableOffset + 6 + (i * 12);
            if (recordPos + 12 > tableOffset + tableLength)
                break;

            stream.Position = recordPos;
            ushort platformId = ReadUInt16BigEndian(reader);
            ushort encodingId = ReadUInt16BigEndian(reader);
            ushort languageId = ReadUInt16BigEndian(reader);
            ushort nameId = ReadUInt16BigEndian(reader);
            ushort length = ReadUInt16BigEndian(reader);
            ushort offset = ReadUInt16BigEndian(reader);

            if (nameId is not (1 or 2 or 4 or 6 or 16 or 17))
                continue;

            long valueOffset = storageOffset + offset;
            if (valueOffset + length > stream.Length)
                continue;

            stream.Position = valueOffset;
            byte[] bytes = reader.ReadBytes(length);
            string? value = DecodeString(bytes, platformId, encodingId);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            bool isPreferred = (platformId == 3 && (languageId == 0x0409 || encodingId == 1)) || platformId == 0;

            switch (nameId)
            {
                case 1:
                    if (legacyFamily is null || (isPreferred && !hasWinLegacyFamily))
                    {
                        legacyFamily = value;
                        hasWinLegacyFamily |= isPreferred;
                    }
                    break;
                case 2:
                    if (legacySubfamily is null || (isPreferred && !hasWinLegacySubfamily))
                    {
                        legacySubfamily = value;
                        hasWinLegacySubfamily |= isPreferred;
                    }
                    break;
                case 4:
                    if (fullName is null || (isPreferred && !hasWinFullName))
                    {
                        fullName = value;
                        hasWinFullName |= isPreferred;
                    }
                    break;
                case 6:
                    if (postScriptName is null || (isPreferred && !hasWinPostScriptName))
                    {
                        postScriptName = value;
                        hasWinPostScriptName |= isPreferred;
                    }
                    break;
                case 16:
                    if (typoFamily is null || (isPreferred && !hasWinTypoFamily))
                    {
                        typoFamily = value;
                        hasWinTypoFamily |= isPreferred;
                    }
                    break;
                case 17:
                    if (typoSubfamily is null || (isPreferred && !hasWinTypoSubfamily))
                    {
                        typoSubfamily = value;
                        hasWinTypoSubfamily |= isPreferred;
                    }
                    break;
            }
        }

        family = typoFamily ?? legacyFamily ?? string.Empty;
        subfamily = typoSubfamily ?? legacySubfamily ?? "Regular";
    }

    private static string? DecodeString(byte[] bytes, ushort platformId, ushort encodingId)
    {
        try
        {
            // Platform 3 (Windows), Platform 0 (Unicode): UTF-16BE
            if (platformId == 3 || platformId == 0)
            {
                return Encoding.BigEndianUnicode.GetString(bytes).Trim();
            }

            // Platform 1 (Macintosh): MacRoman (Latin1 approximation)
            if (platformId == 1 && encodingId == 0)
            {
                return Encoding.Latin1.GetString(bytes).Trim();
            }

            return Encoding.UTF8.GetString(bytes).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static BFontEmbeddingRights ParseEmbeddingRights(ushort fsType)
    {
        BFontEmbeddingPermission permission = (fsType & 0x000F) switch
        {
            0x0002 => BFontEmbeddingPermission.Restricted,
            0x0004 => BFontEmbeddingPermission.PreviewAndPrint,
            0x0008 => BFontEmbeddingPermission.Editable,
            _ => BFontEmbeddingPermission.Installable,
        };

        bool noSubsetting = (fsType & 0x0100) != 0;
        bool bitmapOnly = (fsType & 0x0200) != 0;

        return BFontEmbeddingRights.FromFsType(fsType);
    }

    private static uint ReadUInt32BigEndian(BinaryReader reader) =>
        ((uint)reader.ReadByte() << 24) |
        ((uint)reader.ReadByte() << 16) |
        ((uint)reader.ReadByte() << 8) |
        reader.ReadByte();

    private static ushort ReadUInt16BigEndian(BinaryReader reader) =>
        (ushort)(((ushort)reader.ReadByte() << 8) | reader.ReadByte());

    private static short ReadInt16BigEndian(BinaryReader reader) =>
        (short)(((short)reader.ReadByte() << 8) | reader.ReadByte());
}
