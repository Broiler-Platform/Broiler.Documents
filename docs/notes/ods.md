# ODS Conformance

`Broiler.Documents.Ods` reads and writes OASIS OpenDocument Spreadsheet documents
(`.ods`, `.ots`) through the `DocumentCodec` contract, with no DOM, UI, OS, or
third-party dependencies. Spreadsheets project through the rich-text model's
tables ([ADR 0016](../adr/0016-spreadsheet-formats-through-the-rich-text-model.md));
`Broiler.Documents.Model.Spreadsheets` adds a typed `SpreadsheetWorkbook`
projected both ways to and from `RichTextDocument`. Rights are in the
[ODS IP Register](../ip/ods.md).

## Probe

| Input evidence | Confidence |
|---|---|
| `.ods`, `.ots` extension, or media type `application/vnd.oasis.opendocument.spreadsheet`, `application/vnd.oasis.opendocument.spreadsheet-template`, and ZIP prefix contains an uncompressed `mimetype` entry with value `application/vnd.oasis.opendocument.spreadsheet` | Certain |
| The same hint with valid ZIP magic (`PK\x03\x04`) | High |
| ZIP magic (`PK\x03\x04`) and uncompressed `mimetype` entry for ODS without hint | High |
| Extension or media type hint alone without matching prefix bytes | Low |
| No hint and no ZIP magic | No match |

## Reading

ODF part 2 §3.3 has the `mimetype` file stored first in the ZIP archive,
uncompressed (`CompressionLevel.NoCompression`). The reader checks:

1. `mimetype`: a valid OASIS spreadsheet MIME type.
2. `META-INF/manifest.xml`: part paths and content types.
3. `content.xml`: the document body and its `<office:spreadsheet>` element.
4. `styles.xml`: shared styles and formatting definitions.

Repeated rows and columns are expanded within bounds, since packages often
state e.g. `table:number-rows-repeated="1048500"` on empty trailing rows and
unbounded expansion would be an out-of-memory denial of service:

- Trailing rows with only empty cells are capped to one row.
- Non-empty repeated rows are bounded by `DocumentLimits.MaxTableRows`.
- Repeated columns (`table:number-columns-repeated`) are bounded by
  `DocumentLimits.MaxTableColumns`.

| ODS | Model |
|---|---|
| Each `<table:table>` sheet | A `DocumentTable` in the `RichTextDocument`; `table:name` is `DocumentTable.Name` and `SpreadsheetWorksheet.Name` |
| Explicit column and row styles | Column widths and row heights |
| Cell styles | Solid RGB fills and data-style references for fixed-decimal numbers, grouping, percentages and prefix currency symbols |
| `string` cell | `office:string-value` or nested `<text:p>` elements |
| `float` / `currency` / `percentage` cell | `office:value` |
| `boolean` cell | `office:boolean-value` |
| `date` cell | `office:date-value` |
| Cell values | Typed, in `TableCell.Value` (`CellValue`); number formats in `TableCell.NumberFormat` |
| `table:formula` | `TableCell.Formula`, the OpenFormula prefixes (`of:=...` or `:=...`) normalized to standard formula syntax |
| `table:number-columns-spanned`, `table:number-rows-spanned` | Cell spans; the `<table:covered-table-cell>` elements after them are skipped or spanned accordingly |

`OdsReadResult` carries both the `RichTextDocument` and the typed
`SpreadsheetWorkbook`.

## Writing

The writer produces an OASIS OpenDocument Spreadsheet package:

- `mimetype`: first in the ZIP archive, uncompressed (`NoCompression`),
  containing `application/vnd.oasis.opendocument.spreadsheet`.
- `META-INF/manifest.xml`: declares the root, `content.xml`, and `styles.xml`.
- `styles.xml`: the package styles part. The writer places deduplicated cell, data and dimension styles in `content.xml` as automatic styles.
- `content.xml`: `<office:spreadsheet>`, `<table:table>`,
  `<table:table-column>`, `<table:table-row>`, and typed `<table:table-cell>`
  elements; formulas in OpenFormula syntax (`of:=...`).

The number format subset covers codes such as `0.00`, `#,##0.00`, `0.0%` and
`$#,##0.00`. Supported dates (`yyyy-mm-dd`, `dd.mm.yyyy`, `mm/dd/yyyy`) normalize the input null date to the model's Excel-compatible serial and export using Calc's default date base. Dates before March 1900 are adjusted for Excel's fictitious leap day, which ODS cannot represent as a distinct date. Other date formats, custom literals and conditional sections require
additional mappings. Unsupported formats report a warning and retain the cell
value. Solid fills are written as opaque RGB. The reader recognizes directly
referenced styles from either package XML part.

Cell styles also carry common horizontal and vertical alignment, font family,
size, foreground color, bold, italic, underline, strikethrough and four side
borders. Export sets the alignment source for explicit non-left alignment.
The codec projects a single font per cell through the shared table model.

## Diagnostics

| Diagnostic Code | Meaning |
|---|---|
| `ods.corrupt` | The ZIP archive is malformed or damaged. |
| `ods.missing-content` | `content.xml` was not found in the package. |
| `ods.content` | `content.xml` has invalid XML or empty root. |
| `ods.missing-spreadsheet` | `content.xml` contains no `office:spreadsheet` element. |
| `ods.styles` | The optional styles XML could not be read. |
| `ods.number-format` | A cell number format was omitted because it could not be translated. |
| `ods.text.control` | A control character that XML cannot represent was dropped. |
