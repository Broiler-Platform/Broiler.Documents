using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Broiler.Documents.Model;

namespace Broiler.Documents.Csv;

/// <summary>
/// Reads CSV into the model: one table, one row per record, one cell per field.
/// </summary>
/// <remarks>
/// <para>
/// A cell's text is its own paragraphs (<see cref="DocumentTable"/>), so a field
/// that holds line breaks becomes a cell of several paragraphs, and a field that
/// holds none becomes a cell of one. Values are text as written: nothing is
/// parsed as a number or a date, and nothing that looks like a formula is
/// evaluated. A spreadsheet's types are its own reading of the text, and the
/// text is what the file holds.
/// </para>
/// <para>
/// The grid is rectangular. A record with fewer fields than the widest is
/// padded with empty cells, which is how every spreadsheet shows it, and the
/// padding is noted so it is not mistaken for something the file said.
/// </para>
/// </remarks>
internal static class CsvReader
{
    /// <summary>How many records are read between cancellation checks.</summary>
    private const int CancellationStride = 1024;

    public static DocumentReadResult Read(
        ReadOnlySpan<byte> bytes,
        bool truncated,
        CsvReadOptions options,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<DocumentDiagnostic>();
        if (truncated)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                CsvDiagnosticCodes.LimitBytes,
                "The input exceeded MaxDocumentBytes; only the records before the limit were read."));
        }

        string text = CsvTextDecoder.Decode(bytes, truncated, diagnostics);
        char delimiter = options.Delimiter ?? CsvSniffer.DetectDelimiter(text);

        var parser = new CsvParser(text, delimiter);
        var records = new List<List<string>>();
        int columns = 0;
        while (parser.ReadRecord() is List<string> record)
        {
            if (records.Count % CancellationStride == 0)
                cancellationToken.ThrowIfCancellationRequested();

            records.Add(record);
            columns = Math.Max(columns, record.Count);
        }

        if (parser.SawUnterminatedQuote)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                CsvDiagnosticCodes.QuoteUnterminated,
                "A quoted field was still open at the end of the input, so it ran to the end; " +
                "the records after its opening quote may have been read into it."));
        }

        if (parser.SawStrayQuote)
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                CsvDiagnosticCodes.QuoteStray,
                "A quote stood inside an unquoted field or after a closing quote; it was kept as text."));
        }

        RichTextDocument document = BuildTable(records, columns, options, diagnostics, cancellationToken);
        return new DocumentReadResult(document, diagnostics, DocumentReadResult.StatusFrom(diagnostics));
    }

    private static RichTextDocument BuildTable(
        List<List<string>> records,
        int columns,
        CsvReadOptions options,
        List<DocumentDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        DocumentLimits limits = options.Limits;
        var paragraphs = new List<RichTextParagraph>();
        var rows = new List<TableRow>(records.Count);
        int paddedRows = 0;
        bool runTruncated = false;

        for (int r = 0; r < records.Count; r++)
        {
            if (r % CancellationStride == 0)
                cancellationToken.ThrowIfCancellationRequested();

            List<string> record = records[r];
            if (paragraphs.Count + ParagraphsNeeded(record, columns) > limits.MaxParagraphCount)
            {
                diagnostics.Add(DocumentDiagnostic.Warning(
                    CsvDiagnosticCodes.LimitParagraphs,
                    string.Create(CultureInfo.InvariantCulture,
                        $"The records exceeded MaxParagraphCount; {records.Count - r} of {records.Count} were dropped.")));
                break;
            }

            var cells = new List<TableCell>(columns);
            for (int c = 0; c < columns; c++)
            {
                int start = paragraphs.Count;
                if (c < record.Count)
                {
                    foreach (string line in Lines(record[c]))
                    {
                        string kept = line;
                        if (kept.Length > limits.MaxRunLength)
                        {
                            kept = kept[..limits.MaxRunLength];
                            runTruncated = true;
                        }

                        paragraphs.Add(RichTextParagraph.Plain(kept));
                    }
                }
                else
                {
                    paragraphs.Add(RichTextParagraph.Empty);
                }

                cells.Add(new TableCell(start, paragraphs.Count - start, c));
            }

            if (record.Count < columns)
                paddedRows++;

            rows.Add(new TableRow(cells, isHeader: options.HasHeaderRow && r == 0));
        }

        if (runTruncated)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                CsvDiagnosticCodes.LimitRun,
                "A field held a line longer than MaxRunLength; it was truncated."));
        }

        if (paddedRows > 0)
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                CsvDiagnosticCodes.RowRagged,
                string.Create(CultureInfo.InvariantCulture,
                    $"{paddedRows} record(s) had fewer fields than the widest; they were padded with empty cells to {columns} columns.")));
        }

        if (rows.Count == 0)
            return RichTextDocument.Empty;

        return RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, paragraphs.Count, rows)]);
    }

    /// <summary>The paragraphs a record takes once padded to <paramref name="columns"/> cells.</summary>
    private static long ParagraphsNeeded(List<string> record, int columns)
    {
        long needed = columns - record.Count;
        foreach (string field in record)
            needed += LineCount(field);
        return needed;
    }

    private static int LineCount(string field)
    {
        int count = 1;
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '\n' || (field[i] == '\r' && (i + 1 == field.Length || field[i + 1] != '\n')))
                count++;
        }

        return count;
    }

    /// <summary>A field's lines, split at CRLF, LF or a lone CR.</summary>
    private static IEnumerable<string> Lines(string field)
    {
        int start = 0;
        for (int i = 0; i < field.Length; i++)
        {
            char c = field[i];
            if (c is not ('\r' or '\n'))
                continue;

            yield return field[start..i];
            if (c == '\r' && i + 1 < field.Length && field[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        yield return field[start..];
    }
}
