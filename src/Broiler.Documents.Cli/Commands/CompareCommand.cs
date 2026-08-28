using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Comparison;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Cli.Rendering;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Commands;

/// <summary>
/// The command the whole tool is pointed at: decide whether two things are the
/// same, and when they are not, say precisely how they differ.
/// </summary>
public static class CompareCommand
{
    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    public static CommandEntry Create() => new(
        new CommandSpec(
            "compare",
            "Compare two documents or two images and report exactly how they differ.",
            "compare <left> <right> [--render] [--diff <path>] [--tolerance <n>]",
            new[]
            {
                OptionSpec.Value("mode", "kind", "auto, image, or document.", "auto"),
                OptionSpec.Flag("render", "In document mode, also render both sides and compare the pixels."),
                OptionSpec.Value("render-out", "path", "Keep the rendered pages. {side} and {page} are replaced."),
                OptionSpec.Value("diff", "path", "Write a difference image. {page} is replaced for a multi-page compare."),
                OptionSpec.Value("diff-style", "style", "overlay, mask, or heat.", "overlay"),
                OptionSpec.Value("tolerance", "n", "Per-channel difference that still counts as equal, 0-255.", "0"),
                OptionSpec.Value("max-different", "n", "Differing pixels allowed before the verdict is 'different'.", "0"),
                OptionSpec.Value("max-different-ratio", "r", "Differing pixel fraction allowed, 0-1.", "0"),
                OptionSpec.Flag("allow-size-difference", "Do not fail on images of different sizes; compare the shared region."),
                OptionSpec.Flag("ignore-whitespace", "Collapse runs of whitespace before comparing text."),
                OptionSpec.Flag("ignore-case", "Compare text without regard to letter case."),
                OptionSpec.Flag("ignore-inline-style", "Compare text and paragraph structure only."),
                OptionSpec.Flag("ignore-paragraph-style", "Compare text and run formatting only."),
                OptionSpec.Value("max-differences", "n", "Stop listing structural differences after this many.", "50"),
            }
            .Concat(RenderPipeline.Specs)
            .Concat(DocumentOptions.Specs)
            .ToArray(),
            new[]
            {
                "compare before.png after.png --diff diff.png",
                "compare reference.docx roundtripped.docx",
                "compare a.docx b.docx --render --continuous --diff diff.png --tolerance 2",
                "compare a.png b.png --tolerance 2 --max-different-ratio 0.0005 --json",
            },
            "modes\n" +
            "  image     Two images, compared pixel by pixel.\n" +
            "  document  Two documents, compared through the model: text, paragraph structure,\n" +
            "            run formatting, and the Formatting Codes projection.\n" +
            "  auto      image when both paths look like images, document otherwise.\n" +
            "\n" +
            "Reach for document mode first. A pixel diff says two exports look different;\n" +
            "the structural comparison says paragraph 14 lost its bold run, which is a\n" +
            "sentence you can turn into a test. Add --render when what you need to know is\n" +
            "whether the two also *look* the same.\n" +
            "\n" +
            "Both rendered sides go through one pipeline with one set of options, so any\n" +
            "pixel that differs came from the documents and not from the render.\n" +
            "\n" +
            "Exit 0 when the two agree within tolerance, 5 when they differ. A missing file\n" +
            "or an unreadable document exits 2 or 3 instead, so a harness can tell 'the\n" +
            "export changed' apart from 'the export did not happen'."),
        Run);

    private static int Run(CommandContext context)
    {
        string left = context.Line.RequirePositional(0, "left");
        string right = context.Line.RequirePositional(1, "right");
        context.Line.RequireNoExtraPositionals(2);

        string mode = context.Line.Get("mode", "auto")!.ToLowerInvariant();
        if (mode == "auto")
            mode = LooksLikeImage(left) && LooksLikeImage(right) ? "image" : "document";

        context.Result["left"] = left;
        context.Result["right"] = right;
        context.Result["mode"] = mode;

        return mode switch
        {
            "image" => CompareImages(context, left, right),
            "document" => CompareDocuments(context, left, right),
            _ => throw new UsageException("--mode expects auto, image, or document, not \"" + mode + "\"."),
        };
    }

    private static int CompareImages(CommandContext context, string left, string right)
    {
        CodecComposition.RegisterImageCodecs();

        using BBitmap leftImage = DecodeImage(left);
        using BBitmap rightImage = DecodeImage(right);

        Thresholds thresholds = Thresholds.From(context.Line);
        DiffStyle? style = context.Line.Has("diff") ? ParseDiffStyle(context.Line) : null;

        ImageComparison comparison = ImageComparison.Compare(
            leftImage, rightImage, thresholds.Tolerance, style);

        try
        {
            context.Report("image comparison");
            foreach (string line in comparison.Describe())
                context.Report(line);

            if (comparison.Diff is not null)
            {
                string path = context.Line.Require("diff");
                DocumentIo.WriteAllBytes(path, DocumentRasterizer.EncodePng(comparison.Diff));
                context.Report("  diff image    " + path);
            }

            bool passes = comparison.Passes(
                thresholds.MaxDifferent,
                thresholds.MaxDifferentRatio,
                !context.Line.Has("allow-size-difference"));

            context.Result["image"] = comparison.ToJson();
            context.Result["equal"] = passes;
            context.Report();
            context.Report(passes ? "verdict: same" : "verdict: DIFFERENT");

            return passes ? ExitCode.Ok : ExitCode.Different;
        }
        finally
        {
            comparison.Diff?.Dispose();
        }
    }

