using System.Text;

namespace Broiler.Documents.Csv.Tests;

/// <summary>Builds and inspects the documents the CSV tests read and write.</summary>
internal static class CsvFixtures
{
    public static DocumentReadResult Read(string csv, DocumentReadOptions? options = null) =>
        Read(Encoding.UTF8.GetBytes(csv), options);

    public static DocumentReadResult Read(byte[] bytes, DocumentReadOptions? options = null)
    {
        using var stream = new MemoryStream(bytes);
        return new CsvDocumentCodec().Read(stream, options);
    }

    public static string Write(RichTextDocument document, DocumentWriteOptions? options = null) =>
        Encoding.UTF8.GetString(WriteBytes(document, options).Bytes);

    public static (byte[] Bytes, DocumentWriteResult Result) WriteBytes(RichTextDocument document, DocumentWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        DocumentWriteResult result = new CsvDocumentCodec().Write(document, stream, options);
        return (stream.ToArray(), result);
    }

    public static DocumentWriteResult WriteResult(RichTextDocument document, DocumentWriteOptions? options = null) =>
        WriteBytes(document, options).Result;

    /// <summary>The document's one table as cell texts, a cell's paragraphs joined by line feeds.</summary>
    public static string[][] Grid(RichTextDocument document)
    {
        DocumentTable table = Assert.Single(document.Tables);
        return [.. table.Rows.Select(row => row.Cells.Select(cell => CellText(document, cell)).ToArray())];
    }

    public static string CellText(RichTextDocument document, TableCell cell) =>
        string.Join("\n", Enumerable.Range(cell.ParagraphIndex, cell.ParagraphCount)
            .Select(index => document.Paragraphs[index].Text));

    /// <summary>A document holding one table and nothing else; a line feed in a cell starts a new paragraph.</summary>
    public static RichTextDocument Table(params string[][] rows)
    {
        var paragraphs = new List<RichTextParagraph>();
        DocumentTable table = TableOver(paragraphs, rows);
        return RichTextDocument.FromParagraphs(paragraphs).WithTables([table]);
    }

    /// <summary>Appends a table's cell paragraphs to <paramref name="paragraphs"/> and returns the grid over them.</summary>
    public static DocumentTable TableOver(List<RichTextParagraph> paragraphs, string[][] rows)
    {
        int start = paragraphs.Count;
        var tableRows = new List<TableRow>();
        foreach (string[] row in rows)
        {
            var cells = new List<TableCell>();
            for (int column = 0; column < row.Length; column++)
            {
                int cellStart = paragraphs.Count;
                foreach (string line in row[column].Split('\n'))
                    paragraphs.Add(RichTextParagraph.Plain(line));
                cells.Add(new TableCell(cellStart, paragraphs.Count - cellStart, column));
            }

            tableRows.Add(new TableRow(cells));
        }

        return new DocumentTable(start, paragraphs.Count - start, tableRows);
    }

    public static string[] Codes(IEnumerable<DocumentDiagnostic> diagnostics) =>
        [.. diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal)];
}
