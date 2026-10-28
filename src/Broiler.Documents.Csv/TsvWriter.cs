using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;

namespace Broiler.Documents.Csv;

/// <summary>
/// Writes the model as TSV (tab-separated values) per IANA text/tab-separated-values registration:
/// fields are separated by tabs, records by CRLF, and no quotes are added or escaped.
/// </summary>
internal sealed class TsvWriter
{
    private const char SoftBreak = (char)0x2028;
    private const char ObjectReplacement = (char)0xFFFC;
    internal const int MaxColumns = 16384;

    private readonly RichTextDocument _document;
    private readonly CsvWriteOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly StringBuilder _output = new();
    private readonly List<DocumentDiagnostic> _diagnostics = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private int _escapedFormulas;

    private TsvWriter(RichTextDocument document, CsvWriteOptions options, CancellationToken cancellationToken)
    {
        _document = document;
        _options = options;
        _cancellationToken = cancellationToken;
    }

    public static DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        CsvWriteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        bool revised = RevisionDisplay.HasRevisions(document);
        if (revised)
            document = RevisionDisplay.WithoutDeletions(document);

        var writer = new TsvWriter(document, options, cancellationToken);
        if (revised)
        {
            writer._diagnostics.Add(DocumentDiagnostic.Info(
                CsvDiagnosticCodes.RevisionAccepted,
                "Tracked changes were written accepted: the deleted text was left out and the inserted text written as plain text; TSV holds a cell's text only."));
        }

        byte[] bytes = writer.Build();

