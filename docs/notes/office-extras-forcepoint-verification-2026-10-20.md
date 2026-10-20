# The first `sw/qa/extras` report: three fuzzer files — 2026-10-20

The first ranked run over LibreOffice's `sw/qa/extras` tree (issue 116) put
three documents at the top, all from the `layout/data/forcepoint*` family:
`forcepoint91.html`, `forcepoint99.html` and `forcepoint102.rtf`. Each was
ranked on a page-count mismatch - 3 against 20, 3 against 23, 4 against 21.

These are fuzzer output, kept upstream as crash regressions. LibreOffice's own
test for each of the three is one line that loads the file, under the comment
`//just care it doesn't crash/assert`. So the question for each was not how to
reach LibreOffice's page count, but which of the differences were this
component reading the file wrongly. Four were, and are fixed. What is left is
LibreOffice's reading of malformed input, which is not a reference anybody
should be held to, and the report says so below rather than leaving a page
count to imply otherwise.

## What was wrong here

1. **RTF: content after the document's closing brace was read.** The fuzzer
   repeated the last block of `forcepoint102.rtf` until there were fourteen
   copies, thirteen of them past the `{\rtf1 ...}` group's closing brace. Word
   and LibreOffice stop at that brace; this reader carried on, and drew the
   block's footer text and its picture data, as hexadecimal, once per copy. It now stops at the root group's close and
   reports anything non-blank after it as `rtf.trailing` (informational). Checked
   against LibreOffice on a minimal file: `{\rtf1 Hello\par} After` reads as
   `Hello` there and now here.
2. **RTF: a destination nested inside a skipped one escaped it.** The same file
   lost a backslash before `\colortbl`, which left an unbalanced
   `{\stylesheet` holding the whole body. Every header and footer inside it set
   its own destination and became the page's header or footer. The general case
   matters well beyond fuzz input: a hyperlink inside a footnote put its text in
   the body beside the footnote mark. LibreOffice reads
   `A{\footnote ...{\fldrslt link}}B` as `A1B`; this reader read `AlinkB`, and now
   reads `AB`. A skipped destination is now skipped with everything inside it,
   as the list table already was; a picture inside one is still reported as
   `rtf.embedded`.
3. **HTML: `noframes` and `noembed` were read as prose.** Nearly all of
   `forcepoint99.html` sits inside `<noframes>`. The parser reads that element as
   raw text, as the standard says, and the reader then printed it - tags and all
   - as the document's text. The HTML rendering rules give both elements
   `display: none`, and the reader now skips them with the rest of that list
   (`area`, `basefont`, `datalist`, `param`). `rp` stays readable on purpose: it
   is the ruby fallback, and this model has no ruby.
4. **HTML: a NUL in body text was kept.** `forcepoint91.html` has NULs inside
   words. The tree builder ignores a NUL in body content, so a conforming DOM
   never holds one there; the parser this codec uses keeps it, and the renderer
   drew a missing glyph in the middle of `Cm?aq`. The reader now drops it.

Each was checked against a reference rather than against LibreOffice alone:
the RTF grammar and LibreOffice for the first two, and html5lib, a WHATWG
parser, for the HTML pair. After the fixes the text this component reads from
`forcepoint99.html` is the `<textarea>` content html5lib's tree holds once the
hidden elements are set aside, and `Cmaq New Zenand` is html5lib's spelling of
the NUL-damaged line in `forcepoint91.html`.

## What was not wrong here

LibreOffice's page counts come from its handling of the damage, not from the
documents' content:

- **`forcepoint102.rtf`, 21 pages.** LibreOffice also treats the body as part
  of the unbalanced stylesheet - its text projection holds none of the body's
  prose - but its importer lets the hidden `{\v\xe ...}` index entries out of
  the stylesheet as visible `XE "..."` fields, inside the table cells it also
  lets out. Twenty pages of that is the count. This component now reads one page:
  the literal `Ücolortbl;;;…` text the lost backslash left behind and the one
  copy of picture data that falls inside the document, both of which LibreOffice
  shows too.
- **`forcepoint99.html`, 23 pages.** LibreOffice's HTML import reads the
  `noframes` content as markup and lays it out - the column of stray `>` down
  its first page comes from there - along with tables whose fuzzed attributes
  draw as black blocks. The standard hides the first, and all but one of those
  tables sit between `<textarea>` and `</textarea>`, where it reads markup as
  the textarea's text - which is what this reader now shows.
- **`forcepoint91.html`, 20 pages.** Fuzzed table attributes - a
  `cellpadding` of 65536, a width of 2285779150443901191 - make LibreOffice lay
  out page-high grey cells holding one character per line. This reader flattens
  HTML tables to their cell paragraphs, so none of that geometry exists here.

## Measured

The conformance runner was run over just these three files, at the same
resolution and with the same LibreOffice and poppler versions the issue names,
once with the base commit's `broilerdoc` and once with this change. Fonts were
not pinned, so the absolute numbers differ slightly from the issue's; the two
runs share every condition but the build.

| Sample | Pages before | Pages after | LibreOffice | Severity before | Severity after |
| --- | ---: | ---: | ---: | ---: | ---: |
| `forcepoint91.html` | 3 | 3 | 20 | 2172.65 | 2172.65 |
| `forcepoint99.html` | 4 | 3 | 23 | 1974.37 | 2012.68 |
| `forcepoint102.rtf` | 4 | 1 | 21 | 1858.56 | 1986.50 |

The severity went *up* for two of the three, and that is the measurement
working rather than failing. It is dominated by the page-count gap, and reading
these files correctly widens the gap, because the pages LibreOffice adds are the
ones it invents from the damage. The same lesson the office guide records for
its own seeds applies: a score that gets worse after a fix is not necessarily a
regression. For `forcepoint91.html` the fix did not reach the worst page the
score is taken from, so the number did not move; the NUL glyphs it removed were
on another page.

Until the ranking told a crash-regression input from a document, this family
would have kept the top of the report, and every fix that read them more
faithfully would have pushed them higher. That was a decision about the runner
rather than about a codec, and it was taken separately: fuzzer output is now
set aside from the ranking and checked only for being handled, as
[the office guide](../guides/office-conformance.md#the-upstream-ranking)
describes.

## Validation

New regressions: `RtfSkippedDestinationTests` (47 cases),
`RtfDocumentEndTests` (12) and `HtmlHiddenContentTests` (14). Run against the
base commit's readers, 34 of the RTF cases and 11 of the HTML cases fail; all
pass with the change. The full solution passes: 2,827 tests across the
fourteen test projects, 2,824 passing and three skipped as before.

The office suite's own seed corpus, run with the workflow's font arguments on a
host whose font directory holds more than the pinned set, fails the same 47 of
779 checks on the base commit and with this change - the identical rows, 46 of
them pixel rows that the extra fonts account for - so the change moves nothing
there.

The corpus suite reports 2313/2320 with seven `roundtrip/.../rtf` rows failing
as "a loss that stopped happening" (line spacing 1.5 against 1). The base
commit reports the same seven rows, so they predate this change and are left
for whoever records that improvement in the corpus baseline.
