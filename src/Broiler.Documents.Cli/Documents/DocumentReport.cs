using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// Turns codec diagnostics and document shape into the two forms this tool
/// reports in: lines for a person, and JSON for a harness.
/// </summary>
/// <remarks>
/// Diagnostics are the part of a read that matters most to the job this tool
/// exists for. A codec that meets a construct it does not implement returns a
/// usable document <em>and</em> says what it dropped, so the gap is already named
/// in the result - long before anything has to be inferred from a pixel diff.
/// Anything that reads a document therefore reports them, and <c>--fail-on</c>
/// can promote them to a non-zero exit.
/// </remarks>
public static class DocumentReport
{
    /// <summary>Diagnostics as a JSON array, in the order the codec produced them.</summary>
    public static JsonArray ToJson(IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var array = new JsonArray();
        foreach (DocumentDiagnostic diagnostic in diagnostics)
        {
            var entry = new JsonObject
            {
                ["severity"] = diagnostic.Severity.ToString().ToLowerInvariant(),
                ["code"] = diagnostic.Code,
                ["message"] = diagnostic.Message,
            };

            if (diagnostic.Location is DocumentDiagnosticLocation location)
            {
                var where = new JsonObject();
                if (location.ByteOffset.HasValue)
                    where["byteOffset"] = location.ByteOffset.Value;
                if (location.ParagraphIndex.HasValue)
                    where["paragraphIndex"] = location.ParagraphIndex.Value;
                if (location.PageNumber.HasValue)
                    where["pageNumber"] = location.PageNumber.Value;
                if (!string.IsNullOrEmpty(location.Part))
                    where["part"] = location.Part;
                entry["location"] = where;
            }

            array.Add(entry);
        }

        return array;
    }

    /// <summary>Prints a diagnostic summary, and each diagnostic when <c>--verbose</c> is on.</summary>
    public static void Print(CommandContext context, IReadOnlyList<DocumentDiagnostic> diagnostics, string what)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Count == 0)
        {
            context.Report(what + ": no diagnostics.");
            return;
        }

        int errors = diagnostics.Count(d => d.Severity == DocumentDiagnosticSeverity.Error);
        int warnings = diagnostics.Count(d => d.Severity == DocumentDiagnosticSeverity.Warning);
        int infos = diagnostics.Count - errors - warnings;

        context.Report(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1} diagnostic(s) - {2} error, {3} warning, {4} info.",
            what,
            diagnostics.Count,
            errors,
            warnings,
            infos));

        // Errors always print. They are the ones that changed the outcome, and a
        // run that hides them behind --verbose reports a success it did not have.
        foreach (DocumentDiagnostic diagnostic in diagnostics)
        {
            string text = "  " + Describe(diagnostic);
            if (diagnostic.Severity == DocumentDiagnosticSeverity.Error)
                context.Report(text);
            else
                context.Detail(text);
        }

        if (!context.Verbose && diagnostics.Count > errors)
            context.Detail("  (pass --verbose to list every diagnostic)");
    }

    /// <summary>One diagnostic as a line, with its location when it has one.</summary>
    public static string Describe(DocumentDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        string line = diagnostic.Severity.ToString().ToLowerInvariant() + " " +
            diagnostic.Code + ": " + diagnostic.Message;
        return diagnostic.Location is null ? line : line + " [" + diagnostic.Location + "]";
    }

    /// <summary>The worst severity present, or null for an empty set.</summary>
    public static DocumentDiagnosticSeverity? WorstSeverity(IEnumerable<DocumentDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        DocumentDiagnosticSeverity? worst = null;
        foreach (DocumentDiagnostic diagnostic in diagnostics)
        {
            if (worst is null || diagnostic.Severity > worst)
                worst = diagnostic.Severity;
        }

        return worst;
    }

    /// <summary>
    /// <see cref="ExitCode.Diagnostics"/> when the diagnostics reach the
    /// <c>--fail-on</c> threshold, otherwise <paramref name="otherwise"/>.
    /// </summary>
    public static int ApplyFailOn(
        IEnumerable<DocumentDiagnostic> diagnostics,
        DocumentDiagnosticSeverity? threshold,
        int otherwise)
    {
        if (threshold is null)
            return otherwise;

        DocumentDiagnosticSeverity? worst = WorstSeverity(diagnostics);
        return worst is not null && worst >= threshold ? ExitCode.Diagnostics : otherwise;
    }

    /// <summary>A count of everything in a document that a comparison might care about.</summary>
    public static DocumentStatistics Measure(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var statistics = new DocumentStatistics { Paragraphs = document.ParagraphCount };
        var families = new SortedSet<string>(StringComparer.Ordinal);

        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            statistics.Characters += paragraph.Length;
            statistics.Runs += paragraph.Runs.Count;

            if (paragraph.Length == 0)
                statistics.EmptyParagraphs++;
            if (paragraph.Style.ListKind != ListKind.None)
                statistics.ListParagraphs++;
            if (paragraph.Style.Alignment != TextAlignment.Left)
                statistics.AlignedParagraphs++;
            if (paragraph.Style.IndentLevel > 0)
                statistics.IndentedParagraphs++;

            foreach (StyleRun run in paragraph.Runs)
            {
                InlineStyle style = run.Style;
                if (style.Bold)
                    statistics.BoldRuns++;
                if (style.Italic)
                    statistics.ItalicRuns++;
                if (style.Underline)
                    statistics.UnderlinedRuns++;
                if (style.Strikethrough)
                    statistics.StruckRuns++;
                if (style.IsLink)
                    statistics.LinkRuns++;
                if (style.IsImage)
                    statistics.Images++;
                if (!style.Foreground.IsEmpty)
                    statistics.ColoredRuns++;
                if (!style.Background.IsEmpty)
                    statistics.HighlightedRuns++;
                if (!string.IsNullOrEmpty(style.FontFamily))
                    families.Add(style.FontFamily);
            }
        }

        statistics.FontFamilies = families.ToArray();
        return statistics;
    }
}

