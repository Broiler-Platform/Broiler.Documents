# Office issue #236: RTF tab leaders, hairlines, starred character styles and indents

Seven RTF samples, rendered locally with `broilerdoc render --font-dir` against the
pinned LibreOffice 24.2 PDFs (rasterised with `pdftoppm -r 96`). The ink counts are
dark pixels per page at 96 dpi, listed as broilerdoc / LibreOffice.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `rtfimport/data/fdo81033.rtf` | broilerdoc drew a blank page; LibreOffice draws an underscore leader. The paragraph applies a style with `\tqc\tx4536\tqr\tx9072` and then states `\tx3293\tlul\tx9072` itself. The reader added the paragraph's stops to the style's, giving three stops, so the second `\tab` stopped at the centre stop, which has no leader. LibreOffice's `DomainMapper` does not start an RTF paragraph's stops from its style's (the `LN_CT_PPrBase_tabs` handler skips `InitTabStopFromStyle` for RTF), and `rtfimport.cxx testFdo81033` pins two stops. Second, a tab that ends its paragraph was dropped as trailing whitespace, leader and all. | Fixed. A paragraph's first `\tx` now replaces the stops its style gave it. `LineWrapper` keeps trailing tabs, up to the last one that has a leader, at the end of a paragraph and before a forced break. Ink: 0 → 327, against LibreOffice's 384. |
| `rtfimport/data/tdf115153.rtf` | broilerdoc drew a blank page; LibreOffice draws a vertical `\do\dpline` line with `\dplinew0`. The reader and the layout both handled the line. Zero width means a hairline, which the painter drew as a 0.25 pt band. That is a third of a pixel at 96 dpi, and the rasteriser samples pixel centres, so the band covered none and nothing was drawn. LibreOffice draws a hairline one device pixel wide. | Fixed. `DocumentPagePainter` draws a zero-width outline 0.75 pt wide (`HairlineWidth`). That is one pixel at 96 dpi, and any band that wide covers a pixel centre. The render list has no device-pixel unit, so a hairline is thicker than one pixel at higher resolutions. Ink: 0 → 48, against 50. |
| `rtfexport/data/tdf141964_numId0.rtf` | broilerdoc drew `[]` and bare bullets. Writer's export opens each link's result with `{\fldrslt \*\cs12 ...}`. The reader treated `\*` before any keyword except `shpinst`/`background` as an unknown destination and skipped the whole group, link text included. A character style is not a destination, and LibreOffice applies it. The list items also ignored their `\fi-283` hanging indent, because the RTF reader read neither `\fi` nor `\ri`, in a paragraph or in a style. | Mostly fixed. `\*\cs` is now applied like `\cs`, and all five link texts and `[Verbergen]` render. `\fi` and `\ri` are read into `FirstLineIndent`/`RightIndent`, with style inheritance; a based-on style's `\fi0` clears the inherited value. **Not fixed:** "Geschichte" has no bullet. It carries `\ls0` and no `\listtext`, and the RTF reader still does not import list tables (`rtf.list`). Ink: 1533 → 4264, against 4538. |
| `rtfimport/data/groupshape.rtf` | Not a broilerdoc text loss. LibreOffice's text export leaves out shape text, and its PDF sets `foo` and `baz` on one line, so the line diff pairs them differently. In the render, broilerdoc draws the two group children (`shapeType` 202) as page-high text boxes at their stated geometry, which is what Word shows. LibreOffice 24.2 draws each one as a draw text shape that shrinks around its text, centred across the child's width, with its top at the page edge rather than at the stated offset of -585 twips. | Not changed. This is how LibreOffice imports a text box inside a group, and copying it would mean shrinking and moving stated geometry for this one case. `rtfimport.cxx testGroupshape` checks only that there is one group with two children. Ink: 8593 / 367. |
| `rtfexport/data/tdf49073.rtf` | As in #232 and #234: the ruby annotations (`EQ \o\ad(\s\up …)`) are not modelled (`rtf.field.ruby`), and the CJK base text has no glyphs in the pinned fonts. Broiler.Graphics draws its own `?` fallback. LibreOffice's `.notdef` in Liberation Serif has no outline, so the CI raster is blank. A local `pdftoppm` draws boxes for those glyphs. | Not fixed. Ruby is a cross-layer model and line-layout feature, and the fallback glyph belongs to Broiler.Graphics. Ink: 15865 / 10229 locally, 15865 / 0 in CI. |
| `rtfimport/data/tdf59699.rtf` | As in #234: `INCLUDEPICTURE "libreoffice.png" \d` with an empty result. LibreOffice loads the file from beside the document. | Not fixed, by design. A reader gets only the document's bytes (`rtf.image.external`). Ink: 0 / 855. |
| `rtfimport/data/ole-inline.rtf` | As in #232 and #234: the object's `\result` is an Equation.3 preview EMF made only of text records, and `EmfRasterizer` draws no text. | Not fixed. Drawing it needs EMF text output (`EXTCREATEFONTINDIRECTW`/`EXTTEXTOUTW`) with fonts available at decode time, or a WMF reader for the `\nonshppict` fallback. Ink: 0 / 120. |

