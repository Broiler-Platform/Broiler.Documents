# Office issue #249: spreadsheets, HTML pictures and hybrid PDFs

Fourteen samples: six ODS, six HTML and two PDF. I reproduced each one on Linux with
the office runner at 144 dpi (`--font-set office-fonts-2`, the pinned font directory),
with LibreOffice 24.2.7.2 as the reference. I checked behaviour with the local `soffice`
24.2.7.2 (`--convert-to pdf`, then `pdftoppm`, `pdftotext -bbox` and `pdfimages -list`
for positions, and `--convert-to fodt` for Writer's styles). Similarity is the runner's
pixel score, before → after.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `mailmerge/data/10-testing-addresses.ods`, `4_v01.ods`, `bookmarkcondition.ods`, `one-empty-address.ods`, `tdf118113.ods`, `uiwriter/data/hash#path/calc-data-source.ods` | The ODS reader projected each sheet as one table on broilerdoc's own page: 1-inch margins, no header or footer, 12 pt text, every column squeezed into the page width. LibreOffice prints a sheet the way Calc does. (1) It uses the page style the sheet's table style names (`style:master-page-name`): A4 here, 2 cm margins where the layout states none (`ScStyleSheetPool::CreateStandardStyles`), and a header (the sheet name, centred) and a footer (`Page N`). Each band is `fo:min-height` (7.5 mm) tall *inside* the margin with its spacing included. Measured with `pdftotext -bbox`: the header line sits at the 2 cm margin, and the first row's cells start 2.75 cm down. (2) Cell text uses the style chain (`Default`, then `style:default-style`): Liberation Sans 10 pt, or Calibri 11 pt through `Default` in calc-data-source.ods. Text sits at the foot of the cell. Numbers stand to the right (Calc's standard alignment), and text is 1 pt from the cell's left edge (`SvxMarginItem`, 20 twips). (3) A sheet wider than the page is printed a run of whole columns per page, top to bottom first (`ScPrintFunc::CalcPages`). 10-testing-addresses.ods is three pages, A–I, J–O and P. In 4_v01.ods page 3 holds K–Q with only Q filled, so empty columns inside the used range keep their width. (4) Text that does not fit runs on over empty neighbouring cells instead of wrapping. datasource.ods runs a heading past the sheet's only column, because Calc widens the print range for it (`MaybeAddExtraColumn`). | **Fixed.** `OdsPrintSetup` reads the page, header and footer, and `OdsCellStyles` resolves the style chain and the pool defaults. A cell with no alignment of its own takes Calc's standard one, and an unstated vertical alignment is the foot (the writer now writes `top` explicitly). `DocumentStyleDefaults.PrintsSheets` marks the tables as sheets. The layout (`SheetPrinting`) cuts each one into runs of columns that fit the page and leaves out runs with nothing to print. It also widens a cell over the empty cells its text runs onto, with the rest of the page as a last empty column, and gives cells Calc's 1 pt margin. The model keeps one table per sheet, so CSV export and round trips are unchanged, and layout lines still name the document's paragraphs. Pixel: 92.6 / 97.3 / 97.1 / 90.9 / 73.1 / 59.5 % with pages 1/3, 1/3, 1/2, 1/2, 1/1, 1/1 → all six pass with LibreOffice's page counts (profile similarity 0.977–0.999). |
| `htmlimport/data/tdf142781.html` | Two `<img>` of a 1344 × 960 PNG, one with `width="672"`, one with `height="480"`. LibreOffice draws both at 672 × 480 px (504 × 360 pt; `pdfimages` shows 192 ppi). The HTML reader stated one side and left the other unset, as `InlineImage` allows ("one given preserves the aspect ratio"). But the resource had not been inspected for its pixels, so `ImageStore.MeasurePoints` fell back to the decoded pixel size for *both* sides and drew 1008 × 720 pt, over two pages. | **Fixed:** `ImageStore` keeps the stated side and takes the other from the decoded pixels' ratio. 46.97 % on 2/1 pages → pass on 1 page. |
| `htmlimport/data/tdf122789.html` (passing; regressed by the fix above, then fixed) | `<img width="70%">` on a 200 × 400 JPEG. LibreOffice does *not* keep the shape for a percentage: `pdfimages` shows 400 px at 96 ppi, so the picture is 70 % of the text width wide and 300 pt tall. With the first fix alone the picture became 316 × 632 pt and the sample failed. | **Fixed:** where a percentage stands alone, the HTML reader states the other side at the picture's own size, read from its header (`HtmlImagePixels`: PNG `IHDR`, JPEG frame header, GIF screen). Passing before and after. |
| `htmlimport/data/PageAndParagraphFilled.html` | `<body background="data:image/png…">` and a paragraph with a CSS `background: url(…) repeat`. The reader took only a colour for the page, so the page was white. LibreOffice tiles the body picture over the whole page from its corner, at its CSS pixel size: the 158 × 57 px tile matches the reference with offset (0, 0) every 118.5 × 42.75 pt. | **Fixed (page):** `HtmlPageBackground` reads the picture (HTML `background`, CSS `background-image` or the `url()` in `background`) as a tiled page fill from the top-left; `no-repeat` draws it once. 44.92 % → passes at 80.8 % (profile 0.965, blur difference 0.192, coverage difference 0.099). **Not done:** the paragraph's own tiled picture. `ParagraphStyle.Background` is a colour, so a picture fill behind a paragraph is a model and painter feature of its own. It is the remaining blur and coverage difference, so this pass has little margin. |
| `htmlimport/data/nested-list-mixed-type.html` | Text check only. An `<ol>` whose second item nests a `<ul>`. The 24.2 text export numbers the bullets `1. Bullet 1`, `2. Bullet 2`. This is tdf#124339: the HTML import kept one numbering rule for the list hierarchy and never changed a level's type once set. It was fixed by "tdf#124339 fix HTML import of nested lists with mixed ol/ul types" (master `0d9010656e3f75f6a88f6478ad7e24520119a9d3`, libreoffice-26-2 `d9a14b9b3eb39e1636bf69b77bc9945234bcbc5d`, first in 26.2.4). The fix came with this sample and `htmlimport.cxx testNestedListMixedType`, which asserts that "Bullet 1" is a character bullet. broilerdoc already reads bullets. | **Reference defect, recorded** in `tests/office/extras-reference-defects.json` (text axis; the pixel check passes either way). failed → partial, with the text check skipped. |
| `htmlimport/data/outline-level.html` | `<h1>heading</h1>`. In 24.2's HTML import, `Heading 1` is 24 pt bold in "Noto Sans". The FODT export states that face, and with the pinned fonts it falls back to DejaVu Sans Bold. The Heading style's 0.1665 in space above is also kept at the top of the page (the text is 12 pt lower). broilerdoc sets it in the serif default at the top. I found nothing in the format or in Writer's style pool that names Noto Sans. The parent `Heading` style is Liberation Sans, so the face looks like it comes from the converting machine's settings, not from the document. | **Not fixed.** Copying a face that comes from the converting machine would not be a format rule. The spacing alone does not lift the score enough to pass. 63.05 %. |
| `htmlimport/data/tdf118579.html` | Malformed nested tables: a `<table>` opens inside the first table's unclosed `<thead>`. LibreOffice moves the second table's caption ("Riepilogo") into the first table's place, drops "Percorso Anno I" and adds empty lines. That is how Writer's HTML table parser recovers from the broken markup. As in #143/#147. | **Not fixed.** This is a separate piece of HTML table-recovery work. 61.43 %, text 3 differences. |
| `htmlimport/data/tdf155011.html` | As in #236–#242: the page's only ink is LibreOffice's radio-button form control inside three nested tables, which the reader skips. Its 42 px offset comes from nested-table layout. | **Not fixed**, for the reasons the #242 note gives. 0 %. |
| `pdf/data/Hybrid_AdditionalStreamsOnly.pdf`, `Hybrid_AdditionalStreamsAndPDFAttachedFile.pdf` | Text check only. These are hybrid PDFs: the trailer's `/AdditionalStreams` (and in the second, an attached file) carries the ODT that LibreOffice exported them from. LibreOffice opens that ODT in Writer instead of importing the PDF (`HybridPdfTest`). There, the two lines are one paragraph with a line break, which the text export writes as two lines. broilerdoc reads the page content and its reading-order heuristic joins the two lines: the block's widest line is the first one, so nothing shows the first line ended short. | **Not fixed.** The root cause is opening the embedded ODF instead of the PDF. A PDF codec may not reference the ODT codec (ADR 0001/0007), so that needs a composition-level decision (a hybrid-PDF route in the CLI's codec composition), not a reader fix. Guessing a line break from a two-line block would be a heuristic with no measure to test against. |

## Verification

All runs use the office runner at 144 dpi with the pinned fonts. "Before" is the
starting commit `8a20ca0`; "after" is this branch.

- **The fourteen samples:** 8 failing → passing (the six ODS, `tdf142781.html`,
  `PageAndParagraphFilled.html`); `nested-list-mixed-type.html` failing → partial
  (its text check is a recorded reference defect; severity 478.8 → 418.8). Five still
  fail with unchanged scores: `outline-level.html` 63.05 %, `tdf118579.html` 61.43 %,
  `tdf155011.html` 0 %, and the two hybrid PDFs (text). The ODS severities went 804.6 / 792.8 /
  752.6 / 761.1 / 760.3 / 693.0 → 101.4 / 0.8 / 0.7 / 0.6 / 103.6 / 103.7.
- **Every ODS, HTML, PDF and XLSX sample in the corpus** (60 after the `.xhtml` the
  runner does not take): nothing newly fails. The four ODS that passed before still
  pass; `datasource.ods` improved from profile 0.891 to 0.999 once text ran on past the
  sheet. `tdf122789.html` passes (it failed in the run that had only the ImageStore
  fix, which is why the HTML reader states a percentage picture's other side).
- **Layout sweep** (all 3,847 ODT/FODT/DOCX/RTF samples) against the starting commit's
  fingerprints: no sample changed. Sheet printing applies only to documents marked
  `PrintsSheets`, which only the ODS reader sets.
- **Unit tests:** `OdsSheetPrintingTests` (page, header, footer, style chain, standard
  alignment, vertical round trip), `DocumentLayoutSheetPrintingTests` (column runs, empty
  runs left out, text running on, past the sheet's last column, a non-sheet table
  untouched), `ImageStoreTests.An_Encoded_Picture_That_States_One_Side_Keeps_Its_Aspect_Ratio`,
  `HtmlImageTests.A_Percentage_Width_Alone_Leaves_The_Height_At_The_Pictures_Own` and
  `HtmlPageBackgroundTests.The_Body_Picture_Tiles_The_Page_From_Its_Corner`.

## Not modelled

- Calc's `ltr` page order (`style:print-page-order="ltr"`), manual column and row page
  breaks, `scale-to` / `scale-to-pages`, repeated print rows, and print ranges. No sample
  in the corpus uses them.
- Text running on from a centred cell, and a cell that wraps (`fo:wrap-option="wrap"`):
  the model carries no wrap flag, so every text cell may run on.
- Each sheet after the first starts a page with its own page style and header (a
  section per sheet). The corpus has no multi-sheet ODS to measure it against.
