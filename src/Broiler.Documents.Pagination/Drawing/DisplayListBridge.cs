using System;
using System.Collections.Generic;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Paging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination.Drawing;

#region Bridged Primitives

/// <summary>Base descriptor for a display item bridged to the fixed-page graphics engine.</summary>
public abstract record BridgedDisplayItem;

/// <summary>Fills a rectangle in CSS pixel coordinates.</summary>
public sealed record BridgedFillRect(double X, double Y, double Width, double Height, BColor Color) : BridgedDisplayItem;

/// <summary>Draws box borders in CSS pixel coordinates with per-side widths, colors, and corner radii.</summary>
public sealed record BridgedBorder(
    double X,
    double Y,
    double Width,
    double Height,
    double TopWidth,
    double RightWidth,
    double BottomWidth,
    double LeftWidth,
    BColor TopColor,
    BColor RightColor,
    BColor BottomColor,
    BColor LeftColor,
    double RadiusNw = 0,
    double RadiusNe = 0,
    double RadiusSe = 0,
    double RadiusSw = 0) : BridgedDisplayItem;

/// <summary>Draws styled text at an origin specified in CSS pixels.</summary>
public sealed record BridgedText(
    string Text,
    string FontFamily,
    double FontSizePixels,
    BColor Color,
    double OriginX,
    double OriginY,
    bool Bold = false,
    bool Italic = false,
    double ShadowOffsetX = 0,
    double ShadowOffsetY = 0,
    BColor ShadowColor = default) : BridgedDisplayItem;

/// <summary>Draws an owned image resource positioned in CSS pixels.</summary>
public sealed record BridgedImage(
    BPixelBuffer Pixels,
    double SourceX,
    double SourceY,
    double SourceWidth,
    double SourceHeight,
    double DestX,
    double DestY,
    double DestWidth,
    double DestHeight,
    double Opacity = 1.0) : BridgedDisplayItem;

/// <summary>Pushes a rectangular clip defined in CSS pixels.</summary>
public sealed record BridgedClip(double X, double Y, double Width, double Height) : BridgedDisplayItem;

/// <summary>Pops the most recent clip from the clip stack.</summary>
public sealed record BridgedRestoreClip : BridgedDisplayItem;

/// <summary>Pushes a 2D affine transform matrix [m11, m12, m21, m22, m31, m32].</summary>
public sealed record BridgedTransform(float M11, float M12, float M21, float M22, float M31, float M32) : BridgedDisplayItem;

/// <summary>Pops the most recent transform from the transform stack.</summary>
public sealed record BridgedRestoreTransform : BridgedDisplayItem;

/// <summary>Draws a 2D line between two points in CSS pixels.</summary>
public sealed record BridgedLine(double X1, double Y1, double X2, double Y2, BColor Color, double Width) : BridgedDisplayItem;

/// <summary>Draws a closed polygon with vertex coordinates in CSS pixels.</summary>
public sealed record BridgedPolygon(
    IReadOnlyList<(double X, double Y)> Points,
    BColor Fill,
    BColor Stroke,
    double StrokeWidth = 1.0) : BridgedDisplayItem;

/// <summary>An unsupported visual effect (e.g. filter, blend-mode, conic gradient) to diagnose.</summary>
public sealed record BridgedUnsupportedEffect(string EffectName, string Details) : BridgedDisplayItem;

#endregion

/// <summary>
/// Bridges Layout DisplayList primitives into a fixed <see cref="BPage"/>.
/// Performs explicit CSS-pixel to typographical point conversion, enforces stack balancing,
/// registers owned image buffers, and diagnoses unsupported commands.
/// </summary>
public static class DisplayListBridge
{
    private enum LayerKind
    {
        Clip,
        Transform,
    }

