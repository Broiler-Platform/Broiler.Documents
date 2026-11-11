using System;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf;

/// <summary>
/// The section's page border: <c>\pgbrdrt</c>, <c>\pgbrdrl</c>, <c>\pgbrdrb</c>
/// and <c>\pgbrdrr</c>, each followed by the border words that describe that
/// side (<c>\brdrs</c>, <c>\brdrw</c>, <c>\brdrcf</c>, <c>\brsp</c>), and
/// <c>\pgbrdropt</c>, which says what the spacing is measured from.
/// </summary>
/// <remarks>
/// <para>
/// LibreOffice reads these into the same page-border properties as DOCX's
/// <c>w:pgBorders</c> (<c>rtfdispatchflag.cxx</c>, <c>RTFBorderState::PAGE</c>):
/// <c>\brsp</c> is the side's space in points, and only bits 5 to 7 of
/// <c>\pgbrdropt</c> are read, a value of 1 there measuring the space from the
/// page edge rather than from the text. The bits that choose the pages a border
/// is drawn on are not read by LibreOffice, and not here either.
/// </para>
/// <para>
/// Each side keeps its own width and colour. Every kind of line is drawn as a
/// single one, the convention the table borders follow.
/// </para>
/// </remarks>
internal sealed class RtfPageBorders
{
    /// <summary>RTF 1.9.1: <c>\brdrwN</c> cannot be greater than 75 twips.</summary>
    private const int MaxBorderTwips = 75;

    /// <summary>What a border that states a kind and no width is drawn as: a hairline.</summary>
    private const double DefaultBorderWidth = 0.5;

    private readonly RtfColorTable _colors;
    private readonly Side[] _sides = new Side[4];
    private int _current = -1;
    private bool _fromPageEdge;

    public RtfPageBorders(RtfColorTable colors) => _colors = colors;

    /// <summary>True when any side draws.</summary>
    public bool HasBorders => Array.Exists(_sides, side => side.Drawn);

    /// <summary>
    /// Applies <paramref name="keyword"/> when it is a page-border word, and
    /// returns whether it was one. A word that is not ends the side the last
    /// <c>\pgbrdr</c> opened, so a later paragraph or cell border is not read
    /// into the page's. With <paramref name="record"/> false the words are
    /// consumed and nothing is kept: a later section's border, whose page this
    /// reader does not read.
    /// </summary>
    public bool Handle(string keyword, int parameter, bool hasParameter, bool record = true)
    {
        switch (keyword)
        {
            case "pgbrdrt": _current = 0; return true;
            case "pgbrdrl": _current = 1; return true;
            case "pgbrdrb": _current = 2; return true;
            case "pgbrdrr": _current = 3; return true;
            case "pgbrdropt":
                if (record)
                    _fromPageEdge = hasParameter && ((parameter & 0xE0) >> 5) == 1;
                return true;
            case "pgbrdrhead":
            case "pgbrdrfoot":
            case "pgbrdrsnap":
                return true;
        }

        if (_current < 0)
            return false;

        if (keyword == "brsp")
        {
            if (record && hasParameter)
                _sides[_current].Space = Math.Max(0, parameter) / 20d;
            return true;
        }

        // A paragraph border's side word, or anything that is not a border
        // word, closes the page border's side.
        if (!keyword.StartsWith("brdr", StringComparison.Ordinal) ||
            keyword is "brdrt" or "brdrb" or "brdrl" or "brdrr" or "brdrbtw" or "brdrbar")
        {
            _current = -1;
            return false;
        }

        if (!record)
            return true;

        ref Side side = ref _sides[_current];
        switch (keyword)
        {
            case "brdrw":
                if (hasParameter)
                    side.Width = Math.Clamp(parameter, 0, MaxBorderTwips) / 20d;
                break;
            case "brdrcf":
                if (hasParameter)
                    side.Color = _colors.Get(parameter);
                break;
            case "brdrnone":
            case "brdrnil":
            case "brdrtbl":
                side.Drawn = false;
                break;
            case "brdrsp":
            case "brdrart":
                break;
            default:
                side.Drawn = true;
                break;
        }

        return true;
    }

    /// <summary>\sectd: the section's page has no border until it states one.</summary>
    public void Reset()
    {
        Array.Clear(_sides);
        _current = -1;
        _fromPageEdge = false;
    }

    /// <summary>
    /// <paramref name="geometry"/> with the border box drawn around it: each side
    /// its space outside the text, or its space inside the page edge when
    /// <c>\pgbrdropt</c> says so. The line is drawn outward from the space, so a
    /// side measured from the text keeps that much room between them.
    /// </summary>
    public PageGeometry ApplyTo(PageGeometry geometry)
    {
        if (!HasBorders)
            return geometry;

        TableBorder Border(int index) => _sides[index].ToTableBorder();
        double Inset(int index, double margin)
        {
            Side side = _sides[index];
            if (_fromPageEdge)
                return side.Space;
            double width = side.Drawn ? Border(index).Width : 0;
            return Math.Max(0, margin - side.Space - width);
        }

        return geometry with
        {
            Borders = new CellBorders(Border(1), Border(0), Border(3), Border(2)),
            BorderInsets = new TableCellPadding(
                Inset(1, geometry.MarginLeft),
                Inset(0, geometry.MarginTop),
                Inset(3, geometry.MarginRight),
                Inset(2, geometry.MarginBottom)),
        };
    }

    private struct Side
    {
        public bool Drawn;
        public double Width;
        public double Space;
        public BColor Color;

        /// <summary>The edge drawn: black where the document gives no colour or the auto one.</summary>
        public readonly TableBorder ToTableBorder() => Drawn
            ? new TableBorder(Color.IsEmpty ? BColor.Black : Color, Width > 0 ? Width : DefaultBorderWidth)
            : TableBorder.None;
    }
}