    private static int CompareDocuments(CommandContext context, string left, string right)
    {
        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);

        LoadedDocument leftDocument = DocumentIo.LoadOrThrow(left, catalog, readOptions, context.Line.Get("from"));
        LoadedDocument rightDocument = DocumentIo.LoadOrThrow(right, catalog, readOptions, context.Line.Get("from"));

        var options = new ComparisonOptions
        {
            IgnoreWhitespace = context.Line.Has("ignore-whitespace"),
            IgnoreCase = context.Line.Has("ignore-case"),
            IgnoreInlineStyle = context.Line.Has("ignore-inline-style"),
            IgnoreParagraphStyle = context.Line.Has("ignore-paragraph-style"),
            MaxDifferences = Math.Max(1, context.Line.GetInt32("max-differences", 50)),
        };

        DocumentComparison comparison = DocumentComparison.Compare(
            leftDocument.Document, rightDocument.Document, options);

        context.Report("left   " + left + "  (" + leftDocument.FormatName + ", " + leftDocument.Status + ")");
        context.Report("right  " + right + "  (" + rightDocument.FormatName + ", " + rightDocument.Status + ")");
        context.Report();
        context.Report("structure");
        context.Report("  plain text      " + (comparison.TextEqual ? "same" : "DIFFERENT"));
        context.Report("  format codes    " + (comparison.FormatCodesEqual ? "same" : "DIFFERENT"));

        if (comparison.FirstTextDifference is int offset)
        {
            context.Report("  first text difference at character " +
                offset.ToString(CultureInfo.InvariantCulture));
        }

