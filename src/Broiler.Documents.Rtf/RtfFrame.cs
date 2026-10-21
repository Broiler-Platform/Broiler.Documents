namespace Broiler.Documents.Rtf;

/// <summary>What a frame's horizontal position is measured from: <c>\phcol</c>, <c>\phmrg</c>, <c>\phpg</c>.</summary>
internal enum RtfFrameHorizontal
{
    Column,
    Margin,
    Page,
}

/// <summary>What a frame's vertical position is measured from: <c>\pvmrg</c>, <c>\pvpg</c>, <c>\pvpara</c>.</summary>
internal enum RtfFrameVertical
{
    Margin,
    Page,
    Paragraph,
}

/// <summary>A frame's horizontal alignment in its reference: <c>\posxl</c> and the rest, or none for <c>\posx</c>.</summary>
internal enum RtfFrameAlignX
{
    None,
    Left,
    Center,
    Right,
    Inside,
    Outside,
}

/// <summary>A frame's vertical alignment in its reference: <c>\posyt</c> and the rest, or none for <c>\posy</c>.</summary>
internal enum RtfFrameAlignY
{
    None,
    Inline,
    Top,
    Center,
    Bottom,
    Inside,
    Outside,
}

/// <summary>How text goes around a frame: <c>\wraparound</c>, <c>\nowrap</c>, <c>\overlay</c> and the rest.</summary>
internal enum RtfFrameWrap
{
    Auto,
    Around,
    NotBeside,
    None,
    Through,
}

/// <summary>
/// The frame a paragraph's position words put it in (RTF 1.9.1, "Positioned
/// Objects and Frames"). A paragraph property, so <c>\pard</c> clears it, and
/// consecutive paragraphs that state the same frame share it.
/// </summary>
/// <remarks>
/// <para>
/// Width and height are twips. A height below nought is exact, above nought the
/// least the frame is, and nought leaves it to the text, which is how
/// <c>\absh</c> states it and how LibreOffice reads it into Word's
/// <c>w:framePr</c> height rule.
/// </para>
/// <para>
/// Whether the words make a frame at all is LibreOffice's rule
/// (<c>RTFFrame::hasProperties</c>): a position, a size, or a reference or
/// alignment other than the default makes one, and the distances from the text
/// do not.
/// </para>
/// </remarks>
internal readonly record struct RtfFrame(
    int X = 0,
    int Y = 0,
    int Width = 0,
    int Height = 0,
    RtfFrameHorizontal Horizontal = RtfFrameHorizontal.Column,
    RtfFrameVertical Vertical = RtfFrameVertical.Margin,
    RtfFrameAlignX AlignX = RtfFrameAlignX.None,
    RtfFrameAlignY AlignY = RtfFrameAlignY.None,
    int HorizontalSpace = 0,
    int VerticalSpace = 0,
    RtfFrameWrap Wrap = RtfFrameWrap.Auto)
{
    /// <summary>True when the words make the paragraph a frame.</summary>
    public bool IsFrame =>
        X != 0 || Y != 0 || Width != 0 || Height != 0 ||
        AlignX is not (RtfFrameAlignX.None or RtfFrameAlignX.Left) ||
        AlignY is not (RtfFrameAlignY.None or RtfFrameAlignY.Inline) ||
        Horizontal != RtfFrameHorizontal.Column ||
        Vertical != RtfFrameVertical.Margin ||
        Wrap != RtfFrameWrap.Auto;
}
