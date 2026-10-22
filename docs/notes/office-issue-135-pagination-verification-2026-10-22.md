# Issue 135: paragraph pagination controls

The next three implementation gaps investigated after the first batch were:

1. Keep with next: `w:keepNext` / `fo:keep-with-next`.
2. Keep paragraph lines together: `w:keepLines` / `fo:keep-together`.
3. Widow and orphan control: `w:widowControl` / `fo:widows` and `fo:orphans`.

These declarations occur in the styles of ranked documents including
`tdf95033.docx`, `tdf83309.docx`, `tdf147199.docx`, `tdf142694-1.odt` and
`tdf91083.odt`. Presence alone does not prove a rule causes a document's current
worst difference; the before/after experiment below checks that separately.

## Implementation

`ParagraphStyle` holds the resolved keep flags and nullable widow/orphan line
counts. Null means no explicit line-count policy, zero explicitly disables it.
DOCX and ODT readers apply these through their existing style chains, including
direct off/auto/zero overrides. Their writers preserve them; DOCX reports custom
ODF counts it can only reduce to its two-line on/off control. Unspecified counts
retain the renderer's existing policy rather than changing every document's
pagination by default. Other interchange codecs do not yet preserve these new
properties.

Both the shared pagination engine (raster output, preview and printing) and the
PDF writer honor the rules in body text. A keep chain that fits on a fresh page
or column moves as a group; a heading can stay with the start of a body paragraph
that exceeds one page. An oversized keep-together paragraph and impossible line
counts relax rather than generating blank pages or losing text. Explicit page
breaks take precedence. Continuous output does not paginate for these rules.

Lookahead snapshots the list counters, so measuring a subsequent numbered item
does not consume its label. Paragraph-anchored wrapping shapes are re-anchored
when a paragraph moves. Multi-column sections with explicit keep/line-count
constraints are left unbalanced rather than splitting a kept paragraph during
column balancing.

The shared engine also groups legal cut positions inside table cells for kept
paragraphs, linked paragraph boundaries and widow/orphan line groups. Oversized
cell constraints relax. The PDF writer's existing policy places entire table
rows and does not split them. Keep chains stop at section changes, explicit
breaks and table boundaries; a table's own keep-with-next rule is still not
modeled. These changes do not implement unequal columns or dynamic header fields.

## Verification

All 1,995 tests passed across Pagination (142), DOCX (522), ODT (370), PDF (707)
and CLI (254). Authored regressions cover all three rules, body and cell text,
numbered-list lookahead, chains, oversized paragraphs, explicit breaks, columns,
continuous output, style inheritance, DOCX element order and round trips.

Four authored DOCX documents were additionally exported to PDF by LibreOffice
26.2.6.3 and by the PDF codec. They use an 80-point text area, exact 10-point line
height and explicit 5.5-point paragraph spacing. Extracted page-by-page text
matches for all four cases:

| Rule | First page | Second page |
| --- | --- | --- |
| Keep lines together | Five filler lines | All three body lines |
| Keep with next | Six filler lines | Heading and both body lines |
| Widow control | Four filler lines and two body lines | Last two body lines |
| Orphan control | Six filler lines | All three body lines |

The five ranked samples listed above were run before and after the changes with
LibreOffice 26.2.6.3 / Poppler 26.07.0 on Windows, unpinned fonts, 72 DPI, one job
and a 40-page cap. The reported worst-page bands, page counts and check outcomes
were unchanged: one pixel pass, four pixel failures; two text passes and three
text failures. Thus these controls are verified missing behaviors now implemented,
**not a demonstrated reduction in the sampled issue scores**. A full Linux run
is needed to measure the aggregate effect. No upstream document bytes were
committed; the downloads and authored integration files stayed in temporary
storage.

## References

- [Microsoft: keep with next](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.keepnext)
- [Microsoft: widow control](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.widowcontrol)
- [Microsoft: paragraph pagination options](https://support.microsoft.com/en-us/word/line-and-page-breaks)
