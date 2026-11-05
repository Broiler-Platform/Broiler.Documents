# Office conformance issue 199

## tdf#161368

The pinned LibreOffice 24.2.7.2 PDF for `layout/data/tdf161368.fodt` has four
pages. Its lines measure around one character wide despite a 120 cm square
page and 2 cm margins. The layout used 16-bit unsigned twip widths and
overflowed the body width. The [fix](https://gerrit.libreoffice.org/c/core/+/168341)
and its regression test ship in LibreOffice 24.8. The test asserts one page and
one line per paragraph. Broilerdoc and a local LibreOffice 26.2.6.3 PDF export
both produce one page at the stated paper size.

## tdf#138020

The pinned PDF for `ooxmlexport/data/tdf138020_all_rows_tblHeader.docx` leaves
page one for the opening paragraph, clips the final table rows on page two
and produces a blank third page. The pinned plain-text projection retains all
the rows. The [upstream fix](https://gerrit.libreoffice.org/c/core/+/202205)
ships in LibreOffice 26.8. Its test requires the table to start on page two,
continue on page three and use zero repeating headers. The DOCX row flags
still select table styling. Pagination suppresses repetition when every row
in a multi-row table carries the header flag.

## tdf#125469

The pinned LibreOffice 24.2.7.2 PDF for
ooxmlexport/data/tdf125469_singleSpacing.docx has four pages, three of them
blank. The [upstream fix](https://gerrit.libreoffice.org/c/core/+/170670)
ships in LibreOffice 25.2. It uses automatic line spacing when the
paragraph and its style supply no rule. LibreOffice 26.2.6.3 and Broilerdoc
with Calibri each render one page containing all four paragraphs. The
bundled comparison font wraps the opening line and Broilerdoc then uses two
pages.
