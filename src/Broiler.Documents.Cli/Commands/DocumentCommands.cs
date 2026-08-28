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

namespace Broiler.Documents.Cli.Commands;

/// <summary>Creating, changing, converting, and dumping documents.</summary>
public static class DocumentCommands
{
    private static readonly OptionSpec[] EditingSpecs =
    {
        OptionSpec.Many("op", "operation", "One edit operation. Applied in the order given."),
        OptionSpec.Many("script", "path", "A file of edit operations, one per line; # starts a comment."),
    };

    public static CommandEntry New() => new(
        new CommandSpec(
            "new",
            "Create a document from text and write it in any supported format.",
            "new --out <path> [--text <text> | --from-file <path>] [--op <operation>]...",
            new[]
            {
                OptionSpec.Value("out", "path", "Where to write the document. Use - for standard output."),
                OptionSpec.Value("text", "text", "The body text. Newlines start new paragraphs."),
                OptionSpec.Value("from-file", "path", "Read the body text from a UTF-8 file, or - for standard input."),
            }
            .Concat(EditingSpecs)
            .Concat(DocumentOptions.WriteSpecs)
            .ToArray(),
            new[]
            {
                "new --out hello.docx --text \"Hello world\"",
                "new --out styled.rtf --text \"Title\\nBody\" --op \"inline:0:*:bold=on,size=18\" --op \"para:0:align=center\"",
                "new --out list.html --from-file items.txt --op \"para:*:list=bullet\"",
            },
            "The body is plain text split into paragraphs on newlines; --op then applies\n" +
            "formatting. Run 'broilerdoc edit --help' for the operation grammar."),
        RunNew);

    public static CommandEntry Edit() => new(
        new CommandSpec(
            "edit",
            "Apply edit operations to an existing document.",
            "edit <input> --out <path> [--op <operation>]... [--script <path>]...",
            new[]
            {
                OptionSpec.Value("out", "path", "Where to write the result. Use - for standard output."),
                OptionSpec.Flag("in-place", "Write the result back over the input."),
            }
            .Concat(EditingSpecs)
            .Concat(DocumentOptions.Specs)
            .Concat(DocumentOptions.WriteSpecs)
            .ToArray(),
            new[]
            {
                "edit report.docx --out report.docx --op \"replace:DRAFT:FINAL\"",
                "edit notes.md --out notes.rtf --op \"para:0:align=center\" --op \"inline:0:*:bold=on\"",
                "edit report.docx --in-place --script fixes.txt",
            },
            "operations\n" + string.Join("\n", EditOperations.GrammarHelp)),
        RunEdit);

    public static CommandEntry Convert() => new(
        new CommandSpec(
            "convert",
            "Read a document in one format and write it in another.",
            "convert <input> --out <path> [--to <format>]",
            new[]
            {
                OptionSpec.Value("out", "path", "Where to write the result. Use - for standard output."),
            }
            .Concat(DocumentOptions.Specs)
            .Concat(DocumentOptions.WriteSpecs)
            .ToArray(),
            new[]
            {
                "convert report.docx --out report.rtf",
                "convert report.docx --out - --to markdown",
                "convert page.html --out page.docx --fail-on warning",
            },
            "Both sides report their diagnostics. What a conversion drops is usually said on\n" +
            "the read side, the write side, or both, and --fail-on turns that into an exit\n" +
            "code a harness can branch on."),
        RunConvert);

