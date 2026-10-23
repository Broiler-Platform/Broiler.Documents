using System;
using System.Collections.Generic;
using Broiler.Graphics.Color;

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
/// anchored to, or from the top of the page for one anchored to a page.
/// </para>
/// <para>
/// Stacking is two independent questions and the model keeps both.
/// <see cref="BehindText"/> is the shape against the text - DOCX's
/// <c>behindDoc</c>, ODT's <c>style:run-through</c> - and it is the difference
/// between a letterhead's stripe, which the letter is written on top of, and a
/// stamp meant to cover it. <see cref="ZOrder"/> is the shape against other
/// shapes, which is a different question with a different answer: a letterhead
/// puts a stripe and a logo box both in front of nothing in particular, and
/// which of the two wins where they overlap is the whole of what the reader
/// sees.
/// </para>
/// <para>
/// The anchor is a paragraph index, so an edit that inserts or removes
/// paragraphs above a shape moves the text out from under it. That is the same
/// trade every word processor makes with paragraph-anchored objects, and it is
/// better than the alternative this replaces, which was to drop the shape.
/// </para>
/// <para>
/// A shape can be anchored to a page instead, by number; see
/// <see cref="AnchorPage"/>.
/// </para>
/// </remarks>
public sealed class DocumentShape(
    int paragraphIndex,
    double offsetX,
    double offsetY,
    double width,
    double height,
    ShapeFill? fill = null,
    BColor outline = default,
    IReadOnlyList<RichTextParagraph>? paragraphs = null,
    InlineImage? image = null,
    bool behindText = true,
    ShapeWrap wrap = ShapeWrap.None,
    WrapSide wrapSide = WrapSide.Largest,
    double wrapDistance = 0,
    int zOrder = 0,
    int anchorPage = 0,
    bool offsetFromPage = false,
    bool growsToText = false,
    IReadOnlyList<DocumentTable>? tables = null,
    TableCellPadding textPadding = default,
    CellVerticalAlignment textVerticalAlignment = CellVerticalAlignment.Top,
    bool textOverflow = false,
    bool growsWidthToText = false,
    string? frameName = null,
    string? nextFrameName = null,
    ShapeGeometry geometry = ShapeGeometry.Rectangle,
    double outlineWidth = 1)
{

    /// <summary>The paragraph the shape is anchored to.</summary>
    public int ParagraphIndex { get; } = paragraphIndex;

    /// <summary>Points from the text column's left edge; negative puts the shape in the margin.</summary>
    public double OffsetX { get; } = offsetX;

    /// <summary>
    /// Points from the top of the anchoring paragraph, or of the page when
    /// <see cref="IsPageAnchored"/> or <see cref="OffsetFromPage"/>.
    /// </summary>
    public double OffsetY { get; } = offsetY;

    public double Width { get; } = width;

    public double Height { get; } = height;

    public TableCellPadding TextPadding { get; } = textPadding;

    public double TextWidth => Math.Max(1, Width - TextPadding.Left - TextPadding.Right);

    /// <summary>Vertical placement of text inside the padded frame.</summary>
    public CellVerticalAlignment TextVerticalAlignment { get; } = textVerticalAlignment;
    /// <summary>Whether lines may paint beyond the frame's fixed height.</summary>
    public bool TextOverflow { get; } = textOverflow;
    /// <summary>Whether Width is a minimum and text determines the unwrapped width.</summary>
    public bool GrowsWidthToText { get; } = growsWidthToText;
    /// <summary>The name by which another text frame can link to this one.</summary>
    public string? FrameName { get; } = string.IsNullOrEmpty(frameName) ? null : frameName;
    /// <summary>The frame that receives text which does not fit in this frame.</summary>
    public string? NextFrameName { get; } = string.IsNullOrEmpty(nextFrameName) ? null : nextFrameName;

    /// <summary>How the box is painted, or null when it is not filled.</summary>
    public ShapeFill? Fill { get; } = fill;

    /// <summary>The outline colour; <see cref="BColor.Empty"/> for no outline.</summary>
    public BColor Outline { get; } = outline;

    public ShapeGeometry Geometry { get; } = geometry;
    /// <summary>Stroke width in points; zero represents a device hairline.</summary>
    public double OutlineWidth { get; } = double.IsFinite(outlineWidth) && outlineWidth >= 0 ? Math.Min(outlineWidth, 20000) : 1;

    /// <summary>
    /// The picture the box draws, or null when it draws only paint and text. It
    /// fills <see cref="Width"/> by <see cref="Height"/>: the box is the picture's
    /// frame, so the size the document stated for the frame is the size it draws
    /// at, and <see cref="InlineImage.Width"/> is not consulted again.
    /// </summary>
    public InlineImage? Image { get; } = image;

    /// <summary>
    /// True when the shape draws under the body text, false when it draws over it.
    /// Defaults to true: a shape whose format says nothing about stacking is the
    /// letterhead case, and painting it over the letter would hide the letter.
    /// </summary>
    public bool BehindText { get; } = behindText;

    /// <summary>
    /// Where the shape sits among the other shapes: a higher number draws later,
    /// and therefore on top.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One order for the whole document, because that is what all three formats
    /// record - ODF's <c>draw:z-index</c>, OOXML's <c>relativeHeight</c>, RTF's
    /// <c>\shpz</c> - and none of them scopes it to the body or to a header. A
    /// letterhead is exactly the case that needs it: the stripe lives in the
    /// header and the logo box in the body, they overlap, and the document says
    /// which is on top by giving them numbers that can be compared across the two.
    /// </para>
    /// <para>
    /// Zero is the default and means "the format said nothing", which leaves the
    /// order the shapes were read in - the answer this model gave before the
    /// property existed, kept as the floor rather than as the rule. Negative
    /// values are clamped away because all three formats state a non-negative
    /// number and a reader producing one would be reporting its own arithmetic.
    /// </para>
    /// </remarks>
    public int ZOrder { get; } = Math.Max(0, zOrder);

    /// <summary>
    /// How the body text behaves around the shape. Defaults to
    /// <see cref="ShapeWrap.None"/>: text ignores it, which is what every shape
    /// did before any of them said otherwise.
    /// </summary>
    public ShapeWrap Wrap { get; } = wrap;

    /// <summary>Which side the text runs down when the shape wraps.</summary>
    public WrapSide WrapSide { get; } = wrapSide;

    /// <summary>
    /// Points of clearance the text keeps around a wrapping shape, so a line does
    /// not touch the picture it flows beside.
    /// </summary>
    public double WrapDistance { get; } = double.IsFinite(wrapDistance) && wrapDistance > 0 ? wrapDistance : 0;

    /// <summary>True when the body text has to keep clear of this shape.</summary>
    public bool Wraps => Wrap != ShapeWrap.None;

    /// <summary>
    /// The page the shape stands on, counted from one, or zero when it is
    /// anchored to the paragraph <see cref="ParagraphIndex"/> names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ODF anchors a frame to a page by its number
    /// (<c>text:anchor-type="page"</c>, <c>text:anchor-page-number</c>), and a
    /// magazine template does exactly that: the cover box on page one, a
    /// full-page panel on page seventeen. Such a shape stands on that page
    /// whatever the text does, and a renderer makes the pages it needs to have
    /// one to put it on, as LibreOffice does.
    /// </para>
    /// <para>
    /// For a page-anchored shape <see cref="OffsetY"/> is measured from the top
    /// edge of the page, as a running band's shapes are, because there is no
    /// paragraph to measure it from; <see cref="OffsetX"/> is still measured from
    /// the text column's left edge. <see cref="ParagraphIndex"/> keeps the
    /// paragraph the shape was read beside, which is where a format with no page
    /// anchor writes it.
    /// </para>
    /// </remarks>
    public int AnchorPage { get; } = Math.Max(0, anchorPage);

    /// <summary>True when the shape stands on a page by number rather than beside a paragraph.</summary>
    public bool IsPageAnchored => AnchorPage > 0;

    /// <summary>
    /// True when <see cref="OffsetY"/> is measured from the top edge of the page
    /// the anchoring paragraph lands on, rather than from the paragraph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page a paragraph lands on is a layout result, so a reader cannot turn
    /// such an offset into one from the paragraph, and a shape it kept the offset
    /// for was drawn that far below its paragraph instead. Every format states
    /// it: DOCX's <c>relativeFrom="page"</c>, VML's
    /// <c>mso-position-vertical-relative:page</c>, ODF's
    /// <c>style:vertical-rel="page"</c> on a paragraph's frame, RTF's
    /// <c>\shpbypage</c>. A full-page backdrop is the plain case: anchored to the
    /// first paragraph of each page, at nought from the page's top.
    /// </para>
    /// <para>
    /// The shape still travels with its paragraph: it is on whichever page the
    /// paragraph is. A shape anchored to a page by number already measures from
    /// the page, and this says nothing more for one.
    /// </para>
    /// </remarks>
    public bool OffsetFromPage { get; } = offsetFromPage && anchorPage <= 0;

    /// <summary>
    /// True when <see cref="Height"/> is the least the box is, and it grows to
    /// hold its text.
    /// </summary>
    /// <remarks>
    /// A frame whose height its document leaves to the text is how RTF and Word
    /// position a paragraph: <c>\absh0</c>, or <c>w:framePr</c> with no
    /// <c>w:h</c>, gives a width and a place and lets the lines decide the rest.
    /// ODF says the same with <c>fo:min-height</c>, and DrawingML with
    /// <c>a:spAutoFit</c>. The reader cannot measure lines, so it states the
    /// least height and the layout makes the box as tall as its text.
    /// </remarks>
    public bool GrowsToText { get; } = growsToText;

    /// <summary>
    /// The top of the shape's box on its page, given the top of the paragraph it
    /// hangs from there, in the same coordinates.
    /// </summary>
    public double TopOnPage(double paragraphTop) =>
        IsPageAnchored || OffsetFromPage ? OffsetY : paragraphTop + OffsetY;

    /// <summary>The shape's own text, empty when it holds none.</summary>
    public IReadOnlyList<RichTextParagraph> Paragraphs { get; } = paragraphs is null || paragraphs.Count == 0
            ? []
            : [.. paragraphs];

    /// <summary>True when the shape holds text rather than only paint.</summary>
    public bool HasText => Paragraphs.Count > 0;

    /// <summary>Tables in this shape's own paragraph coordinate space.</summary>
    public IReadOnlyList<DocumentTable> Tables { get; } = tables is null ? [] : [.. tables];

    /// <summary>True when the shape is a floating picture.</summary>
    public bool HasImage => Image is not null;

    /// <summary>
    /// True when the box has no breadth in one direction and an outline to
    /// draw, so it draws as a straight rule along its other side.
    /// </summary>
    /// <remarks>
    /// A ruled form - a statement's grid, a letterhead's underline - is drawn
    /// with lines, each of them a box of nought by its length in every format
    /// that writes one. A box with no breadth and no outline draws nothing and is
    /// not one.
    /// </remarks>
    public bool IsRule =>
        !Outline.IsEmpty && Outline.A > 0 &&
        ((Width <= 0 && Height > 0) || (Height <= 0 && Width > 0));
}

/// <summary>Basic shape paths; line directions use page coordinates with y increasing downwards.</summary>
public enum ShapeGeometry { Rectangle, Ellipse, Line, LineAscending }
