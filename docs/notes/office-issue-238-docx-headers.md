# Office issue #238: DOCX near-blank pages, headers and vertical text frames

The DOCX samples of issue #238 where one renderer drew a page blank and the
other inked it, and four pages whose only content is a text box or a footer
frame. I rendered each one locally against the LibreOffice 24.2 reference PDFs
(`office-references-v4`), with the reference fonts (`--font-dir fonts/0`), at
96 dpi. I scored each render as the number of ink pixels (darker than 50%)
that do not match the reference, with a 2 px tolerance. To check for
regressions, I rendered every corpus sample that uses a changed feature with
the build before and after the change. Four samples were analysed in the
[issue #234 note](office-issue-234-docx-blank.md) and the
[issue #236 note](office-issue-236-docx-blank.md). I re-checked the blocker
recorded there for each one.

The `docx.header.even` and `docx.header.first` diagnostics on these samples
do not cause the differences. They report Word's rule, which broilerdoc
follows: an even-page header counts only with `w:evenAndOddHeaders`, and a
first-page header only with `w:titlePg`.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/fdo69636.docx`, `ooxmlimport/data/btlr-frame-vml.docx`, `ooxmlexport/data/tbrl-frame-vml.docx` | A text box 57 points wide and 385 tall whose text runs up its side or down it. DrawingML states this as `a:bodyPr vert="vert270"` (or `vert`, `eaVert`). VML states it as `v:textbox style="layout-flow:vertical"`, with `mso-layout-flow-alt:bottom-to-top` for upward text. broilerdoc read neither. It set the label as horizontal text and broke it after "Vertica". LibreOffice imports these as the frame's `WritingMode` (BT_LR or TB_RL), which the upstream tests check. | Fixed. `DocxReader` reads both forms into a new `DocumentShape.TextFlow` (`TopToBottom`, `BottomToTop`). `DocumentLayout` sets the text in the box turned about its centre (`LayoutShape.TextFrame`): lines as long as the box is tall, with the padding and the vertical anchor applied to that turned box. `DocumentPagePainter` turns the text a quarter onto the box, clockwise for downward text and anticlockwise for upward text. The fill and the outline are not turned. A box fitted to its text (`spAutoFit`, `mso-fit-shape-to-text`) keeps its height and takes the width its turned lines need. Mismatch: `fdo69636` 1108 to 0. `btlr-frame-vml` went from 1083 to 990 and `tbrl-frame-vml` from 1087 to 969. Both VML samples now pass the pixel check. They are still 15 px off across the box: LibreOffice's VML import sets the text against the top of the turned box and ignores `v-text-anchor:middle`, but the DrawingML twin `fdo69636` centres it as `anchor="ctr"` says. I left `v-text-anchor` as it is because only the vertical VML case shows this. |
| `ooxmlexport/data/tdf112446_frameStyle.docx` | The page's only ink is a `PAGE` field in a footer paragraph framed at `x="3742" y="4745"` against the page. The reader ignored `w:framePr` in headers and footers, so the number was a line of the footer band, 80 points too high. The paragraph's style also states `xAlign="right"`, and the reader let that alignment beat the paragraph's own `x`. LibreOffice's test for the bug requires `HoriOrient NONE`, meaning the frame is placed at `x`. | Fixed. A header or footer frame whose `w:vAnchor` is `page` or `margin` is now a shape of its band, read by `DocxFrameAccumulator` and placed on the page. A frame that hangs from its paragraph (`vAnchor="text"`, the usual page number at the band's right) stays a paragraph, as before. A frame's own `x` or `y` now replaces the `xAlign` or `yAlign` its style gives. The number is where LibreOffice draws it (mismatch 27 to 4), and the page passes the pixel check. One text check remains: LibreOffice's text export lists the frame's "1", but broilerdoc's text dump lists the body only. |
| `ooxmlexport/data/fdo78384.docx`, `ooxmlimport/data/tdf103664.docx` | `w:sym` characters in Wingdings, a font that is not in the reference font set. With `office-references-v4`, LibreOffice falls back to Liberation Serif, and the PDF draws glyph 1 of the embedded subset, with the original character only as `ActualText`. That glyph has no ink: a local pdftoppm at 300 dpi paints nothing either. So the reference page really is blank. The DejaVu Serif notdef box that the issue #236 note describes came from the earlier font set. broilerdoc draws the graphics stack's fallback "?". | Not changed. LibreOffice draws the fallback face's missing glyph, which in Liberation Serif is empty. Matching that means drawing a face's `.notdef` instead of "?", which is a Broiler.Graphics change. Dropping the characters in this repository would also hide content that Word, and a LibreOffice that has Wingdings, draw as ✓➲①. |
| `ooxmlimport/data/tdf125038b.docx` | An `IF` field whose cached result is "phone: 1234". LibreOffice's import garbles its condition and recalculates the field to the empty branch (issue #234 note). | Not changed. LibreOffice 26.2 is still blank, so there is no fixed release that a reference-defect row could record. broilerdoc draws the result, as Word evaluates it. |
| `ooxmlexport/data/tdf104115.docx` | A VML `_x0000_t19` arc, which LibreOffice 24.2 and 26.2 import as an empty path (issue #234 note). | Not changed, for the same reason. |

## Verification

- The eight samples, mismatch before and after: `fdo69636` 1108 to 0,
  `btlr-frame-vml` 1083 to 990, `tbrl-frame-vml` 1087 to 969,
  `tdf112446_frameStyle` 27 to 4. These four now pass the pixel check.
  `fdo78384` (1752), `tdf103664` (254), `tdf125038b` (207) and `tdf104115`
  (70) are unchanged against blank references.
- Regression sweeps, before and after, scored against the references:
  - All 20 DOCX samples with vertical text in a text box (`vert`, `vert270`,
    `eaVert`, `layout-flow:vertical`). 10 are closer to the reference and none
    is further away. Besides the three above, `TextFrameRotation.docx` and
    `tdf103573.docx` go to 0 (from 1559 and 1781).
    `tdf127825.docx` and `tdf124754.docx` improve from 2058 to 1550 and from
    2783 to 1951: their fitted boxes now take their text's width.
    `btlr-textbox.docx` and `tbrl-textbox.docx` improve from 1107 to 622 and
    from 1331 to 922. `tbrl-textbox` is still too short, because its height
    is a `wp14:pctHeight` of the margin, which broilerdoc does not read.
    `tdf149551_mongolianVert.docx` (`mongolianVert`) is not turned.
  - All 36 DOCX samples with a `w:framePr` in a header or footer, whether
    stated directly or through a style. Four are closer to the reference and
    none is further away: `testHeaderImageAlignment.docx` 3733 to 164,
    `tdf159207_footerFramePrBorder.docx` 4549 to 985, `tdf148671.docx` 231 to
    227, and `tdf112446_frameStyle.docx`. The two samples whose style gives an
    `xAlign` that the paragraph's own `x` overrides are both in this set.
- Tests: `DocxOfficeIssue238Tests` (reading `vert` and `layout-flow`, header
  frames placed on the page or hanging from their paragraph, a frame's own `x`
  over its style's `xAlign`) and `DocumentLayoutIssue238Tests` (turned text
  laid out along the box and drawn inside a quarter turn, a fitted box taking
  its turned text's width).
- Not covered: the PDF writer (`PdfPageLayout`) does not read `TextFlow`, so
  it does not turn the text. A linked text-frame chain lays out
  its overflow in the unturned box. A turned box that does not fit to its
  text does not grow.
