using System.Collections.Generic;

namespace Broiler.Documents.Pagination.Drawing;

/// <summary>
/// Support classification of a Layout DisplayList primitive against Graphics BRenderCommand and BPage.
/// </summary>
public enum DisplayCommandSupport
{
    /// <summary>Direct 1:1 mapping to a native BRenderCommand primitive.</summary>
    Direct1To1,

    /// <summary>Decomposed into one or more native BRenderCommand primitives (e.g. borders, polygons).</summary>
    Decomposed,

    /// <summary>Adapted through resource management (e.g. image caching, font resolution).</summary>
    Adapted,

    /// <summary>Unsupported directly: fallback approximated using geometric bounds or alternative styles.</summary>
    FallbackApproximated,

    /// <summary>Unsupported directly: rasterized to a pixel buffer within resource bounds.</summary>
    FallbackRasterized,

    /// <summary>Unsupported effect: recorded as a diagnostic, preserving drawing stream integrity without silent drops.</summary>
    UnsupportedDiagnosed,
}

/// <summary>
/// Formal inventory record mapping a Layout DisplayItem primitive to Graphics BRenderCommand / BPage.
/// </summary>
public sealed record DisplayItemMappingRecord(
    string DisplayItemType,
    DisplayCommandSupport Support,
    string TargetRenderCommand,
    string CoordinateRule,
    string ClippingRule,
    string TransformRule,
    string FallbackOutcome,
    string Notes);

/// <summary>
/// Inventory and resource limits for Layout DisplayList primitives mapped against Graphics BRenderCommand and BPage.
/// </summary>
public static class DisplayListInventory
{
    #region Resource Limits

    /// <summary>Maximum pixel dimension (width or height) permitted for rasterized fallback buffers.</summary>
    public const int MaxRasterDimension = 4096;

    /// <summary>Default resolution for high-quality rasterized fallback buffers (DPI).</summary>
    public const double DefaultRasterDpi = 300.0;

    /// <summary>Screen display resolution (DPI) matching standard CSS pixel definition.</summary>
    public const double ScreenDpi = 96.0;

    /// <summary>Maximum nested clip stack depth before rejecting or clamping.</summary>
    public const int MaxClipDepth = 64;

    /// <summary>Maximum nested transform stack depth before rejecting or clamping.</summary>
    public const int MaxTransformDepth = 64;

    #endregion

