# Issue #136: lists, symbols and default RTF pages

Source: [GitHub issue #136](https://github.com/Broiler-Platform/Broiler.Documents/issues/136),
fetched directly through the GitHub connector on 2026-10-22. The issue compares
3,878 documents and ranks 30 differences. This batch addresses five shared
defects behind ranked samples; it does not resolve the five highest-ranked
documents or claim a corpus-wide improvement.

## Five corrections

1. **Deleted ODT headings keep their outline numbering.** The ordinary heading
   reader handled `text:outline-level`, but the separate tracked-deletion path
   did not. Both now use the same outline reference resolution. This restores
   the missing headings in `tdf130680.odt` (rank 11), and some labels in
   `fdo66304-1.odt` (rank 20).
2. **Only the first paragraph of an ODT list item receives its label.** Following
   paragraphs keep the indent without consuming another number. Nested items
   have their own first-paragraph state. This applies to ordinary content and
   the separate deleted-content reader.
3. **Text export includes fallback list labels.** A list without a named
   definition already received a bullet in rendering, but its label disappeared
   in the text dump. Both paths now use the numberer's fallback. This fixes the
   semantic comparisons for `tdf163230.fodt` and `tdf163285.fodt` (ranks 21/30).
4. **DOCX symbols preserve their character and font.** `w:sym` was skipped,
   losing 30 checkbox symbols in `tdf123705.docx` (rank 17). Valid hexadecimal
   character codes now retain their symbol font and adjacent run formatting;
   invalid codes are diagnosed. Authored tests also cover a DOCX round trip.
5. **RTF files without paper dimensions have an explicit default page.** They
   previously lost page geometry and used the renderer's fallback. The reader
   now uses LibreOffice's observed Letter page with one-inch margins; stated
   page/section properties override those defaults. This fixes the geometry
   comparison for `fdo45183.rtf` and `tdf81943.rtf` (ranks 16/28).

An investigated DOCX counter change was discarded. Authored LibreOffice exports
confirmed that numbering instances sharing an abstract definition can count
together. Making every instance independent would regress that case. The final
patch leaves DOCX counter identity unchanged; `mixednumberings.docx` remains a
failing control sample.

## Verification

**2,697 tests pass:** Model 156, DOCX 538, ODT 390, RTF 475, Pagination 165,
PDF 718 and CLI 255. The CLI Release build succeeds and `git diff --check`
passes. Fixtures committed with the patch are authored tests, not upstream
sample bytes.

Authored LibreOffice text exports confirm that a deleted chapter/section retains
labels `1` / `1.1`, the following section becomes `1.2`, and a two-paragraph
list item receives one number before the next item becomes `2`. Exporting an
RTF containing no page properties to FODT confirms 612 by 792 points and
72-point margins on all four sides. The two upstream RTF PDFs are also Letter.

Eight unchanged files downloaded directly from LibreOffice's GitHub repository
were compared before and after, at 72 DPI, one job, unpinned Windows fonts,
LibreOffice 26.2.6.3 and Poppler 26.07.0. Baseline measurements combine the
six-file list/symbol run and the two-file RTF run. These are local measurements,
not a rerun of the Linux corpus at 144 DPI.

| Sample | Before | After |
| --- | --- | --- |
| `tdf130680.odt` | Text fails | Text passes |
| `tdf163230.fodt` | Text fails | Text passes |
| `tdf163285.fodt` | Text fails | Text passes |
| `tdf123705.docx` | Text fails | Text passes; all 30 symbols retained |
| `fdo66304-1.odt` | Text fails | Text still fails; outline labels restored, complex deletion/list differences remain |
| `mixednumberings.docx` | Text fails | Unchanged; counter experiment discarded |
| `fdo45183.rtf` | Geometry off; 43.73% blurred difference | Geometry matches; 42.58% blurred difference |
| `tdf81943.rtf` | Geometry off; 13.67% blurred difference | Geometry matches; 13.31% blurred difference |

**Checks improve from 10/24 to 14/24, with no skips.** All eight read checks
pass, and six text checks now pass instead of two. All eight pixel checks still
fail, and page counts are unchanged. In particular, this patch does not resolve
the table/image layout failures at the top of the issue. Windows font fallback,
missing shape/image support, pagination and complex tracked-change formatting
remain material limits; retaining symbols alone does not establish correct
glyph appearance on every machine.
