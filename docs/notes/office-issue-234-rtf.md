# Office issue #234: RTF tab stops, linked pictures, missing glyphs and header rows

Five RTF samples, rendered locally with `broilerdoc render --font-dir` against the
pinned LibreOffice 24.2 PDFs. The ink counts are dark pixels per page at 96 dpi,
listed as broilerdoc / LibreOffice.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf158658b.rtf` | The footer reads `\tab\tab 4` under `\tqc\tx4536\tqr\tx9072` on a page 243.8 pt wide. The RTF reader read no tab stops at all, so the page number sat at the second default stop, inside the page. LibreOffice sets it at the right stop, 453.6 pt from the margin. That is past the page edge, so nothing shows. The upstream test `layout6.cxx testTdf158658b` pins this with a 4446-twip right-tab portion. | Fixed. `\tx` with `\tqr`/`\tqc`/`\tqdec` and the leaders now become `ParagraphStyle.TabStops`, in a paragraph or a style. An RTF sets `TabsOverMargin`, because LibreOffice reads a document with no Word compatibility mode as Word 2010 (`SettingsTable::ApplyProperties`). Ink: 31 → 0, against LibreOffice's 0. |
| `rtfimport/data/tblrepeat.rtf` | The first three rows carry `\trhdr`, and broilerdoc repeats them on page two. LibreOffice 24.2's RTF import ignored `\trhdr` (tdf#118465), so its page two is blank. The fix (`44d1dd3a`, first released in 25.8) came with this sample and `rtfimport2.cxx test118465Tdf`, which asserts `HeaderRowCount` 3. | Reference defect, recorded in `tests/office/extras-reference-defects.json` (pixel axis only, because the text export lists the header text once either way). broilerdoc is unchanged at [772, 772]; LibreOffice draws [746, 0]. |
| `rtfexport/data/tdf49073.rtf` | The ruby base text is CJK, in Century and Liberation Serif, and neither pinned face covers it. LibreOffice falls back to Liberation Serif. Its `.notdef` glyph has no outline (the glyph has zero bytes in the pinned `LiberationSerif-Regular.ttf`), so the PDF shows `<01>` glyphs that draw nothing. Broiler.Graphics draws its own fallback `?` for an unmapped glyph. | Not fixed. The fallback glyph comes from Broiler.Graphics, not this repository. The reader is right: the text export matches LibreOffice's. Ink: 15865 / 0. |
| `rtfimport/data/tdf59699.rtf` | `INCLUDEPICTURE "libreoffice.png" \d` with an empty `\fldrslt`. LibreOffice loads the PNG from the folder beside the document. | Not fixed, by design. A reader is given only the document's bytes and does not open another file (DOCX does the same: `docx.image.external`). The drop is no longer silent: the new `rtf.image.external` warning reports it. A picture inside a field result now counts as the field's result, so a field with a saved picture is not reported. Ink: 0 / 1188. |
| `rtfimport/data/ole-inline.rtf` | The object's `\result` is an Equation.3 preview EMF (`e = mc²`) made only of text records, and `EmfRasterizer` draws no text. | Not fixed. As before, drawing it needs EMF text output (`EXTCREATEFONTINDIRECTW`/`EXTTEXTOUTW`) with the font set available at decode time, or a WMF reader for the `\nonshppict` fallback. Ink: 0 / 110. |

## Tab leaders

Reading the tab stops also made Broiler draw dot leaders in RTF tables of contents
(`fdo82071.rtf`, `tdf106694.rtf`), and a bold leader ran on past its page number
to the page edge. `DocumentPagePainter` drew a leader as one string of `count`
characters, with `count` worked out from the measured advance of one character.
The renderer drew that string with an advance wider than the measured one. Each
leader character is now drawn at its measured position, so the leader ends inside
the tab's gap. The bold dot leaders of the DOCX table of contents in `fdo69649.docx`
had the same overflow and are fixed the same way.

`\tleq` gives no leader. Word draws equals signs, but LibreOffice 24.2 draws
nothing (`tab-stop-fill-chars.rtf`).

## Effect on the other RTF samples

I compared every reference RTF with `\tx` or `INCLUDEPICTURE` (87 samples). For
each page I took the mean absolute grey difference against LibreOffice, before
and after this change. Seventeen samples changed:

- `tdf112208_hangingIndent.rtf` goes from 2 pages to LibreOffice's 1.
- `tdf136587_noStyleName.rtf`, the `tdf158658a/b/c` samples, `fdo75735.rtf`,
  `tdf134614_toc_indent.rtf` and several smaller ones moved closer to the reference.
- `fdo82071.rtf`, `tdf106694.rtf` and `tab-stop-fill-chars.rtf` score slightly
  worse. Their entries now line up with LibreOffice's: right-aligned page numbers
  and dot leaders, where Broiler used to place them at default stops. What is left
  is the font fallback LibreOffice picks and the vertical offset. Both are visible
  without the leaders too.

## Tests

`RtfIssue234Tests` (Rtf.Tests) and `DocumentLayoutTabStopTests` (Pagination.Tests).
The Rtf, Pagination, Docx, Odt, Office, Cli, Pdf and Documents test projects all pass.
