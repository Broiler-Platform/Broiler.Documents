# The third `sw/qa/extras` report: marks, bands and fallback metrics — 2026-10-20

The third ranked run over LibreOffice's `sw/qa/extras` tree (issue 120) put
three DOCX documents at the top. Two of them are documents the
[second report](office-extras-columns-verification-2026-10-20.md) already
explained. The third, `tdf168017.docx`, is a 29-page services contract that
`broilerdoc` drew on 33 pages with no header and no footer, and it was this
component's reading and layout, in four ways. Those are fixed. What is left of
all three is said below, rather than left to a page count.

## What was wrong here

1. **A family with no font mapping was measured with an estimate.** The
   contract is set in Arial Narrow, which neither side has. LibreOffice draws
   it with fontconfig's fallback, DejaVu Sans; `broilerdoc` draws it with the
   renderer's host fallback, which is the same face, and measured every line of
   it with the layout's estimate - 80% ascent and a rounded 125% line, 13 points
   at 10 - where the face it was drawing makes the line 11.64. The CLI reports
   a face's metrics only for families it maps, so a family it did not map got
   the estimate whatever face drew it. The host fallback is now measured for
   those families too (`FontResolution.MetricPath`). Every line of the
   contract's body was a tenth too tall.
2. **An empty paragraph was as tall as the document's default.** A paragraph
   with no text has no run, and the model had nothing else to say how tall its
   line is, so the layout used the document's default font: here Times New
   Roman at 12 points. Word states that size on the paragraph mark,
   `w:pPr/w:rPr` (ECMA-376 §17.3.1.29), and both Word and LibreOffice draw an
   empty paragraph at it. The contract opens with 36 empty paragraphs whose
   marks are 10-point Arial Narrow, and has about 190 in all; drawn at the
   default they pushed half of the first page's text onto the second. The
   model now holds the mark (`RichTextParagraph.MarkStyle`), edits keep it,
   the DOCX reader and writer carry it, the ODT and RTF readers fill it from
   the paragraph style's text properties and the formatting at `\par`, and the
   layout measures an empty line with it.
3. **A section that names no header continued nothing.** The contract has nine
   sections and names its header and footer in the first one only. ECMA-376
   §17.10.5 says a section with no header of a type has the previous
   section's, so they are on every page; the reader read the last section's
   references alone and found none. It now takes each type the last section
   leaves out from the nearest section before it that names one. Two rules
   that make that safe were missing as well: a `first` part counts only in a
   section with `w:titlePg`, and an `even` part only where `w:settings` holds
   `w:evenAndOddHeaders` (ECMA-376 §17.15.1.35). The contract names an even
   footer holding a lone page number and does not turn even pages on, so
   without the second rule the continued footer would have put that number on
   every even page where Word and LibreOffice draw the default. The writer now
   writes `w:evenAndOddHeaders` whenever it writes an even part, without which
   Word ignored the part.
4. **A header that ran past its margin was left out.** The contract's last
   sections set the header at 36 points in a top margin of 36, and the footer
   at 36 in a bottom margin of 36: neither fits inside its margin. The layout
   left such a band out and said so. ECMA-376 §17.6.11 says a positive top
   margin pushes the text down to keep it off the header, and a positive bottom
   margin up off the footer, which is what Word and LibreOffice draw. Each page
   now sets its header and footer at its own section's distances and margins,
   and starts and ends its text clear of them; a band that would take more than
   half of the text's height is still left out and reported, as one taller than
   its margin always was.

Each was checked against the standard's text, and against LibreOffice's pages
for what the standard leaves to the implementation: the first is a measurement
of the face both sides draw with.

## What was not wrong here, or not yet

