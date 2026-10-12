# Broiler.Documents.Csv Conformance

`Broiler.Documents.Csv` reads comma-separated values into a table in the
rich-text model and writes tables back out, through the same `DocumentCodec`
contract as every other format. It has no DOM, UI, OS, or third-party
dependency. Why a spreadsheet format goes through the rich-text model rather
than a model of its own is
[ADR 0016](../adr/0016-spreadsheet-formats-through-the-rich-text-model.md).

The grammar is RFC 4180's (§2). The reader accepts more than the RFC writes,
because files that break it are common; the writer produces the RFC's form.

## Probe

CSV has no signature, and any text with a comma in it is CSV of some shape. A
probe that looked at content alone would claim every plain-text file, so the
codec matches only when the caller's hint says CSV, and uses the content to say
how sure it is.

| Input evidence | Confidence |
|---|---|
| `.csv` extension or `text/csv` media type (parameters ignored), and the prefix reads as a grid: two or more records, the same number of fields in each, more than one | High |
| The same hint, and a prefix that is text but not a grid | Medium |
| The same hint, and a prefix with a NUL byte outside a UTF-16 or UTF-32 byte-order mark | No match |
| No hint | No match |

High is deliberate. A spreadsheet's first column often starts with something the
Markdown probe reads as a block marker, such as a row headed `1. Quartal`, and a
file the caller named `.csv` should not lose to that. The prefix's last line is
ignored, because a prefix usually ends inside a record. A caller with no hint
names the format instead, as `broilerdoc --from csv` does.

## Reading

### Encoding

CSV states no encoding, so the bytes are asked in a fixed order.

| Input | Read as |
|---|---|
| UTF-8, UTF-16 or UTF-32 byte-order mark | That encoding. Bytes that break it become U+FFFD and are reported as `csv.encoding.invalid` |
| No mark, valid UTF-8 | UTF-8 |
| No mark, not valid UTF-8 | Windows-1252, reported as `csv.encoding.fallback` |

UTF-8 is tried first because only a UTF-8 decode can fail: every byte sequence
is valid Windows-1252. The fallback is Windows-1252 rather than Latin-1 because
the two differ in exactly the characters such files hold, the euro sign and
curly quotes. The table is shared with the RTF codec (`Broiler.Documents.Text.Windows1252`),
and no encoding provider is registered. When the byte limit cuts a multi-byte
UTF-8 character in half, the half is dropped so the file is not mistaken for
Windows-1252.

### Delimiter

`CsvReadOptions.Delimiter` states it. Left null, the reader detects it from the
first 20 records, quotes respected:

1. Tab, semicolon and comma are each tried, and the field count most records
   share under it is taken as its width. A candidate whose width is one field
   is out.
2. The one under which the larger share of records has its width wins.
3. Then the wider.
4. Then the rarer character: tab, then semicolon, then comma. Commas occur inside
   values on their own, in prose and in every number written with a decimal
   comma, so a file whose records all split evenly on a semicolon is far more
   likely to be separated by one.

The common width rather than the first record's, because exports often open
with a title line above the data. Blank lines are passed over. A file no
candidate splits is read as one column.
Detection is what lets a semicolon-separated file with decimal commas, as
spreadsheets in many European locales save it, open without being told.

### Grammar

| Input | Read as |
|---|---|
| Fields separated by the delimiter | One cell each, left to right |
| CRLF, LF or a lone CR | End of record |
| A final line break, or none | Either; the last record may lack one |
| `"` at the start of a field | A quoted field, up to the closing quote; a delimiter, line break or `""` inside it is text |
| `""` inside a quoted field | One `"` |
| A line break inside a quoted field | A new paragraph in the same cell |
| Spaces around a value | Part of the value |
| A blank line | A row with one empty field |
| A delimiter at the end of a record | One more empty field |
| `"` inside an unquoted field | Kept as text, reported as `csv.quote.stray` |
| Text after a closing quote | Appended to the field, reported as `csv.quote.stray` |
| A quoted field still open at the end of input | Runs to the end, reported as `csv.quote.unterminated` |

### Mapping

| CSV | Model |
|---|---|
| The file | One `DocumentTable` covering every paragraph, with no column widths, borders or shading, because the file states none |
| A record | A `TableRow` |
| A field | A `TableCell` at its column, holding one plain paragraph per line |
| A record shorter than the widest | Padded with empty cells, reported as `csv.row.ragged` |
| The first record, when `CsvReadOptions.HasHeaderRow` is set | A header row (`TableRow.IsHeader`), which repeats at the top of each page |
| No records at all | `RichTextDocument.Empty`, with no table |

Values are text exactly as written. `007`, `1e5`, `2026-10-12` and `=SUM(A1:A2)`
are all strings: a spreadsheet's types are its own reading of the text, and
nothing that looks like a formula is evaluated. Whether the first record names
the columns is something RFC 4180 says beside the file, in the `header` media-type
parameter, never in it, so the reader is told rather than guessing.

## Writing

The writer walks the body in order and writes UTF-8 with CRLF after every record.

