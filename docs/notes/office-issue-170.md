# Issue #170: dictionary headers, Word 2013 justification and a defective reference

The first of the [30 ranked differences in issue #170](https://github.com/Broiler-Platform/Broiler.Documents/issues/170)
was `ooxmlexport/data/tdf163894_hidden.docx` (severity 1484.99): `broilerdoc`
drew 4 pages where the reference drew 22. The second was its sibling
`tdf163894.docx`, with the same numbers. Both are LibreOffice's test documents
for tdf#163894: one long justified paragraph listing dictionary entry words in
the character style "Main Index Entry", under a header of two fields,
`STYLEREF "Main Index Entry"` and `STYLEREF "Main Index Entry" \l`. The paths
and input SHA-256 are in `tests/office/selections/issue-170.txt` and
`tests/office/selections/issue-170.sha256`, with the third sample of the
same fix, `tdf163894_from_top.docx`. No document is vendored.

The difference had three parts: one defect in the reference and two in
`broilerdoc`.

## The reference is LibreOffice's bug

A `STYLEREF` naming a character style shows text in that style. In a header or
footer that is the first stretch on the page, or the last with `\l`, else
the nearest before the page, else the first after it. LibreOffice
before February 2026 showed the whole paragraph holding the first stretch
instead. It fixed this as tdf#163894 in `955f0f9b5e`, `da88bd54eb` and
`c29c1fd9c2` (master, 18 to 21 February 2026; first released in 26.8), and the
three DOCX samples came with the fix. `testTdf163894`, `testTdf163894_hidden` and
`testTdf163894_from_top_to_beginning_of_the_documentMarguerite` in
`sw/qa/extras/ooxmlexport/ooxmlexport14.cxx` pin each page's two header words.
`testTdf163894` in `sw/qa/extras/layout/layout2.cxx` records the old behaviour:
"This was 'Mauris … nonummy.', i.e. full text of the paragraph".

The reference LibreOffice is 24.2.7.2. Its PDFs in `office-references-v2` show
the old behaviour. `tdf163894.docx` and `tdf163894_hidden.docx` carry the
whole entry list in each header, so each page has room for a few lines of body,
and they run to 22 pages. `tdf163894_from_top.docx` heads its pages with its
first paragraph and runs to 5. `layout/data/tdf163894.fodt` heads its one page
with a whole paragraph. LibreOffice 26.2.6.3 still draws `tdf163894_hidden.docx`
on 67 pages; the notes for issues #143 and #145 measured four with 26.8.1.1.
So a renderer that draws the document as LibreOffice 26.8 and Word do can't
match this reference. Earlier triage, from issue #135 to #164, reached the same
conclusion each time, but had no way to record it.

That way now exists. `tests/office/extras-reference-defects.json` lists samples
whose reference is a traced LibreOffice defect, with the bug, the fixing
commits, the upstream tests and `fixedIn`. While the reference LibreOffice is
older than that, the runner skips the checks the row names, here the pixel
check, and lists the sample under "Known Reference Defects". `broilerdoc` still
has to read and render each sample with a documented exit code. The text
check is unaffected, because LibreOffice's text export leaves headers out, and
it still runs and ranks. The [office-conformance guide](../guides/office-conformance.md#the-upstream-ranking)
describes the rule, and `OfficeControlGuardTests` checks the file.

## Style references in headers

The DOCX reader used to read every field as the result Word saved, and the
headers showed "mitosis" and "modicum", the last page's words, on all four
pages. Now:

- **The field is known.** A complex field's `w:fldChar`/`w:instrText` and a
  `w:fldSimple` are followed (`DocxFields`). A `STYLEREF` naming a character
  style, with `\l` or the `\* MERGEFORMAT`/`CHARFORMAT` switches, marks its
  result runs with `InlineStyle.StyleReference` (`StyleReferenceField`). Other
  fields, including a `STYLEREF` of a paragraph style or with another switch,
  keep their saved result as before.
- **Runs keep their character style's name** (`InlineStyle.CharacterStyle`,
  from `w:rStyle`'s `w:name`), so the field has something to find text by.
- **Each page shows its own words.** A header or footer showing a style
  reference is laid out in a second pass, as one showing the page count is.
  The first pass records where each page's body text starts: laid-out pieces
  now carry their offset in the paragraph (`LayoutPiece.SourceOffset`). A page
  runs from its start to the next page's, so hidden text between the last
  visible character of one page and the first of the next belongs to the first
  page, as in LibreOffice. The search is `StyleReferenceField.Text`. Adjacent
  runs in the style are one stretch, hidden text counts, and an exact name is
  preferred over one that matches only ignoring case. Its order follows
  `SwGetRefFieldType::FindAnchorRefStyleMarginal` on master.

## Word 2013 justification

With the right search the headers were right on pages 1 and 2 and wrong on 3
and 4. LibreOffice ends page 3 at "misrepresenting ← AT THE END,", which puts
the hidden styled space after that word on page 3 (both header fields show a
space) and "mitosis, modicum" on page 4. `broilerdoc` ended page 3 two lines
earlier. Body pages matched LibreOffice line for line up to a line on page 2
that LibreOffice ends "… Donec commodo enim a" and `broilerdoc` broke before
the "a".

The document states Word's `compatibilityMode` 15. LibreOffice reads a DOCX of
mode 15 or later with `JustifyLinesWithShrinking` (tdf#130088, `7d08767b89`;
`SwTextGuess::Guess` in `guess.cxx`, the same rule in 24.2 and on master). A
justified line that would break may be wider than its column by a quarter of
a space for each space before the break, plus the one before the next word.
The line is then justified with its spaces narrowed. This is now
`DocumentStyleDefaults.ShrinksJustifiedLines`:

- The DOCX reader sets it from the highest `compatibilityMode` in `w:compat`
  (Word's URI, 11 and up), and the writer writes mode 15 back.
- The ODT reader and writer keep LibreOffice's own `JustifyLinesWithShrinking`
  configuration item.
- The line wrapper widens such a row once a word overflows it, and
  `PlaceLine` narrows its spaces to the column, including a last line that the
  extra width let in. A row holding a tab does not shrink (tdf#164499).

With both changes, `broilerdoc` draws the three DOCX samples on four pages
each. Their twelve page headers show exactly the words the three upstream tests
assert: handbooks/infuriating, infuriating/infuriating, initializes/misfitting
and misrepresenting/modicum; handbooks/infuriating, infuriating/infuriating,
a space each and mitosis/modicum; handbooks/infuriating,
infuriating/infuriating, initializes/Marguerite and maroon/modicum.

## Verification

The unit suite passes. There are regression tests for the search
(`StyleReferenceFieldTests`), the DOCX fields, names and compatibility mode
(`DocxStyleReferenceTests`), the ODT setting (`OdtJustificationSettingTests`),
per-page headers and shrinking in layout (`DocumentLayoutStyleReferenceTests`),
and the reference-defect list in the runner (`ReferenceDefectTests`) and its
guard.

Every corpus sample (3,896) was rendered with an isolated build of `b3d5120`
(main) and with this change, using the same fonts. 24 DOCX renders changed,
none of them in exit code, and one by a page. Adding the ODT setting changed
6 of the 16 flat ODT samples that state it. LibreOffice references were
prepared for those 30 samples, and both builds were scored against them:

| Axis | Before | After |
| --- | ---: | ---: |
| Pixel checks passed (of 30) | 15 | 19 |
| Text checks passed (of 30) | 24 | 24 |
| Pixel checks skipped | 4 | 4 |
| Summed severity | 9,449 | 7,271 |

The pixel checks that now pass are `layout/data/tdf119908_smart_hyphenation.docx`
and the three copies of `tdf130088.docx` (`layout`, `ooxmlexport`, `uiwriter`).
These are LibreOffice's own tests for Word 2013 justification, each a line that
fits only with shrinking. No check that passed fails. The largest rise in
severity is 1.4 (`ooxmlexport/data/tdf133701.docx`), within a failing pixel
check. The four skipped pixel checks are the three tdf163894 DOCX samples
(the host's LibreOffice, 26.2, is older than the fix too) and
`ooxmlexport/data/tdf38575_fullWidthLine.docx`, for which LibreOffice wrote no
PDF.

In a nightly run the three tdf163894 DOCX samples and
`layout/data/tdf163894.fodt` will be listed under "Known Reference Defects"
instead of ranked.

## Environment and reproduction

Windows, LibreOffice 26.2.6.3, Poppler 24.04.0 (pdftoppm), 96 DPI, 80%
threshold, the Liberation, Carlito, Caladea and DejaVu faces pinned on both
sides. The issue uses Linux, LibreOffice 24.2.7.2 and the `office-references-v2`
archive. That archive has no Liberation faces, so its reference drew these
documents in DejaVu; the
[workflow improvements note](office-conformance-workflow-improvements.md)
explains why and what to change.

```powershell
dotnet test Broiler.Documents.slnx
dotnet run --project src/tests/Broiler.Documents.Office -- `
  --extras-dir <sw/qa/extras samples> --sample-list tests/office/selections/issue-170.txt `
  --font-dir <font dir> --dpi 96 --allow-pending-tools
```

Follow-up work is in the [roadmap](../roadmap.md): style references to
paragraph styles and heading levels, the other `STYLEREF` switches, ODT's
`loext:style-ref` and `text:chapter`, and a header band sized from each page's
field text rather than the saved one.
