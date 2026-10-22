# RTF Conformance And Limits

Writing linked text frames, minimum-width sizing, vertical text alignment or
explicit overflow reports `rtf.shape.flow`; frame text is retained but these
layout properties are not written.

This document is the authoritative statement of what `Broiler.Documents.Rtf`
honours, approximates, ignores, and skips when reading RTF into the rich-text
document model (ADR 0005), and what it emits when writing. The reader is total
(never throws on malformed input) and safe by default (ADR 0004). The categories
below are exercised by `RtfConformanceTests`, `RtfLimitTests`,
`RtfPageBreakTests`, `RtfSecurityTests`, `RtfCapitalizationTests`,
`RtfRunningContentTests`, `RtfPageGeometryTests`, `RtfShapeTests`, and
`RtfRoundTripTests`, `RtfLineSpacingTests`, `RtfSpaceRunTests`, `RtfDefaultFontTests`, and
`RtfFontNameTests`, plus `RtfResourceTableTests` for resource collection across
body, running content and shape text, `RtfDefaultSizeTests` for inherited
font sizes on export, `RtfSkippedDestinationTests` for what a skipped
destination keeps to itself, `RtfDocumentEndTests` for where the document
stops, and `RtfTableTests` for tables.

`RtfUnicodeFallbackTests` covers escaped fallback characters after `\uN`:
literal text, hexadecimal bytes and character-producing control symbols count
towards `\ucN`, so the following real text is retained. A zero fallback count
keeps the escaped character.

A control word and its argument count as one fallback character too; when
skipped they neither emit content nor change formatting.

An opening or closing group brace ends any unfinished Unicode fallback skipping
before the delimiter. The next group's text, or text after a header/footer
destination, is therefore retained even when the preceding fallback is shorter
than its `\ucN` count.

## Honoured — read into the model

