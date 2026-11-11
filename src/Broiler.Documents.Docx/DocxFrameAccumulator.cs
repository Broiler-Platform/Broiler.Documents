using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// Resolved frame formatting from <c>w:framePr</c> on a paragraph or its style chain.
/// </summary>
internal readonly record struct DocxFrameProperties(
    int? Width,
    int? Height,
    string? HeightRule,
    int? X,
    int? Y,
    string? HorizontalAnchor,
    string? VerticalAnchor,
    string? HorizontalAlign,
    string? VerticalAlign,
    string? Wrap,
    int? HorizontalSpace,
    int? VerticalSpace);

/// <summary>
/// Accumulates adjacent paragraphs that share identical <c>w:framePr</c> formatting
/// into a single floating <see cref="DocumentShape"/>, matching Word and LibreOffice.
/// </summary>
internal sealed class DocxFrameAccumulator
{
    public DocxFrameProperties? ActiveProperties { get; private set; }
    public DocxDocumentBuilder? ActiveBuilder { get; private set; }
    public int AnchorParagraphIndex { get; private set; }

    public void StartOrContinue(DocxFrameProperties properties, DocxReadContext context)
    {
        if (ActiveBuilder is null)
        {
            ActiveProperties = properties;
            AnchorParagraphIndex = context.Builder.CurrentParagraphIndex;
            ActiveBuilder = new DocxDocumentBuilder(
                context.Builder.Limits,
                context.Builder.Diagnostics,
                context.Builder.Reported)
            {
                WordCompatibilityMode = context.Builder.WordCompatibilityMode,
                SplitPgBreakAndParaMark = context.Builder.SplitPgBreakAndParaMark,
            };
        }
    }

    public void Finish(DocxReadContext context, int? lastAnchorInSection = null)
    {
        if (ActiveBuilder is null || !ActiveProperties.HasValue)
            return;

        DocxFrameProperties props = ActiveProperties.Value;
        int anchorIndex = Math.Min(AnchorParagraphIndex, lastAnchorInSection ?? AnchorParagraphIndex);
        RichTextDocument frameDoc = ActiveBuilder.Build();

        ActiveBuilder = null;
        ActiveProperties = null;

        if (frameDoc.ParagraphCount == 0 && (props.Height ?? 0) == 0)
            return;

        DocumentShape shape = CreateFrameShape(props, frameDoc.Paragraphs, anchorIndex, context.PlacementPage);
        context.Builder.AddShape(shape);

        // Drawings read inside the frame's paragraphs (a VML group in a framed
        // logo paragraph, say) were built into the frame's own document and
        // dropped with it. Move them out, measured from the frame's box.
        foreach (DocumentShape inner in frameDoc.Shapes)
            context.Builder.AddShape(MoveIntoFrame(inner, shape));
    }

    private static DocumentShape MoveIntoFrame(DocumentShape inner, DocumentShape frame)
    {
        bool fixedOnPage = inner.IsPageAnchored || inner.OffsetFromPage;
        double x = fixedOnPage ? inner.OffsetX : frame.OffsetX + inner.OffsetX;
        double y = fixedOnPage ? inner.OffsetY : frame.OffsetY + inner.OffsetY;
        return new DocumentShape(frame.ParagraphIndex, x, y, inner.Width, inner.Height, inner.Fill, inner.Outline,
            inner.Paragraphs, inner.Image, inner.BehindText, inner.Wrap, inner.WrapSide, inner.WrapDistance, inner.ZOrder,
            inner.AnchorPage, fixedOnPage ? inner.OffsetFromPage : frame.OffsetFromPage, inner.GrowsToText, inner.Tables,
            inner.TextPadding, inner.TextVerticalAlignment, inner.TextOverflow, inner.GrowsWidthToText, inner.FrameName,
            inner.NextFrameName, inner.Geometry, inner.OutlineWidth, inner.OffsetXFromCell, inner.CellHorizontalAlignment,
            inner.Printable)
        {
            IsAsCharacter = inner.IsAsCharacter,
            IsCustomShape = inner.IsCustomShape,
            FitsText = inner.FitsText,
            RotationRadians = inner.RotationRadians,
            TextFlow = inner.TextFlow,
            WrapMargins = inner.WrapMargins,
            WrapPolygon = inner.WrapPolygon,
            BoundedByPage = inner.BoundedByPage,
        };
    }

