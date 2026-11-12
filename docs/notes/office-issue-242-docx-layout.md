# Office conformance issue #242: DOCX layout

Eight DOCX samples of [issue 242](https://github.com/Broiler-Platform/Broiler.Documents/issues/242)
about pagination, tables and columns, run locally with `broilerdoc-office
--references-dir … --accept-reference-mismatch --font-dir … --font-set
office-fonts-2 --dpi 96` against the pinned LibreOffice 24.2 PDFs. Where the
question was what Writer does rather than what the 24.2 PDF shows, I converted
the sample, a cut-down copy of it, or a small DOCX written for the question,
with the local LibreOffice 24.2.7.2 (the references' version) and read the word
boxes with `pdftotext -bbox`. Similarity is the runner's pixel similarity at
96 dpi, which is the similarity of the worst page.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf128959.docx` | A cell merged down three exact-height rows holds more text than the rows hold. broilerdoc kept the rows a merged cell's text needs together and moved them whole to page 2, which pushed the 29 empty paragraphs after the table onto a page 3. Writer breaks the table between the rows and continues the merged cell's text beside the rows left on the next page (below). | Fixed: passes (27.4 % before), 2 pages. |
| `ooxmlexport/data/tdf170602_exportContext.docx` | Two causes. (1) Every heading is based on "List Paragraph", which turns `w:contextualSpacing` on, and states its own spacing; broilerdoc collapsed the 18 and 6 points between consecutive headings, LibreOffice keeps them, because the styles part lists the headings before "List Paragraph" (below). The lost space was the lost page. (2) The front matter's sections number their pages with `w:pgNumType w:fmt="upperRoman"` and the header's `PAGE \* MERGEFORMAT` follows them: "II", "III", where broilerdoc wrote "2", "3". | Improved: 11.5 % → 37.9 %, 18 pages as LibreOffice (17 before). Left: the running header's `STYLEREF "Überschrift 1"` names a paragraph style by its German name and still shows its saved "Inhalt>"; LibreOffice keeps the 24 points above the headings that a lone page break pushes to a new page (in my cut-down copies only while the header holds that STYLEREF field, which I take for a LibreOffice quirk and did not copy); and the two-line heading 2's direct `w:line="360"` is ignored by LibreOffice, which keeps the style's double spacing, for a reason I did not find. |
| `layout/data/fdo43573-2-min.docx` | Two-column pages with pictures. (1) A picture that keeps text above and below it (`wrapTopAndBottom`) pushed down the text of both columns, though it stands in the left one only: page 7's right column started under it, its inline picture went to page 8, and the document ran a page long. (2) Pictures placed `relativeFrom="column"` in the right column were drawn from the left of the text area, on top of the left column's pictures. | Improved: 54.2 % → 56.5 %, 15 pages as LibreOffice (16 before). With only (1) the pages were right but page 8 drew two pictures over each other and the text drifted: 19.3 %. Left: text set on a page before the paragraph a page-anchored picture hangs from does not move out of the picture's way (page 8's second column), which a single forward pass cannot do. |
| `uiwriter/data/tdf104649.docx` | A Persian document: every paragraph is `w:bidi` with `w:jc="left"`. LibreOffice sets such a paragraph from the right; broilerdoc read no paragraph direction and set every line flush left with its words in reading order from the left. | Partly fixed: right-to-left paragraphs are now set from the right, their words running from the right (below). The similarity is unchanged (27.9 %): the worst page (29) is a page where the text has drifted by a line, and the Arabic letters are still drawn unjoined and left to right inside each word, which is the text drawing's shaping, not the layout. Page 2 of LibreOffice holds 21 entries where broilerdoc fits 20: the 21st ends 0.06 points past the foot because Writer rounds each line's height to whole twips (17.45 points against broilerdoc's 17.461); I left that rounding alone, as it would move every document. |
| `layout/data/tdf123163-1.docx` | The thirty-one-section magazine of earlier rounds: column and frame drift from page one. | Not changed (15.4 %). |
| `layout/data/tdf170477.docx` | As in issues 236 to 240: the text differences are LibreOffice's text export leaving out the floating tables (`Table5`, `Table9`), which its PDF draws; the pixel side is square-wrapped pictures in cells and a row holding a floating table. | Not fixed (74.4 %). |
| `ooxmlexport/data/n830205.docx` | Tracked changes to list numbering that LibreOffice shows as both numbers ("[1.]", "2.5[2.6]"), with continuous column sections. | Not fixed (11.9 %): showing a tracked numbering change is a revisions feature outside this cluster. |
| `uiwriter/data/tdf157131.docx` | The worst page is page 1, whose "ABC" picture is an SVG without a size that LibreOffice stretches over the page (issue 236). The footer's floating table (`w:tblpPr w:vertAnchor="text" w:tblpY="1"`) sits 30 points higher than LibreOffice puts it. | Not fixed (38.7 %). Converting cut-down copies with 24.2: LibreOffice hangs the table from the top of the footer's last paragraph and gives it no room in the footer; a negative `tblpY` lifts it, but 20 or 60 points down leave it where 0 does. Without an account of that I left the footer as it is; the score is page 1's anyway. |

## A table breaks between the rows a merged cell spans

`DocumentLayout` kept together the rows a merged cell's text needs (issue 207,
for `2024-01-19_merged-cells-on-separate-pages-vertical-alignment.odt`, so that
a 51-row merge with little text no longer moved all 51 rows) and moved that
group to the next page when it did not fit. Writer instead breaks a table at
any row and carries the merged cell's remaining lines into the follow row
(Writer's "follow flow line"). Small DOCX files converted with 24.2 (the
filler pushing the table to the foot of an A4 page, a cell merged down three
17-point rows, five 13.8-point lines in it):

- `trHeight hRule="exact"`: two rows on page 1 with the two lines that fit
  beside them, the third row on page 2 with the other three lines, which run
  past the row; the paragraph after the table follows the 17-point row;
- `hRule="atLeast"` and no height: the same split, the last row growing to
  hold the rest;
- room for one row only: one line on page 1, the next on page 2;
- two auto-height rows, five lines merged over them, room for about four lines:
  both rows start on page 1 and the merged cell's lines fill it to the foot
  (three lines), the other two carry on at the top of page 2; with three rows
  and six lines, four lines on page 1;
- three 20-point `atLeast` (or exact) rows, room for 52 points: two rows and the
  two lines beside them on page 1, though a third line would fit the page; the
  third row, whose 20 points do not fit, starts page 2 with the rest;
- the same files saved as ODT by 24.2 and converted again lay out the same, so
  the rule is Writer's, not the DOCX import's (it settles the clash with the
  odt-layout cluster's "a group that fits a page moves whole", which had no
  reference behind it).

`SplitMergedRows` does this where the group would otherwise move: the rows that
fit stay with each merged cell's lines that end beside them, and the rest of
the cell moves into the first row left over, which draws the cell's box for the
rows it still spans (`CellLines.SpanRows`) and grows if its rows may. Where
that row may break, its own cells fit beside the rows above and every merged
cell running into it ends in it, it is made breakable and starts on the page
too, so the row split below cuts the merged cell's rest at the page's foot. It
does nothing for header rows, a table kept together, rows kept with the next,
or a merged cell none of whose lines would stay; a group taller than a page is
the odt-layout cluster's `StackOversizedSpans`. The unit test
`A_Merged_Group_Moves_To_The_Next_Page_When_It_Fits_There` asserted the old
behaviour without a reference; it now asserts the measured one
(`A_Merged_Group_Breaks_Between_Its_Rows_At_The_Page_Foot`).

## Contextual spacing and the order of the styles part

Writer keeps `w:contextualSpacing` in the item that holds the space above and
below, and writerfilter creates the styles in the order `styles.xml` lists
them. A style that states its own spacing gets an item made from its parent
chain as it is at that moment, so it inherits the flag only from ancestors
listed before it. Cut-down copies of `tdf170602_exportContext.docx` converted
with 24.2:

- headings based on "List Paragraph" (`w:contextualSpacing`), stating 18 and 6
  points, listed before "List Paragraph": 48.15 points between consecutive
  headings, nothing collapsed; deleting the `w:contextualSpacing` element
  changes nothing;
- the same with "List Paragraph" moved before the headings: 15.25 points,
  collapsed;
- removing the numbering, the outline level, the indents, the run properties,
  the latent styles, the linked character styles, or renaming either style
  changes nothing.

`DocxStyles.ContextualSpacing` resolves the flag that way; a style that states
no spacing still inherits from its parent as it finally is.

## Page number formats

`w:pgNumType w:fmt` is read into `DocumentSection.PageNumberFormat`. A `PAGE`
field without a format switch of its own (`\* MERGEFORMAT`, or a format
LibreOffice has no numbering for, as `tdf170003_bottomSpacing.docx`'s
ArabicDash) is marked `PageField.FollowsPageFormat`, and the layout writes it in
the format of the section that owns the page. LibreOffice imports the format as
the page style's numbering, which such a field shows.

## Right-to-left paragraphs

`w:bidi` is read into `ParagraphStyle.RightToLeft`. A small DOCX converted with
24.2 shows that Writer reads the paragraph's `w:jc` and `w:ind` as logical:
`left` (and no `w:jc`, and `start`) sets it flush right, `right` flush left, a
left indent stands in from the right and a centred line centres between the
mirrored indents. So the layout composes the line as before and mirrors it
across its text area (`MirrorRightToLeft`). A run of left-to-right words
(Latin text, numbers after it, the spaces between them) keeps its own order
inside the room the mirror gives it, as the bidirectional algorithm orders it
at the word level: "aaa bbb ccc" in such a paragraph reads left to right at the
right margin, as in LibreOffice. A paragraph with a drop cap is left as it was.

## Top-and-bottom frames and columns

`ExclusionBandQueryEngine` let a top-and-bottom (`ClearanceOnly`) frame push
down any line it met vertically, wherever the line stood across the page.
Writer moves only text whose area the frame overlaps. `TextWrapExclusions.Resolve`,
which the body and frames use for each line, now asks the engine to skip such a
frame where it does not reach into the line's column. Other callers (tables,
clearance of objects) keep the old rule.

`wp:positionH relativeFrom="column"` (and `"character"`, which this reader
places as the column) is now `DocumentShape.OffsetXFromColumn`: the layout
places the shape, and its exclusion, across from the left of the column the
anchoring paragraph was set in, not the text area's.

## Verification

Runner, all axes, before (the run-155 tree) and after on the same machine and
fonts, `--jobs 2`:

- The eight samples: see the table. Text checks: 7 of 8 pass before and after
  (`tdf170477.docx` fails as before).
- Merged rows: every input with a vertical merge (DOCX `w:vMerge`, ODT
  `number-rows-spanned` above 1, RTF `\clvmgf`, 61): text 56 → 56, pixel
  42 → 43 passed; only `tdf128959.docx` changed.
- After merging the other clusters (`bc16958`) and making a second row start
  on the page (above): the 61 merged-cell inputs with
  `tdf127316_autoEscapement2.odt` (62) score exactly as on `bc16958`: text
  57, pixel 45 passed, `tdf128959.docx`, `fdo114306.odt` and
  `tdf127316_autoEscapement2.odt` passing; against run 155, pixel 42 → 45 and
  `tdf127118.docx` 50.9 → 54.0 %.
- Page number format and contextual spacing: the DOCX inputs with a non-decimal
  `w:fmt` and a `PAGE` field, and those whose styles part has a style stating
  spacing below a later-listed ancestor with `w:contextualSpacing` (7), with the
  eight: text 12 → 12, pixel 5 → 6; only `tdf128959.docx` and
  `tdf170602_exportContext.docx` changed.
- Right to left: every DOCX input with `w:bidi` (37): text 37 → 37, pixel
  30 → 30; `tdf163575.docx` 68.9 → 70.2 %, `tdf155707.docx` 64.5 → 64.8 %,
  nothing fell.
- Top-and-bottom frames: every DOCX input with `wrapTopAndBottom` or a VML
  `w10:wrap type="topAndBottom"`, every ODT/FODT with `style:wrap="none"` and
  every RTF with `\shpwr1` (134), measured with the column-relative change
  not yet in: text 115 → 115, pixel 88 → 88 passed; `fdo43573-2-min.docx`
  went to 15 pages (19.3 %, see above) and `shape-anchored-in-follow.docx`
  moved 76.30 → 76.21 %.
- Column-relative anchors: the DOCX inputs with a `relativeFrom="column"` or
  `"character"` anchor and a section of two or more columns (5), with
  `fdo43573-2-min.docx` and the eight samples (13): text 12 → 12, pixel
  2 → 3 passed; `fdo43573-2-min.docx` 54.2 → 56.5 % (15 pages),
  `shape-anchored-in-follow.docx` 76.30 → 76.21 % (its rectangle in the
  middle of three columns no longer pushes the third column down; its first
  page is far from LibreOffice's either way), the four column samples
  unchanged.

Tests: `DocumentLayoutIssue242DocxLayoutTests` (a table broken between merged
rows, the merged cell's box on both pages, growing rows; right-to-left lines;
a column-relative shape and the column it leaves alone), `DocxIssue242DocxLayoutTests`
(the section's page number format, a switch that keeps a page field's own
format, contextual spacing by style order, `w:bidi`, column-relative anchors),
`TextWrapExclusionsIssue242Tests`, `DocumentLayoutPageFieldTests.A_Page_Number_With_No_Format_Of_Its_Own_Follows_Its_Sections`.
`DocxPageFieldTests` now expects the new `FollowsPageFormat` flag. Run:
`Broiler.Documents.Docx.Tests`, `Broiler.Documents.Pagination.Tests`,
`Broiler.Documents.Model.Tests`, `Broiler.Documents.Tests`,
`Broiler.Documents.Office.Tests`, `Broiler.Documents.Cli.Tests`,
`Broiler.Documents.Pdf.Tests`, `Broiler.Documents.Odt.Tests`,
`Broiler.Documents.Rtf.Tests`, `Broiler.Documents.FormatCodes.Tests`, all
passing; the solution builds without warnings.
