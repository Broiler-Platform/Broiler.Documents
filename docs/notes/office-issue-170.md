# Issue #170: dictionary headers, Word 2013 justification and a defective reference

The first two of the [30 ranked differences in issue #170](https://github.com/Broiler-Platform/Broiler.Documents/issues/170)
were `ooxmlexport/data/tdf163894_hidden.docx` and its sibling `tdf163894.docx`
(severity 1484.99 each): `broilerdoc` drew 4 pages, the reference 22. Both are
LibreOffice's test documents for tdf#163894: one long justified paragraph of
dictionary entry words in the character style "Main Index Entry", under a
header of `STYLEREF "Main Index Entry"` and `STYLEREF "Main Index Entry" \l`.
Paths and input SHA-256, with the fix's third sample `tdf163894_from_top.docx`:
`tests/office/selections/issue-170.txt` and
`tests/office/selections/issue-170.sha256`; not vendored.

The difference had three parts: a defect in the reference and two in
`broilerdoc`.

## The reference is LibreOffice's bug

A `STYLEREF` naming a character style shows text in that style; in a header or
footer that is the first stretch on the page, or the last with `\l`, else the
nearest before the page, else the first after it. LibreOffice before February
2026 showed the whole paragraph holding the first stretch. The fix, tdf#163894
(`955f0f9b5e`, `da88bd54eb`, `c29c1fd9c2`; master, 18–21 February 2026; first
released in 26.8), came with the three DOCX samples:

- `testTdf163894`, `testTdf163894_hidden` and
  `testTdf163894_from_top_to_beginning_of_the_documentMarguerite` in
  `sw/qa/extras/ooxmlexport/ooxmlexport14.cxx` pin each page's two header words;
- `testTdf163894` in `sw/qa/extras/layout/layout2.cxx` records the old
  behaviour: "This was 'Mauris … nonummy.', i.e. full text of the paragraph".

The reference LibreOffice, 24.2.7.2 (`office-references-v2`), shows the old
behaviour: `tdf163894.docx` and `tdf163894_hidden.docx` carry the whole entry
list in each header, leaving room for a few body lines, and run to 22 pages;
`tdf163894_from_top.docx` heads its pages with its first paragraph and runs to
5; `layout/data/tdf163894.fodt` heads its one page with a whole paragraph.
LibreOffice 26.2.6.3 still draws `tdf163894_hidden.docx` on 67 pages; the
notes for issues #143 and #145 measured four with 26.8.1.1. A renderer drawing
these as LibreOffice 26.8 and Word do cannot match this reference. Triage from
issue #135 to #164 reached the same conclusion but had no way to record it.

Now `tests/office/extras-reference-defects.json` lists samples whose reference
is a traced LibreOffice defect, with the bug, fixing commits, upstream tests
and `fixedIn`. While the reference LibreOffice is older, the runner skips the
checks the row names (here pixel) and lists the sample under "Known Reference
Defects"; `broilerdoc` must still read and render it with a documented exit
code. The text check still runs and ranks: LibreOffice's text export leaves
headers out. The [office-conformance guide](../guides/office-conformance.md#the-upstream-ranking)
describes the rule; `OfficeControlGuardTests` checks the file. In a nightly
run the three tdf163894 DOCX samples and `layout/data/tdf163894.fodt` are
listed there instead of ranked.

## Style references in headers

The DOCX reader read every field as Word's saved result, so all four pages
were headed "mitosis" and "modicum", the last page's words. Now:

- **The field is known.** Complex fields (`w:fldChar`/`w:instrText`) and
  `w:fldSimple` are followed (`DocxFields`). A `STYLEREF` naming a character
  style, with `\l` or the `\* MERGEFORMAT`/`CHARFORMAT` switches, marks its
  result runs with `InlineStyle.StyleReference` (`StyleReferenceField`). Other
  fields, including a `STYLEREF` of a paragraph style or with another switch,
  keep their saved result.
- **Runs keep their character style's name** (`InlineStyle.CharacterStyle`,
  from `w:rStyle`'s `w:name`).
- **Each page shows its own words.** A header or footer showing a style
  reference is laid out in a second pass, like one showing the page count. The
  first pass records where each page's body text starts (laid-out pieces carry
  their paragraph offset, `LayoutPiece.SourceOffset`). A page runs from its
  start to the next page's, so hidden text between pages belongs to the
  earlier page, as in LibreOffice. The search (`StyleReferenceField.Text`)
  treats adjacent runs in the style as one stretch, counts hidden text, and
  prefers an exact name over a case-insensitive match; its order follows
  `SwGetRefFieldType::FindAnchorRefStyleMarginal` on master.

## Word 2013 justification

With the right search, headers were right on pages 1 and 2 and wrong on 3 and
4. LibreOffice ends page 3 at "misrepresenting ← AT THE END,", putting the
hidden styled space after it on page 3 (both fields show a space) and
"mitosis, modicum" on page 4; `broilerdoc` ended page 3 two lines earlier.
The first divergence was a page 2 line LibreOffice ends "… Donec commodo enim
a" and `broilerdoc` broke before the "a".

The document states Word's `compatibilityMode` 15. LibreOffice reads a DOCX of
mode 15 or later with `JustifyLinesWithShrinking` (tdf#130088, `7d08767b89`;
`SwTextGuess::Guess` in `guess.cxx`, same in 24.2 and on master): a justified
line that would break may be wider than its column by a quarter of a space for
each space before the break, plus the one before the next word, and is then
justified with narrowed spaces. This is now
`DocumentStyleDefaults.ShrinksJustifiedLines`:

- the DOCX reader sets it from the highest `compatibilityMode` in `w:compat`
  (Word's URI, 11 and up); the writer writes mode 15 back;
- the ODT reader and writer keep LibreOffice's `JustifyLinesWithShrinking`
  configuration item;
- the line wrapper widens such a row once a word overflows it, and
  `PlaceLine` narrows its spaces to the column, including a last line the
  extra width let in. A row holding a tab does not shrink (tdf#164499).

Now the three DOCX samples draw four pages each, and their twelve headers show
exactly what the upstream tests assert: handbooks/infuriating,
infuriating/infuriating, initializes/misfitting and misrepresenting/modicum;
handbooks/infuriating, infuriating/infuriating, a space each and
mitosis/modicum; handbooks/infuriating, infuriating/infuriating,
initializes/Marguerite and maroon/modicum.

## Verification

Tests: `StyleReferenceFieldTests`, `DocxStyleReferenceTests`,
`OdtJustificationSettingTests`, `DocumentLayoutStyleReferenceTests`,
`ReferenceDefectTests` and the guard.

Every corpus sample was rendered with `b3d5120` (main) and with this change: 24
DOCX renders changed (no exit code, one by a page) and 6 of the 16 flat ODT
samples stating the ODT setting. Scored on those 30 against LibreOffice: pixel
passes 15 → 19, text 24 → 24, summed severity 9,449 → 7,271. Newly passing:
`layout/data/tdf119908_smart_hyphenation.docx` and the three `tdf130088.docx`
copies (`layout`, `ooxmlexport`, `uiwriter`), LibreOffice's own Word 2013
justification tests. None lost; the largest rise is 1.4
(`ooxmlexport/data/tdf133701.docx`, failing pixel check). Four pixel checks
were skipped: the three tdf163894 DOCX samples (the host's 26.2 predates the
fix too) and `ooxmlexport/data/tdf38575_fullWidthLine.docx` (no LibreOffice
PDF).

## Environment and reproduction

Windows, LibreOffice 26.2.6.3, Poppler 24.04.0 (pdftoppm), 96 DPI, 80%
threshold, Liberation, Carlito, Caladea and DejaVu pinned on both sides. The
issue used Linux, LibreOffice 24.2.7.2 and `office-references-v2`, which has no
Liberation faces, so its reference drew these documents in DejaVu; the
[workflow note](office-conformance-workflow.md#issue-170) explains why and what
to change. Replay: `dotnet run --project src/tests/Broiler.Documents.Office --
--extras-dir <samples> --sample-list tests/office/selections/issue-170.txt
--font-dir <fonts> --dpi 96 --allow-pending-tools`.

Open, in the [roadmap](../roadmap.md): style references to paragraph styles and
heading levels, the other `STYLEREF` switches, ODT's `loext:style-ref` and
`text:chapter`, and a header band sized from each page's field text rather
than the saved one.