    /// <summary>
    /// Comprehensive catalog of all 23 Layout DisplayList drawing primitives
    /// mapped against Graphics BRenderCommand and BPage.
    /// </summary>
    public static readonly IReadOnlyList<DisplayItemMappingRecord> Mappings =
    [
        new(
            "FillRectItem",
            DisplayCommandSupport.Direct1To1,
            "BRenderCommand.FillRect(BRect, BColor)",
            "CSS px scaled by 0.75 to typographical points",
            "Affected by active clip stack",
            "Affected by active transform matrix",
            "Native execution",
            "Direct 1:1 hardware/software fill"),

        new(
            "DrawBorderItem",
            DisplayCommandSupport.Decomposed,
            "Decomposed into 4 FillRect / StrokeRect / FillTriangle",
            "Bounds scaled to points; edge widths scaled proportionally",
            "Subject to active clip",
            "Subject to active transform",
            "Mitered corner triangles or rounded rects where radii are specified",
            "Preserves individual side colors and widths"),

        new(
            "DrawTextItem",
            DisplayCommandSupport.Adapted,
            "BRenderCommand.DrawText(BTextRun, BPoint)",
            "Origin point scaled from CSS px to points; FontSize in points",
            "Subject to active clip",
            "Subject to active transform",
            "Text shadows decomposed into offset passes; gradient text falls back to solid color with diagnostic",
            "Glyph shaping preserved via PageShapingManifest"),

        new(
            "DrawImageItem",
            DisplayCommandSupport.Adapted,
            "BRenderCommand.DrawImage(BImageHandle, Source, Dest, Opacity)",
            "Source in bitmap px; Dest scaled from CSS px to points",
            "Subject to active clip",
            "Subject to active transform",
            "Unresolvable images drawn as placeholder bounding rectangles with diagnostic",
            "Image pixels uploaded and owned by BPageBuilder.AddImage"),

        new(
            "DrawTiledImageItem",
            DisplayCommandSupport.Decomposed,
            "Tiled loop of BRenderCommand.DrawImage within PushClip(FillRect)",
            "Tile width/height and origins scaled to points",
            "Bounded within destination FillRect clip",
            "Subject to active transform",
            "Iterates tile grid across FillRect bounds",
            "Bounded tile loop up to MaxRasterDimension limits"),

        new(
            "ClipItem",
            DisplayCommandSupport.Adapted,
            "BRenderCommand.PushClip(BRect)",
            "ClipRect scaled from CSS px to points",
            "Pushes new rectangular clip level onto stack",
            "Subject to active transform",
            "Arbitrary polygon / rounded clips use bounding-box clip + diagnostic",
            "Stack depth verified against MaxClipDepth"),

        new(
            "RestoreItem",
            DisplayCommandSupport.Direct1To1,
            "BRenderCommand.PopClip",
            "N/A",
            "Pops topmost clip level from stack",
            "N/A",
            "Stack underflow diagnosed and ignored safely",
            "Must balance every ClipItem"),

        new(
            "OpacityItem",
            DisplayCommandSupport.UnsupportedDiagnosed,
            "Diagnostics / Alpha Modulation",
            "N/A",
            "N/A",
            "N/A",
            "Alpha modulated into child solid colors or diagnosed as unsupported layer",
            "BRenderCommand does not carry group opacity layer push/pop"),

        new(
            "RestoreOpacityItem",
            DisplayCommandSupport.UnsupportedDiagnosed,
            "Diagnostics / Alpha Restore",
            "N/A",
            "N/A",
            "N/A",
            "Restores preceding alpha context",
            "Balances OpacityItem"),

        new(
            "BlendModeItem",
            DisplayCommandSupport.UnsupportedDiagnosed,
            "Diagnostics / Normal Blend Fallback",
            "N/A",
            "N/A",
            "N/A",
            "Normal alpha blending with diagnostic (CSS mix-blend-mode unsupported natively)",
            "Preserves drawing stream without failing"),

        new(
            "RestoreBlendModeItem",
            DisplayCommandSupport.UnsupportedDiagnosed,
            "Diagnostics / Blend Restore",
            "N/A",
            "N/A",
            "N/A",
            "Restores normal blend mode",
            "Balances BlendModeItem"),

        new(
            "FilterItem",
            DisplayCommandSupport.UnsupportedDiagnosed,
            "Diagnostics / Pass-through",
            "N/A",
            "N/A",
            "N/A",
            "Pass-through rendering with diagnostic (CSS color-matrix filter unsupported natively)",
            "Documented non-silent diagnostic"),

        new(
            "RestoreFilterItem",
            DisplayCommandSupport.UnsupportedDiagnosed,
            "Diagnostics / Filter Restore",
            "N/A",
            "N/A",
            "N/A",
            "Restores non-filter context",
            "Balances FilterItem"),

        new(
            "DrawTiledGradientItem",
            DisplayCommandSupport.FallbackApproximated,
            "Linear/Radial stop decomposition or diagnostic",
            "FillRect and tile sizes scaled to points",
            "Subject to active clip",
            "Subject to active transform",
            "Decomposed to dominant color fill or multi-stop bands; conic diagnosed",
            "Rasterized buffer fallback supported when rasterizer composed"),

        new(
            "DrawLineItem",
            DisplayCommandSupport.Decomposed,
            "BRenderCommand.FillRect or FillTriangle pair",
            "Start/End points and Width scaled to points",
            "Subject to active clip",
            "Subject to active transform",
            "Axis-aligned lines emit FillRect; diagonal lines emit FillTriangle pair",
            "Dash styles approximated or solid"),

        new(
            "DrawSvgRectItem",
            DisplayCommandSupport.Decomposed,
            "BRenderCommand.FillRect + StrokeRect",
            "X, Y, Width, Height scaled to points",
            "Subject to active clip",
            "Subject to active transform",
            "Fill drawn first, stroke outline drawn second",
            "Zero stroke width skips stroke"),

        new(
            "DrawSvgEllipseItem",
            DisplayCommandSupport.FallbackApproximated,
            "BRenderCommand.FillRoundedRect + StrokeRoundedRect",
            "Center Cx, Cy and radii Rx, Ry scaled to points",
            "Subject to active clip",
            "Subject to active transform",
            "Approximated via rounded rectangle with Rx, Ry corner radii",
            "High-fidelity curve approximation"),

        new(
            "DrawSvgTextItem",
            DisplayCommandSupport.Adapted,
            "BRenderCommand.DrawText(BTextRun, BPoint)",
            "X, Y origin scaled to points; FontSize in points",
            "Subject to active clip",
            "Subject to active transform",
            "Text run with SVG font family and fill color",
            "Matches DrawTextItem rules"),

        new(
            "DrawSvgLineItem",
            DisplayCommandSupport.Decomposed,
            "BRenderCommand.FillRect or FillTriangle pair",
            "(X1, Y1) to (X2, Y2) scaled to points; StrokeWidth scaled",
            "Subject to active clip",
            "Subject to active transform",
            "Decomposed identically to DrawLineItem",
            "Preserves stroke color and thickness"),

        new(
            "DrawSvgPolygonItem",
            DisplayCommandSupport.Decomposed,
            "Triangle fan of BRenderCommand.FillTriangle",
            "Points list scaled to typographical points",
            "Subject to active clip",
            "Subject to active transform",
            "Triangulated from first vertex (fan decomposition)",
            "Stroke drawn as edge lines if stroke width > 0"),

        new(
            "DrawSvgPolylineItem",
            DisplayCommandSupport.Decomposed,
            "Sequence of line segments (FillRect / FillTriangle)",
            "Points list scaled to typographical points",
            "Subject to active clip",
            "Subject to active transform",
            "Decomposed into individual line segment quads",
            "Optional fill triangulated as polygon"),

        new(
            "TransformItem",
            DisplayCommandSupport.Direct1To1,
            "BRenderCommand.PushTransform(BMatrix3x2)",
            "Affine matrix elements mapped; translation scaled from px to points",
            "N/A",
            "Concatenated into active transform stack",
            "Native affine transform execution",
            "Stack depth verified against MaxTransformDepth"),

        new(
            "RestoreTransformItem",
            DisplayCommandSupport.Direct1To1,
            "BRenderCommand.PopTransform",
            "N/A",
            "N/A",
            "Pops topmost transform from stack",
            "Stack underflow diagnosed and ignored safely",
            "Must balance every TransformItem")
    ];
}
