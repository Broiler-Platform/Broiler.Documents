# Office issue #249: docx-text

The docx-text cluster of issue #249 is 75 DOCX samples whose text check
(`broilerdoc dump --as text --cells lines` against LibreOffice 24.2.7.2's
`txt:Text (encoded):UTF8` export) failed at `8a20ca0`. I compared the two
texts for each sample and, where it was not clear what LibreOffice does, cut
the sample down and converted the copies with the same LibreOffice (to text,
PDF and flat ODF). To check a change against more than the 75 samples, I
dumped all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf` corpus samples before and after
it, and compared the 605 samples whose body is one empty paragraph with
LibreOffice's text export of each.

## Causes

| Sample(s) | Cause | Outcome |
| --- | --- | --- |
| `layout/data/inline-textbox-position.docx`, `layout/data/tdf117188.docx`, `ooxmlimport/data/tdf114212.docx` | The body is one paragraph holding a `wp:inline` text box. LibreOffice's text export writes the document's first frame only when the body is one *empty* paragraph (wrtasc.cxx, `SwASCWriter::WriteStream`). A shape anchored as a character is a character of its paragraph, so that paragraph is not empty and nothing is written. The DOCX reader lays inline shapes out as blocks and did not say they were inline. | **Fixed.** `DocumentShape.InlineInSource` (DOCX `wp:inline`, a VML shape without `position:absolute`). The dump does not take the frame branch when the paragraph holds such a shape, or an `IsAsCharacter` one. |
| `layout/data/tdf117028.docx`, `uiwriter/data/tdf99004.docx`, `ooxmlexport/data/fdo78432.docx`, `uiwriter/data/tdf135412.docx`, `ooxmlexport/data/nestedAlternateContent.docx`, `ooxmlexport/data/dml-groupshape-sdt.docx` | The export takes the *first* frame format, whatever it is. A shape without a text box is a drawing with no text of its own, and a picture's frame holds the picture, so either one first means nothing is written. The dump skipped to the first shape with text. A `wpg` group's text boxes are frames the import makes before the group's drawing, so the first of them comes first, even an empty one (`dml-groupshape-sdt.docx`). | **Fixed.** The DOCX reader marks a `wps` shape without `wps:txbx` as `IsCustomShape` (a drawing); VML shapes are left as they were, which no sample needed changed, and gives group members a shared `DocumentShape.GroupNumber`. `DocumentCommands.FirstFrame` follows the rule. |
| `ooxmlimport/data/n773061.docx`, `odfexport/data/tdf151100.docx` | The only frame is a text box in the header or footer. The dump looked only at as-character header frames. | **Fixed.** Header and footer text frames are considered after the body's, in Word's reference order (even header, default header, even footer, default footer, first header, first footer). WordArt there (`StretchesTextToBox`) is a drawing; `rtfexport/data/watermark.rtf` would otherwise have broken. |
| `ooxmlexport/data/tdf112446_frameStyle.docx`, `ooxmlimport/data/tdf112443.docx` (outside the cluster) | That frame is a footer `w:framePr` paragraph holding a `PAGE` field. LibreOffice writes 1 where the first page shows that footer and 0 where it does not (`w:titlePg`); toggling `w:titlePg` in copies swaps the two. | **Fixed.** The number written is the first page's number or nought. |
| `ooxmlexport/data/n779642.docx` | The floating table is the only frame candidate in the body, but the footer's oval drawing is LibreOffice's first frame format: a copy without the oval writes "Urgent". Floating tables are converted after the headers and footers are read, but a page border shape is also a header shape in the model, so header drawings cannot be told apart from it. | Not fixed. |
| `ooxmlexport/data/fdo79008.docx` | The body paragraph holds a comment anchor, which makes it non-empty in LibreOffice. The model has no comments. | Not fixed. |
| `odfexport/data/arabic-zero3/4/5-numbering.docx` | Word 2010 writes `w:numFmt w:val="custom" w:format="001, 002, 003, ..."` in `mc:AlternateContent` with a decimal fallback. LibreOffice reads the choice as ARABIC_ZERO3/4/5. | **Fixed.** `ListNumberFormat.DecimalZero3/4/5`, read from the `mc:Choice`. |
| `ooxmlexport/data/FDO74105.docx`, `ooxmlexport/data/tdf132754.docx`, `ooxmlexport/data/tdf120394.docx` | A level without `w:start` started at 1. ECMA-376 §17.9.25 says nought, and LibreOffice numbers FDO74105 "0. Zero" (`ooxmlexport4.cxx testFDO74105` keeps the start unwritten on export). | **Fixed.** No other corpus sample's text changed. |
| `ooxmlimport/data/numbering-circle.docx`, `ooxmlexport/data/FDO74106.docx` | `decimalEnclosedCircle` and `hebrew1` were drawn in decimal. | **Fixed.** `EnclosedCircle` (①–⑳, ㉑–㉟, ㊱–㊿, decimal from 51 and for 0) and `HebrewNumeral` (no geresh; 15/16 as טו/טז; 500 as תק). Both rules were checked with copies starting at 0, 20, 35, 50, 51 and 11, 15, 120, 499. |
| `ooxmlexport/data/listWithLgl.docx` | `w:isLgl` turned the `decimalZero` level into decimal. LibreOffice keeps zero padding under legal numbering ("Sect 1.01"; a chapter level switched to `decimalZero` reads "01.01"). | **Fixed.** |
| `ooxmlexport/data/cjklist44.docx` | `koreanDigital2` wrote nought as 〇. LibreOffice writes 一零, 二零. | **Fixed.** The existing test that expected 一〇 now expects 一零. |
| `ooxmlexport/data/tdf92470_footnoteRestart.docx`, `ooxmlexport/data/tdf143583_emptyParaAtEndOfFootnote.docx`, `layout/data/footnote-in-2-column-section.fodt` | Footnotes numbered per page. LibreOffice's text export writes the document before laying its pages out: the first such footnote is 1 and every other one 0. A copy without the page break gives 1, 0, 0, 0; a copy numbered per section gives 1, 2, 3, 4. | **Fixed** in the dump. |
| `ooxmlexport/data/line-break-in-ref.docx`, `ooxmlexport/data/tdf171383_refFieldInCell.docx` | A `REF` field's saved result runs over paragraph marks. LibreOffice's flat ODF export shows the import: the reference field is one character where the field ends, showing the whole result as one string, and the paragraphs the result ran over keep their marks but lose its text (an empty paragraph, then "Text1Text2"). | **Fixed.** `DocxDocumentBuilder.CarryReferenceResult` moves the result text on to the next paragraph of the same flow. A result that runs into a table is left alone. A first version joined the paragraphs, which put the text one line too high, and the pixel check failed. |
| `ooxmlexport/data/tdf171299_tableInField.docx` | A `REF` whose result holds a table. LibreOffice 24.2 flattens the table into one paragraph. tdf#171299 fixes this (core `f6e1aa27081c736c3ec45341dab596e529d39f0e`, `986c69622f2905780778d4bbb38e1658ce5162b1`, master 2026-03-19), and the upstream test asserts cell A1 is "ID". | Not changed. This is a reference-defect candidate. I could not confirm the release the fix shipped in, so I did not add a row. |
| `ooxmlexport/data/tdf147724.docx`, `ooxmlexport/data/customxml-two-data-stores.docx`, `ooxmlexport/data/tdf104823.docx` | Content controls bound to custom XML parts and to the extended properties. Only core-property bindings were refreshed. LibreOffice evaluates the path in the store `w:storeItemID` names, or in each store in turn. `name()` compares the store's own prefix, so one path matches stores in two namespaces. | **Fixed.** `DocxCoreBindings` loads core, app and `customXml/itemN.xml` stores (item IDs from `itemPropsN.xml`). A restricted evaluator handles child steps, `[n]`, `name()='…'` and `@a='…'` predicates and a final attribute step. Package XPath is still never passed to an XPath engine. Proofing and bookmark marks inside a control no longer block the refresh. |
| `ooxmlexport/data/sdt-picture-databinding.docx` | LibreOffice 24.2 replaces a bound picture with its base64 text (issue #236 note). | Not changed. |
| `ooxmlexport/data/paragraph-sdt.docx` | A cell-level control bound to the core title: LibreOffice writes "T" + the title. | Not changed. This is a LibreOffice defect. |
| `ooxmlexport/data/tdf121441.docx`, `ooxmlexport/data/tdf105035_framePrB.docx`, `ooxmlimport/data/tdf129912.docx` | `w:customMarkFollows` followed by a `w:sym`: the symbol is the note's label. The reader handled only `w:t`, so the note was numbered and the symbol stayed as body text. | **Fixed** for the first two. tdf129912 still differs on its fifth note: LibreOffice's label there is "b", which the upstream test calls wrong in a TODO. |
| `ooxmlexport/data/tdf118169.docx`, `uiwriter/data/tdf126626.docx`, `ooxmlimport/data/tdf126114.docx` | An ActiveX check box (glyph and caption) and a `FORMDROPDOWN` result are controls in LibreOffice, which the text export does not write. | **Fixed.** `InlineStyle.IsFormControl` marks the runs. They are still drawn, and the dump leaves them out. The pixel checks of tdf118169 and tdf126626 fail as before. |
| `ooxmlexport/data/sdt-before-field.docx`, `ooxmlimport/data/n751017.docx` | `ASK` shows nothing. LibreOffice imports it as an input field without text ("AbarBbarC" saved, "ABbarC" shown). | **Fixed.** It is hidden like `SET`. |
| `ooxmlexport/data/tdf148494.docx` (and part of `Unsupportedtextfields.docx`) | `MACROBUTTON Macro Text` has no saved result. LibreOffice shows the text. | **Fixed.** |
| `ooxmlexport/data/tdf150542.docx` | A `DOCVARIABLE` showed its saved "JUNK VALUE". LibreOffice shows the `w:docVar` value. | **Fixed** for body fields within one paragraph (`DocxDocumentVariables`). `_xHHHH_` escapes are decoded. |
| `ooxmlexport/data/tdf164176.docx` | LibreOffice 24.2 shows a multi-line document variable with raw `_x000d__x000a_`. tdf#164176 fixes this (core `9647aded124c17e97db1bf7bb071136c7f91cb90`, master 2025-02-11). | Not changed. This is a reference-defect candidate. I did not confirm the release, so I added no row. |
| `ooxmlexport/data/tdf164474.docx` | LibreOffice 24.2's PDF has no table at all ("Some text" only). tdf#164474 fixes this (core `647aa0f596e899176a088e47c16124cccdff4fc8`, "do not delete anchor node", master 2024-12-27); the upstream test asserts the table survives import. | Not changed. This is a reference-defect candidate. I added no row, because the release is unconfirmed. |
| `ooxmlimport/data/tdf169173.docx` | A cell-level dropdown control: LibreOffice 24.2 writes "Choose an item." into the next cell. tdf#169173 fixes this (core `b9c04b6e94bf9c320a91bd1528cfb3932b2b2728`, 2025-11-10). | Not changed. This is a reference-defect candidate. |
| `ooxmlexport/data/tdf104394_lostTextbox.docx` | A table whose cells state the `w:framePr` of the framed paragraph after it. LibreOffice 24.2 draws the table inside that frame, so its text export leaves the table out. The reader never lets a table open a frame (issue #242 note), and changing that is a layout change in the docx-layout cluster's area. | Not changed. |
| `ooxmlexport/data/tdf133647.docx`, `ooxmlimport/data/tdf133647.docx`, `ooxmlexport/data/tdf123382.docx`, `tdf123386.docx` ×2, `tdf123355.docx`, `tdf123388.docx`, `ooxmlimport/data/tdf122224.docx`, `tdf122648.docx` | `=` formula fields that LibreOffice works out again as Writer table formulas, with its own range and error rules ("** Expression is faulty **", ranges summed to 0). | Not changed. This needs a Writer table-formula evaluator, which is a separate feature. |
| `ooxmlexport/data/tdf148380_printField.docx`, `tdf148380_modifiedField.docx`, `tdf132475_printField.docx`, `fdo81341.docx` | `SAVEDATE`, `PRINTDATE` and `EDITTIME` are worked out from the document's metadata in UTC, in LibreOffice's locale default date formats ("08/04/2022 07:10:00 AM", "00:05"). | Not changed. This needs metadata date fields and locale formats, which is a separate feature. |
| `ooxmlexport/data/tdf146973_signatureBlock.docx`, `tdf146973_arabicDateField.docx`, `uiwriter/data/tdf164620.docx`, `ww8export/data/tdf79435_legacyInputFields.docx` | `DATE`/`TIME` and statistics fields that LibreOffice recomputes at conversion time. The reference text holds the day the reference was made. | Not changed. These cannot be matched deterministically. |
| `ooxmlexport/data/tdf125038_conditionalText_DOCPROPERTY.docx`, `globalfilter/data/fieldmark_QUOTE_nest.docx` | LibreOffice's text export lists a nested field's result inside the outer field's instruction, which its own PDF does not draw (issue #236 note). | Not changed. |
| `ooxmlexport/data/tdf147861_customField.docx` | LibreOffice writes the custom property's `_x0000_` characters as NULs. | Not changed. |
| `ooxmlexport/data/WordOK.docx`, `ooxmlexport/data/table-start-2-sdt.docx` | LibreOffice output defects ("a%6%)", "IIowa"). | Not changed. |
| `ooxmlexport/data/tdf141966_chapterNumberTortureTest.docx`, `tdf147646_mergedCellNumbering.docx`, `tdf95848_2.docx`, `ooxmlimport/data/tdf154319-ToC_with_s_and_d.docx`, `ooxmlexport/data/tdf170003_bottomSpacing.docx`, `ooxmlexport/data/Unsupportedtextfields.docx` (rest) | Outline numbering through styles, numbering in merged cells, list continuation across lists, a TOC field's page numbers, and an unpaginated page field. | Not investigated past the first diff. Each is a separate numbering or field feature. |

## Verification

- Harness on the 75 cluster samples, plus every sample a text or layout sweep
  showed as changed (100 samples, `--jobs 2`, office-fonts-2):
  - At `8a20ca0`: 75 of 75 text checks failed (81 failed checks).
  - After the change: 34 of the 75 cluster samples pass. 53 samples went from
    failed to passed: the 34, plus 19 outside the cluster
    (`IndexingExport_Shapes.odt`, `footnote-in-2-column-section.fodt`,
    `tdf123898.odt`, `user-field-decl-fly.odt`,
    `activex_control_at_run_end.odt`, `checkbox_control.odt`, `fdo66688.docx`,
    `tdf112446_frameStyle.docx`, `tdf120760_ZOrderInHeader.docx`,
    `tdf133070_testRelativeAnchorHeightFromBottomMarginHasFooter.docx`,
    `tdf137850_compat14ZOrder.docx`, `tdf137850_compat15ZOrder.docx`,
    `tdf166511.docx`, `tdf112443.docx`, `n825305.rtf`, `tdf104936.rtf`,
    `tdf88811.rtf`, `fdo47036.rtf`, `fdo49892.rtf`).
  - Seven more samples now pass their text check but still fail pixel, as they
    did before: `tdf164932.fodt`, `tdf118242.odt`, `tdf149546.docx`,
    `tdf159158_zOrder_zIndexWins.docx`, `tdf105035_framePrB.docx`,
    `tdf118169.docx`, `tdf126626.docx`.
  - No sample's severity rose. Severity fell for `tdf129912.docx` (775.8 to
    655.5) and `Unsupportedtextfields.docx` (324.0 to 308.3).
    `tdf141231_arabicHebrewNumbering.docx` is "partial", as before, because its
    pixel check is skipped under the tdf#160984 reference-defect row.
- Text sweep (`dump --as text --cells lines` of all 3,847 Writer samples):
  every change was to a sample in the list above.
- For the 605 samples whose body is one empty paragraph, compared with
  LibreOffice 24.2's text export of each: 528 matched before and 561 after, and
  none that matched before stopped matching.
- Layout sweep against `/tmp/claude-0/sweep/baseline.tsv`: 24 samples changed,
  all of them numbering, field or binding samples above, all re-run through
  the harness.
- Unit tests: `DocxOfficeIssue249TextTests` (REF results, ASK, MACROBUTTON,
  DOCVARIABLE, drop-down form fields, data bindings, shape flags),
  `DocxNumberingTests`, `ListNumbererTests`, `DocxNoteTests`,
  `DocxActiveXTests`, `DumpTableTextTests` and `DocumentLayoutListTests`. Each
  new test fails without its change.
