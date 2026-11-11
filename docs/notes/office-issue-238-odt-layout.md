# Office issue #238: ODT and ODS pagination and text layout

I reproduced each sample locally on Windows at 96 dpi with `broilerdoc-office --sample-list`,
the `office-references-v4` archive (LibreOffice 24.2.7.2, `--accept-reference-mismatch`)
and its font directory. Similarity is the runner's pixel score; ink is the count of
pixels darker than 200 on a page. Where a cause needed LibreOffice's behaviour beyond the
reference, I changed a copy of the sample and exported it with a local LibreOffice
26.2.6.3.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `uiwriter/data/tdf104440.odt` | Two full-width text frames, each anchored to an empty paragraph. Each frame states `svg:height="9.979cm"`, and its text box states a larger `fo:min-height` (14.896 cm and 14.979 cm). `ReadShape` took the larger of the two, so each frame stood 15 cm high. Frame 1 then pushed its anchor's line onto page 2, which drew "Frame1" a second time. Frame 2 and "Second page." took a page each, 4 pages in all. LibreOffice reads the text box's attributes before the frame's into one height (`XMLTextFrameContext_Impl`), so the frame's `svg:height` wins. It stays the least the frame is, and the frame still grows. Writer then lays out 9.979 cm frames: frame 2 moves to page 2 with its paragraph, and "Second page." follows it there (tdf#104440: the second frame's anchor is on page 2). The `odt.styles.unknown` diagnostic (paragraph style `P6` is not defined) has no effect on layout. | **Fixed.** A frame that states `svg:height` takes it as its least height, and a text box's `fo:min-height` sizes the frame only when the frame states no height. 4 → 2 pages, 50.3% → pass. `OdtPageAnchorTests` asserted the old rule with no sample behind it. It now asserts LibreOffice's. |
| `layout/data/tdf137185.odt` | A page-anchored `draw:custom-shape` of `draw:type="diamond"`, 10 × 8 cm, with "Align me!" in it. Its style says `draw:auto-grow-height="false"` and `draw:textarea-vertical-align="middle"`. (1) `FitsText` was set for every custom shape that holds text. That flag shrinks the shape round its words and centres it in the stated box (added for `shape-fill-link.fodt`, whose style states no growth). So the diamond became a small rectangle at the top of its box. (2) The model had no diamond outline. | **Fixed.** (1) A custom shape whose style turns `draw:auto-grow-height` off keeps its stated box. Its text sits in it by `draw:textarea-vertical-align`. ODF takes the attribute as true when it is unstated, so `shape-fill-link.fodt` keeps `FitsText`. (2) `ShapeGeometry.Diamond`: the reader maps `draw:type="diamond"` to it, and `DocumentPagePainter` fills and strokes the four edge midpoints as two triangles. The PDF writer still draws a diamond as its box, as it does a triangle. 7.7% → pass. |
| `layout/data/tdf135035.odt` | Three paragraph-anchored text frames with no `svg:width`, only `fo:min-width="0.041cm"` on the text box: Writer's auto-width frames (`WidthType` MIN). Two hold empty bordered paragraphs. The third holds a line of 75 letters, set 1.97 cm into a 16 cm text column. The reader let such a frame widen to its text only when its style also said `fo:wrap-option="no-wrap"`. These frames were therefore 0.041 cm wide, and the long line was set a letter to a line down the whole page. Writer measures the frame's text on one line no wider than the page's print area (`SwTextFrame::CalcFitToContent`). It then keeps the frame inside the text column (`SwFlyFreeFrame::CheckClip`). The third frame ends at the column's right edge, and its last "a" wraps (tdf#135035: wider than its paragraph). | **Fixed.** A frame with `fo:min-width` and no `svg:width` widens to its text (`GrowsWidthToText`) whatever its wrap option. In `DocumentLayout`, a box that widens is limited to the text column, less its own offset into the column (`GrownWidthLimit`), and its text wraps there. 26.9% → pass. The PDF writer's `BoxWidth` has no page box and does not apply the limit yet. |
| `mailmerge/data/linked-labels.odt` | A label sheet: two page-anchored text frames, 5.85 × 8.27 in, cover a page with no margins, and the body is one empty paragraph. (1) The default graphic style's `svg:stroke-color="#3465a4"` drew a blue edge round both labels. A Writer text frame's edge is its `fo:border`, and LibreOffice draws none. The issue #234 note made the same change for picture frames. (2) The second label's section is linked (`text:section-source`), and the reader warned `odt.block.unsupported` for that element. The section holds the copy LibreOffice saved, and the reader reads it. (3) broilerdoc moves the empty body paragraph below the frames, onto a page 2 of its own. LibreOffice keeps it on page 1. I checked this locally against LibreOffice 26.2 with an explicit wrap of `none`, `parallel` and `run-through`, and with two and three empty paragraphs: each stays on one page. A paragraph that holds text ("X" after the empty one) gets a page 2. The text diff (`<First Name>`) comes from LibreOffice's text export, which writes one label's text; broilerdoc's text projection leaves page-anchored frames out. | **Improved.** (1) A text frame outside a `draw:g` takes its outline only from its border, as LibreOffice does. Three cases the corpus run turned up keep a line. A style that derives from Writer's built-in "Frame" style without the document defining it gets that style's black hairline (`RES_POOLFRM_FRAME`; `OdtStyles` adds it at the bottom of the chain). This applies to `redlineFrame_at_char_start_inside.fodt` and `_outside.fodt`. A frame whose own style turns `draw:stroke` on keeps its stroke: it is a shape's text box saved as a frame (`ww8export/data/tdf136620.odt`). A frame that states only some edges (`fo:border-top`) is outlined with the first edge that draws a line, because the model keeps one outline (`stable-at-page-anchored-fly-position.odt`). (2) `text:section-source` is skipped silently, and the saved copy is read as before. Page 1 now matches the reference at 99.99%, but the check still fails on the page count. **Not fixed:** (3). The empirical rule is that empty paragraphs are not pushed off a page by these frames. I did not trace it in Writer. `SwTextFrame::FormatEmpty` skips its fly check while a paragraph has no height yet, but whether that is the whole cause is not established. A layout rule built on a guess would move every empty paragraph that sits under a frame, in DOCX too. The text projection is unchanged. |
| `ww8export/data/tdf127316_autoEscapement2.odt` | Six paragraphs of 40 pt text, each ending in a run with `style:text-position` `super`/`sub` at 80%, 50% or 20% (automatic escapement). LibreOffice sets those runs smaller and raised or lowered, three lines to a 4.13 in page. The model has no superscript or subscript for ordinary text, so each run is drawn at 40 pt on the baseline. "Base1AutoSuperscript" then wraps, and each paragraph fills a page: 6 pages against 2. | **Not fixed.** Superscript and subscript is an open roadmap item for every codec (`docs/roadmap.md`). It needs a run property in the model, a baseline shift in the line layout, and the raster and PDF painters. Folding only the size into `FontSize` would draw the runs on the baseline and write them back as a plain size. That is not done here; this sample is now named in the roadmap item. |
| `layout/data/tdf54465_ColumnsWithFootnoteDoNotOccupyEntirePage_Old.odt` | Sections of two and three columns that hold footnotes. The settings say `FootnoteInColumnToPageEnd="false"`, so LibreOffice keeps the layout from before tdf#139336. A column section with footnotes then takes the rest of its page, and the document runs to 5 pages (`layout4.cxx` asserts more than one). broilerdoc lays it out as the `_New` sample, on one page. | **Not fixed.** That old layout is a compatibility mode of Writer's section and footnote layout, kept for old documents. It is not a focused fix. The `_New` sibling is the same content without the flag. |
| `layout/data/tdf144450.fodt` | Eleven 40 pt "Noto Sans" paragraphs on a page with a line text grid (`style:layout-grid-mode="line"`, 12 lines, base 0.5555 in, ruby 0.139 in), each with different line spacing, one to a page. LibreOffice 24.2 sets every line on the grid's 0.69 in pitch, with the grid centred in the body, so pages 7 to 11 look the same. The test `layout5.cxx testTdf144450` expects a later rule, where each spacing changes the line height under the grid. broilerdoc has no text grid, so each spacing is honoured as stated. Page 9, with spacing of at least 4 in, puts the second line 4 in down. The archive has no Noto Sans, and the fallback faces wrap the line at a different word. | **Not fixed.** Setting lines on a page's text grid is a layout feature that broilerdoc does not have; the tdf165089 row of the issue #236 ODT layout note records the same gap. The 24.2 reference also predates the rule the sample tests, so it would not be the right target either. |
| `layout/data/tdf107209-vert-rtl.fodt` | As recorded in the issue #236 ODT layout note: vertical right-to-left pages. broilerdoc sets them horizontally on one page. | **Not fixed.** No change since #236. Laying out vertical pages is a feature. |
| `uiwriter/data/fdo114306.odt` | As recorded in the issue #236 ODT layout note: rows tied by a row-spanning cell are kept whole (`RowBox.CanSplit`). A merged block taller than a page therefore runs off it, and a blank page 3 follows. | **Not fixed.** No change since #236. I looked at the split again: placing the rows a merged cell covers in step, each cut at its own line boundary, touches each of `DocumentLayout`'s table-pagination paths (`RowBox` splitting, header repeat, keep-with-next). That is still a table-layout change, not a focused fix. |
| `mailmerge/data/4_v01.ods` | As recorded in the issue #236 ODT layout note: LibreOffice prints the sheet across 3 pages of columns at 10 pt with the page style's header and footer. broilerdoc projects one table squeezed to one page. | **Not fixed.** No change. The ODS reader still reads no page style or cell styles (`OdsReader` is a used-range projection). Paging columns needs both first. |

## Effect on other samples

I ran all 1,480 ODT and FODT samples in the archive that have a reference PDF, before and
after, at 96 dpi (`--sample-list`, every check).

Pixel checks went from 1,132 to 1,150 passing; text checks did not change (1,376).
No check that passed before fails now.

- Now pass (similarity before in brackets): `tdf104440.odt` (50.3%),
  `tdf135035.odt` (26.9%) and `tdf137185.odt` (7.7%). The custom-shape change also
  fixes 15 samples whose shape style turns growth off:
  `indexing/data/IndexingExport_Shapes.odt` (53.4%), `layout/data/tdf137819.fodt` (15.5%),
  `layout/data/tdf138951.odt` (77.4%), `odfimport/data/tdf128737.odt` (76.7%),
  `ooxmlexport/data/lorem_hyperlink.fodt` (76.9%), `rtfexport/data/fdo82860.odt` (9.7%),
  `uiwriter/data/fdo82191.odt` (44.8%), `fdo85554.odt` (44.9%), `shape-textbox.odt` (18.1%),
  `tdf129655-vtextbox.odt` (32.8%), `tdf131124_Fontwork_Selection.odt` (69.6%),
  `tdf133490.odt` (65.2%), `tdf137964.odt` (65.1%), `tdf145321.odt` (56.9%, now 3 pages
  as in LibreOffice) and `tdf146190.odt` (73.2%).
- Better, still failing: `linked-labels.odt` 60.0 → 99.99% (page count),
  `shape-textbox-vertadjust.odt` 22.4 → 59.1%, `tdf152142.fodt` 58.7 → 65.3%,
  `tdf152142-donotmirror.fodt` 58.2 → 64.8%, `odfimport/data/tdf107776.fodt` 56.0 → 62.9%.
- `uiwriter/data/tdf139843.odt` moved from 67.64 to 67.61% (an auto-width frame in it
  now stops at the column). It fails as before, on its page count.
- A first full run without the three frame-outline exceptions above failed four
  samples that had passed: the two `redlineFrame_at_char_start_*.fodt`,
  `tdf136620.odt` and `stable-at-page-anchored-fly-position.odt`. All four pass
  with the exceptions in place.

## Verification

- `OdtIssue238Tests` (Broiler.Documents.Odt.Tests), twelve cases:
  - a frame's `svg:height` overrides a larger text box `fo:min-height`, and the frame
    still grows;
  - a text box's `fo:min-height` alone still sizes the frame;
  - a frame with only `fo:min-width` widens to its text, and one with `svg:width` does not;
  - a diamond custom shape with `draw:auto-grow-height="false"` keeps its box, and one
    that states nothing still fits its text;
  - a text frame takes no outline from the default graphic stroke but keeps its `fo:border`;
  - it takes Writer's hairline under an undefined "Frame" parent, keeps a stroke its own
    style turns on, and is outlined by a single bordered edge;
  - a linked section reads its saved copy without a warning.
- `OdtPageAnchorTests.A_Frames_Height_Overrides_A_Larger_Minimum_Height_And_The_Box_Still_Grows`
  replaces the test that asserted the larger height.
- `DocumentLayoutIssue238Tests` (Broiler.Documents.Pagination.Tests): a diamond paints
  two triangles through its edge midpoints; a widening frame stops at the text column's
  right edge and wraps; a short text keeps the frame as narrow as the text.
- Projects run: Broiler.Documents.Odt.Tests (684), Broiler.Documents.Pagination.Tests
  (436), Broiler.Documents.Model.Tests (343), Broiler.Documents.Ods.Tests (16),
  Broiler.Documents.Docx.Tests (989), Broiler.Documents.Rtf.Tests (645),
  Broiler.Documents.Pdf.Tests (749), Broiler.Documents.Tests (329) and
  Broiler.Documents.Office.Tests (95). All pass.