        string[] counts = comparison.DescribeStatistics().ToArray();
        if (counts.Length > 0)
        {
            context.Report();
            context.Report(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-20} {1,10} {2,10}   {3}",
                "COUNT",
                "LEFT",
                "RIGHT",
                "DELTA"));
            foreach (string line in counts)
                context.Report(line);
        }

        if (comparison.AlignedByIndex)
        {
            context.Warn(
                "the documents were too large to align paragraph by paragraph; " +
                "they were compared by index, so one inserted paragraph will report as many.");
        }

        context.Report();
        if (comparison.Differences.Count == 0)
        {
            context.Report("no structural differences.");
        }
        else
        {
            context.Report(comparison.Differences.Count.ToString(CultureInfo.InvariantCulture) +
                " structural difference(s):");
            foreach (DocumentDifference difference in comparison.Differences)
                context.Report("  " + difference);

            if (comparison.Truncated)
                context.Report("  (stopped at --max-differences; there may be more)");
        }

        context.Result["document"] = comparison.ToJson();
        context.Result["leftFormat"] = leftDocument.FormatName;
        context.Result["rightFormat"] = rightDocument.FormatName;
        context.Result["leftDiagnostics"] = DocumentReport.ToJson(leftDocument.Diagnostics);
        context.Result["rightDiagnostics"] = DocumentReport.ToJson(rightDocument.Diagnostics);

        bool equal = comparison.Equal;

        if (context.Line.Has("render"))
            equal &= CompareRendered(context, leftDocument, rightDocument);

        context.Result["equal"] = equal;
        context.Report();
        context.Report(equal ? "verdict: same" : "verdict: DIFFERENT");

        if (!equal)
            return ExitCode.Different;

        // The two agree, but a codec may still have reported something on the way
        // in. Two documents that are equal because both lost the same construct
        // are a pass a caller may well not want.
        return DocumentReport.ApplyFailOn(
            leftDocument.Diagnostics.Concat(rightDocument.Diagnostics),
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }

    /// <summary>Renders both documents through one pipeline and compares the pages.</summary>
    private static bool CompareRendered(
        CommandContext context,
        LoadedDocument left,
        LoadedDocument right)
    {
        RenderPipeline pipeline = RenderPipeline.Create(context.Line);
        using RenderOutcome leftPages = pipeline.Render(left.Document);
        using RenderOutcome rightPages = pipeline.Render(right.Document);

        if (context.Line.Has("render-out"))
        {
            string pattern = context.Line.Require("render-out");
            pipeline.Write(leftPages, pattern.Replace("{side}", "left", StringComparison.Ordinal));
            pipeline.Write(rightPages, pattern.Replace("{side}", "right", StringComparison.Ordinal));
            foreach (string path in leftPages.WrittenPaths.Concat(rightPages.WrittenPaths))
                context.Report("wrote " + path);
        }

        Thresholds thresholds = Thresholds.From(context.Line);
        DiffStyle? style = context.Line.Has("diff") ? ParseDiffStyle(context.Line) : null;
        bool requireSameSize = !context.Line.Has("allow-size-difference");

        context.Report();
        context.Report("render");
        context.Report("  pages           " + leftPages.Pages.Count + " vs " + rightPages.Pages.Count);

        bool equal = leftPages.Pages.Count == rightPages.Pages.Count;
        int shared = Math.Min(leftPages.Pages.Count, rightPages.Pages.Count);
        var pageResults = new JsonArray();

        for (int i = 0; i < shared; i++)
        {
            ImageComparison comparison = ImageComparison.Compare(
                leftPages.Pages[i], rightPages.Pages[i], thresholds.Tolerance, style);

            try
            {
                bool passes = comparison.Passes(
                    thresholds.MaxDifferent, thresholds.MaxDifferentRatio, requireSameSize);
                equal &= passes;

                context.Report(string.Format(
                    CultureInfo.InvariantCulture,
                    "  page {0,-10} {1} differing pixel(s), max delta {2}{3}",
                    i + 1,
                    comparison.DifferingPixels,
                    comparison.MaxChannelDelta,
                    passes ? string.Empty : "   DIFFERENT"));

                if (!passes || context.Verbose)
                {
                    foreach (string detail in comparison.Describe())
                        context.Detail("  " + detail);
                }

                JsonObject json = comparison.ToJson();
                json["page"] = i + 1;
                json["passes"] = passes;

                if (comparison.Diff is not null)
                {
                    string path = DiffPath(context.Line.Require("diff"), i + 1, shared == 1);
                    DocumentIo.WriteAllBytes(path, DocumentRasterizer.EncodePng(comparison.Diff));
                    context.Report("  diff            " + path);
                    json["diffPath"] = path;
                }

                pageResults.Add(json);
            }
            finally
            {
                comparison.Diff?.Dispose();
            }
        }

        context.Result["render"] = new JsonObject
        {
            ["settings"] = pipeline.Manifest(leftPages),
            ["leftPageCount"] = leftPages.Pages.Count,
            ["rightPageCount"] = rightPages.Pages.Count,
            ["pages"] = pageResults,
        };

        return equal;
    }

    private static string DiffPath(string pattern, int page, bool single)
    {
        if (pattern.Contains("{page}", StringComparison.Ordinal))
        {
            return pattern.Replace(
                "{page}",
                page.ToString("D3", CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        if (single)
            return pattern;

        string directory = Path.GetDirectoryName(pattern) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(pattern) + "-" +
            page.ToString(CultureInfo.InvariantCulture) + Path.GetExtension(pattern);

        return directory.Length == 0 ? name : Path.Combine(directory, name);
    }

    private static BBitmap DecodeImage(string path)
    {
        byte[] bytes = DocumentIo.ReadAllBytes(path, long.MaxValue);
        try
        {
            return BBitmap.Decode(bytes);
        }
        catch (Exception exception) when (
            exception is Broiler.Media.MediaException or InvalidOperationException
                or ArgumentException or NotSupportedException or FormatException)
        {
            throw new DocumentIoException(ExitCode.Input, "Cannot decode " + path + " as an image: " + exception.Message);
        }
    }

    private static bool LooksLikeImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static DiffStyle ParseDiffStyle(CommandLine line)
    {
        string value = line.Get("diff-style", "overlay")!;
        return value.ToLowerInvariant() switch
        {
            "overlay" => DiffStyle.Overlay,
            "mask" => DiffStyle.Mask,
            "heat" => DiffStyle.Heat,
            _ => throw new UsageException("--diff-style expects overlay, mask, or heat, not \"" + value + "\"."),
        };
    }

    /// <summary>The three numbers that turn a measurement into a verdict.</summary>
    private sealed class Thresholds
    {
        public int Tolerance { get; private init; }

        public long MaxDifferent { get; private init; }

        public double MaxDifferentRatio { get; private init; }

        public static Thresholds From(CommandLine line)
        {
            int tolerance = line.GetInt32("tolerance", 0);
            if (tolerance is < 0 or > 255)
                throw new UsageException("--tolerance must be between 0 and 255.");

            double maxDifferent = line.GetDouble("max-different", 0);
            if (maxDifferent < 0)
                throw new UsageException("--max-different cannot be negative.");

            double ratio = line.GetDouble("max-different-ratio", 0);
            if (ratio is < 0 or > 1)
                throw new UsageException("--max-different-ratio must be between 0 and 1.");

            return new Thresholds
            {
                Tolerance = tolerance,
                MaxDifferent = (long)Math.Min(maxDifferent, long.MaxValue),
                MaxDifferentRatio = ratio,
            };
        }
    }
}
