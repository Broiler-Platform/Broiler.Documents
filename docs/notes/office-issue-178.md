# Issue #178: a page background linked outside the package

First of the [30 ranked differences in issue #178](https://github.com/Broiler-Platform/Broiler.Documents/issues/178):
`tiledrendering/data/page-background-link.fodt` (severity 1313.79). One page
each, but 99.86% of the blurred page differed (ink-profile similarity 0.226,
coverage delta 99.67%); the text check passed. A flat ODT with one paragraph
and a page layout whose only property is a legacy background picture:

```xml
<style:background-image xlink:href="http://192.0.2.1:12345/background.png"
    xlink:type="simple" xlink:actuate="onLoad" xlink:show="embed"
    style:position="center" style:repeat="no-repeat"/>
```

Path and input SHA-256 are the first line of
`tests/office/selections/issue-178.txt` and
`tests/office/selections/issue-178.sha256`, which also list the related
samples below; not vendored.

The notes for issues
[#138](office-issue-138.md#page-fills-and-rtf-field-text),
[#143](office-issue-143.md), [#145](office-issue-145.md),
[#147](office-issue-147.md) and [#164](office-issue-164.md) each blamed the
reader not fetching the picture. The reference never fetched it either:
`192.0.2.1` is TEST-NET-1 (RFC 5737), never routed, and references are made in
a container. The reference shows how LibreOffice paints a background whose
picture does not load.

## The reference: a page of `#729FCF`

The reference PDF (`551ac34d…cbe6abb7141372aec3e349c1075619e7a584c17d76992159.pdf`
in `office-references-v3`) draws one rectangle before the text over the whole
sheet, margins included:

```text
0.4470588235 0.6235294117 0.8117647058 rg
-0.05 841.939 m 595.25 841.939 l 595.25 0.039 l -0.05 0.039 l ... f*
```

That is `#729FCF`, `COL_DEFAULT_SHAPE_FILLING`, the default of
`XFillColorItem`. In LibreOffice 24.2:

- Writer turns a legacy background into fill attributes
  (`unobrushitemhelper.cxx`, `setSvxBrushItemAsFillAttributesToTargetSet`). A
  brush with a picture position becomes `FillStyle_BITMAP`; the function first
  clears every fill item, so the fill colour reverts to its default even when
  `fo:background-color` states one.
- `sdrattributecreator.cxx`, `createNewSdrFillAttribute`, builds the fill from
  the colour item and, for a bitmap, `createNewSdrFillGraphicAttribute`. A
  linked picture is a deferred graphic with an origin URL; the load fails, the
  graphic attribute is empty, and a solid fill in the item's colour remains.

The upstream test `testPageBackgroundImageRemoteNotFetched`
(`sw/qa/extras/tiledrendering/tiledrendering2.cxx`, added 2026-05-22) only
asserts that painting does not fetch the URL when link updates are not
allowed. Master adds exactly that guard to `createNewSdrFillGraphicAttribute`
and keeps the fallback, so this is not a reference defect: current LibreOffice
paints the same page.

## The other half: how much of the page a page fill covers

`broilerdoc` drew a legacy page colour or picture inside the margins only
(from [issue #140](office-issue-140.md)), right only for old documents.
`PageMasterImportContext.cxx`, `FillPropertySet_PageStyle`, bounds a page
style without a drawing-page style:

- by default, the whole page;
- `meta:generator` naming OpenOffice.org, AOO 4.x or LibreOffice before 6.3:
  any fill inside the margins;
- LibreOffice 6.3 and 6.4: a single or stretched picture inside the margins; a
  colour, gradient, hatch or tiled picture the whole page.

The producer is read as `xmlimp.cxx` (`DocumentInfo`) reads the build id
`xmlmetai.cxx` derives from `meta:generator`; a document without one, like
this sample, gets the whole page. `testPageStyleBackgroundFullSizeOOo`/`LO64`/
`LO70` (`odfexport3.cxx`) pin the three cases.

## Confirmed on LibreOffice 26.2

Authored flat ODT and ODT files converted with 26.2.6.3 (same code on these
paths), no generator:

- Page picture linked to `http://192.0.2.1:12345/…` (alone, beside
  `fo:background-color="#ff0000"`, or with 2 cm margins) or to a missing
  `file:///…` path: whole page `#729FCF`. With `draw:fill="bitmap"
  draw:fill-color="#00ff00"` on the page layout: whole page `#00ff00`.
- Page `draw:fill="bitmap"` with no image named, the name undefined or the
  package entry missing: whole page `#729FCF`.
- Package page picture `Pictures/missing.png`: nothing; beside
  `fo:background-color="#ff0000"`, whole page red.
- Shape bitmap fill linked to the URL: `#729FCF`; with
  `draw:fill-color="#00ff00"`, `#00ff00`; with a default graphic style
  `draw:fill-color="#ff00ff"`, `#ff00ff`.
- `fo:background-color` or an embedded picture with 3 cm margins: whole page.

The 24.2 references of `background-image-link.fodt`,
`paragraph-style-background-link.fodt` and
`two-paragraph-background-links.fodt` paint each linked paragraph's area
`#729FCF`, and `shape-fill-link.fodt` its shape.

## What changed

- **Unloaded bitmap fills.** A `draw:fill="bitmap"` whose picture does not load
  is painted in its `draw:fill-color`, the default graphic style's for a shape,
  or `#729FCF` (before, only when a colour was stated).
- **Linked legacy backgrounds.** A page or paragraph `style:background-image`
  linking outside the package is painted `#729FCF` over any background colour
  beside it, reporting `odt.fill` next to `odt.image.external`. Nothing is
  fetched (ADR 0004); this is what LibreOffice draws when the link does not
  load, the only case the reader can know. A legacy picture naming a missing
  package entry now leaves the background colour (it used to leave nothing).
- **Page fill bounds by producer.** `OdtMetadata.PageFillBounds` reads the
  producer as LibreOffice does, and a page layout's fill covers what that rule
  gives. Drawing-page fills keep their own `draw:background-size`.

## Verification

Tests: `OdtUnloadedBackgroundTests` (linked, missing and unnamed bitmaps on
pages, shapes and paragraphs; the producer parse; bounds by producer); five
assertions pinning the old behaviour were updated.

Over all 1,492 ODT samples (`.odt`, `.fodt`, `.ott`), `02a99e6` (main) →
this change: pixel passes 968 → 971, none lost; text and read unchanged. Ten
renders changed, listed in `tests/office/selections/issue-178.txt`. Newly
passing: `page-background-link.fodt` (blur difference 99.86% → 0.55%, profile
0.226 → 1.000); `odfexport/data/pagestyle_background_lo64.odt` (LibreOffice
6.4; stretched and single pictures now inside the margins; its worst-page
blur difference reads 0.06% → 12.8% only because the worst page moved from
the fixed page 4 to page 6, a hatch always 12.8% off);
`ww8export/data/tdf128608_fillStyleNoneB.odt` (LibreOffice 6.2; colour inside
the margins, 60.1% → 6.3%). Closer: `uiwriter/data/tdf145731.odt`
(LibreOffice 5.4, 15.4% → 0.3%), `odfimport/data/PageBackground.odt` (AOO
4.2, 14.1% → 5.3%). `pagestyle_background_ooo33.odt` (OpenOffice.org 3.3)
keeps its fills inside the margins.

## Not changed, seen on the way

- **`shape-fill-link.fodt`'s shape is the wrong size.** LibreOffice 24.2 draws
  the 12 cm x 6 cm custom shape as 142 x 14 pt around the stated box's centre,
  the size of its one text line. `broilerdoc` draws the stated box, now in the
  right colour, so blur difference rose 0.8% → 12.3% (failing before and
  after). Its text check fails because LibreOffice's export omits shape text.
- **A flat ODT naming no styles.** `two-paragraph-background-links.fodt` has no
  page layout and no default style. LibreOffice sets it in Liberation Serif,
  first baseline at 2 cm plus the ascent; `broilerdoc` uses a sans face about
  15 pt lower, so the now correctly coloured paragraph fills sit offset
  (profile similarity rose, blur difference barely moved).
- **Paragraph background pictures that do load** are not painted (only their
  colour). No ranked sample needs it yet.

## Environment and reproduction

- References `office-references-v3` (corpus `54dd3de1`; LibreOffice
  24.2.7.2, pdftoppm 24.02, 96 dpi, reference fonts) at
  `D:/scratch/office-references-v3`; LibreOffice 26.2.6.3 on Windows for the
  authored probes.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-178.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- A reference PDF's fills can be read from its content stream (the `rg`
  operators before the first `BT`); that, not a raster diff, gave the colour.
