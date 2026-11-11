# Office issue #240: ODT pictures, frames and charts

These samples were flagged with `odt.image.*` diagnostics. I reproduced each one
on Linux against the LibreOffice 24.2.7.2 reference archive
(`--references-dir --accept-reference-mismatch`, office-fonts-2, 96 dpi).
Percentages are the runner's rendering similarity on the worst page.
Cut-down experiments ran through the local `soffice` 24.2.7.2.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `uiwriter/data/tdf132911.odt`, `uiwriter/data/tdf135061.odt` | The same table, saved twice. Each cell's only paragraph is empty and anchors 2.35 × 1.56 in pictures (`style:wrap="dynamic"`) in a 3.35 in cell. Room beside each picture is about 0.4 in on each side, less than the 2 cm Writer needs beside a dynamically wrapped object. So Writer moves the empty line below the pictures, and each row is one line taller than its pictures. That carries row 2 onto page 2. broilerdoc left the line at the top of the cell, under the pictures. So row 1 was a line short and row 2 still fitted on page 1. | **Fixed.** `DocumentLayout.CellObjectsLeavingNoRoom` finds the wrapping objects anchored in a cell paragraph that leave a line no room beside them. These are a top-and-bottom wrap, no room at all, or less than `MinimumWrapRoom` for a largest-side wrap. A line that would overlap one of them moves below it, past each object in turn, and the cell grows to hold it. The paragraph's objects stay at the paragraph's top: the new `LayoutLine.AnchorSkip` holds how far the first line moved, and the row places objects from `Top - AnchorSkip`. A line with room beside its object stays where it was. Cell text still does not run beside objects. A soffice cut-down showed the same rule (a cell's `AAA` anchor line under a 2 cm `wrap="none"` picture set 0.2 cm down lands below the picture; a dynamic wrap with 6 cm beside it leaves the line at the top). 43.8% → 100% for both. |
| `odfimport/data/fdo68839.odt` | Three problems. (1) `FrameXXX` is paragraph-anchored with `style:horizontal-pos="center"` relative to the paragraph. broilerdoc applied left, center or right only to page-relative objects and to objects in table cells, so the frame stood at the left margin. LibreOffice ignores `svg:x` for an aligned object and also keeps the upper margin of the `Frame` style (0.201 cm) below the top it is set against. (2) `ObjectXXX` is an empty embedded spreadsheet. Its `ObjectReplacements/Object 1` SVM has only TEXTLANGUAGE, PUSH/POP, LAYOUTMODE and ISECTREGIONCLIPREGION actions, so it draws nothing. broilerdoc drew the marked missing-picture box there. (3) `graphicsXXX` holds two `draw:image`s: a page-sized PNG with a small circle in its corner, then an SVG circle. LibreOffice picks the SVG (`MultiImageImportHelper::solveMultipleImages` ranks vector formats above PNG) and stretches it to the frame. broilerdoc cannot decode SVG, so it draws the PNG, which shrinks to a dot. | **Partly fixed**, 8.2% → 11.3%. (1) `OdtReader.AlignInColumn`: a paragraph- or character-anchored object outside a table, measured from the paragraph (`horizontal-rel` absent, `paragraph` or `paragraph-content`), set `left`, `center` or `right`, sits at `fo:margin-left`, the middle, or `fo:margin-right` from the right edge of the text column. This follows `SwAnchoredObjectPosition::CalcRelPosX`. `vertical-pos="top"` against the paragraph now keeps `fo:margin-top`, and against the line it keeps `fo:margin-bottom` above the line. FrameXXX now lands on LibreOffice's pixels (359, 83). (2) The new `SvmRasterizer.IsBlank` finds a well-formed metafile whose actions only set state. These are colours, fonts, map modes, clip regions, push/pop, layout mode and language. The loader draws such a metafile as an unmarked empty frame. A metafile with text, bitmaps or comments (EMF+) keeps the mark. (3) is **not fixed**. It needs an SVG rasteriser. In the corpus every other frame with an SVG carries a PNG fallback that already draws, except `tdf108124.odt`, whose SVG holds only metadata. So a rasteriser would help this one sample. |
| `layout/data/tdf116501.odt` | As the issue #238 note says: a paragraph-anchored picture in a table row overflows page 1, and Writer leaves it there while the row's text continues on page 2. broilerdoc moves the row whole. | **Not fixed** (53.5% → 55.0%, still 3 pages against 2). This is a table-flow change: splitting a row inside its anchor paragraph and letting an object that does not follow the text flow hang past the body. |
| `ooxmlexport/data/tdf128820.fodt` | An as-char `draw:g` holding a 1 cm frame whose `draw:image` has no picture data, only the paragraph "json". LibreOffice draws "json" at the start of the line as the graphic's text. broilerdoc draws the marked missing-picture box at the member's offset. | **Not fixed** (2.8%). I reconsidered it. It still needs a picture frame's own `draw:image` paragraphs read as a text shape, and an as-char group's member placed at its line. That is two reader changes and a placement rule for one sample. Making the box blank alone leaves an empty page against LibreOffice's text. |
| `layout/data/horizontal_multilevel.odt`, `layout/data/tdf127304.odt` | Bar charts (`Object 3`) with no `ObjectReplacements` entry. LibreOffice renders them from `Object 3/content.xml`. | **Not fixed** (0%). I agree with the issue #238 verdict. A `chart:chart` bar renderer is only worth anything with LibreOffice's automatic axis scaling, ticks, rotated three-level category labels and legend layout. The score is driven by those labels, so a bars-only renderer would not pass either sample. For two samples I judged it not reasonably scoped. |
| `globalfilter/data/document_with_linked_graphic.odt` (0%), `globalfilter/data/graphic_shape.odt` (57.4%) | Both link `../linked_graphic.jpg`, outside the package. | **Not fixed, by policy.** ADR 0004: broilerdoc does not read files that document content names. Unchanged, as the issue #236 and #238 notes record. |

