# Office issue #242: DOCX pages one side draws blank

The DOCX samples of issue #242 where one renderer drew a page blank, or
nearly blank, and the other inked it. I rendered each one locally against the
LibreOffice 24.2 references (`office-fonts-2`, `--font-dir fonts/0`, the
harness's metric aliases) at 96 dpi through the conformance harness, and
rasterised both sides to see which side inks what. For the cut-down
experiments I ran the local LibreOffice 24.2.7.2 with a private profile and a
fontconfig configuration that lists only the reference fonts, so it substitutes
fonts the way the reference machine does. Six of the seven samples were
analysed in the [issue #232](office-issue-232-hidden-section.md),
[#234](office-issue-234-docx-blank.md), [#236](office-issue-236-docx-blank.md),
[#238](office-issue-238-docx-headers.md) and
[#240](office-issue-240-docx-blank.md) notes. I re-checked the blocker each one
records; four of them no longer hold.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/test_msword_hang.docx` | A text box 165.3 points tall (3306 twips) holds a 6 point space before, a "Drawing" caption line and a 142.5 point picture line. Writer sets each line at a whole number of twips, rounding the 1.15 proportional height down, so the caption is 336 twips and the picture line ends exactly at the frame's foot. broilerdoc measured the caption at 16.85 points, so the picture line ended 0.05 points past the foot, and a box only keeps the lines that fit, so the picture was dropped. | Fixed (6.2% to pass). `DocumentLayout.PlaceShapeContent` lets the n-th line of a box overrun its foot by up to n twips, the most that Writer's rounding of n line heights can save. The scan of every DOCX sample (below) found two other samples where this tolerance decides a line, `Tdf147485.docx` and `tdf147485-forcepoint.docx`; neither check changes. |
| `ooxmlexport/data/fdo78384.docx`, `ooxmlimport/data/tdf103664.docx` | `w:sym` characters in Wingdings (U+F0E7; U+F020, U+F0FC, U+F0DC, U+F081), which the reference font set does not have. The earlier notes put the blank reference down to which fonts were installed. The experiments show the rule. LibreOffice imports a `w:sym` in the symbol character set, and a symbol font it does not have is not replaced through fontconfig but by its default serif face: the reference PDF embeds Liberation Serif, a `w:sym` in a made-up font "Foobar" is also drawn in Liberation Serif, and the same characters typed as `w:t` in Wingdings are drawn in DejaVu Sans, fontconfig's match. A private-use character no face has is drawn as the face's missing glyph (`.notdef`). Liberation Serif's `.notdef` has no contours, so the page is blank. DejaVu Sans's is a box, which is what the `w:t` variant shows. broilerdoc drew Wingdings with the host fallback face and the renderer's block "?". | Fixed (0% to pass, both). `InlineStyle.IsSymbolCharacter` marks a `w:sym` character. The layout sets one whose font has no face of its own in the `serif` family, the harness's Liberation Serif. `DocumentPagePainter.DrawRun` draws a private-use character that the face lacks as the face's `.notdef` outline, filled by the non-zero rule, and `ResolvedFontFace` gives it the `.notdef` advance. A `w:sym` in a font with a face, Arial for instance, keeps it (LibreOffice draws Liberation Sans's box there). Other missing characters are left to the renderer, see the regression notes. |
| `ooxmlimport/data/tdf125038b.docx` | `IF "1234" = "" "" "phone: 1234"`, saved with the result "phone: 1234". The issue #234 note found that LibreOffice garbles the condition and LibreOffice 26.2 is still blank, so it left the field alone. The experiments show what the import does. It reads the comparison `=` as the name of Word's formula field, so the flat ODF condition is `1234 FORMULA`, and the PDF shows the result Writer's calculator gives for that. With 33 cut-down `IF` fields: a condition `A = B` whose first operand is a number always draws the true result (`1 = 2` gives the true result, `"1234" = ""` gives the empty one); one whose first operand is text (`"a" = "a"`, `"x.x@x.x" = ""`, `abc = abc`) is not worked out and keeps the saved result; numeric `<`, `<=`, `>`, `>=` and `<>` comparisons and a lone number are worked out as Word works them out. The text export does not lay the page out and lists the saved result. The instruction running over a paragraph break does not matter: the same field on one line is blank too. | Fixed (0% to pass). `DocxFields.LibreOfficeIfResult` works out the forms above and leaves every other condition, and any `IF` with a nested field, with its saved result. Where it works one out, the saved result stays in the document hidden, so the text check still matches LibreOffice's export, and the result it gives is drawn. Of the corpus's six `IF` samples only this one has such a condition. |
| `ooxmlexport/data/shape-in-floattable.docx` | An inline `wpg` group whose only member is a text box, set at the right of the group's child space. The issue #240 note took LibreOffice's placing of "Hello" at the cell's corner for a floating-table quirk. It is not one: the same group in a plain table, in the body, with the child space scaled 1:1, and with a plain red rectangle instead of the text box, is always drawn over the whole group. A second member in the group puts both at their own offsets. `groupshape-theme-font.docx` and `dml-charheight-default.docx` show the same thing: their one box is drawn at the group's full height. | Fixed (0% to pass). `DocxReader.ReadShapeGroup` reads a group of one `wps:wsp` as that shape in the group's box. Diagram drawings, which the reader also reads as groups, are left alone. "Hello" is still drawn in Liberation Serif where LibreOffice uses DejaVu Sans (a shape's text there takes the document default font, CG Times, not the Normal style's Times New Roman), and the floating table's row does not grow to the inline group. Neither stops the page passing. |
| `ooxmlexport/data/tdf104115.docx` | A VML `_x0000_t19` arc with its own `path`, which LibreOffice 24.2 imports as an empty path (issue #234 note). The experiments narrow it down. Without the shape's `path` attribute LibreOffice draws the arc. With a `path` of `wr` segments and `nf`/`ns` sub-paths it draws nothing. With one `wr` segment it draws the arc at the page's corner. That is LibreOffice's `wr` handling going wrong, not a rule a renderer could follow. Only `tdf104115.docx` and `cloud.docx` in the corpus have such paths. | Not fixed (0%). |
| `ooxmlexport/data/tdf162746.docx` | LibreOffice lays the body table out about zero wide (issue #240 note). The only commit on tdf#162746, 74533c1f1d5e8e48a65d33bef236f1382d32aa06, says that it only stops the layout loop and that the table still has the wrong width. No release fixes it, so it cannot be a reference-defect row. | Not fixed (36.1%, 2 pages against 1). |

## Verification

- The seven samples through the harness: `test_msword_hang` (6.2%),
  `fdo78384`, `tdf103664`, `tdf125038b` and `shape-in-floattable` (0%) now
  pass their pixel checks. `tdf104115` (0%) and `tdf162746` (36.1%, 2/1) are
  unchanged. All seven text checks still pass. 0/7 to 5/7 pixel checks passed.
- Regression corpus: 68 DOCX samples, run through the harness
  with the build before and after: 42/68 to 47/68 pixel checks passed and
  57/68 text checks before and after, with no check newly failing. The set
  is every corpus sample with a `w:sym` (15), an `IF` field (6) or a `wpg`
  group of one member (6), and every sample where a debug build showed the
  changed code deciding something: a character the run's face has no glyph
  for (47, most of them East Asian or Arabic text), or a box line kept only
  by the twip tolerance (3). I found those by rendering all 1805 DOCX
  samples with that build. The five pixel checks newly passed are the five
  samples above. Three other samples move by under 0.1%:
  `tdf105035_framePrC` (79.57% to 79.62%), `2129393649` (78.70% to 78.72%)
  and `tdf104492` (73.92% to 73.86%). The last two have Wingdings `w:sym`
  arrows, which are now blank as in LibreOffice, and the text after them now
  starts the Liberation Serif `.notdef` advance further on.
- Tests: `DocxIssue242BlankTests` (a `w:sym` marked as a symbol character, the
  `IF` forms LibreOffice works out and the ones it leaves, a nested field, a
  group of one shape and of two) and `DocumentLayoutIssue242BlankTests` (a
  box line within a twip a line of its foot, a symbol character with no face
  set in `serif`, a private-use character drawn as DejaVu Sans's `.notdef`
  and as nothing in Liberation Serif, its `.notdef` advance, the non-zero
  fill). `DocxShapeGroupTests.Maps_Each_Box_Through_The_Groups_Child_Space`
  now has a second box, so it still tests the mapping. Docx (1073), Pagination (472), Model
  (343), core (332, 3 skipped), Cli (357), Odt (714), Rtf (659), Pdf (749),
  Office (99) and Html (244) test projects all pass. The solution builds with
  no warnings.
- Not covered: LibreOffice's glyph fallback. It draws a character the run's
  face lacks from another installed face (Arabic from DejaVu Sans), and East
  Asian text in the run's East Asian font, which the model does not keep.
  broilerdoc still draws those as the renderer's "?". Drawing the Latin face's
  `.notdef` for every missing character was tried first: it made
  `tdf130314.docx`'s Chinese paragraphs blank where LibreOffice draws boxes
  (77.3% to 51.6%), so the `.notdef` is drawn only for private-use
  characters. The PDF writer does not draw `.notdef` outlines.
