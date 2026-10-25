# Issue 147: background activation and named outline lists

[Issue #147](https://github.com/Broiler-Platform/Broiler.Documents/issues/147) ranks 30 major Office differences. All 30 public originals were downloaded and compared before/after these changes. Work starts from `main` at `eff23d7` (after PR #146). Earlier #143/#145 fixes are already present and are not counted as new fixes here.

## Results and scope

The local comparison improves from **39/89 to 41/89 passing checks**, with failures decreasing from 50 to 48 and one unchanged skipped pixel reference (`tdf170672.docx`: LibreOffice produces no PDF locally). All 30 reads pass. One additional original, `tdf126533_noPageBitmap.docx`, now passes all three checks. `tdf139843.odt` gains a passing text check but retains its visual failure. Every check outcome/detail for the other 28 originals is unchanged.

Comparisons use LibreOffice 26.8.1.1, poppler 26.05, 72 dpi, unpinned fonts, a 40-page cap and the unchanged 80% threshold. The issue uses LibreOffice 26.2.6.3, poppler 26.01 and 144 dpi. These are local results, not exact reproduction of that environment. Page counts are Broiler / LibreOffice. Scores describe individual rendering checks, not overall conformance percentages.

## DOCX: activate VML backgrounds only with an explicit colour attribute

The failing original has an embedded VML image and enabled `w:displayBackgroundShape`, but no `w:color` attribute on `w:background`. The reader painted its bitmap, while native LibreOffice leaves the page blank. Independent variants of the original confirm that missing `w:color` also leaves solid/gradient VML backgrounds inactive. Explicit numeric colour or `auto` activates the background; a missing attribute must therefore be distinguished from an explicit automatic value.

The reader now leaves such backgrounds inactive and emits `docx.background.color` when a VML child is present. It does so before resource loading, so inactive images are not extracted or fetched. Explicit automatic/numeric colours keep the supported background pipeline. This applies to page backgrounds, not ordinary VML shape fill colours. Eight native-reference variants cover absent/explicit colour, bitmap, solid and gradient paint; checks improve from **21/24 to 24/24**. The original improves from **2/3 to 3/3** checks (one page in both renderers), removing its previous 41.81% blur / blank-versus-ink difference.

Six new unit cases cover missing-colour activation/resource handling and explicit automatic/numeric activation. Existing gradient-angle and VML colour-spelling fixtures now explicitly activate their background with `w:color="auto"`; their colour/geometry assertions are unchanged. Missing-activation behaviour has separate assertions rather than weakening those colour tests.

## ODT: resolve explicitly named outline styles in ordinary lists

`tdf139843.odt` uses `text:list text:style-name="Outline"` to name its `text:outline-style`. The reader stored that definition under an internal heading-numbering key, so ordinary list lookup incorrectly reported it as undefined and produced bullets. List lookup now also resolves the outline definition by its declared name. An ordinary `text:list-style` with the same name retains precedence; genuinely unknown names still warn. The shared outline object remains separately stored, preserving heading numbering and style counts.

Three new unit cases cover standard/custom outline names, nested labels, ODT round trips, name collisions and unknown names. In the original, labels such as `1.1.1.1`, `1.1.1.2` replace the incorrect bullets and the full semantic text check passes.

**The original remains visually unresolved.** Pagination stays six vs seven pages, and similarity changes slightly adversely from 52.80% to 52.26%; worst-page blur changes from 8.89% to 8.96%. This is a verified numbering/text correction, not an overall visual improvement claim. List indentation, typography and broader page flow still need work.

## All ranked originals

The areas below include retained findings from the earlier investigations, not claims that every larger layout feature was reimplemented in this change. Complex inline drawings, nested floating-table overflow and variable-width paragraph continuation require their own model/layout work; guessed page breaks or font substitutions were not added. The external page-background URL remains unfetched. See [the earlier investigation](office-issue-145.md) for native evidence and rejected prototypes.

| Rank | Original under `sw/qa/extras` | Read / text / pixel | Remaining area or result |
| --- | --- | --- | --- |
| 1 | `ooxmlexport/data/tdf163894_hidden.docx` | pass / pass / fail | Long-paragraph layout and reference-version differences |
| 2 | `layout/data/tdf142694-1.odt` | pass / fail / fail | Nested tables, merged cells and repeated headers |
| 3 | `uiwriter/data/tdf147199.docx` | pass / fail / fail | Inline drawing-group flow footprint |
| 4 | `tiledrendering/data/page-background-link.fodt` | pass / pass / fail | External image is not fetched |
| 5 | `layout/data/floattable-center-shift-down.docx` | pass / fail / fail | Floating-table avoidance and text flow |
| 6 | `layout/data/fdo43573-2-min.docx` | pass / fail / fail | Two-column text/image/table flow |
| 7 | `ww8export/data/tdf91083.odt` | pass / fail / fail | Nested/merged table pagination |
| 8 | `ooxmlexport/data/tdf141173_missingFrames.rtf` | pass / fail / fail | Framed RTF content and initial blank page |
| 9 | `ooxmlexport/data/tdf146955.odt` | pass / fail / fail | Complex frames/resources; inherited header regression |
| 10 | `ooxmlexport/data/tdf163894.docx` | pass / pass / fail | Long-paragraph layout and reference-version differences |
| 11 | `layout/data/ToC-in-linked-frame-on-first-page-style.fodt` | pass / fail / fail | Linked frames and application-generated heading metrics |
| 12 | `uiwriter/data/tdf135056.odt` | pass / fail / fail | Frames and cached/generated index differences |
| 13 | `layout/data/tdf123163-1.docx` | pass / fail / fail | Unequal columns and page-relative VML geometry |
| 14 | `ooxmlexport/data/tdf123351_UnderlineGroupSapeText.docx` | pass / fail / fail | Grouped text positioning and underline fidelity |
| 15 | `ww8export/data/fdo66304-1.odt` | pass / fail / fail | Header/body geometry and text layout |
| 16 | `ooxmlexport/data/tdf132149_pgBreak.odt` | pass / pass / fail | Paragraph continuation across changing page styles |
| 17 | `ooxmlexport/data/tdf81345.docx` | pass / fail / fail | Multi-column tables and row pagination |
| 18 | `ooxmlimport/data/fdo87488.docx` | pass / pass / fail | SmartArt without supported static fallback |
| 19 | `uiwriter/data/tdf170672.docx` | pass / fail / skip | Reference PDF unavailable in local environment |
| 20 | `layout/data/tdf170381-split-float-table-in-normal-table.docx` | pass / fail / fail | Cell-nested floating-table overflow |
| 21 | `layout/data/tdf116501.odt` | pass / fail / fail | Anchored image/text geometry |
| 22 | `ooxmlexport/data/FDO76248.docx` | pass / fail / fail | Table pagination and text metrics |
| 23 | `uiwriter/data/floattable-split.docx` | pass / fail / fail | Floating-table split and text placement |
| 24 | `uiwriter/data/tdf135623.docx` | pass / fail / fail | Text-frame flow and page count |
| 25 | `ooxmlexport/data/tdf126533_noPageBitmap.docx` | pass / pass / pass | Fixed: inactive VML background; all three checks pass |
| 26 | `ooxmlexport/data/tdf87569_vml.docx` | pass / fail / pass | Table/text extraction; pixel check already passes locally |
| 27 | `ooxmlexport/data/fdo78883.docx` | pass / fail / fail | Columns/table text layout |
| 28 | `ooxmlexport/data/tdf127814.docx` | pass / fail / pass | List text extraction; pixel check already passes locally |
| 29 | `layout/data/pageBreakInHiddenSection.fodt` | pass / pass / fail | Page-style and hidden-paragraph break semantics |
| 30 | `uiwriter/data/tdf139843.odt` | pass / pass / fail | Fixed numbering text; visual layout remains open |

## Validation and reproduction

Release solution build succeeds; **3,978 executed unit tests pass** with three additional skips. The CLI corpus passes **2,320/2,320 checks**, with 105 documented skips. No corpus baseline, comparison threshold or fidelity assertion was relaxed. Known inherited regressions documented for #143/#145 remain visible in those notes.

Artifacts under `/workspace/.setup`, outside the checkout:

- `issue147-inputs.json`: original paths and SHA-256 hashes.
- `issue147-before.json` / `issue147-after.json`, logs and retained workspaces: all 30 originals, identical source bytes and settings.
- `issue147-background-probes`, `issue147-background-probes-baseline.json` / `issue147-background-probes-after.json`: eight native-reference variants. The isolated `issue147-background-before-bin` preserves the exact pre-background-change CLI; its ODT list correction does not affect these DOCX probes.
- `issue147-outline-after.json`: independent targeted original comparison.
- `issue147-build2.log`, `issue147-tests.log`, `issue147-corpus.json` and JUnit output.

After activating the environment and building Release:

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /workspace/.setup/issue147-all --soffice /workspace/.setup/soffice26 \
  --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 \
  --report /workspace/.setup/issue147-after.json \
  --workspace /workspace/.setup/issue147-after-work --keep
```

The comparison exits with 48 for the remaining failed checks. It does not close #147.


## Three further ODT list corrections

Starting from `bbf753f`, three independently reproduced list defects are corrected:

1. **Numbered paragraphs lost their entire text.** `text:numbered-paragraph` is now read as numbered content instead of an unsupported block. Paragraphs sharing a `text:list-id` share their counters across intervening body text; separate IDs count independently. Stated levels and start values survive, while cached `text:number` labels are not duplicated as body text. ODT export uses the existing ordinary-list representation and retains the tested content/labels on round trip.
2. **New sibling sub-lists continued the previous sub-list by default.** Within one parent item, a second sub-list now starts its own level afresh unless it explicitly continues. Explicit first-item start values win and are consumed once. Continuation propagates through structural wrappers with no actual parent paragraph, preserving the existing deep-list text correction in `tdf139843.odt`. The writer marks a resumed nested run as continuing when it starts below its parent level; an independent LibreOffice text export confirms the round-tripped `a), b), c)` sequence.
3. **Explicit list alignment was replaced by a fixed nesting indent.** Modern `label-alignment` list-level properties now supply `fo:margin-left` and `fo:text-indent`, including explicit zero and hanging indents. They override paragraph-style margins; continuation paragraphs retain the text indent but omit the first-line hang. Unspecified alignment retains the existing nesting fallback. This is import/model placement support: exact ODT list-offset export remains on the existing diagnosed approximation path (`odt.list.indent`). Legacy spacing mode, arbitrary tab-following behaviour and broader typography are not claimed resolved.

Nine authored native-reference FODT documents cover three numbered-paragraph cases, sibling restart/continuation, structural-wrapper continuation and three explicit alignments. Identical final fixtures improve from **21/27 to 27/27 checks**; all nine read/text/pixel comparisons pass. These are authored regression documents, not nine ranked originals. The baseline CLI snapshot `issue147-three-before-bin` is the pre-change `bbf753f` implementation. Eleven new unit cases cover content preservation, counters, levels, restarts, empty structural items, explicit alignment and ODT round trips. The Release build succeeds and **3,989 executed unit tests** pass, with three additional skips.

The final all-30-original comparison remains **41/89 passing checks**, 48 failures and one unchanged skipped pixel reference. No previously passing check is lost and no additional ranked original is fully resolved by these three changes. Twenty-nine originals have identical check outcomes/details to `issue147-after.json`. Only `tdf139843.odt` changes a measured visual detail: worst-page blur improves from 8.96% to 8.77%, while reported similarity remains 52.26% and pagination remains six vs seven pages; its semantic text check stays passing. The CLI corpus passes **2,320/2,320** checks with 105 documented skips. No fidelity assertion was relaxed.

Two early experiments were rejected after native checks. Numeric boolean spellings did not behave as hypothesized in the local LibreOffice reference, and completely empty `text:list-item` elements are ignored rather than counted there. Neither experiment is retained. An initial unconditional sub-list restart also lost the original's explicit continuation through structural wrappers; the retained rule and a separate regression/native fixture cover that distinction. Intermediate `odt-three-after.json` and `issue147-three-after-final.json` describe rejected/incomplete states, not the retained implementation.

Artifacts under `/workspace/.setup`: `make-odt-three-probes.py`, `odt-three-probes`, `odt-three-inputs.json`, `odt-three-baseline-verified.json`, `odt-three-after-verified.json`, `issue147-three-verified.json`, `odt-three-final-build.log`, `odt-three-final-tests.log`, `odt-three-corpus-verified.json`, and `odt-three-native-export/odt-three-nested-continue-roundtrip.txt`. Source/reference versions, DPI, font limitations, page cap and thresholds remain those documented above. No comparison threshold or corpus baseline was changed.
