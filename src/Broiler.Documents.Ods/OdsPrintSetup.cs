using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Ods;

/// <summary>
/// The page a sheet is printed on, read from the page style its table names:
/// the paper, the margins, and the header and footer Calc draws on it.
/// </summary>
/// <remarks>
/// <para>
/// A sheet's table style names a master page (<c>style:master-page-name</c>,
/// <c>Default</c> when it names none), which names a page layout. A layout
/// that leaves the paper or a margin unstated keeps Calc's own: the system
/// paper, A4 for the references this suite compares with, and 2 cm margins
/// (<c>ScStyleSheetPool::CreateStandardStyles</c>, <c>TWO_CM</c>).
/// </para>
/// <para>
/// Calc's header band is its <c>fo:min-height</c> tall, measured down from the
/// top margin, with the header's spacing (<c>fo:margin-bottom</c>) inside it:
/// LibreOffice 24.2 starts the cells of a sheet on the <c>Default</c> page style
/// (2 cm margin, 7.5 mm band, 2.5 mm spacing) 2.75 cm from the top of the page
/// and sets the header's line at the top margin. The footer mirrors it, its
/// line at the bottom margin. Writer, by contrast, adds the spacing to the
/// band, which is what the ODT reader reads.
/// </para>
/// </remarks>
internal sealed class OdsPrintSetup
{
    /// <summary>Calc's default page margin, 2 cm.</summary>
    internal const double DefaultMargin = 2 * 72 / 2.54;

    /// <summary>Calc's default header and footer height, 7.5 mm including its spacing.</summary>
    internal const double DefaultBandHeight = 7.5 * 72 / 25.4;

    private readonly Dictionary<string, XElement> _masters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement> _layouts = new(StringComparer.Ordinal);

    public OdsPrintSetup(XElement? shared, XElement? content)
    {
        foreach (XElement? root in new[] { shared, content })
        {
            if (root is null)
                continue;
            foreach (XElement master in root.Elements(OdsNamespaces.Office + "master-styles").Elements(OdsNamespaces.Style + "master-page"))
            {
                if ((string?)master.Attribute(OdsNamespaces.Style + "name") is string name)
                    _masters[name] = master;
            }

            foreach (XElement layout in root.Elements(OdsNamespaces.Office + "automatic-styles").Elements(OdsNamespaces.Style + "page-layout"))
            {
                if ((string?)layout.Attribute(OdsNamespaces.Style + "name") is string name)
                    _layouts[name] = layout;
            }
        }
    }

    /// <summary>
    /// The paper and the header and footer of the sheet whose table style is
    /// <paramref name="tableStyle"/>, with the sheet's name and the document's
    /// title for the fields that show them.
    /// </summary>
    public (PageGeometry Page, RunningContent Running) For(XElement? tableStyle, string sheetName, string? title)
    {
        string masterName = (string?)tableStyle?.Attribute(OdsNamespaces.Style + "master-page-name") is { Length: > 0 } named
            ? named
            : "Default";
        XElement? master = _masters.GetValueOrDefault(masterName);
        XElement? layout = (string?)master?.Attribute(OdsNamespaces.Style + "page-layout-name") is string layoutName
            ? _layouts.GetValueOrDefault(layoutName)
            : null;
        XElement? properties = layout?.Element(OdsNamespaces.Style + "page-layout-properties");

        double Length(XElement? element, string attribute, double fallback) =>
            OdsReader.TryParseLength((string?)element?.Attribute(OdsNamespaces.Fo + attribute), out double points) &&
            double.IsFinite(points) && points >= 0
                ? points
                : fallback;

        double width = Length(properties, "page-width", PageGeometry.A4.Width);
        double height = Length(properties, "page-height", PageGeometry.A4.Height);
        double margin = Length(properties, "margin", DefaultMargin);
        double top = Length(properties, "margin-top", margin);
        double bottom = Length(properties, "margin-bottom", margin);
        double left = Length(properties, "margin-left", margin);
        double right = Length(properties, "margin-right", margin);

        double contentWidth = Math.Max(1, width - left - right);
        IReadOnlyList<RichTextParagraph> header = Band(master?.Element(OdsNamespaces.Style + "header"), contentWidth, sheetName, title);
        IReadOnlyList<RichTextParagraph> footer = Band(master?.Element(OdsNamespaces.Style + "footer"), contentWidth, sheetName, title);
        double headerBand = header.Count == 0 ? 0 : BandHeight(layout, "header-style");
        double footerBand = footer.Count == 0 ? 0 : BandHeight(layout, "footer-style");

        var page = new PageGeometry(width, height, left, right, top + headerBand, bottom + footerBand,
            HeaderDistance: header.Count == 0 ? 0 : top, FooterDistance: footer.Count == 0 ? 0 : bottom);
        if (!page.IsUsable)
            page = new PageGeometry(PageGeometry.A4.Width, PageGeometry.A4.Height, DefaultMargin, DefaultMargin, DefaultMargin, DefaultMargin);

        RunningContent running = RunningContent.Empty;
        if (header.Count > 0)
            running = running.WithHeader(PageSelection.Default, header);
        if (footer.Count > 0)
            running = running.WithFooter(PageSelection.Default, footer);
        return (page, running);

        double BandHeight(XElement? pageLayout, string band)
        {
            XElement? bandProperties = pageLayout?.Element(OdsNamespaces.Style + band)?
                .Element(OdsNamespaces.Style + "header-footer-properties");
            if (bandProperties is null)
                return DefaultBandHeight;
            double stated = Length(bandProperties, "min-height", -1);
            if (stated < 0 && OdsReader.TryParseLength((string?)bandProperties.Attribute(XName.Get("height", "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0")), out double fixedHeight))
                stated = fixedHeight;
            return stated >= 0 ? stated : DefaultBandHeight;
        }
    }

