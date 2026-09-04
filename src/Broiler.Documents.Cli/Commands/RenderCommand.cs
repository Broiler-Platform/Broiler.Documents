using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Commands;

/// <summary>Rasterizing a document to images.</summary>
public static class RenderCommand
{
    public static CommandEntry Create() => new(
        new CommandSpec(
            "render",
            "Render a document to PNG (or JPEG or BMP) pages.",
            "render <input> --out <path> [--dpi <n>] [--continuous] [--page-size <size>]",
            new[]
            {
                OptionSpec.Value("out", "path", "Output image path. {page} in the path is replaced with the page number."),
                OptionSpec.Value("manifest", "path", "Write a JSON description of the render, for a harness to keep."),
            }
            .Concat(RenderPipeline.Specs)
            .Concat(DocumentOptions.Specs)
            .ToArray(),
            new[]
            {
                "render report.docx --out report.png",
                "render report.docx --out pages/{page}.png --dpi 150",
                "render report.docx --out report.png --continuous --font-dir ./fonts",
            },
            "A document may state the page it was written for, and DOCX, ODT and RTF all do.\n" +
            "A render given no page of its own takes it; --page-size, --margin or --landscape\n" +
            "override it, and --dpi never does. So both sides of a comparison need the same\n" +
            "flags or the same stated page. The manifest records what was used either way.\n" +
            "\n" +
            "For comparing two exports, --continuous is usually the setting you want: with\n" +
            "pagination on, one extra line before a page break shifts every later page and a\n" +
            "one-line difference reads as a whole-document difference.\n" +
            "\n" +
            "Fonts are the other half of reproducibility. Without --font-file or --font-dir\n" +
            "every family draws in one host face, so two machines with different font sets\n" +
            "disagree about a document neither of them got wrong."),
        Run);

    private static int Run(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);
        string destination = context.Line.Require("out");

        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        RenderPipeline pipeline = RenderPipeline.Create(context.Line);
        using RenderOutcome outcome = pipeline.Render(loaded.Document);
        pipeline.Write(outcome, destination);

        JsonObject manifest = pipeline.Manifest(outcome);

        context.Report("read " + source + " as " + loaded.FormatName + " (" + loaded.Status + ")");
        DocumentReport.Print(context, loaded.Diagnostics, "read diagnostics");
        context.Report();

        foreach (string path in outcome.WrittenPaths)
            context.Report("wrote " + path);

        context.Report(string.Format(
            CultureInfo.InvariantCulture,
            "{0} page(s) at {1} DPI, {2}x{3} pixels each",
            outcome.Pages.Count,
            pipeline.Setup.Dpi,
            outcome.Pages[0].Width,
            outcome.Pages[0].Height));

        foreach (string note in outcome.Notes)
            context.Warn(note);

        if (pipeline.Fonts.UnmappedRequests.Count > 0)
        {
            context.Detail(
                "families with no --font-file mapping, drawn in the host fallback face: " +
                string.Join(", ", pipeline.Fonts.UnmappedRequests));
        }

        if (context.Line.Has("manifest"))
        {
            string path = context.Line.Require("manifest");
            var document = new JsonObject
            {
                ["source"] = source,
                ["sourceFormat"] = loaded.FormatName,
                ["render"] = manifest.DeepClone(),
                ["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics),
            };

            DocumentIo.WriteAllBytes(
                path,
                new UTF8Encoding(false).GetBytes(document.ToJsonString(JsonOutput.Indented)));
            context.Report("wrote " + path);
        }

        context.Result["source"] = source;
        context.Result["sourceFormat"] = loaded.FormatName;
        context.Result["render"] = manifest;
        context.Result["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        return DocumentReport.ApplyFailOn(
            loaded.Diagnostics,
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }
}