| Model | CSV |
|---|---|
| A table | Its rows as records, padded to the table's widest row |
| A cell with several paragraphs | One field, paragraphs joined by LF |
| A soft line break (U+2028) | LF in the field |
| A cell spanning columns | Its text in the first column, empty fields after it, reported as `csv.table.span` |
| A cell spanning rows | Its text in the first row, empty fields below, reported as `csv.table.span` |
| A table inside a cell | That cell's text, one line per paragraph, reported as `csv.table.nested` |
| A paragraph outside any table | A record of one field, reported as `csv.text.outside-table` |
| An empty paragraph outside any table | Nothing; it is spacing, and would come back as an empty row |
| A header row at the top | The first record, unreported, because the header line is the first record |
| Column widths, padding, borders, shading, row heights, a header row lower down | Dropped, reported as `csv.table.layout` |
| Character formatting and link targets | Dropped with the text kept, reported as `csv.inline-style` |
| Paragraph formatting | Dropped, reported as `csv.paragraph-style` |
| A picture | Left out, reported as `csv.image.omitted` |
| Floating shapes | Left out, reported as `csv.shape.omitted` |
| Headers and footers | Left out, reported as `csv.running-content` |
| Metadata the caller supplies | Dropped, reported as `document.metadata.dropped` naming the fields |
| Page geometry and style defaults | Not written and not reported: no reader of a CSV file expects a page |

A field is quoted only when it holds the delimiter, a quote, a CR or an LF, and a
quote inside it is doubled. Leading and trailing spaces are written as they are.
An empty document writes no bytes. The output is built in memory and written
once, so a cancelled or failed write leaves the destination untouched.

`CsvWriteOptions` sets the delimiter (comma by default; a quote, CR or LF is
refused), a UTF-8 byte-order mark (off by default), and formula escaping (off by
default, below).

### Formulas

A spreadsheet opening a CSV file runs a field that starts with `=`, `+`, `-` or
`@` as a formula, and some run a leading tab or CR the same way. A document read
from somewhere untrusted and saved as CSV can therefore carry a formula to
whoever opens the result. With `CsvWriteOptions.EscapeFormulas` set, such a field
is written with a leading apostrophe, which spreadsheets show as text, and the
count is reported as `csv.formula.escaped`. It is off by default because it
changes the data: `-5` is written as `'-5`, and a CSV round trip no longer gives
back what went in.

## Diagnostics

| Code | Severity | Direction | Meaning |
|---|---|---|---|
| `csv.limit.bytes` | Error | Read | The input exceeded `MaxDocumentBytes`; only the records before the limit were read |
| `csv.limit.paragraphs` | Warning | Read | The records would have exceeded `MaxParagraphCount`; the rest were dropped whole. The message gives the count |
| `csv.limit.run` | Warning | Read | A line of a field exceeded `MaxRunLength` and was truncated |
| `csv.encoding.fallback` | Warning | Read | No byte-order mark and not UTF-8, so read as Windows-1252 |
| `csv.encoding.invalid` | Warning | Read | The byte-order mark named an encoding the bytes broke; the breaks are U+FFFD |
| `csv.quote.unterminated` | Warning | Read | A quoted field ran to the end of the input |
| `csv.quote.stray` | Info | Read | A quote stood where the grammar allows none; it was kept as text |
| `csv.row.ragged` | Info | Read | Short records were padded to the widest; the message gives the count |
| `csv.text.outside-table` | Warning | Write | Paragraphs outside a table were written as one-field records |
| `csv.table.span` | Warning | Write | A merged cell was written in its first column and row |
| `csv.table.nested` | Warning | Write | A table inside a cell was written as the cell's text |
| `csv.table.layout` | Warning | Write | Widths, padding, borders, shading, row heights or a lower header row were dropped |
| `csv.inline-style` | Warning | Write | Character formatting or link targets were dropped |
| `csv.paragraph-style` | Warning | Write | Paragraph formatting was dropped |
| `csv.image.omitted` | Warning | Write | A picture was left out |
| `csv.shape.omitted` | Warning | Write | Floating shapes were left out |
| `csv.running-content` | Warning | Write | Headers and footers were left out |
| `csv.formula.escaped` | Info | Write | Fields were escaped as `EscapeFormulas` asked; the message gives the count |

Each is reported once per read or write. Messages carry counts, never document
text. The codes are the constants on `CsvDiagnosticCodes`.

## Security And Limits

- The reader evaluates nothing, fetches nothing and resolves nothing. A value is
  text.
- `MaxDocumentBytes` bounds the input. `MaxParagraphCount` bounds the grid,
  padding included, so one very wide record cannot turn every other row into as
  many cells. Records past the limit are dropped whole rather than cut part way.
  `MaxRunLength` bounds a line of a field.
- The writer's grid is at most 16,384 columns wide, the most a spreadsheet
  opens. A cell a malformed model places or spans past that joins the last
  column, so no text is lost and nothing is allocated at the size of the claim.
- Reads and writes through the request contract check cancellation while
  reading the input, every 1024 records, and before writing.
- Options of another codec's type are refused before any byte is touched
  (`document.options.invalid`).
- Formula escaping on write is the caller's choice, described above.

## Known Limitations

- One table per file. A CSV file has no sheets, and nothing marks where one
  table would end and the next begin.
- No types. Numbers, dates and booleans are text, and the writer writes text.
- The dialect is not carried. A file read with semicolons is written with
  commas unless the caller asks for semicolons again, and nothing in the read
  result says which delimiter was found.
- The `header` and `charset` media-type parameters are not read from a hint. A
  caller who has them sets `HasHeaderRow` or relies on the byte-order mark.
- A leading `sep=;` line, which some spreadsheets write to name the delimiter, is
  read as a record.
- Tab-separated values as their own format (`.tsv`, `text/tab-separated-values`)
  are not registered; a tab-separated file named `.csv` is read correctly.
- The table carries no borders, because the file states none. A host that wants
  a grid drawn styles it.

## Standards and rights

The rights position, the provenance findings, the sources consulted and the
proposed labels are in [the CSV register](../ip/csv.md). The reader deliberately
accepts input RFC 4180 does not allow, and no conformance testing has been done
on the writer, so nothing may describe this codec as conforming to the RFC.
