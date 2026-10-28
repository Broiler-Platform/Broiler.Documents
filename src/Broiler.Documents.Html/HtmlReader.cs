using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.Graphics.Color;
using Broiler.Documents.Resources;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Html;

internal static class HtmlReader
{
    private static readonly HashSet<string> ParagraphElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "li", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    private static readonly HashSet<string> BlockContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "body", "dd", "details",
        "dialog", "div", "dl", "dt", "fieldset", "figcaption", "figure",
        "footer", "form", "header", "main", "nav", "section", "table",
        "tbody", "td", "tfoot", "th", "thead", "tr",
    };

    /// <summary>
    /// Elements whose content is not text the document says: the ones the HTML
    /// rendering rules give <c>display: none</c> ("Hidden elements"), and the
    /// external or embedded content this reader does not fetch or draw.
    /// </summary>
    /// <remarks>
    /// <c>noframes</c> and <c>noembed</c> cost the most when they were missing,
    /// because the parser reads their content as raw text: a document that put
    /// its markup inside one came out with that markup, tags and all, as its
    /// prose. <c>rp</c> is on the rendering list and deliberately not on this
    /// one. It holds the parentheses a reader without ruby support shows around
    /// the annotation, and this model has no ruby.
    /// </remarks>
    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "basefont", "canvas", "datalist", "embed", "head",
        "iframe", "input", "link", "meta", "noembed", "noframes",
        "noscript", "object", "param", "script", "style", "svg", "template",
        "title",
    };

    /// <summary>
    /// The two properties that say a box starts on a new page, both of which are
    /// current. <c>page-break-before</c> is the CSS2 property and is what
    /// LibreOffice writes; <c>break-before</c> is the CSS3 replacement that the
    /// older one is now defined as an alias for. A reader that knew only one of
    /// them would take the break out of every document written by the other
    /// half of the world, and say nothing, because a break that was never read
    /// leaves nothing behind to report.
    /// </summary>
    private static readonly string[] PageBreakProperties = ["page-break-before", "break-before"];

    public static DocumentReadResult Read(byte[] bytes, DocumentReadOptions options, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<DocumentDiagnostic>();
        if (truncated)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "html.limit.bytes",
                "HTML input exceeded MaxDocumentBytes and was truncated before parsing."));
        }

        string html = DecodeUtf8(bytes);
        HtmlDocumentParseResult parse;
        try
        {
            parse = HtmlDocumentParser.ParseDocument(html);
        }
        catch (Exception ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "html.parse",
                $"HTML parser recovered by returning an empty document: {ex.GetType().Name}."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }

        foreach (HtmlParseDiagnostic diagnostic in parse.Diagnostics)
            diagnostics.Add(DocumentDiagnostic.Warning("html.parse", diagnostic.Message));

        // Gathered before the walk rather than after it, because the type rules
        // in it are a base for every element the walk touches. The page comes out
        // of the same text at the end: one collection of the style elements
        // answers both questions, and two collections would be two chances for
        // the answers to disagree about what the document's stylesheet is.
        string? styleSheetText = StyleSheetText(parse.Document);
        HtmlStyleSheet styleSheet = HtmlStyleSheet.Parse(styleSheetText);
        if (styleSheet.HasSkippedRule)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "html.css.rule",
                "A style rule was not applied because this codec does not implement it; " +
                "only a bare element-name selector and the @page at-rule are read."));
        }

        var resources = new DocumentConversionContextBuilder(options.ResourcePolicy);
        var builder = new HtmlDocumentBuilder(options.Limits, diagnostics, resources);
        DomNode root = parse.Document.Body is not null
            ? parse.Document.Body
            : parse.Document.DocumentElement is not null ? parse.Document.DocumentElement : parse.Document;
        ExtractFootnotes(root, builder, styleSheet);
        ReadChildren(root, builder, styleSheet, InlineStyle.Default, ParagraphStyle.Default, preserveWhitespace: false);

        RichTextDocument document = builder.Build();
        if (HtmlPage.TryRead(styleSheetText, PageGeometry.A4, out PageGeometry page))
            document = document.WithPageGeometry(page);
        BColor? canvas = PageBackground(parse.Document.DocumentElement, styleSheet)
            ?? PageBackground(parse.Document.Body, styleSheet);
        if (canvas is BColor fill)
            document = document.WithPageBackground(ShapeFill.Solid(fill));

        return new DocumentReadResult(document, diagnostics, DocumentReadResult.StatusFrom(diagnostics), resources.Build());
    }

    /// <summary>
    /// The colour an element paints behind the page: its <c>background-color</c>,
    /// or the colour in its <c>background</c> shorthand, or failing both its
    /// <c>bgcolor</c> attribute, which HTML maps to the same property beneath any
    /// author style (HTML §15.3.1). Null for transparent or none.
    /// </summary>
    /// <remarks>
    /// CSS 2.1 §14.2 paints the canvas with the root element's background, and
    /// with the body's when the root has none, which is how a browser and
    /// LibreOffice fill the whole page from <c>&lt;body bgcolor&gt;</c>. That
    /// fill is the page's, not the text's: reading it onto the runs would
    /// highlight every line and leave the margins white.
    /// </remarks>
    private static BColor? PageBackground(DomElement? element, HtmlStyleSheet styleSheet)
    {
        if (element is null)
            return null;

        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        if (declarations.TryGetValue("background-color", out string? stated) &&
            HtmlCss.TryParseColor(stated, out BColor color))
        {
            return color.IsEmpty || color.A == 0 ? null : color;
        }

        if (declarations.TryGetValue("background", out string? shorthand))
        {
            foreach (string part in shorthand.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (HtmlCss.TryParseColor(part, out color))
                    return color.IsEmpty || color.A == 0 ? null : color;
            }
        }

        return HtmlCss.TryParseColor(element.GetAttribute("bgcolor"), out color) && !color.IsEmpty && color.A > 0
            ? color
            : null;
    }

    private static string GetInnerText(DomNode node)
    {
        if (node is DomText text)
            return text.Data;
        var sb = new StringBuilder();
        foreach (DomNode child in node.ChildNodes)
            sb.Append(GetInnerText(child));
        return sb.ToString();
    }

    private static void ExtractFootnotes(DomNode root, HtmlDocumentBuilder builder, HtmlStyleSheet styleSheet)
    {
        var candidateLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FindCandidateCitations(root, candidateLabels);

        // 1. Semantic containers: <section class="footnotes">, <aside class="footnote">, <div id="footnotes">
        var containers = new List<DomElement>();
        FindFootnoteContainers(root, containers);
        foreach (DomElement container in containers)
        {
            builder.NodesToSkip.Add(container);
            // Collect list items or paragraphs inside container
            var items = new List<DomElement>();
            foreach (DomNode child in container.ChildNodes)
            {
                if (child is DomElement childEl)
                {
                    if (childEl.LocalName is "ol" or "ul")
                    {
                        foreach (DomNode li in childEl.ChildNodes)
                        {
                            if (li is DomElement liEl && liEl.LocalName == "li")
                                items.Add(liEl);
                        }
                    }
                    else if (childEl.LocalName is "p" or "div")
                    {
                        items.Add(childEl);
                    }
                }
            }

            int index = 1;
            foreach (DomElement item in items)
            {
                string? id = item.GetAttribute("id");
                string label = !string.IsNullOrEmpty(id) && id.StartsWith("fn", StringComparison.OrdinalIgnoreCase) && id.Length > 2
                    ? id[2..]
                    : index.ToString(CultureInfo.InvariantCulture);
                index++;

                IReadOnlyList<RichTextParagraph> paras = ReadNoteParagraphs(item, styleSheet, builder);
                paras = StripLabelPrefix(paras, label);
                if (paras.Count > 0)
                {
                    var docNote = new DocumentNote(NoteKind.Footnote, paras, label);
                    builder.RegisterFootnote(label, docNote);
                    if (!string.IsNullOrEmpty(id))
                        builder.RegisterFootnote(id, docNote);
                }
            }
        }

        // 2. Post-<hr> notes (as HtmlWriter formats them)
        if (root.ChildNodes.Count > 0)
        {
            int hrIndex = -1;
            var children = root.ChildNodes.ToList();
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is DomElement el && el.LocalName.Equals("hr", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if subsequent paragraph matches candidate citation
                    for (int j = i + 1; j < children.Count; j++)
                    {
                        if (children[j] is DomElement nextEl && nextEl.LocalName.Equals("p", StringComparison.OrdinalIgnoreCase))
                        {
                            string text = GetInnerText(nextEl).TrimStart();
                            foreach (string cand in candidateLabels)
                            {
                                if (text.StartsWith(cand + " ", StringComparison.Ordinal) ||
                                    text.StartsWith(cand + ". ", StringComparison.Ordinal) ||
                                    text.StartsWith(cand + ": ", StringComparison.Ordinal) ||
                                    text.StartsWith("[" + cand + "] ", StringComparison.Ordinal))
                                {
                                    hrIndex = i;
                                    break;
                                }
                            }
                            break;
                        }
                    }
                    if (hrIndex >= 0)
                        break;
                }
            }

            if (hrIndex >= 0)
            {
                builder.NodesToSkip.Add(children[hrIndex]);
                string? currentLabel = null;
                var currentElements = new List<DomElement>();

                void FlushCurrent()
                {
                    if (currentLabel is not null && currentElements.Count > 0)
                    {
                        var noteParas = new List<RichTextParagraph>();
                        foreach (DomElement elem in currentElements)
                            noteParas.AddRange(ReadNoteParagraphs(elem, styleSheet, builder));

                        IReadOnlyList<RichTextParagraph> cleaned = StripLabelPrefix(noteParas, currentLabel);
                        if (cleaned.Count > 0)
                        {
                            var docNote = new DocumentNote(NoteKind.Footnote, cleaned, currentLabel);
                            builder.RegisterFootnote(currentLabel, docNote);
                        }
                    }
                    currentElements.Clear();
                }

                for (int i = hrIndex + 1; i < children.Count; i++)
                {
                    if (children[i] is DomElement el)
                    {
                        builder.NodesToSkip.Add(el);
                        string text = GetInnerText(el).TrimStart();
                        string? matchedLabel = null;
                        foreach (string cand in candidateLabels)
                        {
                            if (text.StartsWith(cand + " ", StringComparison.Ordinal) ||
                                text.StartsWith(cand + ". ", StringComparison.Ordinal) ||
                                text.StartsWith(cand + ": ", StringComparison.Ordinal) ||
                                text.StartsWith("[" + cand + "] ", StringComparison.Ordinal))
                            {
                                matchedLabel = cand;
                                break;
                            }
                        }

                        if (matchedLabel is not null)
                        {
                            FlushCurrent();
                            currentLabel = matchedLabel;
                            currentElements.Add(el);
                        }
                        else if (currentLabel is not null)
                        {
                            currentElements.Add(el);
                        }
                    }
                }
                FlushCurrent();
            }
        }
    }

    private static void FindCandidateCitations(DomNode node, HashSet<string> candidateLabels)
    {
        if (node is DomElement el)
        {
            string tag = el.LocalName;
            if (tag.Equals("sup", StringComparison.OrdinalIgnoreCase) ||
                (tag.Equals("a", StringComparison.OrdinalIgnoreCase) &&
                 (el.GetAttribute("class")?.Contains("footnote", StringComparison.OrdinalIgnoreCase) == true ||
                  el.GetAttribute("href")?.StartsWith("#fn", StringComparison.OrdinalIgnoreCase) == true)))
            {
                string text = GetInnerText(el).Trim().Trim('[', ']');
                if (!string.IsNullOrEmpty(text))
                    candidateLabels.Add(text);
                string? href = el.GetAttribute("href");
                if (href?.StartsWith("#", StringComparison.Ordinal) == true)
                    candidateLabels.Add(href[1..]);
            }
        }

        foreach (DomNode child in node.ChildNodes)
            FindCandidateCitations(child, candidateLabels);
    }

    private static void FindFootnoteContainers(DomNode node, List<DomElement> containers)
    {
        if (node is DomElement el)
        {
            string? cls = el.GetAttribute("class");
            string? id = el.GetAttribute("id");
            if (cls?.Contains("footnotes", StringComparison.OrdinalIgnoreCase) == true ||
                cls?.Contains("footnote", StringComparison.OrdinalIgnoreCase) == true ||
                id?.Equals("footnotes", StringComparison.OrdinalIgnoreCase) == true)
            {
                containers.Add(el);
                return;
            }
        }

        foreach (DomNode child in node.ChildNodes)
            FindFootnoteContainers(child, containers);
    }

    private static IReadOnlyList<RichTextParagraph> ReadNoteParagraphs(
        DomElement element, HtmlStyleSheet styleSheet, HtmlDocumentBuilder builder)
    {
        var noteBuilder = new HtmlDocumentBuilder(builder.Limits, builder.Diagnostics, builder.Resources);
        ReadChildren(element, noteBuilder, styleSheet, InlineStyle.Default, ParagraphStyle.Default, preserveWhitespace: false);
        RichTextDocument doc = noteBuilder.Build();
        return doc.Paragraphs.Count > 0 ? doc.Paragraphs : [RichTextParagraph.Empty];
    }

    private static IReadOnlyList<RichTextParagraph> StripLabelPrefix(IReadOnlyList<RichTextParagraph> paragraphs, string label)
    {
        var list = new List<RichTextParagraph>(paragraphs);
        if (list.Count > 0 && !string.IsNullOrEmpty(label))
        {
            string text = list[0].Text;
            if (text.StartsWith(label, StringComparison.Ordinal))
            {
                int cut = label.Length;
                if (cut < text.Length && (text[cut] == '.' || text[cut] == ':'))
                    cut++;
                while (cut < text.Length && char.IsWhiteSpace(text[cut]))
                    cut++;
                list[0] = list[0].RemoveRange(0, cut);
            }
            else if (text.StartsWith("[" + label + "]", StringComparison.Ordinal))
            {
                int cut = label.Length + 2;
                if (cut < text.Length && (text[cut] == '.' || text[cut] == ':'))
                    cut++;
                while (cut < text.Length && char.IsWhiteSpace(text[cut]))
                    cut++;
                list[0] = list[0].RemoveRange(0, cut);
            }
        }
        return list;
    }

    private static void ReadChildren(
        DomNode parent,
        HtmlDocumentBuilder builder,
        HtmlStyleSheet styleSheet,
        InlineStyle inlineStyle,
        ParagraphStyle paragraphStyle,
        bool preserveWhitespace)
    {
        foreach (DomNode child in parent.ChildNodes)
            ReadNode(child, builder, styleSheet, inlineStyle, paragraphStyle, preserveWhitespace);
    }

    private static void ReadNode(
        DomNode node,
        HtmlDocumentBuilder builder,
        HtmlStyleSheet styleSheet,
        InlineStyle inlineStyle,
        ParagraphStyle paragraphStyle,
        bool preserveWhitespace)
    {
        if (builder.NodesToSkip.Contains(node))
            return;

        if (node is DomText text)
        {
            builder.AppendText(text.Data, inlineStyle, preserveWhitespace, paragraphStyle);
            return;
        }

        if (node is not DomElement element)
            return;

        if (element.GetAttribute("class")?.Contains("footnote-back", StringComparison.OrdinalIgnoreCase) == true)
            return;

        string tag = element.LocalName;
        if (tag.Equals("sup", StringComparison.OrdinalIgnoreCase) ||
            (tag.Equals("a", StringComparison.OrdinalIgnoreCase) &&
             (element.GetAttribute("class")?.Contains("footnote", StringComparison.OrdinalIgnoreCase) == true ||
              element.GetAttribute("href")?.StartsWith("#fn", StringComparison.OrdinalIgnoreCase) == true)))
        {
            string citationText = GetInnerText(element).Trim().Trim('[', ']');
            string? href = element.GetAttribute("href");
            string? targetId = href?.StartsWith("#", StringComparison.Ordinal) == true ? href[1..] : null;

            if ((!string.IsNullOrEmpty(citationText) && builder.TryGetFootnote(citationText, out DocumentNote? note)) ||
                (targetId is not null && builder.TryGetFootnote(targetId, out note)))
            {
                InlineStyle noteInline = ApplyInlineElement(element, styleSheet, inlineStyle, builder.Diagnostics);
                builder.AppendNote(note!, noteInline, paragraphStyle);
                return;
            }
        }
        if (tag.Equals("img", StringComparison.OrdinalIgnoreCase))
        {
            ReadImageElement(element, builder, styleSheet, inlineStyle, paragraphStyle);
            return;
        }

        if (SkippedElements.Contains(tag))
        {
            if (tag is "object" or "embed" or "iframe")
                builder.AddDiagnosticOnce("html.skip.external", "External or embedded HTML content was skipped.");
            return;
        }

        if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendSoftBreak(inlineStyle, paragraphStyle);
            return;
        }

        InlineStyle childInline = ApplyInlineElement(element, styleSheet, inlineStyle, builder.Diagnostics);
        ParagraphStyle childParagraph = ApplyParagraphElement(element, styleSheet, paragraphStyle);
        bool childPreserveWhitespace =
            preserveWhitespace ||
            tag.Equals("pre", StringComparison.OrdinalIgnoreCase) ||
            PreservesWhitespace(element, styleSheet);

        if (tag.Equals("ul", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("ol", StringComparison.OrdinalIgnoreCase))
        {
            ParagraphStyle listStyle = childParagraph with
            {
                ListKind = tag.Equals("ol", StringComparison.OrdinalIgnoreCase) ? ListKind.Numbered : ListKind.Bullet,
                IndentLevel = Math.Max(1, childParagraph.IndentLevel + 1),
                List = null,
            };
            IReadOnlyDictionary<string, string> listDeclarations = styleSheet.DeclarationsFor(element);
            bool ownMargin = listDeclarations.TryGetValue("margin-left", out string? margin) &&
                HtmlCss.TryParsePoints(margin, out _);
            if (ownMargin)
                listStyle = listStyle with { LeftIndent = (float)childParagraph.LeftIndentIn() };
            else if (childParagraph.LeftIndent is float inheritedOffset)
                listStyle = listStyle with { LeftIndent = Math.Clamp(inheritedOffset + 18f, -20000, 20000) };
            // Physical container indentation is independent of list nesting.
            int level = Math.Clamp((paragraphStyle.List?.Level ?? -1) + 1, 0, ListDefinition.MaxLevels - 1);
            if (tag.Equals("ol", StringComparison.OrdinalIgnoreCase))
            {
                bool explicitStart = int.TryParse(element.GetAttribute("start"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int start);
                ListNumberFormat format = element.GetAttribute("type") switch
                {
                    "a" => ListNumberFormat.LowerLetter, "A" => ListNumberFormat.UpperLetter,
                    "i" => ListNumberFormat.LowerRoman, "I" => ListNumberFormat.UpperRoman,
                    _ => ListNumberFormat.Decimal,
                };
                ListDefinition definition = ListDefinition.PlainNumbers.WithLevel(level,
                    ListLevel.DecimalAt(level) with { Start = explicitStart ? start : 1, Format = format });
                listStyle = listStyle with { List = new ListReference(definition, level) };
            }
            else
                listStyle = listStyle with { List = new ListReference(ListDefinition.PlainBullets.Copy(), level) };
            builder.FinishParagraph(force: false);
            ReadChildren(element, builder, styleSheet, childInline, listStyle, childPreserveWhitespace);
            builder.FinishParagraph(force: false);
            return;
        }

        if (ParagraphElements.Contains(tag))
        {
            if (tag.Equals("li", StringComparison.OrdinalIgnoreCase) && childParagraph.ListKind == ListKind.None)
                childParagraph = childParagraph with { ListKind = ListKind.Bullet, IndentLevel = Math.Max(1, childParagraph.IndentLevel) };

            if (tag.Equals("li", StringComparison.OrdinalIgnoreCase) && childParagraph.List is ListReference numbered &&
                int.TryParse(element.GetAttribute("value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                childParagraph = childParagraph with { List = new ListReference(numbered.Definition, numbered.Level, value) };

            if (tag.StartsWith("h", StringComparison.OrdinalIgnoreCase) && tag.Length == 2 && char.IsDigit(tag[1]))
                childInline = ApplyHeadingInline(tag, childInline);

            // The break goes on the style this paragraph is started with and not
            // on the one its children inherit. Every other paragraph property
            // here descends - a centred div centres the paragraphs inside it -
            // but a break does not: it happens once, where it was stated, and a
            // div carrying one is not three page breaks because it holds three
            // paragraphs.
            bool pageBreak = DeclaresPageBreakBefore(element, styleSheet, builder);
            int itemStart = builder.ParagraphCount;
            builder.StartParagraph(childParagraph with
            {
                PageBreakBefore = pageBreak,
                ColumnBreakBefore = !pageBreak && DeclaresColumnBreakBefore(element, styleSheet),
            });
            ReadChildren(element, builder, styleSheet, childInline, childParagraph, childPreserveWhitespace);
            builder.FinishParagraph(force: true);
            if (tag.Equals("li", StringComparison.OrdinalIgnoreCase))
                builder.FinishListItem(itemStart, childParagraph.List);
            return;
        }

        if (tag.Equals("table", StringComparison.OrdinalIgnoreCase))
        {
            ReadTable(element, builder, styleSheet, childInline, childParagraph, childPreserveWhitespace);
            return;
        }

        if (BlockContainers.Contains(tag))
        {
            builder.FinishParagraph(force: false);
            ReadChildren(element, builder, styleSheet, childInline, childParagraph, childPreserveWhitespace);
            builder.FinishParagraph(force: false);
            return;
        }

        ReadChildren(element, builder, styleSheet, childInline, childParagraph, childPreserveWhitespace);
    }

    private static void ReadTable(DomElement element, HtmlDocumentBuilder builder,
        HtmlStyleSheet styleSheet, InlineStyle inline, ParagraphStyle paragraph, bool preserveWhitespace)
    {
        builder.FinishParagraph(force: false);
        foreach (DomElement caption in element.ChildNodes.OfType<DomElement>().Where(child => child.LocalName == "caption"))
        {
            ReadChildren(caption, builder, styleSheet, inline, paragraph, preserveWhitespace);
            builder.FinishParagraph(force: false);
        }
        int start = builder.ParagraphCount;
        int nestedStart = builder.Tables.Count;
        var rows = new List<TableRow>();
        var occupied = new Dictionary<int, int>();
        double borderWidth = AttributeNumber(element, "border", 0) * .75;
        var borders = CellBorders.All(new TableBorder(BColor.FromName("black"), borderWidth));
        double padding = AttributeNumber(element, "cellpadding", 1) * .75;
        foreach (DomElement row in TableRows(element))
        {
            var cells = new List<TableCell>();
            int column = 0;
            foreach (DomElement cell in row.ChildNodes.OfType<DomElement>())
            {
                if (cell.LocalName is not ("td" or "th"))
                    continue;
                while (occupied.TryGetValue(column, out int through) && through > rows.Count)
                    column++;
                int span = (int)Math.Clamp(AttributeNumber(cell, "colspan", 1), 1, 1000);
                int rowSpan = (int)Math.Clamp(AttributeNumber(cell, "rowspan", 1), 1, 1000);
                int cellStart = builder.ParagraphCount;
                int cellTables = builder.Tables.Count;
                InlineStyle cellInline = ApplyInlineElement(cell, styleSheet, inline, builder.Diagnostics);
                if (cell.LocalName == "th")
                    cellInline = cellInline with { Bold = true };
                ParagraphStyle cellParagraph = ApplyParagraphElement(cell, styleSheet,
                    cell.LocalName == "th" ? paragraph with { Alignment = TextAlignment.Center } : paragraph);
                builder.StartParagraph(cellParagraph);
                ReadChildren(cell, builder, styleSheet, cellInline, cellParagraph, preserveWhitespace);
                builder.FinishParagraph(force: true);
                if (builder.ParagraphCount == cellStart)
                {
                    builder.StartParagraph(paragraph);
                    builder.FinishParagraph(force: true);
                }
                DocumentTable[] nested = builder.Tables.Skip(cellTables).ToArray();
                builder.Tables.RemoveRange(cellTables, builder.Tables.Count - cellTables);
                cells.Add(new TableCell(cellStart, builder.ParagraphCount - cellStart, column,
                    span, rowSpan, PageBackground(cell, styleSheet) ?? default, borders,
                    tables: nested, padding: new TableCellPadding(padding, padding, padding, padding)));
                for (int c = column; c < column + span; c++)
                    occupied[c] = rows.Count + rowSpan;
                column += span;
            }
            if (cells.Count > 0)
                rows.Add(new TableRow(cells));
        }
        builder.Tables.RemoveRange(nestedStart, builder.Tables.Count - nestedStart);
        if (rows.Count > 0 && builder.ParagraphCount > start)
        {
            string width = element.GetAttribute("width") ?? "";
            double? percent = width.EndsWith('%') && double.TryParse(width[..^1],
                NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
            double points = AttributeNumber(element, "width", 0) * .75;
            builder.Tables.Add(new DocumentTable(start, builder.ParagraphCount - start, rows,
                preferredWidth: points > 0 ? points : null, widthPercent: percent, autoFit: true));
        }
    }

    private static IEnumerable<DomElement> TableRows(DomElement table)
    {
        foreach (DomElement child in table.ChildNodes.OfType<DomElement>())
        {
            if (child.LocalName == "tr")
                yield return child;
            else if (child.LocalName is "thead" or "tbody" or "tfoot")
                foreach (DomElement row in TableRows(child))
                    yield return row;
        }
    }

    private static double AttributeNumber(DomElement element, string name, double fallback) =>
        double.TryParse(element.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture,
            out double value) && double.IsFinite(value) && value >= 0 ? Math.Min(value, 100000) : fallback;

    private static InlineStyle ApplyInlineElement(
        DomElement element,
        HtmlStyleSheet styleSheet,
        InlineStyle style,
        ICollection<DocumentDiagnostic> diagnostics)
    {
        string tag = element.LocalName;
        switch (tag)
        {
            case "b":
            case "strong":
                style = style with { Bold = true };
                break;
            case "i":
            case "em":
                style = style with { Italic = true };
                break;
            case "u":
                style = style with { Underline = true };
                break;
            case "s":
            case "strike":
            case "del":
                style = style with { Strikethrough = true };
                break;
            case "code":
            case "kbd":
            case "samp":
            case "tt":
                style = style with { FontFamily = "monospace" };
                break;
            case "a":
                style = ApplyLink(element, style, diagnostics);
                break;
            case "font":
                style = ApplyFontElement(element, style);
                break;
        }

        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        foreach (KeyValuePair<string, string> declaration in declarations)
            style = ApplyInlineCss(style, declaration.Key, declaration.Value);

        return style;
    }

    private static InlineStyle ApplyHeadingInline(string tag, InlineStyle style)
    {
        float size = tag switch
        {
            "h1" => 24f,
            "h2" => 20f,
            "h3" => 17f,
            "h4" => 15f,
            "h5" => 13f,
            _ => 12f,
        };

        return style with { Bold = true, FontSize = size };
    }

    private static InlineStyle ApplyLink(
        DomElement element,
        InlineStyle style,
        ICollection<DocumentDiagnostic> diagnostics)
    {
        string? href = element.GetAttribute("href")?.Trim();
        if (string.IsNullOrEmpty(href))
            return style;

        if (DocumentLinkTarget.IsAllowed(href))
            return style with { LinkHref = href };

        diagnostics.Add(DocumentDiagnostic.Warning(
            "html.link",
            "A hyperlink with a disallowed or relative target was dropped."));
        return style;
    }

    private static InlineStyle ApplyFontElement(DomElement element, InlineStyle style)
    {
        string? face = HtmlCss.ParseFontFamily(element.GetAttribute("face"));
        if (face is not null)
            style = style with { FontFamily = face };

        if (HtmlCss.TryParseColor(element.GetAttribute("color"), out BColor color))
            style = style with { Foreground = color };

        return style;
    }

    private static InlineStyle ApplyInlineCss(InlineStyle style, string property, string value)
    {
        switch (property)
        {
            case "font-weight":
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase) || value == "400")
                    return style with { Bold = false };
                if (value.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("bolder", StringComparison.OrdinalIgnoreCase) ||
                    (int.TryParse(value, out int weight) && weight >= 600))
                    return style with { Bold = true };
                break;
            case "font-style":
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    return style with { Italic = false };
                if (value.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("oblique", StringComparison.OrdinalIgnoreCase))
                    return style with { Italic = true };
                break;
            case "text-decoration":
            case "text-decoration-line":
                return ApplyTextDecoration(style, value);
            case "color":
                if (HtmlCss.TryParseColor(value, out BColor foreground))
                    return style with { Foreground = foreground };
                break;
            case "background":
            case "background-color":
                if (HtmlCss.TryParseColor(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out BColor background))
                {
                    // A transparent background is no background, and the model
                    // spells that BColor.Empty rather than a colour that happens
                    // to be invisible. Storing the transparent colour instead
                    // made every such run a highlighted run: it counts in
                    // info's highlightedRuns, it survives into every writer that
                    // emits a highlight, and it is a highlight nobody applied.
                    //
                    // It cost nothing while only an inline style could say it.
                    // Then type-selector rules arrived and LibreOffice writes
                    // "background: transparent" in the p rule of every HTML
                    // document it produces, which turned a latent wrong answer
                    // into one on every paragraph of every such file.
                    return style with
                    {
                        Background = background.A == 0 ? BColor.Empty : background,
                    };
                }

                break;
            case "text-transform":
                if (value.Equals("uppercase", StringComparison.OrdinalIgnoreCase))
                    return style with { Capitalization = TextCapitalization.AllCaps };
                if (value.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                    style.Capitalization == TextCapitalization.AllCaps)
                {
                    return style with { Capitalization = TextCapitalization.None };
                }
                break;
            case "font-variant":
            case "font-variant-caps":
                if (value.Equals("small-caps", StringComparison.OrdinalIgnoreCase))
                    return style with { Capitalization = TextCapitalization.SmallCaps };
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase) &&
                    style.Capitalization == TextCapitalization.SmallCaps)
                {
                    return style with { Capitalization = TextCapitalization.None };
                }
                break;
            case "font-family":
                return style with { FontFamily = HtmlCss.ParseFontFamily(value) };
            case "font-size":
                if (HtmlCss.TryParseFontSize(value, out float fontSize))
                    return style with { FontSize = fontSize };
                break;
        }

        return style;
    }

    private static InlineStyle ApplyTextDecoration(InlineStyle style, string value)
    {
        string lower = value.ToLowerInvariant();
        if (lower.Contains("none", StringComparison.Ordinal))
            return style with { Underline = false, Strikethrough = false };

        if (lower.Contains("underline", StringComparison.Ordinal))
            style = style with { Underline = true };
        if (lower.Contains("line-through", StringComparison.Ordinal))
            style = style with { Strikethrough = true };
        return style;
    }

    /// <summary>
    /// Whether an element's <c>white-space</c> declaration keeps the tabs and
    /// runs of spaces inside it, the way <c>pre</c> does. It is how a browser is
    /// told to show a tab as a tab, and so how a tab reaches this reader intact.
    /// <c>pre-line</c> is not one of them: it keeps line breaks and collapses
    /// everything else, tabs included.
    /// </summary>
    private static bool PreservesWhitespace(DomElement element, HtmlStyleSheet styleSheet)
    {
        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        return declarations.TryGetValue("white-space", out string? value) &&
               value.Trim().ToLowerInvariant() is "pre" or "pre-wrap" or "break-spaces";
    }

    /// <summary>
    /// Whether the declarations resolved for this element say the paragraph
    /// starts a new page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the element's own style and from a bare type rule beneath it,
    /// which is what every other declaration this reader looks at now does. It
    /// used to be the element alone, and the reasoning was that a rule selecting
    /// paragraphs is the cascade and this codec has none - true while the codec
    /// matched no selector at all, and no longer true of <c>p</c>. Carving the
    /// break out of what <see cref="HtmlStyleSheet"/> resolves would leave a
    /// document whose line spacing came from its stylesheet and whose page breaks
    /// did not, and no reader of either the code or the output could say why.
    /// A class rule still selects nothing, and that is the line that moved: not
    /// off the element, but from "no selector" to "one selector".
    /// </para>
    /// <para>
    /// <c>auto</c> is the initial value and is not a break. Neither is
    /// <c>avoid</c>, which asks for the opposite, nor the column and region
    /// values, which name a fragmentation into containers this model does not
    /// have.
    /// </para>
    /// <para>
    /// When the two spellings disagree - <c>page-break-before: always</c> beside
    /// <c>break-before: auto</c> - the break wins. CSS settles that by source
    /// order, and source order is exactly what
    /// <see cref="HtmlCss.ParseDeclarations"/> does not keep: it returns a
    /// dictionary. Rather than invent an order, both are read as one question,
    /// which is whether a break is stated anywhere in the style resolved for this
    /// paragraph. A document that says it twice means it once.
    /// </para>
    /// </remarks>
    private static bool DeclaresPageBreakBefore(
        DomElement element, HtmlStyleSheet styleSheet, HtmlDocumentBuilder builder)
    {
        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        bool breaks = false;
        foreach (string property in PageBreakProperties)
        {
            if (!declarations.TryGetValue(property, out string? value))
                continue;

            switch (value.Trim().ToLowerInvariant())
            {
                case "always":
                case "page":
                    breaks = true;
                    break;
                case "left":
                case "right":
                case "recto":
                case "verso":
                    // These break to the next page of a named side, which is one
                    // page break or two depending on where the document had got
                    // to. The break is kept because a break is what the document
                    // asked for; the side is not, because the model holds a flag
                    // and has no notion of which face of a sheet a page lands on.
                    // Keeping the larger half and naming the half that went is
                    // the whole reason this codec reports anything at all.
                    builder.AddDiagnosticOnce(
                        "html.page-break",
                        "A page break to a named left or right page was read as a plain page break; " +
                        "the model carries no page parity.");
                    breaks = true;
                    break;
            }
        }

        return breaks;
    }

    /// <summary>
    /// Whether the style resolved for this paragraph starts the next column:
    /// CSS Fragmentation's <c>break-before: column</c>, or the
    /// <c>column-break-before: always</c> older engines wrote. It is not
    /// inherited, for the reason a page break is not.
    /// </summary>
    private static bool DeclaresColumnBreakBefore(DomElement element, HtmlStyleSheet styleSheet)
    {
        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        return (declarations.TryGetValue("break-before", out string? value) &&
                value.Trim().Equals("column", StringComparison.OrdinalIgnoreCase)) ||
               (declarations.TryGetValue("column-break-before", out string? legacy) &&
                legacy.Trim().Equals("always", StringComparison.OrdinalIgnoreCase));
    }

    private static ParagraphStyle ApplyParagraphElement(
        DomElement element, HtmlStyleSheet styleSheet, ParagraphStyle style)
    {
        string tag = element.LocalName;
        float inheritedLeft = (float)style.LeftIndentIn();
        if (tag.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
            style = style with { IndentLevel = style.IndentLevel + 1 };

        string? align = element.GetAttribute("align");
        if (!string.IsNullOrWhiteSpace(align))
            style = ApplyAlignment(style, align);

        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        bool exactMargin = false;
        foreach (KeyValuePair<string, string> declaration in declarations)
        {
            switch (declaration.Key)
            {
                case "text-align":
                    style = ApplyAlignment(style, declaration.Value);
                    break;
                case "line-height":
                    if (HtmlCss.TryParseLineHeightLength(declaration.Value, out float lineHeight))
                    {
                        style = style with
                        {
                            LineSpacing = 1f, LineHeightRule = LineHeightRule.AtLeast, LineHeight = lineHeight,
                        };
                    }
                    else if (HtmlCss.TryParseLineSpacing(declaration.Value, out float lineSpacing))
                    {
                        style = style with
                        {
                            LineSpacing = lineSpacing, LineHeightRule = LineHeightRule.Proportional, LineHeight = 0f,
                        };
                    }
                    break;
                case "margin-top":
                    if (HtmlCss.TryParsePoints(declaration.Value, out float before))
                        style = style with { SpacingBefore = before };
                    break;
                case "margin-bottom":
                    if (HtmlCss.TryParsePoints(declaration.Value, out float after))
                        style = style with { SpacingAfter = after };
                    break;
                case "margin-left":
                    if (HtmlCss.TryParsePoints(declaration.Value, out float marginLeft))
                    {
                        float exact = Math.Clamp(inheritedLeft + marginLeft, -20000, 20000);
                        int indent = Math.Max(style.IndentLevel, (int)Math.Round(exact / 18f));
                        style = style with { LeftIndent = exact, IndentLevel = indent };
                        exactMargin = true;
                    }
                    break;
                case "padding-left":
                    if (HtmlCss.TryParsePoints(declaration.Value, out float left))
                        style = style with { IndentLevel = Math.Max(style.IndentLevel, (int)Math.Round(left / 18f)) };
                    break;
            }
        }

        // Canonicalize only after all declarations: changing the legacy level
        // later must not change an explicitly resolved physical margin.
        if (exactMargin && style.LeftIndent is float exactLeft && Math.Abs(exactLeft - style.IndentLevel * 18f) < 0.001f)
            style = style with { LeftIndent = null };
        return style;
    }

    private static ParagraphStyle ApplyAlignment(ParagraphStyle style, string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "center" => style with { Alignment = TextAlignment.Center },
            "right" or "end" => style with { Alignment = TextAlignment.Right },
            "justify" => style with { Alignment = TextAlignment.Justify },
            _ => style with { Alignment = TextAlignment.Left },
        };
    }

    /// <summary>
    /// The text of every <c>style</c> element in the document, joined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>style</c> is in this reader's skip list and stays there: its content is
    /// not text the document says, and a reader that let it through would put a
    /// stylesheet in the middle of the prose. What that skip also did was throw
    /// away everything in a stylesheet the model has somewhere to put. So the
    /// element is still skipped for content and read here twice over - by
    /// <see cref="HtmlPage"/> for the page, and by <see cref="HtmlStyleSheet"/>
    /// for the type-selector rules that stand under each element's own style.
    /// </para>
    /// <para>
    /// Every sheet rather than the first, because a producer may split the page
    /// away from the rest; <see cref="HtmlPage"/> takes the first rule it finds
    /// across them and <see cref="HtmlStyleSheet"/> reads them all in order. A
    /// <c>link</c> to an external sheet is not followed - this codec reads a
    /// document, and fetching a URL to find out how a paragraph is spaced would
    /// make reading one a network operation.
    /// </para>
    /// </remarks>
    private static string? StyleSheetText(DomDocument document)
    {
        StringBuilder? text = null;
        foreach (DomElement element in document.GetElementsByTagName("style"))
        {
            string content = element.TextContent;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            text ??= new StringBuilder();
            text.AppendLine(content);
        }

        return text?.ToString();
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }


    private static void ReadImageElement(
        DomElement element,
        HtmlDocumentBuilder builder,
        HtmlStyleSheet styleSheet,
        InlineStyle inlineStyle,
        ParagraphStyle paragraphStyle)
    {
        string? src = element.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
            return;

        src = src.Trim();
        if (!src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddDiagnosticOnce("html.skip.external", "External or embedded HTML content was skipped.");
            return;
        }

        if (!DataUriParser.TryParse(src, out string mediaType, out ReadOnlyMemory<byte> data, out _))
        {
            builder.Diagnostics.Add(DocumentDiagnostic.Warning(
                "html.image.malformed",
                "An embedded HTML image data URI was malformed and could not be decoded."));
            return;
        }

        if (data.Length > builder.Limits.MaxBinBytes)
        {
            builder.AddDiagnosticOnce(
                "html.image.limit",
                "An embedded HTML image exceeded MaxBinBytes and was skipped.");
            return;
        }

        string? alt = element.GetAttribute("alt");
        string? title = element.GetAttribute("title");
        string name = !string.IsNullOrWhiteSpace(title) ? title : (!string.IsNullOrWhiteSpace(alt) ? alt : "image");

        TryParseImageDimensions(element, styleSheet, out double? width, out double? height);

        BImageResource resource;
        try
        {
            resource = BImageResource.FromEncoded(data, mediaType);
        }
        catch (Exception ex)
        {
            builder.Diagnostics.Add(DocumentDiagnostic.Warning(
                "html.image.malformed",
                $"An embedded image payload could not be loaded: {ex.Message}"));
            return;
        }

        var request = new DocumentResourceRequest(
            resource,
            DocumentResourceProvenance.ReadFromSource,
            DocumentResourceDisposition.Embedded,
            name: name,
            sourceFormat: "HTML");

        if (!builder.Resources.TryAdmit(request, DocumentResourceOperations.ExtractToModel, out DocumentResourceId id, out string? denial))
        {
            builder.Diagnostics.Add(DocumentDiagnostic.Warning(
                "html.image.denied",
                "A picture was not read into the document because " + denial + "."));
            return;
        }

        var image = new InlineImage(resource, id, width, height, altText: alt, name: name);
        builder.AppendImage(image, inlineStyle, paragraphStyle);
    }

    private static void TryParseImageDimensions(
        DomElement element,
        HtmlStyleSheet styleSheet,
        out double? width,
        out double? height)
    {
        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        string? cssWidth = declarations.TryGetValue("width", out string? w) ? w : null;
        string? cssHeight = declarations.TryGetValue("height", out string? h) ? h : null;
        string? attrWidth = element.GetAttribute("width");
        string? attrHeight = element.GetAttribute("height");

        TryParseDimension(attrWidth, cssWidth, out width);
        TryParseDimension(attrHeight, cssHeight, out height);
    }

    private static bool TryParseDimension(string? attrValue, string? cssValue, out double? points)
    {
        points = null;
        string? val = cssValue;
        if (string.IsNullOrWhiteSpace(val))
            val = attrValue;
        if (string.IsNullOrWhiteSpace(val))
            return false;

        val = val.Trim();
        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double pxVal))
        {
            if (pxVal > 0 && double.IsFinite(pxVal))
            {
                points = pxVal * 72.0 / 96.0;
                return true;
            }
            return false;
        }

        if (HtmlCss.TryParsePoints(val, out float parsed) && parsed > 0 && float.IsFinite(parsed))
        {
            points = parsed;
            return true;
        }

        return false;
    }

    private sealed class HtmlDocumentBuilder(DocumentLimits limits, List<DocumentDiagnostic> diagnostics, DocumentConversionContextBuilder resources)
    {
        public DocumentLimits Limits => limits;
        public DocumentConversionContextBuilder Resources => resources;
        public HashSet<DomNode> NodesToSkip { get; } = [];
        public Dictionary<string, DocumentNote> Footnotes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterFootnote(string label, DocumentNote note)
        {
            Footnotes[label] = note;
        }

        public bool TryGetFootnote(string label, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DocumentNote? note) =>
            Footnotes.TryGetValue(label, out note);

        public void AppendNote(DocumentNote note, InlineStyle style, ParagraphStyle paragraphStyle)
        {
            EnsureParagraph(paragraphStyle);
            AddSegment(DocumentNote.PlaceholderText, style with { Note = note, Image = null });
        }

        private readonly List<RichTextParagraph> _paragraphs = [];
        public int ParagraphCount => _paragraphs.Count;
        public List<DocumentTable> Tables { get; } = [];
        private readonly List<Segment> _segments = [];
        private ParagraphStyle _paragraphStyle = ParagraphStyle.Default;
        private bool _paragraphOpen;
        private readonly HashSet<string> _diagnosticOnce = new(StringComparer.Ordinal);

        public List<DocumentDiagnostic> Diagnostics { get; } = diagnostics;

        public void AddDiagnosticOnce(string code, string message)
        {
            if (_diagnosticOnce.Add(code))
                Diagnostics.Add(DocumentDiagnostic.Warning(code, message));
        }

        public void StartParagraph(ParagraphStyle style)
        {
            if (_segments.Count > 0)
                FinishParagraph(force: false);

            _paragraphStyle = style;
            _paragraphOpen = true;
        }

        public void AppendText(string text, InlineStyle style, bool preserveWhitespace, ParagraphStyle paragraphStyle)
        {
            // DOM text already has its HTML character references decoded.
            //
            // A NUL is not text. The tree builder ignores one in body content,
            // so a document's DOM never holds it there; this parser keeps it,
            // and the renderer drew it as a missing glyph in the middle of a
            // word. Where the standard turns a NUL into U+FFFD instead - &#0;,
            // and inside a textarea - it is dropped here too, because by the
            // time the text arrives the two cases look the same.
            text = text.Replace("\0", string.Empty, StringComparison.Ordinal);
            string normalized = NormalizeText(text, preserveWhitespace);
            if (normalized.Length == 0)
                return;

            if (!_paragraphOpen && string.IsNullOrWhiteSpace(normalized))
                return;

            // Both trims are the collapsing rule, so both stop applying when the
            // element has asked for the text verbatim: CSS `pre`, `pre-wrap` and
            // `break-spaces` all keep white space at the start of a line, and a
            // reader that trimmed anyway would drop what the declaration was
            // written to protect. Without this, a paragraph the writer had
            // correctly marked still lost its leading space.
            if (!preserveWhitespace)
            {
                if (_segments.Count == 0)
                    normalized = normalized.TrimStart();
                else if (EndsWithWhitespace(_segments[^1].Text) && normalized.Length > 0 &&
                         char.IsWhiteSpace(normalized[0]))
                    normalized = normalized.TrimStart();
            }

            if (normalized.Length == 0)
                return;

            if (normalized.Length > limits.MaxRunLength)
            {
                normalized = normalized[..limits.MaxRunLength];
                AddDiagnosticOnce("html.limit.run", "An HTML text run exceeded MaxRunLength and was truncated.");
            }

            EnsureParagraph(paragraphStyle);
            AddSegment(normalized, style);
        }

        public void AppendSoftBreak(InlineStyle style, ParagraphStyle paragraphStyle)
        {
            EnsureParagraph(paragraphStyle);
            AddSegment(((char)0x2028).ToString(), style);
        }

        public void AppendImage(InlineImage image, InlineStyle style, ParagraphStyle paragraphStyle)
        {
            EnsureParagraph(paragraphStyle);
            AddSegment(InlineImage.PlaceholderText, style with { Image = image });
        }

        public void FinishParagraph(bool force)
        {
            if (!_paragraphOpen && _segments.Count == 0)
                return;
            if (!force && _segments.Count == 0)
            {
                _paragraphOpen = false;
                _paragraphStyle = ParagraphStyle.Default;
                return;
            }

            if (_paragraphs.Count >= limits.MaxParagraphCount)
            {
                AddDiagnosticOnce("html.limit.paragraphs", "HTML input exceeded MaxParagraphCount; remaining paragraphs were dropped.");
                _segments.Clear();
                _paragraphOpen = false;
                _paragraphStyle = ParagraphStyle.Default;
                return;
            }

            RichTextParagraph paragraph = RichTextParagraph.Empty.WithParagraphStyle(_paragraphStyle);
            int offset = 0;
            foreach (Segment segment in _segments)
            {
                paragraph = paragraph.InsertText(offset, segment.Text, segment.Style);
                offset += segment.Text.Length;
            }

            _paragraphs.Add(paragraph);
            _segments.Clear();
            _paragraphOpen = false;
            _paragraphStyle = ParagraphStyle.Default;
        }

        public RichTextDocument Build()
        {
            FinishParagraph(force: false);
            return _paragraphs.Count == 0
                ? RichTextDocument.Empty
                : RichTextDocument.FromParagraphs(_paragraphs).WithTables(Tables);
        }

        private void EnsureParagraph(ParagraphStyle paragraphStyle)
        {
            if (!_paragraphOpen)
                StartParagraph(paragraphStyle);
        }

        public void FinishListItem(int start, ListReference? list)
        {
            if (list is not ListReference itemList)
                return;
            bool first = true;
            for (int i = start; i < _paragraphs.Count; i++)
            {
                ParagraphStyle style = _paragraphs[i].Style;
                if (style.List is not ListReference candidate || candidate.Level != itemList.Level ||
                    !ReferenceEquals(candidate.Definition, itemList.Definition))
                    continue;
                if (first)
                    first = false;
                else
                    _paragraphs[i] = _paragraphs[i].WithParagraphStyle(style with { List = null, ListKind = ListKind.None });
            }
        }

        private void AddSegment(string text, InlineStyle style)
        {
            if (!style.IsNote && !style.IsImage && _segments.Count > 0 && _segments[^1].Style.Equals(style))
            {
                Segment previous = _segments[^1];
                _segments[^1] = new Segment(previous.Text + text, style);
                return;
            }

            _segments.Add(new Segment(text, style));
        }

        private static bool EndsWithWhitespace(string text) =>
            text.Length > 0 && char.IsWhiteSpace(text[^1]);

        /// <summary>
        /// The white space HTML collapses, which is not the white space Unicode
        /// recognises.
        /// </summary>
        /// <remarks>
        /// CSS names five characters, and a non-breaking space is deliberately
        /// not one of them - it exists to be a space that survives. This used to
        /// ask <c>char.IsWhiteSpace</c>, which says yes to U+00A0, so a
        /// <c>&amp;nbsp;</c> next to an ordinary space was folded into it and the
        /// document lost the character it was written with.
        /// </remarks>
        private static bool IsHtmlWhiteSpace(char character) =>
            character is ' ' or '\t' or '\n' or '\r' or '\f';

        private static string NormalizeText(string text, bool preserveWhitespace)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (preserveWhitespace)
                return text;

            var builder = new StringBuilder(text.Length);
            bool inWhitespace = false;
            foreach (char c in text)
            {
                if (IsHtmlWhiteSpace(c))
                {
                    if (!inWhitespace)
                        builder.Append(' ');
                    inWhitespace = true;
                }
                else
                {
                    builder.Append(c);
                    inWhitespace = false;
                }
            }

            return builder.ToString();
        }

        private readonly record struct Segment(string Text, InlineStyle Style);
    }
}
