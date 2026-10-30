# XLSX Conformance

`Broiler.Documents.Xlsx` reads and writes Open XML SpreadsheetML workbooks
(ISO/IEC 29500 / ECMA-376) through the `DocumentCodec` contract, with no DOM,
UI, OS, or third-party package dependencies. Why spreadsheets project through
the rich-text model rather than a standalone one is
[ADR 0016](../adr/0016-spreadsheet-formats-through-the-rich-text-model.md);
`Broiler.Documents.Model.Spreadsheets` adds a typed `SpreadsheetWorkbook`
converted both ways to and from `RichTextDocument`. Rights are in the
[XLSX IP Register](../ip/xlsx.md).

## Probe

| Input evidence | Confidence |
|---|---|
| `.xlsx`, `.xlsm`, `.xltx` extension, or media type `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.ms-excel.sheet.macroEnabled.12`, or `application/vnd.openxmlformats-officedocument.spreadsheetml.template`, and ZIP prefix contains `[Content_Types].xml` with SpreadsheetML content | Certain |
| The same hint with valid ZIP magic (`PK\x03\x04`) | High |
| ZIP magic (`PK\x03\x04`) and `[Content_Types].xml` declaring SpreadsheetML workbook content type without hint | High |
| Extension or media type hint alone without matching prefix bytes | Low |
| No hint and no ZIP magic | No match |

## Reading

The reader walks the Open Packaging Conventions (OPC) package:

1. `_rels/.rels`: the primary office document relationship target (typically
   `xl/workbook.xml`).
2. `xl/workbook.xml`: sheet definitions
   (`<sheet name="..." sheetId="..." r:id="..."/>`).
3. `xl/_rels/workbook.xml.rels`: sheet relationship IDs resolved to worksheet
   parts (`xl/worksheets/sheet{N}.xml`), the shared strings part
   (`xl/sharedStrings.xml`), and the styles part (`xl/styles.xml`).

`xl/sharedStrings.xml` becomes an indexed string table, its memory bounded by
`DocumentLimits.MaxTextLength`: simple strings (`<si><t>...</t></si>`),
preserved whitespace (`xml:space="preserve"`), and rich text runs
(`<si><r><t>...</t></r>...</si>`) concatenated into one string.

`xl/styles.xml` supplies cell formatting:

- Number formats (`<numFmts>`): built-in codes (such as the standard date/time
  formats 14-22) and custom date/time formats are recognised so serial numeric
  dates convert to ISO-8601 strings.
- Cell formats (`<cellXfs>`): by the zero-based index a cell's `s` attribute
  names.
- Fill colours (`<fills>`): solid pattern fills and RGB/theme colours become
  cell shading background colours.

Each worksheet (`xl/worksheets/sheet{N}.xml`) becomes a `DocumentTable`:

| SpreadsheetML | Model |
|---|---|
| The sheet's name in `workbook.xml` | `DocumentTable.Name` and `SpreadsheetWorksheet.Name` |
| `<cols><col min="..." max="..." width="..." customWidth="1"/></cols>` | Table column widths |
| `<row r="..." ht="..." customHeight="1"/>` | Row heights |
| `r="B4"` | Cell position, from A1 notation |
| `t="s"` | Shared string, by index |
| `t="inlineStr"` | Inline string from `<is><t>` |
| `t="b"` | Boolean (`1` true, `0` false) |
| `t="e"` | Error text (e.g. `#REF!`, `#VALUE!`, `#DIV/0!`) |
| `t="str"` | Cached string result of a formula |
| No type / numeric | IEEE 754 floating-point or integer; a serial date with a date number format becomes an ISO-8601 date |
| Cell values | Typed, in `TableCell.Value` (`CellValue`); number formats in `TableCell.NumberFormat` |
| `<f>` | `TableCell.Formula` and `SpreadsheetCell.Formula` |
| `<mergeCells><mergeCell ref="A1:C2"/>` | `ColumnSpan` and `RowSpan` on the anchor cell |

`XlsxReadResult` carries both the `RichTextDocument` and the typed
`SpreadsheetWorkbook`.

## Writing

The writer produces an OPC XLSX package:

- `[Content_Types].xml`: content types for the workbook, worksheet, styles, and
  shared strings parts.
- `_rels/.rels`: the package root relationship to `xl/workbook.xml`.
- `xl/workbook.xml`: sheets with unique names and sheet IDs.
- `xl/_rels/workbook.xml.rels`: the workbook's links to each worksheet part,
  `styles.xml`, and `sharedStrings.xml`.
- `xl/styles.xml`: base styles, default font, fills, borders, and number formats.
- `xl/sharedStrings.xml`: text strings across all sheets, deduplicated.
- `xl/worksheets/sheet{N}.xml`: sheet dimensions, column widths, rows, cells
  (coordinates, types, values, and formulas), and merge cells.

## Diagnostics

| Diagnostic Code | Meaning |
|---|---|
| `xlsx.corrupt` | The ZIP archive or package structure is malformed. |
| `xlsx.missing-workbook` | The package contains no workbook relationship or `xl/workbook.xml` part. |
| `xlsx.xml` | The workbook XML part is invalid or empty. |
| `xlsx.sst` | Shared strings XML table is invalid or empty. |
| `xlsx.styles` | Styles XML part contains malformed XML. |
| `xlsx.sheet` | A worksheet XML part is malformed or invalid. |
| `xlsx.rels` | A relationships XML part is malformed or invalid. |
