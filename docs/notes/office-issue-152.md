# Issue 152: inline boundaries, RTF/VML shapes, table grids and theme colors

[Issue #152](https://github.com/Broiler-Platform/Broiler.Documents/issues/152)
ranked 30 originals. The [exact selection](../../tests/office/selections/issue-152.txt)
keeps the whole control group; its
[SHA-256 manifest](../../tests/office/selections/issue-152.sha256) records the
upstream bytes (`sha256sum -c` from an extras directory). Sources stay external.
The work started from main `f444f29`; each follow-up below names its base
commit. All local comparisons use LibreOffice 26.8.1.1, poppler 26.05.0,
72 DPI, the 80% threshold, a 40-page cap, a 30-second reference timeout and
unpinned fonts, with an isolated copy of the pre-change CLI as the before side.
The issue itself used LibreOffice 26.2.6.3, poppler 26.01.0 and 144 DPI, so
these are local before/after comparisons, not CI reproductions. The follow-ups
fix independently tested behaviors (authored controls plus unit tests), not
the same number of ranked originals. No threshold, baseline classification or
tool approval changed in any pass. The authored controls came from uncommitted
local generators in `/workspace/.setup` (`make-issue152-probes.py`,
`make-issue152-ten-probes.py`, `make-issue152-vml-probes.py`,
`make-issue152-rtf-group-probes.py`, `make-issue152-table-probes.py`,
`make-issue152-word-color-probes.py`).

```bash
dotnet run --project src/tests/Broiler.Documents.Office -c Release -- \
  --extras-dir /path/to/extras --sample-list tests/office/selections/issue-152.txt \
  --soffice /path/to/soffice --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 \
  --top-differences 30 --triage-dir /tmp/issue152-before
# After the fix, same inputs and reference environment:
# --triage-dir /tmp/issue152-after --previous-triage /tmp/issue152-before/triage.json
```

The 30 originals went from **39/89 to 40/89** passing checks in the first pass
(one skipped reference) and stay at 40/89 (49 failed) through every follow-up.

## Inline page/column boundaries and missing master pages

- A DOCX page break between two runs in one paragraph produces paragraph
  fragments instead of a line separator. Column breaks preserve the next-column
  boundary, or the next page in a one-column section.
- Explicit numbering stays on the first fragment; continuations get no marker
  and do not restart the counter. A leading column break in a numbered item
  keeps the empty numbered fragment before the boundary and the following item
  label (as native import does); a leading page break keeps the existing
  paragraph/item identity.
- Fragments keep inline formatting, paragraph-mark formatting and indentation.
  Only the outer fragments keep before/after spacing; the first fragment's
  keep-with-next is cleared; continuations have no first-line indent.
  `docx.pagebreak.fragment` reports the approximation.
- An ODT paragraph/table style naming an absent master-page definition still
  requests its page boundary, keeping the active paper and running content. Such
  requests are ignored inside hidden sections/cells, and an explicit empty
  master-page name cancels inheritance.

Evidence: four authored DOCX controls go 8/12 -> 12/12.
`pageBreakInHiddenSection.fodt` now has 3 pages like LibreOffice (was 2).
`tdf153964_numberingAfterBreak14.docx` passes its text comparison and goes
2 -> 3 pages (LibreOffice: 4). Both still fail visually.

## RTF shape properties

Base `17c6c81`. Ten shared RTF shape-path corrections, motivated by the ranked
RTF graphics cases (unit tests: `RtfShapePropertyTests`):

- Explicit `fFilled=0` wins regardless of color-property order.
- `fFlipH` composes with `fFlipV` and signed endpoint orientation.
- `posrelh=1` converts page offsets to the model's text-column origin.
- All four EMU text insets and `anchorText` (top/center/bottom) are read and
  written.
- Fill, gradient-end and outline opacity survive reading and writing.
- Drawn shapes with omitted fill properties default to white (Word and native
  reference); picture frames keep their fallback.
- `shpfblwtxt` / `fBehindDocument` select the layer independently of z-order;
  the writer emits it.
- Square/top-and-bottom/none wrapping, representable side and clearance survive.

Decision: omitting `fHidden` shapes was rejected - LibreOffice still paints
them and omission regressed a passing visual check. Hidden objects are not
preserved.

Evidence: eleven controls 30/33 -> 31/33 (all 11 pixel checks pass; two text
failures remain). Native import of exported RTF confirms 20/10/20/0-pt insets
and 50% fill opacity, but LibreOffice's FODT export drops `anchorText` and
`lineOpacity`, so native-consumer fidelity is limited. Originals unchanged.

Limits: one model wrap distance (asymmetric clearances use the maximum and
export symmetrically); both-side/contour wrap approximated (contour diagnosed);
page-to-column conversion ignores multi-column origin differences.

## VML shapes

Base `f30762f`. Ten DOCX/VML import corrections:

- Every supported drawn sibling in one `w:pict` is read (mixed blocks keep the
  first-image fallback).
- VML groups map child coordinates through group origin, nonuniform scale and
  nested groups into point-based shapes.
- Local shape-type chains are resolved: paint/geometry defaults inherit, with
  instance attribute and fill/stroke overrides.
- Fill opacity (decimal, percentage, 65536-based, independent gradient-end
  alpha) and stroke opacity are retained.
- `polyline` applies CSS left/top and margins in addition to its points.
- Wrap side and maximum clearance; named horizontal (left/center/right against
  page or text area) and vertical (top/center/bottom against page or margin)
  placement.
- Line flips compose; unitless line endpoints are pixels, explicit points stay
  points.

Native rules found: `line` endpoints already state the position and do not
get the CSS translation that `polyline` gets; shape-type layout dimensions are
not inherited as instance CSS. Type cycles, bad group dimensions, excessive
nesting and unsupported transforms are bounded or diagnosed.

Evidence: eleven `position:absolute` controls 32/33 -> 33/33 (vertical
placement). `ooxmlexport/data/tdf81345.docx` has mixed/worse observations on the
same worst page (ink-box delta 22 -> 43 px, blur 29.8011% -> 29.8040%; still 7
pages vs 5) with unchanged checks; model serialization is identical for old and
new readers and the rendering cause is not established.

## RTF groups, positioning and formatting

Base `07073fa`. Ten RTF reader corrections:

- Relative-only drawing groups expand through parent origins/scales, including
  nested groups and independent child paint.
- Only the owning `shpinst` scope commits a shape (no duplicate paint from
  anonymous formatting scopes).
- `dhgt` orders shapes; explicit `shpz` (including zero) takes precedence.
- `posh` left/center/right and `posv` top/center/bottom resolve against the
  page or margin area; `posyc` / `posyb` do so for exact-height frames.
- Repeated clearances keep the last value per edge, then the model maximum.
- Non-default underlines and double strike keep a diagnosed single-line
  approximation, including off toggles.
- `chcbpat` sets the run background; `plain` resets it.

The normalizer keeps the diagnosed fallback for rotated/flipped groups,
excessive nesting, mixed absolute/relative child bounds, missing bounds,
aggregate group wrapping and unsupported group-level content. Opaque metadata,
running bands and notes are not rewritten as body groups.

Evidence: eleven controls 29/33 -> 32/33 (one text-order mismatch remains).
The nested-group probe passes the broad pixel check but differs natively:
LibreOffice paints the outer 200-pt width, the model composes the relative
120-pt child width. `rtfimport/data/tdf84684.rtf` keeps two formerly missing
grouped shapes (ink profile 0 -> 0.9103, coverage delta 0.3615% -> 0.2218%) but
still fails: page sizes differ (612x792 vs 596x842 px at 72 DPI) and blur
worsens 0.1930% -> 0.4911% because the blank page used to score better.

## Table grids and styles

Base `637f2aa`. Ten DOCX table corrections:

- Import: `gridBefore` offsets cells into their declared columns; a grid-less
  row keeps its trailing extent.
- Export: `gridBefore` / `gridAfter` are emitted from model cell positions
  against the complete grid (after merge-continuation cells are synthesized).
- `tblStyleRowBandSize` (including inherited) and `tblStyleColBandSize` are
  honored.
- Row `tblPrEx` shading and per-edge cell margins apply; cell-level values win.
- Without `tblGrid`, widths are reconstructed from positive absolute `tcW`,
  `wBefore` and `wAfter` (fallback, not Word autofit; spans spread evenly; grid
  counts bounded and diagnosed).
- Vertical merges require matching start column and span along the chain.
- Omitted edge cells count when selecting first/last-column formats.

Decisions: banding after a horizontally merged cell keeps the native
cell-count rule, not grid-column parity. Inheriting style row heights and table
looks was rejected - the height change broke a pixel check.

Evidence: with a one-real-cell sparse model, the new writer's `gridBefore=1` /
`gridAfter=1` makes LibreOffice place text at x=122.1 pt instead of 72.1 pt.
Ten controls stay 30/30; LibreOffice still alternates bands, ignores the row
exception paint, and autofits/accepts malformed merges differently. Row
exception margins appear in two ranked originals but their zero values change
nothing.

## Word theme colors

Base `8335698`. Theme colors now resolve for run foreground, run background,
paragraph background, table/cell backgrounds and table/cell borders. A shared
WordprocessingML resolver maps Word theme names (text/background/dark/light and
hyperlink slots), looks up the palette and applies byte tint/shade. A resolved
theme beats its cached RGB (as specified; see the
[python-docx color analysis](https://github.com/python-openxml/python-docx/blob/master/docs/dev/analysis/features/text/font-color.rst));
missing themes fall back to RGB without the modifiers. Luminance uses truncated
byte channels (Text 2 `1F497D` tint `99` -> `548DD4`; `4F81BD` shade `BF` ->
`366091`); invalid tone bytes are ignored. DrawingML color handling is separate
and unchanged. Shading respects `nil`, uses the foreground for `solid` and keeps
highlight precedence; explicit `auto` text color clears inherited color.
Export writes explicit RGB; live theme bindings are not kept.

Evidence: six controls stay 18/18, but the tested LibreOffice paints the stale
RGB cache, so this does not prove parity. Native import of the exported DOCX
shows `#336699` text, `#8BB2D8` paragraph/table background, `#19334C` run
background and `#8BB2D8` / `#19334C` borders. Only
`ooxmlexport/data/tdf153964_numberingAfterBreak14.docx` changed (profile
0.00928856 -> 0.00928693). Combined tint+shade uses a tint-first fallback.

## Still open

- Inline boundaries inside table cells keep the line-break approximation; row
  pagination ignores them. Paragraph borders, identity and editing semantics
  can differ; repeated/trailing empty pages remain.
- Full RTF drawing-group model: rotated/flipped groups, grouped text
  extraction, hidden objects, text-sized/paragraph-relative frame centring,
  exact patterned/word-only underline and double-strike geometry.
- VML: rotated/mirrored groups, grouped lines, physical-unit child coordinates,
  grouped images, group-level named alignment and exact path geometry. Export
  uses the DrawingML shape writer.
- Table autofit (percentages, conflicting rows, content sizing).
- Custom `clrSchemeMapping`, patterned shading beyond solid/clear, dynamic theme
  editing.
- `tdf81345.docx` worse observations without a known cause.
