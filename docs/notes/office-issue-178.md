# Issue #178: a page background linked outside the package

The first of the [30 ranked differences in issue #178](https://github.com/Broiler-Platform/Broiler.Documents/issues/178)
was `tiledrendering/data/page-background-link.fodt` (severity 1313.79): one
page each, but the pixel check found 99.86% of the blurred page different, an
ink-profile similarity of 0.226 and a coverage delta of 99.67%. The text check
passed. The document is a flat ODT with one paragraph and a page layout whose
only property is a legacy background picture:

```xml
<style:background-image xlink:href="http://192.0.2.1:12345/background.png"
    xlink:type="simple" xlink:actuate="onLoad" xlink:show="embed"
    style:position="center" style:repeat="no-repeat"/>
```

Its path and input SHA-256 are the first line of
`tests/office/selections/issue-178.txt` and `tests/office/selections/issue-178.sha256`,
which also list the related samples below. No document is vendored.

The sample had been ranked five times before. The notes for issues
[#138](office-issue-138-fills-fields-verification-2026-10-23.md),
[#143](office-issue-143.md), [#145](office-issue-145.md),
[#147](office-issue-147.md) and [#164](office-issue-164.md) each put the
difference down to the reader not fetching the external picture, as if the
reference showed it. It cannot: `192.0.2.1` is in TEST-NET-1 (RFC 5737), an
address range that is never routed, and the references are made in a container.
LibreOffice never loaded the picture either. What the reference shows is how
LibreOffice paints a background whose picture does not load.

## The reference: a page of `#729FCF`

The reference PDF (`551ac34d…cbe6abb7141372aec3e349c1075619e7a584c17d76992159.pdf`
in `office-references-v3`) draws one rectangle before the text, over the whole
sheet, margins included:

```text
0.4470588235 0.6235294117 0.8117647058 rg
-0.05 841.939 m 595.25 841.939 l 595.25 0.039 l -0.05 0.039 l ... f*
```

That colour is `#729FCF`, `COL_DEFAULT_SHAPE_FILLING`, the default of
`XFillColorItem`. LibreOffice 24.2's code accounts for it:

- Writer turns a legacy background into fill attributes
  (`unobrushitemhelper.cxx`, `setSvxBrushItemAsFillAttributesToTargetSet`). A
  brush with a picture position becomes `FillStyle_BITMAP`; the function first
  clears every fill item, so the fill colour falls back to its default even
  when `fo:background-color` states one.
- `sdrattributecreator.cxx`, `createNewSdrFillAttribute`, builds the fill from
  the colour item and, for a bitmap fill, from `createNewSdrFillGraphicAttribute`.
  A linked picture arrives as a deferred graphic with an origin URL; the load
  fails, the graphic attribute comes back empty, and what remains is a solid
  fill in the colour item's colour.

LibreOffice's upstream test for the sample, `testPageBackgroundImageRemoteNotFetched`
in `sw/qa/extras/tiledrendering/tiledrendering2.cxx` (added 2026-05-22), only
asserts that painting does not fetch the URL when link updates are not allowed.
Master adds exactly that guard to `createNewSdrFillGraphicAttribute` and keeps
the fallback, so this is not a reference defect: a current LibreOffice paints
the same page.

## The other half: how much of the page a page fill covers

`broilerdoc` drew a legacy page colour or picture inside the margins only. That
came from [issue #140](office-issue-140.md), and it is right only for old
documents. `PageMasterImportContext.cxx`, `FillPropertySet_PageStyle`, decides
the bounds of a page style that has no drawing-page style:

- the default is the whole page;
- a document whose `meta:generator` names OpenOffice.org, AOO 4.x or
  LibreOffice before 6.3 keeps any fill inside the margins;
- LibreOffice 6.3 and 6.4 keep a single or stretched picture inside the margins
  and give a colour, gradient, hatch or tiled picture the whole page.

The producer is read as `xmlimp.cxx` (`DocumentInfo`) reads the build id that
`xmlmetai.cxx` makes from `meta:generator`. A document without one, like this
sample, gets the whole page. LibreOffice's own tests
`testPageStyleBackgroundFullSizeOOo`/`LO64`/`LO70` (`odfexport3.cxx`) pin the
three cases.

## Confirmed on LibreOffice 26.2

Authored flat ODT and ODT files, converted with the host's LibreOffice 26.2.6.3
(the same code on these paths), give:

| Case | LibreOffice paints |
| --- | --- |
| Page picture linked to `http://192.0.2.1:12345/…`, no generator | whole page `#729FCF` |
| Same, beside `fo:background-color="#ff0000"` | whole page `#729FCF` |
| Same, with 2 cm margins | whole page `#729FCF` |
| Page picture linked to a missing `file:///…` path | whole page `#729FCF` |
| Page layout `draw:fill="bitmap" draw:fill-color="#00ff00"` and the link | whole page `#00ff00` |
| Package page picture `Pictures/missing.png` | nothing |
| Same, beside `fo:background-color="#ff0000"` | whole page red |
| Page `draw:fill="bitmap"`, no image named / name undefined / package entry missing | whole page `#729FCF` |
| Shape bitmap fill linked to the URL, no colour | shape `#729FCF` |
| Same, `draw:fill-color="#00ff00"` | shape `#00ff00` |
| Same, default graphic style `draw:fill-color="#ff00ff"` | shape `#ff00ff` |
| `fo:background-color` or embedded picture, 3 cm margins, no generator | whole page |

The 24.2 references of the sibling samples agree: `background-image-link.fodt`,
`paragraph-style-background-link.fodt` and `two-paragraph-background-links.fodt`
paint each linked paragraph's area `#729FCF`, and `shape-fill-link.fodt` its
shape.

## What changed

- **Unloaded bitmap fills.** A `draw:fill="bitmap"` whose picture does not load
  is painted in its `draw:fill-color`, the default graphic style's for a shape,
  or `#729FCF`. Before, it was painted only when a colour was stated.
- **Linked legacy backgrounds.** A page or paragraph `style:background-image`
  linking outside the package is painted `#729FCF`, over any background colour
  beside it, and reports `odt.fill` next to the existing `odt.image.external`.
  Nothing is fetched (ADR 0004); this is what LibreOffice draws when the link
  does not load, which is the only case the reader can know about. A legacy
  picture naming a package entry that is missing now leaves the background
  colour, where it used to leave nothing.
- **Page fill bounds by producer.** `OdtMetadata.PageFillBounds` reads the
  producer as LibreOffice does, and a page layout's fill covers what that rule
  gives it. Drawing-page fills keep their own `draw:background-size`.

## Verification

Replayed against `office-references-v3` (LibreOffice 24.2.7.2, pdftoppm 24.02,
96 dpi, the reference fonts) with `broilerdoc` built from `02a99e6` (main) and
from this change, over all 1,492 ODT samples (`.odt`, `.fodt`, `.ott`):

| Checks | Before | After |
| --- | ---: | ---: |
| Pixel passed / failed / skipped | 968 / 513 / 11 | 971 / 510 / 11 |
| Text passed / failed / skipped | 1,389 / 93 / 10 | 1,389 / 93 / 10 |
| Read passed / failed | 1,485 / 4 | 1,485 / 4 |

Three pixel checks pass that failed, and none that passed fails:
`page-background-link.fodt`, `odfexport/data/pagestyle_background_lo64.odt`
(LibreOffice 6.4; its stretched and single pictures now sit inside the margins)
and `ww8export/data/tdf128608_fillStyleNoneB.odt` (LibreOffice 6.2; its colour
now inside the margins, blur difference 60.1% -> 6.3%). Ten samples' renders
changed in all. `tests/office/selections/issue-178.txt` lists them, with
`pagestyle_background_ooo33.odt`, whose OpenOffice.org 3.3 fills stay inside
the margins as before.
Two that passed before came closer: `uiwriter/data/tdf145731.odt`
(LibreOffice 5.4, 15.4% -> 0.3%) and `odfimport/data/PageBackground.odt`
(AOO 4.2, 14.1% -> 5.3%). `shape-fill-link.fodt` and the paragraph samples are
described below. `pagestyle_background_lo64.odt`'s reported worst-page blur
difference rose (0.06% -> 12.8%) because the worst page moved from page 4, now
fixed, to page 6, a hatch that was always 12.8% off.

The ranked sample now passes its pixel check: blur difference 99.86% -> 0.55%,
profile similarity 0.226 -> 1.000.

`dotnet test Broiler.Documents.slnx -c Release`: 4,994 passed. The ODT tests
gain `OdtUnloadedBackgroundTests` (linked, missing and unnamed bitmaps on pages,
shapes and paragraphs; the producer parse; bounds by producer) and update five
assertions that pinned the old behaviour.

## Not changed, seen on the way

- **`shape-fill-link.fodt`'s shape is the wrong size.** LibreOffice 24.2 draws
  the 12 cm x 6 cm custom shape as 142 x 14 pt around the stated box's centre,
  the size of its one line of text. `broilerdoc` draws the stated box, now in
  the right colour, so this sample's blur difference rose (0.8% -> 12.3%); it
  failed before and after. Its text check fails because LibreOffice's text
  export leaves the shape's text out.
- **A flat ODT that names no styles.** `two-paragraph-background-links.fodt`
  has no page layout and no default style. LibreOffice sets its text in
  Liberation Serif with the first baseline at 2 cm plus the ascent;
  `broilerdoc` uses a sans face about 15 pt lower. The paragraph fills are now
  the right colour but sit offset, so the paragraph samples' profile similarity
  rose while their blur difference barely moved.
- **Paragraph background pictures that do load** are not painted (only their
  colour is). No ranked sample needs it yet.

## Environment and reproduction

- Reference set `office-references-v3` (corpus `54dd3de1`), unpacked at
  `D:/scratch/office-references-v3`; LibreOffice 26.2.6.3 on Windows for the
  authored probes.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-178.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- A reference PDF's fills can be read straight from its content stream (the
  `rg` operators before the first `BT`); that, not a raster diff, gave the
  colour.
