using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Something a page painted that is not text, reduced to its box and its place
/// in the paint order.
/// </summary>
/// <remarks>
/// <para>
/// Kept for one question: what lies under a fill. A white fill on bare paper
/// paints nothing a reader can see, and the same fill over a picture hides it.
/// Only what was painted before the fill, and where, can tell the two apart.
/// </para>
/// <para>
/// A mark whose extent is not known is recorded as covering everything. A
/// shading fills whatever the clip allows, and this interpreter does not track
/// the clip. The answer that assumption gives can only ever make a fill look
/// more significant than it was, never less.
/// </para>
/// </remarks>
/// <param name="IsPaper">
/// True for a fill in the paper's own colour. Painting it changed nothing
/// that was not already paper-coloured underneath, so it hides nothing a later
/// fill could be said to cover.
/// </param>
internal readonly record struct PdfPaintedMark(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    int Order,
    bool IsPaper)
{
    /// <summary>
    /// The colour of the page before anything is painted on it. PDF defines no
    /// page colour, and every viewer and printer starts from white.
    /// </summary>
    public static BColor Paper { get; } = new(255, 255, 255);

    /// <summary>A mark covering the whole page, for paint whose extent is unknown.</summary>
    public static PdfPaintedMark Everywhere(int order) => new(
        double.NegativeInfinity,
        double.NegativeInfinity,
        double.PositiveInfinity,
        double.PositiveInfinity,
        order,
        IsPaper: false);

    /// <summary>Whether this mark overlaps the box, sharing more than an edge with it.</summary>
    public bool Overlaps(double minX, double minY, double maxX, double maxY) =>
        MinX < maxX && MaxX > minX && MinY < maxY && MaxY > minY;
}
