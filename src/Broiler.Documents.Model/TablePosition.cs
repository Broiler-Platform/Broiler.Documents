using System;

namespace Broiler.Documents.Model;

/// <summary>What a floating table's position is measured from, in one direction.</summary>
public enum TableAnchor
{
    /// <summary>
    /// The text: across, the column the table is in; down, the place the table
    /// would have had in the flow.
    /// </summary>
    Text,

    /// <summary>The page's margins: the box the body text is written in.</summary>
    Margin,

    /// <summary>The edge of the paper.</summary>
    Page,
}

/// <summary>Where a floating table is aligned within its anchor, in place of an offset.</summary>
public enum TableAlignment
{
    /// <summary>No alignment: the offset places the table.</summary>
    None,

    /// <summary>Left across, top down.</summary>
    Start,

    Center,

    /// <summary>Right across, bottom down.</summary>
    End,

    /// <summary>Towards the binding: left on an odd page, right on an even one.</summary>
    Inside,

    /// <summary>Away from the binding: right on an odd page, left on an even one.</summary>
    Outside,
}

/// <summary>
/// Where a floating table stands: a table taken out of the text flow and
/// positioned on the page, with the body text running around it.
/// </summary>
/// <remarks>
/// <para>
/// DOCX states one with <c>w:tblpPr</c> (ECMA-376 §17.4.57), and LibreOffice
/// imports it as a text frame holding the table, which is the same idea: the
/// table keeps its own width, stands where the document puts it, and the
/// paragraphs beside it wrap. An inline table is one with no position at all.
/// </para>
/// <para>
/// Offsets are points from the anchor's edge, to the table's left edge and top.
/// An alignment other than <see cref="TableAlignment.None"/> wins over the
/// offset in its direction, which is what <c>tblpXSpec</c> and <c>tblpYSpec</c>
/// do to <c>tblpX</c> and <c>tblpY</c>.
/// </para>
/// </remarks>
public sealed record TablePosition(
    TableAnchor HorizontalAnchor,
    double X,
    TableAnchor VerticalAnchor,
    double Y,
    TableAlignment HorizontalAlignment = TableAlignment.None,
    TableAlignment VerticalAlignment = TableAlignment.None,
    double LeftFromText = 0,
    double RightFromText = 0,
    double TopFromText = 0,
    double BottomFromText = 0,
    bool AllowOverlap = true,
    int ZOrder = 0,
    bool BehindText = false)
{
    /// <summary>
    /// Where the table's left edge goes across a box from <paramref name="boxLeft"/>
    /// <paramref name="boxWidth"/> wide, for a table <paramref name="width"/> wide on
    /// page <paramref name="page"/>.
    /// </summary>
    public double LeftIn(double boxLeft, double boxWidth, double width, int page) =>
        Resolve(HorizontalAlignment, boxLeft, boxWidth, width, X, page);

    /// <summary>
    /// Where the table's top goes down a box from <paramref name="boxTop"/>
    /// <paramref name="boxHeight"/> tall, for a table <paramref name="height"/> tall.
    /// </summary>
    public double TopIn(double boxTop, double boxHeight, double height, int page) =>
        Resolve(VerticalAlignment, boxTop, boxHeight, height, Y, page);

    private static double Resolve(TableAlignment alignment, double start, double extent, double size, double offset, int page)
    {
        bool odd = page % 2 == 1;
        return alignment switch
        {
            TableAlignment.Start => start,
            TableAlignment.Center => start + ((extent - size) / 2),
            TableAlignment.End => start + extent - size,
            TableAlignment.Inside => odd ? start : start + extent - size,
            TableAlignment.Outside => odd ? start + extent - size : start,
            _ => start + (double.IsFinite(offset) ? offset : 0),
        };
    }
}
