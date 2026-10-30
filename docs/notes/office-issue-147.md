# Issue 147: background activation and named outline lists

[Issue #147](https://github.com/Broiler-Platform/Broiler.Documents/issues/147)
ranks 30 Office differences. All 30 public originals were compared before and
after each round, starting from `main` at `eff23d7` (after PR #146); #143/#145
fixes are already present. Later rounds start from `bbf753f`, `7d742b8` and
`d69013b`.

Local runs use LibreOffice 26.8.1.1, poppler 26.05, 72 dpi, unpinned fonts, a
40-page cap and the unchanged 80% threshold; the issue used LibreOffice
26.2.6.3, poppler 26.01 and 144 dpi. Pages are Broiler / LibreOffice. No
baseline, threshold or fidelity assertion is relaxed. Reports, probe
generators and exact baseline CLIs were kept outside the checkout in the cloud
workspace's `/workspace/.setup`.

## Results and scope

The 30 originals improve from **39/89 to 41/89** passing checks (failures 50 →
48; one skip: LibreOffice produces no PDF for `tdf170672.docx` locally) and
stay there through the later rounds. `tdf126533_noPageBitmap.docx` now passes
all three checks; `tdf139843.odt` gains its text check. The runner exits with
48.

## DOCX: activate VML backgrounds only with an explicit colour attribute

The original has an embedded VML image and enabled
`w:displayBackgroundShape` but no `w:color` on `w:background`. Broiler painted
the bitmap; LibreOffice leaves the page blank. Variants confirm a missing
`w:color` also leaves solid and gradient VML backgrounds inactive, while an
explicit numeric colour or `auto` activates them, so a missing attribute
differs from explicit automatic.

The reader now leaves such backgrounds inactive and emits
`docx.background.color` when a VML child is present, before resource loading,
so inactive images are neither extracted nor fetched. This applies to page
backgrounds only, not VML shape fills. Eight native-reference variants
improve from 21/24 to 24/24; the original goes 2/3 → 3/3 (one page each,
removing a 41.81% blur blank-versus-ink difference). Existing gradient-angle
and VML colour-spelling fixtures now activate with `w:color="auto"`.

## ODT: resolve explicitly named outline styles in ordinary lists

`tdf139843.odt` uses `text:list text:style-name="Outline"` to name its
`text:outline-style`. The reader stored the outline under an internal
heading-numbering key, so list lookup reported it undefined and drew bullets.
List lookup now also resolves the outline by its declared name; an ordinary
`text:list-style` of the same name wins, and unknown names still warn. The
shared outline object stays separate, preserving heading numbering and style
counts. Labels such as `1.1.1.1`, `1.1.1.2` replace the bullets and the text
check passes.

**The original remains visually unresolved:** six vs seven pages, similarity
52.80% → 52.26%, blur 8.89% → 8.96% (8.77% after the later list rounds). List
indentation, typography and page flow remain open.

## All ranked originals

Areas carry over from [#145](office-issue-145.md), which has the native
evidence and rejected prototypes. Inline drawings, nested floating-table
overflow and variable-width paragraph continuation need their own model/layout
work; no guessed page breaks or font substitutions were added, and the
external page-background URL stays unfetched.

| Rank | Original under `sw/qa/extras` | Read / text / pixel | Remaining area or result |
| --- | --- | --- | --- |
| 1 | `ooxmlexport/data/tdf163894_hidden.docx` | pass / pass / fail | Long-paragraph layout, reference-version differences |
| 2 | `layout/data/tdf142694-1.odt` | pass / fail / fail | Nested tables, merged cells, repeated headers |
| 3 | `uiwriter/data/tdf147199.docx` | pass / fail / fail | Inline drawing-group flow footprint |
| 4 | `tiledrendering/data/page-background-link.fodt` | pass / pass / fail | External image not fetched |
| 5 | `layout/data/floattable-center-shift-down.docx` | pass / fail / fail | Floating-table avoidance, text flow |
| 6 | `layout/data/fdo43573-2-min.docx` | pass / fail / fail | Two-column text/image/table flow |
| 7 | `ww8export/data/tdf91083.odt` | pass / fail / fail | Nested/merged table pagination |
| 8 | `ooxmlexport/data/tdf141173_missingFrames.rtf` | pass / fail / fail | Framed RTF content, initial blank page |
| 9 | `ooxmlexport/data/tdf146955.odt` | pass / fail / fail | Complex frames/resources; inherited header regression |
| 10 | `ooxmlexport/data/tdf163894.docx` | pass / pass / fail | Long-paragraph layout, reference-version differences |
| 11 | `layout/data/ToC-in-linked-frame-on-first-page-style.fodt` | pass / fail / fail | Linked frames, application-generated heading metrics |
| 12 | `uiwriter/data/tdf135056.odt` | pass / fail / fail | Frames, cached vs generated index |
| 13 | `layout/data/tdf123163-1.docx` | pass / fail / fail | Unequal columns, page-relative VML geometry |
| 14 | `ooxmlexport/data/tdf123351_UnderlineGroupSapeText.docx` | pass / fail / fail | Grouped text positioning, underline |
| 15 | `ww8export/data/fdo66304-1.odt` | pass / fail / fail | Header/body geometry, text layout |
| 16 | `ooxmlexport/data/tdf132149_pgBreak.odt` | pass / pass / fail | Continuation across changing page styles |
| 17 | `ooxmlexport/data/tdf81345.docx` | pass / fail / fail | Multi-column tables, row pagination |
| 18 | `ooxmlimport/data/fdo87488.docx` | pass / pass / fail | SmartArt without supported static fallback |
| 19 | `uiwriter/data/tdf170672.docx` | pass / fail / skip | No local reference PDF |
| 20 | `layout/data/tdf170381-split-float-table-in-normal-table.docx` | pass / fail / fail | Cell-nested floating-table overflow |
| 21 | `layout/data/tdf116501.odt` | pass / fail / fail | Anchored image/text geometry |
| 22 | `ooxmlexport/data/FDO76248.docx` | pass / fail / fail | Table pagination, text metrics |
| 23 | `uiwriter/data/floattable-split.docx` | pass / fail / fail | Floating-table split, text placement |
| 24 | `uiwriter/data/tdf135623.docx` | pass / fail / fail | Text-frame flow, page count |
| 25 | `ooxmlexport/data/tdf126533_noPageBitmap.docx` | pass / pass / pass | Fixed: inactive VML background |
| 26 | `ooxmlexport/data/tdf87569_vml.docx` | pass / fail / pass | Table/text extraction |
| 27 | `ooxmlexport/data/fdo78883.docx` | pass / fail / fail | Columns/table text layout |
| 28 | `ooxmlexport/data/tdf127814.docx` | pass / fail / pass | List text extraction |
| 29 | `layout/data/pageBreakInHiddenSection.fodt` | pass / pass / fail | Page-style and hidden-paragraph break semantics |
| 30 | `uiwriter/data/tdf139843.odt` | pass / pass / fail | Numbering text fixed; visual layout open |

## Three further ODT list corrections

Nine authored FODT probes improve from 21/27 to 27/27.

1. **`text:numbered-paragraph` lost its text.** It is now numbered content, not
   an unsupported block. Paragraphs sharing a `text:list-id` share counters
   across intervening body text; separate IDs count independently. Stated
   levels and start values survive; cached `text:number` labels are not
   duplicated as text. ODT export uses the ordinary-list representation.
2. **Sibling sub-lists continued the previous sub-list.** Within one parent
   item a second sub-list restarts its level unless it explicitly continues.
   Explicit first-item starts win and are consumed once. Continuation
   propagates through structural wrappers with no parent paragraph, which
   keeps the deep-list fix in `tdf139843.odt`. The writer marks a resumed
   nested run as continuing when it starts below its parent level; a
   LibreOffice text export confirms the round-tripped `a), b), c)`.
3. **Explicit list alignment was replaced by a fixed nesting indent.** Modern
   `label-alignment` properties supply `fo:margin-left` and `fo:text-indent`,
   including zero and hanging indents, overriding paragraph-style margins.
   Continuation paragraphs keep the text indent without the first-line hang.
   Unspecified alignment keeps the nesting fallback. Exact ODT list-offset
   export stays on the diagnosed `odt.list.indent` approximation.

Rejected after native checks: numeric boolean spellings did not behave as
hypothesized in LibreOffice; completely empty `text:list-item` elements are
ignored there, not counted; an unconditional sub-list restart broke explicit
continuation through structural wrappers.

## Four further corrections: legacy alignment, image links, tab leaders and HTML numbering

Nine probes (six FODT, three HTML) improve from 24/27 to 27/27; the pixel
probes already passed the broad threshold, so the narrower observations
below are the evidence.

1. **Legacy ODF list alignment.** `text:space-before` plus
   `text:min-label-width` supply the text indent, the label width being the
   hanging offset; explicit zero is kept without a negative-zero difference.
   `label-alignment` stays authoritative when selected. The wide probe's
   marker now starts at x=80 px like LibreOffice (was x=38); font/gap geometry
   is not pixel-identical. Default ODT list exports state the default label
   origin and six-point marker gap instead of a hanging indent and no longer
   warn; arbitrary exact offsets keep the diagnosed export limitation.
2. **Clickable inline ODT pictures.** A `draw:a` around an inline frame applies
   the shared hyperlink policy and keeps the allowed target on the image run;
   disallowed targets warn and are dropped while the image stays. The writer
   emits `draw:a` with direct frame children, text formatting outside it.
   **LibreOffice's PDF export of the re-exported ODT
   (`four-image-link-corrected.odt`) still lacks the URI annotation** that the
   authored source has, so this is codec link preservation, not native
   clickable-export closure.
3. **Disabled tab leaders.** `style:leader-style="none"` overrides a retained
   `style:leader-text`. In the measured gap the old renderer painted 30 dark
   pixels, LibreOffice and the fix zero; dotted leaders stay enabled.
4. **Explicit HTML ordered-list counters.** `ol start` and `li value` produce
   numbering definitions/restarts; signed/zero starts and
   decimal/letter/Roman types are kept. Independent lists keep their own
   counters; a nested bullet list does not inherit the number definition. The
   HTML writer still flattens lists with its diagnostic.

Baseline tightened: the `lists/odt/docx` row drops eight obsolete indent-loss
expectations (now lossless), and `docx-package/docx/odt` no longer expects
`odt.list.indent` for default offsets.

Rejected: LibreOffice ignored the tested repeated text-table rows and refused
the constructed case-collision and URI-escaped image packages, so no package
lookup/cache/URI-decoding change was kept.

## Five further corrections: list identity, item paragraphs and precise offsets

Thirteen authored HTML/RTF probes improve from 32/39 to 39/39.

1. **Ordered-list types without a start.** `ol type="a"`, `A`, `i`, `I` choose
   the format without `start`/`li value`; nested ordered lists default to
   decimal.
2. **Independent ordered lists.** Each `ol`/`ul` has its own definition, so
   adjacent separate lists restart at one.
3. **One marker per list item.** Only the first paragraph at the item's own
   list identity/level is labelled; later paragraphs keep item indentation
   but neither label nor advance the counter. Nested items count
   independently; a parent continuation after a nested list is not a new
   item. Marker gap and typography still differ.
4. **Direct block text keeps paragraph formatting.** Direct text or a soft
   break inherits the walked container's paragraph style, so alignment
   survives in `div`, `section` and `blockquote`. The body root's own styles,
   container break transfer and a CSS box model are not implemented.
5. **Precise left margins.** HTML keeps exact left offsets (no 18-point
   rounding), accumulating container margins without leaking to later
   siblings. Logical list depth is independent of physical indentation. HTML
   export emits the resolved offset; RTF reads/writes `\liN` as twips (1/20pt
   precision). The 25-point HTML, nested-container and RTF probes now start at
   x=45 px like LibreOffice (was x=38).

Baseline: `odt-package/odt/html` drops its obsolete 28.346→36-point loss; the
RTF offset records only 28.346→28.35-point twip quantization;
`html-structure/html/markdown` keeps its known indent loss with the correct
30-point source value (was rounded 36). The public HTML originals
`tdf118579.html` and `table_border_1px.html` stay at 4/6.

## Still open

- Every ranked original except `tdf126533_noPageBitmap.docx` (see table).
- Native PDF export of ODT image hyperlinks.
- List indentation/typography in `tdf139843.odt`; legacy spacing mode and
  arbitrary tab-following behaviour.

## Reproduction

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /path/to/issue147-all --soffice /path/to/soffice26 \
  --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 \
  --report /path/outside/checkout/issue147-after.json \
  --workspace /path/outside/checkout/issue147-after-work --keep
```
