using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.FormatCodes;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Cli.Commands;

/// <summary>Creating, changing, converting, and dumping documents.</summary>
public static class DocumentCommands
{
    private static readonly OptionSpec[] EditingSpecs =
    [
        OptionSpec.Many("op", "operation", "One edit operation. Applied in the order given."),
        OptionSpec.Many("script", "path", "A file of edit operations, one per line; # starts a comment."),
    ];

    public static CommandEntry New() => new(
        new CommandSpec(
            "new",
            "Create a document from text and write it in any supported format.",
            "new --out <path> [--text <text> | --from-file <path>] [--op <operation>]...",
            [
                OptionSpec.Value("out", "path", "Where to write the document. Use - for standard output."),
                OptionSpec.Value("text", "text", "The body text. Newlines start new paragraphs."),
                OptionSpec.Value("from-file", "path", "Read the body text from a UTF-8 file, or - for standard input."),
                .. EditingSpecs,
                .. DocumentOptions.WriteSpecs,
            ],
            [
                "new --out hello.docx --text \"Hello world\"",
                "new --out styled.rtf --text \"Title\\nBody\" --op \"inline:0:*:bold=on,size=18\" --op \"para:0:align=center\"",
                "new --out list.html --from-file items.txt --op \"para:*:list=bullet\"",
            ],
            "The body is plain text split into paragraphs on newlines; --op then applies\n" +
            "formatting. Run 'broilerdoc edit --help' for the operation grammar."),
        RunNew);

    public static CommandEntry Edit() => new(
        new CommandSpec(
            "edit",
            "Apply edit operations to an existing document.",
            "edit <input> --out <path> [--op <operation>]... [--script <path>]...",
            [
                OptionSpec.Value("out", "path", "Where to write the result. Use - for standard output."),
                OptionSpec.Flag("in-place", "Write the result back over the input."),
                .. EditingSpecs,
                .. DocumentOptions.Specs,
                .. DocumentOptions.WriteSpecs,
            ],
            [
                "edit report.docx --out report.docx --op \"replace:DRAFT:FINAL\"",
                "edit notes.md --out notes.rtf --op \"para:0:align=center\" --op \"inline:0:*:bold=on\"",
                "edit report.docx --in-place --script fixes.txt",
            ],
            "operations\n" + string.Join("\n", EditOperations.GrammarHelp)),
        RunEdit);

    public static CommandEntry Convert() => new(
        new CommandSpec(
            "convert",
            "Read a document in one format and write it in another.",
            "convert <input> --out <path> [--to <format>] [--sheet <name|index>] [--table <index>] [--dialect <preset>] [--delimiter <sep>]",
            [
                OptionSpec.Value("out", "path", "Where to write the result. Use - for standard output."),
                .. DocumentOptions.RoutingSpecs,
                .. DocumentOptions.Specs,
                .. DocumentOptions.WriteSpecs,
            ],
            [
                "convert report.docx --out report.rtf",
                "convert report.docx --out - --to markdown",
                "convert sheet.csv --out table.html --sheet 0",
                "convert report.docx --out table.csv --table 0 --dialect excel",
                "convert page.html --out page.docx --fail-on warning",
            ],
            "Both sides report their diagnostics. What a conversion drops is usually said on\n" +
            "the read side, the write side, or both, and --fail-on turns that into an exit\n" +
            "code a harness can branch on.\n\n" +
            "When converting spreadsheet or multi-table documents, --sheet or --table selects a\n" +
            "single table to convert. Dialect options (--dialect, --delimiter) configure CSV/TSV\n" +
            "formatting presets and separators."),
        RunConvert);

