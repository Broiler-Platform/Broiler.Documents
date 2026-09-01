using System.Collections.Generic;
using Broiler.Graphics;

namespace Broiler.Documents.Model;

/// <summary>
/// A floating shape: a positioned box that sits outside the text flow, and may
/// hold text or a picture of its own.
/// </summary>
/// <remarks>
/// <para>
/// Outside the flow is the point. A letterhead's coloured stripe and its logo box
/// are anchored beside the text rather than in it, and reading them as body
/// content would drop a green rectangle into the middle of the letter.
/// </para>
/// <para>
/// A floating picture is the same box with an <see cref="Image"/> on it. The
/// alternative was the one this replaces: place it in the text, where a logo
/// anchored over the letterhead pushed the whole letter down by its height.
/// </para>
/// <para>
/// <see cref="OffsetX"/> is measured from the text column's left edge, which is
/// what the formats anchor to and what both renderers already know - so a shape
/// in the left margin needs no page geometry to place, only a negative offset.
/// <see cref="OffsetY"/> is measured from the top of the paragraph the shape is
/// anchored to.
/// </para>
/// <para>
/// <see cref="BehindText"/> is the one piece of stacking the model keeps. It is
/// what the formats actually record - DOCX's <c>behindDoc</c>, ODT's
/// <c>style:run-through</c> - and it is the difference between a letterhead's
/// stripe, which the letter is written on top of, and a stamp meant to cover it.
/// Order among shapes is not modelled: they draw in the order they were read.
/// </para>
/// <para>
/// The anchor is a paragraph index, so an edit that inserts or removes
/// paragraphs above a shape moves the text out from under it. That is the same
/// trade every word processor makes with paragraph-anchored objects, and it is
/// better than the alternative this replaces, which was to drop the shape.
/// </para>
/// </remarks>
public sealed class DocumentShape
{
    public DocumentShape(
        int paragraphIndex,
        double offsetX,
        double offsetY,
        double width,
        double height,
        ShapeFill? fill = null,
        BColor outline = default,
        IReadOnlyList<RichTextParagraph>? paragraphs = null,
        InlineImage? image = null,
        bool behindText = true)
    {
        ParagraphIndex = paragraphIndex;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Width = width;
        Height = height;
        Fill = fill;
        Outline = outline;
        Image = image;
        BehindText = behindText;
        Paragraphs = paragraphs is null || paragraphs.Count == 0
            ? []
            : [.. paragraphs];
    }

    /// <summary>The paragraph the shape is anchored to.</summary>
    public int ParagraphIndex { get; }

    /// <summary>Points from the text column's left edge; negative puts the shape in the margin.</summary>
    public double OffsetX { get; }

    /// <summary>Points from the top of the anchoring paragraph.</summary>
    public double OffsetY { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>How the box is painted, or null when it is not filled.</summary>
    public ShapeFill? Fill { get; }

    /// <summary>The outline colour; <see cref="BColor.Empty"/> for no outline.</summary>
    public BColor Outline { get; }

    /// <summary>
    /// The picture the box draws, or null when it draws only paint and text. It
    /// fills <see cref="Width"/> by <see cref="Height"/>: the box is the picture's
    /// frame, so the size the document stated for the frame is the size it draws
    /// at, and <see cref="InlineImage.Width"/> is not consulted again.
    /// </summary>
    public InlineImage? Image { get; }

    /// <summary>
    /// True when the shape draws under the body text, false when it draws over it.
    /// Defaults to true: a shape whose format says nothing about stacking is the
    /// letterhead case, and painting it over the letter would hide the letter.
    /// </summary>
    public bool BehindText { get; }

    /// <summary>The shape's own text, empty when it holds none.</summary>
    public IReadOnlyList<RichTextParagraph> Paragraphs { get; }

    /// <summary>True when the shape holds text rather than only paint.</summary>
    public bool HasText => Paragraphs.Count > 0;

    /// <summary>True when the shape is a floating picture.</summary>
    public bool HasImage => Image is not null;
}
