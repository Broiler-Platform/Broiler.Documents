# Office issue #249: ODT pixel similarity (odt-pixel cluster)

The cluster is 145 ODT, OTT and FODT samples that broilerdoc pages like
LibreOffice 24.2.7.2 but draws under 80% alike. I ran each sample I looked at
on Linux with `eng/run-office-conformance.sh` (office-fonts-2, 144 dpi), and
compared it with the local `soffice` 24.2.7.2 (`--convert-to pdf`, then
`pdftotext -bbox` for positions and `pdftoppm` for pictures). To test a rule
beyond the samples I converted cut-down documents with the same `soffice`.
Line positions on the broilerdoc side come from the layout itself, laid out
with the pinned fonts.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `pagelinespacing/data/*.fodt` (18 in the cluster) | **Page line spacing.** The page layouts name a reference style (`style:register-truth-ref-style-name`). Their paragraphs set `style:register-true`. Writer sets every line of such a paragraph in the body on the reference style's register. `SwTextFrame::FillRegister` gives the pitch: the font height (ascent, descent and line gap) times the proportion; at least the stated height; the font height plus leading; or a fixed height. It also gives the first baseline, the font's ascent and line gap below the top of the body (four fifths of a fixed height). `SwTextFormatter::CalcRealHeight` grows each line until its baseline is on the next register line. The body top is the column's body top, so in a section with columns it is the top of the section. broilerdoc read neither attribute. In `multipleParagraphs.fodt` LibreOffice's baselines are exactly 15.85 pt (12 pt × 1.149 × 115%) apart, starting 11.21 pt below the body top. | **Fixed.** `ParagraphStyle.RegisterTrue` and `PageGeometry.LineRegister` (the reference style, resolved) are read. `DocumentLayout.RegisterOf` and `OnRegister` move each register-true body line down so its baseline is on the register. Writer counts the line gap above the baseline, and this layout keeps it below, so the gap is added back when the baseline is compared. A line within 0.1% of a pitch past a register line stays on it. No register applies in headers, footers, notes, frames or table cells. 13 of the 18 now pass; the other five are in the rows below. |
| `pagelinespacing/data/multipleParagraphs.fodt` and the other 13 that open with a titled page; `layout/data/tdf165354_spread-left-page.fodt`, `odfexport/data/tdf132599_spread-left-page.fodt`, `tiledrendering/data/{multiline,search,numrules}.odt` | **Space above a paragraph that opens a page with its own break.** The opening title says `fo:break-before="page"` and has 0.1665 in above it. broilerdoc dropped the space for an opening paragraph with a break, and for every later ODT page break. I tested this in 24.2 (`spacetop.fodt`). The opening paragraph keeps its space above, with or without a break. So does a paragraph that a `fo:break-before` opens on a later page. A paragraph the text merely flows onto a new page loses it. This is `SwFlowFrame::HasParaSpaceAtPages`: the first page, or `IsPageBreak` (a break before, a break after the paragraph above, or a page style of its own). | **Fixed.** `DocumentStyleDefaults.PageBreaksKeepSpaceAbove`, which the ODT reader sets, keeps the space above on any page a paragraph's own break opens, the first included. The DOCX rule (`PageBreakKeepsSpaceAbove` per paragraph) is unchanged. |
| `uiwriter/data/tdf139638.odt` (passed before) | Two causes. (1) An empty paragraph, a 12 pt one, holds only an empty 10.5 pt span. LibreOffice sets the empty line in the span's font. I removed the span in a copy, and the next line moved down 1.9 pt. (2) Its headings open with a Wingdings span. Wingdings is not installed, and the font is declared `style:font-charset="x-symbol"`. LibreOffice substitutes OpenSymbol, and the line keeps the 10.5 pt Times height (12.1 pt). broilerdoc falls back to DejaVu Sans, as `fc-match` does, which makes the line 12.67 pt. Each such heading is 0.6 pt too low. Before, the dropped space above the page-style paragraph (6 pt) hid this. | **(1) Fixed:** `OdtReader.EmptySpansMark` gives an empty paragraph made only of empty spans their formatting. **(2) Not fixed,** so this sample now **fails** (passed before; 13.4%). With the page-break space kept, the drift of four Wingdings lines (+1.5 pt) moves Chapter XV onto page 4. LibreOffice has 0.1 pt left on page 3 for it. Putting the space back would undo eight fixed samples (see Verification). OpenSymbol is not among the pinned fonts, so broilerdoc cannot measure it. |
| `ooxmlexport/data/tdf134649_pageBreak.fodt`, `layout/data/tdf162614.fodt` | tdf134649's page layouts state a size and no margins; tdf162614 states only the top and bottom ones. LibreOffice draws the text 2 cm in from every edge, the margins it gives any new page style (`lcl_DefaultPageFormat`). broilerdoc put the text at the paper's edge. | **Fixed** (0% and 75.6% → pass). A margin a page layout does not state is 2 cm. A margin stated without a unit (`fo:margin-top="0"`, `C4_must_start_on_p1.fodt`) still reads as none, as before. |
| `ooxmlexport/data/tdf138374.odt` | A bent arrow, `draw:polygon`, behind a turned text frame. broilerdoc did not read `draw:polygon`. The first paragraph also has a page break, so (as above) its 12 pt space above was lost and the frame stood 12 pt high. | **Fixed** (79.1% → pass). `draw:polygon` is read through `ReadShape`. Its `draw:points` are taken in its `svg:viewBox` as an outline (`ReadPolygon`), which the painter already fills and strokes. |
| `ww8export/data/tdf70838.odt`, `tiledrendering/data/tdf168318.fodt` | The rectangle's `draw:transform` starts with `skewX (3.7E-17)`, a shear of nothing that Writer writes. broilerdoc only read a transform that starts with `rotate`, so the shapes were drawn upright. In tdf70838 the turned shape is set as a character. Writer sets it in its line by the box round it as turned, and ignores the saved `translate`. | **Fixed.** A `skewX` of under 1e-9 before the rotation is ignored; a real shear still is not modelled. A turned as-character shape takes no saved position. Its line is as tall as its turned box, and its corner is moved so that box starts at the line (`TurnedBox`, `PlaceShapes`). Both pass (66.2% and 75.3% before). |
| `ww8export/data/tdf108448_endNote.odt` (and `ooxmlexport/data/tdf134951_duplicates.odt`, outside the cluster) | The endnotes' `text:notes-configuration` names the master page `Endnote`, which is portrait. The body is landscape. Writer collects the endnotes on pages of that style (`SwEndNoteInfo::GetPageDesc`). broilerdoc used the body's page, so the second page had the wrong shape ("geometry off" at 99.96%). | **Fixed.** `NoteNumbering.EndnotePage` and `EndnoteRunningContent` are read from that master page. The dedicated endnote page starts on them. |
| `pagelinespacing/data/sectionWithColumns.fodt` | A two-column section with one paragraph of 17 lines. LibreOffice balances it 9 and 8. broilerdoc keeps every multi-line paragraph with widow or orphan control out of balancing (`regionUnbalanced`), so all 17 lines stay in column 1. | **Not fixed.** 76.9% → 79.98%, which is the register fix. Balancing that respects widows and orphans is a change to `BalanceColumns` and `Pack`. It would touch every balanced section, and I left it. |
| `pagelinespacing/data/proportionalLineSpacing.fodt` | The paragraph is at 73%. Writer computes the pitch and the line heights in whole twips. The pitch is 201 (276 × 73 / 100, truncated) and each line is 202 (276 − 74). Each line from the third ends 1 twip past a register line, so it moves a whole pitch: 20.1 pt apart. broilerdoc measures both in points, so they are equal and it moves nothing. | **Not fixed** (69.3% → 73.9%). Copying the twip rounding would also need the line heights in twips. Done for the register pitch alone, it moves every 115% line a whole pitch. |
| `pagelinespacing/data/pageColumns.fodt` | Page columns on the register. LibreOffice ends column 1 two lines sooner than broilerdoc. | **Not fixed** (72.9% → 72.8%). Not traced. |
| `pagelinespacing/data/multiPageFloatingTable.fodt` | Register lines in a floating table across pages. Table cells are composed before their place on the page is known, so they are not on the register. | **Not fixed** (57.1%). |
| `pagelinespacing/data/verticalPageStyle.fodt`, `layout/data/tdf107209-vert-ltr.fodt` | Vertical page layout (`style:writing-mode="tb-rl"` / `"tb-lr"`). | **Not fixed.** Laying out vertical pages is a missing feature, as earlier notes record. |
| `cjk/data/tdf167455.odt`, `odfexport/data/charborder.odt` and the 11 other cluster samples with ODT body paragraph borders | ODT body paragraph borders are neither drawn nor given room (`DocumentStyleDefaults.LaysOutBodyParagraphBorders` is DOCX only). I measured Writer 24.2 (`pborder.fodt`). The box stands at the indents. The text is the line plus padding inside it on every side. Neighbours with the same border join, with the space between them inside one box. The background fills the box inside its lines. | **Declined after a trial.** I implemented this by moving the indents in by each side's line and padding, using the DOCX box drawing, and filling the background to the box. `tdf167455` passed (51.7%), `charborder.odt` went from 70.5% to 74.9% and `tdf150606-1-min` from 57.3% to 57.8%. But `ooxmlexport/data/tdf136929_framesOfParagraph.odt` went from pass to 59.8%. Its empty, tracked-deleted heading has a bottom border. broilerdoc sets it on page 1, and LibreOffice does not (a separate redline layout difference). `layout/data/tdf152307.odt` fell from 14.8% to 14.2%. Reverted. |
| `odfexport/data/charborder.odt` ×5 (also in htmlexport, ooxmlexport, rtfexport, ww8export) | Character borders, padding and shadow (`fo:border`/`fo:padding`/`style:shadow` on text properties), plus the paragraph border above. | **Not fixed.** Character borders are a model and layout feature (a run's box widens the run and heightens the line). |
| `ooxmlexport/data/kde302504-1.odt`, `tdf147978_enhancedPath_commandABVW.odt`, `odfimport/data/tdf146597.odt`, `tdf107776.fodt`, `uiwriter/data/tdf169651.odt`, `odfexport/data/textbox-rounded-corners.odt` | Custom shapes whose `draw:enhanced-path` uses arcs (`U`, `T`, `A`, `B`, `W`, `V`, `X`, `Y`). `OdtCustomShapePath` reads straight lines only and keeps the box. kde302504's path is a 2.4° arc, so LibreOffice draws almost nothing, while broilerdoc draws a full red square. | **Not fixed.** Arcs approximated as polygons would cover these. `OdtIssue242OdtTextLayoutTests.A_Path_Of_Curves_Or_Unknown_Values_Keeps_The_Box` asserts that a curved path keeps the box, and that test is not mine to change. |
| `odfexport/data/chapter-number-new-line.odt` | Outline numbering with `text:label-followed-by="newline"`: LibreOffice puts the number on a line of its own. The list model has no label suffix. | **Not fixed** (one sample in the corpus uses it). |
| `mailmerge/data/missing-default-line-color.ott` | Its page-anchored frames stand 108 pt lower than LibreOffice's, and the header ("Seite 1 von 1") is drawn where LibreOffice draws none. | **Not fixed.** Not traced past the master page. |
| `ww8export/data/tdf55427_footnote2endnote.odt` ×2 | Endnotes on the Endnote page style are read now, and the page is unchanged (same geometry). The score is from other content. | **Not fixed** (42.1%, unchanged). |

## Verification

- Final harness run (`eng/run-office-conformance.sh`, office-fonts-2, `--jobs 2`):
  the 145 cluster samples, plus every sample the layout sweep reports changed
  (53), plus every ODT/OTT/FODT whose page layouts leave a margin unstated (221),
  plus every one whose endnotes name a master page (19), plus the two with a
  `skewX` transform: 341 samples. A run of the 145 at 8a20ca0 first
  reproduced the coordinator's baseline: all 145 failed the pixel check.
  - Cluster: 24 of 145 went from failed to passed. 121 still fail, and none has
    a higher severity score than at 8a20ca0. The 24: the 13 pagelinespacing
    samples named above, `tdf134649_pageBreak.fodt`, `tdf162614.fodt`,
    `tdf138374.odt`, `tdf70838.odt`, `tdf168318.fodt`, `tdf108448_endNote.odt`,
    both `spread-left-page` samples and the three `tiledrendering` samples
    `multiline.odt`, `search.odt` and `numrules.odt`.
  - Outside the cluster, three failing samples now pass:
    `odfimport/data/tdf96113.odt`, `ooxmlexport/data/tdf134951_duplicates.odt`
    and `ooxmlexport/data/tdf95495.docx`. Better but still failing:
    `ooxmlexport/data/ooo96040-2.odt` 37.9% → 63.2% and
    `ooxmlexport/data/tdf166850_24.2.7.2.fodt` 36.9% → 55.0%.
  - Passing before, failing now: `uiwriter/data/tdf139638.odt` (see its row).
    Every other sample that passed still passes. The five whose pixel check is
    skipped as a recorded reference defect are still skipped.
  - The page-break rule alone, measured as a variant without it for later
    breaks, accounts for `multiplePageStyles`, `multiplePages`, `removePage`,
    the two `spread-left-page` samples and the three `tiledrendering` samples
    (8 passes), against `tdf139638`'s failure.
- Layout sweep (all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf` samples, page count,
  lines and shapes per page) against the starting commit: 53 samples changed, all
  ODT/FODT except `ooxmlexport/data/tdf95495.docx`. That DOCX sample opens with a
  paragraph whose style asks for `w:pageBreakBefore`, in Word 2010 mode (14),
  so the DOCX reader already marks it as keeping its space above. It now keeps it on the first page too, as Writer does, and it passes
  (67.8% before).
- Unit tests, all failing to build on 8a20ca0 (they use the new model members)
  and passing now:
  - `OdtIssue249OdtPixelTests` (Broiler.Documents.Odt.Tests), 17 cases: the
    register reference style resolved, none for an undefined or absent name,
    `register-true="false"`; the 2 cm default margins, a stated or uniform margin
    winning; page breaks keeping the space above (break before, page style,
    break after); an empty span's height; `draw:polygon`; a rotation after a
    zero shear and not after a real one; a turned as-character shape keeping no
    saved position; the endnote master page.
  - `DocumentLayoutIssue249OdtPixelTests` (Broiler.Documents.Pagination.Tests),
    8 cases: register lines a pitch apart, the first register baseline, no
    register off register-true paragraphs or pages, an exact reference height,
    opening and later page breaks keeping the space above, a turned
    as-character shape in its line, the dedicated endnote page on its own
    paper.
- `dotnet test Broiler.Documents.slnx`: 5,808 tests, none failing.
