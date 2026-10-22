# Office issue 135: empty cells, cell spacing and table breaks

The [GitHub report](https://github.com/Broiler-Platform/Broiler.Documents/issues/135)
was fetched again for this batch. Investigation focused on the remaining table
pagination gaps rather than adding more appearance properties.

## Three changes

1. **Empty ODT cells retain their line boxes.** A `table:table-cell` with no
   paragraph now receives one empty paragraph through the normal reader path.
   This retains its font defaults, paragraph limits, editable range and line
   height. Covered cells do not receive paragraphs. Previously entire empty
   tables could disappear and empty spacer rows could collapse. This affects
   `odfimport/data/tdf103025.odt`, which contains 216 paragraph-less cells.
   PDF row pagination also now checks occupied space rather than drawn glyphs:
   continuation pages containing only empty rows used to let later text fall
   below the page and disappear from rendered output.
2. **Spacing before the first cell paragraph is honored.** Both layout engines
   include `SpacingBefore` at the top of a cell, inside its padding, and account
   for it in row height. It was previously dropped on the first paragraph in
   every cell, including nested flows.
3. **ODT table breaks are represented.** Page/column breaks can precede an
   inline body table. The reader retains its own `fo:break-before`, carries a
   preceding block's `fo:break-after` onto the table, and carries a table's
   trailing break to the following paragraph or table. The model preserves the
   flags through paragraph edits and the ODT writer writes them. The shared
   paginator handles page/column breaks, ignores them in continuous output, and
   avoids a blank page for a break on the opening table. The PDF writer treats
   column breaks as page breaks, as it already does for paragraphs.

DOCX has no direct table-level break property. Writing these flags reports
`docx.table.break`; it does not silently claim to preserve them by putting the
break inside a cell. Breaks before nested tables inside cells remain outside the
layout implementation. Existing floating-table behavior is unchanged.

## Verification

2,214 tests passed across Model (156), DOCX (533), ODT (388), Pagination (165),
PDF (718), and CLI (254). Regressions cover empty cells and covered cells, font
inheritance, reader limits, round trips, block-to-table and table-to-table
breaks, column placement, continuous output, opening-table breaks, cell spacing,
and PDF continuation pages containing only empty rows.

Temporary authored documents were exported with LibreOffice 26.2.6.3:

- Eight DOCX/ODT exports compare 0 versus 7 points before the first cell
  paragraph, both at the start of the document and after body text. LibreOffice
  moves the cell text down exactly 7 points in every case. This confirms the
  space belongs inside the cell even when its table opens the page.
- An ODT with a break before a table places `before` on page one and `cell` /
  `after` on page two in both LibreOffice and the PDF codec.
- An ODT with twelve 30-point rows, ten of them holding no paragraph markup,
  produces six pages in both exporters: `first` on page one, four blank
  continuation pages, and `last` on page six. The PDF regression specifically
  prevents the final text from being positioned below an overfilled blank page.

The unchanged seven upstream files from the previous batch were compared at
72 DPI with one job, a 40-page cap, unpinned Windows fonts, LibreOffice 26.2.6.3
and Poppler 26.07.0. These are local results, not a Linux corpus rerun.

| Sample | Previous batch | This batch |
| --- | --- | --- |
| `tdf103025.odt` | 3 vs 5 pages; 1.58% blurred difference | 6 vs 5 pages; 1.38% blurred difference; worst-page profile 0.548 to 0.390 |
| `tdf150606-1-min.odt` | 5 vs 5 pages; worst-page profile 0.490 | 5 vs 5 pages; worst-page profile 0.612; blurred difference 7.96% to 7.99% |
| `tdf165156.odt` | 2 vs 3 pages; worst-page profile 0.232 | 2 vs 3 pages; worst-page profile 0.178; blurred difference 0.51% to 0.54% |
| Other four samples | Existing results | Page counts, check outcomes and reported metrics unchanged |

**No additional checks pass:** both runs pass 12/21 checks with no skips, and
all seven pixel checks still fail. The empty-cell change reduces the absolute
page-count gap for `tdf103025.odt` from two pages to one, but overshoots the
reference and worsens its worst-page profile. It is not a complete document fix.
The earlier `tdf142694-1.odt` regression remains at 5 versus 7 pages.

An early probe with explicit empty paragraphs produced five pages locally;
the final implementation/run produces six. The final measurement above is the
reported result. Inherited row/column styles were also investigated as a possible
cause; that isolated probe did not change the original sample and no such style
resolution change was retained in this batch.

No upstream sample bytes or generated integration files were added to the
repository. Missing automatic table fitting, default-font differences, nested
table breaks, merged-row allocation and exact-row clipping remain separate work.