    public static CommandEntry Dump() => new(
        new CommandSpec(
            "dump",
            "Print a document's content in a form that diffs cleanly.",
            "dump <input> [--as text|json|codes|outline]",
            new[]
            {
                OptionSpec.Value("as", "form", "text, json, codes, or outline.", "text"),
                OptionSpec.Value("out", "path", "Write to a file instead of standard output."),
            }
            .Concat(DocumentOptions.Specs)
            .ToArray(),
            new[]
            {
                "dump report.docx --as json > before.json",
                "dump report.docx --as codes",
                "dump report.docx --as outline",
            },
            "forms\n" +
            "  text     The plain text, paragraphs separated by newlines.\n" +
            "  json     Every paragraph, run, and resolved style. Lossless and stable, so two\n" +
            "           dumps of equal documents are byte-identical and diff cleanly.\n" +
            "  codes    The Formatting Codes projection: the component's own canonical,\n" +
            "           versioned rendering of a document's semantics (ADR 0006).\n" +
            "  outline  One line per paragraph with its style flags. Made for reading."),
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
        if (operations.Count > 0)
            document = EditOperations.Apply(document, operations);

        return WriteDocument(context, document, destination, Array.Empty<DocumentDiagnostic>());
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

        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        IReadOnlyList<string> operations = EditOperations.Collect(context.Line);
        if (operations.Count == 0)
            throw new UsageException("Give at least one --op or --script.");

        RichTextDocument edited = EditOperations.Apply(loaded.Document, operations);

        context.Report("read " + source + " as " + loaded.FormatName);
        context.Report("applied " + operations.Count.ToString(CultureInfo.InvariantCulture) + " operation(s)");
        DocumentReport.Print(context, loaded.Diagnostics, "read diagnostics");

        context.Result["source"] = source;
        context.Result["sourceFormat"] = loaded.FormatName;
        context.Result["operations"] = operations.Count;
        context.Result["readDiagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        // --in-place stages the whole output before replacing the file, so an
        // operation that fails half way leaves the original where it was.
        return WriteDocument(context, edited, destination, loaded.Diagnostics);
    }

    private static int RunConvert(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);
        string destination = context.Line.Require("out");

        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        context.Report("read " + source + " as " + loaded.FormatName + " (" + loaded.Status + ")");
        DocumentReport.Print(context, loaded.Diagnostics, "read diagnostics");

        context.Result["source"] = source;
        context.Result["sourceFormat"] = loaded.FormatName;
        context.Result["sourceStatus"] = loaded.Status.ToString().ToLowerInvariant();
        context.Result["readDiagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        return WriteDocument(context, loaded.Document, destination, loaded.Diagnostics);
    }

    private static int RunDump(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        string form = context.Line.Get("as", "text")!.ToLowerInvariant();
        string payload;

        switch (form)
        {
            case "text":
                payload = loaded.Document.PlainText;
                break;

            case "json":
                payload = ModelJson.Describe(loaded.Document).ToJsonString(JsonOutput.Indented);
                break;

            case "codes":
                FormatCodeProjection projection = new FormatCodeProjector().Project(loaded.Document);
                payload = projection.Text;
                context.Result["grammarVersion"] = projection.GrammarVersion;
                context.Result["tokenCount"] = projection.Tokens.Count;
                context.Result["projectionDiagnostics"] = ProjectionDiagnostics(projection);
                break;

            case "outline":
                payload = Outline(loaded.Document);
                break;

            default:
                throw new UsageException("--as expects text, json, codes, or outline, not \"" + form + "\".");
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

    /// <summary>The shared tail of every command that produces a document.</summary>
    private static int WriteDocument(
        CommandContext context,
        RichTextDocument document,
        string destination,
        IReadOnlyList<DocumentDiagnostic> readDiagnostics)
    {
        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentCodec codec = DocumentIo.ResolveWriteCodec(catalog, destination, context.Line.Get("to"));
        DocumentWriteOptions writeOptions = DocumentOptions.WriteOptionsFrom(context.Line);

        DocumentWriteResult result = DocumentIo.Save(document, destination, codec, writeOptions);

        context.Result["destination"] = destination;
        context.Result["destinationFormat"] = codec.Descriptor.Name;
        context.Result["destinationStatus"] = result.Status.ToString().ToLowerInvariant();
        context.Result["bytesWritten"] = result.BytesWritten;
        context.Result["writeDiagnostics"] = DocumentReport.ToJson(result.Diagnostics);

        DocumentReport.Print(context, result.Diagnostics, "write diagnostics");

        if (result.Status == DocumentResultStatus.Rejected)
        {
            context.Fail(
                "the " + codec.Descriptor.Name + " codec rejected the write; " + destination + " was not changed.");
            return ExitCode.Write;
        }

        context.Report(string.Format(
            CultureInfo.InvariantCulture,
            "wrote {0} as {1} ({2} bytes, {3})",
            destination,
            codec.Descriptor.Name,
            result.BytesWritten,
            result.Status));

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
            if (Math.Abs(style.LineSpacing - 1f) > 0.001f)
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
