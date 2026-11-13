using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;
using Broiler.Dom;
using Broiler.Dom.Html;

namespace Broiler.Documents.Html;

/// <summary>Serializes the rich-text document model to deterministic UTF-8 HTML.</summary>
public static class HtmlWriter
{
    /// <summary>
    /// What a paragraph holding a tab declares so the tab survives HTML's
    /// whitespace collapsing. <see cref="HtmlReader"/> reads the same declaration
    /// back, so the tab returns as a tab rather than as the space a browser would
    /// otherwise show.
    /// </summary>
    internal const string PreserveWhitespaceDeclaration = "white-space: pre-wrap";

    /// <summary>
    /// What a paragraph that starts a new page declares.
    /// </summary>
    /// <remarks>
    /// The CSS2 property rather than the CSS3 <c>break-before: page</c>, even
    /// though the newer one is the replacement and this codec's reader takes
    /// either. What is written here has to be understood by whoever opens the
    /// file, and <c>page-break-before</c> is the one every browser and every
    /// word processor has read for twenty years - CSS Fragmentation keeps it as
    /// an alias for exactly that reason. Writing both spellings was the other
    /// option and was rejected: it states one break twice, and a consumer that
    /// resolved them in the wrong order would be entitled to honour the second.
    /// </remarks>
    internal const string PageBreakBeforeDeclaration = "page-break-before: always";

    public static DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        DocumentConversionContext resources = (options ?? DocumentWriteOptions.Default).Resources;

        var diagnostics = new List<DocumentDiagnostic>();
        if (document.Sections.Any(section => section.Hidden))
            diagnostics.Add(DocumentDiagnostic.Warning("html.section.hidden",
                "Hidden section visibility was not written; its retained content is exported as visible paragraphs."));
        if (document.Sections.Count > 0 || document.PageGeometry is { HasColumns: true })
        {
            // CSS could state columns, but a browser balances them over the
            // whole body rather than filling a page at a time, so writing them
            // would describe a different layout rather than this one.
            diagnostics.Add(DocumentDiagnostic.Warning(
                "html.columns",
                "The document's sections and text columns were not written; this writer lays the body out " +
                "as one column."));
        }

        // A document that hides its changes is written as it is drawn, its
        // deletions left out; one that shows them marks each with ins or del.
        document = RevisionDisplay.HideDeletions(document);

        DomDocument dom = BuildDom(document, resources, diagnostics);
        string html = HtmlSerializer.Serialize(
            dom.DocumentElement!,
            new HtmlSerializationOptions(IncludeHtmlDoctype: true));