        cancellationToken.ThrowIfCancellationRequested();
        destination.Write(bytes, 0, bytes.Length);
        return new DocumentWriteResult(bytes.Length, writer._diagnostics, DocumentWriteResult.StatusFrom(writer._diagnostics));
    }

    private byte[] Build()
    {
        WriteBody();
        ReportDocumentLevel();

        if (_escapedFormulas > 0)
        {
            _diagnostics.Add(DocumentDiagnostic.Info(
                CsvDiagnosticCodes.FormulaEscaped,
                string.Create(CultureInfo.InvariantCulture,
                    $"{_escapedFormulas} field(s) that a spreadsheet would run as a formula were written with a leading apostrophe.")));
        }

        DocumentMetadataReport.Describe(
            _options.Metadata,
            DocumentMetadataReport.StatedFields(_options.Metadata),
            _diagnostics);

        byte[] body = Encoding.UTF8.GetBytes(_output.ToString());
        if (!_options.ByteOrderMark)
            return body;

        byte[] marked = new byte[body.Length + 3];
        marked[0] = 0xEF;
        marked[1] = 0xBB;
        marked[2] = 0xBF;
        body.CopyTo(marked, 3);
        return marked;
    }

    private void WriteBody()
    {
        int index = 0;
        while (index < _document.ParagraphCount)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (_document.TableStartingAt(index) is DocumentTable table)
            {
                WriteTable(table);
                index = Math.Max(index + 1, table.ParagraphEnd);
                continue;
            }

            WriteLooseParagraph(index);
            index++;
        }
    }

    private void WriteLooseParagraph(int index)
    {
        string text = ParagraphText(_document.Paragraphs[index]);
        if (text.Length == 0)
            return;

        ReportOnce(
            CsvDiagnosticCodes.TextOutsideTable,
            "Paragraphs outside a table were written as records of one field each.");
        WriteRecord([text]);
    }

    private void WriteTable(DocumentTable table)
    {
        ReportTableLayout(table);

        int columns = 1;
        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
                columns = (int)Math.Clamp((long)cell.ColumnIndex + cell.ColumnSpan, columns, MaxColumns);
        }

        var written = new bool[table.ParagraphCount];

        foreach (TableRow row in table.Rows)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var fields = new string?[columns];
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsRowSpanContinuation || cell.ColumnSpan > 1 || cell.RowSpan > 1)
                {
                    ReportOnce(
                        CsvDiagnosticCodes.TableSpan,
                        "A cell that spans columns or rows was written in its first column and row, and empty fields after it.");
                }

                if (cell.ColumnIndex >= columns)
                    continue;

                fields[cell.ColumnIndex] = CellText(table, cell, written);
            }

            WriteRecord(fields);
        }

        for (int p = 0; p < written.Length; p++)
        {
            if (written[p])
                continue;

            string text = ParagraphText(_document.Paragraphs[table.ParagraphIndex + p]);
            if (text.Length > 0)
                WriteRecord([text]);
        }
    }

    private string CellText(DocumentTable table, TableCell cell, bool[] written)
    {
        if (cell.Tables.Count > 0)
        {
            ReportOnce(
                CsvDiagnosticCodes.TableNested,
                "A table inside a cell was written as that cell's text, one line per paragraph; CSV holds only one grid.");
        }

        var builder = new StringBuilder();
        for (int p = 0; p < cell.ParagraphCount; p++)
        {
            int bodyIndex = cell.ParagraphIndex + p;
            int tableOffset = bodyIndex - table.ParagraphIndex;
            if (tableOffset >= 0 && tableOffset < written.Length)
                written[tableOffset] = true;

            string text = ParagraphText(_document.Paragraphs[bodyIndex]);
            if (text.Length == 0)
                continue;

            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append(text);
        }

        return builder.ToString();
    }

    private string ParagraphText(RichTextParagraph paragraph)
    {
        if (paragraph.Style != ParagraphStyle.Default)
        {
            ReportOnce(
                CsvDiagnosticCodes.ParagraphStyle,
                "Paragraph formatting (alignment, spacing, lists, indentation, page breaks) was not written.");
        }

        var builder = new StringBuilder(paragraph.Length);
        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            int length = Math.Min(run.Length, paragraph.Length - offset);
            if (run.Style.IsNote)
            {
                ReportOnce(CsvDiagnosticCodes.NoteOmitted, "A footnote or endnote was left out with its citation; TSV holds a cell's text only.");
                AppendSanitized(builder, paragraph.Text, offset, length, ObjectReplacement);
            }
            else if (run.Style.IsImage)
            {
                ReportOnce(CsvDiagnosticCodes.ImageOmitted, "A picture was left out; TSV holds text only.");
                AppendSanitized(builder, paragraph.Text, offset, length, ObjectReplacement);
            }
            else
            {
                if (run.Style != InlineStyle.Default)
                {
                    ReportOnce(
                        CsvDiagnosticCodes.InlineStyle,
                        "Character formatting and link targets were not written; their text was.");
                }

                AppendSanitized(builder, paragraph.Text, offset, length, null);
            }

            offset += length;
        }

        return builder.ToString();
    }

    private static void AppendSanitized(StringBuilder builder, string text, int start, int length, char? dropped)
    {
        for (int i = start; i < start + length; i++)
        {
            char c = text[i];
            if (c == dropped)
                continue;

            // In TSV, tabs cannot separate values within a field, and newlines separate records.
            if (c is '\t' or '\r' or '\n' or SoftBreak)
                builder.Append(' ');
            else
                builder.Append(c);
        }
    }

    private void WriteRecord(IReadOnlyList<string?> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
                _output.Append('\t');

            string field = fields[i] ?? string.Empty;
            if (_options.EscapeFormulas && field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\r')
            {
                field = "'" + field;
                _escapedFormulas++;
            }

            _output.Append(field);
        }

        _output.Append("\r\n");
    }

    private void ReportTableLayout(DocumentTable table)
    {
        bool layout = table.ColumnWidths.Count > 0 || table.CellPadding != DocumentTable.DefaultCellPadding;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            TableRow row = table.Rows[r];
            layout |= (row.IsHeader && r > 0) || row.MinHeight > 0;
            foreach (TableCell cell in row.Cells)
                layout |= cell.Borders.IsVisible || !cell.Shading.IsEmpty;
        }

        if (layout)
        {
            ReportOnce(
                CsvDiagnosticCodes.TableLayout,
                "Column widths, borders, shading, cell padding, row heights and header rows below the first were not written.");
        }
    }

    private void ReportDocumentLevel()
    {
        if (!_document.RunningContent.IsEmpty)
            ReportOnce(CsvDiagnosticCodes.RunningContent, "Headers and footers were left out; TSV has no page.");

        if (_document.Shapes.Count > 0)
            ReportOnce(CsvDiagnosticCodes.ShapeOmitted, "Floating shapes were left out; TSV holds text only.");
    }

    private void ReportOnce(string code, string message)
    {
        if (_reported.Add(code))
            _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }
}
