namespace Broiler.Documents.Csv;

/// <summary>
/// The diagnostic codes the CSV codec reports. Codes are API: a host branches on
/// them, and <c>docs/notes/csv.md</c> describes every one.
/// </summary>
public static class CsvDiagnosticCodes
{
    /// <summary>The input exceeded <see cref="DocumentLimits.MaxDocumentBytes"/> and was cut off there.</summary>
    public const string LimitBytes = "csv.limit.bytes";

    /// <summary>The records would have exceeded <see cref="DocumentLimits.MaxParagraphCount"/>; the rest were dropped.</summary>
    public const string LimitParagraphs = "csv.limit.paragraphs";

    /// <summary>A line of a field exceeded <see cref="DocumentLimits.MaxRunLength"/> and was truncated.</summary>
    public const string LimitRun = "csv.limit.run";

    /// <summary>The input was not UTF-8 and had no byte-order mark, so it was read as Windows-1252.</summary>
    public const string EncodingFallback = "csv.encoding.fallback";

    /// <summary>The input's byte-order mark named an encoding its bytes did not follow.</summary>
    public const string EncodingInvalid = "csv.encoding.invalid";

    /// <summary>A quoted field was still open at the end of the input.</summary>
    public const string QuoteUnterminated = "csv.quote.unterminated";

    /// <summary>A quote stood where the grammar does not allow one; it was kept as text.</summary>
    public const string QuoteStray = "csv.quote.stray";

    /// <summary>Records had different numbers of fields; short rows were padded with empty cells.</summary>
    public const string RowRagged = "csv.row.ragged";

    /// <summary>Paragraphs outside any table were written as records of one field each.</summary>
    public const string TextOutsideTable = "csv.text.outside-table";

    /// <summary>A merged cell was written as its text in the first column it covers, with empty fields for the rest.</summary>
    public const string TableSpan = "csv.table.span";

    /// <summary>A table inside a cell was written as that cell's text.</summary>
    public const string TableNested = "csv.table.nested";

    /// <summary>Column widths, borders, shading, padding, row heights or header rows were not written.</summary>
    public const string TableLayout = "csv.table.layout";

    /// <summary>Character formatting or a link target was not written.</summary>
    public const string InlineStyle = "csv.inline-style";

    /// <summary>Paragraph formatting was not written.</summary>
    public const string ParagraphStyle = "csv.paragraph-style";

    /// <summary>A picture was left out.</summary>
    public const string ImageOmitted = "csv.image.omitted";

    /// <summary>A footnote or endnote was left out, with its citation.</summary>
    public const string NoteOmitted = "csv.note.omitted";

    /// <summary>Floating shapes were left out.</summary>
    public const string ShapeOmitted = "csv.shape.omitted";

    /// <summary>Headers and footers were left out.</summary>
    public const string RunningContent = "csv.running-content";

    /// <summary>A field that a spreadsheet would run as a formula was written with a leading apostrophe.</summary>
    public const string FormulaEscaped = "csv.formula.escaped";
}
