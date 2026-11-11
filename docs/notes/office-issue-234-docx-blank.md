# Office issue #234: DOCX pages one side draws blank

Each sample here had a page that one renderer drew blank and the other inked.
I compared broilerdoc against the LibreOffice 24.2 references (font set
`office-fonts-2`) and counted ink per page at 96 dpi. Where LibreOffice drew
nothing, I also converted the sample with a local LibreOffice 26.2 and read its
flat ODF export to see what LibreOffice's model holds. That checks whether the
blank page is correct behaviour or an import defect that a later release fixed.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlimport/data/tdf121664.docx` | The section asks for line numbering, `<w:lnNumType w:countBy="1"/>`. Nothing read `w:lnNumType`, and the layout had no line numbering. LibreOffice numbers the four empty paragraphs (two to a page) "1, 2" on each page. | Fixed. `PageGeometry.LineNumbering` (new `LineNumbering` record: count-by, start, distance, restart) is read by `DocxSections.ReadLineNumbering`. Numbering is on only for `w:countBy` >= 1. `w:start` is stored one below the first number, which LibreOffice's `fdo66543.docx` (`w:start="1"`, first shown number 2) confirms. `w:restart` defaults to `newPage`, and a missing `w:distance` is LibreOffice's 0.5 cm. `DocumentLayout.NumberLines` sets the number of each body line (lines in tables, headers, footers, notes and hidden sections do not count) in the document's default face, right-aligned at that distance left of the text column. Ink 0/0 against 56/56 became 54/54. `fdo66543.docx` (count by 3) now draws 3, 6, 9 where LibreOffice does. `w:suppressLineNumbers` on a paragraph is not read yet. Tests: `DocxLineNumberingTests`, `DocumentLayoutLineNumberingTests`. |
| `ooxmlexport/data/fdo80523_pptm.docx`, `ooxmlexport/data/fdo80523_sldm.docx` | The body is one `w:object`, an embedded PowerPoint presentation. Its preview `word/media/image1.emf` draws "Title" and "Subtitle" with `EXTTEXTOUTW` records and no other ink. `EmfRasterizer` draws no text, so the preview is blank. | Not fixed. This is the same cause as `fdo79968.docx` below: drawing EMF text is a feature, not a focused fix. |
| `ooxmlexport/data/fdo79968.docx` | Re-checked. Nothing has changed: the EMF preview draws its words with `EXTTEXTOUTW`. | Not fixed (see the issue #232 note on hidden sections, symbol fonts and OLE previews). |
| `ooxmlexport/data/fdo78384.docx`, `ooxmlimport/data/tdf103664.docx` | `w:sym` characters in Wingdings (U+F0E7; U+F020, U+F0FC, U+F0DC, U+F081). The reference font set has no Wingdings. LibreOffice 24.2 draws nothing for them: both reference pages are blank, so the notdef box the issue #232 note describes is not what these references show. broilerdoc draws the graphics stack's fallback "?" for each one, including U+F020, which is a space in Wingdings. A LibreOffice 26.2 that has Wingdings draws ✓➲① for `tdf103664.docx`. | Not fixed. The difference comes from which fonts are installed, not from a LibreOffice defect. The fallback glyph is drawn in Broiler.Graphics, not in this repository. A fix would recode symbol-font text the way `SymbolFontText` recodes Symbol, which needs a Wingdings table and a decision on what to draw when no face has the glyph. |
| `ooxmlexport/data/TextFrameRotation.docx` | A VML `v:rect` with `rotation:-90`, a `v:textbox` with `layout-flow:vertical; mso-layout-flow-alt:bottom-to-top`, and the text "33333 33333 33333". LibreOffice 24.2 and 26.2 both import it as an unrotated Writer frame, `svg:x="-8.389cm"` and 28.9 cm wide, with `writing-mode="bt-lr"`. Its vertical text lines then start left of the page, so nothing is drawn. broilerdoc applies the rotation and draws the three words reading bottom to top in a band 44 pt wide, which is the rotated box. | Not changed. LibreOffice drops the rotation, and 26.2 still does, so this is not a defect a later release fixed (no reference-defect row). broilerdoc's rotated box is where Word positions a VML shape rotated by 90 degrees. |
| `ooxmlimport/data/tdf125038b.docx` | An `IF "1234" = "" "" "phone: 1234"` field whose instruction is split across a paragraph break, with the cached result "phone: 1234". LibreOffice's text export contains the cached result, but the flat ODF export shows the condition it imported: `text:condition="ooow:1234 FORMULA "`. When LibreOffice lays out and recalculates the field, that garbled condition gives the empty true branch, so the PDF is blank. Word's condition is false ("1234" is not ""), so its result is "phone: 1234". | Not changed. broilerdoc draws the field result, as Word evaluates it. LibreOffice 26.2 is still blank, so there is no fixed release to record. |
| `ooxmlexport/data/tdf104115.docx` | A VML group holding one `_x0000_t19` arc with its own `wr ... nfe ... nsxe` path and a 3 pt stroke. LibreOffice 24.2 and 26.2 both import the arc as an empty path (`svg:d="M0 0z"`, 0 x 0 cm, according to the flat ODF export), so they draw nothing. The upstream test for this bug checks only that the export writes `a:custGeom`. broilerdoc draws the stroked arc at the position the group transform gives. | Not changed. The blank page comes from LibreOffice's import, and 26.2 still has it, so it is not a reference defect that can be recorded. |

## Verification

- Ink per page, broilerdoc against LibreOffice 24.2, before and after this change:
  `tdf121664.docx` went from 0/0 to 54/54 against 56/56. The other samples are
  unchanged: `TextFrameRotation` 1893 against 0, `fdo79968` 0 against 2007,
  `fdo78384` 1752 against 0, both `fdo80523` samples 0 against 753,
  `tdf125038b` 311 against 0, `tdf103664` 254 against 0, `tdf104115` 72 against 0.
- I spot-checked other corpus samples that number lines. `fdo66543.docx`,
  `tools-line-numbering.docx` and `tdf141231_arabicHebrewNumbering.docx` now
  carry their numbers. `tdf167526.docx` numbers "1, 2" where LibreOffice
  numbers "1, 3". LibreOffice also counts the empty paragraph that anchors a
  floating table, which broilerdoc lays out differently.
- Tests: the Model, Pagination, Docx, Odt, Rtf, Cli, Pdf, Office, Html,
  Markdown and core `Broiler.Documents` test projects all pass.