    /// <summary>
    /// A header or footer's lines, or none where it is hidden or empty. Calc's
    /// band has three areas; a band that writes its paragraphs directly has
    /// them in the centre one, which is how LibreOffice reads a plain
    /// <c>style:header</c>. Several areas share each line, set at a centred
    /// tab in the middle and a right tab at the right edge.
    /// </summary>
    private static IReadOnlyList<RichTextParagraph> Band(XElement? band, double width, string sheetName, string? title)
    {
        if (band is null || (string?)band.Attribute(OdsNamespaces.Style + "display") == "false")
            return [];

        List<List<(string Text, InlineStyle Style)>> Lines(XElement? region) =>
            region?.Elements(OdsNamespaces.Text + "p").Select(p => Runs(p, sheetName, title)).ToList() ?? [];

        XElement? leftRegion = band.Element(OdsNamespaces.Style + "region-left");
        XElement? centreRegion = band.Element(OdsNamespaces.Style + "region-center");
        XElement? rightRegion = band.Element(OdsNamespaces.Style + "region-right");
        List<List<(string Text, InlineStyle Style)>> left = Lines(leftRegion);
        List<List<(string Text, InlineStyle Style)>> centre = leftRegion is null && centreRegion is null && rightRegion is null
            ? Lines(band)
            : Lines(centreRegion);
        List<List<(string Text, InlineStyle Style)>> right = Lines(rightRegion);

        static bool Empty(List<List<(string Text, InlineStyle Style)>> lines) => lines.All(line => line.Sum(run => run.Text.Length) == 0);
        bool hasLeft = !Empty(left), hasCentre = !Empty(centre), hasRight = !Empty(right);
        if (!hasLeft && !hasCentre && !hasRight)
            return [];

        int areas = (hasLeft ? 1 : 0) + (hasCentre ? 1 : 0) + (hasRight ? 1 : 0);
        if (areas == 1)
        {
            (List<List<(string Text, InlineStyle Style)>> lines, TextAlignment alignment) = hasLeft
                ? (left, TextAlignment.Left)
                : hasCentre ? (centre, TextAlignment.Center) : (right, TextAlignment.Right);
            return [.. lines.Select(line => Paragraph(line, ParagraphStyle.Default with { Alignment = alignment }))];
        }

        var stops = new ParagraphTabStops([
            new ParagraphTabStop(width / 2, TabAlignment.Center),
            new ParagraphTabStop(width, TabAlignment.Right),
        ]);
        var paragraphs = new List<RichTextParagraph>();
        for (int i = 0; i < Math.Max(left.Count, Math.Max(centre.Count, right.Count)); i++)
        {
            var line = new List<(string Text, InlineStyle Style)>();
            if (i < left.Count)
                line.AddRange(left[i]);
            line.Add(("\t", InlineStyle.Default));
            if (i < centre.Count)
                line.AddRange(centre[i]);
            line.Add(("\t", InlineStyle.Default));
            if (i < right.Count)
                line.AddRange(right[i]);
            paragraphs.Add(Paragraph(line, ParagraphStyle.Default with { TabStops = stops }));
        }

        return paragraphs;
    }

    private static RichTextParagraph Paragraph(List<(string Text, InlineStyle Style)> runs, ParagraphStyle style)
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty;
        foreach ((string text, InlineStyle runStyle) in runs)
            paragraph = paragraph.InsertText(paragraph.Length, text, runStyle);
        return paragraph.WithParagraphStyle(style);
    }

    /// <summary>
    /// A band paragraph's text, with Calc's header fields as Calc prints them:
    /// the sheet's name, the page's number and the page count worked out on
    /// each page, and the text the document saved for the others.
    /// </summary>
    private static List<(string Text, InlineStyle Style)> Runs(XElement paragraph, string sheetName, string? title)
    {
        var runs = new List<(string Text, InlineStyle Style)>();
        var text = new StringBuilder();

        void Flush()
        {
            if (text.Length > 0)
                runs.Add((text.ToString(), InlineStyle.Default));
            text.Clear();
        }

        void Walk(XElement element)
        {
            foreach (XNode node in element.Nodes())
            {
                if (node is XText plain)
                {
                    text.Append(plain.Value);
                    continue;
                }

                if (node is not XElement child)
                    continue;

                if (child.Name.Namespace != OdsNamespaces.Text)
                {
                    Walk(child);
                    continue;
                }

                switch (child.Name.LocalName)
                {
                    case "sheet-name":
                        text.Append(sheetName);
                        break;
                    case "title":
                        text.Append(title);
                        break;
                    case "page-number":
                    case "page-count":
                        Flush();
                        PageFieldKind kind = child.Name.LocalName == "page-count" ? PageFieldKind.PageCount : PageFieldKind.PageNumber;
                        string saved = child.Value.Length > 0 ? child.Value : "1";
                        runs.Add((saved, InlineStyle.Default with { PageField = new PageField(kind) }));
                        break;
                    case "s":
                        int count = int.TryParse((string?)child.Attribute(OdsNamespaces.Text + "c"), out int c) ? Math.Clamp(c, 1, 1000) : 1;
                        text.Append(' ', count);
                        break;
                    case "tab":
                        text.Append('\t');
                        break;
                    case "line-break":
                        text.Append(' ');
                        break;
                    default:
                        Walk(child);
                        break;
                }
            }
        }

        Walk(paragraph);
        Flush();
        return runs;
    }
}
