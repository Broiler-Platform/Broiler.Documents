# Issue #136: section colours and exact paragraph indents

The remaining rankings were fetched directly from
[GitHub issue #136](https://github.com/Broiler-Platform/Broiler.Documents/issues/136)
on 2026-10-22. This batch implements five further corrections:

1. **Section background paint.** ODT section colours were discarded unless the
   section also specified columns. The model now retains the colour, the shared
   paginator paints its text area across page breaks, and the ODT writer keeps
   it on export.
2. **Nested section colour precedence.** Closing an outer section previously
   discarded the inner section. The reader now resolves non-overlapping ranges,
   using the innermost opaque colour and returning to the outer colour afterward.
   Section style inheritance is resolved too. Existing outer-column behaviour
   is preserved, and adjacent equivalent ranges are merged.
3. **Exact left indents and zero overrides.** DOCX and ODT paragraphs retain
   point values instead of relying on rounded 18-point levels. An explicit zero
   can cancel a parent style's indent. Negative offsets remain representable.
4. **Right indents.** They now reduce the available wrapping/alignment width,
   including inside cells, and survive DOCX/ODT non-list paragraph round trips.
5. **First-line and hanging indents.** The first line gets its own width and
   offset; following lines return to the paragraph's body indent. DOCX list
   labels use their resolved hanging position, with style/direct attributes
   overriding numbering-level attributes individually. The PDF layout also
   places the marker separately so the gap before the body is retained.

Exact values appear in CLI JSON dumps and style comparisons. Editing a legacy
indent level clears the old exact left/first-line values so the edit takes effect.

## Measured impact

Six unchanged ranked samples were compared using LibreOffice 26.2.6.3, Poppler
26.07.0, Windows, unpinned fonts, 72 DPI and one job. These local measurements
are not a rerun of the Linux corpus at 144 DPI. An isolated temporary baseline
build preserves the preceding batch's reader/text fixes while excluding these
new changes; the original section comparison was also recorded before editing.

| Sample | Before | After |
| --- | --- | --- |
| `section-nested-with-pagebreaks.fodt` (rank 13) | Pixel fails; 70.77% blurred difference | **All three checks pass**; 3.81% blurred difference; 4 vs 4 pages |
| `tdf123163-1.docx` (rank 3) | 11 vs 16 pages | 12 vs 16 pages; visual/text checks still fail |
| `tdf127814.docx` (rank 12) | 16.33% blurred difference; worst-page profile 0.168 | 14.29% blurred difference; profile 0.185; visual/text checks still fail |
| `tdf83309.docx` (rank 5) | Pixel passes; 18.15% blurred difference; profile 0.943 | Pixel still passes; 18.19% blurred difference; profile 0.905; text still fails |
| `tdf95033.docx` (rank 18) | 3 vs 3 pages; pixel fails | Unchanged |
| `tdf142694-1.odt` (rank 4, regression control) | 5 vs 7 pages; pixel fails | Unchanged; earlier regression remains unresolved |

Combined checks improve from **9/18 to 10/18**, with no skips. Two pixel checks
pass instead of one. This is one newly passing document, not five complete
document fixes. The other scores remain mixed, including a small deterioration
for the already-passing `tdf83309.docx`.

## Verification and limits

**2,710 tests pass:** Model 156, DOCX 540, ODT 393, RTF 475, Pagination 170,
PDF 721 and CLI 255. The Release CLI build and `git diff --check` pass.
New authored tests cover reader inheritance/cancellation, both indent directions,
round trips, cell/body placement, list marker positions, wrapping width,
multi-page section painting, nested colour precedence and shifted ranges.

Authored DOCX and ODT exports through LibreOffice place a paragraph with a
7.5-point left indent and a +5.25/-5.25-point first-line offset at x=22.85/12.35,
with its second line at x=17.60. The model/PDF positions are 22.75/12.25 and
17.50, within the same 0.10-point exporter rounding offset observed in earlier
authored checks. A DOCX list with a 36-point body indent and 18-point hanging
label has LibreOffice x=28.10 for its marker and 46.10 for both body lines;
the shared paginator/PDF positions are 28 and 46. Baseline/font metrics and
some wrapping still differ; matching these offsets does not establish complete
layout equivalence.

ODT list-level positioning remains separate: its reader uses the existing list
layout, and writing exact per-paragraph list offsets reports `odt.list.indent`.
The standalone PDF layout reports unsupported section colours; the shared
paginator paints them. DOCX writing reports `docx.section.background` when a
section colour cannot be represented directly. These limitations are explicit,
not silent round-trip claims.

Changing ODT master-page chains, alternating page sizes, grouped drawing table
content, and the remaining table/image pagination failures were investigated
but are not fixed by this patch. No upstream document bytes, generated PDFs or
rendered images were added to the repository.