    public static CommandEntry Dump() => new(
        new CommandSpec(
            "dump",
            "Print a document's content in a form that diffs cleanly.",
            "dump <input> [--as text|json|codes|outline|grid|table] [--sheet <name|index>] [--table <index>]",
            [
                OptionSpec.Value("as", "form", "text, json, codes, outline, grid, or table.", "text"),
                OptionSpec.Value("out", "path", "Write to a file instead of standard output."),
                OptionSpec.Value("sheet", "name|index", "Sheet name or 0-based index to dump."),
                OptionSpec.Value("table", "index", "0-based index of table to dump."),
                OptionSpec.Value("cells", "tabs|lines",
                    "How --as text writes a table: a row per line, its cells separated by tabs, or a line per cell.", "tabs"),
                .. DocumentOptions.Specs,
            ],
            [
                "dump report.docx --as json > before.json",
                "dump report.docx --as codes",
                "dump report.docx --as outline",
                "dump sheet.csv --as grid",
            ],
            "forms\n" +
            "  text     The plain text, paragraphs separated by newlines. A table row is one\n" +
            "           line, its cells separated by tabs, as LibreOffice 24.8 and later\n" +
            "           export text; --cells lines puts each cell on lines of its own, as\n" +
            "           earlier releases do.\n" +
            "  json     Every paragraph, run, and resolved style. Lossless and stable, so two\n" +
            "           dumps of equal documents are byte-identical and diff cleanly.\n" +
            "  codes    The Formatting Codes projection: the component's own canonical,\n" +
            "           versioned rendering of a document's semantics (ADR 0006).\n" +
            "  outline  One line per paragraph with its style flags. Made for reading.\n" +
            "  grid     Tabular box-drawing grid of document tables / spreadsheet sheets.\n" +
            "  table    Synonym for grid."),
        RunDump);

    private static int RunNew(CommandContext context)
    {
        context.Line.RequireNoExtraPositionals(0);
        string destination = context.Line.Require("out");

        if (context.Line.Has("text") && context.Line.Has("from-file"))
            throw new UsageException("--text and --from-file are alternatives; give one.");

        string body = context.Line.Has("from-file")
            ? ReadText(context.Line.Require("from-file"))
            : context.Line.Get("text", string.Empty)!;

        RichTextDocument document = RichTextDocument.FromPlainText(body);
        IReadOnlyList<string> operations = EditOperations.Collect(context.Line);
        var resources = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        if (operations.Count > 0)
            document = EditOperations.Apply(document, operations, resources);

        return WriteDocument(context, document, destination, [], resources.Build());
    }

