using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Commands;

/// <summary>
/// Introspects tabular and spreadsheet structures in documents.
/// </summary>
public static class TablesCommand
{
    public static CommandEntry Create() => new(
        new CommandSpec(
            "tables",
            "Inspect tabular and spreadsheet structures in a document.",
            "tables <input> [--table <index>] [--cells] [--formulas] [--evaluate] [--json] [--verbose]",
            [
                OptionSpec.Value("table", "index", "0-based index of a specific table/sheet to inspect."),
                OptionSpec.Flag("cells", "Include cell grid and per-cell details."),
                OptionSpec.Flag("formulas", "Include formula expressions, dependencies, and evaluation status."),
                OptionSpec.Flag("evaluate", "Recalculate formulas and format cell values."),
                .. DocumentOptions.Specs,
            ],
            [
                "tables sheet.csv",
                "tables report.docx --cells",
                "tables budget.xlsx --formulas --evaluate",
                "tables document.odt --table 0 --json",
            ],
            "Inspects grid dimensions, row and column counts, cell bounds, merged cell spans,\n" +
            "cell data types, formulas with dependency DAGs, and tabular layouts."),
        Run);

    private static int Run(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        DocumentCodecCatalog catalog = DocumentOptions.CreateCatalogFrom(context.Line);
        DocumentReadOptions options = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, options, context.Line.Get("from"));

        bool includeCells = context.Line.Has("cells");
        bool includeFormulas = context.Line.Has("formulas");
        bool evaluate = context.Line.Has("evaluate");

        int? requestedIndex = null;
        if (context.Line.Has("table"))
        {
            if (!int.TryParse(context.Line.Require("table"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                throw new UsageException("The --table option requires an integer index.");
            requestedIndex = idx;
        }

        if (loaded.Document.Tables.Count == 0)
        {
            if (requestedIndex.HasValue)
                throw new UsageException("Document contains no tables.");

            context.Report(source + ": no tables found.");
            context.Result["source"] = source;
            context.Result["format"] = loaded.FormatName;
            context.Result["tableCount"] = 0;
            context.Result["tables"] = new JsonArray();
            context.Result["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);
            return DocumentReport.ApplyFailOn(loaded.Diagnostics, DocumentOptions.FailOnFrom(context.Line), ExitCode.Ok);
        }

        if (requestedIndex.HasValue && (requestedIndex.Value < 0 || requestedIndex.Value >= loaded.Document.Tables.Count))
        {
            throw new UsageException(
                $"Table index {requestedIndex.Value} is out of range. Document contains {loaded.Document.Tables.Count} table(s).");
        }

        var reports = new List<TableIntrospectionReport>();
        if (requestedIndex.HasValue)
        {
            int idx = requestedIndex.Value;
            reports.Add(TableIntrospection.Inspect(
                loaded.Document.Tables[idx],
                idx,
                loaded.Document,
                includeCells,
                evaluate,
                CultureInfo.InvariantCulture));
        }
        else
        {
            for (int i = 0; i < loaded.Document.Tables.Count; i++)
            {
                reports.Add(TableIntrospection.Inspect(
                    loaded.Document.Tables[i],
                    i,
                    loaded.Document,
                    includeCells,
                    evaluate,
                    CultureInfo.InvariantCulture));
            }
        }

        context.Report(source + (loaded.Document.Tables.Count == 1 ? " (1 table):" : $" ({loaded.Document.Tables.Count} tables):"));

        for (int i = 0; i < reports.Count; i++)
        {
            if (i > 0)
                context.Report();

            TableIntrospectionReport report = reports[i];
            context.Report($"Table {report.TableIndex}: {report.RowCount} rows x {report.ColumnCount} columns ({report.Bounds})");
            context.Report($"  paragraphs        {report.ParagraphIndex}..{report.ParagraphEnd} ({report.ParagraphCount} paragraphs)");
            context.Report($"  slots             {report.TotalSlots} ({report.RootCells} root, {report.ContinuationSlots} continuation, {report.EmptySlots} empty)");

            if (report.MergedRegions.Count > 0)
                context.Report($"  merged regions    {report.MergedRegions.Count} region(s): " + string.Join(", ", report.MergedRegions.Select(m => m.Description)));

            if (report.HeaderRows.Count > 0)
                context.Report($"  headers           row(s) " + string.Join(", ", report.HeaderRows));

            if (report.NestedTableCount > 0)
                context.Report($"  nested tables     {report.NestedTableCount} nested table(s)");

            context.Report($"  layout            alignment: {report.Alignment}, total width: {report.TotalWidth.ToString("0.#", CultureInfo.InvariantCulture)} pt, padding: {report.CellPadding.ToString("0.#", CultureInfo.InvariantCulture)} pt" +
                (report.AutoFit ? ", auto-fit" : ""));

            if (report.ColumnWidths.Count > 0)
                context.Report($"  columns           [" + string.Join(", ", report.ColumnWidths.Select(w => w.ToString("0.#", CultureInfo.InvariantCulture))) + "] pt");

            if (report.Position is not null)
                context.Report($"  position          {report.Position}");

            context.Report($"  data types        {report.DataTypeCounts.FormatSummary()}");

            if (report.Formulas.Count > 0 || includeFormulas)
            {
                context.Report($"  formulas          {report.Formulas.Count} formula(s)");
                foreach (var f in report.Formulas)
                {
                    string deps = f.Dependencies.Count > 0 ? " -> [" + string.Join(", ", f.Dependencies) + "]" : "";
                    string err = f.HasError ? $" [error: {f.Error}]" : "";
                    string evalStr = evaluate && f.FormattedValue is not null ? $" => {f.EvaluatedValue} (\"{f.FormattedValue}\")" : "";
                    context.Report($"    {f.Coordinate}: {f.Expression}{evalStr}{err}{deps}");
                }
            }

            if (includeCells)
            {
                context.Report("  grid:");
                string gridText = TableIntrospection.RenderGrid(loaded.Document.Tables[report.TableIndex], loaded.Document, evaluate, CultureInfo.InvariantCulture);
                foreach (string line in gridText.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        context.Report("    " + line);
                }

                if (context.Verbose)
                {
                    context.Detail("  cell details:");
                    foreach (var c in report.Cells)
                    {
                        string f = c.Formula is not null ? $" formula: {c.Formula}" : "";
                        string v = evaluate && c.FormattedValue is not null ? $" => {c.FormattedValue}" : "";
                        context.Detail($"    {c.Coordinate}: \"{c.Text}\" ({c.DataType}){f}{v}");
                    }
                }
            }
        }

        context.Report();
        DocumentReport.Print(context, loaded.Diagnostics, "diagnostics");

        var tablesArray = new JsonArray();
        foreach (var report in reports)
            tablesArray.Add(report.ToJson(includeCells));

        context.Result["source"] = source;
        context.Result["format"] = loaded.FormatName;
        context.Result["tableCount"] = loaded.Document.Tables.Count;
        context.Result["tables"] = tablesArray;
        context.Result["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        return DocumentReport.ApplyFailOn(
            loaded.Diagnostics,
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }
}