## Verification

All runs used `--accept-reference-mismatch`, office-fonts-2, 96 dpi and two jobs,
with the same binaries before and after apart from the change. Rows listed in
`tests/office/extras-reference-defects.json` are left out by the runner.

- Feature corpus (154 samples), pixel axis, before and after: every ODT/FODT
  with a paragraph- or character-anchored drawing whose own graphic style aligns
  it left, center or right from the paragraph (111). Every ODT/FODT with a
  drawing anchored inside a table cell (18). Every DOCX with a column-relative
  anchor or `mso-position-horizontal-relative:text` shape inside a `w:tc` (31),
  because they share the cell layout. Plus this cluster. Passing went from 115
  to 118, and none regressed. Newly passing: `tdf132911.odt`, `tdf135061.odt`
  and `htmlexport/data/transparent-image.odt` (77.8% → passing, a centered
  picture). Better without changing outcome: `tdf165351.fodt` 90.5 → 96.8%,
  `n822175.odt` 86.9 → 94.7%, `tiledrendering/frame.odt` 87.2 → 94.9%,
  `fdo48557.odt` 90.4 → 98.3%, `fly_hidden_paragraph.fodt` 92.6 → 99.5%, and
  ten more by 0.4–4.1 points. Lower without changing outcome:
  - `drop_vertical.fodt` 95.0 → 89.4%. A `tb-rl` frame is now centered where
    LibreOffice centers it. LibreOffice also widens it for vertical text,
    which broilerdoc does not lay out.
  - `redline_sections.fodt` 96.2 → 93.0%. The frame is inside a tracked
    deletion that LibreOffice hides. broilerdoc already drew it and now centers it.
  - `tdf105417.odt` 94.2 → 91.7%. The frame now keeps its 0.201 cm top margin
    as LibreOffice does. The body text above wraps to 3 lines against
    LibreOffice's 2, so the frame lands one line lower.
  - `tdf116830.odt` 99.4 → 97.2% and `tdf40260.odt` 73.7 → 73.1%: small shifts.
  - `tdf125518.odt` 99.7 → 97.6%. It already failed on page count, 3 against 2.
    The centered chart sits as LibreOffice places it, but its stored
    replacement differs from LibreOffice's live chart.
- Wider frame corpus (144 more ODT/FODT): every other sample with a `left`,
  `center`, `right` or `top` position anywhere in its styles and a paragraph-
  or character-anchored drawing. Passing 115 → 115, and no similarity changed.
- Metafile corpus (50 samples): every ODT whose package holds an SVM, every
  FODT with an `image/x-vclgraphic` payload, the two chart samples and the two
  linked-graphic samples. Passing 31 → 31. Only `fdo68839.odt` changed among
  the metafile samples (8.2 → 11.3%). The other changes are the frame-alignment
  ones above.
- An intermediate version moved the paragraph's objects down with the moved
  line, because the row took the paragraph's top from its first line. That
  dropped tdf132911 to 31.7%. `AnchorSkip` keeps the objects at the paragraph's top.
- Tests:
  - `OdtIssue240OdtImagesTests` (Broiler.Documents.Odt.Tests) covers:
    - left, center and right frames ignoring `svg:x`, with their side margins;
    - `from-left` keeping its offset;
    - the top margin against the paragraph and the bottom margin above the line;
    - a centered frame in a table cell left to the layout;
    - a blank object replacement giving an unmarked empty frame.
  - `SvmRasterizerIssue240OdtImagesTests` (Broiler.Documents.Tests): state-only
    metafiles are blank; drawing, comment (EMF+), truncated and foreign streams
    are not.
  - `DocumentLayoutIssue240OdtImagesTests` (Broiler.Documents.Pagination.Tests)
    covers:
    - a dynamic-wrap picture with too little room moving its line and growing
      the cell, while the picture stays at the paragraph top;
    - a picture with room leaving the line in place;
    - two stacked top-and-bottom pictures moving the line past both.
  - Updated to the Writer behaviour above:
    - `DocumentLayoutCellShapeTests`: a top-and-bottom object over its anchor's
      line now moves that line below it.
    - `OdtIssue236ImageTests.A_Metafile_That_Draws_Nothing_Understood_Keeps_The_Format_Mark`:
      its metafile now carries the TEXT action its comment describes. A
      fill-colour-only metafile is blank.
- Projects run, all passing:
  - Broiler.Documents.Odt.Tests (702)
  - Broiler.Documents.Tests (332)
  - Broiler.Documents.Pagination.Tests (454)
  - Broiler.Documents.Docx.Tests (1029)
  - Broiler.Documents.Rtf.Tests (653)
  - Broiler.Documents.Pdf.Tests (749)
  - Broiler.Documents.Cli.Tests (349)

  The solution builds with no warnings from this change. The one analyzer
  warning in a full rebuild, xUnit2030 in `DocumentLayoutIssue236Tests.cs`, was
  already there before it.