    private static int RunEdit(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        bool inPlace = context.Line.Has("in-place");
        if (inPlace && context.Line.Has("out"))
            throw new UsageException("--in-place and --out are alternatives; give one.");
        if (!inPlace && !context.Line.Has("out"))
            throw new UsageException("Give --out, or --in-place to overwrite the input.");
        if (inPlace && source == DocumentIo.StandardStreamToken)
            throw new UsageException("--in-place needs a file; standard input has nowhere to write back to.");

        string destination = inPlace ? source : context.Line.Require("out");

        DocumentCodecCatalog catalog = DocumentOptions.CreateCatalogFrom(context.Line);
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        IReadOnlyList<string> operations = EditOperations.Collect(context.Line);
        if (operations.Count == 0)
            throw new UsageException("Give at least one --op or --script.");

        // The edit continues the read's conversion rather than starting a new
        // one, so pictures that came out of the file keep the ids the model is
        // already holding and an inserted picture joins them.
        DocumentConversionContextBuilder resources = DocumentConversionContextBuilder.Continuing(
            loaded.Result.Resources,
            DocumentResourcePolicy.AllowOwnDocuments);
        RichTextDocument edited = EditOperations.Apply(loaded.Document, operations, resources);

        context.Report("read " + source + " as " + loaded.FormatName);
        context.Report("applied " + operations.Count.ToString(CultureInfo.InvariantCulture) + " operation(s)");
        DocumentReport.Print(context, loaded.Diagnostics, "read diagnostics");

        context.Result["source"] = source;
        context.Result["sourceFormat"] = loaded.FormatName;
        context.Result["operations"] = operations.Count;
        context.Result["readDiagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        // --in-place stages the whole output before replacing the file, so an
        // operation that fails half way leaves the original where it was.
        return WriteDocument(context, edited, destination, loaded.Diagnostics, resources.Build());
    }

    private static int RunConvert(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);
        string destination = context.Line.Require("out");

        DocumentCodecCatalog catalog = DocumentOptions.CreateCatalogFrom(context.Line);
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        context.Report("read " + source + " as " + loaded.FormatName + " (" + loaded.Status + ")");
        DocumentReport.Print(context, loaded.Diagnostics, "read diagnostics");

        context.Result["source"] = source;
        context.Result["sourceFormat"] = loaded.FormatName;
        context.Result["sourceStatus"] = loaded.Status.ToString().ToLowerInvariant();
        context.Result["readDiagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        int sheetIndex = DocumentOptions.ResolveSheetIndex(loaded.Document, context.Line);
        RichTextDocument documentToConvert = loaded.Document;
        if (sheetIndex >= 0)
        {
            documentToConvert = DocumentOptions.ExtractSheet(loaded.Document, sheetIndex);
            context.Report($"selected sheet/table {sheetIndex} ({documentToConvert.ParagraphCount} paragraphs)");
            context.Result["selectedSheet"] = sheetIndex;
        }

        string? dialectName = context.Line.Get("dialect") ?? context.Line.Get("csv-dialect");
        if (!string.IsNullOrEmpty(dialectName))
            context.Result["dialect"] = dialectName;

        return WriteDocument(context, documentToConvert, destination, loaded.Diagnostics, loaded.Result.Resources);
    }

    private static int RunDump(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        DocumentCodecCatalog catalog = DocumentOptions.CreateCatalogFrom(context.Line);
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        int sheetIndex = DocumentOptions.ResolveSheetIndex(loaded.Document, context.Line);
        RichTextDocument documentToDump = loaded.Document;
        if (sheetIndex >= 0)
        {
            documentToDump = DocumentOptions.ExtractSheet(loaded.Document, sheetIndex);
            context.Result["selectedSheet"] = sheetIndex;
        }

        string form = context.Line.Get("as", "text")!.ToLowerInvariant();
        string payload;

        switch (form)
        {
            case "text":
                string cells = context.Line.Get("cells", "tabs")!.ToLowerInvariant();
                if (cells is not ("tabs" or "lines"))
                    throw new UsageException("--cells expects tabs or lines, not \"" + cells + "\".");
                payload = LabelledText(documentToDump, joinsTableRows: cells == "tabs");
                break;

            case "json":
                payload = ModelJson.Describe(documentToDump).ToJsonString(JsonOutput.Indented);
                break;

            case "codes":
                FormatCodeProjection projection = FormatCodeProjector.Project(documentToDump);
                payload = projection.Text;
                context.Result["grammarVersion"] = FormatCodeProjection.GrammarVersion;
                context.Result["tokenCount"] = projection.Tokens.Count;
                context.Result["projectionDiagnostics"] = ProjectionDiagnostics(projection);
                break;

            case "outline":
                payload = Outline(documentToDump);
                break;

            case "grid":
            case "table":
                payload = TableIntrospection.RenderAllGrids(documentToDump);
                break;

            default:
                throw new UsageException("--as expects text, json, codes, outline, grid, or table, not \"" + form + "\".");
        }

        if (context.Line.Has("out"))
        {
            string destination = context.Line.Require("out");
            DocumentIo.WriteAllBytes(destination, new UTF8Encoding(false).GetBytes(payload));
            context.Report("wrote " + destination + " (" + form + ")");
        }
        else
        {
            context.WriteOutLine(payload);
        }

        context.Result["source"] = source;
        context.Result["format"] = loaded.FormatName;
        context.Result["form"] = form;
        context.Result["content"] = payload;
        context.Result["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        return DocumentReport.ApplyFailOn(
            loaded.Diagnostics,
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }

    /// <summary>LibreOffice's default bullet for each list level, the first level first.</summary>
    private const string TextExportBullets = "•◦▪";

    /// <summary>
    /// The document's text with each numbered paragraph's label before it, as
    /// LibreOffice's text export writes it: the label, one space, the text.
    /// </summary>
    /// <remarks>
    /// Lists known only by their kind use the same fallback labels as the
    /// renderer. A note's citation is its label too, as LibreOffice
    /// writes it, and the note's own text is not part of the body's. Nor are
    /// the characters that draw a form control, which LibreOffice imports as
    /// a drawn control (<see cref="InlineStyle.IsFormControl"/>).
    /// <para>
    /// The text in a shape is not the body's either. LibreOffice's export walks
    /// the body's paragraphs and nothing anchored to them (wrtasc.cxx,
    /// <c>SwASCWriter::WriteStream</c>), with one exception: a document whose
    /// whole body is one empty paragraph exports its first frame instead, which
    /// is how a form built only of text boxes has any text in it at all.
    /// </para>
    /// <para>
    /// A floating table is not the body's either: LibreOffice imports every
    /// table with a position as a text frame holding it (DOCX and RTF through
    /// DomainMapperTableHandler::endTable, ODT as written), so its cells are
    /// left out like a text box's, and a body of one empty paragraph beside it
    /// exports the table in its place, before any text box, as LibreOffice 24.2
    /// does for the corpus samples built that way.
    /// </para>
    /// <para>
    /// An inline WordArt shape's string is not written either
    /// (<see cref="InlineStyle.IsShapeText"/>): LibreOffice imports the shape
    /// as a drawing set as a character.
    /// </para>
    /// <para>
    /// A bullet is written as LibreOffice's text export writes one: not the
    /// glyph the document chose but LibreOffice's default bullet for the
    /// item's level - "•", "◦", "▪", and round again - whatever the list
    /// draws on the page (ascatr.cxx, numfunc::GetBulletChar). A document's
    /// "o", "◆" or Symbol-font private-use bullet would otherwise differ from
    /// LibreOffice's text on every item without either reader having lost a
    /// word.
    /// </para>
    /// </remarks>
    internal static string LabelledText(RichTextDocument document, bool joinsTableRows = true) =>
        LabelledText(document, joinsTableRows, pageNumber: null);

    /// <param name="document">The document.</param>
    /// <param name="joinsTableRows">Whether a table row is one line, its cells separated by tabs.</param>
    /// <param name="pageNumber">
    /// The number a page number field is written as, where it is known to
    /// differ from the result the source saved; null to write the saved result.
    /// </param>
    private static string LabelledText(RichTextDocument document, bool joinsTableRows, int? pageNumber)
    {
        var numberer = new ListNumberer();
        var notes = new NoteNumberer(document.NoteNumbering);
        var builder = new StringBuilder();
        var cellSeparators = new Dictionary<int, char>();
        if (joinsTableRows)
            ReadCellSeparators(document.Tables, cellSeparators);
        IEnumerable<DocumentTable> FloatingTables(IEnumerable<DocumentTable> tables)
        {
            foreach (DocumentTable table in tables)
            {
                if (table.IsFloating)
                    yield return table;
                else
                    foreach (DocumentTable nested in FloatingTables(table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Tables)))
                        yield return nested;
            }
        }

        DocumentTable[] floating = [.. FloatingTables(document.Tables)];
        bool InFloatingTable(int index) =>
            floating.Any(table => index >= table.ParagraphIndex && index < table.ParagraphEnd);

        int[] body = [.. Enumerable.Range(0, document.ParagraphCount).Where(index => !InFloatingTable(index))];
        // A frame set as a character is a character of its paragraph to
        // LibreOffice, so a paragraph holding one is not empty
        // (SwASCWriter::WriteStream tests the node's text), and so is a shape
        // the source sets in the line though the layout places it as a block
        // (inline-textbox-position.docx).
        if ((body.Length == 0 || (body.Length == 1 && document.Paragraphs[body[0]].Length == 0 &&
                !document.Shapes.Any(shape => shape.ParagraphIndex == body[0] && (shape.IsAsCharacter || shape.InlineInSource)))) &&
            document.Tables.All(table => table.IsFloating))
        {
            // LibreOffice's first frame is the floating table's where there is
            // one, even with a text box in its cells (tdf170516_drawingBeforePlainText.docx).
            if (floating.Length > 0)
            {
                body = [.. Enumerable.Range(floating[0].ParagraphIndex, floating[0].ParagraphCount)];
            }
            // LibreOffice writes the first of the document's frames and
            // drawings in the order it made them - the header's and footer's
            // first, as the styles are read before the body - and nothing
            // when that is a drawing (SwASCWriter::WriteStream takes
            // GetSpzFrameFormats()[0], and a drawing has no text section):
            // IndexingExport_Shapes.odt's first shape is a circle. A drawn
            // group's text boxes come before its drawing; FirstFrame below
            // takes those. This is ODF's order, where the source names its
            // text frames (DocumentShape.IsTextFrame); a DOCX's import makes
            // the body's shapes first, which FirstFrame below follows.
            else if (StatesTextFrames(document) && document.RunningContent.HeaderShapes(PageSelection.Default)
                .Concat(document.RunningContent.FooterShapes(PageSelection.Default))
                .Concat(document.Shapes)
                .FirstOrDefault() is DocumentShape firstMade && (firstMade.IsTextFrame ||
                    (firstMade is { IsCustomShape: true, Tables.Count: 0, GroupNumber: 0 } &&
                        !(firstMade.ZOrder > 0 && document.Shapes.Any(shape => shape != firstMade && shape.ZOrder == firstMade.ZOrder)))))
            {
                // A text frame the source names as one is written whether it
                // stands in the header or the body (tdf118242.odt's header frame).
                return firstMade.IsTextFrame
                    ? LabelledText(RichTextDocument.FromParagraphs(firstMade.Paragraphs).WithTables(firstMade.Tables)
                        .WithNoteNumbering(document.NoteNumbering), joinsTableRows)
                    : "";
            }
            // Otherwise it is the first shape LibreOffice made, the body's
            // before the headers' and footers' (whose references a DOCX's
            // closing w:sectPr names after the text), whatever it is: the
            // export looks at that one only, and writes nothing when it is a
            // drawing, such as a text watermark, rather than a frame with text
            // of its own (tdf120760_ZOrderInHeader.docx, fdo66688.docx).
            else if (FirstFrame(ShapeParts(document).FirstOrDefault(part => part.Count > 0) ?? []) is DocumentShape only)
            {
                // A page number in a header or footer frame is the number its
                // page gave it when LibreOffice laid the first page out: that
                // page's number where the first page shows the header or
                // footer, nought where it does not (tdf112446_frameStyle.docx
                // writes 1, tdf112443.docx, whose first page has a footer of its
                // own, 0; each the other way round with w:titlePg toggled).
                int? framePageNumber = null;
                foreach ((DocumentShape shape, PageSelection selection) in RunningShapes(document.RunningContent))
                {
                    if (ReferenceEquals(shape, only))
                    {
                        bool onFirstPage = document.RunningContent.DifferentFirstPage
                            ? selection == PageSelection.First
                            : selection == PageSelection.Default;
                        framePageNumber = onFirstPage ? document.Sections.FirstOrDefault()?.PageNumberStart ?? 1 : 0;
                        break;
                    }
                }

                return LabelledText(RichTextDocument.FromParagraphs(only.Paragraphs).WithTables(only.Tables)
                    .WithNoteNumbering(document.NoteNumbering), joinsTableRows, framePageNumber);
            }
        }

        // The paragraphs LibreOffice has laid out when it writes the text: the
        // first, and those kept with it. See UnlaidNumber. A frame anchored to
        // the first paragraph (not as a character) has the whole first page
        // and the first paragraph of the next laid out to place it; without a
        // layout of its own this keeps every saved number then, which is the
        // saved page's number on those pages.
        int laidOut = 0;
        while (laidOut < body.Length - 1 && document.Paragraphs[body[laidOut]].Style.KeepWithNext)
            laidOut++;
        if (body.Length > 0 && document.Shapes.Any(shape => shape.ParagraphIndex == body[0] && !shape.IsAsCharacter))
            laidOut = body.Length;

        bool first = true;
        for (int position = 0; position < body.Length; position++)
        {
            int i = body[position];
            RichTextParagraph paragraph = document.Paragraphs[i];
            bool unlaid = position > laidOut;
            if (!first)
                builder.Append(cellSeparators.GetValueOrDefault(i, '\n'));
            first = false;

            string? label = numberer.Advance(paragraph.Style);
            if (label is not null)
            {
                if (paragraph.Style.ListKind == ListKind.Bullet)
                {
                    int level = paragraph.Style.List?.Level ?? Math.Max(0, paragraph.Style.IndentLevel - 1);
                    label = TextExportBullets[Math.Max(0, level) % TextExportBullets.Length].ToString();
                }
                builder.Append(label).Append(' ');
            }

            int offset = 0;
            foreach (StyleRun run in paragraph.Runs)
            {
                int length = Math.Min(run.Length, paragraph.Length - offset);
                if (run.Style.Note is DocumentNote { Kind: NoteKind.Footnote, CustomMark: null } pageNote && unlaid &&
                    document.NoteNumbering.FootnoteRestart == NoteNumberingRestart.EachPage)
                    builder.Append(UnlaidNumber(pageNote.Section?.Format ?? document.NoteNumbering.FootnoteFormat, 0));
                else if (run.Style.Note is DocumentNote note)
                    builder.Append(notes.Label(note));
                else if (run.Style.PageField is { Kind: PageFieldKind.PageNumber } field && pageNumber is int number)
                    builder.Append(number == 0 ? UnlaidNumber(field.Format, field.Adjust) : ListNumberer.Format(number, field.Format));
                else if (run.Style.PageField is { Kind: PageFieldKind.PageNumber } unlaidField && unlaid)
                    builder.Append(UnlaidNumber(unlaidField.Format, unlaidField.Adjust));
                else if (!run.Style.IsImage && !run.Style.IsFormElement && !run.Style.IsFormControl &&
                    !run.Style.IsShapeText)
                    builder.Append(paragraph.Text, offset, length);
                offset += length;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// A number that only the page layout knows - a page number, or a
    /// footnote's number where footnotes restart on each page - as
    /// LibreOffice's text export writes it outside the paragraphs it has laid
    /// out: the number 0.
    /// </summary>
    /// <remarks>
    /// <c>soffice --convert-to txt</c> writes the text before the document is
    /// laid out. Only the first body paragraph has been formatted, with the
    /// paragraphs it keeps with it (the next is formatted to see whether it
    /// fits, <c>SwContentFrame::MakeAll</c>). Every other page field still
    /// holds the value <c>SwTextField</c> expanded when it was read, with no
    /// layout: <c>SwPageNumberField</c>'s page 0 of 0 pages. Its type writes
    /// that as "0" in Arabic numerals, as nothing in any other numbering
    /// (<c>SvxNumberType::GetNumStr</c> allows 0 only for Arabic), and as
    /// nothing when the field shows another page than its own, which is past
    /// the last page or before the first (<c>SwPageNumberFieldType::Expand</c>).
    /// LibreOffice 24.2.7.2 writes "1", "0", "0" for a page number in each of
    /// three paragraphs, "1" and "0" with a heading kept with the paragraph
    /// after it, and "foo1", "bar0" for two footnotes numbered by page.
    /// </remarks>
    internal static string UnlaidNumber(ListNumberFormat format, int adjust) =>
        adjust == 0 && format == ListNumberFormat.Decimal ? "0" : "";

    /// <summary>
    /// The headers' and footers' shapes in the order Word writes their
    /// references, which is the order LibreOffice's import reads them in:
    /// even header, default header, even footer, default footer, then the
    /// first page's.
    /// </summary>
    private static IEnumerable<(DocumentShape Shape, PageSelection Selection)> RunningShapes(RunningContent running) =>
        RunningParts.SelectMany(part => (part.Header ? running.HeaderShapes(part.Selection) : running.FooterShapes(part.Selection))
            .Select(shape => (shape, part.Selection)));

    private static readonly (bool Header, PageSelection Selection)[] RunningParts =
    [
        (true, PageSelection.Even), (true, PageSelection.Default), (false, PageSelection.Even),
        (false, PageSelection.Default), (true, PageSelection.First), (false, PageSelection.First),
    ];

    /// <summary>
    /// The shapes LibreOffice's import makes, part by part in the order it
    /// makes them: the body's (those not anchored to the page), then each
    /// header and footer part in <see cref="RunningShapes"/>'s order. A drawn
    /// group is numbered within its own part, so it is looked for in one part
    /// only.
    /// </summary>
    private static IEnumerable<IReadOnlyList<DocumentShape>> ShapeParts(RichTextDocument document)
    {
        yield return [.. document.Shapes.Where(shape => !shape.IsPageAnchored)];
        foreach ((bool header, PageSelection selection) in RunningParts)
            yield return header
                ? document.RunningContent.HeaderShapes(selection)
                : document.RunningContent.FooterShapes(selection);
    }

    /// <summary>
    /// Whether the source names its word processor text frames as such
    /// (<see cref="DocumentShape.IsTextFrame"/>, which only ODF states), so
    /// that its frames are made in ODF's order: the headers' and footers'
    /// before the body's.
    /// </summary>
    private static bool StatesTextFrames(RichTextDocument document) =>
        document.Shapes.Any(shape => shape.IsTextFrame) ||
        RunningParts.Any(part => (part.Header
            ? document.RunningContent.HeaderShapes(part.Selection)
            : document.RunningContent.FooterShapes(part.Selection)).Any(shape => shape.IsTextFrame));

    /// <summary>
    /// The frame LibreOffice's text export writes for a body of one empty
    /// paragraph, or null where it writes none.
    /// </summary>
    /// <remarks>
    /// The export takes the document's first frame format, whatever it is
    /// (wrtasc.cxx, <c>SwASCWriter::WriteStream</c>), and writes its text when
    /// it is a frame with text of its own. A drawing has no text of its own and
    /// a picture's frame holds the picture, so either one first means nothing
    /// is written (tdf117028.docx's rectangle before its text box). A drawn
    /// group's text boxes are frames the import makes before the group's
    /// drawing, so the first of them comes first, even an empty one
    /// (dml-groupshape-sdt.docx); a group with none is a drawing.
    /// </remarks>
    private static DocumentShape? FirstFrame(IEnumerable<DocumentShape> shapes)
    {
        static bool IsTextFrame(DocumentShape shape) =>
            (!shape.IsCustomShape || shape.Tables.Count > 0) && !shape.StretchesTextToBox &&
            !(shape.Image is not null && !shape.HasText);

        List<DocumentShape> list = [.. shapes];
        if (list.Count == 0)
            return null;

        // The members of one drawn group: those the reader numbered as one,
        // or, where it numbers none, those sharing the first shape's stated
        // stacking order, as a group's shapes do (n825305.rtf, tdf88811.rtf).
        DocumentShape first = list[0];
        Func<DocumentShape, bool>? sameGroup = first.GroupNumber > 0
            ? shape => shape.GroupNumber == first.GroupNumber
            : first.ZOrder > 0 && list.Count > 1 && list[1].ZOrder == first.ZOrder
                ? shape => shape.ZOrder == first.ZOrder
                : null;
        DocumentShape? frame = sameGroup is not null
            ? list.TakeWhile(sameGroup).FirstOrDefault(IsTextFrame)
            : IsTextFrame(first) ? first : null;
        return frame is { HasText: true } ? frame : null;
    }

    // Writer's text export separates table cells with tabs and rows with
    // newlines since LibreOffice 24.8 (tdf#144576, SwASCWriter::WriteTable).
    // Paragraphs inside a cell keep their own newline boundaries. Earlier
    // releases write each cell's paragraphs on lines of their own, which is
    // what leaving these out gives.
    private static void ReadCellSeparators(
        IReadOnlyList<DocumentTable> tables, Dictionary<int, char> separators)
    {
        foreach (DocumentTable table in tables)
        {
            bool firstRow = true;
            foreach (TableRow row in table.Rows)
            {
                bool firstCell = true;
                foreach (TableCell cell in row.Cells)
                {
                    if (cell.ParagraphCount == 0)
                        continue;
                    if (!firstCell)
                        separators[cell.ParagraphIndex] = '\t';
                    else if (!firstRow)
                        separators[cell.ParagraphIndex] = '\n';
                    ReadCellSeparators(cell.Tables, separators);
                    firstCell = false;
                }
                firstRow = false;
            }
        }
    }

    /// <summary>The shared tail of every command that produces a document.</summary>
    private static int WriteDocument(
        CommandContext context,
        RichTextDocument document,
        string destination,
        IReadOnlyList<DocumentDiagnostic> readDiagnostics,
        DocumentConversionContext? resources = null)
    {
        DocumentCodecCatalog catalog = DocumentOptions.CreateCatalogFrom(context.Line);
        DocumentCodec codec = DocumentIo.ResolveWriteCodec(catalog, destination, context.Line.Get("to"));

        // A PDF read leaves its pictures as decoded samples, and every writer
        // needs encoded bytes. See PictureEncoding for why encoding them is this
        // tool's decision rather than the writer's.
        (document, resources, int encoded) = PictureEncoding.EncodeDecodedPictures(document, resources);
        if (encoded > 0)
            context.Report($"encoded {encoded} picture(s) the source held as decoded samples, as PNG");

        DocumentWriteOptions writeOptions = DocumentOptions.WriteOptionsFrom(context.Line, resources);

        DocumentWriteResult result = DocumentIo.Save(document, destination, codec, writeOptions);

        context.Result["destination"] = destination;
        context.Result["destinationFormat"] = codec.Descriptor.Name;
        context.Result["destinationStatus"] = result.Status.ToString().ToLowerInvariant();
        context.Result["bytesWritten"] = result.BytesWritten;
        context.Result["picturesEncoded"] = encoded;
        context.Result["writeDiagnostics"] = DocumentReport.ToJson(result.Diagnostics);

        DocumentReport.Print(context, result.Diagnostics, "write diagnostics");

        if (result.Status == DocumentResultStatus.Rejected)
        {
            context.Fail(
                "the " + codec.Descriptor.Name + " codec rejected the write; " + destination + " was not changed.");
            return ExitCode.Write;
        }

        context.Report($"wrote {destination} as {codec.Descriptor.Name} ({result.BytesWritten} bytes, {result.Status})");

        return DocumentReport.ApplyFailOn(
            readDiagnostics.Concat(result.Diagnostics),
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }

    private static JsonArray ProjectionDiagnostics(FormatCodeProjection projection)
    {
        var array = new JsonArray();
        foreach (FormatCodeDiagnostic diagnostic in projection.Diagnostics)
        {
            array.Add(new JsonObject
            {
                ["severity"] = diagnostic.Severity.ToString().ToLowerInvariant(),
                ["code"] = diagnostic.Code,
                ["message"] = diagnostic.Message,
            });
        }

        return array;
    }

    /// <summary>One line per paragraph: index, style flags, and the text.</summary>
    private static string Outline(RichTextDocument document)
    {
        var builder = new StringBuilder();

        for (int i = 0; i < document.ParagraphCount; i++)
        {
            RichTextParagraph paragraph = document.Paragraphs[i];
            ParagraphStyle style = paragraph.Style;

            var flags = new List<string>();
            if (style.Alignment != TextAlignment.Left)
                flags.Add(style.Alignment.ToString().ToLowerInvariant());
            if (style.ListKind != ListKind.None)
                flags.Add(style.ListKind.ToString().ToLowerInvariant());
            if (style.IndentLevel > 0)
                flags.Add("indent" + style.IndentLevel.ToString(CultureInfo.InvariantCulture));
            if (style.LineHeightRule == LineHeightRule.Exact)
                flags.Add("line" + style.LineHeight.ToString("0.##", CultureInfo.InvariantCulture) + "pt");
            else if (style.LineHeightRule == LineHeightRule.AtLeast)
                flags.Add("line>=" + style.LineHeight.ToString("0.##", CultureInfo.InvariantCulture) + "pt");
            else if (Math.Abs(style.LineSpacing - 1f) > 0.001f)
                flags.Add("spacing" + style.LineSpacing.ToString("0.##", CultureInfo.InvariantCulture));

            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.Bold && !flags.Contains("bold"))
                    flags.Add("bold");
                if (run.Style.Italic && !flags.Contains("italic"))
                    flags.Add("italic");
                if (run.Style.Underline && !flags.Contains("underline"))
                    flags.Add("underline");
                if (run.Style.IsLink && !flags.Contains("link"))
                    flags.Add("link");
                if (run.Style.IsImage && !flags.Contains("image"))
                    flags.Add("image");
                if (run.Style.IsNote && !flags.Contains("note"))
                    flags.Add("note");
            }

            builder.Append(i.ToString("D4", CultureInfo.InvariantCulture));
            builder.Append("  ");
            builder.Append(('[' + string.Join(",", flags) + ']').PadRight(28));
            builder.Append("  ");
            builder.AppendLine(paragraph.Text.Replace('\t', ' '));
        }

        return builder.ToString();
    }

    private static string ReadText(string path)
    {
        if (path == DocumentIo.StandardStreamToken)
            return Console.In.ReadToEnd();

        if (!File.Exists(path))
            throw new DocumentIoException(ExitCode.Input, "File not found: " + path);

        return File.ReadAllText(path);
    }
}
