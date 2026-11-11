# Office issue #232: tdf163285.fodt, nestedTableInFooter.odt, tdf135056.odt

I compared each sample against the `office-references-v4` archive (font set
`office-fonts-2`) with `broilerdoc-office --sample-list ... --accept-reference-mismatch`
on Windows. The older local `D:\scratch\office-references` archive is
`office-fonts-1`. It has no Liberation faces, so it is the wrong reference for
these samples.

| Sample | Before | After | Cause | Fix |
| --- | --- | --- | --- | --- |
| `layout/data/tdf163285.fodt` | 907.81 (2 vs 2 pages) | reference defect, skipped | (1) The ODT reader gave a `text:p` that names no style nothing but the paragraph defaults. Writer gives it `Standard`, so this sample's bare list items and empty spacer paragraphs lost Standard's 8pt `margin-bottom`. (2) LibreOffice 24.2 splits the keep-together first row only once and never draws the rest of it, from the line after `pg_2` through `pg_3` (tdf#163285). | (1) `OdtStyles.Chain` resolves an unnamed paragraph to `Standard` when the document defines it. broilerdoc now lays out three pages starting `pg_1`, `pg_2` and `pg_3`, which is what the upstream test `layout4.cxx TestTdf163285` asserts. (2) Recorded in `tests/office/extras-reference-defects.json` (fix `229964ac`, first released in 25.2; pixel axis only, because the text export is complete). |
| `odfexport/data/nestedTableInFooter.odt` | 904.45 | passes | `RunningContent` held only paragraphs and shapes. A footer table was flattened into empty paragraphs, so none of its borders were drawn. A nested table's cell also kept the empty paragraph that Writer saves after the table, which made the row one line taller than LibreOffice draws it. | `RunningContent` carries tables for each band (`WithHeader/WithFooter(..., tables)`, `EffectiveHeaderTables/EffectiveFooterTables`). `DocumentLayout.ComposeRunning` lays out a band that has tables through `ComposeBlocks`, the same path a frame's content takes, and the band's cells are added to the page cells. The ODT reader returns a part's tables. `OdtTableReader` drops the empty closing paragraph after a nested table at the end of a cell. |
| `uiwriter/data/tdf135056.odt` | 962.74 | 361.67 (17 vs 17 pages) | (1) An empty body paragraph after an anchor carrying two frames side by side (a short "Sommaire" banner and a page-high TOC frame, with a gap narrower than `TEXT_MIN` between them) was pushed below the deeper frame. That put it on page 3 and moved the following heading and every page after it down by one page. The exclusion kernel descended to the deepest foot of the obstacles; Writer steps down to the nearest one. (2) TOC entries name the empty `Index Link` character style, but the layout decorated every link blue and underlined. (3) An empty footer paragraph whose only content is a top border (the rule above the page number) was treated as "no footer". | (1) `TextWrapExclusions.Resolve` now descends to the shallowest blocking foot (`ClearanceDescentStep.Shallowest`, up to 32 steps). (2) An ODT whose links name character styles sets `UndecoratedLinks`. A link there that names no style gets Writer's `Internet Link` look (the document's `Internet_20_link` style, or navy and underlined). A document whose links name no style keeps the layout's decoration, so ODT round trips are unchanged. (3) `ReadPart` keeps a lone empty paragraph when it has a border or a background. |

## Verification

- Repro for (1): an anchor paragraph with a 2.67in frame on the left and a
  9.67in frame on the right, followed by an empty paragraph. Local
  LibreOffice 26.2 lays out 3 pages and sets the paragraph under the short
  frame. broilerdoc laid out 4 pages before the fix and 3 after it.
- After fixes (1) and (2), `tdf135056.odt` scored 901.07, and its worst page
  was a blank page with no footer rule. With fix (3) it scores 361.67, and the
  worst page is page 3.
- `nestedTableInFooter.odt` passes its pixel check. `tdf163285.fodt` is
  skipped as a known reference defect.
- The text in the TOC still differs: the document asks for Sawasdee, which
  neither side has. LibreOffice falls back to DejaVu Sans Bold, and broilerdoc
  to its own pinned face.
- Tests: `OdtIssue232Tests`, `DocumentLayoutIssue232Tests`, plus the Model,
  Pagination, Odt, Docx, Rtf, Cli, Html, Office, Markdown and Pdf test projects.
