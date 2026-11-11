# Office issue #232: RTF embedded objects, IF fields and ruby

Three RTF samples, rendered locally with `broilerdoc render --font-dir` against the
pinned LibreOffice 24.2 PDFs (and a local LibreOffice 26.2.6.3 for comparison).

| Sample | Cause | Status |
| --- | --- | --- |
| `rtfexport/data/tdf156030.rtf` | Nine `IF` fields with no `\fldrslt`. The reader resolved only `TITLE`/`AUTHOR`/`SUBJECT` without a result, so the page was empty. | Fixed: `RtfReader.EvaluateIfField`. |
| `rtfimport/data/ole-inline.rtf` | `\object` was skipped as a whole, `\result` included. | Reader fixed; the page stays blank until EMF text is drawn (see below). |
| `rtfexport/data/tdf49073.rtf` | Ruby (`EQ \o\ad(\s\up …)`) annotations are not modelled; only the base text is kept (`rtf.field.ruby`). CJK glyphs missing from the pinned fonts draw as `?` here and as boxes in LibreOffice. | Not fixed. |

## IF fields

Arguments are split on whitespace, with quoted arguments kept whole (an unclosed quote
runs to the end). The last two arguments are the true and false texts, and everything
before them is the condition. LibreOffice parses malformed fields the same way: case 7
shows `value` and case 8 shows `se`. A field with fewer than three arguments shows
nothing. A comparison `a op b` (`=`, `<>`, `<`, `>`, `<=`, `>=`) is evaluated as Word
evaluates it, numerically when both sides are numbers. A lone number is true unless it
is zero. Any other condition is taken as true.

One line deliberately differs from LibreOffice. Probing LibreOffice 24.2 and 26.2 with
extra cases showed that Writer's calculator treats `=`, `==` and `<>` as always true.
For example, both `1 = 2` and `2 <> 2` show the true text, while `!=`, `<`, `>`, `<=`
and `>=` compare correctly, and string comparisons show nothing. Broiler follows Word
for `1 = 2`, so the sample's first line reads `false value 1` where the reference reads
`true value 1`. Because 26.2 still behaves this way, this is not a reference-defect row.

## Embedded objects

A new `RtfDestination.Object` skips the OLE data (`\objdata`, `\objclass` and any other
ignorable group) and reads `\result` in the destination the object opened in, the same
way Word and LibreOffice draw it. `rtf.embedded` is still reported, because the OLE
payload itself is not imported. The `ole-inline.rtf` result is an Equation.3 preview
EMF made only of text records. `EmfRasterizer` draws no text, so the picture is still
dropped. Drawing it needs EMF text output (`EXTCREATEFONTINDIRECTW`/`EXTTEXTOUTW`) with
access to the font set, or a WMF reader for the `\nonshppict` fallback.

## Ruby (tdf49073)

LibreOffice sets the phonetic text in small type above (`\ad`, `\al`, `\ar`) or centred
over the base, and the extra height pushes each line down. Representing this needs a
ruby run in the model and in line layout. That is a cross-layer feature and is out of
scope for this round.

Tests: `RtfIssue232Tests` (Rtf.Tests); `Broiler.Documents.Rtf.Tests` and
`Broiler.Documents.Office.Tests` both pass.
