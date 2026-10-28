using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;

namespace Broiler.Documents.Csv;

/// <summary>
/// Reads TSV (tab-separated values) per IANA text/tab-separated-values registration:
/// fields are separated by tabs, records by line breaks, and quotes are plain text.
/// </summary>
internal static class TsvReader
{
    private const int CancellationStride = 1024;

    public static TsvReadResult Read(
        ReadOnlySpan<byte> bytes,
        bool truncated,
        CsvReadOptions options,
        CancellationToken cancellationToken,
        Encoding? explicitEncoding = null)
    {
        var diagnostics = new List<DocumentDiagnostic>();
        if (truncated)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                CsvDiagnosticCodes.LimitBytes,
                "The input exceeded MaxDocumentBytes; only the records before the limit were read."));
        }

        (string text, Encoding encoding) = CsvTextDecoder.Decode(bytes, truncated, diagnostics, explicitEncoding);

        var records = new List<List<string>>();
        int columns = 0;
        int position = 0;

        while (position < text.Length)
        {
            if (records.Count % CancellationStride == 0)
                cancellationToken.ThrowIfCancellationRequested();

            List<string> record = ReadLineFields(text, ref position);
            records.Add(record);
            columns = Math.Max(columns, record.Count);
        }

        RichTextDocument document = BuildTable(records, columns, options, diagnostics, cancellationToken);
        return new TsvReadResult(document, encoding, diagnostics, DocumentReadResult.StatusFrom(diagnostics));
    }

    private static List<string> ReadLineFields(string text, ref int position)
    {
        var fields = new List<string>();
        int fieldStart = position;

        while (position < text.Length)
        {
            char c = text[position];
            if (c == '\t')
            {
                fields.Add(text[fieldStart..position]);
                position++;
                fieldStart = position;
            }
            else if (c is '\r' or '\n')
            {
                fields.Add(text[fieldStart..position]);
                if (c == '\r' && position + 1 < text.Length && text[position + 1] == '\n')
                    position += 2;
                else
                    position++;
                return fields;
            }
            else
            {
                position++;
            }
        }

        fields.Add(text[fieldStart..position]);
        return fields;
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
            if (paragraphs.Count + columns > limits.MaxParagraphCount)
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
                    string kept = record[c];
                    if (kept.Length > limits.MaxRunLength)
                    {
                        kept = kept[..limits.MaxRunLength];
                        runTruncated = true;
                    }
                    paragraphs.Add(RichTextParagraph.Plain(kept));
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
}
