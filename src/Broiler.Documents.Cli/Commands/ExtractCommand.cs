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
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Commands;

/// <summary>
/// Extracts tabular and spreadsheet data from documents into structured formats.
/// </summary>
public static class ExtractCommand
{
    public static CommandEntry Create(string name = "extract") => new(
        new CommandSpec(
            name,
            "Extract tables from a document into structured data (CSV, TSV, JSON, HTML, Markdown).",
            $"{name} <input> [--table <index>] [--range <A1:D10>] [--to csv|tsv|json|html|markdown|text] [--out <path>] [--evaluate] [--formatted] [--headers] [--fill-merged] [--empty <text>] [--delimiter <sep>] [--all] [--json] [--verbose]",
            [
                OptionSpec.Value("table", "index", "0-based index of a specific table to extract (default: 0 or all)."),
                OptionSpec.Flag("all", "Extract all tables in the document."),
                OptionSpec.Value("range", "A1:C10", "Cell coordinate range to extract (e.g. A1:D10, A:C, 1:5)."),
                OptionSpec.Value("to", "format", "Output format: csv, tsv, json, html, markdown, or text (default: csv)."),
                OptionSpec.Value("as", "format", "Synonym for --to."),
                OptionSpec.Value("out", "path", "Write extracted table(s) to a file instead of stdout. Supports {table} or {index} template for multiple tables."),
                OptionSpec.Flag("evaluate", "Recalculate formulas and extract evaluated values."),
                OptionSpec.Flag("formatted", "Format numbers, dates, and currencies using cell number formats."),
                OptionSpec.Flag("headers", "Treat the first row as column headers (emits JSON array of objects)."),
                OptionSpec.Flag("fill-merged", "Repeat merged root cell values across all covered continuation slots."),
                OptionSpec.Value("empty", "text", "Replacement string for empty cells (default: empty string).", ""),
                OptionSpec.Value("delimiter", "sep", "Field delimiter for CSV output (comma, semicolon, tab, pipe, or one character).", "comma"),
                .. DocumentOptions.Specs,
            ],
            [
                "extract report.docx --to csv",
                "extract sheet.csv --table 0 --range B2:D10 --to json --headers",
                "extract budget.xlsx --evaluate --formatted --to markdown",
                "extract data.docx --all --out \"tables/table_{index}.csv\"",
                "extract report.pdf --to html --out tables.html",
            ],
            "Extracts tabular and spreadsheet data from any supported document format.\n" +
            "Supports format conversion, cell range sub-setting, formula recalculation,\n" +
            "merged cell un-merging/filling, and structured record exports."),
        Run);

