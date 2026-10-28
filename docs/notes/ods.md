# ODS Conformance

`Broiler.Documents.Ods` reads and writes OASIS OpenDocument Spreadsheet documents
(`.ods`, `.ots`) through the `DocumentCodec` contract. It has no DOM, UI, OS, or
third-party dependencies.

Spreadsheet formats project through the rich-text model's tables per
[ADR 0016](../adr/0016-spreadsheet-formats-through-the-rich-text-model.md). In
addition, `Broiler.Documents.Model.Spreadsheets` provides a typed
`SpreadsheetWorkbook` representation with bidirectional projection to and from
`RichTextDocument`.

## Probe

| Input evidence | Confidence |
|---|---|
| `.ods`, `.ots` extension, or media type `application/vnd.oasis.opendocument.spreadsheet`, `application/vnd.oasis.opendocument.spreadsheet-template`, and ZIP prefix contains an uncompressed `mimetype` entry with value `application/vnd.oasis.opendocument.spreadsheet` | Certain |
| The same hint with valid ZIP magic (`PK\x03\x04`) | High |
| ZIP magic (`PK\x03\x04`) and uncompressed `mimetype` entry for ODS without hint | High |
| Extension or media type hint alone without matching prefix bytes | Low |
| No hint and no ZIP magic | No match |

## Reading

### Package Structure

Section 3.3 of ODF part 2 mandates that packages store the `mimetype` file
as the first entry in the ZIP archive, uncompressed (`CompressionLevel.NoCompression`).
The reader verifies package integrity:
1. `mimetype`: Checked for valid OASIS spreadsheet MIME type.
2. `META-INF/manifest.xml`: Validates part paths and content types.
3. `content.xml`: Contains the document body and `<office:spreadsheet>` element.
4. `styles.xml`: Read for shared styles and formatting definitions.

### Bounded Expansion

Spreadsheet packages frequently use repeated row and column attributes, e.g.
`table:number-rows-repeated="1048500"` on empty trailing grid rows. Unbounded
expansion would cause out-of-memory denial of service. The reader inspects cell
contents:
- Trailing rows with only empty cells are bounded (capped to 1 row).
- Non-empty repeated rows are bounded by `DocumentLimits.MaxTableRows`.
- Repeated columns (`table:number-columns-repeated`) are bounded by `DocumentLimits.MaxTableColumns`.

### Tables and Cells

- **Sheet name**: Read from `table:name` on `<table:table>` and preserved on
  `DocumentTable.Name` and `SpreadsheetWorksheet.Name`.
- **Column widths**: Extracted from `<table:table-column>` column styles.
- **Value types**:
  - `string`: Read from `office:string-value` or nested `<text:p>` elements.
  - `float` / `currency` / `percentage`: Read from `office:value`.
  - `boolean`: Read from `office:boolean-value`.
  - `date`: Read from `office:date-value`.
- **Formulas**: Read from `table:formula`. OpenFormula prefix conventions (`of:=...` or `:=...`) are normalized to standard formula syntax.
- **Merged cells**: `table:number-columns-spanned` and `table:number-rows-spanned` determine cell spans; subsequent covered cells (`<table:covered-table-cell>`) are skipped or spanned accordingly.

### Model Projection

- Each sheet produces a `DocumentTable` in the resulting `RichTextDocument`.
- Cells carry typed values in `TableCell.Value` (`CellValue`), formulas in `TableCell.Formula`, and number formats in `TableCell.NumberFormat`.
- `OdsReadResult` carries both the `RichTextDocument` and the typed `SpreadsheetWorkbook`.

## Writing

The writer generates a compliant OASIS OpenDocument Spreadsheet package:
- `mimetype`: Written uncompressed (`NoCompression`) as the first entry in the ZIP archive, containing `application/vnd.oasis.opendocument.spreadsheet`.
- `META-INF/manifest.xml`: Manifest declaring root, `content.xml`, and `styles.xml`.
- `styles.xml`: Standard styles, fonts, and office document properties.
- `content.xml`: Full spreadsheet content with `<office:spreadsheet>`, `<table:table>`, `<table:table-column>`, `<table:table-row>`, and typed `<table:table-cell>` elements. Formulas are written with standard OpenFormula syntax (`of:=...`).

## Diagnostics

| Diagnostic Code | Meaning |
|---|---|
| `ods.corrupt` | The ZIP archive is malformed or damaged. |
| `ods.missing-content` | `content.xml` was not found in the package. |
| `ods.content` | `content.xml` has invalid XML or empty root. |
| `ods.missing-spreadsheet` | `content.xml` contains no `office:spreadsheet` element. |

## IP and Governance

See the [ODS IP Register](../ip/ods.md).
