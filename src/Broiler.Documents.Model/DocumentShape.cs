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
    double outlineWidth = 1,
    bool offsetXFromCell = false,
    TextAlignment? cellHorizontalAlignment = null,
    bool printable = true)
{

    /// <summary>The paragraph the shape is anchored to.</summary>
    public int ParagraphIndex { get; } = paragraphIndex;

    /// <summary>Points from the text area's left edge, or the cell's text area when OffsetXFromCell is true.</summary>
    public double OffsetX { get; } = offsetX;

    /// <summary>Measure OffsetX from the anchoring table cell's text area rather than the page's text area.</summary>
    public bool OffsetXFromCell { get; } = offsetXFromCell;

    /// <summary>
    /// Align the shape within its anchoring cell's text area instead of using
    /// OffsetX. Null keeps the numeric offset; justification is not an alignment
    /// for a positioned object.
    /// </summary>
    public TextAlignment? CellHorizontalAlignment { get; } = offsetXFromCell && anchorPage <= 0 &&
        cellHorizontalAlignment is TextAlignment.Left or TextAlignment.Center or TextAlignment.Right
            ? cellHorizontalAlignment : null;

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
    /// False for a shape the document keeps off the printed page: shown while
    /// editing, laid out and wrapped around as any other, but not drawn on a
    /// page that is printed or exported. ODF states it as
    /// <c>style:print-content="false"</c>; a letterhead template uses it for
    /// the band and crest that the printed company paper already carries.
    /// </summary>
    public bool Printable { get; } = printable;

    /// <summary>Whether the source anchors this frame as a character in its paragraph.</summary>
    public bool IsAsCharacter { get; init; }

    /// <summary>True when this shape is a custom drawing shape rather than a text frame.</summary>
    public bool IsCustomShape { get; init; }

    /// <summary>True when this shape's box shrinks/fits to its text content centered in the declared bounds.</summary>
    public bool FitsText { get; init; }

    /// <summary>
    /// True when <see cref="Width"/> is the most the box may take and it
    /// narrows to its widest unwrapped line: a Word frame of one paragraph
    /// that states no width, which Writer imports at the minimum width and
    /// fits to its text (tdf135035.docx).
    /// </summary>
    public bool ShrinksWidthToText { get; init; }

    /// <summary>Clockwise rotation in radians around the shape's top-left corner.</summary>
    public double RotationRadians { get; init; }

    /// <summary>
    /// Which way the shape's text runs inside its box, which stays where it is:
    /// a text box whose words read down or up its side, as a spine label or a
    /// margin tab does.
    /// </summary>
    /// <remarks>
    /// DrawingML states it as <c>a:bodyPr vert="vert"</c> or <c>"vert270"</c>,
    /// VML as <c>v:textbox style="layout-flow:vertical"</c>, with
    /// <c>mso-layout-flow-alt:bottom-to-top</c> for the upward one. The text is
    /// laid out as if the box were turned with it: its lines are as long as the
    /// box is tall, <see cref="TextPadding"/> and
    /// <see cref="TextVerticalAlignment"/> apply to that turned box, and the
    /// shape does not grow to its text.
    /// </remarks>
    public ShapeTextFlow TextFlow { get; init; }

    /// <summary>
    /// True when the shape's text is WordArt: each line is stretched across the
    /// box and the lines together down it, as Word and LibreOffice fit a VML
    /// <c>v:textpath</c> (a text watermark) to its shape.
    /// </summary>
    public bool StretchesTextToBox { get; init; }

    /// <summary>
    /// The colour the outline of each glyph of <see cref="StretchesTextToBox"/>
    /// text is stroked in, or <see cref="BColor.Empty"/> for none: WordArt's
    /// stroke, drawn over the glyphs its fill paints.
    /// </summary>
    public BColor TextOutline { get; init; }

    /// <summary>How wide <see cref="TextOutline"/> is stroked, in points.</summary>
    public double TextOutlineWidth { get; init; }

    /// <summary>Optional per-side wrap clearance margins in points (Left, Top, Right, Bottom).</summary>
    public TableCellPadding? WrapMargins { get; init; }

    /// <summary>
    /// The outline text wraps against instead of the box, for DOCX's
    /// <c>wrapTight</c> and <c>wrapThrough</c>: vertices as fractions of the
    /// box's width and height, 0 to 1. Null wraps against the box.
    /// </summary>
    public IReadOnlyList<(double X, double Y)>? WrapPolygon { get; init; }

    /// <summary>
    /// The least room, in points, a line needs beside a shape that wraps on its
    /// larger side; with less on both sides the line goes below it. Zero lets
    /// text into any gap. Writer keeps text out of a gap narrower than 2 cm
    /// (<c>TEXT_MIN</c> in its text-fly code).
    /// </summary>
    public double MinimumWrapRoom { get; init; }

    /// <summary>
    /// True when the shape belongs to its anchor's page however far down it
    /// reaches, rather than taking its paragraph to the next page when it would
    /// pass the foot of the text area: a DrawingML object outside a table,
    /// which Writer's DOCX import does not let follow the text flow, so its
    /// layout environment is the page, not the body
    /// (<c>SwEnvironmentOfAnchoredObject</c>).
    /// </summary>
    /// <remarks>
    /// <c>tdf123243.docx</c> anchors two text boxes to a paragraph 450 points
    /// down a US Letter page; the taller, grown to its text, runs past the
    /// page's foot. LibreOffice keeps both, and the paragraph, on page one and
    /// draws what passes the page's edge nowhere. Only a shape whose top is
    /// already past the page's foot takes its paragraph along, or one that
    /// keeps text above and below it (<see cref="ShapeWrap.TopAndBottom"/>),
    /// which still moves as before: <c>fdo43573-2-min.docx</c>'s pictures keep
    /// its 19 pages that way.
    /// </remarks>
    public bool BoundedByPage { get; init; }

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
    public bool Wraps => !IsAsCharacter && Wrap != ShapeWrap.None;

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

    /// <summary>Creates a copy of this shape anchored to a different paragraph.</summary>
    public DocumentShape WithParagraphIndex(int paragraphIndex) =>
        WithPlacement(paragraphIndex, OffsetX, OffsetY, AnchorPage, OffsetFromPage, Wrap, ZOrder);

    /// <summary>
    /// Creates a copy of this shape placed somewhere else: another anchor and
    /// offset, how the body text treats it, and where it stacks. A reader uses
    /// it to lift a frame nested in another frame's text out to the frame's
    /// own anchor.
    /// </summary>
    public DocumentShape WithPlacement(
        int paragraphIndex,
        double offsetX,
        double offsetY,
        int anchorPage,
        bool offsetFromPage,
        ShapeWrap wrap,
        int zOrder) =>
        new(paragraphIndex, offsetX, offsetY, Width, Height, Fill, Outline, Paragraphs, Image, BehindText, wrap, WrapSide, WrapDistance, zOrder, anchorPage, offsetFromPage, GrowsToText, Tables, TextPadding, TextVerticalAlignment, TextOverflow, GrowsWidthToText, FrameName, NextFrameName, Geometry, OutlineWidth, OffsetXFromCell, CellHorizontalAlignment, Printable)
        {
            IsAsCharacter = IsAsCharacter,
            IsCustomShape = IsCustomShape,
            FitsText = FitsText,
            ShrinksWidthToText = ShrinksWidthToText,
            RotationRadians = RotationRadians,
            TextFlow = TextFlow,
            StretchesTextToBox = StretchesTextToBox,
            TextOutline = TextOutline,
            TextOutlineWidth = TextOutlineWidth,
            WrapMargins = WrapMargins,
            WrapPolygon = WrapPolygon,
            MinimumWrapRoom = MinimumWrapRoom,
            BoundedByPage = BoundedByPage,
        };

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

/// <summary>
/// Basic shape paths; line directions use page coordinates with y increasing downwards.
/// <see cref="Diamond"/> joins the midpoints of the box's four edges.
/// </summary>
public enum ShapeGeometry { Rectangle, Ellipse, Line, LineAscending, RoundedRectangle, Triangle, Diamond }

/// <summary>Which way a shape's lines of text run inside its box.</summary>
public enum ShapeTextFlow
{
    /// <summary>Left to right, lines stacked downwards: the ordinary case.</summary>
    Horizontal,

    /// <summary>Turned a quarter clockwise: lines read downwards, stacked from right to left.</summary>
    TopToBottom,

    /// <summary>Turned a quarter anticlockwise: lines read upwards, stacked from left to right.</summary>
    BottomToTop,
}