    private static int Run(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        DocumentCodecCatalog catalog = DocumentOptions.CreateCatalogFrom(context.Line);
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        string? targetFormat = context.Line.Get("to");
        if (string.IsNullOrEmpty(targetFormat))
            targetFormat = context.Line.Get("as");

        if (string.IsNullOrEmpty(targetFormat) && context.Line.Has("out"))
        {
            string outPath = context.Line.Require("out");
            string ext = Path.GetExtension(outPath).ToLowerInvariant();
            targetFormat = ext switch
            {
                ".csv" => "csv",
                ".tsv" => "tsv",
                ".json" => "json",
                ".html" or ".htm" => "html",
                ".md" or ".markdown" => "markdown",
                ".txt" => "text",
                _ => "csv"
            };
        }

        targetFormat = (targetFormat ?? "csv").ToLowerInvariant();
        if (targetFormat is "grid") targetFormat = "text";
        if (targetFormat is "md") targetFormat = "markdown";

        if (targetFormat is not ("csv" or "tsv" or "json" or "html" or "markdown" or "text"))
        {
            throw new UsageException($"Unknown output format '{targetFormat}'. Supported formats: csv, tsv, json, html, markdown, text.");
        }

        int? requestedTable = null;
        if (context.Line.Has("table"))
        {
            if (!int.TryParse(context.Line.Require("table"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tIdx))
                throw new UsageException("The --table option requires an integer index.");
            requestedTable = tIdx;
        }

        bool extractAll = context.Line.Has("all");
        string? range = context.Line.Get("range");
        bool evaluate = context.Line.Has("evaluate");
        bool formatted = context.Line.Has("formatted");
        bool headers = context.Line.Has("headers");
        bool fillMerged = context.Line.Has("fill-merged");
        string emptyValue = context.Line.Get("empty", "")!;
        char delimiter = ParseDelimiter(context.Line.Get("delimiter", "comma"));

        if (loaded.Document.Tables.Count == 0)
        {
            if (requestedTable.HasValue)
                throw new UsageException("Document contains no tables.");

            context.Report(source + ": no tables found.");
            context.Result["source"] = source;
            context.Result["format"] = loaded.FormatName;
            context.Result["targetFormat"] = targetFormat;
            context.Result["tableCount"] = 0;
            context.Result["extractedCount"] = 0;
            context.Result["tables"] = new JsonArray();
            context.Result["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);
            return DocumentReport.ApplyFailOn(loaded.Diagnostics, DocumentOptions.FailOnFrom(context.Line), ExitCode.Ok);
        }

        if (requestedTable.HasValue && (requestedTable.Value < 0 || requestedTable.Value >= loaded.Document.Tables.Count))
        {
            throw new UsageException(
                $"Table index {requestedTable.Value} is out of range. Document contains {loaded.Document.Tables.Count} table(s).");
        }

        var options = new TableExtractOptions
        {
            TableIndex = requestedTable,
            ExtractAll = extractAll,
            Range = range,
            TargetFormat = targetFormat,
            Evaluate = evaluate,
            Formatted = formatted,
            Headers = headers,
            FillMerged = fillMerged,
            EmptyValue = emptyValue,
            Delimiter = delimiter,
            Culture = CultureInfo.InvariantCulture,
        };

        var tableIndices = new List<int>();
        if (requestedTable.HasValue)
        {
            tableIndices.Add(requestedTable.Value);
        }
        else if (extractAll || loaded.Document.Tables.Count == 1)
        {
            for (int i = 0; i < loaded.Document.Tables.Count; i++)
                tableIndices.Add(i);
        }
        else
        {
            string? outPath = context.Line.Get("out");
            bool hasTemplate = outPath is not null && (outPath.Contains("{table}") || outPath.Contains("{index}") || outPath.Contains("{n}"));
            if (hasTemplate)
            {
                for (int i = 0; i < loaded.Document.Tables.Count; i++)
                    tableIndices.Add(i);
            }
            else
            {
                // Default to all tables
                for (int i = 0; i < loaded.Document.Tables.Count; i++)
                    tableIndices.Add(i);
            }
        }

        var extractedTables = TableExtractor.ExtractAll(loaded.Document, options, tableIndices);
        string payload = TableExtractor.RenderCombined(extractedTables, targetFormat, options, loaded.Document);

        if (context.Line.Has("out"))
        {
            string outTemplate = context.Line.Require("out");
            if (outTemplate == DocumentIo.StandardStreamToken)
            {
                context.WriteOutLine(payload);
            }
            else if (outTemplate.Contains("{table}") || outTemplate.Contains("{index}") || outTemplate.Contains("{n}"))
            {
                foreach (var table in extractedTables)
                {
                    string tPath = outTemplate
                        .Replace("{table}", table.TableIndex.ToString(CultureInfo.InvariantCulture))
                        .Replace("{index}", table.TableIndex.ToString(CultureInfo.InvariantCulture))
                        .Replace("{n}", table.TableIndex.ToString(CultureInfo.InvariantCulture));

                    DocumentTable? dt = table.TableIndex < loaded.Document.Tables.Count ? loaded.Document.Tables[table.TableIndex] : null;
                    string tContent = table.Render(targetFormat, options, dt, loaded.Document);
                    DocumentIo.WriteAllBytes(tPath, Encoding.UTF8.GetBytes(tContent));
                    context.Report($"wrote {tPath} ({targetFormat})");
                }
            }
            else
            {
                DocumentIo.WriteAllBytes(outTemplate, Encoding.UTF8.GetBytes(payload));
                context.Report($"wrote {outTemplate} ({targetFormat})");
            }
        }
        else
        {
            context.WriteOutLine(payload);
        }

        var tablesArray = new JsonArray();
        foreach (var t in extractedTables)
            tablesArray.Add(t.ToJson());

        context.Result["source"] = source;
        context.Result["format"] = loaded.FormatName;
        context.Result["targetFormat"] = targetFormat;
        context.Result["tableCount"] = loaded.Document.Tables.Count;
        context.Result["extractedCount"] = extractedTables.Count;
        context.Result["tables"] = tablesArray;
        context.Result["content"] = payload;
        context.Result["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        return DocumentReport.ApplyFailOn(
            loaded.Diagnostics,
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }

    private static char ParseDelimiter(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return ',';

        return value.ToLowerInvariant() switch
        {
            "comma" => ',',
            "semicolon" => ';',
            "tab" => '\t',
            "pipe" => '|',
            _ when value.Length == 1 && value[0] is not ('"' or '\r' or '\n') => value[0],
            _ => throw new UsageException("--delimiter expects one character other than a quote or line break, or comma, semicolon, tab, pipe.")
        };
    }
}
