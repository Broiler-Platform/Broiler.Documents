# Office issue #218 — severity ≥ 925

Run 139 (workflow run `42533622267`, commit `618b4b5`, `office-references-v4` at 96 dpi)
reported six samples at severity 925 or above. Each was reproduced bit-for-bit from the
reference archive before it was fixed.

| Sample | Before | Cause | Fix |
| --- | --- | --- | --- |
| `odfexport/data/DUMMY2.odt`, `uiwriter/data/DUMMY2.odt` | 1025.18 (7 vs 5 pages) | A paragraph's continuation was recomposed on a page fully covered by a page-anchored picture, which put a page-high wrap skip in front of its first line. When it then moved on, `ReflowContinuation` refused to recompose at line 0, so that skip came along to the next page, overflowed it and left a blank page. | `DocumentLayout.ReflowContinuation` now composes the continuation again when none of it was placed (`nextLine == 0`). |
| `htmlimport/data/tdf122789.html` | 929.52 | `<img src="libreoffice.jpg" width="70%">` — external images were dropped without a trace, and percentage sizes were not read. | An external image that states a size now leaves a missing-payload placeholder of that size (`html.skip.external` is still reported, and nothing is fetched). Percentage sizes resolve against the default page's text area. |
| `ooxmlexport/data/mce-nested.docx` | 928.87 | `wp:align` and `wp14:pctPosHOffset` / `wp14:pctPosVOffset` were ignored, so a right-aligned sidebar group was drawn at the left edge and a title bar 25 % down the page was drawn at the top. | `DocxReader.AlignedOrRelativeH/V` resolve both against the anchor's frame, using the anchor's own extent (a group is aligned as one box). |
| `uiwriter/data/tdf156560.docx` | 926.75 | `w:pgBorders` was not read; LibreOffice draws the page border on page 1 only (`w:display="firstPage"`). | `DocxReader.WithPageBorder` adds the border as an outline shape in the header band of the pages `w:display` selects (`allPages`, `firstPage`, `notFirstPage`); `offsetFrom` `page` and `text` are both handled. Line styles are drawn as one stroke at the widest side's width. |
| `layout/data/shape-anchored-in-follow.docx` | 925.51 | The closing landscape section sets `w:titlePg` but no section names a first-page header or footer. Its page was drawn with no header or footer, while LibreOffice carries the default ones. | A `w:titlePg` section whose (own or continued) references hold no `first` header or footer keeps its default bands on its first page. |

## Verification

* 96 dpi rerun of the six samples (before the page-border hook was restored, see below):
  `mce-nested.docx` passed; `DUMMY2.odt` ×2 dropped to 371.16 (5 vs 5 pages);
  `shape-anchored-in-follow.docx` to 712.46; `tdf122789.html` to 841.39.
* `tdf156560.docx` is covered by `DocxIssue218Tests`; the reference archive was gone from
  the VM by the time it could be rerun, so its rendered score should be confirmed in CI.
* Tests: `DocxIssue218Tests`, `HtmlImageTests.Sized_External_Image_Keeps_Its_Box_As_A_Missing_Payload`.
