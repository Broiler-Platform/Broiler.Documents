# Office conformance issue #249: DOCX headers and footers

The 46 DOCX samples of [issue 249](https://github.com/Broiler-Platform/Broiler.Documents/issues/249)
whose reader diagnostics are `docx.header.even` or `docx.header.first`, all
with LibreOffice's page count. The two diagnostics only say that a section names
an even or first-page header the settings do not switch on, so the cluster is
really "DOCX samples with running content": the causes found are in the text
export of empty bodies, in where header and footer shapes stand, and in two
settings. Measured with `eng/run-office-conformance.sh --font-set
office-fonts-2 --jobs 2` against LibreOffice 24.2.7.2 on the same machine,
before (`8a20ca0`) and after. Where the question was what Writer does, I
converted the sample or a cut-down copy with the local 24.2.7.2 and read the
word boxes with `pdftotext -bbox` or the pixels of `pdftoppm -r 72`.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/tdf120760_ZOrderInHeader.docx`, `tdf166511.docx`, `tdf133070_testRelativeAnchorHeightFromBottomMarginHasFooter.docx` | The body is one empty paragraph; the only text is a text box in the default header or footer. LibreOffice's text export then writes its first frame (below); broilerdoc looked only at as-character header frames. | Fixed: pass (475.8, 655.7, 25.6 before). `tdf120760` and `tdf166511` also needed the header shape placement below. |
| `ooxmlexport/data/fdo66688.docx`, `tdf137850_compat14ZOrder.docx`, `tdf137850_compat15ZOrder.docx` | One empty body paragraph with a rectangle or arrow without text first and a text box after it. LibreOffice exports nothing: only the first frame counts, and a drawing has no text of its own to export. broilerdoc skipped to the first shape with text. | Fixed: pass. |
| `ooxmlexport/data/tdf112446_frameStyle.docx`, `ooxmlimport/data/tdf112443.docx` | The first frame is a `w:framePr` paragraph in the default footer holding a `PAGE` field. LibreOffice writes the number of the page that draws it: "1", or "0" in `tdf112443`, whose `w:titlePg` gives page one a footer of its own, so the default footer's frame is never formatted (a cut-down copy of `tdf112446` with `w:titlePg` added also writes "0"). broilerdoc wrote the saved field text, "2" in `tdf112443`. | Fixed: pass. |
| `ooxmlexport/data/fdo83057.docx` | The header's page number box is placed 72.6 points below its header paragraph; broilerdoc measured that from the page's top edge, 36 points too high. A picture whose blip has no relationship at all (the sample's was stripped) draws broilerdoc's missing-picture box, where LibreOffice draws nothing. | Fixed: pass (795.2 → 218.6). The empty-blip box is left: only six corpus files have such a blip, and two of them pass with it. |
| `ooxmlexport/data/fdo80895.docx`, `uiwriter/data/tdf92648.docx` | Footer shapes placed against their footer paragraph: broilerdoc put the paragraph at the bottom margin, LibreOffice stacks a footer's paragraphs up from the footer distance, so a one-line footer's paragraph is a line above that. fdo80895's oval, 8.55 points above its paragraph, is 784 points down the page in LibreOffice and was 763 here. | Fixed: pass (746.8, 742.1 before). |
| `ooxmlexport/data/tdf119201.docx` | A rectangle whose `wp:docPr` says `hidden="1"`; LibreOffice imports it invisible and still wraps the text around it (cut-down copies, and `tdf169802_hidden_shape.docx`). broilerdoc drew it. | Fixed: pass (691.9 → 109.1). |
| `ooxmlexport/data/tdf166141_linkedStyles2.docx` | `w:linkStyles` with no `w:attachedTemplate`: LibreOffice updates the styles from Word's defaults, 10 points below every paragraph and 115 % lines, under what `w:docDefaults` and the styles state (below). broilerdoc ignored the setting. | Fixed: pass (707.2 → 228.4). |
| `ooxmlexport/data/tdf166141_linkedStyles.docx` | The same with a `w:attachedTemplate` that is not in the package. LibreOffice 24.2 applies Word's defaults here too; the fix of tdf#166141 leaves the styles alone when the template does not exist, and broilerdoc draws that. | Reference defect row `tdf166141-link-styles-missing-template` (fixed in 26.2); the pixel check is skipped, text passes. |
| `ooxmlexport/data/tdf149546.docx` | The text failure was the first-frame export (now passes). The pixel side: an inline (`wp:inline`) drawing group in the footer after two tabs, a triangle and a text box. broilerdoc draws the group at the paragraph's start and the triangle as a rectangle. | Improved: text passes, 831.2 → 816.2. Left: inline groups placed in their line, and the triangle preset. |
| `ooxmlexport/data/floattable-anchorpos.docx` | Header shape placement, as above. | Improved: 730.0 → 665.6. Left: the floating table position. |
| `ooxmlexport/data/FDO73546.docx`, `tdf129805.docx`, `tdf136850.docx`, `tdf90697_complexBreaksHeaders.docx`, `testOuterShdw.docx`, `FDO77715.docx`, `tdf168017.docx`, `fdo78663.docx`, `testWordArtWithinDraingtool.docx` | The header or footer holds a page number in a `w:framePr` paragraph anchored to the text (`w:vAnchor="text"`, `w:xAlign="right"` or `"center"`). LibreOffice makes it a frame narrowed to its text at the band's right, and the next paragraph flows beside it; broilerdoc keeps it a paragraph of the band (a decision of an earlier round), so the band is a line taller and the number stands at the left. | Not fixed. It needs three things together: text-anchored frames in running content (the paragraph offset this round adds is the anchor), a right- or centre-aligned frame that narrows to its text (the layout only narrows a left-aligned one), and page-number fields evaluated in running shapes' text, which the layout does only for band paragraphs. 16 passing samples have such frames too. |
| `ooxmlexport/data/fdo78663.docx`, `testWordArtWithinDraingtool.docx` (text) | A text box holding only a VML WordArt string: LibreOffice's first frame holds a drawing, so it exports nothing; broilerdoc writes the WordArt string as the box's text (issue 242). | Not fixed (as in issue 242). |
| `ooxmlexport/data/fdo66781.docx` | A bullet level with an empty `w:lvlText`; LibreOffice's text export writes "    • " before each item, broilerdoc writes no label. Writing the default bullet for an empty level broke `tdf83309.docx` and `fdo87488.docx`, whose empty Symbol levels LibreOffice exports without a bullet; the difference (a `w:start`, a `w:pStyle` link, four leading spaces) is not understood. | Not fixed; the attempt was dropped. |
| `ooxmlexport/data/tdf66401.docx`, `tdf169101_datePicker.docx`, `ooxmlimport/data/tdf133647_unicode.docx` | Text: combined characters written with ideographic spaces, a date picker's `30 december 1899`, formula fields that LibreOffice evaluates to "** Expression is faulty **". | Not fixed: three field and character features of their own. |
| `ooxmlexport/data/tdf124637_sectionMargin.docx` | Text: an empty numbered paragraph whose bullet broilerdoc writes and LibreOffice does not. | Not investigated further. |
| `ooxmlexport/data/tdf126533_axialAngle.docx`, `tdf126533_axialAngle2.docx` | The page background: broilerdoc paints the VML axial gradient, the 24.2 PDF a flat magenta page (profile similarity 0.998, the colour fails the check). | Not fixed: an earlier round (office extras runs) implemented the gradient after LibreOffice's model; the 24.2 PDF does not show it. |
| `ooxmlexport/data/tdf143384_tableInFoot_negativeMargins.docx` | LibreOffice fits the body on page 1 and leaves page 2 blank; broilerdoc's body lines are taller (`w:lineRule="exact"` paragraphs with `w:position`), so a heading reaches page 2. | Not fixed. |
| `uiwriter/data/tdf157131.docx`, `ooxmlexport/data/tdf153964_firstIndentAfterBreak14.docx`, `tdf170602_exportContext.docx`, `2129393649.docx`, `tdf105688.docx`, `fdo78420.docx`, `tdf167527_title_letters_cut_from_below.docx`, `tdf103001.docx` | Known from issues 236 to 242 (SVG picture size, borders, STYLEREF, frames, floating header tables, WMF). | Not changed beyond the numbers below. |
| `layout/data/tdf134685.docx`, `tdf122878.docx`, `ooxmlimport/data/tdf128076.docx`, `ooxmlexport/data/fdo80997.docx`, `tdf89731.docx` | Pagination of a table across pages, a footer table 4 points low, a `DOCVARIABLE` field spanning two paragraphs, a Symbol bullet LibreOffice does not draw, page 6 drift. | Not investigated beyond a look at the pages; each is its own cause. |

## The first frame of an empty body

`SwASCWriter::WriteStream` writes the body's paragraphs, with one exception:
when the whole body is one empty paragraph and the document has frames, it
writes the content of `GetSpzFrameFormats()[0]` and nothing else; if that
format is a drawing (no content node), it writes nothing. `dump --as text`
(`DocumentCommands.LabelledText`) did this for a floating table and for the
first body or as-character header shape that had text. It now follows the
format list:

- The first floating table still comes first (issue 175).
- Otherwise the first shape decides: the body's, then the headers' and
  footers' in the order a DOCX's closing `w:sectPr` names them and Word writes
  them (even header, default header, even footer, default footer, first-page
  header, first-page footer). Only a text frame or a shape's text box writes
  its text; a custom or grouped drawing, a WordArt string or a picture writes
  nothing, which is why a text watermark still exports nothing
  (`watermark.docx`, issue 240).
- A drawing group's text boxes are frames made while the group is read, before
  the group itself is added, so the group's first text box stands for it
  (`dml-groupshape-runfonts.docx`, `wpg-nested.docx`, `dkvert.docx`: a first
  version without this lost their text). The DOCX reader marks the boxes of a
  `wpg:wgp` with `DocumentShape.DrawingGroup`.
- A `PAGE` field in a header or footer frame shows the page that draws the
  band, or 0 when the one page draws another band.

Across every Writer sample (`.docx`, `.odt`, `.fodt`, `.rtf`, 4,073), 24 text
dumps changed, and all 24 now equal LibreOffice's text export where none did
before (DOCX, ODT and RTF alike).

## Header and footer shapes

`DocxReader.VerticalOffset` turns a running shape's offset into one from the
page's top, because the reader does not know where the band's paragraphs land.
For a header shape placed against its paragraph it used the offset as stated,
from the page's top edge; the estimate now adds the header distance, outside a
table cell. The stated offset is also kept as
`DocumentShape.OffsetFromBandParagraph`, and `DocumentLayout.PlaceRunningShapes`
measures it from the band paragraph as laid out (its first line less its space
above), which is what puts footer shapes right: LibreOffice stands a footer's
paragraphs on the footer distance, so the paragraph is not at the bottom
margin. An inline drawing and one in a table cell keep the estimate: a header's
floating table is laid out in the band's flow here and drawn at the page's top
by LibreOffice (`tdf105688.docx`, which a first version moved 40 points the
wrong way, 419.8 → 875.7).

A `wps:wsp` whose `wp:docPr` says `hidden` is read with `Printable` false:
laid out and wrapped around, not drawn.

## `w:linkStyles`

Converting cut-down copies of `tdf166141_linkedStyles2.docx` with 24.2.7.2:
without `w:linkStyles` the paragraphs have no space between them; with it,
10 points; a Normal stating `w:after="60"` keeps 3 points, a `w:pPrDefault`
stating `w:after="400"` keeps 20, and a Normal without `w:spacing` gets lines
14.5 points apart where 12.65 without the setting (115 %). So the setting adds
Word's template defaults under everything the document states, which
`DocxStyles.UpdatesStylesFromWordDefaults` does as the first `w:pPr` of every
paragraph chain. With a `w:attachedTemplate` the fix of tdf#166141
(`bf326cd72905df985a1f150dbaabf4470fb5aabb`, gerrit change 186855, merged on
master on 2025-06-24 after the libreoffice-25-8 branch was cut and not
cherry-picked, so first in 26.2) makes no change, and broilerdoc follows the
fix; `tdf166141_linkedStyles.docx` is a reference defect row. Seven corpus files
state `w:linkStyles`; the four others with a template pass as before and
`tdf61000.docx` lays out unchanged.

## Verification

Runner, all axes, `--jobs 2`, on the 46 samples, every sample whose text dump
or layout fingerprint changed (the 24 text changes, and 39 layout changes
against the sweep baseline), 88 in all:

- The 46: 13 pass that failed (`fdo83057`, `fdo80895`, `tdf92648`,
  `tdf166141_linkedStyles2`, `tdf119201`, `tdf166511`,
  `tdf120760_ZOrderInHeader`, `tdf137850_compat14ZOrder`,
  `tdf137850_compat15ZOrder`, `fdo66688`,
  `tdf133070_testRelativeAnchorHeightFromBottomMarginHasFooter`,
  `tdf112446_frameStyle`, `tdf112443`); `tdf166141_linkedStyles` is a known
  reference defect (pixel skipped, text passes); 32 still fail. Improved:
  `tdf149546` 831.2 → 816.2 (text now passes), `floattable-anchorpos`
  730.0 → 665.6, `fdo78420` 352.6 → 349.3, `tdf105688` 419.8 → 419.1. Text
  checks 30 → 39 passed, pixel checks 14 → 19 passed (the reference defect
  row's sample, run again after the row was added, is skipped on pixel).
- Outside the cluster, 17 failing samples now pass: `Cool15691_headerTextWrapsAroundImage`
  and `…Largest`, `fdo76803`, `text-copy`, `tdf117028`, `tdf151100`,
  `user-field-decl-fly.odt`, `fdo78432`, `n773061`, `n825305.rtf`,
  `tdf104936.rtf`, `tdf88811.rtf`, `fdo47036.rtf`, `fdo49892.rtf`,
  `tdf135412`, `tdf99004`, `IndexingExport_Shapes.odt`. Improved:
  `floattable-footer-object` 691.7 → 267.3, `floattable-footer-object-nofit`
  486.5 → 266.7, `FDO78292` 733.5 → 699.0, `tdf118242.odt` 700.3 → 685.3,
  `tdf159158_zOrder_zIndexWins` 739.8 → 724.8, `tdf130314` 766.9 → 761.5,
  `tdf81100` 346.1 → 345.1.
- Worse: `tdf156484.docx`, 689.9 → 707.2, same pages. Its header rectangle is
  `hidden="1"`; LibreOffice does not draw it and broilerdoc no longer does
  either, and page 1 now matches. The score is page 3's, which starts a line
  early because broilerdoc does not apply the DOCX widow and orphan default to
  body paragraphs (roadmap); the rectangle drawn there before happened to
  cover part of the difference.
- `tdf38575_fullWidthLine.docx` and `tdf158349_SDTstreamStateStack.docx` (both
  passing at the baseline) changed layout and could not be compared: the
  local LibreOffice fails to load them ("source file could not be loaded"),
  with or without this change, so their pixel checks are skipped; their text
  checks pass or are skipped as before. Their new renders move the header logo
  and the footer box by the header distance and footer layout as above.
- All other passing samples whose layout changed still pass.

Tests: `DumpFirstFrameTextTests` (a drawing ahead of a text box, a group's
text box, a header text box after an empty even header, a watermark, the page
number of a footer frame), `DocxShapeGroupTests.A_Groups_Boxes_Are_Marked_As_One_Drawing_Group`,
`DocxLinkStylesTests`, `DocxHeaderShapePlacementTests` (header-relative
estimate, page-relative and cell offsets, the band paragraph offset, hidden
drawings), `DocumentLayoutBandShapeTests` (a footer shape hung from its laid-out
paragraph).
