# Office issue #236: ODT pictures and embedded objects

These samples were flagged with `odt.image.missing`, `odt.image.anchored` or
`odt.image.format`. I reproduced each one locally on Windows. Each broilerdoc render
was compared with the LibreOffice 24.2 reference PDF from the local reference
archive. Ink is the count of pixels darker than 200 on page 1 at 96 dpi.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf130242.odt` | A chart frame: `draw:object` plus `draw:image` pointing at `ObjectReplacements/Object 1`. That entry has no extension. Its manifest media type is `application/x-openoffice-gdimetafile`, and its bytes are a StarView metafile (`VCLMTF`), LibreOffice's own GDI metafile. No image sniffer recognised it, so the loader returned `LoadResult.Format` and drew the marked missing-picture box. | **Fixed.** The new `SvmRasterizer` (in `Broiler.Documents/Resources`, beside `EmfRasterizer`) rasterises the vector subset of an SVM to a PNG: RECT, POLYGON, POLYPOLYGON (with flagged Bézier polygons) and POLYLINE (with `LineInfo` width), drawn in their LINECOLOR/FILLCOLOR, with PUSH/POP. Coordinates go through the header map mode and every MAPMODE action. That includes `MapRelative`, which LibreOffice uses to place a chart inside an embedded text object. `OdtImageLoader` calls it, or `EmfRasterizer` for an EMF, when no raster type is found (`RasterizeMetafile`). It does this both for package entries and for `office:binary-data`. Ink 6513 → 114323 (LibreOffice 112844). The pixel check passes. |
| `ooxmlexport/data/embedded_chart.odt` | The same `ObjectReplacements` SVM, here for an embedded *text* object that holds a chart. The outer metafile is in twips. The chart is placed in it with a `MapRelative` map mode: origin (3006, 3014), scale 72/127. | **Fixed** by the same change. Ink 7213 → 15866 (LibreOffice 16689). The pixel check passes. LibreOffice draws its live rendering of the inner document, which places the chart about 0.7 cm further left than the stored replacement does. |
| `htmlexport/data/ole1-pres-data-wmf.odt` | A `draw:object-ole` whose `ObjectReplacements/Object 1` is an EMF (record 1, ` EMF` signature). The ODT loader never tried `EmfRasterizer`, which DOCX and RTF already use. | **Fixed:** `RasterizeMetafile` routes an EMF to `EmfRasterizer`. Ink 6753 → 39232 (LibreOffice 45774). The pixel check passes. |
| `layout/data/tdf159443.odt` | The same SVM chart replacement. | **Improved, not passing.** The replacement is now drawn: ink 6513 → 45727 (LibreOffice 72609), blur diff 12.1% → 9.2%. Two differences remain. (1) The stored replacement shows the chart with a data table under the plot. LibreOffice 24.2 draws its own live rendering of `Object 1`, which has no table and has a taller plot. (2) Text is not drawn (see below). broilerdoc has no ODF chart renderer, so the stored replacement is the best picture it has. |
| `layout/data/horizontal_multilevel.odt`, `layout/data/tdf127304.odt` | Charts (`Object 3`) whose `draw:image` names `ObjectReplacements/Object 3`, an entry these packages do not contain. LibreOffice renders the chart from `Object 3/content.xml`. broilerdoc keeps the frame's box and draws nothing (the issue #234 rule for a picture the package does not hold). | **Not fixed.** Drawing these needs an ODF chart renderer (`chart:chart` plot areas, axes, series and the embedded table data). That is a feature, not a focused fix. |
| `globalfilter/data/document_with_linked_graphic.odt` | The frame links `../linked_graphic.jpg`, a file outside the package. In the corpus it sits one directory up, beside the document's folder. LibreOffice resolves the relative link against the document URL and finds it. broilerdoc deliberately does not read files that document content names (`odt.image.external`, ADR 0004), so it draws an empty frame. | **Not fixed.** Changing this is a policy change, not a bug fix. It would need an opt-in resolver like HTML's `--allow-local-images`, reaching the document's *parent* directory, with the conformance runner passing it for ODT. That should be decided on its own. |
| `ooxmlexport/data/tdf128820.fodt` | A grouped `draw:frame` whose `draw:image` carries an empty SVG (`<svg xmlns=.../>`) as `office:binary-data`, plus a `text:p` "json" inside the image. LibreOffice draws the empty picture as nothing and draws "json" as the graphic object's text. broilerdoc does not decode SVG, so it drew the marked missing-picture box. | **Not fixed.** Matching LibreOffice needs text drawn inside an image frame, which the model does not carry for pictures. Treating the empty SVG as blank would only swap one wrong page for another. |
| `ww8export/data/tdf46441-2.odt` | Not a picture problem. The visible text lives in `Frame15`, a paragraph-anchored text frame nested inside the text box of `Frame6`, and inside a tracked insertion. `ReadShapeParagraphs` reads a frame's text into a nested builder and keeps only its paragraphs and tables. A text frame nested in a text box is dropped with its text. LibreOffice shows three lines there; broilerdoc draws only the body's `P` (ink 35 against 3934). | **Not fixed.** Hoisting nested frames out of a shape's text and positioning them relative to the outer frame is a text-frame layout change in `OdtReader` and the shape model. It is outside this picture work. |

## What the SVM rasteriser does not draw

Text actions (TEXT, TEXTARRAY, STRETCHTEXT) are skipped, as `EmfRasterizer` skips
text. This removes axis labels, legends' series names and data labels from
charts. Drawing them would need the page's font resolution inside a codec-level
rasteriser. Bitmap actions (one BMPSCALE in the corpus) are skipped too. Clip
regions are ignored. A hairline (`LineInfo` width 0) is drawn at 1 pt, so it is
still visible once the picture is scaled down onto the page.

## Verification

- Related corpus run, pixel axis, before and after: every sample above, plus
  every other ODT in the reference archive whose `ObjectReplacements/` holds an
  SVM or EMF (46 samples, `--references-dir --accept-reference-mismatch`, 144
  dpi). Passing went from 10 to 30 and none regressed. Newly passing besides
  the three above:
  - `IndexingExport_OLE`, `charttable`, `piechart_leaderline`, `tdf108021`,
    `tdf114163`, `tdf122800`, `tdf123268`, `tdf124796`, `tdf125334`,
    `tdf125335`, `tdf133005`, `tdf136816`, `tdf72727`, `testTruncatedAxisLabel`;
  - `internal_hyperlink_ole`, `ole-save-while-edit`, `tdf97601`.

  `tdf143269_missingEmbeddings.odt` still fails. Blur diff went from 12.3% to
  13.3%: its first chart's stored replacement is a pie, while LibreOffice
  renders the live line chart.
- `SvmRasterizerTests` (Broiler.Documents.Tests): colours of a filled polygon
  and a hairline; a `MapRelative` map mode composed onto a twip page; null for
  a metafile with nothing drawable or a bare header; a truncated action stops
  the read without throwing.
- `OdtIssue236Tests` (Broiler.Documents.Odt.Tests): an SVM chart replacement
  becomes a PNG picture at the frame's size, with no `odt.image.format`; an
  EMF OLE replacement is rasterised; a metafile with nothing drawable keeps the
  marked missing-picture box.
- Projects run, all passing: Broiler.Documents.Tests (324),
  Broiler.Documents.Pagination.Tests (417), Broiler.Documents.Odt.Tests (669),
  Broiler.Documents.Rtf.Tests (640), Broiler.Documents.Docx.Tests (964).