## Effect on other samples

I rendered every reference RTF (557) and every DOCX/ODT with a tab leader or a
zero-width stroke (186) with the base build and with this change, and compared the
renders that differed by mean absolute grey difference against LibreOffice.

- The leader and hairline changes altered 15 samples. Every one I inspected now draws
  something LibreOffice also draws:
  - the trailing underscore leaders of `tdf137335_whitespaceLineHeight.docx`, of the
    score-sheet form in `tdf66039.docx` and of `missing_newline.odt`;
  - the `\dplinew0` table and footer rules of `fdo94835.rtf`;
  - the line in `tdf122717.docx`.

  The grey difference rises for some of them (`fdo94835.rtf` 1.42 → 1.57,
  `fdo81033.rtf` 0.07 → 0.16), because a line one or two pixels off its reference
  position scores worse than no line at all.
- `\fi`/`\ri` changed 59 RTF samples: 23 moved closer and 9 moved further away. No
  page count changed. The largest regressions are font or position artefacts rather
  than wrong indents:
  - `tdf146489.rtf`: the footnote's `\li340\fi-340` hanging indent now applies and
    its text fits one line in broilerdoc's face, so the footnote area moves.
  - `all_gaps_word.rtf`: the missing "4½ pt" label now renders, as in LibreOffice.
    A pre-existing row-height difference moves the rows below it.

  The largest improvements are `tdf158982.rtf` (17.53 → 17.04) and the `cjklist*.rtf`
  samples (about 6.0 → 5.7).

## tdf164835_nonDummyLineHeight.docx and the pinned serif face

A full-corpus run showed `ooxmlexport/data/tdf164835_nonDummyLineHeight.docx` going
from passed to failed (18.48%; 14.92% locally) on this branch. The paragraphs beside
its image are whitespace only, each a tab to a dot-leader stop that `Normal` states.
The first tab is 72 pt. The base build dropped those trailing tabs, so it drew no
leaders. It also set the first line at the 36 pt paragraph-mark height, which left
room for the fourth paragraph on page 1. That paragraph rendered blank either way,
and the page passed by accident.

LibreOffice 24.2 draws all three leaders. Its first line takes the 72 pt tab's
height: the leader's baseline sits 66.75 pt below the margin, the ascent of DejaVu
Serif at 72 pt. Its fourth paragraph still fits on page 1, a few points above the
bottom margin.

This branch now lays the page out the same way, and then it depends on the face.
- **The cause:** the runs ask for Times New Roman, and the runner aliases that to
  Liberation Serif. The reference font set has no Liberation faces (only DejaVu,
  Carlito and Caladea), so the alias is skipped and broilerdoc draws with its host
  fallback. LibreOffice drew with DejaVu Serif, the only face its PDF embeds. The
  host face is taller, so the fourth paragraph overflows to page 2 by about 9 pt.
- **The evidence:** pinned the way LibreOffice drew it (`--font-file "Times New
  Roman=DejaVuSerif.ttf"`), this branch's render matches the reference. The leaders
  sit at the same positions and lengths, and page 2 holds only the closing line.
  With the runner given a font directory where DejaVu Serif stands in for Liberation
  Serif, all four checks pass (fdo81033, tdf115153, tdf141964_numId0,
  tdf164835_nonDummyLineHeight). Only tdf141964's missing "Geschichte" bullet
  remains as a text difference.

The layout change is kept. Dropping the leaders again would put back the line
heights that differ from LibreOffice. The gap is in the harness's font pinning
(`FontPinning.MetricAliases` assumes Liberation faces the reference set does not
carry), and closing it changes every sample that names Times New Roman, Arial or
Courier New. So it is left for a deliberate run of the whole corpus rather than
folded into this change.

## Tests

- `RtfIssue236Tests` (Rtf.Tests): a paragraph's stops replacing its style's, a
  style's stops kept when the paragraph states none, a `\*\cs` inside a field result,
  and `\fi`/`\ri` in paragraphs and styles.
- `DocumentLayoutIssue236Tests` (Pagination.Tests): a trailing leader tab at
  paragraph end and before a forced break, a trailing plain tab still dropped, and
  the hairline width.

The Rtf, Pagination, Documents, Docx, Odt, Cli, Office and Pdf test projects all pass.