- **`tdf123163-1.docx`, 12 pages against 16.** Unchanged, and for the reason
  the second report gave: none of its fourteen pictures is in the package, and
  LibreOffice keeps a grey box of the size each states - its third page is one
  of them - where this reader keeps nothing. Keeping that box is a model
  change, not a reader decision: an image with no payload reaches every writer,
  the resource policy and both layouts. It is on the
  [roadmap](../roadmap.md#format-fidelity). Its sections also differ in paper
  size by a few twips, and the page is drawn at the document's, which is what
  puts its `geometry` band at `off` by two or three pixels.
- **`tdf163894_hidden.docx`, 4 pages against 17.** LibreOffice 24.2's
  `STYLEREF` expansion, as the second report found: the header holds the whole
  document in small print. Its score moved from 1464.11 to 1474.11 with the
  third fix, and that is the fix being right. The document names a first and an
  even header and sets neither `w:titlePg` nor `w:evenAndOddHeaders`, so Word
  draws the default header on every page; this reader now does too, where it
  drew the first and even parts before.
- **`tdf168017.docx`, 31 pages against 29.** Its first eight pages now hold
  the same text as LibreOffice's, page for page, under the same header and
  over the same footer. From about its eleventh page it falls behind, and it
  reaches Exhibit D two pages after LibreOffice does, the second of them a few
  lines long; the two exhibits after that take the pages LibreOffice gives
  them. Where the drift starts was not pinned down. Two differences in its
  lists are the model's, not the reader's: the model has no list start, so
  clauses numbered from a `w:start` of 4, 9 and 12 are drawn from 1, and it
  picks a marker's format from the indent, so a level-0 clause indented half
  an inch draws "17." as "a.". List numbering is on the
  [roadmap](../roadmap.md#format-fidelity). The text comparison's 21 lines are
  the list labels LibreOffice's text projection writes and `broilerdoc dump`
  does not - with every leading label taken off both, the two projections are
  identical - which the
  [office guide](../guides/office-conformance.md#what-it-has-found) already
  classifies as a projection difference.

## Measured

The conformance runner was run over these three files at the resolution and
with the LibreOffice and poppler versions the issue names. The host had more
fonts than the workflow installs, and LibreOffice took Inter for Arial Narrow
from them, so LibreOffice ran under a fontconfig file listing only the
Liberation, Carlito, Caladea, DejaVu and LibreOffice directories; with that, the
first column reproduces the issue's three severities exactly. Each column is a
run of the build after that fix.

| Sample | Base | Fallback metrics | Paragraph mark | Bands read | Bands placed | LibreOffice |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `tdf123163-1.docx` | 12 (1619.51) | 12 (1619.51) | 12 (1619.52) | 12 (1619.52) | 12 (1619.52) | 16 |
| `tdf163894_hidden.docx` | 4 (1464.11) | 4 (1464.11) | 4 (1464.11) | 4 (1474.11) | 4 (1474.11) | 17 |
| `tdf168017.docx` | 33 (1382.71) | 32 (1418.13) | 30 (1369.23) | 30 (1374.23) | 31 (1329.33) | 29 |

Pages `broilerdoc` rendered, with the severity score in brackets. Two columns
move a score the wrong way, and both are the measurement seeing the next
difference. With its lines the right height, `tdf168017`'s first page still
overflowed on its empty paragraphs, and the overflow moved the worst page;
the bands, once read, were not drawn on any page until the last fix placed
them, and placing them pushes the text of its last sections by a line each way
- a page longer, and the best score of the five. The paragraph mark moved
`tdf123163-1` by a hundredth.

## Validation

New regressions: `FontMetricPathTests` (4), `ParagraphMarkStyleTests` (7),
`DocxParagraphMarkTests` (4), `OdtParagraphMarkTests` (1),
`RtfParagraphMarkTests` (2), with cases added to `LineMetricsTests`,
`DocxPropertyOrderTests`, `DocxRunningContentTests` and
`DocumentLayoutRunningContentTests`. Run against the base commit's reader and
writer, seven of the new running-content cases fail; the rest need the new
members to compile. Four existing running-content cases named a `first` header
in a section without `w:titlePg` and now state it, and two layout cases that
asserted a header or footer past its margin is left out now assert it is drawn
and pushes the text, with the leaving-out kept for a band that would take
more than half the text's height. The full solution passes: 3,019 tests
across the fourteen test projects, 3,016 passing and three skipped as before.
The corpus suite passes 2320/2320.

The office suite's seed corpus, run with the workflow's font arguments and
the same fontconfig file, fails 47 of 779 checks on the base commit - the 47 the
second report found - and 49 with these changes: the same 47 and two RTF
pixel rows that now agree better than the baseline records.

- `lists-nested/rtf` goes from `near` to `tight` on the ink box and `poor` to
  `fair` on the profile (0.744 to 0.962 on this host). LibreOffice's RTF sets
  its bullets in Symbol, which the pinned set does not map, and those lines
  were on the estimate until the first fix; run without it, the row matches
  the baseline again.
- `letterhead-frames/rtf` goes from `poor` to `fair` on the profile (0.863 to
  0.962) and `heavy` to `light` on the blur. Its header on page one runs
  past the top margin, and the text now starts below it, where LibreOffice
  starts it; run without the fourth fix, the row matches the baseline again.
  The runner ranks a row by its worst band, which is `wild` on the ink box both
  times - the footer LibreOffice draws on page one and this component, by
  `\titlepg`, does not - so it reports the row as agreeing less well.

Neither row is restated in `tests/office/office-baseline.json` here. A
baseline row is made on the toolchain the workflow pins and not on a
developer's host, whose numbers differ from it within a band (this host reads
`lists-nested/rtf`'s base ink box as 32 px where the baseline says 19), and the
new ink box of 8 px sits on the `tight` boundary. The nightly run will report
both rows, and they want regenerating there.