| Group | Control words |
|---|---|
| Inline character | `\b` `\b0` `\i` `\i0` `\ul` `\ul0` `\ulnone` `\strike` `\strike0` `\v` `\v0` `\caps` `\caps0` `\scaps` `\scaps0` `\plain` `\fsN` `\fN` `\cfN` `\cbN` `\highlightN` |
| Paragraph | `\pard` `\ql` `\qc` `\qr` `\qj` `\liN` `\sbN` `\saN` `\slN` with `\slmult1` `\pagebb` `\pagebb0` `\cbpatN` (the paragraph's shading, → `ParagraphStyle.Background`; written back) |
| Paragraph mark | The character formatting in effect at `\par` (→ `RichTextParagraph.MarkStyle`, what an empty paragraph is as tall as; not written back) |
| Breaks & entities | `\par` `\line` `\page` (→ `PageBreakBefore` on the paragraph that follows) `\tab` `\nestcell` (→ tab) `\lquote` `\rquote` `\ldblquote` `\rdblquote` `\bullet` `\endash` `\emdash` `\enspace` `\emspace` |
| Control symbols | `\\` `\{` `\}` `\~` (nbsp) `\_` (nb-hyphen) `\-` (optional hyphen, dropped) `\`+CR/LF (→ paragraph) `\*` (destination marker) |
| Encoding | `\'hh` `\uN` `\ucN` `\ansicpgN` `\fcharsetN` (per font) |
| Resource tables | `\fonttbl` `\colortbl` |
| Tables | `\trowd` `\cellxN` `\intbl` `\cell` `\row` `\trleftN` `\trgaphN` `\trrhN` `\trhdr` `\trkeep` `\clbrdrt` `\clbrdrb` `\clbrdrl` `\clbrdrr` with `\brdrwN` `\brdrcfN` and the border kinds, `\clcbpatN` (→ `DocumentTable`; see below). In a header, footer or shape's text `\cell` is a tab and `\row` a paragraph, as before |
| Document default font | `\deffN` resolves the declared font-table entry into `DocumentStyleDefaults.FontFamily`; `\plain` returns runs to that inherited family |
| Runs of spaces | `{\*\generator}` `\stshfdbchN` `\dntblnsbdb`, which say whether the document's runs of spaces are laid out wider (→ `DocumentStyleDefaults.WidensSpaceRuns`; see below). A write states it |
| Fields | `\field` `\fldinst` `\fldrslt` (HYPERLINK only, including the `\l` switch that spells a same-document reference: it is read as `#name` and written back the same way. The name may not contain a quote — the field argument is quoted and RTF has no escape for one inside it, so a quote would truncate the target silently; the shared rule refuses it for every format. Targets otherwise follow that rule, or `rtf.link`) |
| Running content | `\header` `\headerf` `\headerl` `\headerr` `\footer` `\footerf` `\footerl` `\footerr` (→ `RunningContent`, not the body flow) |
| Page geometry | `\paperwN` `\paperhN` `\marglN` `\margrN` `\margtN` `\margbN` `\headeryN` `\footeryN`, and the first section's `\pgwsxnN` `\pghsxnN` `\marglsxnN` `\margrsxnN` `\margtsxnN` `\margbsxnN` over them (→ `PageGeometry`) |
| Drawings | `\shp` `\*\shpinst` `\shpleftN` `\shptopN` `\shprightN` `\shpbottomN` `\shpzN` `\shpbypara` `\shpbypage` `\shpbymargin` `\shpbxpage` `\shpbxmargin` `\shpbxcolumn` `\shptxt`, and the `{\sp{\sn name}{\sv value}}` pairs `shapeType` `fFilled` `fillColor` `fillBackColor` `fillType` `fillAngle` `fillFocus` `fLine` `lineColor` `posrelv` `fFitShapeToText` (→ `DocumentShape`) |
| Frames | `\abswN` `\abshN` `\posxN` `\posyN` `\posxl` `\posxc` `\posxr` `\posxi` `\posxo` `\posyil` `\posyt` `\posyc` `\posyb` `\posyin` `\posyout` `\phcol` `\phmrg` `\phpg` `\pvmrg` `\pvpg` `\pvpara` `\wraparound` `\nowrap` `\overlay` `\wrapthrough` `\dxfrtextN` `\dfrmtxtxN` `\dfrmtxtyN` (→ a `DocumentShape` holding the framed paragraphs; see Drawings) |
| Page background | The shape in `{\*ackground}`, read where `iewbksp1` shows it, as LibreOffice takes `iewbksp` for Word's `w:displayBackgroundShape`; its fill is a shape's (→ `RichTextDocument.PageBackground`). A write states both. A `fillFocus` near 50 is an axial gradient, on a page or a shape. |

## Notes

A `\footnote` group in the body, a table cell or a frame is a note, cited
where the group stands: one citation character holding a `DocumentNote`
whose paragraphs are the group's, `\par` between them. `\ftnalt` makes it an
endnote. Word writes the citation's label as `{\super\chftn}` just before the
group and repeats it at the head of the note's text; the label is drawn from
the citation, so the `\chftn` and the space after it in the note are left
out, and the one in the body draws nothing. A label the author typed instead
of `\chftn` stays body text, and the note is numbered. A `\footnote` in a
header, a footer, a shape or another note has nothing to be cited from and is
skipped as before; a header, font or colour table, list table or shape stated
inside a note is skipped with everything in it, so it cannot reach the
document. The writer writes each note the same way, `{\super\chftn}` then
`{\footnote\pard\plain{\super\chftn} ...}`, a note's own mark in place of
`\chftn`.

## Tracked changes

`\revised` marks inserted text and `\deleted` deleted text, as a
`TextRevision` on the runs, and on the paragraph mark where one is open at
`\par`. `\revauthN` and `\revauthdelN` name the author by index into
`\revtbl`, and `\revdttmN` and `\revdttmdelN` give the time as a packed DTTM,
taken as universal time. Deleted text is kept, as LibreOffice keeps it, and a
deletion wins over an insertion the same text is in. The writer states no
changes yet: it writes the document as accepting them would leave it, the
deleted text left out and the inserted text plain, and reports `rtf.revision`.

## Default font

An explicit `\deffN` selects the font-table family inherited by runs with no
`\fN`, including runs reset with `\plain`. The family is resolved after reading
the table, so the declaration may precede it. Missing declarations, unknown
indices and blank font names leave the model's default family unspecified.
An explicit run font continues to override the document default.

`\plain` resets byte decoding along with the character style: it restores the
declared default font's charset, or the document's `\ansicpgN`/caller default
code page when that font entry is unavailable. A previous run's font charset
does not leak past the reset. This is covered by `RtfPlainCodePageTests`.

The writer includes the document's default family in the font table and selects
it with `\deffN`. If no default family is stated, it retains the blank `\f0`
entry as the default, preserving an unspecified family on read-back.

Font-table names decode `\uN` with group-scoped `\ucN` fallback counts,
literal high bytes and hex-byte escapes using the active code page, and escaped braces/backslashes.
This preserves non-ASCII family names emitted by the writer, including UTF-16
surrogate pairs. The decoded names remain table metadata and never enter body
text.

A semicolon within a font name is emitted as `\u59?`, keeping it distinct from
the raw semicolon that terminates the table entry. Both the default family and
explicit run families preserve it on read-back.

## Line spacing

Positive `\slN` with `\slmult1` maps to `ParagraphStyle.LineSpacing` as
`N / 240`: for example, `\sl276\slmult1` requests 1.15-line spacing.
The two controls can arrive in either order. Their state follows group scope,
and `\pard` resets it. A final body paragraph retains its properties even when
the root group closes without a final `\par`.

Without `\slmult1` the amount is a height in twips, which is how LibreOffice
reads `\sl` (`RTFKeyword::SL`): positive is the least a line is and negative
exactly its height (`ParagraphStyle.LineHeightRule` and `LineHeight`, kept
within Word's 1584 points). `\sl0` is single spacing, and a negative multiple
is taken by its size, as LibreOffice takes it. `tdf133437.rtf` sets every line
of a bank statement at exactly ten points with `\sl-200`, and it used to draw
at its font's own height. The writer emits finite positive nondefault
multipliers rounded to the nearest 1/240 with `\slmult1`, a stated height as
`\slN\slmult0` with the sign saying which rule, and omits default spacing.

## Runs of spaces

LibreOffice lays out each space in a run of two or more in an RTF document a
sixth of an em wider than its font makes it, which its importer gives as how
Word lays out RTF, unless the document says it came from somewhere else
(tdf#123703). It puts a U+2006 in front of each such space; the reader keeps
the text as typed and sets
`DocumentStyleDefaults.WidensSpaceRuns`, and both layouts widen the run. A lone
space keeps its width, and so does a run in a Courier family, as LibreOffice
leaves it. `tdf123703.rtf` pads each line with 118 spaces: LibreOffice puts the
second word on a line of its own and draws two pages, and the run used to fit,
which drew one.

The document says otherwise in the three ways LibreOffice looks for: a
`{\*\generator}` whose name does not start with "Microsoft", a `\stshfdbchN`
other than 31505, or `\dntblnsbdb` (tdf#128428). The writer writes
`{\*\generator Broiler.Documents;}` for a document that does not widen its runs
and no generator for one that does, so a write read back, here or by
LibreOffice, lays out as the document did. DOCX, ODF and HTML have no such
setting, so a document converted to them keeps its spaces at their font's
width.

## Page breaks

RTF spells an explicit page break two ways and this codec reads both into the one
thing the model has, `ParagraphStyle.PageBreakBefore`.

`\pagebb` is the paragraph property, and it is what LibreOffice writes when it
exports a document whose paragraph asks to start a page. It is read as a property
like any other: `\pard` clears it along with the rest of the paragraph, and
`\pagebb0` turns it off.

`\page` is content. It sits in the character stream and means "break here", so it
is read onto the paragraph that follows it. A `\page` that interrupts a paragraph
ends that paragraph first, which is what makes `text\page more\par` and
`text\par\page more\par` reach the same model: both say the break comes before
"more". A break is spent by the first paragraph to close after it, so one `\page`
never starts more than one page.

Two things a reader should know about the limits of that mapping:

- **A break with no paragraph after it is dropped**, and reported once as
  `rtf.pagebreak.empty` (informational). It happens at the end of a document and
  between two consecutive `\page`. What such a break asks for is a *blank* page,
  and a page break here is a property of a paragraph, so there is no paragraph to
  put it on; inventing an empty one to carry it would add a blank line the
  document does not contain and would come back out of the writer as content.
  Word does draw that empty page, so this is a real difference, which is why it is
  said out loud rather than dropped quietly.
- **A break spelled as a section break is not read.** `\sect` and the section
  break kinds (`\sbkpage`, `\sbkeven`, `\sbkodd`, `\sbkcol`) are ignored control
  words: the model has no sections, so a document that starts its new page by
  starting a new section reads as one continuous flow. The page a section states
  is read - see below, where `\sect` and `\sectd` say which section's page is
  the document's - but the section is not. The
  exporter this mapping was checked against does not do that for a
  paragraph-level break - LibreOffice writes `\sectd\sbknone` for the one section
  and `\pagebb` on the paragraph - but a document from elsewhere may.

## Running content

`\header` and `\footer` are read, not skipped. Each opens a destination of its
own and the paragraphs it collects reach `RichTextDocument.RunningContent`,
never the body flow: a header's text is on the page rather than in the document,
and a reader that appended it to the body would put the letterhead in the middle
of the letter.

RTF spells eight of these destinations and the model holds six — three page
selections for each of the header and the footer — so the mapping is stated
rather than left to be inferred, and it is the collapsing of eight spellings into
six slots that makes stating it worth the lines. `\headerf`/`\footerf` are
the first page's and `\headerl`/`\footerl` the left - even - page's.
`\header`/`\footer` are every page the other two do not claim, and
`\headerr`/`\footerr` are read as those as well: they name the right, odd page,
which is every page in a document that does not distinguish them.

`\titlepg` is read and written beside them. It is what makes `\headerf` and
`\footerf` mean anything, and what makes a band the first page does not state
*empty* there rather than the default one - the same fact `w:titlePg` carries in
DOCX and a master-page chain carries in ODF. **LibreOffice disagrees here and
this reader follows Microsoft's own format.** Given `\titlepg{\footer ...}` with
no `\footerf`, Word draws no footer on the first page and LibreOffice draws the
default one; LibreOffice's DOCX importer honours the same flag the Word way, so
matching its RTF reading would have made this component disagree with itself
across two of its own readers to agree with an application that disagrees with
itself across two of its own importers.

A field's result belongs to the destination the field is in. `\fldrslt` used to
replace the current destination rather than sit inside it, so a `PAGE` field in a
footer handed its cached number to the body - the letterhead's page number arrived
at the head of the letter with the word beside it correctly skipped, which is what
said the routing rather than the parsing was wrong.

A page break inside running content is read, because `\pagebb` is a paragraph
property wherever a paragraph is. It cannot be written back, which the writing
section says out loud rather than leaving to be discovered.

## Page geometry

The section control words that state the paper and its margins - `\paperw`,
`\paperh`, `\margl`, `\margr`, `\margt`, `\margb`, and the `\headery`/`\footery`
distances - are read into `PageGeometry`. They are twips, twenty to the point,
and RTF states them one control word at a time, so they are held aside and made
into a page when the document ends.

A section states a page of its own with `\pgwsxn`, `\pghsxn`, `\marglsxn`,
`\margrsxn`, `\margtsxn` and `\margbsxn`, and RTF 1.9.1 makes each of them
default to the document's word of the same meaning - so what a section states
is the page, and what it leaves out is the document's. The document's page is
the one it opens on, which is the first section's: the section words before the
first `\sect` are read over the document's, `\sectd` clears them back to the
document's, and those after the first `\sect` are not read, because they belong
to a section this reader does not hold and would otherwise turn the whole
document landscape for one landscape section at its end. `\lndscpsxn` and
`\landscape` are flags beside the size, not an instruction to swap it: the
width and height a landscape section states are already the landscape page's,
and LibreOffice draws `\lndscpsxn\pgwsxn11907\pghsxn16840` portrait.

A document that states no paper size gets no page rather than a guessed one: a
default invented here would be indistinguishable from a size the author chose. A
document whose margins leave no column to write in gets none either, and is told
so as `rtf.page.geometry` - the numbers are real, but the page they describe is
not one anything can lay out.

`\cols` divides the page into that many text columns, `\colsx` apart - 720
twips when it is absent, as the specification says. Columns that leave no room
keep the page and lose the columns, as `rtf.columns.geometry`. A write states
both when there is more than one column. `\column`, the column break, starts
the next column: where the page has columns it is
`ParagraphStyle.ColumnBreakBefore` on the paragraph after it, carried there the
way `\page` carries a page break, and where it has one it is a page break, as
Word takes it. A write puts `\column` where the paragraph that has the break
starts.

This is the page, not sections. Everything else a section states is still
ignored, including the section break kinds above, and a document whose sections
have columns or margins of their own is written with the page and its columns
alone, which `rtf.sections` says.

## Tables

A body table is read into a `DocumentTable` over the paragraphs its cells hold.
`\trowd` starts a row definition, and each `\cellx` closes one cell of it at
the right edge it names, with the borders and shading stated before it.
`\intbl` puts a paragraph in the table; it is a paragraph property, so `\pard`
clears it and a closing group gives it back. `\cell` ends a cell's last
paragraph - with or without `\intbl`, since it can mean nothing else - and
`\row` closes the row under the definition in effect. The first paragraph out
of the table ends the table.

The row definition and the table being read are not part of the state RTF
pushes and pops with its braces. Writers wrap cells and row ends in groups of
their own - `{\b A1 \cell}`, `{\row}`, a whole table inside `{\trowd ... \pard}`
- and a definition that vanished when such a group closed would leave the cells
after it with no table. A row with no `\trowd` of its own takes the definition
of the row before, as Word does.

Rows of one table need not agree on their edges. The grid is every edge any row
states, and a cell spans the grid columns between its own two edges, so every
cell is as wide as its row says - which is what LibreOffice's own test of
`fdo66565.rtf` checks, with a row of seven cells over a row of eighteen. A table
some row of which holds more cells than it defines, or defines an edge that goes
backwards, gets no widths and is divided evenly, as a table stating no grid is.

`\trgaph` is the cell padding; a table that states none has none, which is how
LibreOffice sets it. Positive `\trrh` is a minimum height and negative `\trrh`
is fixed. Fixed rows keep their editable text while rendered overflow is clipped.
`\trhdr` marks a header row and
`\trkeep` keeps a row on one page. `\clbrdrt`, `\clbrdrb`, `\clbrdrl` and
`\clbrdrr` point the border words that follow at a side of the next cell:
`\brdrw` is its width, `\brdrcf` its colour (black when absent or auto), and any
kind but `\brdrnone` draws it - as a single line, because the model has one
kind, so a double border is drawn single. `\clcbpat` is the cell's background.

Not read: merged cells (`\clmgf`, `\clmrg`, `\clvmgf`, `\clvmrg`), whose cells
are drawn as the separate cells the file writes; the row's own borders
(`\trbrdr*`); `\trleft`'s offset of the table from the margin, beyond placing
rows against one another; padding stated as `\trpadd*`; and nested tables, whose
text joins the outer cell's with `\nestcell` read as a tab. A cell mark in a
header, footer or shape's text is a tab and a row mark a paragraph, as before:
those hold paragraphs and nothing beside them.

## Drawings

A `\shp` group is read into a `DocumentShape` anchored to the paragraph it sits
in. Its box comes from `\shpleft`/`\shptop`/`\shpright`/`\shpbottom` in twips
and its paint from `{\sp{\sn name}{\sv value}}` pairs, of which seven are
understood: `fFilled`, `fillColor`, `fillBackColor`, `fillType` and `fillAngle`
for a solid or gradient fill, `fLine` and `lineColor` for the outline, and
`shapeType` for whether it is a line. A shape whose line colour is not stated
is outlined black, which is Word's default and the one LibreOffice's import
takes in place of Writer's blue (`rtfsdrimport.cxx`). `\shpz`
is read and written beside them: it orders one shape against another, and RTF is
the target where a letterhead's stripe and the box over it both land in the body,
so it is the only thing left saying which the reader sees. A shape
colour is one integer holding blue, green and red in that order, which is the
reverse of how the rest of the format writes one and the easiest thing here to
get backwards. `\shptxt` carries the shape's own text, which stays out of the
body for the same reason a header's does. `\shpbypage` and `\shpbymargin`, or a `posrelv` of 1
or 0, measure `\shptop` from the page or its top margin rather than from the
paragraph: `DocumentShape.OffsetFromPage`, with the margin added, and the shape
stands that far down whichever page its paragraph is on. They were not read,
and such a shape hung that far below its paragraph. `\shpbxpage` measures
`\shpleft` from the page's left edge, so the margin is taken off it; the margin
and the column are where the model measures from already.
`fFitShapeToText` makes the box grow to its text (`DocumentShape.GrowsToText`),
and the writer writes it for such a box.

A line (`shapeType` 20) is a box of nought by its length, and a box with no
breadth and an outline is drawn as the rule its outline makes
(`DocumentShape.IsRule`). A ruled form is drawn this way: `tdf133437.rtf`'s
statement is 285 lines and an unfilled rectangle around paragraphs set in
frames, and every one of them was dropped. A slanted line would need a geometry
the model does not hold, and drawn as its box it would be a rectangle that is
not there, so it is not read and is reported once (`rtf.shape.line`).

Every other shape property is ignored, `pib` - a picture inside a shape - among
them. A shape that ends with no fill, no text and no outline is dropped, and so
is one whose box has no width or height and is not a rule: what would be left is
a box with nothing in it, or nothing in a box. A picture frame (`shapeType` 75)
needs fill or text of its own to be kept, since the picture it draws is not
read and its line alone would be an empty box.

### Frames

A paragraph whose position words make a frame (RTF 1.9.1, "Positioned Objects
and Frames") is read into a `DocumentShape` that holds it, rather than into the
body. Which words make one is LibreOffice's rule (`RTFFrame::hasProperties`): a
position, a size, or a reference or alignment other than the default; the
distances from the text alone do not. Consecutive paragraphs stating the same
frame share it, and the first paragraph stating another or none finishes it.
The frame is anchored to that paragraph, the "next unframed paragraph" RTF
measures `\pvpara` from. `\pard` ends a frame, and a frame in a table is read as
the table's text, as LibreOffice reads it.

- **Across:** `\posx` from the column (`\phcol`, the default), the margin
  (`\phmrg`) or the page's edge (`\phpg`); `\posxl`, `\posxc`, `\posxr` place it
  by its width in that reference, with inside and outside taken as left and
  right. A position replaces an alignment and an alignment a position,
  whichever comes last. `\posnegx` is not read; neither does LibreOffice.
- **Down:** `\posy` from the top margin (`\pvmrg`, the default), the page
  (`\pvpg`) - both `DocumentShape.OffsetFromPage` - or the paragraph
  (`\pvpara`). `\posyt` is the reference's top. `\posyc` and `\posyb` need the
  frame's height, which a frame sized by its text does not have until it is
  laid out, so they are set at the top and reported (`rtf.frame.position`).
  `\posyil` is the paragraph's own place.
- **Size:** `\absw`, or the column's width when it is not stated. `\absh` above
  nought is the least the frame is and nought leaves it to the text, both
  `DocumentShape.GrowsToText`; below nought is exact.
- **Wrap:** text goes around the frame (`\wraparound`, the default) at
  `\dfrmtxtx`, above and below it (`\nowrap`) at `\dfrmtxty`, or through it
  (`\overlay`, `\wrapthrough`). `\dxfrtext` states both distances.

The writer writes such a shape as a `\shp` with its text, which reads back as
the same box; it does not write frame words.

`\shpinst` is the single exception to the `\*` rule below. A shape arrives as
`{\*\shpinst …}`, and the star says to ignore what the reader does not
understand, which this is not.

## Approximated

| Construct | Approximation |
|---|---|
| `\headerr` / `\footerr` | Read as the every-page destination: the model names the first page, the even pages, and the rest, and the right-hand page is the rest |
| `\liN` (twips) | Mapped to a discrete indent level (`round(N / 360)`, capped) |
| `\fs` on write | Rounded to the nearest half-point (`\fs` is integer half-points) |
| `\'hh` under a double-byte code page | Decoded byte by byte through a Latin-1 fallback for `0x80`-`0xFF`, reported once as `rtf.codepage`. Windows-1252 and every other single-byte code page - Cyrillic 1251, Central European 1250, Greek 1253 and the rest a font's `\fcharset` names - are exact, decoded from the runtime's code-page data without registering a provider. `\uN` text is always exact. |

## Ignored — parsed, no effect on the model

Unrecognized formatting control words are silently ignored (predictable
degradation), for example `\lang`,
`\viewkind`, `\widowctrl`, `\kerning`, and the section break kinds
(`\sbkpage` and the rest), and the document and section options other than the
page geometry above; `\sect` and `\sectd` matter only to which section's page is
read. The character-set words `\ansi`, `\mac`, `\pc` and
`\pca` are ignored too: the active code page comes from `\ansicpgN`, from a
font's `\fcharsetN`, or from the caller's default, and nothing else sets it.
Shape properties other than those named above are ignored as well.
List markup does not set `ListKind` in this release (it stays `None`); list
tables are detected and skipped as described below. Numbering, restarts and
nested marker reconstruction are not implemented.

## Skipped destinations — content dropped

`\pict` and `\object`/`\*\objdata` are skipped and reported once as
`rtf.embedded` - or once as `document.capability.not-composed` when the caller
asked for embedded decoding, because a caller that asked for something this
reader does not compose is owed an answer to the question rather than a note
about the document. Skipped with them are the following non-content
destinations: `\info` `\stylesheet` `\annotation`
`\colorschememapping` `\latentstyles` `\datastore` `\themedata` `\generator`
`\pntext`, plus any unknown
ignorable destination introduced by `\*` - `\*\shpinst` excepted, since the
reader understands that one. `\binN` binary data is skipped at the tokenizer
(reported as `rtf.bin` when it exceeds `MaxBinBytes`).

A skipped destination is skipped with every group inside it. A destination the
reader does understand - a field, a header or footer, a font or colour table, a
shape - has no effect when it sits inside one this reader skips. That used to
stop one level down: a hyperlink in a footnote put its text in the body beside
the footnote mark (LibreOffice reads `A{\footnote ...link...}B` as `A1B`; this
reader read it as `AlinkB`, then as `AB` while footnotes were skipped, and now
as `A1B` with the link in the note), and a header inside a stylesheet became
the page's header. The one thing still said about a skipped subtree is
that it held a picture or object, because `rtf.embedded` reports what the
document lost and a picture inside `{\*\shppict ...}` is a picture all the same.

`\listtable` and `\listoverridetable`, with or without `\*`, are detected
and skipped. They produce one warning, `rtf.list`, per read, so the read status
is `Partial`. The warning reports unsupported list semantics; it does not claim
that the table is valid or referenced by a body paragraph. The whole subtree
stays opaque: nested fields, running content, formatting and font tables cannot
leak into or change the body. Text before and after a closed table is preserved.
Truncated tables remain skipped, and the existing tokenizer byte and nesting
limits still apply. This is detection, not list-table interpretation.

`\header` and `\footer` are not on that list. Their content goes to the
document's running content (above): none of it reaches the body, and none of it
is lost.

## The end of the document

The document is the `{\rtf1 ...}` group and ends at its closing brace. What
follows that brace is not read, which is what Word and LibreOffice both do: the
grammar is `'{' <header> <document> '}'`, and bytes after the last brace are not
in it. This reader used to carry on, so a stray `}` in the middle of a file
ended the document for everybody else and not for it, and a file whose last
section had been repeated past the brace came out with that section's footer
and picture data once per copy.

Trailing content is reported once as `rtf.trailing` (informational). Blank
text (spaces, tabs, a NUL) and further closing braces are not reported, because
a file ending in a newline, a terminator or one brace too many has nothing after
its document that anybody wrote. A closing brace with no group open closes
nothing and does not end the read, and a root group that never closes is read to
the end of the input, as before.

## Limits (`DocumentLimits`)

| Limit | Default | Guards against |
|---|---|---|
| `MaxProbeBytes` | 4 KiB | unbounded signature sniffing |
| `MaxDocumentBytes` | 64 MiB | oversized input (reports `rtf.size`) |
| `MaxGroupDepth` | 256 | `{ … }` nesting bombs / stack overflow (reports `rtf.depth`) |
| `MaxRunLength` | 1 Mi chars | pathological single runs |
| `MaxParagraphCount` | 1 Mi | paragraph floods (reports `rtf.paragraphs`; text past the cap is dropped) |
| `MaxBinBytes` | 16 MiB | huge `\bin` payloads (reports `rtf.bin`) |

Group nesting is iterative (no recursion), so depth cannot overflow the stack.

## Security guarantees (ADR 0004)

- **No network.** Reading performs no HTTP/file access. `INCLUDEPICTURE`, remote
  templates, and hyperlink targets are never fetched.
- **No code execution.** `\object`/`\*\objdata` OLE payloads are skipped, never
  instantiated or deserialized.
- **URL policy.** Only `http`, `https`, and `mailto` survive as an inert
  `LinkHref`, together with a non-empty same-document fragment such as
  `#chapter`; other schemes (`javascript:`, `data:`, `file:`, `vbscript:`, …)
  and every relative target are dropped and reported as `rtf.link`. The rule is
  `DocumentLinkTarget`, one predicate for every codec, and the writer asks it
  again rather than trusting what a read admitted.
- **Bounded.** Every limit above is enforced; binary is skipped, not tokenized.
- **Total.** Malformed input yields a best-effort document plus diagnostics, never
  an exception across the API.

## Writing (model → RTF)

The writer emits the honoured subset above as pure ASCII: a
`{\rtf1\ansi\ansicpg1252\deffN\uc1` header, `\fonttbl`/`\colortbl` built from
the styles used and the declared default family, the page geometry and the header and footer destinations the
document states - before the body, where a reader takes them as section
properties - one group-wrapped run per style, and a `\par` after every
paragraph. A document that states no page writes no `\paperw`, because
inventing one would put words in the author's mouth. Non-ASCII characters are
escaped as `\uN?`; there is no raw-byte mode, so a caller that sets
`AsciiOnly=false` is told once as `document.capability.not-composed` rather than
quietly handed the escaped form it did not ask for.

Font and colour collection includes body paragraphs, body-shape text, and all
six header/footer paragraph variants before either table is emitted. A resource
used only in running content or a shape therefore has a defined table index,
preserving its family, foreground colour and highlight on read-back.

When a run inherits a document default size other than 12 points, the writer
emits that size explicitly. This applies to body, header/footer and shape text;
explicit run sizes retain precedence. On read-back the effective size is
preserved as a run property rather than a custom document default. RTF's integer
half-point precision still applies.

A page break is written as `\page`, at the head of the paragraph it belongs to:
both spellings say the same thing and Word and Writer honour both, but `\page`
lives in the character stream that any reader has to walk to get the text out at
all, where `\pagebb` is a paragraph property that a simpler reader steps over
without a word. Writing both would tell a reader that honours both to break
twice for one break. The first paragraph of a document is the exception and takes
`\pagebb` instead: a `\page` at the head of the body is not a break between two
things, and a reader that meets one draws a blank first page nobody asked for.
The same one-line document renders on two pages in Writer with `\page` and on one
with `\pagebb`, because a break before the first paragraph is already satisfied -
which is also why this codec's own layout ignores it there. The word is read back
either way, so the flag round-trips. A page break on a header, footer, or shape
paragraph is **not** written, and says so once as `rtf.pagebreak.dropped`: there
is no page for it to start.

An embedded image is written as a `\pict` destination — `\pngblip` or
`\jpegblip`, sized with `\picwgoal`/`\pichgoal` — so a picture survives into Word
and WordPad; any other image format is dropped with `rtf.image.format`, and a
picture the caller's resource policy does not permit into an output is left out
with `rtf.image.omitted`, whose message names the denial. Because `\pict` is a
skipped destination on the read side, an image does **not** survive a model → RTF
→ model round-trip; DOCX is the format that round-trips pictures. A table is
written as its cell paragraphs in row order, with `rtf.table.flattened`: RTF
states a table as `\trowd` and `\cellx` runs, which the reader now takes but
the writer does not yet write, so a table read from RTF comes back from a round
trip as its paragraphs without the grid. A shape
is written as a `\shp` group at the head of the paragraph it is anchored to,
carrying its box, its fill, its outline and its text, which is the whole of what
the reader takes back out of one. A floating picture is the exception: it is
written into the paragraph instead, with `rtf.image.anchored`, because a picture
inside a shape is a `pib` shape property, and this reader knows only the fill and
line properties — so a shape written that way would come back with no picture and
no paint, which is a shape it drops. In the table and the floating picture alike,
what is on the page is worth more than the structure around it.

Round-trip is otherwise lossless for any document the reader can
produce, with three further documented exceptions: a table's grid, above;
list kind is not written (it is not read either, so it stays at its default); and a
**page break inside running content or
a shape** is read (`\pagebb` is honoured in those destinations too) but not
written back, per `rtf.pagebreak.dropped` above. All three are safe
(never lossy for text, formatting, colors, sizes, alignment, or hyperlinks).

## Diagnostics

Every code this codec reports, on both sides. The table is here so the claim can
be checked rather than believed: a code in the source and not in this table is a
loss nobody was told about, and a code here and not in the source is a promise
the codec stopped keeping. Two codes are raised on both sides: `rtf.link`,
because the writer asks the URL rule again rather than trusting what a read
admitted, and `document.capability.not-composed`, which is the shared code for
anything a caller asked for that this codec does not do.

| Code | Severity | Side | Meaning |
| --- | --- | --- | --- |
| `rtf.size` | Warning | Read | Input exceeded `MaxDocumentBytes` and was truncated. |
| `rtf.depth` | Warning | Read | Group nesting exceeded `MaxGroupDepth`; tokenization stopped there. |
| `rtf.bin` | Warning | Read | A `\binN` length exceeded `MaxBinBytes`; the binary data was skipped. |
| `rtf.hex` | Warning | Read | A malformed `\'hh` hex escape was skipped. |
| `rtf.paragraphs` | Warning | Read | The document exceeded `MaxParagraphCount`; the text past the cap was dropped. |
| `rtf.codepage` | Info | Read | A double-byte code page, or one the runtime does not carry, decoded its high bytes through a Latin-1 fallback; `\uN` text is unaffected. Once per read. |
| `rtf.shape.line` | Info | Read | A slanted line was not read; the model holds a line only as a horizontal or vertical rule. Once per read. |
| `rtf.shape.table` | Warning | Write | Tables inside a text box are written as paragraphs. Their text is retained, but their cell layout is not written. Once per write. |
| `rtf.shape.padding` | Warning | Write | Text-box inner padding was not written; the text and outer frame are retained. Once per write. |
| `rtf.frame.position` | Info | Read | A frame aligned to the centre or foot of its reference was set at its top, its height not being known until layout. Once per read. |
| `rtf.embedded` | Info | Read | Pictures and objects were skipped. Once per read, and replaced by `document.capability.not-composed` when decoding was asked for. |
| `rtf.page.geometry` | Warning | Read | The section properties described a page with no room to write on, so the page was not read. |
| `rtf.columns.geometry` | Warning | Read | `\cols` and `\colsx` left no room to write in a column; the page was kept and read as one column. |
| `rtf.pagebreak.empty` | Info | Read | A page break with no paragraph after it asks for a blank page, which this model cannot hold; the break was dropped. Once per read. |
| `rtf.trailing` | Info | Read | Content followed the root group's closing brace; it is not part of the document and was not read. Once per read. |
| `rtf.link` | Warning | Read, write | A hyperlink target the shared rule refuses. Reading drops the link; writing keeps the run as plain text. |
| `rtf.pagebreak.dropped` | Warning | Write | A page or column break on a header, footer, or shape paragraph was not written; there is no page for it to start. |
| `rtf.sections` | Warning | Write | The document's sections, each with columns and margins of its own, were not written; the page and its columns were written for the whole document. |
| `rtf.table.flattened` | Warning | Write | A table was written as its cell paragraphs, in row order; this codec carries no table structure. |
| `rtf.note.nested` | Warning | Write | A note cited inside a note was dropped; RTF cannot nest them. |
| `rtf.revision` | Warning | Write | Tracked changes were written accepted: the deleted text left out and the inserted text written as plain text. |
| `rtf.image.format` | Warning | Write | RTF carries PNG and JPEG pictures; one in another format was dropped. |
| `rtf.image.omitted` | Warning | Write | The caller's resource policy did not permit a picture into the output; the message names the denial. |
| `rtf.image.anchored` | Warning | Write | A floating picture was written into its paragraph; its position beside the text was not kept. |
| `rtf.shape.page` | Warning | Write | A shape anchored to a page by number was written beside its paragraph with `\shpbypage`; RTF can measure from the page but not name it. A shape measured from the page its paragraph is on (`DocumentShape.OffsetFromPage`) is written with `\shpbypage` too, and is not reported: that is exactly what RTF says. |
| `document.capability.not-composed` | Warning | Read, write | Something the caller asked for that this codec does not compose: `DecodeEmbeddedObjects` on a document that carries a picture or object, or `AsciiOnly=false` on a write. |

## Standards and rights

RTF is a vendor format with no standards body. Its rights position (the Microsoft
Open Specification Promise names `[RTF]` itself), the approved labels and the
provenance findings are in [the RTF register](../ip/rtf.md). Decided is not
cleared: no lawyer reviewed it, and patent-freedom is not claimed.
