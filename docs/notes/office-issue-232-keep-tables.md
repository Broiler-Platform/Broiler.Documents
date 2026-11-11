# Office conformance issue #232: kept tables, a rotated frame, a magazine

Three samples of [issue 232](https://github.com/Broiler-Platform/Broiler.Documents/issues/232),
rendered locally with the runner (`--sample-list`, pinned fonts, `--accept-reference-mismatch`)
and compared with the pinned LibreOffice 24.2 PDFs and a local LibreOffice 26.2.6.3 export.

## `ooxmlexport/data/tdf119054.docx`: fixed

One single-row table follows an empty paragraph. Every paragraph in its cell is
a `Heading 2` (`Cmsor2`), whose style states `w:keepNext`. LibreOffice 24.2 and
26.2 both leave page one empty and start the table on page two, splitting the
row onto page three; Word does the same (its `w:lastRenderedPageBreak` sits in
the first cell run). `broilerdoc` started the table under the empty paragraph,
two pages against three.

Two causes:

- The DOCX reader never set `DocumentTable.KeepWithNext`. It now does when every
  paragraph the table holds keeps with the next, directly or through its style
  (`DocxTableReader`, `DocxDocumentBuilder.HasKeepWithNext`).
- Layout honoured a kept table only when the table and the next paragraph's
  opening fit on one page. Word and LibreOffice start a kept table that does
  not fit where it is on a new page even when it is longer than a page, and the
  table then splits from the top of that page (`DocumentLayout`).

Score 971 to 694, pages 3 against 3. Of the nine corpus DOCX files whose tables
are kept throughout, `tdf61000.docx` also went from failing to passing; none
regressed. What is left on `tdf119054.docx` is the face: the theme's heading font
is not among the pinned mappings.

Tests: `DocxPageBreakTests.A_Table_Whose_Every_Paragraph_Keeps_With_The_Next_Keeps_With_The_Next`,
`DocumentLayoutTableFlowTests.A_kept_table_longer_than_a_page_starts_on_a_new_page`.

## `ooxmlexport/data/TextFrameRotation.docx`: not fixed

A VML `v:rect` 820 x 44.35 pt, `rotation:-90`, with a `layout-flow:vertical`
bottom-to-top text box holding "33333 33333 33333" at 18 pt. `broilerdoc`
draws the text as a vertical strip near the left edge. The pinned 24.2 PDF is
blank and has no text objects at all, and LibreOffice 26.2 exports the same
blank page, so this is not a reference defect under the
`extras-reference-defects.json` rule (no fixed upstream bug). Without a Word
rendering to say where the text belongs, drawing nothing to match would be
moving toward an unexplained LibreOffice behaviour; left ranked.

## `layout/data/tdf123163-1.docx`: not fixed

The thirty-one-section magazine of earlier rounds (see
[office-extras-runs.md](office-extras-runs.md)). Page count matches (16), but the
first divergence is page 1 and the two-column flow drifts from there, so the
worst page (14 in CI, 10 locally) is a symptom of earlier column and frame
placement, not a page-14 defect. The missing-picture outlines (drawn where
LibreOffice draws nothing) still add ink. Too broad for a focused fix here.
