# Office issue #249: RTF layout (the rtf-layout cluster)

The 67 RTF samples of [issue 249](https://github.com/Broiler-Platform/Broiler.Documents/issues/249)
whose pixel check failed at `8a20ca0`. I ran them with
`eng/run-office-conformance.sh --font-dir … --font-set office-fonts-2` (144 dpi,
threshold 80 %) against LibreOffice 24.2.7.2. Where the question was what Writer
does, I converted a cut-down RTF with the local `soffice` 24.2.7.2 and read the
word boxes (`pdftotext -bbox`) or the FODT export's styles. Similarity is the
runner's pixel similarity of the worst page, before → after.

Most of the 67 were not about layout at all. They were about the formatting
broilerdoc reads from a hand-written or LibreOffice-test RTF. Several rules
apply to almost every such file:

1. The font of text that names none.
2. What a paragraph gets from the style it names.
3. How Writer bases a "heading N" style.
4. The space above a paragraph at the top of a page.

## Fixed Defects

- tdf#165805: RTF page break after table is no longer lost.

## Causes

| Cause | Samples | Outcome |
| --- | --- | --- |
| **Default font.** With no `\fonttbl`, or with no `\deff`, broilerdoc left the family unstated and drew sans serif. writerfilter applies `\deff` only when it reads the font table, and does not imply font 0. Text that names no font is set in Writer's default, Times New Roman. soffice: `{\rtf1 Hello\par}`, `\deff0` with no table, and a table naming `\f0` Arial with no `\deff` all draw Liberation Serif. A `\deff` or `\fN` that the table does not name, or names blank, is drawn in the fallback face, DejaVu Sans (`\f5` with `\deff1` Arial gives DejaVu Sans). 46 of the 67 samples have no table (41) or no `\deff` (5). | `gutter-top`, `tdf118047`, `tdf87034`, `tdf131234`, `tdf158044`, `fdo84679`, `tdf122430` and most of the others | **Fixed** (`RtfReader.DefaultFontFamily`, `RtfFontTable.Resolve`). The default is Times New Roman. An unknown `\fN` in a run or style is the generic `sans-serif`. A `\deff` the table does not name stays unstated, which already means the fallback face. |
| **A style's own formatting.** broilerdoc applied everything a paragraph style (style 0 included) or a character style states. writerfilter treats a style's own formatting as something the paragraph or run is expected to repeat. For each property it does not repeat, `RTFSprms::cloneAndDeduplicate` sets the RTF default as direct formatting ("not found - try to override style with default", tdf#118047). soffice, FODT export: `\pard\plain\s1` with a style stating `\qc\sb\sa\li\ri\fi\cbpat\b\i\ul\cf\f\fs40` gives a paragraph that is left aligned, unindented, unspaced and unshaded, in 12 pt regular black text in the default font. The style's `\sl`, `\keepn`, `\strike`, `\caps` and `\highlight` stay. What the style inherits through `\sbasedon` is not reset. A character style (`{\cs2 x}`) is read the same way. | `tdf118047` (Normal `\fs72\sa3200`), `min-3`, `min-wtf`, `tdf165923` | **Fixed** (`RtfStyleOwnFormatting`): `tdf118047` passes. `min-3` 17.7 → 77.8 %, `min-wtf` 18.1 → 48.6 %. |
| **Associated fonts.** broilerdoc ignored `\af`/`\afs`. Word writes an empty paragraph's mark as `\loch\af31506\hich\af31506\dbch\af31505`, with no `\f`. After the reset above, such marks lost Normal's font. soffice: `{\af1 x}`, `{\loch\af1 x}`, `{\hich\af1 x}`, `{\ltrch\af1 x}` and `{\rtlch\loch\af1 x}` set x in font 1, `{\f1\af0 x}` in font 0, and `{\afs40 x}` in 20 pt. `{\rtlch\af1 x}`, `{\rtlch\fcs1 \af1 \ltrch\fcs0 x}` and `{\dbch\af1 x}` leave the Latin font alone. | `all_gaps_word.rtf` (pass → 79.75 % with the reset alone) | **Fixed** (`TakesAssociatedFont`): `all_gaps_word` passes again. |
| **Writer's heading styles.** writerfilter maps an RTF style named "heading 1"–"heading 9", "Title" or "Subtitle" to Writer's own style. It clears that style's formatting, and the style keeps its Writer parent "Heading" unless the entry states `\sbasedon`. FODT export of `{\s3 heading 3;}`: "Heading 3" has no formatting of its own, and its parent "Heading" is Liberation Sans 14 pt, 0.423 cm above, 0.212 cm below, kept with next. With `\sbasedon0` the parent is "Standard". An alias (`heading 1,h1`) is not mapped. | `fdo74599`, `fdo80742`, `fdo83204`, `fdo62044`, `tdf106950`, `tdf108955`, `tdf152784_1`, `tdf94456`, `tdf107480` | **Fixed** (`RtfStyleSheet.WriterHeading`): all pass. |
| **Space above the first paragraph.** writerfilter sets `AddParaTableSpacingAtStart` for RTF as it does for DOCX. soffice: `\pard\sb480 Foo` starts 24 pt down, and `\sb80` 4 pt. | `char-color` (24 pt heading), `tdf126309`, `fdo82071`, `4010_min` | **Fixed** (`PreserveOpeningParagraphSpacing`). |
| **Space above at a page a paragraph opens.** A `\page` before any text of its paragraph, `\pagebb` and a section break keep the paragraph's space above at the top of the page. A `\page` in mid-paragraph does not. soffice: `A\par\pard\sb480\page B`, `A\par\page\pard\sb480 B`, `A\par\pard\pagebb\sb480 B` and `A\par\sect\sectd\pard\sb480 B` set B 24 pt down. `\pard\sb480 A\page B` sets B at the top. | `tdf108123` (a `\sb480` heading opening a section) | **Fixed**: `PageBreakKeepsSpaceAbove` is set on such breaks, only where there is space above. |
| **Footnote separator.** broilerdoc always drew the line above the footnotes. writerfilter draws it only when the document has a separator, which in RTF is a `\chftnsep` (`DomainMapper::lcl_utext`'s 0x3, `m_bHasFtnSep`). Otherwise it sets the line's width to 0 and keeps its room. soffice: the same note with `{\*\ftnsep\chftnsep}` has a line at y 488. With `{\*\ftnsep\par}` or no `\ftnsep` it has none, and the note stays at y 493. | `copypaste-footnote`, `tdf122430`, `tdf87034` | **Fixed**: `NoteNumbering.DrawsSeparatorLine`, read from `\chftnsep` and written back by `RtfWriter`. |
| **A note's last `\par`.** It opens one more, empty paragraph, which writerfilter keeps. soffice: a note ending `note\par` stands one line higher than one ending `note`, and one ending `\par\par` two lines higher. | `cp1000018.rtf` (pass → 76.2 % once the line above was gone) | **Fixed**: `Accumulator.Build(keepEmptyLast)` for notes. `cp1000018` passes again. |
| **HTML auto spacing.** `\sbauto1`/`\saauto1` were not read. With `\htmautsp` they stand for 14 pt, none above the first paragraph, and spacing collapses to the larger of two values. Without it they stand for 5 pt, and the two add. soffice: two auto-spaced paragraphs start at the margin, 27.8 pt apart, with `\htmautsp`. Without it they start 5 pt down, 23.8 pt apart. `\sa400` then `\sb400` stand 33.8 pt apart with it and 53.8 pt without. This is the same rule as `DocxReader.ApplyAutoSpacing`. | `tdf107480` | **Fixed**: `\htmautsp` sets `CollapsesParagraphSpacing`. |
| **Contextual spacing.** `\contextualspace` was not read. Writer adds no space between two paragraphs of one style when the paragraph that would give it says so. | `tdf113202` | **Fixed**, as DOCX's `w:contextualSpacing` already is. |
| **Gutter.** `\gutter`/`\guttersxn` were not read. soffice: `gutter-left.rtf`'s text starts 36 pt right of its 1 inch margin. `\gutterprl` (`gutter-top.rtf`) moves the text 36 pt down. `\rtlgutter` widens the right margin. | `gutter-top` (0 %) | **Fixed**: the gutter widens its margin in the page geometry. Shapes measured from the margin still use the margin without the gutter. |
| **Legacy text box fill.** A `\dptxbx` was filled with its `\dpfillbgc`. soffice: a `\dptxbx` with a red background and `\dpfillpat0` or `\dpfillpat1` is white inside. A `\dprect` with the same words is red. | `tdf91074` (a black box) | **Fixed**: legacy text boxes are not filled. |
| **Hidden paragraph marks in the body.** Writer sets a paragraph whose mark is hidden on one line with the next. testTdf152872: "C DE". broilerdoc did this for headers only (issue 240). | `hidden-para-separator.rtf`; also `layout/data/hidden-para-separator.docx`, `tdf164903.docx` and the three `tdf131728.docx` | **Fixed**: `DocumentLayout.JoinBodyHiddenMarks`. A join stops before a paragraph that starts a table, section, page or column, or has shapes, and never starts from a paragraph with nothing shown (that one still takes no room). |
| **Default text inset.** RTF shapes that state no `dxTextLeft`/`dyTextTop` had no inset. The defaults are 0.1 in at the sides and 0.05 in above and below. soffice: "Hello" in a box at (72, 72) pt starts at x 79.2 pt. | `fdo66040.rtf` (with the next row) | **Fixed.** |
| **Shapes above the page.** soffice: an RTF text box or rectangle 50 pt above a first paragraph 28 pt down starts at the page's top edge. A DOCX drawing does not: applied to every format, this broke `tdf78902.docx` and `tdf106153.docx`. | `fdo66040.rtf`. The correct font had brought its pixel check from pass to 79.3 %; it passes again. | **Fixed for RTF only** (`DocumentShape.KeptBelowPageTop`). |
| **A `PAGE` field without a result.** It showed nothing. soffice shows the number: `fdo48193.rtf`'s text is "foo1bar". | `fdo48193.rtf` (text check) | **Fixed.** |

### Investigated, not fixed

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/tdf141173_missingFrames.rtf`, `rtfexport/data/tdf158586_lostFrame.rtf`, `rtfexport/data/tdf158586_pageBreak0.rtf` | Frame paragraphs (`\pvpg`, `\posxc`, `\wraptight`) before a `\page`/`\sect`. Writer anchors them to the next unframed paragraph. That paragraph's page depends on whether text precedes the `\page` in it: in `tdf141173` the frames go to page 2 and in `lostFrame` they stay on page 1. The `\wraptight` frame in `pageBreak0` swallows the break and takes a line. Issues 214, 227 and 240 changed this logic three times. | Not fixed. It needs the frame-to-anchor rules redone as one piece. 0 %, 42 %, 51 %. |
| `rtfexport/data/tdf49073.rtf`, `rtfexport/data/fdo62977.rtf`, `rtfexport/data/tdf129578_lostBI.rtf` | CJK text: the model has no East Asian font (as in issues 232–242). | Not fixed. |
| `rtfimport/data/tdf59699.rtf`, `rtfimport/data/fdo79319.rtf`, `rtfexport/data/mathtype.rtf`, `rtfexport/data/piccrop.rtf` | Linked pictures (ADR 0004), and embedded OLE objects whose results differ. | Not fixed, by policy or as a separate object feature. |
| `rtfexport/data/numbering-font.rtf` | The list level's own character formatting (`\f40\fs144` in `\listlevel`) is the marker's font. The model has no marker formatting for any format. | Not fixed: a model and layout feature (21 %). |
| `rtfimport/data/posh-posv.rtf`, `rtfimport/data/posh-leftright.rtf` | `fFitShapeToText` shrinks a `shapeType 202` box to its text, and `posv 2` centres it on its paragraph. The model's `GrowsToText` only grows, and the layout has no paragraph-relative alignment. | Not fixed (57 %, 59 %). The default inset (above) moved them 0.1 % either way. |
| `rtfimport/data/165483.rtf`, `rtfimport/data/tdf167254.rtf`, `rtfimport/data/165805.rtf` | LibreOffice 24.2 behaviour that the sample's own bug fixed later. `165483`: a `\ts` table style's spacing is applied to `\s96` paragraphs. `tdf167254`: an empty last `\trowd\row` turns the table into paragraphs. `165805`: a `\page` after a table is lost. | Not fixed. These would be rows in `extras-reference-defects.json`, but I could not confirm the fix commits from here. |
| `rtfexport/data/fdo43965.rtf` | Fonts named "Times" and "Helvetica" are resolved by fontconfig aliases broilerdoc's font lookup lacks, and `\box\brdrs` is not read. | Not fixed. |
| `rtfexport/data/n751020.rtf` | `\linkstyles` gives the paragraphs Writer's default 0.139 in below and 115 % line height. | Not fixed: one-off. |
| `rtfexport/data/fdo51916.rtf`, `layout/data/tdf152839_firstrows.rtf`, `rtfimport/data/tdf153196.rtf`, `rtfimport/data/tdf108055.rtf` | Nested table row heights and hidden first rows. | Not fixed (79 %, 78 %, 75 %, 59 %). |
| `rtfimport/data/n695479.rtf`, `uiwriter/data/tdf147006.rtf`, `rtfexport/data/tdf107413.rtf`, `rtfimport/data/ink.rtf` | Paragraph borders in frames, a `\do` line, a picture scaled to its frame, and wrapped shapes. | Not fixed. |
| `rtfexport/data/fdo49893-2.rtf`, `rtfimport/data/section-pagebreak.rtf`, `rtfexport/data/fdo39001.rtf`, `rtfimport/data/tdf160553.rtf`, `rtfimport/data/tdf59454.rtf`, `rtfimport/data/lndscpsxn.rtf` | Page counts around empty paragraphs, `\spltpgpar` and section breaks (1 vs 5, 5 vs 4, 4 vs 3, 3 vs 1, 3 vs 2 pages; `lndscpsxn` a landscape section). | Not fixed. Similarity on the pages compared is 93–100 %. |
| `rtfimport/data/cont-section-pagebreak.rtf`, `rtfexport/data/page-break-emptyparas-spltpgpar.rtf` | Empty paragraphs around page and continuous-section breaks. | Not fixed (51 %, 55 %). |
| `rtfexport/data/hexcrlf.rtf`, `rtfexport/data/tdf112507.rtf`, `layout/data/merge_hidden_redline_lineheight.rtf`, `rtfexport/data/min-3.rtf`, `layout/data/min-wtf.rtf`, `rtfimport/data/tdf165923.rtf` | What remains after the fixes above: `\'0d`/`\'0a` written as text, which LibreOffice breaks lines on (`hexcrlf`); table and cell geometry (`tdf112507`, `tdf165923`, `min-3`'s border above its first paragraph); hidden redlines (`merge_hidden_redline_lineheight`); a header's tab-positioned lines (`min-wtf`). | Not fixed. 55 %, 50 %, 65 %; `min-3` 17.7 → 77.8 %, `min-wtf` 18.1 → 48.6 %, `tdf165923` 37.0 → 60.0 %. |

## Verification

All runs used the conformance runner at 144 dpi with the pinned `office-fonts-2`
set and `--jobs 2`. "Before" is `8a20ca0`, rerun locally, and it reproduced
the baseline: all 67 samples fail pixel, and read and text pass.

- **The 67 samples:** pixel checks 0 → 27 passing.
  - Fixed: `4010_min`, `char-color`, `copypaste-footnote`, `fdo62044`, `fdo83204`,
    `fdo84679`, `gutter-top`, `tdf106950`, `tdf107480`, `tdf108123`, `tdf108955`,
    `tdf113202`, `tdf118047`, `tdf126309`, `tdf131234`, `tdf152784_1`,
    `tdf153195`, `tdf158982`, `tdf87034`, `tdf91074`, `tdf94456` (rtfexport);
    `fdo74599`, `fdo80742`, `fdo82071`, `hidden-para-separator`, `tdf122430`,
    `tdf158044` (rtfimport).
  - Summed severity went from 38,766 to 26,182.
  - Improved but still failing: `min-3` 17.7 → 77.8 %, `min-wtf` 18.1 → 48.6 %,
    `tdf165923` 37.0 → 60.0 %, `fdo51916` 74.0 → 79.2 %.
  - Read and text stay 67/67.
- **Every reference RTF (557), and the 8 other samples whose layout the sweep
  showed changing:**
  - No check newly fails, and no sample gained a failed check group.
  - Also fixed, outside the cluster:
    - `layout/data/hidden-para-separator.docx`, `layout/data/tdf164903.docx`,
      and `tdf131728.docx` in htmlexport, ooxmlexport and uiwriter: the body
      join.
    - `rtfexport/data/fdo48193.rtf`: the PAGE field, text check.
  - `all_gaps_word.rtf` (79.75 % with the style reset alone), `cp1000018.rtf`
    (76.2 % without the line above its note) and `fdo66040.rtf` (79.3 % with
    the right font) went below 80 % at intermediate steps. All three pass the
    pixel check again (`fdo66040` still fails its text check, as at baseline).
- **Severity that rose slightly on samples still failing, measured against
  the baseline:**
  - `posh-posv` 727.4 → 731.1 (58.7 → 56.9 %) and `posh-leftright` 727.1 →
    730.0 (60.2 → 58.7 %). The text in their fit-to-text boxes is now Times
    New Roman, as in LibreOffice, but the boxes keep their stated height.
  - `tdf158586_lostFrame` 772.2 → 774.1 (42.2 % either way) and `165483`
    765.4 → 766.0 (68.8 → 65.6 %). LibreOffice 24.2 sets `165483`'s text in
    the table style's double spacing, the bug the sample is named for.
  - `fdo47107` 326.0 → 326.5 and `listWithLgl` 477.2 → 478.6. Both fail text
    only; the ink-box metric moves with the serif face.
  - `tdf59454`, `cont-section-pagebreak` and `page-break-emptyparas-spltpgpar`
    rose by less than 0.6.
  - Each rise comes from the default font or the opening spacing, both
    measured against soffice. Each sample's remaining difference is listed
    above.
- **Layout sweep** (all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf` samples): 70 changed
  against the baseline, 63 RTF and 7 DOCX. All are in the run above. A first
  version of the page-top rule applied to every format moved
  `tdf78902.docx` (pass → 36 %) and `tdf106153.docx` (pass → 67 %), so it is
  limited to RTF shapes. With it limited, the sweep shows no other change.
- **Unit tests:**
  - `dotnet test Broiler.Documents.slnx`: 5,847 tests, 5,844 passed,
    3 skipped, 0 failed.
  - The Release build has no warnings.

## Tests

`RtfIssue249Tests` (Rtf.Tests) covers each cause above with a cut-down RTF. Each test
fails on `8a20ca0` and passes with the change, except the guards that check
what stays as it was: a font the table names, no contextual spacing, a note
without a last `\par`, and a style that is not a heading.
`DocumentLayoutNoteTests` and `DocumentLayoutHiddenParagraphTests`
(Pagination.Tests) cover the hidden separator line and the body join.

Some existing tests asserted the formatting writerfilter does not apply, so
they changed with the behaviour, each with the soffice evidence in a comment:

- `RtfStyleSheetTests`: paragraphs that name a style now repeat its formatting,
  as Word writes them, or assert the reset values soffice shows.
  `Paragraph_Style_Formatting_The_Paragraph_Repeats_Is_Applied` is new.
- `RtfIssue236Tests.A_Style_Carries_Its_Indents…`: paragraph "a" is unindented.
- `RtfDefaultFontTests`: a document without `\deff` is no longer "unstated". It
  is Times New Roman. The export test now builds its unstated document as a
  model.
- `RtfPageBreakTests.A_Break_Keeps_The_Rest_Of_The_Paragraph_Style`: an RTF
  break before a paragraph with space above keeps that space.

## Follow-up: a heading style when `\deff` names a font

The cluster's verifier found a gap in the Writer "Heading" mapping. A
`heading N`, Title or Subtitle style with no `\sbasedon` took Heading's
Liberation Sans even when `\deff` names a font in the font table. writerfilter
applies the `\deff` font to all the text as direct formatting. soffice 24.2.7.2
draws `{\rtf1\deff0{\fonttbl{\f0 Courier New;}}{\stylesheet{\s1 heading 1;}}\s1 H\par}`
in Liberation Mono, Courier New's stand-in, at Heading's 14 points. It does the
same when the style names `\f1` Arial. `RtfStyleSheet.ResolveInheritance` now
takes the `\deff` family and gives it to such a style in place of Liberation
Sans. Without a font table, the style keeps Liberation Sans, as `tdf108955.rtf`
needs. No corpus sample has this pattern.
`RtfIssue249LayoutTests.A_Heading_Style_Without_A_Font_Takes_The_Default_Font`
covers it.
