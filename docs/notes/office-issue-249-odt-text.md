# Office issue #249, ODT text: what LibreOffice's text export writes

Issue #249's odt-text cluster held 88 ODT and FODT samples whose semantic text
check failed: `broilerdoc dump --as text` did not say what
`soffice --convert-to 'txt:Text (encoded):UTF8'` (LibreOffice 24.2.7.2) says.
I reproduced each sample at commit `8a20ca0` with the conformance harness and
the pinned fonts, and compared the two texts line by line. Where the samples
did not settle a rule, I converted cut-down flat ODF documents with the local
LibreOffice 24.2.7.2 and read its text or its PDF. LibreOffice source names
below are from the `libreoffice-24.2.7.2` tag.

Two facts about LibreOffice's text export (`SwASCWriter`, `wrtasc.cxx` and
`ascatr.cxx`) explain most of the cluster:

- It writes the document **before it is laid out**. Only the first body
  paragraph has been formatted, plus the paragraphs kept with it
  (`fo:keep-with-next`; `SwContentFrame::MakeAll` formats the next frame to
  see whether it fits). A field writes the value it was given when it was
  read (`SwField::ExpandField(true, nullptr)`), which for a page number is
  page 0 of 0 pages.
- It writes **every body text node**, hidden ones included, and leaves out
  only form-element placeholders (`CH_TXT_ATR_FORMELEMENT`). When the body is
  a single empty paragraph, it writes the content of the *first* fly or
  drawing format the document made (`GetSpzFrameFormats()[0]`). If that is a
  drawing, it writes nothing.

## Samples