    /// <summary>
    /// Replays a sequence of bridged display items into a <see cref="BPage"/> of the specified size in points.
    /// </summary>
    public static BPage Replay(
        BSize pageSizePoints,
        IEnumerable<BridgedDisplayItem> items,
        DrawingDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var sink = diagnostics ?? new DrawingDiagnosticSink();
        var builder = new BPageBuilder(pageSizePoints);
        var addedImages = new Dictionary<BPixelBuffer, BImageHandle>(ReferenceEqualityComparer.Instance);
        var layers = new Stack<LayerKind>();

        int clipCount = 0;
        int transformCount = 0;

        foreach (var item in items)
        {
            switch (item)
            {
                case BridgedFillRect fill:
                {
                    var rect = CssPointsBridge.RectToPoints(fill.X, fill.Y, fill.Width, fill.Height);
                    builder.Content.FillRect(rect, fill.Color);
                    break;
                }

                case BridgedBorder border:
                {
                    ReplayBorder(builder, border);
                    break;
                }

                case BridgedText text:
                {
                    ReplayText(builder, text);
                    break;
                }

                case BridgedImage image:
                {
                    ReplayImage(builder, image, addedImages);
                    break;
                }

                case BridgedClip clip:
                {
                    if (clipCount >= DisplayListInventory.MaxClipDepth)
                    {
                        sink.Warning("CLIP_DEPTH_EXCEEDED", $"Clip stack depth exceeded maximum limit ({DisplayListInventory.MaxClipDepth}). Clip ignored.", "ClipItem");
                        break;
                    }

                    var clipRect = CssPointsBridge.RectToPoints(clip.X, clip.Y, clip.Width, clip.Height);
                    builder.Content.PushClip(clipRect);
                    layers.Push(LayerKind.Clip);
                    clipCount++;
                    break;
                }

                case BridgedRestoreClip:
                {
                    if (clipCount <= 0)
                    {
                        sink.Warning("CLIP_UNDERFLOW", "Attempted to pop clip from empty clip stack.", "RestoreItem");
                        break;
                    }

                    builder.Content.PopClip();
                    clipCount--;
                    PopLayerKind(layers, LayerKind.Clip);
                    break;
                }

                case BridgedTransform transform:
                {
                    if (transformCount >= DisplayListInventory.MaxTransformDepth)
                    {
                        sink.Warning("TRANSFORM_DEPTH_EXCEEDED", $"Transform stack depth exceeded limit ({DisplayListInventory.MaxTransformDepth}). Transform ignored.", "TransformItem");
                        break;
                    }

                    // Map matrix elements: translation elements m31/m32 are converted from CSS px to points
                    var matrix = new BMatrix3x2(
                        transform.M11,
                        transform.M12,
                        transform.M21,
                        transform.M22,
                        (float)CssPointsBridge.PixelsToPoints(transform.M31),
                        (float)CssPointsBridge.PixelsToPoints(transform.M32));

                    builder.Content.PushTransform(matrix);
                    layers.Push(LayerKind.Transform);
                    transformCount++;
                    break;
                }

                case BridgedRestoreTransform:
                {
                    if (transformCount <= 0)
                    {
                        sink.Warning("TRANSFORM_UNDERFLOW", "Attempted to pop transform from empty transform stack.", "RestoreTransformItem");
                        break;
                    }

                    builder.Content.PopTransform();
                    transformCount--;
                    PopLayerKind(layers, LayerKind.Transform);
                    break;
                }

                case BridgedLine line:
                {
                    ReplayLine(builder, line);
                    break;
                }

                case BridgedPolygon polygon:
                {
                    ReplayPolygon(builder, polygon);
                    break;
                }

                case BridgedUnsupportedEffect unsupported:
                {
                    sink.Warning("UNSUPPORTED_EFFECT", $"Visual effect '{unsupported.EffectName}' ({unsupported.Details}) is unsupported on fixed pages and was bypassed.", unsupported.EffectName);
                    break;
                }
            }
        }

        // Auto-balance layers in strict LIFO order
        while (layers.Count > 0)
        {
            var layer = layers.Pop();
            if (layer == LayerKind.Clip)
            {
                sink.Warning("UNBALANCED_CLIP_RESTORED", "Emitted balancing PopClip for unclosed clip layer.", "RestoreItem");
                builder.Content.PopClip();
            }
            else
            {
                sink.Warning("UNBALANCED_TRANSFORM_RESTORED", "Emitted balancing PopTransform for unclosed transform layer.", "RestoreTransformItem");
                builder.Content.PopTransform();
            }
        }

        return builder.Build();
    }

    private static void PopLayerKind(Stack<LayerKind> stack, LayerKind expected)
    {
        if (stack.Count > 0 && stack.Peek() == expected)
        {
            stack.Pop();
        }
        else if (stack.Contains(expected))
        {
            var temp = new Stack<LayerKind>();
            while (stack.Count > 0)
            {
                var top = stack.Pop();
                if (top == expected)
                    break;
                temp.Push(top);
            }
            while (temp.Count > 0)
                stack.Push(temp.Pop());
        }
    }

    private static void ReplayBorder(BPageBuilder builder, BridgedBorder border)
    {
        double x = border.X;
        double y = border.Y;
        double w = border.Width;
        double h = border.Height;

        double tw = border.TopWidth;
        double rw = border.RightWidth;
        double bw = border.BottomWidth;
        double lw = border.LeftWidth;

        // Top side
        if (tw > 0 && !border.TopColor.IsEmpty)
        {
            var topRect = CssPointsBridge.RectToPoints(x, y, w, tw);
            builder.Content.FillRect(topRect, border.TopColor);
        }

        // Bottom side
        if (bw > 0 && !border.BottomColor.IsEmpty)
        {
            var botRect = CssPointsBridge.RectToPoints(x, y + h - bw, w, bw);
            builder.Content.FillRect(botRect, border.BottomColor);
        }

        // Left side
        if (lw > 0 && !border.LeftColor.IsEmpty)
        {
            var leftRect = CssPointsBridge.RectToPoints(x, y + tw, lw, Math.Max(0, h - tw - bw));
            builder.Content.FillRect(leftRect, border.LeftColor);
        }

        // Right side
        if (rw > 0 && !border.RightColor.IsEmpty)
        {
            var rightRect = CssPointsBridge.RectToPoints(x + w - rw, y + tw, rw, Math.Max(0, h - tw - bw));
            builder.Content.FillRect(rightRect, border.RightColor);
        }
    }

