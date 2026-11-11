# Office issue #238: ODT pictures, charts, frames and text boxes

These samples were flagged with `odt.image.*` diagnostics or are frames and text
boxes drawn wrongly. I reproduced each one locally on Windows against the
LibreOffice 24.2 reference archive (`--references-dir --accept-reference-mismatch`,
96 dpi). Ink is the count of pixels darker than 200 on the worst page.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/hidden-para-as-char-fly.fodt` | Two paragraphs hold a `text:hidden-paragraph` field (`text:condition="ooow:TRUE"`, `text:is-hidden="true"`): a body paragraph of 23 line breaks and the second paragraph of an as-char frame. The reader had no support for the field, so the line breaks pushed the frame 450 px down the page, and the frame was two lines tall instead of one. | **Fixed.** `OdtReader.HiddenByParagraphField` leaves out a paragraph whose hidden-paragraph fields all hide it, as `SwTextNode::CalcHiddenParaField` decides. A field's condition is evaluated where it can be, and otherwise its stored `text:is-hidden` stands. Fields inside a frame anchored in the paragraph count for the frame's own paragraphs, not the anchor. `EvaluateCondition` now reads a bare `ooow:TRUE` / `ooow:FALSE`. Similarity 12.75% → passing. |
| `ww8export/data/tdf46441-2.odt` | The visible text is in `Frame15`, a text frame anchored in the text box of `Frame6`. `ReadShapeParagraphs` keeps only a shape's paragraphs and tables, so a frame nested in a text box was dropped with its text. Once it was read, it drew a black box: its style comes down from the document's `Frame` style, which states no `fo:border`, and the reader took the default graphic style's `svg:stroke-color` as its line. | **Fixed.** `HoistNestedShapes` lifts every non-as-char shape read in a frame's text out to that frame's anchor, above the frame (z-order), with no body wrap. It is placed against the outer frame's content box: its own offset plus the top of the paragraph it is anchored in. The reader does not lay text out, so that top is estimated from the paragraphs before it (one line each at their largest font, 1.15 em, scaled by line spacing, with their spacing). A frame anchored in the box's first paragraph is placed exactly. A nested frame measured from the page (`horizontal-rel` / `vertical-rel` of `page`) keeps its page position, as LibreOffice places it. A Writer text frame, one whose style descends from `Frame`, now draws only its `fo:border`, as picture frames already did. A drawing-shape text box (a style with no `Frame` parent) keeps its stroke. A frame whose `Frame` parent the file does not define gets Writer's built-in Frame style, with its black hairline. Similarity 1.49% → passing; the three lines of `Frame15` land within a few pixels of LibreOffice's. |
| `uiwriter/data/tdf129655-vtextbox.odt` | A 9.6 × 9 cm `draw:custom-shape` rectangle with four CJK characters. Its style says `draw:auto-grow-height="false"` (LibreOffice's "Resize shape to fit text" is off). `FitsText` is set on every custom shape with text, so the layout shrank the rectangle around its words to a 48 × 30 px box. | **Fixed.** `FitsText` is no longer set when the style says `draw:auto-grow-height="false"`. Similarity 32.8% → passing. The characters still draw as `?` because the reference fonts hold no CJK face. LibreOffice draws nothing visible for them at this size either. |
| `globalfilter/data/document_with_linked_graphic.odt`, `globalfilter/data/graphic_shape.odt` | Both link `../linked_graphic.jpg`, a file outside the package. LibreOffice resolves the link against the document's URL. broilerdoc deliberately does not read files named by document content (ADR 0004). `graphic_shape.odt`'s embedded first picture already draws where LibreOffice draws it. | **Not fixed.** This is the policy question the issue #236 note records. It needs an opt-in resolver like HTML's `--allow-local-images`, reaching the document's parent directory, decided on its own. |
| `layout/data/horizontal_multilevel.odt`, `layout/data/tdf127304.odt` | Bar charts (`Object 3`) with no `ObjectReplacements/Object 3` entry. LibreOffice renders them from `Object 3/content.xml`. | **Not fixed.** In the whole reference archive these two are the only charts without a stored replacement. All 23 others have an SVM replacement, which `SvmRasterizer` already draws. A minimal renderer is feasible, but not worth it for these two. It would turn a `chart:chart` bar plot area into model shapes rather than a raster: filled rectangles for the bars, rules for the grid and the wall, and small text shapes for the axis labels and the legend. Text shapes keep the page's fonts, which a codec-level rasteriser does not have. Most of its cost is LibreOffice's automatic layout: axis scaling and ticks, rotated category labels, and these samples' three-level categories. The layout also drives the score, because a plot without its labels stays far from the 80% bar. |
| `ooxmlexport/data/tdf128820.fodt` | An as-char `draw:g` holding a frame whose `draw:image` is an empty SVG (`<svg xmlns=…/>`) as `office:binary-data` and carries the paragraph "json". LibreOffice draws the empty picture as nothing and "json" as the graphic's text. broilerdoc cannot decode SVG, so it draws the marked missing-picture box. | **Not fixed.** A fix needs three things: recognising an empty SVG as a blank picture, reading a picture frame's own `draw:image` paragraphs as a text shape, and placing an as-char group's member at its line. That is three changes for one sample. The reader keeps the issue #236 verdict. |
| `layout/data/tdf116501.odt` | A paragraph-anchored picture (`wrap="none"`, 5.56 cm) in the second row of a table, with the cell's text after a `text:soft-page-break`. LibreOffice leaves the picture on page 1, where its anchor starts, hanging past the body's bottom edge. The row's text continues on page 2. broilerdoc keeps the row whole and moves it, picture and all, to page 2, and everything after it moves down a page: 3 pages against 2. | **Not fixed.** It is a pagination change: splitting a row inside a cell's anchor paragraph, and letting an object that does not follow the text flow overflow the page. It belongs with the table-flow work, not the picture reader. The reader's output is right. |

## Verification

- Feature corpus run, pixel axis, before and after (204 samples): every ODT/FODT in
  the reference archive with a `draw:text-box`, a nested drawing in a text box,
  or a `text:hidden-paragraph`, plus the samples above. Rows listed in
  `tests/office/extras-reference-defects.json` are left out. Passing went from
  146 to 152, and none regressed. Newly passing besides `hidden-para-as-char-fly`,
  `tdf46441-2` and `tdf129655-vtextbox`:
  `hidden-para-follow-frame.fodt` (78.8%, 2 vs 1 pages), `nested-text-frames.odt`
  (61.1%) and `IndexingExport_Shapes.odt` (53.4%). Better without passing:
  `floattable-nested.odt` 79.5 → 90.4%, `tdf114973.fodt` 44.1 → 76.2%.
- An intermediate version cleared the outline of every frame without `fo:border`.
  That regressed `tdf136620.odt` (a drawing-shape text box), the two
  `redlineFrame_at_char_start_*.fodt` (no `Frame` style in the file) and
  `stable-at-page-anchored-fly-position.odt` (a top border only). The rule above
  (frames descending from `Frame`, with no side border stated, and Writer's
  built-in border for an undefined `Frame`) keeps all four passing.
- Custom-shape run (88 more samples whose styles say `draw:auto-grow-height="false"`):
  passing went from 61 to 75, and none regressed. Newly passing: `tdf137185`,
  `tdf137819`, `tdf138951`, `tdf128737`, `lorem_hyperlink`, `fdo82860`,
  `fdo82191`, `fdo85554`, `shape-textbox`, `tdf131124_Fontwork_Selection`,
  `tdf133490`, `tdf137964`, `tdf145321` and `tdf146190`. Better without passing:
  `shape-textbox-vertadjust` 22.4 → 59.1%, `tdf152142` and
  `tdf152142-donotmirror` 58 → 65%, `tdf107776` 56.0 → 62.9%.
- `OdtIssue238FrameTests` (Broiler.Documents.Odt.Tests) covers:
  - hidden-paragraph fields: a true condition, a false one, and a stored
    `is-hidden` for a condition the reader cannot evaluate; a paragraph with one
    showing and one hiding field is kept; a field in a frame hides the frame's
    paragraph and not its anchor;
  - a nested frame lifted inside its frame's padding, above it and with no
    wrap; one anchored below an empty 20 pt paragraph; one measured from the
    page;
  - a `Frame`-descended text frame with no border draws no line, while a
    drawing-shape text box keeps its stroke; an undefined `Frame` parent gives
    the black hairline;
  - `draw:auto-grow-height="false"` clears `FitsText`.
- Projects run, all passing: Broiler.Documents.Odt.Tests (682),
  Broiler.Documents.Tests (326), Broiler.Documents.Model.Tests (343),
  Broiler.Documents.Pagination.Tests (433), Broiler.Documents.Docx.Tests (989),
  Broiler.Documents.Cli.Tests (346). The solution builds with no warnings.
