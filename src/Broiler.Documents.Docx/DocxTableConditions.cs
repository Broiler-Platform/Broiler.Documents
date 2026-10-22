using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// The parts of a table a cell belongs to, as a table style's conditional
/// formats (ECMA-376 §17.7.6, <c>w:tblStylePr</c>) name them.
/// </summary>
[Flags]
internal enum TableConditions
{
    None = 0,
    FirstRow = 1 << 0,
    LastRow = 1 << 1,
    FirstColumn = 1 << 2,
    LastColumn = 1 << 3,
    Band1Horizontal = 1 << 4,
    Band2Horizontal = 1 << 5,
    Band1Vertical = 1 << 6,
    Band2Vertical = 1 << 7,
    NorthWestCell = 1 << 8,
    NorthEastCell = 1 << 9,
    SouthWestCell = 1 << 10,
    SouthEastCell = 1 << 11,
}

/// <summary>
/// Which conditional formats of a table style reach which cell: the table's
/// <c>w:tblLook</c>, read and applied as LibreOffice applies it.
/// </summary>
/// <remarks>
/// <para>
/// The rules are LibreOffice's (<c>writerfilter/dmapper/DomainMapperTableHandler</c>
/// and <c>DomainMapperTableManager::finishTableLook</c>), which is what the
/// rendering is compared with. A table that states no look takes <c>0x04A0</c>,
/// the value Microsoft's errata document: first row, first column and horizontal
/// banding. A look that states any of the 2010 attributes is read from them
/// alone, each one on unless it says otherwise. A first or last row or column
/// whose style defines no format for it is switched off, which matters because
/// banding counts the first row and column as a band when they are on.
/// </para>
/// <para>
/// A row is the first row, the last row, or a band, in that order; a header row
/// is always a first row. A cell is the first cell of its row, the last, or a
/// band. Where a row and a column condition meet at a corner, the corner's own
/// format applies too.
/// </para>
/// </remarks>
internal readonly struct DocxTableLook
{
    private const int FirstRowBit = 0x020;
    private const int LastRowBit = 0x040;
    private const int FirstColumnBit = 0x080;
    private const int LastColumnBit = 0x100;
    private const int NoHorizontalBandBit = 0x200;
    private const int NoVerticalBandBit = 0x400;

    private readonly int _look;

    private DocxTableLook(int look) => _look = look;

    /// <summary>
    /// The conditional formats in the order LibreOffice merges them, each later
    /// one overriding what an earlier one states, with the element value naming
    /// each in <c>w:tblStylePr w:type</c>.
    /// </summary>
    public static IReadOnlyList<(TableConditions Condition, string Type)> MergeOrder { get; } =
    [
        (TableConditions.Band2Horizontal, "band2Horz"),
        (TableConditions.Band1Horizontal, "band1Horz"),
        (TableConditions.Band2Vertical, "band2Vert"),
        (TableConditions.Band1Vertical, "band1Vert"),
        (TableConditions.LastColumn, "lastCol"),
        (TableConditions.FirstColumn, "firstCol"),
        (TableConditions.LastRow, "lastRow"),
        (TableConditions.FirstRow, "firstRow"),
        (TableConditions.SouthWestCell, "swCell"),
        (TableConditions.SouthEastCell, "seCell"),
        (TableConditions.NorthWestCell, "nwCell"),
        (TableConditions.NorthEastCell, "neCell"),
    ];

    /// <summary>
    /// The look a table states in its <c>w:tblLook</c>, with the edge formats its
    /// style does not define switched off.
    /// </summary>
    /// <param name="properties">The table's <c>w:tblPr</c>.</param>
    /// <param name="defines">Whether the table's style defines a format of a type.</param>
    public static DocxTableLook Read(XElement? properties, Func<string, bool> defines)
    {
        int look = 0x4A0;
        XElement? element = properties?.Element(DocxNamespaces.Wordprocessing + "tblLook");
        if (element is not null)
        {
            bool? firstRow = Switch(element, "firstRow");
            bool? lastRow = Switch(element, "lastRow");
            bool? firstColumn = Switch(element, "firstColumn");
            bool? lastColumn = Switch(element, "lastColumn");
            bool? noHBand = Switch(element, "noHBand");
            bool? noVBand = Switch(element, "noVBand");
            if (firstRow is not null || lastRow is not null || firstColumn is not null ||
                lastColumn is not null || noHBand is not null || noVBand is not null)
            {
                look = 0;
                if (firstRow ?? true) look |= FirstRowBit;
                if (lastRow ?? true) look |= LastRowBit;
                if (firstColumn ?? true) look |= FirstColumnBit;
                if (lastColumn ?? true) look |= LastColumnBit;
                if (noHBand ?? true) look |= NoHorizontalBandBit;
                if (noVBand ?? true) look |= NoVerticalBandBit;
            }
            else if (TryReadHex((string?)element.Attribute(DocxNamespaces.Wordprocessing + "val"), out int value))
            {
                look = value;
            }
        }

        if ((look & FirstRowBit) != 0 && !defines("firstRow")) look &= ~FirstRowBit;
        if ((look & LastRowBit) != 0 && !defines("lastRow")) look &= ~LastRowBit;
        if ((look & FirstColumnBit) != 0 && !defines("firstCol")) look &= ~FirstColumnBit;
        if ((look & LastColumnBit) != 0 && !defines("lastCol")) look &= ~LastColumnBit;
        return new DocxTableLook(look);
    }

    /// <summary>The conditions every cell of a row shares.</summary>
    public TableConditions ForRow(int row, int rowCount, bool isHeader)
    {
        if (isHeader || (row == 0 && (_look & FirstRowBit) != 0))
            return TableConditions.FirstRow;
        if (row > 0 && row == rowCount - 1 && (_look & LastRowBit) != 0)
            return TableConditions.LastRow;
        if ((_look & NoHorizontalBandBit) != 0)
            return TableConditions.None;

        int band = row + 1 + ((_look & FirstRowBit) != 0 ? 1 : 0);
        return (band & 1) != 0 ? TableConditions.Band1Horizontal : TableConditions.Band2Horizontal;
    }

    /// <summary>A cell's conditions, given those of its row.</summary>
    public TableConditions ForCell(TableConditions row, int cell, int cellCount)
    {
        TableConditions column = TableConditions.None;
        if (cell == 0 && (_look & FirstColumnBit) != 0)
            column = TableConditions.FirstColumn;
        else if (cell > 0 && cell == cellCount - 1 && (_look & LastColumnBit) != 0)
            column = TableConditions.LastColumn;
        else if ((_look & NoVerticalBandBit) == 0)
        {
            int band = cell + 1 + ((_look & FirstColumnBit) != 0 ? 1 : 0);
            column = (band & 1) != 0 ? TableConditions.Band1Vertical : TableConditions.Band2Vertical;
        }

        TableConditions conditions = row | column;
        return (row, column) switch
        {
            (TableConditions.FirstRow, TableConditions.FirstColumn) => conditions | TableConditions.NorthWestCell,
            (TableConditions.FirstRow, TableConditions.LastColumn) => conditions | TableConditions.NorthEastCell,
            (TableConditions.LastRow, TableConditions.FirstColumn) => conditions | TableConditions.SouthWestCell,
            (TableConditions.LastRow, TableConditions.LastColumn) => conditions | TableConditions.SouthEastCell,
            _ => conditions,
        };
    }

    /// <summary>A <c>w:tblLook</c> on/off attribute, or null when it is absent.</summary>
    private static bool? Switch(XElement element, string name)
    {
        string? value = (string?)element.Attribute(DocxNamespaces.Wordprocessing + name);
        if (value is null)
            return null;

        return !(value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("0", StringComparison.Ordinal) ||
                 value.Equals("off", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadHex(string? value, out int result) =>
        int.TryParse(value, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out result);
}
