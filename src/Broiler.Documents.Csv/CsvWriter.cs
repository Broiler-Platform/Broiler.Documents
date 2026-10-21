using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;

namespace Broiler.Documents.Csv;

/// <summary>
/// Writes the model as CSV: every table as its grid of records, and every other
/// paragraph as a record of one field.
/// </summary>
/// <remarks>
/// <para>
/// CSV holds text in a grid and nothing else, so this writer's job is mostly to
/// decide what becomes a record and to say what it could not keep. It walks the
/// body in order. A table is written row by row, padded to its widest row so
/// each record in it has the same number of fields. A paragraph outside any
/// table is written as a record of its own, so no text is lost by saving a
/// document that is not a table; an empty one is skipped, because it is spacing
/// rather than data and would otherwise come back as an empty row.
/// </para>
/// <para>
/// A cell's paragraphs are joined with line feeds into one field, which is how
/// a spreadsheet writes a cell with line breaks in it, and the reader splits
/// them back. A soft line break is written the same way, so it comes back as a
/// paragraph break.
/// </para>
/// <para>
/// The output is built in memory and written once, so a write that is cancelled
/// or fails has written nothing.
/// </para>
/// </remarks>
internal sealed class CsvWriter
{
    private const char SoftBreak = (char)0x2028;
    private const char ObjectReplacement = (char)0xFFFC;

    /// <summary>
    /// The widest grid written: the most columns a spreadsheet opens. A cell
    /// placed or spanning past it comes from a malformed model, and its text
    /// joins the last column rather than costing an allocation the size of
    /// whatever the model claimed.
    /// </summary>
    internal const int MaxColumns = 16384;

    private readonly RichTextDocument _document;
    private readonly CsvWriteOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly StringBuilder _output = new();
    private readonly List<DocumentDiagnostic> _diagnostics = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private int _escapedFormulas;

    private CsvWriter(RichTextDocument document, CsvWriteOptions options, CancellationToken cancellationToken)
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

        var writer = new CsvWriter(document, options, cancellationToken);
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

    /// <summary>A paragraph that no table cell holds, written as a record of one field.</summary>
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

        // Which of the table's paragraphs a cell wrote, so any that none did -
        // a table whose cells do not cover its range - still reach the output.
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
                        "A merged cell was written as its text in the first column it covers, " +
                        "with empty fields for the rest.");
                }

                if (cell.IsRowSpanContinuation)
                    continue;

                if (cell.Tables.Count > 0)
                {
                    ReportOnce(
                        CsvDiagnosticCodes.TableNested,
                        "A table inside a cell was written as that cell's text, one line per paragraph.");
                }

                string text = CellText(cell, table, written);
                int column = Math.Min(cell.ColumnIndex, columns - 1);
                fields[column] = fields[column] is null ? text : fields[column] + "\n" + text;
            }

            WriteRecord(fields);
        }

        for (int i = 0; i < written.Length && table.ParagraphIndex + i < _document.ParagraphCount; i++)
        {
            if (!written[i])
                WriteLooseParagraph(table.ParagraphIndex + i);
        }
    }

    private string CellText(TableCell cell, DocumentTable table, bool[] written)
    {
        var builder = new StringBuilder();
        int end = Math.Min(cell.ParagraphEnd, _document.ParagraphCount);
        for (int index = cell.ParagraphIndex; index < end; index++)
        {
            if (index > cell.ParagraphIndex)
                builder.Append('\n');

            builder.Append(ParagraphText(_document.Paragraphs[index]));

            int offset = index - table.ParagraphIndex;
            if (offset >= 0 && offset < written.Length)
                written[offset] = true;
        }

        return builder.ToString();
    }

    /// <summary>
    /// A paragraph's text as a field holds it, reporting the formatting and
    /// pictures that do not survive.
    /// </summary>
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
                ReportOnce(CsvDiagnosticCodes.NoteOmitted, "A footnote or endnote was left out with its citation; CSV holds a cell's text only.");
                AppendWithout(builder, paragraph.Text, offset, length, ObjectReplacement);
            }
            else if (run.Style.IsImage)
            {
                ReportOnce(CsvDiagnosticCodes.ImageOmitted, "A picture was left out; CSV holds text only.");
                AppendWithout(builder, paragraph.Text, offset, length, ObjectReplacement);
            }
            else
            {
                if (run.Style != InlineStyle.Default)
                {
                    ReportOnce(
                        CsvDiagnosticCodes.InlineStyle,
                        "Character formatting and link targets were not written; their text was.");
                }

                AppendWithout(builder, paragraph.Text, offset, length, null);
            }

            offset += length;
        }

        return builder.ToString();
    }

    private static void AppendWithout(StringBuilder builder, string text, int start, int length, char? dropped)
    {
        for (int i = start; i < start + length; i++)
        {
            char c = text[i];
            if (c == dropped)
                continue;

            builder.Append(c == SoftBreak ? '\n' : c);
        }
    }

    private void WriteRecord(IReadOnlyList<string?> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
                _output.Append(_options.Delimiter);

            AppendField(fields[i] ?? string.Empty);
        }

        _output.Append("\r\n");
    }

    private void AppendField(string field)
    {
        if (_options.EscapeFormulas && field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            field = "'" + field;
            _escapedFormulas++;
        }

        if (!NeedsQuotes(field))
        {
            _output.Append(field);
            return;
        }

        _output.Append('"');
        _output.Append(field.Replace("\"", "\"\"", StringComparison.Ordinal));
        _output.Append('"');
    }

    private bool NeedsQuotes(string field)
    {
        foreach (char c in field)
        {
            if (c == _options.Delimiter || c is '"' or '\r' or '\n')
                return true;
        }

        return false;
    }

    private void ReportTableLayout(DocumentTable table)
    {
        // A header row at the top is not a loss: RFC 4180's header line is the
        // first record, and whether it names the columns is said beside the
        // file rather than in it. One further down has no such reading.
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
            ReportOnce(CsvDiagnosticCodes.RunningContent, "Headers and footers were left out; CSV has no page.");

        if (_document.Shapes.Count > 0)
            ReportOnce(CsvDiagnosticCodes.ShapeOmitted, "Floating shapes were left out; CSV holds text only.");
    }

    private void ReportOnce(string code, string message)
    {
        if (_reported.Add(code))
            _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }
}
