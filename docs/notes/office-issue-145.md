# Issue 145: ten highest-impact cases

The ten highest severity scores in [issue 145](https://github.com/Broiler-Platform/Broiler.Documents/issues/145) were selected in reported order and rerun from the original public LibreOffice sample files. Nine overlap the top ten of #143. This change corrects index-source classification; **it does not resolve the ten visual conformance failures**.

## Local results

LibreOffice 26.8.1.1, poppler 26.05.0, Linux, 72 DPI, unpinned fonts, unchanged 80% threshold and a 40-page cap. The issue used LibreOffice 26.2.6.3, poppler 26.01.0 and 144 DPI; these are local comparisons, not exact reproductions of that environment. Page counts below are Broiler / LibreOffice. Similarity is the runner's rendering score, not an overall conformance percentage.

| Rank | Reported severity | Sample under `sw/qa/extras` | Pages | Similarity | Investigation / remaining work |
| --- | ---: | --- | --- | ---: | --- |
| 1 | 1771.69 | `ooxmlexport/data/tdf163894_hidden.docx` | 5 / 4 | 22.64% | The reported 40 LibreOffice pages are not reproduced locally. Existing hidden-text handling is not retuned to this count. |
| 2 | 1367.46 | `layout/data/tdf142694-1.odt` | 5 / 8 | 8.5% | Nested tables, repeated headers and image flow remain open; see the investigation for #143. |
| 3 | 1336.89 | `uiwriter/data/tdf147199.docx` | 5 / 8 | 0% | Inline drawing groups lack an inline flow footprint in the model/layout. A proper inline-drawing representation remains necessary. |
| 4 | 1321.56 | `tiledrendering/data/page-background-link.fodt` | 1 / 1 | 0.11% | The background is a document-controlled external URL at 192.0.2.1. It is not fetched automatically. |
| 5 | 1318.58 | `layout/data/floattable-center-shift-down.docx` | 4 / 4 | 66.15% | Page counts match with the local reference version; floating-table avoidance and text geometry still differ. |
| 6 | 1303.92 | `layout/data/fdo43573-2-min.docx` | 22 / 15 | 52.76% | Two-column flow, anchored images and tables remain open. Column widths differ by only one twip. |
| 7 | 1299.68 | `ww8export/data/tdf91083.odt` | 6 / 7 | 20.97% | Nested tables, merged cells and row pagination remain open. |
| 8 | 1286.87 | `ooxmlexport/data/tdf141173_missingFrames.rtf` | 1 / 2 | 0% | LibreOffice has a blank first page. Earlier broad frame/page-break experiments failed independent probes and remain rejected. |
| 9 | 1279.43 | `ooxmlexport/data/tdf146955.odt` | 40 / 40 | 0% | Complex frame/resource layout remains open, including the known header-geometry visual regression documented for #143. Both counts hit the 40-page cap. |
| 10 | 1235.92 | `layout/data/ToC-in-linked-frame-on-first-page-style.fodt` | 2 / 5 | 61.35% | Cached index entries and linked-frame names survive. Generation-source metadata was incorrectly reported as lost block content; corrected here. Unstyled body headings still paginate differently. |

## Cached indexes and generation instructions

`ToC-in-linked-frame-on-first-page-style.fodt` contains an index inside a linked text frame. Its `text:table-of-content-source` describes generation rules and templates, while `text:index-body` holds the displayed cached entries. The reader previously reported the source as an unsupported, skipped content block despite retaining the actual entries.

The reader now skips only the matching source element inside each of the seven supported index containers, then reads the cached title and entries normally. Unknown index children still produce the existing unsupported-content warning. It does not regenerate indexes, refresh page-number fields, or import template text as displayed paragraphs. Tests cover all seven index types, linked-frame content/names, and unknown children.

The source-metadata warning disappears from the original input. The before/after runs confirm that all ten pixel and semantic-text check details and page counts remain unchanged. This is a diagnostic correction, not a visual rendering fix.

An independent LibreOffice FODT rewrite of this original gives the unstyled body `text:h` elements an application-generated `Heading_20_1` style: 18pt bold, 12pt before and 6pt after, inherited from `Heading`. That style and those values are absent from the source file. The Broiler model reads these headings with the declared default paragraph text size (12pt). This explains a concrete source of different pagination, but does not justify silently injecting LibreOffice-specific heading metrics into every ODF reader result. The linked-frame pagination case remains open.

## Validation and artifacts

Reports and original input downloads are outside the checkout under `/workspace/.setup`:

- `issue145-inputs.json` records SHA-256 hashes of the downloaded originals.
- `issue145-before.json`, `issue145-before.log`, `issue145-before-work`.
- `issue145-after.json`, `issue145-after.log`, `issue145-after-work`.
- `issue145-build.log`, `issue145-tests-final.log`, `issue145-corpus.json`.
- `issue145-normalized/ToC-in-linked-frame-on-first-page-style.fodt` (independent LibreOffice rewrite used for the heading investigation).

The Release solution build succeeds. All 3,896 executed unit tests pass (3 additional skips); the ODT project includes 485 passing tests. The CLI corpus passes 2,320/2,320 checks with 105 documented skips. The original ten comparisons remain at **12/30** checks (10 reads and 2 text checks; all 10 pixel checks remain open). No thresholds or corpus baselines are relaxed. Prior fixes and the known #143 visual tradeoffs are unchanged; see [the #143 investigation](office-issue-143.md).

Reproduce the original-document run after activating the environment and building Release:

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /workspace/.setup/issue145 --soffice /workspace/.setup/soffice26 \
  --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 --top-differences 10 \
  --report /workspace/.setup/issue145-after.json \
  --workspace /workspace/.setup/issue145-after-work --keep
```

The Office runner exits with 18 for the 18 remaining failed checks; those are recorded conformance differences, not a successful comparison run.