        byte[] bytes = Encoding.UTF8.GetBytes(html);
        destination.Write(bytes, 0, bytes.Length);
        return new DocumentWriteResult(bytes.Length, diagnostics, DocumentWriteResult.StatusFrom(diagnostics));
    }

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(document, stream, options);
        return stream.ToArray();
    }

    private static DomDocument BuildDom(
        RichTextDocument rich,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        var document = new DomDocument();
        DomElement html = document.CreateElement("html");
        DomElement head = document.CreateElement("head");
        DomElement meta = document.CreateElement("meta");
        DomElement body = document.CreateElement("body");

        meta.SetAttribute("charset", "utf-8");
        document.AppendChild(html);
        html.AppendChild(head);
        html.AppendChild(body);
        head.AppendChild(meta);

        // The page, when the document states one. Written as an @page rule
        // because that is where HTML keeps it and where every other producer
        // looks for it - a document that arrived from DOCX or ODF carrying US
        // Letter used to leave through here carrying nothing, and the loss was
        // silent because no reader of the result could tell it apart from a
        // document that never stated a page at all.
        if (rich.PageGeometry is { } page && page.IsUsable)
        {
            DomElement style = document.CreateElement("style");
            style.SetAttribute("type", "text/css");
            style.AppendChild(document.CreateTextNode(HtmlPage.Format(page)));
            head.AppendChild(style);
        }

        // The page colour is the body's background, which CSS paints over the
        // whole canvas (CSS 2.1 §14.2). CSS could state a gradient, but the
        // readers this writes for - LibreOffice's among them - take a colour, so
        // a gradient leaves as its middle colour, as LibreOffice writes one.
        if (rich.PageBackground is ShapeFill background)
        {
            body.SetAttribute("style", "background-color: " + HtmlCss.FormatColor(background.ColorAt(0.5)));
            if (background.IsGradient)
            {
                diagnostics.Add(DocumentDiagnostic.Warning(
                    "html.background.gradient",
                    "The page background is a gradient; it was written as its middle colour."));
            }
        }

        var notes = new NoteNumberer(rich.NoteNumbering);
        AppendFlow(document, body, rich, 0, rich.ParagraphCount, rich.Tables, notes, new ListNumberer(), resources, diagnostics);

        // HTML has no notes. They are written after the body, under a rule,
        // each opened with its label, and the citation is the label raised.
        IReadOnlyList<DocumentNote> cited = rich.Notes;
        if (cited.Count > 0)
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                "html.note",
                "Footnotes and endnotes were written as paragraphs after the body, each opened with its label."));
            body.AppendChild(document.CreateElement("hr"));
            var written = new HashSet<DocumentNote>(ReferenceEqualityComparer.Instance);
            foreach (DocumentNote note in cited)
            {
                if (!written.Add(note))
                    continue;

                RichTextParagraph first = note.Paragraphs[0];
                RichTextParagraph labelled = first.InsertText(0, notes.Label(note) + " ", first.StyleAt(0) with { Note = null, Image = null });
                AppendParagraphs(document, body, [labelled, .. note.Paragraphs.Skip(1)], notes, resources, diagnostics, inNote: true);
            }
        }

        return document;
    }

    private static void AppendFlow(DomDocument document, DomElement parent, RichTextDocument rich,
        int start, int count, IReadOnlyList<DocumentTable> tables, NoteNumberer notes, ListNumberer numbering,
        DocumentConversionContext resources, List<DocumentDiagnostic> diagnostics)
    {
        int end = start + count;
        int index = start;
        foreach (DocumentTable table in tables.OrderBy(table => table.ParagraphIndex))
        {
            if (table.ParagraphIndex < index || table.ParagraphIndex + table.ParagraphCount > end)
                continue;

            AppendParagraphs(document, parent, rich.Paragraphs.Skip(index).Take(table.ParagraphIndex - index),
                notes, resources, diagnostics, numbering: numbering);
            AppendTable(document, parent, rich, table, notes, numbering, resources, diagnostics);
            index = table.ParagraphIndex + table.ParagraphCount;
        }

        AppendParagraphs(document, parent, rich.Paragraphs.Skip(index).Take(end - index), notes, resources, diagnostics, numbering: numbering);
    }

    private static void AppendTable(DomDocument document, DomElement parent, RichTextDocument rich,
        DocumentTable table, NoteNumberer notes, ListNumberer numbering, DocumentConversionContext resources, List<DocumentDiagnostic> diagnostics)
    {
        DomElement element = document.CreateElement("table");
        parent.AppendChild(element);
        var tableStyle = new List<string> { "border-collapse: collapse" };
        if (table.WidthPercent is double percent)
            tableStyle.Add(string.Create(CultureInfo.InvariantCulture, $"width: {percent}%"));
        else if (table.PreferredWidth is double width)
            tableStyle.Add("width: " + Points(width));
        element.SetAttribute("style", string.Join("; ", tableStyle));

        var occupied = new Dictionary<int, int>();
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            TableRow row = table.Rows[rowIndex];
            DomElement tr = document.CreateElement("tr");
            element.AppendChild(tr);
            int column = 0;
            foreach (TableCell cell in row.Cells.OrderBy(cell => cell.ColumnIndex))
            {
                if (cell.IsRowSpanContinuation)
                    continue;
                while (column < cell.ColumnIndex)
                {
                    if (occupied.GetValueOrDefault(column) <= rowIndex)
                        tr.AppendChild(document.CreateElement("td"));
                    column++;
                }

                DomElement td = document.CreateElement(row.IsHeader ? "th" : "td");
                tr.AppendChild(td);
                if (cell.ColumnSpan > 1)
                    td.SetAttribute("colspan", cell.ColumnSpan.ToString(CultureInfo.InvariantCulture));
                if (cell.RowSpan > 1)
                    td.SetAttribute("rowspan", cell.RowSpan.ToString(CultureInfo.InvariantCulture));
                column = cell.ColumnIndex + cell.ColumnSpan;
                if (cell.RowSpan > 1)
                    for (int covered = cell.ColumnIndex; covered < column; covered++)
                        occupied[covered] = rowIndex + cell.RowSpan;

                var declarations = new List<string>();
                if (cell.Shading.A > 0)
                    declarations.Add("background-color: " + HtmlCss.FormatColor(cell.Shading));
                TableCellPadding padding = cell.Padding ?? default;
                declarations.Add($"padding: {Points(padding.Top)} {Points(padding.Right)} {Points(padding.Bottom)} {Points(padding.Left)}");
                AddBorder(declarations, "top", cell.Borders.Top);
                AddBorder(declarations, "right", cell.Borders.Right);
                AddBorder(declarations, "bottom", cell.Borders.Bottom);
                AddBorder(declarations, "left", cell.Borders.Left);
                td.SetAttribute("style", string.Join("; ", declarations));
                AppendFlow(document, td, rich, cell.ParagraphIndex, cell.ParagraphCount,
                    cell.Tables, notes, numbering, resources, diagnostics);
            }
        }
    }

    private static void AddBorder(List<string> declarations, string side, TableBorder border)
    {
        if (border.IsVisible)
            declarations.Add($"border-{side}: {Points(border.Width)} {BorderStyle(border.Style)} {HtmlCss.FormatColor(border.Color)}");
    }

    private static string Points(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.########}pt");

    private static string BorderStyle(TableBorderStyle style) => style switch
    {
        TableBorderStyle.Dashed => "dashed",
        TableBorderStyle.Dotted => "dotted",
        TableBorderStyle.Double => "double",
        TableBorderStyle.Groove => "groove",
        TableBorderStyle.Ridge => "ridge",
        TableBorderStyle.Inset => "inset",
        TableBorderStyle.Outset => "outset",
        _ => "solid",
    };

    private static void AppendParagraphs(
        DomDocument document,
        DomElement body,
        IEnumerable<RichTextParagraph> paragraphs,
        NoteNumberer notes,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics,
        bool inNote = false, ListNumberer? numbering = null)
    {
        numbering ??= new ListNumberer();
        var lists = new List<(DomElement Element, DomElement Item, ListDefinition? Definition, ListKind Kind)>();
        foreach (RichTextParagraph paragraph in paragraphs)
        {
            ParagraphStyle style = paragraph.Style;
            ListReference? reference = style.List;
            ListKind kind = reference?.Kind ?? style.ListKind;
            int ordinal = 1;
            if (reference is ListReference item)
                numbering.Advance(item, out ordinal);
            DomElement p;
            if (kind == ListKind.None)
            {
                lists.Clear();
                p = document.CreateElement("p");
                body.AppendChild(p);
            }
            else
            {
                int level = Math.Clamp(reference?.Level ?? style.IndentLevel - 1, 0, lists.Count);
                while (lists.Count > level + 1)
                    lists.RemoveAt(lists.Count - 1);

                if (lists.Count > level &&
                    (lists[level].Kind != kind || lists[level].Definition != reference?.Definition))
                    lists.RemoveAt(level);

                p = document.CreateElement("li");
                if (lists.Count == level)
                {
                    DomElement list = document.CreateElement(kind == ListKind.Numbered ? "ol" : "ul");
                    (level == 0 ? body : lists[level - 1].Item).AppendChild(list);
                    if (kind == ListKind.Numbered && reference is ListReference listReference)
                    {
                        if (ordinal != 1)
                            list.SetAttribute("start", ordinal.ToString(CultureInfo.InvariantCulture));
                        string? listType = CssListType(listReference.LevelStyle.Format);
                        list.SetAttribute("style", "list-style-type: " + (listType ?? "decimal"));
                        string template = ListLevel.DecimalAt(listReference.Level).Template;
                        if ((listType is null || listReference.LevelStyle.Template != template) &&
                            !diagnostics.Exists(diagnostic => diagnostic.Code == "html.list.marker"))
                        {
                            diagnostics.Add(DocumentDiagnostic.Warning("html.list.marker",
                                "A custom numbered list marker was approximated by a native HTML marker."));
                        }
                    }
                    lists.Add((list, p, reference?.Definition, kind));
                }

                var current = lists[level];
                current.Element.AppendChild(p);
                lists[level] = (current.Element, p, current.Definition, current.Kind);
                if (kind == ListKind.Numbered && reference?.RestartAt is int restart)
                    p.SetAttribute("value", restart.ToString(CultureInfo.InvariantCulture));
            }
            string paragraphStyle = FormatParagraphStyle(paragraph.Style, diagnostics);

            // HTML collapses white space unless the paragraph asks it not to, so a
            // paragraph whose text would not survive that carries the declaration
            // that keeps it. pre-wrap rather than pre: the paragraph must still
            // wrap at the window.
            if (NeedsPreservedWhitespace(paragraph.Text))
            {
                paragraphStyle = paragraphStyle.Length > 0
                    ? paragraphStyle + "; " + PreserveWhitespaceDeclaration
                    : PreserveWhitespaceDeclaration;
            }

            if (paragraphStyle.Length > 0)
                p.SetAttribute("style", paragraphStyle);

            AppendRuns(document, p, paragraph, notes, resources, diagnostics, inNote);
        }
    }

    /// <summary>
    /// Whether HTML's white-space collapsing would change this paragraph's text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule the declaration exists for is not only about tabs. A run of two
    /// spaces, or a space at either edge, collapses just as a tab does, and the
    /// text then reads back shorter than it was written - with nothing said,
    /// because from the writer's side nothing went wrong. This condition used to
    /// name the tab alone, so the doubled space was lost in silence.
    /// </para>
    /// <para>
    /// The declaration is asked for rather than the characters escaped, because
    /// this codec's reader already understands it (an element carrying
    /// <c>pre</c>, <c>pre-wrap</c> or <c>break-spaces</c> keeps its text
    /// verbatim) and because <c>&amp;nbsp;</c> is a different character rather
    /// than a protected space - it does not wrap and it does not compare equal
    /// to the space the document actually held.
    /// </para>
    /// </remarks>
    private static bool NeedsPreservedWhitespace(string text)
    {
        if (text.Length == 0)
            return false;

        // A soft break is written as <br> rather than as white space, so it is
        // not what this asks about.
        if (IsCollapsible(text[0]) || IsCollapsible(text[^1]))
            return true;

        for (int i = 0; i < text.Length; i++)
        {
            // A tab collapses to one space on its own; anything else has to meet
            // a neighbour to be lost.
            if (text[i] == '\t')
                return true;

            if (i > 0 && IsCollapsible(text[i]) && IsCollapsible(text[i - 1]))
                return true;
        }

        return false;

        static bool IsCollapsible(char character) =>
            character is ' ' or '\t' or '\n' or '\r' or '\f';
    }

    private static void AppendRuns(
        DomDocument document,
        DomNode parent,
        RichTextParagraph paragraph,
        NoteNumberer notes,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics,
        bool inNote)
    {
        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            string text = paragraph.Text.Substring(offset, run.Length);
            offset += run.Length;

            if (run.Style.Note is DocumentNote note)
            {
                // A note inside a note has nowhere to go and is left out.
                if (!inNote)
                {
                    DomElement sup = document.CreateElement("sup");
                    sup.AppendChild(document.CreateTextNode(notes.Label(note)));
                    parent.AppendChild(sup);
                }

                continue;
            }

            // Inserted and deleted text is what ins and del are for; the run's
            // own formatting goes inside.
            DomNode host = parent;
            if (run.Style.Revision is { Kind: RevisionKind.Insertion or RevisionKind.Deletion } revision)
            {
                DomElement change = document.CreateElement(revision.IsDeletion ? "del" : "ins");
                if (revision.Date is DateTimeOffset date)
                    change.SetAttribute("datetime", date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                parent.AppendChild(change);
                host = change;
            }

            DomNode target = CreateRunContainer(document, run.Style, diagnostics);
            if (target is DomElement element)
            {
                AppendRunContent(document, element, text, run.Style, resources, diagnostics);
                host.AppendChild(element);
            }
            else
            {
                AppendRunContent(document, host, text, run.Style, resources, diagnostics);
            }
        }
    }

    /// <summary>
    /// Appends a run's characters, turning each image placeholder into an
    /// <c>img</c> whose source is a data URI. HTML has nowhere else to keep the
    /// bytes, and an external file beside the document would make a single-file
    /// export no longer single-file.
    /// </summary>
    private static void AppendRunContent(
        DomDocument document,
        DomNode parent,
        string text,
        InlineStyle style,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        if (style.Image is not InlineImage image)
        {
            AppendTextWithBreaks(document, parent, text);
            return;
        }

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != InlineImage.Placeholder)
                continue;

            if (i > start)
                AppendTextWithBreaks(document, parent, text[start..i]);

            DomElement? img = CreateImageElement(document, image, resources, diagnostics);
            if (img is not null)
                parent.AppendChild(img);
            start = i + 1;
        }

        if (start < text.Length)
            AppendTextWithBreaks(document, parent, text[start..]);
    }

    private static DomElement? CreateImageElement(
        DomDocument document,
        InlineImage image,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        if (!DocumentResourceGate.TryTakeEncodedBytes(
                image,
                resources,
                DocumentResourceOperations.ByteTransfer,
                out ReadOnlyMemory<byte> data,
                out string? contentType,
                out string? denial))
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "html.image.omitted",
                "An image was left out of the HTML output because " + denial + "."));
            return null;
        }

        DomElement img = document.CreateElement("img");
        img.SetAttribute("src", "data:" + contentType + ";base64," + Convert.ToBase64String(data.Span));
        img.SetAttribute("alt", image.AltText);
        if (image.TryGetDisplaySize(out double width, out double height))
        {
            img.SetAttribute(
                "style",
                "width: " + FormatLength(width) + "; height: " + FormatLength(height));
        }

        diagnostics.Add(DocumentDiagnostic.Info(
            "html.image.datauri",
            "An embedded image was written as a base64 data URI."));
        return img;
    }

    private static string FormatLength(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    private static DomNode CreateRunContainer(
        DomDocument document,
        InlineStyle style,
        List<DocumentDiagnostic> diagnostics)
    {
        string css = FormatInlineStyle(style);

        // The reader's rule, applied on the way out as well. Without it a
        // javascript: or data: target reached the anchor - and nothing said
        // so, because from the writer's side nothing had gone wrong.
        if (!string.IsNullOrEmpty(style.LinkHref) && !DocumentLinkTarget.IsAllowed(style.LinkHref))
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "html.link",
                "A hyperlink with a disallowed or relative target was written as plain text."));
        }
        else if (!string.IsNullOrEmpty(style.LinkHref))
        {
            DomElement link = document.CreateElement("a");
            link.SetAttribute("href", style.LinkHref);
            if (css.Length > 0)
                link.SetAttribute("style", css);
            return link;
        }

        if (css.Length == 0)
            return document.CreateDocumentFragment();

        DomElement span = document.CreateElement("span");
        span.SetAttribute("style", css);
        return span;
    }

    private static void AppendTextWithBreaks(DomDocument document, DomNode parent, string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != (char)0x2028)
                continue;

            if (i > start)
                parent.AppendChild(document.CreateTextNode(text[start..i]));
            parent.AppendChild(document.CreateElement("br"));
            start = i + 1;
        }

        if (start < text.Length)
            parent.AppendChild(document.CreateTextNode(text[start..]));
    }

    private static string FormatInlineStyle(InlineStyle style)
    {
        var declarations = new List<string>();
        if (style.Bold)
            declarations.Add("font-weight: bold");
        if (style.Italic)
            declarations.Add("font-style: italic");

        string decoration = FormatTextDecoration(style);
        if (decoration.Length > 0)
            declarations.Add("text-decoration: " + decoration);

        if (style.Shift is { } shift)
        {
            if (shift.Position == BaselinePosition.AutoSuperscript)
                declarations.Add("vertical-align: super");
            else if (shift.Position == BaselinePosition.AutoSubscript)
                declarations.Add("vertical-align: sub");
        }
        if (style.Capitalization == TextCapitalization.AllCaps)
            declarations.Add("text-transform: uppercase");
        else if (style.Capitalization == TextCapitalization.Lowercase)
            declarations.Add("text-transform: lowercase");
        else if (style.Capitalization == TextCapitalization.Capitalize)
            declarations.Add("text-transform: capitalize");
        else if (style.Capitalization == TextCapitalization.SmallCaps)
            declarations.Add("font-variant: small-caps");

        if (style.FontFamily is not null)
            declarations.Add("font-family: " + QuoteCssString(style.FontFamily));
        if (style.FontSize.HasValue)
            declarations.Add("font-size: " + HtmlCss.FormatPoints(style.FontSize.Value));
        if (!style.Foreground.IsEmpty)
            declarations.Add("color: " + HtmlCss.FormatColor(style.Foreground));
        if (!style.Background.IsEmpty)
            declarations.Add("background-color: " + HtmlCss.FormatColor(style.Background));

        return string.Join("; ", declarations);
    }

    private static string FormatParagraphStyle(ParagraphStyle style, List<DocumentDiagnostic> diagnostics)
    {
        var declarations = new List<string>();

        // First in the list, because it is the only one of these that is about
        // where the paragraph starts rather than how it looks once it is there.
        if (style.PageBreakBefore)
            declarations.Add(PageBreakBeforeDeclaration);
        else if (style.ColumnBreakBefore)
            declarations.Add("break-before: column");

        if (style.Alignment == TextAlignment.Center)
            declarations.Add("text-align: center");
        else if (style.Alignment == TextAlignment.Right)
            declarations.Add("text-align: right");
        else if (style.Alignment == TextAlignment.Justify)
            declarations.Add("text-align: justify");

        // CSS states one line height, which LibreOffice reads as the least a
        // line is; an exact height is written that way too, and said.
        if (style.LineHeightRule == LineHeightRule.Leading && style.LineHeight != 0 &&
            !diagnostics.Exists(d => d.Code == "html.linespacing.leading"))
            diagnostics.Add(DocumentDiagnostic.Warning("html.linespacing.leading", "Fixed extra line spacing was omitted."));

        if ((style.LineHeightRule is LineHeightRule.Exact or LineHeightRule.AtLeast) && style.LineHeight > 0)
        {
            declarations.Add("line-height: " + HtmlCss.FormatPoints(style.LineHeight));
            if (style.LineHeightRule == LineHeightRule.Exact && !diagnostics.Exists(d => d.Code == "html.lineheight.exact"))
            {
                diagnostics.Add(DocumentDiagnostic.Warning(
                    "html.lineheight.exact",
                    "An exact line height was written as a CSS line-height, which reads back as the least a line is."));
            }
        }
        else if (Math.Abs(style.LineSpacing - 1f) > 0.001f)
            declarations.Add("line-height: " + style.LineSpacing.ToString("0.###", CultureInfo.InvariantCulture));
        if (style.LeftIndent.HasValue || style.IndentLevel > 0)
            declarations.Add("margin-left: " + HtmlCss.FormatPoints((float)style.LeftIndentIn()));
        if (Math.Abs(style.SpacingBefore) > 0.001f)
            declarations.Add("margin-top: " + HtmlCss.FormatPoints(style.SpacingBefore));
        if (Math.Abs(style.SpacingAfter) > 0.001f)
            declarations.Add("margin-bottom: " + HtmlCss.FormatPoints(style.SpacingAfter));

        return string.Join("; ", declarations);
    }

    private static string? CssListType(ListNumberFormat format) => format switch
    {
        ListNumberFormat.LowerLetter or ListNumberFormat.LowerAlphabetic => "lower-alpha",
        ListNumberFormat.UpperLetter or ListNumberFormat.UpperAlphabetic => "upper-alpha",
        ListNumberFormat.LowerRoman => "lower-roman",
        ListNumberFormat.UpperRoman => "upper-roman",
        ListNumberFormat.DecimalZero => "decimal-leading-zero",
        ListNumberFormat.Decimal => "decimal",
        _ => null,
    };

    private static string FormatTextDecoration(InlineStyle style)
    {
        if (style.Underline && style.Strikethrough)
            return "underline line-through";
        if (style.Underline)
            return "underline";
        return style.Strikethrough ? "line-through" : string.Empty;
    }

    private static string QuoteCssString(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