| Sample | Cause | Outcome |
| --- | --- | --- |
| `rtfexport/data/tdf160976_headerFooter.odt`, `tdf160976_headerFooter3.odt`, `odfexport/data/tdf135338_firstLeftPageFooter.odt`, `ooxmlexport/data/tdf145998_unnecessaryPageStyles.odt` | Body page numbers. LibreOffice writes "1", "0", "0" for one page number in each of three paragraphs. A page number outside the laid-out paragraphs is still page 0. `SvxNumberType::GetNumStr` writes that as "0" in Arabic numerals only, and writes nothing in any other numbering. A field that shows another page (`text:select-page`, `text:page-adjust`) writes nothing, because page 0 has no neighbour (`SwPageNumberFieldType::Expand`). Soffice experiments: "Head\|x 1 y 1\|z 0" when a heading is kept with the next paragraph, and "a\|b 0" for a plain first paragraph. When a frame is anchored to the first paragraph (not as a character), LibreOffice lays out the whole first page to place it: "a \|p1 1\|p2 2\|p2b 0" (`ooxmlexport/data/tdf134951_duplicates.odt`). | **Fixed.** `DocumentCommands.LabelledText` writes `UnlaidNumber` for a page number past the first paragraph and the paragraphs kept with it. Where a frame is anchored to the first paragraph, it keeps every saved number, because the dump has no layout to find where page 1 ends. The same rule fixed four DOCX samples (`tdf143583_emptyParaAtEndOfFootnote.docx`, `tdf92470_footnoteRestart.docx`, `tdf170003_bottomSpacing.docx`) and improved `Unsupportedtextfields.docx` and `uiwriter/data/tdf164620.docx`. |
| `rtfexport/data/tdf160976_headerFooter2.odt`, `odfexport/data/whitespace.odt` | As above. | **Text fixed.** Each still fails its pixel check (788.1 → 728.1, 720.2 → 345.1). |
| `layout/data/footnote-in-2-column-section.fodt` | Footnotes numbered by page (`text:start-numbering-at="page"`) get their number from the layout. Outside the first paragraph they are still 0: "foo1", "bar0". | **Fixed.** Same rule, for a footnote without a mark of its own. |
| `odfexport/data/spellout-numberingtypes.odt` | Page numbers in "1st", "One" and "First" were read as decimal. LibreOffice writes page 0 in those as nothing: "French: , ,". | **Fixed.** `OdtStyles.NumberFormat` reads LibreOffice's numbering identifiers (below), and page fields use it. |
| `odfexport/data/arabic-zero-numbering.odt`, `arabic-zero3-`, `arabic-zero4-`, `arabic-zero5-numbering.odt`, `odfimport/data/tdf167774.odt`, `ooxmlexport/data/numlist-zhcn.odt`, `numlist-zhtw.odt`, `outline-number-types.odt`, `ww8export/data/tdf120629.odt` | ODF `style:num-format` values longer than one character name LibreOffice's numbering types by the identifier its numbering provider gives them (`DefaultNumberingProvider::makeNumberingIdentifier`): "01, 02, 03, ...", "一, 二, 三, ...", "א, י, ק, ...", "①, ②, ③, ...", "Α, Β, ... (gr)", "А, Б, .., Аа, Аб, ... (bg)", and so on. The reader wrote all of them as decimal. | **Fixed.** `ListNumberFormat` gains LibreOffice's types: zero-padded to 3, 4 and 5 digits, circled numbers, Greek numerals, Thai, Persian, Hebrew numerals, circled Hangul, and Bulgarian, Russian, Serbian and Ukrainian Cyrillic in four forms each. `ListNumberer.Format` writes them from LibreOffice's own tables (`defaultnumberingprovider.cxx`, `bullet.h`, `nativenumbersupplier.cxx`). |
| `ooxmlexport/data/tdf156105_percentSuffix.odt`, `odfimport/data/tdf146257.odt` | LibreOffice draws a level's `loext:num-list-format` ("(%)%1%[%]") in place of prefix, levels shown and suffix. A level above that writes no number is left out with the text up to the next number, so "1.(none).1" is "1.1". A level that writes no number keeps only what lies outside its first and last "%": "(%)%1%.%2%[%]" reads "(]" (`SwNumRule::MakeNumString`). | **Fixed.** The ODT reader reads the list format, and `ListNumberer` applies both rules. soffice draws a DOCX `%1(%2)` under an unnumbered first level as "1)". `DocxNumberingTests.A_Level_Numbered_None_Counts_And_Draws_Nothing` expected "(1)", which LibreOffice never wrote, so it now asserts "1)". |
| `odfexport/data/IsLegal.fodt` | `loext:is-legal` was not read. A legal level converts the levels it shows to Arabic numerals but keeps leading zeros (`SvxNumberType::GetNumStr`, `isArabicNumberingType`): "Sect 1.01". The model turned "01" into "1". | **Fixed.** The level is read as legal, and `ListNumberer` keeps the Arabic forms. Fixed too: `ooxmlexport/data/listWithLgl.docx` and `rtfexport/data/listWithLgl.rtf`. For the RTF one, the reader now reads `\levelnfc22` as two-digit decimal. |
| `ooxmlexport/data/tdf106541_noinheritChapterNumbering.odt`, `tdf166850_24.2.7.2.fodt` | A heading outside a list takes the outline numbering only if no style in its chain states a list style other than the outline's (`XMLTextImportHelper::SetStyleAndAttrs`, `lcl_HasListStyle`). A style with `style:list-style-name=""` leaves its headings unnumbered. | **Fixed** for tdf106541. tdf166850's headings are fixed too, but its body `loext:style-ref` is still missing. LibreOffice writes "Error: Reference source not found" for it before layout and draws the heading text in the PDF, and the reader drops the element (821.2 → 761.5). |
| `globalfilter/data/checkbox_form_field.odt`, `dropdown_form_field.odt`, `uiwriter/data/tdf104032.odt`, `tdf95699.odt`, `tdf105625.fodt`, `tiledrendering/data/drop_down_form_field.odt`, `drop_down_form_field2.odt`, `drop_down_form_field_noselection.odt` | A legacy check box or drop-down (`field:fieldmark`) is one placeholder character to LibreOffice, and the text export leaves it out. broilerdoc wrote the box glyph or the chosen entry. | **Fixed.** The run carries `InlineStyle.IsFormElement`. It is still drawn, but the dump leaves it out. |
| `ooxmlexport/data/activex_control_at_run_end.odt`, `ww8export/data/checkbox_control.odt` | A `draw:control` is a drawing object (`SdrUnoObj`), not a text frame, so an empty body does not export its label. | **Fixed.** Controls are marked `IsCustomShape`. |
| `layout/data/ref_page_get_field.fodt` | `text:page-variable-set` shows nothing (`SwRefPageSetField::ExpandImpl`), but its cached "1" was read. | **Fixed.** |
| `uiwriter/data/tdf114973.fodt`, `txtencexport/data/bullets.odt`, `mailmerge/data/empty-db-field.fodt`, `linked-with-condition.odt` | A paragraph hidden by `text:hidden-paragraph`, or by an empty database field, was dropped from the model. LibreOffice keeps it and its text export writes it (`!pNd->IsHidden() \|\| bIncludeHidden`). | **Fixed.** In the body flow, outside tables, the paragraph is read as wholly hidden: every run and the mark are hidden, with no break, page style or list. The layout gives it no room, as before. A paragraph that anchors a drawing is still left out whole. tdf114973 still fails its pixel check (709.8 → 339.9). |
| `odfimport/data/background-image-link.fodt` | The body holds two `office:text` elements. LibreOffice reads both, and the reader read only the first. | **Fixed.** |
| `layout/data/tdf167541.fodt`, `tdf129357.fodt`, `odfimport/data/empty_line_in_text_box.fodt`, `snaptogrid/data/testBoxOffGrid.fodt`, `ooxmlexport/data/tdf139128.odt`, `tdf148720.odt`, `uiwriter/data/tdf128782.odt`, `odfexport/data/tdf166011ee.fodt` | "Draw objects have an automatic style without a parent style" (`XMLTextFrameContext`, #i51726#). A `draw:frame` text box styled like that, or one inside a `draw:g`, is a drawing, so an empty body exports nothing. The dump exported its text. | **Fixed** (tdf167541 and tdf166011ee on text; their pixel checks still fail: 701.0 → 343.0, 800.4 → 740.4). `OdtStyles.IsDrawObjectStyle` marks such frames `IsCustomShape`. |
| `indexing/data/IndexingExport_Shapes.odt` | The first object is a circle, a drawing, so LibreOffice writes nothing. The dump skipped to the text frame after it. | **Fixed.** When the first object (header, then footer, then body) is a drawing, the dump writes nothing. |
| `layout/data/cell-vertical-align-middle-in-fly-at-page.fodt`, `table_cell_overlap.fodt`, `odfimport/data/tdf100033_1.odt`, `layout/data/tdf123898.odt` | Page-anchored frames were never candidates for an empty body. A body with no paragraph at all (tdf100033_1's three page frames) was not taken as one empty paragraph. | **Fixed.** |
| `odfexport/data/user-field-decl-fly.odt` | The styles are read before the body, so a header's frame is made first. LibreOffice writes it: the header frame's "valueFrame". soffice: "HF" over a body frame "BF". | **Fixed.** ODF text frames carry `DocumentShape.IsTextFrame`, and the first one made is written whether it stands in the header or the body. Shapes that say nothing about their kind (DOCX, RTF) keep the old choice. Taking the first shape for them too fixed 20 DOCX/RTF samples but broke 21 others (watermarks, text boxes), so it is not done. |
| `rtfexport/data/fdo82860.odt`, `uiwriter/data/fdo85554.odt`, `tdf128335.odt`, `tdf128603.odt`, `tdf146449.odt` | A custom shape whose automatic style derives from "Frame" holds its words in a text frame. soffice writes "one" for it. It writes nothing for the same shape with no parent style, or with a parent "Other". | **Fixed.** `OdtStyles.IsFrameStyleChild`. |
| `odfexport/data/z-index-header.odt` (and `uiwriter/data/shape-textbox.odt`, `shape-textbox-autosize.odt`, which this would have broken) | LibreOffice keeps a space that follows a frame, even at the end of the paragraph. soffice writes `<p> <frame/> </p>` as " ", so that body paragraph is not empty and no frame is exported. | **Fixed.** `OdtDocumentBuilder.MarkObject` keeps that space in a body paragraph that has no text. The general rule ("a <frame/> b" is "a  b") moved text in 10 samples and made `center_fly_warichu_tab.fodt` worse, so only this case is applied. |
| `layout/data/tdf164932.fodt` | The body's only paragraph holds a frame set as a character. That is a character to LibreOffice, so the paragraph is not empty. | **Text fixed** (676.7 → 666.2; the pixel check fails on the frame's width). |
| `odfimport/data/fdo56272.odt` | The value and unit of a `draw:measure` were read into the heading it hangs from. | **Fixed.** A dimension line, polygon, path, circle, regular polygon and caption no longer leak their text into the paragraph. |
| `uiwriter/data/tdf111969_field.fodt`, `tdf111969_fieldB.fodt` | A fixed `text:time` with no data style shows its `text:time-value` as HH:MM:SS, whatever it caches. soffice: "a15:04:05b" over the cache "cached". | **Fixed.** |
| `odfexport/data/sender-initials.fodt` | LibreOffice wrote `text:sender-initials` in its own namespace in 2013–2017 and still reads that. | **Fixed.** |
| `odfexport/data/tdf71583.odt` | `loext:page-count-range` (pages in the section) was not read. | **Fixed** as `PageFieldKind.SectionPageCount`. |
| `ooxmlexport/data/formula-result.fodt`, `tdf118682.fodt`, `tdf133163.fodt`, `tdf166102.fodt`, `tdf123356.fodt`, `tdf123384.fodt`, `tdf123401.fodt`, `tdf136404.fodt`, `odfexport/data/tdf139126.odt`, `tdf131025_noZerosInTable.odt`, `uiwriter/data/tdf125154.odt`, `tdf141175.odt`, `tdf39828.fodt`, `tdf91801.fodt` | Table formulas. LibreOffice recalculates them on load, before any layout exists, and a range (`<A1:A3>`) is taken through the layout (`SwTableFormula::GetBoxes`, `lcl_GetBoxFrame`), so before layout it is empty and `SUM(<A1:A3>)` writes "0". It also writes "** Expression is faulty **" for errors and unknown functions (`MOD` in 24.2), and evaluates user fields (`aaa+bbb*2`). Synthetic tables gave results I could not explain: every formula, even `<A1>+<B1>`, wrote 0 or the error. | **Declined.** This needs a port of `SwCalc` and of LibreOffice's load-time recalculation order. It is a feature of its own, not a text-export rule. |
| `uiwriter/data/tdf130685.odt`, `ww8export/data/fdo66304-1.odt`, `uiwriter/data/tdf157663_redlineMove.odt` | Tracked changes: list labels of deleted and moved items, and a deleted heading's number (as recorded in the #240 and #242 notes). | **Not fixed.** No change. |
| `odfexport/data/tdf159027.odt` | A cell merged over seven rows: LibreOffice's 24.2 export writes "22" again for each covered row, and the values beside it differ. | **Not fixed.** Not traced. |
| `ooxmlexport/data/ooo67471-2.odt`, `tdf90681-1.odt` | `table:is-sub-table="true"`: LibreOffice merges a sub-table into its parent's grid, so cells come out row by row. Ten corpus samples use it, and the layout would change for all of them. | **Declined.** A separate import feature. |
| `ooxmlexport/data/tdf118242.odt` | The text LibreOffice writes is a frame in the header of the unused "First Page" master page. LibreOffice makes frames for every master page. The model holds only the headers of page styles in use. | **Not fixed.** |
| `layout/data/tdf117187.odt` | A date field that is not fixed: LibreOffice writes the day of the conversion ("15. November 2026"). | **Declined.** It cannot match a reference made on another day. |
| `layout/data/fly_hidden_paragraph.fodt` | A hidden paragraph inside a frame, which LibreOffice exports because the body is empty. Hidden paragraphs are kept only in the body flow. Shape text is laid out by another path, so I did not change it. | **Not fixed.** |

## Effect on other samples

- The layout sweep over all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf` samples
  changed 27 samples against `baseline.tsv`. Each was run in the harness.
- To screen the text dump, I dumped every one of the 3,847 samples in process
  with the baseline build and the final build. The dump changed for 98
  samples, and each was run in the harness.
- Fixed outside the cluster: `layout/data/tdf138039.odt`,
  `odfimport/data/tdf107392.odt`, `ooxmlexport/data/listWithLgl.docx`,
  `rtfexport/data/listWithLgl.rtf`,
  `ooxmlexport/data/tdf143583_emptyParaAtEndOfFootnote.docx`,
  `tdf170003_bottomSpacing.docx` and `tdf92470_footnoteRestart.docx`.
- Better, still failing: `layout/data/tdf105481.odt` 782.6 → 767.6,
  `mailmerge/data/linked-labels.odt` 55.2 → 40.2,
  `ooxmlexport/data/Unsupportedtextfields.docx` 324.0 → 294.0,
  `uiwriter/data/tdf164620.docx` 765.3 → 735.3.
- No sample got worse, and none that passed now fails.
  `layout/data/LIBREOFFICE-N4LA0OHZ.rtf` and `odfimport/data/forcepoint101.fodt`
  are fuzzed inputs whose outcome is `crash-regression` at the baseline too.
  `ooxmlexport/data/tdf172169_intraLinespacing.odt` is `partial` at the baseline
  too, because a reference defect skips its checks. I confirmed all three by
  running the harness from the baseline tree.

## Verification

- Harness, cluster list, at `8a20ca0`: 88 failed. After: **58 pass, 30 fail**.
  6 of the 30 now pass their text check and fail on pixels alone.
- Harness, union of the cluster list, every dump-changed sample and every
  layout-changed sample (120 samples): 65 failing samples fixed, 11 better, 0
  worse, 0 newly failing.
- New tests, all failing on `8a20ca0` except one guard
  (`A_Frame_In_The_First_Paragraph_Keeps_The_Saved_Page_Numbers`, which pins
  the refinement):
  - `OdtIssue249OdtTextTests` (Broiler.Documents.Cli.Tests), 32 cases. They
    cover page numbers in and out of the laid-out paragraphs; footnotes by
    page; form fields; controls; page variables; hidden paragraphs; two text
    bodies; draw-object frames; Frame-styled custom shapes; the header frame
    made first; a space after a frame; a frame set as a character; a fixed
    time; loext sender initials; dimension lines; a section page count; an
    emptied list style; numbering types; list formats; legal levels.
  - `ListNumbererIssue249Tests` (Broiler.Documents.Model.Tests), 33 cases.
    The new types are checked at their boundaries (Hebrew 15, 16 and 1000;
    Greek 6 and 1001; Cyrillic past the alphabet; circled 51), along with
    an unnumbered level above and an unnumbered level's own label, and a
    legal level.
  - `RtfListIssue249Tests` (Broiler.Documents.Rtf.Tests): `\levelnfc22`.
- Changed tests, each now asserting what LibreOffice 24.2 does:
  - `OdtIssue238FrameTests.A_Paragraph_Whose_Hidden_Paragraph_Field_Is_True_Is_Left_Out`
    and `OdtIssue234ControlTests.An_Empty_Database_Field_Hides_Its_Paragraph_When_The_Settings_Say_So`:
    the visible text is unchanged, and the hidden paragraph is asserted to be
    wholly hidden in the model.
  - `DocxNumberingTests.A_Level_Numbered_None_Counts_And_Draws_Nothing`: "1)"
    (soffice 24.2 on the same numbering).
- Test projects run on their own: Broiler.Documents.Model.Tests,
  Odt.Tests, Rtf.Tests, Docx.Tests and Cli.Tests, all passing.
  `dotnet test Broiler.Documents.slnx` at the final commit: 5,850 tests,
  5,847 passed, 0 failed, 3 skipped.
