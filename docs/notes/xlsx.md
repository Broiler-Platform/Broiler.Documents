# XLSX Conformance

`Broiler.Documents.Xlsx` reads and writes Open XML SpreadsheetML workbooks
(ISO/IEC 29500 / ECMA-376) through the `DocumentCodec` contract. It has no DOM,
UI, OS, or third-party package dependencies.

Why spreadsheet formats project through the rich-text model rather than a
standalone model is documented in
[ADR 0016](../adr/0016-spreadsheet-formats-through-the-rich-text-model.md). In
addition, `Broiler.Documents.Model.Spreadsheets` provides a typed
`SpreadsheetWorkbook` object model with bidirectional conversion to and from
`RichTextDocument`.

## Probe

| Input evidence | Confidence |
|---|---|
| `.xlsx`, `.xlsm`, `.xltx` extension, or media type `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.ms-excel.sheet.macroEnabled.12`, or `application/vnd.openxmlformats-officedocument.spreadsheetml.template`, and ZIP prefix contains `[Content_Types].xml` with SpreadsheetML content | Certain |
| The same hint with valid ZIP magic (`PK\x03\x04`) | High |
| ZIP magic (`PK\x03\x04`) and `[Content_Types].xml` declaring SpreadsheetML workbook content type without hint | High |
| Extension or media type hint alone without matching prefix bytes | Low |
| No hint and no ZIP magic | No match |

## Reading

### Package Structure

The reader inspects the Open Packaging Conventions (OPC) package:
1. `_rels/.rels`: Discovers the primary office document relationship target (typically `xl/workbook.xml`).
2. `xl/workbook.xml`: Discovers sheet definitions (`<sheet name="..." sheetId="..." r:id="..."/>`).
3. `xl/_rels/workbook.xml.rels`: Resolves sheet relationship IDs to worksheet part paths (`xl/worksheets/sheet{N}.xml`), the shared strings part (`xl/sharedStrings.xml`), and the styles part (`xl/styles.xml`).

### Shared Strings Table

`xl/sharedStrings.xml` is parsed into an indexed string table:
- Simple text strings (`<si><t>...</t></si>`).
- Preserved whitespace (`xml:space="preserve"`).
- Rich text runs (`<si><r><t>...</t></r>...</si>`), concatenated into a single string.
- Memory consumption is bounded by `DocumentLimits.MaxTextLength`.

### Styles and Number Formatting

`xl/styles.xml` provides cell formatting information:
- Number formats (`<numFmts>`): Built-in format codes (such as standard date/time formats 14-22) and custom date/time formats are identified to convert serial numeric dates into ISO-8601 strings.
- Cell formats (`<cellXfs>`): Mapped by zero-based index referenced by `s` attributes on cells.
- Fill colors (`<fills>`): Solid pattern fills and RGB/theme colors are mapped to cell shading background colors.

### Worksheets

Each worksheet (`xl/worksheets/sheet{N}.xml`) is parsed:
- **Sheet name**: Preserved from `workbook.xml` onto `DocumentTable.Name` and `SpreadsheetWorksheet.Name`.
- **Column widths**: Parsed from `<cols><col min="..." max="..." width="..." customWidth="1"/></cols>` and converted to table column widths.
- **Row heights**: Read from `<row r="..." ht="..." customHeight="1"/>` and mapped to row heights.
- **Cell references**: Resolved from A1-notation coordinates (`r="B4"`).
- **Cell value types**:
  - `t="s"`: Shared string index lookup.
  - `t="inlineStr"`: Inline string value from `<is><t>`.
  - `t="b"`: Boolean value (`1` for true, `0` for false).
  - `t="e"`: Error text (e.g. `#REF!`, `#VALUE!`, `#DIV/0!`).
  - `t="str"`: Cached string evaluation result.
  - Default / numeric: Parsed as IEEE 754 floating-point or integer values. Serial date numbers with date number formats are formatted as ISO-8601 dates.
- **Formulas**: Cell formula expressions `<f>` are stored in `TableCell.Formula` and `SpreadsheetCell.Formula`.
- **Merged cells**: `<mergeCells><mergeCell ref="A1:C2"/>` defines column and row spans on the anchor cell (`ColumnSpan` and `RowSpan`).

### Model Projection

- Each worksheet becomes a `DocumentTable` in the resulting `RichTextDocument`.
- The table carries its sheet name in `DocumentTable.Name`.
- Cells carry typed values in `TableCell.Value` (`CellValue`), formulas in `TableCell.Formula`, and number formats in `TableCell.NumberFormat`.
- `XlsxReadResult` carries both the `RichTextDocument` and the typed `SpreadsheetWorkbook`.

## Writing

The writer generates a valid Open Packaging Conventions (OPC) XLSX package:
- `[Content_Types].xml`: Registers content types for workbook, worksheet, styles, and shared strings parts.
- `_rels/.rels`: Standard package root relationship pointing to `xl/workbook.xml`.
- `xl/workbook.xml`: Declares sheets with unique names and sheet IDs.
- `xl/_rels/workbook.xml.rels`: Connects workbook to each worksheet part, `styles.xml`, and `sharedStrings.xml`.
- `xl/styles.xml`: Writes base styles, default font, fills, borders, and number formats.
- `xl/sharedStrings.xml`: Dedupes and stores text strings across all sheets.
- `xl/worksheets/sheet{N}.xml`: Serializes sheet dimensions, column widths, rows, cells (with coordinates, types, values, and formulas), and merge cells.

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

## IP and Governance

See the [XLSX IP Register](../ip/xlsx.md).
