using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// Configuration options for table extraction.
/// </summary>
public sealed class TableExtractOptions
{
    public int? TableIndex { get; set; }
    public bool ExtractAll { get; set; }
    public string? Range { get; set; }
    public string TargetFormat { get; set; } = "csv";
    public bool Evaluate { get; set; }
    public bool Formatted { get; set; }
    public bool Headers { get; set; }
    public bool FillMerged { get; set; }
    public string EmptyValue { get; set; } = string.Empty;
    public char Delimiter { get; set; } = ',';
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
}

/// <summary>
/// An extracted table representation containing headers, 2D rows, and key-value records.
/// </summary>
public sealed class ExtractedTable
{
    public int TableIndex { get; init; }
    public string Bounds { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public int ColumnCount { get; init; }
    public IReadOnlyList<string> Headers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Records { get; init; } = [];

    public JsonObject ToJson()
    {
        var headersArray = new JsonArray();
        foreach (string h in Headers)
            headersArray.Add(h);

        var rowsArray = new JsonArray();
        foreach (var row in Rows)
        {
            var rowArray = new JsonArray();
            foreach (string val in row)
                rowArray.Add(val);
            rowsArray.Add(rowArray);
        }

        var recordsArray = new JsonArray();
        foreach (var record in Records)
        {
            var recObj = new JsonObject();
            foreach (var kvp in record)
                recObj[kvp.Key] = kvp.Value;
            recordsArray.Add(recObj);
        }

        return new JsonObject
        {
            ["index"] = TableIndex,
            ["bounds"] = Bounds,
            ["rowCount"] = RowCount,
            ["columnCount"] = ColumnCount,
            ["headers"] = headersArray,
            ["rows"] = rowsArray,
            ["records"] = recordsArray,
        };
    }

    /// <summary>
    /// Renders the extracted table into the specified output format.
    /// </summary>
    public string Render(string format, TableExtractOptions options, DocumentTable? sourceTable = null, RichTextDocument? sourceDoc = null)
    {
        return format.ToLowerInvariant() switch
        {
            "csv" => RenderCsv(options.Delimiter),
            "tsv" => RenderTsv(),
            "json" => RenderJson(options.Headers),
            "markdown" or "md" => RenderMarkdown(options.Headers),
            "html" or "htm" => RenderHtml(options.Headers),
            "text" or "grid" => sourceTable is not null
                ? TableIntrospection.RenderGrid(sourceTable, sourceDoc, options.Evaluate, options.Culture)
                : RenderTextGrid(),
            _ => RenderCsv(options.Delimiter)
        };
    }

    public string RenderCsv(char delimiter = ',')
    {
        var sb = new StringBuilder();
        foreach (var row in Rows)
        {
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0) sb.Append(delimiter);
                sb.Append(EscapeCsvField(row[c], delimiter));
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public string RenderTsv()
    {
        var sb = new StringBuilder();
        foreach (var row in Rows)
        {
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0) sb.Append('\t');
                string text = row[c].Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                sb.Append(text);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public string RenderJson(bool asRecords = false)
    {
        if (asRecords && Records.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var rec in Records)
            {
                var obj = new JsonObject();
                foreach (var kvp in rec)
                    obj[kvp.Key] = kvp.Value;
                arr.Add(obj);
            }
            return arr.ToJsonString(JsonOutput.Indented);
        }

        var rowsArr = new JsonArray();
        foreach (var row in Rows)
        {
            var rowArr = new JsonArray();
            foreach (string val in row)
                rowArr.Add(val);
            rowsArr.Add(rowArr);
        }
        return rowsArr.ToJsonString(JsonOutput.Indented);
    }

    public string RenderMarkdown(bool useHeaders = false)
    {
        if (Rows.Count == 0)
            return "(empty table)\n";

        var sb = new StringBuilder();
        IReadOnlyList<string> headerRow;
        IEnumerable<IReadOnlyList<string>> dataRows;

        if (useHeaders || Headers.Count == ColumnCount)
        {
            headerRow = Headers;
            dataRows = Rows.Skip(1);
        }
        else
        {
            headerRow = Enumerable.Range(0, ColumnCount).Select(CellAddress.ColumnToLetters).ToList();
            dataRows = Rows;
        }

        // Header
        sb.Append("| ").Append(string.Join(" | ", headerRow.Select(EscapeMarkdown))).AppendLine(" |");

        // Separator
        sb.Append("| ").Append(string.Join(" | ", headerRow.Select(_ => "---"))).AppendLine(" |");

        // Rows
        foreach (var row in dataRows)
        {
            sb.Append("| ");
            for (int c = 0; c < headerRow.Count; c++)
            {
                if (c > 0) sb.Append(" | ");
                string val = c < row.Count ? row[c] : string.Empty;
                sb.Append(EscapeMarkdown(val));
            }
            sb.AppendLine(" |");
        }

        return sb.ToString();
    }

    public string RenderHtml(bool useHeaders = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<table>");

        IReadOnlyList<string> headerRow = Headers;
        IEnumerable<IReadOnlyList<string>> dataRows = Rows;

        if ((useHeaders || Headers.Count == ColumnCount) && Rows.Count > 0)
        {
            sb.AppendLine("  <thead>");
            sb.AppendLine("    <tr>");
            foreach (string h in headerRow)
            {
                sb.Append("      <th>").Append(WebUtility.HtmlEncode(h)).AppendLine("</th>");
            }
            sb.AppendLine("    </tr>");
            sb.AppendLine("  </thead>");
            dataRows = Rows.Skip(1);
        }

        sb.AppendLine("  <tbody>");
        foreach (var row in dataRows)
        {
            sb.AppendLine("    <tr>");
            foreach (string val in row)
            {
                sb.Append("      <td>").Append(WebUtility.HtmlEncode(val)).AppendLine("</td>");
            }
            sb.AppendLine("    </tr>");
        }
        sb.AppendLine("  </tbody>");
        sb.AppendLine("</table>");

        return sb.ToString();
    }

    private string RenderTextGrid()
    {
        if (Rows.Count == 0 || ColumnCount == 0)
            return "(empty table)\n";

        int[] colWidths = new int[ColumnCount];
        for (int c = 0; c < ColumnCount; c++)
        {
            string colLetter = CellAddress.ColumnToLetters(c);
            colWidths[c] = Math.Max(3, colLetter.Length);
        }

        foreach (var row in Rows)
        {
            for (int c = 0; c < Math.Min(ColumnCount, row.Count); c++)
            {
                int len = row[c].Length;
                if (len > 40) len = 40;
                if (len > colWidths[c])
                    colWidths[c] = len;
            }
        }

        int rowHeaderWidth = Math.Max(2, Rows.Count.ToString(CultureInfo.InvariantCulture).Length);
        var sb = new StringBuilder();

        sb.Append("┌").Append(new string('─', rowHeaderWidth + 2));
        for (int c = 0; c < ColumnCount; c++)
            sb.Append('┬').Append(new string('─', colWidths[c] + 2));
        sb.AppendLine("┐");

        sb.Append("│ ").Append(new string(' ', rowHeaderWidth)).Append(" ");
        for (int c = 0; c < ColumnCount; c++)
        {
            string colLetter = CellAddress.ColumnToLetters(c);
            sb.Append("│ ").Append(colLetter.PadRight(colWidths[c])).Append(' ');
        }
        sb.AppendLine("│");

        sb.Append("├").Append(new string('─', rowHeaderWidth + 2));
        for (int c = 0; c < ColumnCount; c++)
            sb.Append('┼').Append(new string('─', colWidths[c] + 2));
        sb.AppendLine("┤");

        for (int r = 0; r < Rows.Count; r++)
        {
            string rowNum = (r + 1).ToString(CultureInfo.InvariantCulture).PadLeft(rowHeaderWidth);
            sb.Append("│ ").Append(rowNum).Append(' ');

            var row = Rows[r];
            for (int c = 0; c < ColumnCount; c++)
            {
                string text = c < row.Count ? row[c] : "";
                if (text.Length > 40)
                    text = text[..37] + "...";
                sb.Append("│ ").Append(text.PadRight(colWidths[c])).Append(' ');
            }
            sb.AppendLine("│");
        }

        sb.Append("└").Append(new string('─', rowHeaderWidth + 2));
        for (int c = 0; c < ColumnCount; c++)
            sb.Append('┴').Append(new string('─', colWidths[c] + 2));
        sb.AppendLine("┘");

        return sb.ToString();
    }

    private static string EscapeCsvField(string? value, char delimiter)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool needsQuotes = value.IndexOf(delimiter) >= 0 ||
                           value.IndexOf('"') >= 0 ||
                           value.IndexOf('\r') >= 0 ||
                           value.IndexOf('\n') >= 0;

        if (!needsQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string EscapeMarkdown(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("|", "\\|")
            .Replace("\r\n", "<br>")
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}

/// <summary>
/// Engine for extracting tables from documents.
/// </summary>
public static class TableExtractor
{
    /// <summary>
    /// Extracts a single table according to options.
    /// </summary>
    public static ExtractedTable Extract(
        DocumentTable table,
        int tableIndex,
        RichTextDocument? document,
        TableExtractOptions options)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(options);

        TableGrid grid = TableGrid.FromTable(table);
        int totalRows = grid.RowCount;
        int totalCols = grid.ColumnCount;

        if (totalRows == 0 || totalCols == 0)
        {
            return new ExtractedTable
            {
                TableIndex = tableIndex,
                Bounds = "empty",
                RowCount = 0,
                ColumnCount = 0,
                Headers = [],
                Rows = [],
                Records = [],
            };
        }

        // Determine extraction bounds
        int minRow = 0;
        int maxRow = totalRows - 1;
        int minCol = 0;
        int maxCol = totalCols - 1;

        if (!string.IsNullOrWhiteSpace(options.Range))
        {
            (minRow, maxRow, minCol, maxCol) = ParseRange(options.Range, totalRows, totalCols);
        }

        int extractedRowCount = maxRow - minRow + 1;
        int extractedColCount = maxCol - minCol + 1;

        string bounds = $"{new CellAddress(minRow, minCol).Format()}:{new CellAddress(maxRow, maxCol).Format()}";

        TableEvaluationResult? eval = options.Evaluate
            ? TableFormulaEngine.Evaluate(table, document)
            : null;

        var rows = new List<IReadOnlyList<string>>(extractedRowCount);

        for (int r = minRow; r <= maxRow; r++)
        {
            var rowValues = new List<string>(extractedColCount);
            for (int c = minCol; c <= maxCol; c++)
            {
                string val = GetCellValue(grid, r, c, table, document, eval, options);
                rowValues.Add(val);
            }
            rows.Add(rowValues);
        }

        // Headers and Records
        var headers = new List<string>(extractedColCount);
        bool hasHeaders = options.Headers || (table.Rows.Count > minRow && table.Rows[minRow].IsHeader);

        if (hasHeaders && rows.Count > 0)
        {
            var firstRow = rows[0];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int c = 0; c < extractedColCount; c++)
            {
                string rawH = c < firstRow.Count ? firstRow[c].Trim() : "";
                if (string.IsNullOrEmpty(rawH))
                    rawH = $"Column_{c + 1}";

                string uniqueH = rawH;
                int suffix = 2;
                while (!seen.Add(uniqueH))
                {
                    uniqueH = $"{rawH}_{suffix++}";
                }
                headers.Add(uniqueH);
            }
        }
        else
        {
            for (int c = 0; c < extractedColCount; c++)
            {
                headers.Add(CellAddress.ColumnToLetters(minCol + c));
            }
        }

        var records = new List<IReadOnlyDictionary<string, string>>();
        int dataStartRow = hasHeaders ? 1 : 0;

        for (int r = dataStartRow; r < rows.Count; r++)
        {
            var row = rows[r];
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headers.Count; c++)
            {
                string val = c < row.Count ? row[c] : options.EmptyValue;
                dict[headers[c]] = val;
            }
            records.Add(dict);
        }

        return new ExtractedTable
        {
            TableIndex = tableIndex,
            Bounds = bounds,
            RowCount = extractedRowCount,
            ColumnCount = extractedColCount,
            Headers = headers,
            Rows = rows,
            Records = records,
        };
    }

    /// <summary>
    /// Extracts all specified tables from a document.
    /// </summary>
    public static IReadOnlyList<ExtractedTable> ExtractAll(
        RichTextDocument document,
        TableExtractOptions options,
        IReadOnlyList<int>? tableIndices = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var indices = tableIndices ?? Enumerable.Range(0, document.Tables.Count).ToList();
        var results = new List<ExtractedTable>(indices.Count);

        foreach (int idx in indices)
        {
            if (idx >= 0 && idx < document.Tables.Count)
            {
                results.Add(Extract(document.Tables[idx], idx, document, options));
            }
        }

        return results;
    }

    /// <summary>
    /// Combines multiple extracted tables into a single formatted text payload.
    /// </summary>
    public static string RenderCombined(
        IReadOnlyList<ExtractedTable> tables,
        string format,
        TableExtractOptions options,
        RichTextDocument? document = null)
    {
        if (tables.Count == 0)
            return string.Empty;

        if (tables.Count == 1)
        {
            DocumentTable? dt = document is not null && tables[0].TableIndex < document.Tables.Count
                ? document.Tables[tables[0].TableIndex]
                : null;
            return tables[0].Render(format, options, dt, document);
        }

        string fmt = format.ToLowerInvariant();
        if (fmt == "json")
        {
            var arr = new JsonArray();
            foreach (var t in tables)
                arr.Add(t.ToJson());
            return arr.ToJsonString(JsonOutput.Indented);
        }

        var sb = new StringBuilder();
        for (int i = 0; i < tables.Count; i++)
        {
            ExtractedTable t = tables[i];
            DocumentTable? dt = document is not null && t.TableIndex < document.Tables.Count
                ? document.Tables[t.TableIndex]
                : null;

            if (fmt is "markdown" or "md")
            {
                if (i > 0) sb.AppendLine();
                sb.AppendLine($"### Table {t.TableIndex} ({t.Bounds})");
                sb.AppendLine();
                sb.Append(t.Render(fmt, options, dt, document));
            }
            else if (fmt is "html" or "htm")
            {
                if (i > 0) sb.AppendLine();
                sb.Append(t.Render(fmt, options, dt, document));
            }
            else if (fmt is "text" or "grid")
            {
                if (i > 0) sb.AppendLine();
                sb.AppendLine($"Table {t.TableIndex} ({t.RowCount}x{t.ColumnCount}):");
                sb.Append(t.Render(fmt, options, dt, document));
            }
            else // csv or tsv
            {
                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"# Table {t.TableIndex} ({t.Bounds})");
                }
                sb.Append(t.Render(fmt, options, dt, document));
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static (int minRow, int maxRow, int minCol, int maxCol) ParseRange(string rangeStr, int totalRows, int totalCols)
    {
        string trimmed = rangeStr.Trim();

        if (CellRangeAddress.TryParse(trimmed, out var addressRange))
        {
            int minR = addressRange.IsWholeColumn ? 0 : Math.Clamp(addressRange.StartRow, 0, totalRows - 1);
            int maxR = addressRange.IsWholeColumn ? totalRows - 1 : Math.Clamp(addressRange.EndRow, 0, totalRows - 1);
            int minC = addressRange.IsWholeRow ? 0 : Math.Clamp(addressRange.StartCol, 0, totalCols - 1);
            int maxC = addressRange.IsWholeRow ? totalCols - 1 : Math.Clamp(addressRange.EndCol, 0, totalCols - 1);

            if (minR > maxR || minC > maxC)
                throw new UsageException($"Range '{rangeStr}' is outside table bounds (0..{totalRows - 1}, 0..{totalCols - 1}).");

            return (minR, maxR, minC, maxC);
        }

        if (CellAddress.TryParse(trimmed, out var single))
        {
            int r = Math.Clamp(single.Row, 0, totalRows - 1);
            int c = Math.Clamp(single.Column, 0, totalCols - 1);
            return (r, r, c, c);
        }

        throw new UsageException($"Invalid range expression '{rangeStr}'. Expected format like 'A1:D10', 'A:C', or '1:5'.");
    }

    private static string GetCellValue(
        TableGrid grid,
        int r,
        int c,
        DocumentTable table,
        RichTextDocument? document,
        TableEvaluationResult? eval,
        TableExtractOptions options)
    {
        TableGridSlot slot = grid.GetSlot(r, c);

        if (slot.IsEmpty)
            return options.EmptyValue;

        if (slot.IsContinuation)
        {
            if (options.FillMerged)
            {
                return GetCellValue(grid, slot.RootRow, slot.RootColumn, table, document, eval, options);
            }
            return options.EmptyValue;
        }

        TableCell rootCell = slot.RootCell!;
        var cellAddress = new CellAddress(r, c);

        if (options.Evaluate && eval is not null)
        {
            if (options.Formatted)
            {
                return eval.GetFormattedValue(cellAddress, options.Culture).Text;
            }

            if (eval.TryGetError(cellAddress, out FormulaError err))
                return err.ToErrorString();

            CellValue cv = eval.GetValue(cellAddress);
            return cv.Type switch
            {
                CellValueType.Empty => options.EmptyValue,
                CellValueType.Number => cv.Number.ToString("G", options.Culture),
                CellValueType.Boolean => cv.Boolean ? "TRUE" : "FALSE",
                CellValueType.Error => cv.Error.ToErrorString(),
                CellValueType.Text => cv.Text ?? string.Empty,
                _ => string.Empty
            };
        }

        // Not evaluating
        if (!string.IsNullOrWhiteSpace(rootCell.Formula))
            return rootCell.Formula;

        string text = TableIntrospection.GetCellText(rootCell, document);
        if (string.IsNullOrEmpty(text))
            return options.EmptyValue;

        if (text.Trim().StartsWith('='))
            return text.Trim();

        if (options.Formatted && !string.IsNullOrEmpty(rootCell.NumberFormat))
        {
            var formatted = CellNumberFormatter.Format(text, rootCell.NumberFormat, options.Culture);
            return formatted.Text;
        }

        return text;
    }
}
