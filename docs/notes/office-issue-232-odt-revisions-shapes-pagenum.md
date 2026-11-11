# Office issue #232 — deleted_table, fdo75872, hyperlink-field

Reproduced at 96 dpi against the `office-references` archive with
`broilerdoc-office --references-dir … --accept-reference-mismatch --font-dir … --sample-list … --dpi 96 --axis pixel`.

| Sample | Cause | Fix | After |
| --- | --- | --- | --- |
| `odfexport/data/deleted_table.fodt` | The deletion is written *in place* (LibreOffice 7.6+): its `text:changed-region` keeps only `office:change-info`, and the deleted table sits in the body between `text:change-start` and `text:change-end`. `OdtReadContext.OpenChange` ignored every deletion, so the table was never marked deleted, and with `ShowRedlineChanges=false` LibreOffice hides it while broilerdoc drew it. | `OpenChange` now opens a deletion whose region holds no content of its own, so the body between the marks is marked deleted and `RevisionDisplay.HideDeletions` removes the table. | pass |
| `odfimport/data/fdo75872_ooo33.odt` (and `fdo75872_aoo40.odt`) | A `draw:rect` whose styles state no fill or line colour. LibreOffice gives documents from older producers the colours they were drawn with (fdo#75872, `XMLGraphicsDefaultStyle::SetDefaults`): OOo < 3.4 / LO < 4.1 `#99CCFF` fill with a black line, AOO 4 `#CFE7F5` with `#808080`. broilerdoc drew nothing. | `OdtMetadata.LegacyShapeColors` picks the colours from `meta:generator`; `ReadShapeStyle` applies them to drawing shapes (not frames) that state no fill colour / stroke. Current producers are unchanged. | both pass |
| `ooxmlexport/data/hyperlink-field.odt` | (1) Footer `text:page-number` without `style:num-format` was always decimal; Writer takes the page layout's `style:num-format` (`i` here), so the reference shows `iii`/`v`. (2) A paragraph-anchored frame links `../../../backup/horde/sxw/lozenge.wmf`, outside the package; LibreOffice 24.2 draws nothing for it, broilerdoc draws its missing-picture box (with outline, at the left margin rather than centred). | (1) Fixed: `OdtStyles.PageNumberFormat` carries the master's page-layout format while its header/footer is read, and `ReadPageField` uses it for a field that states none. Body fields still default to decimal. (2) Not changed: drawing a box where a picture is missing is a deliberate painter policy (`DocumentPagePainter.DrawMissingImage`) shared by every codec; dropping it for unresolvable ODT links is a wider decision. | still fails (worst page 3 is the placeholder box) |

Tests: `OdtIssue232Tests` (Broiler.Documents.Odt.Tests, 644 passing);
Broiler.Documents.Pagination.Tests (404 passing).