/// <summary>What a document contains, counted. Comparable between two documents.</summary>
public sealed class DocumentStatistics
{
    public int Paragraphs { get; set; }

    public int EmptyParagraphs { get; set; }

    public int Characters { get; set; }

    public int Runs { get; set; }

    public int BoldRuns { get; set; }

    public int ItalicRuns { get; set; }

    public int UnderlinedRuns { get; set; }

    public int StruckRuns { get; set; }

    public int ColoredRuns { get; set; }

    public int HighlightedRuns { get; set; }

    public int LinkRuns { get; set; }

    public int Images { get; set; }

    public int ListParagraphs { get; set; }

    public int AlignedParagraphs { get; set; }

    public int IndentedParagraphs { get; set; }

    public IReadOnlyList<string> FontFamilies { get; set; } = Array.Empty<string>();

    public JsonObject ToJson()
    {
        var families = new JsonArray();
        foreach (string family in FontFamilies)
            families.Add(family);

        return new JsonObject
        {
            ["paragraphs"] = Paragraphs,
            ["emptyParagraphs"] = EmptyParagraphs,
            ["characters"] = Characters,
            ["runs"] = Runs,
            ["boldRuns"] = BoldRuns,
            ["italicRuns"] = ItalicRuns,
            ["underlinedRuns"] = UnderlinedRuns,
            ["struckRuns"] = StruckRuns,
            ["coloredRuns"] = ColoredRuns,
            ["highlightedRuns"] = HighlightedRuns,
            ["linkRuns"] = LinkRuns,
            ["images"] = Images,
            ["listParagraphs"] = ListParagraphs,
            ["alignedParagraphs"] = AlignedParagraphs,
            ["indentedParagraphs"] = IndentedParagraphs,
            ["fontFamilies"] = families,
        };
    }

    /// <summary>The counts as name/value pairs, for a side-by-side table.</summary>
    public IEnumerable<KeyValuePair<string, long>> Counts()
    {
        yield return new KeyValuePair<string, long>("paragraphs", Paragraphs);
        yield return new KeyValuePair<string, long>("emptyParagraphs", EmptyParagraphs);
        yield return new KeyValuePair<string, long>("characters", Characters);
        yield return new KeyValuePair<string, long>("runs", Runs);
        yield return new KeyValuePair<string, long>("boldRuns", BoldRuns);
        yield return new KeyValuePair<string, long>("italicRuns", ItalicRuns);
        yield return new KeyValuePair<string, long>("underlinedRuns", UnderlinedRuns);
        yield return new KeyValuePair<string, long>("struckRuns", StruckRuns);
        yield return new KeyValuePair<string, long>("coloredRuns", ColoredRuns);
        yield return new KeyValuePair<string, long>("highlightedRuns", HighlightedRuns);
        yield return new KeyValuePair<string, long>("linkRuns", LinkRuns);
        yield return new KeyValuePair<string, long>("images", Images);
        yield return new KeyValuePair<string, long>("listParagraphs", ListParagraphs);
        yield return new KeyValuePair<string, long>("alignedParagraphs", AlignedParagraphs);
        yield return new KeyValuePair<string, long>("indentedParagraphs", IndentedParagraphs);
    }
}