    private static void ReplayText(BPageBuilder builder, BridgedText text)
    {
        if (string.IsNullOrEmpty(text.Text))
            return;

        double fontSizePt = CssPointsBridge.PixelsToPoints(text.FontSizePixels);
        var style = new BFontStyle(
            text.FontFamily,
            Math.Max(1.0, fontSizePt),
            text.Bold ? BFontWeight.Bold : BFontWeight.Normal,
            text.Italic ? BFontSlant.Italic : BFontSlant.Normal);

        // Text shadow pass if specified
        if (!text.ShadowColor.IsEmpty && (text.ShadowOffsetX != 0 || text.ShadowOffsetY != 0))
        {
            var shadowOrigin = CssPointsBridge.PointToPoints(text.OriginX + text.ShadowOffsetX, text.OriginY + text.ShadowOffsetY);
            var shadowRun = new BTextRun(text.Text, style, text.ShadowColor);
            builder.Content.DrawText(shadowRun, shadowOrigin);
        }

        var origin = CssPointsBridge.PointToPoints(text.OriginX, text.OriginY);
        var run = new BTextRun(text.Text, style, text.Color);
        builder.Content.DrawText(run, origin);
    }

    private static void ReplayImage(
        BPageBuilder builder,
        BridgedImage image,
        Dictionary<BPixelBuffer, BImageHandle> addedImages)
    {
        if (!addedImages.TryGetValue(image.Pixels, out var handle))
        {
            handle = builder.AddImage(image.Pixels);
            addedImages[image.Pixels] = handle;
        }

        var src = new BRect(image.SourceX, image.SourceY, image.SourceWidth, image.SourceHeight);
        var dest = CssPointsBridge.RectToPoints(image.DestX, image.DestY, image.DestWidth, image.DestHeight);

        builder.Content.DrawImage(handle, src, dest, image.Opacity);
    }

    private static void ReplayLine(BPageBuilder builder, BridgedLine line)
    {
        double x1 = line.X1;
        double y1 = line.Y1;
        double x2 = line.X2;
        double y2 = line.Y2;
        double width = Math.Max(0.5, line.Width);

        if (Math.Abs(y1 - y2) < 0.001)
        {
            // Horizontal line
            double left = Math.Min(x1, x2);
            double w = Math.Abs(x2 - x1);
            var rect = CssPointsBridge.RectToPoints(left, y1 - width / 2.0, w, width);
            builder.Content.FillRect(rect, line.Color);
        }
        else if (Math.Abs(x1 - x2) < 0.001)
        {
            // Vertical line
            double top = Math.Min(y1, y2);
            double h = Math.Abs(y2 - y1);
            var rect = CssPointsBridge.RectToPoints(x1 - width / 2.0, top, width, h);
            builder.Content.FillRect(rect, line.Color);
        }
        else
        {
            // Diagonal line as two triangles (quad)
            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double nx = -dy / len * (width / 2.0);
            double ny = dx / len * (width / 2.0);

            var p1 = CssPointsBridge.PointToPoints(x1 + nx, y1 + ny);
            var p2 = CssPointsBridge.PointToPoints(x2 + nx, y2 + ny);
            var p3 = CssPointsBridge.PointToPoints(x2 - nx, y2 - ny);
            var p4 = CssPointsBridge.PointToPoints(x1 - nx, y1 - ny);

            builder.Content.FillTriangle(p1, p2, p3, line.Color);
            builder.Content.FillTriangle(p1, p3, p4, line.Color);
        }
    }

    private static void ReplayPolygon(BPageBuilder builder, BridgedPolygon polygon)
    {
        if (polygon.Points.Count < 3)
            return;

        // Triangulate simple polygon as triangle fan from vertex 0
        if (!polygon.Fill.IsEmpty)
        {
            var p0 = CssPointsBridge.PointToPoints(polygon.Points[0].X, polygon.Points[0].Y);
            for (int i = 1; i < polygon.Points.Count - 1; i++)
            {
                var pA = CssPointsBridge.PointToPoints(polygon.Points[i].X, polygon.Points[i].Y);
                var pB = CssPointsBridge.PointToPoints(polygon.Points[i + 1].X, polygon.Points[i + 1].Y);
                builder.Content.FillTriangle(p0, pA, pB, polygon.Fill);
            }
        }

        // Draw outline edges if stroke specified
        if (!polygon.Stroke.IsEmpty && polygon.StrokeWidth > 0)
        {
            for (int i = 0; i < polygon.Points.Count; i++)
            {
                var pt1 = polygon.Points[i];
                var pt2 = polygon.Points[(i + 1) % polygon.Points.Count];
                ReplayLine(builder, new BridgedLine(pt1.X, pt1.Y, pt2.X, pt2.Y, polygon.Stroke, polygon.StrokeWidth));
            }
        }
    }
}