    public static DocxFrameProperties? ReadProperties(
        XElement? pPr,
        string? paragraphStyleId,
        DocxReadContext context)
    {
        if (context.Band != RunningBand.Body || context.InNote || context.Builder.InTableCell)
            return null;

        IReadOnlyList<XElement> chain = context.Styles.ParagraphProperties(paragraphStyleId, context.TableStyle, context.TableConditions);
        bool hasFrame = pPr?.Element(DocxNamespaces.Wordprocessing + "framePr") is not null;
        if (!hasFrame)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i].Element(DocxNamespaces.Wordprocessing + "framePr") is not null)
                {
                    hasFrame = true;
                    break;
                }
            }
        }

        if (!hasFrame)
            return null;

        int? width = null;
        int? height = null;
        string? heightRule = null;
        int? x = null;
        int? y = null;
        string? hAnchor = null;
        string? vAnchor = null;
        string? xAlign = null;
        string? yAlign = null;
        string? wrap = null;
        int? hSpace = null;
        int? vSpace = null;
        string? dropCap = null;

        void Apply(XElement? pr)
        {
            XElement? frame = pr?.Element(DocxNamespaces.Wordprocessing + "framePr");
            if (frame is null)
                return;

            if (int.TryParse((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w))
                width = w;
            if (int.TryParse((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "h"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                height = h;
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "hRule") is string hr)
                heightRule = hr;
            if (int.TryParse((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "x"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int xp))
                x = xp;
            if (int.TryParse((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "y"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int yp))
                y = yp;
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "hAnchor") is string ha)
                hAnchor = ha;
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "vAnchor") is string va)
                vAnchor = va;
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "xAlign") is string xa)
                xAlign = xa;
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "yAlign") is string ya)
                yAlign = ya;
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "wrap") is string wr)
                wrap = wr;
            if (int.TryParse((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "hSpace"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hs))
                hSpace = hs;
            if (int.TryParse((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "vSpace"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int vs))
                vSpace = vs;
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "dropCap") is string dc)
                dropCap = dc;
        }

        for (int i = 0; i < chain.Count; i++)
            Apply(chain[i]);
        Apply(pPr);

        if (dropCap is "drop" or "margin" || width == 0 && height == 0 && heightRule == "exact")
            return null;

        return new DocxFrameProperties(
            width, height, heightRule, x, y, hAnchor, vAnchor, xAlign, yAlign, wrap, hSpace, vSpace);
    }

    /// <summary>
    /// The drop cap a <c>w:dropCap</c> paragraph states for the paragraph it
    /// opens, with <paramref name="length"/> characters: <c>w:lines</c> (one
    /// where absent) and <c>w:hSpace</c>, the style chain's beneath the
    /// paragraph's own.
    /// </summary>
    public static DropCap ReadDropCap(XElement? pPr, string? paragraphStyleId, DocxReadContext context, int length)
    {
        int lines = 1;
        double distance = 0;
        bool inMargin = false;

        void Apply(XElement? pr)
        {
            XElement? frame = pr?.Element(DocxNamespaces.Wordprocessing + "framePr");
            if (frame is null)
                return;

            if (int.TryParse((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "lines"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                lines = value;
            if (frame.Attribute(DocxNamespaces.Wordprocessing + "hSpace") is not null)
                distance = DocxValueReader.Twips(frame, "hSpace");
            if ((string?)frame.Attribute(DocxNamespaces.Wordprocessing + "dropCap") is string type)
                inMargin = type == "margin";
        }

        IReadOnlyList<XElement> chain = context.Styles.ParagraphProperties(paragraphStyleId, context.TableStyle, context.TableConditions);
        for (int i = 0; i < chain.Count; i++)
            Apply(chain[i]);
        Apply(pPr);

        return new DropCap(Math.Clamp(lines, 1, DropCap.MaxLines), length, (float)Math.Max(0, distance)) { InMargin = inMargin };
    }

    public static bool IsDropCap(XElement? pPr, string? paragraphStyleId, DocxReadContext context)
    {
        if (context.Band != RunningBand.Body || context.InNote || context.Builder.InTableCell)
            return false;

        IReadOnlyList<XElement> chain = context.Styles.ParagraphProperties(paragraphStyleId, context.TableStyle, context.TableConditions);
        for (int i = 0; i < chain.Count; i++)
        {
            if ((string?)chain[i].Element(DocxNamespaces.Wordprocessing + "framePr")?.Attribute(DocxNamespaces.Wordprocessing + "dropCap") is "drop" or "margin")
                return true;
        }

        return (string?)pPr?.Element(DocxNamespaces.Wordprocessing + "framePr")?.Attribute(DocxNamespaces.Wordprocessing + "dropCap") is "drop" or "margin";
    }

    public static DocumentShape CreateFrameShape(
        DocxFrameProperties frame,
        IReadOnlyList<RichTextParagraph> paragraphs,
        int anchorParagraphIndex,
        PageGeometry? page)
    {
        double pageWidth = page?.Width ?? PageGeometry.A4.Width;
        double marginLeft = page?.MarginLeft ?? PageGeometry.A4.MarginLeft;
        double marginRight = page?.MarginRight ?? PageGeometry.A4.MarginRight;
        double marginTop = page?.MarginTop ?? PageGeometry.A4.MarginTop;
        double textWidth = Math.Max(0, pageWidth - marginLeft - marginRight);

        bool fromPage = frame.HorizontalAnchor == "page";
        double referenceWidth = fromPage ? pageWidth : textWidth;
        double width = frame.Width is > 0 ? frame.Width.Value / 20d : textWidth;
        double height = frame.Height.HasValue ? Math.Abs((double)frame.Height.Value) / 20d : 0d;

        double x = frame.HorizontalAlign switch
        {
            "left" or "inside" => 0,
            "center" => (referenceWidth - width) / 2,
            "right" or "outside" => referenceWidth - width,
            _ => (frame.X ?? 0) / 20d,
        };
        if (fromPage)
            x -= marginLeft;

        string verticalAnchor = frame.VerticalAnchor ?? "margin";
        bool fromParagraph = verticalAnchor == "text" || frame.VerticalAlign == "inline";
        double y = frame.Y is int explicitY ? explicitY / 20d : frame.VerticalAlign switch
        {
            "top" => 0,
            "center" when height > 0 && !fromParagraph =>
                (((verticalAnchor == "page" ? (page?.Height ?? PageGeometry.A4.Height) :
                   ((page?.Height ?? PageGeometry.A4.Height) - marginTop - (page?.MarginBottom ?? PageGeometry.A4.MarginBottom))) - height) / 2),
            "bottom" when height > 0 && !fromParagraph =>
                ((verticalAnchor == "page" ? (page?.Height ?? PageGeometry.A4.Height) :
                  ((page?.Height ?? PageGeometry.A4.Height) - marginTop - (page?.MarginBottom ?? PageGeometry.A4.MarginBottom))) - height),
            _ => 0,
        };

        if (!fromParagraph && verticalAnchor == "margin")
            y += marginTop;

        (ShapeWrap wrap, double distance) = frame.Wrap switch
        {
            "notBeside" => (ShapeWrap.TopAndBottom, (frame.VerticalSpace ?? 0) / 20d),
            "none" or "through" => (ShapeWrap.None, 0d),
            _ => (ShapeWrap.Square, (frame.HorizontalSpace ?? 0) / 20d),
        };

        ParagraphBorder? border = paragraphs.Count > 0 ? paragraphs[0].Style.Border : null;
        TableBorder outline = default;
        TableCellPadding padding = default;
        if (border is { Edges.Top.Style: TableBorderStyle.Solid } uniform &&
            uniform.Edges == CellBorders.All(uniform.Edges.Top))
        {
            outline = uniform.Edges.Top;
            padding = uniform.Spacing;
        }

        return new DocumentShape(
            anchorParagraphIndex,
            x,
            y,
            width,
            height,
            outline: outline.Color,
            paragraphs: outline.IsVisible
                ? [.. paragraphs.Select(paragraph => paragraph.Style.Border == border
                    ? paragraph.WithParagraphStyle(paragraph.Style with { Border = null })
                    : paragraph)]
                : paragraphs,
            behindText: false,
            wrap: wrap,
            wrapDistance: distance,
            offsetFromPage: !fromParagraph,
            growsToText: frame.HeightRule != "exact",
            textPadding: padding,
            outlineWidth: outline.IsVisible ? outline.Width : 1);
    }
}
