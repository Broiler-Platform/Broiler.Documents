using System;
using System.Globalization;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// A run of glyphs the interpreter placed contiguously on one baseline, with the
/// geometry the reading-order pass needs and nothing more.
/// </summary>
/// <remarks>
/// Fragments exist only between content interpretation and model projection. No
/// coordinate survives into <c>RichTextDocument</c>: the rich-text model is a
/// logical model, and keeping a hidden geometry side channel in it would be a
/// fixed-layout claim the format cannot honour on re-pagination.
/// </remarks>
internal sealed class PdfTextFragment
{
    public PdfTextFragment(
        string text,
        double x,
        double y,
        double endX,
        double fontSize,
        double spaceWidth,
        string fontFamily,
        bool bold,
        bool italic,
        BColor color,
        int renderMode)
    {
        Text = text;
        X = x;
        Y = y;
        EndX = endX;
        FontSize = fontSize;
        SpaceWidth = spaceWidth;
        FontFamily = fontFamily;
        Bold = bold;
        Italic = italic;
        Color = color;
        RenderMode = renderMode;
    }

    public string Text { get; }

    /// <summary>Left edge of the run in page space, in points.</summary>
    public double X { get; }

    /// <summary>Baseline position in page space, in points, y increasing upward.</summary>
    public double Y { get; }

    /// <summary>Right edge of the run in page space.</summary>
    public double EndX { get; }

    /// <summary>Effective font size in points, after the text and current matrices.</summary>
    public double FontSize { get; }

    /// <summary>Width of one space in this run's font and size, for gap detection.</summary>
    public double SpaceWidth { get; }

    public string FontFamily { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public BColor Color { get; }

    /// <summary>
    /// The text rendering mode (Tr). Mode 3 is invisible and mode 7 is
    /// clipping-only; both are extracted but flagged, because deciding they are
    /// not "really" in the document would be a visibility claim this release does
    /// not make.
    /// </summary>
    public int RenderMode { get; }

    /// <summary>True for a rendering mode that paints nothing.</summary>
    public bool IsInvisible => RenderMode is 3 or 7;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"'{Text}' @ ({X:F1},{Y:F1}) {FontSize:F1}pt");
}
