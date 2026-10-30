# RTF Conformance And Limits

This document is the authoritative statement of what `Broiler.Documents.Rtf`
honours, approximates, ignores, and skips when reading RTF into the rich-text
document model (ADR 0005), and what it emits when writing. The reader is total
(never throws on malformed input) and safe by default (ADR 0004).

The categories below are exercised by `RtfConformanceTests`, `RtfLimitTests`,
`RtfPageBreakTests`, `RtfSecurityTests`, `RtfCapitalizationTests`,
`RtfRunningContentTests`, `RtfPageGeometryTests`, `RtfShapeTests`,
`RtfRoundTripTests`, `RtfLineSpacingTests`, `RtfSpaceRunTests`,
`RtfDefaultFontTests`, `RtfFontNameTests`, `RtfExactLeftIndentTests`,
`RtfUnicodeFallbackTests`, `RtfPlainCodePageTests` and `RtfTableTests`, plus
`RtfResourceTableTests` (resource collection across body, running content and
shape text), `RtfDefaultSizeTests` (inherited font sizes on export),
`RtfSkippedDestinationTests` (what a skipped destination keeps to itself) and
`RtfDocumentEndTests` (where the document stops).

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
| Tables | `\trowd` `\cellxN` `\intbl` `\cell` `\row` `\trleftN` `\trgaphN` `\trrhN` `\trhdr` `\trkeep` `\clbrdrt` `\clbrdrb` `\clbrdrl` `\clbrdrr` with `\brdrwN` `\brdrcfN` and the border kinds, `\clcbpatN` (→ `DocumentTable`; see [Tables](#tables)). In a header, footer or shape's text `\cell` is a tab and `\row` a paragraph |
| Document default font | `\deffN` resolves the declared font-table entry into `DocumentStyleDefaults.FontFamily`; `\plain` returns runs to that inherited family |
| Runs of spaces | `{\*\generator}` `\stshfdbchN` `\dntblnsbdb`, which say whether the document's runs of spaces are laid out wider (→ `DocumentStyleDefaults.WidensSpaceRuns`; see below). A write states it |
| Fields | `\field` `\fldinst` `\fldrslt` (HYPERLINK only, including the `\l` switch for a same-document reference, read as `#name` and written back the same way; see [Fields](#fields)) |
| Running content | `\header` `\headerf` `\headerl` `\headerr` `\footer` `\footerf` `\footerl` `\footerr` (→ `RunningContent`, not the body flow) |
| Page geometry | `\paperwN` `\paperhN` `\marglN` `\margrN` `\margtN` `\margbN` `\headeryN` `\footeryN`, and the first section's `\pgwsxnN` `\pghsxnN` `\marglsxnN` `\margrsxnN` `\margtsxnN` `\margbsxnN` over them (→ `PageGeometry`) |
| Drawings | `\shp` `\*\shpinst` `\shpleftN` `\shptopN` `\shprightN` `\shpbottomN` `\shpzN` `\shpbypara` `\shpbypage` `\shpbymargin` `\shpbxpage` `\shpbxmargin` `\shpbxcolumn` `\shptxt`, and the `{\sp{\sn name}{\sv value}}` pairs `shapeType` `fFilled` `fillColor` `fillBackColor` `fillType` `fillAngle` `fillFocus` `fLine` `lineColor` `posrelv` `fFitShapeToText` (→ `DocumentShape`) |
| Frames | `\abswN` `\abshN` `\posxN` `\posyN` `\posxl` `\posxc` `\posxr` `\posxi` `\posxo` `\posyil` `\posyt` `\posyc` `\posyb` `\posyin` `\posyout` `\phcol` `\phmrg` `\phpg` `\pvmrg` `\pvpg` `\pvpara` `\wraparound` `\nowrap` `\overlay` `\wrapthrough` `\dxfrtextN` `\dfrmtxtxN` `\dfrmtxtyN` (→ a `DocumentShape` holding the framed paragraphs; see [Frames](#frames)) |
| Page background | The shape in `{\*\background}`, read where `\viewbksp1` shows it, as LibreOffice takes `\viewbksp` for Word's `w:displayBackgroundShape`; its fill is a shape's (→ `RichTextDocument.PageBackground`). A write states both. A `fillFocus` near 50 is an axial gradient, on a page or a shape |
| Style sheets | `\stylesheet`, `\sN`, `\csN` (including `\*\csN`), `\sbasedonN`, `\snextN`, `\additive`, paragraph and character formatting within style definitions, style names (see [Style sheets](#style-sheets)) |
| Notes | `\footnote` `\ftnalt` `\chftn` (→ `DocumentNote`; see [Notes](#notes)) |
| Tracked changes | `\revised` `\deleted` `\revauthN` `\revauthdelN` `\revdttmN` `\revdttmdelN` `\revtbl` (→ `TextRevision`; see [Tracked changes](#tracked-changes)) |

## Approximated

| Construct | Approximation |
|---|---|
| `\headerr` / `\footerr` | Read as the every-page destination: the model names the first page, the even pages, and the rest, and the right-hand page is the rest |
| `\liN` (twips) | Exact left indent `N / 20` points, clamped to the model range; equivalent 18-point steps retain the legacy representation. The writer emits the resolved left offset in twips |
| `\fs` on write | Rounded to the nearest half-point (`\fs` is integer half-points) |
| `\'hh` under a double-byte code page | Decoded byte by byte through a Latin-1 fallback for `0x80`-`0xFF`, reported once as `rtf.codepage`. Windows-1252 and every other single-byte code page - Cyrillic 1251, Central European 1250, Greek 1253 and the rest a font's `\fcharset` names - are exact, decoded from the runtime's code-page data without registering a provider. `\uN` text is always exact |

## Ignored — parsed, no effect on the model

Unrecognised formatting control words are silently ignored (predictable
degradation), for example `\lang`, `\viewkind`, `\widowctrl`, `\kerning`, the
section break kinds (`\sbkpage` and the rest), and the document and section
options other than the page geometry; `\sect` and `\sectd` matter only to which
section's page is read. The character-set words `\ansi`, `\mac`, `\pc` and
`\pca` are ignored too: the active code page comes from `\ansicpgN`, a font's
`\fcharsetN`, or the caller's default, and nothing else sets it. Shape
properties other than those named under [Drawings](#drawings) are ignored.

List markup does not set `ListKind` in this release (it stays
`ListKind.None`); list tables are detected and skipped (below). Numbering,
restarts and nested marker reconstruction are not implemented, and no synthetic
text bullet markers are generated. LibreOffice's plain text export marks nested
levels where `broilerdoc dump` does not, and rightly: Writer's text projection
(`ascatr.cxx`: `GetBulletChar`) uses `•` (`U+2022 BULLET`) at level 0, `◦`
(`U+25E6 WHITE BULLET`) at level 1 and `▪` (`U+25AA BLACK SMALL SQUARE`) at
level 2.

## Skipped destinations — content dropped

`\pict` and `\object`/`\*\objdata` are skipped and reported once as
`rtf.embedded` - or once as `document.capability.not-composed` when the caller
asked for embedded decoding, since that caller is owed an answer to what it
asked rather than a note about the document. Also skipped: `\info`
`\annotation` `\colorschememapping` `\latentstyles` `\datastore` `\themedata`
`\generator` `\pntext`, and any unknown ignorable destination introduced by
`\*` - except `\*\shpinst`, which the reader understands (a shape arrives as
`{\*\shpinst …}`). `\stylesheet` is read, not skipped. `\binN` binary data is
skipped at the tokenizer (reported as `rtf.bin` when it exceeds `MaxBinBytes`).

A skipped destination is skipped with every group inside it. A destination the
reader understands - a field, a header or footer, a font or colour table, a
shape - has no effect inside one it skips: a header inside a stylesheet does
not become the page's header. The one thing still reported about a skipped
subtree is a picture or object in it, because `rtf.embedded` reports what the
document lost and a picture inside `{\*\shppict ...}` is a picture all the
same.

`\listtable` and `\listoverridetable`, with or without `\*`, are detected and
skipped with one warning, `rtf.list`, per read, so the read status is
`Partial`. The warning reports unsupported list semantics; it does not claim
the table is valid or referenced by a body paragraph. The whole subtree stays
opaque: nested fields, running content, formatting and font tables cannot leak
into or change the body. Text before and after a closed table is preserved,
truncated tables remain skipped, and the tokenizer byte and nesting limits
apply. This is detection, not list-table interpretation.

`\header` and `\footer` are not skipped; see [Running content](#running-content).

## Encoding and Unicode fallback

After `\uN`, literal text, hexadecimal bytes and character-producing control
symbols count towards `\ucN`, so the real text that follows is retained. A zero
fallback count keeps the escaped character. A control word and its argument
count as one fallback character; when skipped they neither emit content nor
change formatting. An opening or closing group brace ends unfinished fallback
skipping before the delimiter, so the next group's text, or text after a
header/footer destination, is retained even when the preceding fallback is
shorter than its `\ucN` count.

`\plain` resets byte decoding along with the character style: it restores the
declared default font's charset, or the document's `\ansicpgN`/caller default
code page when that font entry is unavailable. A previous run's font charset
does not leak past the reset.

## Default font

An explicit `\deffN` selects the font-table family inherited by runs with no
`\fN`, including runs reset with `\plain`; an explicit run font overrides it.
The family is resolved after reading the table, so the declaration may precede
it. Missing declarations, unknown indices and blank font names leave the
model's default family unspecified. The writer includes the default family in
the font table and selects it with `\deffN`; with no default family stated it
keeps the blank `\f0` entry as the default, so the family reads back
unspecified.

Font-table names decode `\uN` with group-scoped `\ucN` fallback counts, literal
high bytes and hex-byte escapes in the active code page, and escaped
braces/backslashes, preserving non-ASCII family names the writer emits,
including UTF-16 surrogate pairs. Decoded names remain table metadata and never
enter body text. A semicolon within a font name is written as `\u59?`, distinct
from the raw semicolon that ends the entry; default and explicit run families
both keep it on read-back.

## Style sheets

A `\stylesheet` destination is read into document styles. Each child group
declares a paragraph style (`\sN`, `\s0` being the default Normal paragraph
style) or a character style (`\csN`, often `\*\csN` for legacy readers). An
`\additive` character style applies over the active paragraph style rather than
replacing it. A declaration consists of:

- an optional base style `\sbasedonN`, resolved transitively with cycle
  protection (circular references degrade without infinite recursion);
- an optional next-paragraph style `\snextN`;
- paragraph formatting (`\ql`, `\qc`, `\qr`, `\qj`, `\liN`, `\sbN`, `\saN`,
  `\slN`, `\slmult1`, `\cbpatN`) → `ParagraphStyle`;
- character formatting (`\b`, `\i`, `\ul`, `\strike`, `\v`, `\caps`, `\scaps`,
  `\fsN`, `\fN`, `\cfN`, `\cbN`, `\highlightN`) → `InlineStyle`, with font
  names and colours from `\fonttbl` and `\colortbl`;
- the name, ended by `;`. Alias names follow the primary name after commas
  (`,`).
  `\uN`, `\'hh` and escaped characters in the name are decoded.

In the body, `\sN` sets the active paragraph style (alignment, margins,
spacing, background, and character defaults for following runs); `\csN`
applies character style properties additively to runs; `\pard` restores the
`\s0` (Normal) defaults; `\plain` resets run formatting to the active paragraph
style's character defaults (`\s0`'s if none was set). Direct formatting
overrides style defaults. Unknown or non-style destinations nested in a
`\stylesheet` (headers, footers, shapes, annotations) stay opaque and cannot
escape into the stylesheet or body.

## Fields

HYPERLINK is the only field read as a link. Cached results take precedence;
nested fields retain their own instruction and result state; Unicode and
escaped bytes in field instructions are retained. A field's result belongs to
the destination the field is in, so a `PAGE` field in a footer keeps its cached
number in the footer. A recognised ruby equation with an empty cached result contributes its
base text and reports `rtf.field.ruby`; phonetic annotation layout is not
represented.

A `\l` target name may not contain a quote: the field argument is quoted and
RTF has no escape for a quote inside it, so one would truncate the target
silently, and the shared rule refuses it for every format. Targets otherwise
follow that rule, or report `rtf.link` (see [Security](#security-guarantees-adr-0004)).

## Notes

A `\footnote` group in the body, a table cell or a frame is a note cited where
the group stands: one citation character holding a `DocumentNote` whose
paragraphs are the group's, `\par` between them. `\ftnalt` makes it an endnote.
Word writes the label as `{\super\chftn}` just before the group and repeats it
at the head of the note's text; the label is drawn from the citation, so the
note's `\chftn` and the space after it are left out and the body's draws
nothing. A label the author typed instead of `\chftn` stays body text, and the
note is numbered. A `\footnote` in a header, footer, shape or another note has
nothing to be cited from and is skipped; a header, font or colour table, list
table or shape inside a note is skipped with everything in it. LibreOffice reads
`A{\footnote ...link...}B` as `A1B`, and so does this reader, the link in the
note.

The writer writes each note the same way, `{\super\chftn}` then
`{\footnote\pard\plain{\super\chftn} ...}`, a note's own mark in place of
`\chftn`. A note cited inside a note is dropped (`rtf.note.nested`).

## Tracked changes

`\revised` marks inserted text and `\deleted` deleted text, as a `TextRevision`
on the runs, and on the paragraph mark where one is open at `\par`.
`\revauthN` and `\revauthdelN` name the author by index into `\revtbl`;
`\revdttmN` and `\revdttmdelN` give the time as a packed DTTM, taken as
universal time. Deleted text is kept, as LibreOffice keeps it, and a deletion
wins over an insertion the same text is in. The writer states no changes: it
writes the document as accepting them would leave it, deleted text left out and
inserted text plain, and reports `rtf.revision`.

## Line spacing

Positive `\slN` with `\slmult1` maps to `ParagraphStyle.LineSpacing` as
`N / 240` (`\sl276\slmult1` is 1.15-line spacing). The two controls can arrive
in either order; their state follows group scope and `\pard` resets it. A final
body paragraph keeps its properties when the root group closes without `\par`.

Without `\slmult1` the amount is a height in twips, as LibreOffice reads `\sl`
(`RTFKeyword::SL`): positive is the least a line is and negative exactly its
height (`ParagraphStyle.LineHeightRule` and `LineHeight`, kept within Word's
1584 points). `\sl0` is single spacing, and a negative multiple is taken by its
size, as LibreOffice takes it. `tdf133437.rtf` sets every line of a bank
statement at exactly ten points with `\sl-200`.

The writer emits finite positive non-default multipliers rounded to the nearest
1/240 with `\slmult1`, a stated height as `\slN\slmult0` with the sign giving
the rule, and omits default spacing.

## Runs of spaces

LibreOffice lays out each space in a run of two or more in an RTF document a
sixth of an em wider than its font makes it - its importer's account of how
Word lays out RTF - unless the document says it came from elsewhere
(tdf#123703). It inserts a U+2006 before each such space; this reader keeps the
text as typed and sets `DocumentStyleDefaults.WidensSpaceRuns`, and both
layouts widen the run. A lone space keeps its width, and so does a run in a
Courier family, as LibreOffice leaves it. `tdf123703.rtf` pads each line with
118 spaces: LibreOffice puts the second word on a line of its own and draws two
pages.

The document says otherwise in the three ways LibreOffice checks (tdf#128428):
a `{\*\generator}` whose name does not start with "Microsoft", a `\stshfdbchN`
other than 31505, or `\dntblnsbdb`. The writer writes
`{\*\generator Broiler.Documents;}` for a document that does not widen its runs
and no generator for one that does, so a write read back, here or by
LibreOffice, lays out as the document did. DOCX, ODF and HTML have no such
setting, so a converted document keeps its spaces at their font's width.

## Page breaks

RTF spells an explicit page break two ways, and both read into
`ParagraphStyle.PageBreakBefore`:

- `\pagebb` is the paragraph property, and what LibreOffice writes for a
  paragraph that starts a page. `\pard` clears it and `\pagebb0` turns it off.
- `\page` is content: it means "break here" and is read onto the paragraph that
  follows. A `\page` inside a paragraph ends that paragraph first, so
  `text\page more\par` and `text\par\page more\par` reach the same model. A
  break is spent by the first paragraph to close after it, so one `\page`
  never starts more than one page.

Limits of that mapping:

- **A break with no paragraph after it is dropped**, reported once as
  `rtf.pagebreak.empty` (informational): at the end of a document and between
  two consecutive `\page`. It asks for a blank page, which Word draws; the model
  has no paragraph to put it on, and inventing an empty one would add a line
  the document does not contain and that the writer would emit as content.
- **A break spelled as a section break is not read.** `\sect` and the section
  break kinds (`\sbkpage`, `\sbkeven`, `\sbkodd`, `\sbkcol`) are ignored: the
  model has no sections, so such a document reads as one continuous flow (the
  page a section states is read; see [Page geometry](#page-geometry)).
  LibreOffice writes `\sectd\sbknone` for its one section and `\pagebb` on the
  paragraph, but a document from elsewhere may break by section.

## Running content

`\header` and `\footer` open destinations whose paragraphs reach
`RichTextDocument.RunningContent`, never the body flow: a letterhead does not
belong in the middle of the letter. RTF spells eight such destinations and the
model holds six, three page selections each for header and footer:
`\headerf`/`\footerf` are the first page's, `\headerl`/`\footerl` the left
(even) page's, and `\header`/`\footer` every page the other two do not claim.
`\headerr`/`\footerr` read as the latter: they name the right, odd page, which
is every page in a document that does not distinguish them.

`\titlepg` is read and written. It is what makes `\headerf` and `\footerf`
mean anything, and makes a band the first page does not state *empty* there
rather than the default one - the fact `w:titlePg` carries in DOCX and a
master-page chain in ODF. **LibreOffice disagrees and this reader follows
Microsoft's format**: given `\titlepg{\footer ...}` with no `\footerf`, Word
draws no footer on the first page and LibreOffice draws the default one.
LibreOffice's DOCX importer honours the flag the Word way, so matching its RTF
reading would make this component's own two readers disagree.

A page break inside running content is read, since `\pagebb` is a paragraph
property wherever a paragraph is, but is not written back
(`rtf.pagebreak.dropped`).

## Page geometry

`\paperw`, `\paperh`, `\margl`, `\margr`, `\margt`, `\margb` and the
`\headery`/`\footery` distances are read into `PageGeometry`. They are twips,
twenty to the point, stated one control word at a time, so they are held aside
and made into a page when the document ends.

A section states a page of its own with `\pgwsxn`, `\pghsxn`, `\marglsxn`,
`\margrsxn`, `\margtsxn` and `\margbsxn`, each defaulting to the document's word
of the same meaning (RTF 1.9.1). The document's page is the one it opens on,
the first section's: section words before the first `\sect` are read over the
document's, `\sectd` clears them back to the document's, and those after the
first `\sect` are not read - they belong to a section this reader does not hold
and would otherwise turn the whole document landscape for one landscape section
at its end. `\lndscpsxn` and `\landscape` are flags beside the size, not an
instruction to swap it: a landscape section's stated width and height are
already the landscape page's, and LibreOffice draws
`\lndscpsxn\pgwsxn11907\pghsxn16840` portrait.

A document that states no paper size gets no page rather than a guessed one,
which would be indistinguishable from a size the author chose. A document whose
margins leave no column to write in gets none either, reported as
`rtf.page.geometry`.

`\cols` divides the page into that many text columns, `\colsx` apart (720 twips
when absent, as the specification says). Columns that leave no room keep the
page and lose the columns, as `rtf.columns.geometry`. A write states both when
there is more than one column. `\column`, the column break, is
`ParagraphStyle.ColumnBreakBefore` on the paragraph after it where the page has
columns, carried the way `\page` carries a page break, and a page break where
it has one, as Word takes it. A write puts `\column` where the paragraph with
the break starts.

This is the page, not sections. Everything else a section states is ignored,
and a document whose sections have columns or margins of their own is written
with the page and its columns alone, which `rtf.sections` says.

## Tables

A body table is read into a `DocumentTable` over the paragraphs its cells hold.
`\trowd` starts a row definition, and each `\cellx` closes one cell of it at
the right edge it names, with the borders and shading stated before it.
`\intbl` puts a paragraph in the table; as a paragraph property, `\pard` clears
it and a closing group restores it. `\cell` ends a cell's last paragraph, with
or without `\intbl`, and `\row` closes the row under the definition in effect.
The first paragraph out of the table ends the table.

The row definition and the table being read are not pushed and popped with
braces: writers wrap cells and row ends in groups of their own (`{\b A1 \cell}`,
`{\row}`, a whole table inside `{\trowd ... \pard}`). A row with no `\trowd` of
its own takes the previous row's definition, as Word does.

Rows need not agree on their edges. The grid is every edge any row states, and
a cell spans the grid columns between its own two edges, so every cell is as
wide as its row says - what LibreOffice's own test of `fdo66565.rtf` checks,
with a row of seven cells over a row of eighteen. A table some row of which
holds more cells than it defines, or defines an edge that goes backwards, gets
no widths and is divided evenly, as a table stating no grid is.

- `\trgaph` is the cell padding; a table stating none has none, as LibreOffice
  sets it. `\trpadd*` and `\clpad*` are read into `TableCellPadding`.
- Positive `\trrh` is a minimum height, negative is fixed; fixed rows keep
  their editable text while rendered overflow is clipped.
- `\trhdr` marks a header row; `\trkeep` keeps a row on one page.
- `\clbrdrt`, `\clbrdrb`, `\clbrdrl` and `\clbrdrr` point the following border
  words at a side of the next cell: `\brdrw` is its width, `\brdrcf` its colour
  (black when absent or auto), and any kind but `\brdrnone` draws it as a
  single line (the model has one kind, so a double border is drawn single).
  The row's own borders (`\trbrdr*`) apply where cell borders are unstated.
- `\clcbpat` is the cell's background.
- Merged cells (`\clmgf`, `\clmrg`, `\clvmgf`, `\clvmrg`) resolve into spanned
  opening cells and continuation cells.
- `\trleft`'s offset of the table from the margin is kept.
- Positioned floating tables (`\tpos*`, `\tdfrmtxt*`, `\tpv*`, `\tph*`,
  `\tbloverlap`) are read into `TablePosition`.
- Nested tables join the outer cell's text, `\nestcell` read as a tab.

A cell mark in a header, footer or shape's text is a tab and a row mark a
paragraph: those hold paragraphs and nothing beside them.

## Drawings

A `\shp` group is read into a `DocumentShape` anchored to the paragraph it sits
in. Its box comes from `\shpleft`/`\shptop`/`\shpright`/`\shpbottom` in twips.
Of the `{\sp{\sn name}{\sv value}}` pairs, `fFilled`, `fillColor`,
`fillBackColor`, `fillType` and `fillAngle` give a solid or gradient fill,
`fLine` and `lineColor` the outline, and `shapeType` whether it is a line. A
shape with no stated line colour is outlined black, Word's default and the one
LibreOffice's import takes in place of Writer's blue (`rtfsdrimport.cxx`). A
shape colour is one integer holding blue, green and red in that order - the
reverse of the rest of the format.

- `\shpz` is read and written: it orders shapes against each other, which in
  RTF is the only thing saying whether a letterhead's stripe or the box over
  it is on top.
- `\shptxt` carries the shape's own text, kept out of the body like a header's.
- `\shpbypage` and `\shpbymargin`, or a `posrelv` of 1 or 0, measure `\shptop`
  from the page or its top margin rather than the paragraph:
  `DocumentShape.OffsetFromPage`, with the margin added, and the shape stands
  that far down whichever page its paragraph is on.
- `\shpbxpage` measures `\shpleft` from the page's left edge, so the margin is
  taken off it; margin and column are where the model already measures from.
- `fFitShapeToText` makes the box grow to its text
  (`DocumentShape.GrowsToText`), and the writer writes it for such a box.
- A line (`shapeType` 20) keeps its endpoints and direction, including reversed
  endpoints and `fFlipV`. A box with no breadth and an outline is drawn as the
  rule its outline makes (`DocumentShape.IsRule`): `tdf133437.rtf`'s statement
  is 285 such lines and an unfilled rectangle around paragraphs set in frames.
- Ellipses (`shapeType` 3) keep their path; `lineWidth` keeps EMU widths as
  points. The writer preserves these paths and widths. Legacy drawing-object
  slanted lines are not read and report `rtf.shape.line`.

Every other shape property is ignored, `pib` (a picture inside a shape) among
them. A shape that ends with no fill, no text and no outline is dropped, as is
one whose box has no width or height and is not a rule. A picture frame
(`shapeType` 75) needs fill or text of its own to be kept, since its picture is
not read and its line alone would be an empty box.

### Frames

A paragraph whose position words make a frame (RTF 1.9.1, "Positioned Objects
and Frames") is read into a `DocumentShape` that holds it, not into the body.
Which words make one is LibreOffice's rule (`RTFFrame::hasProperties`): a
position, a size, or a reference or alignment other than the default; the
distances from the text alone do not. Consecutive paragraphs stating the same
frame share it; the first stating another or none finishes it, and the frame is
anchored to that paragraph, the "next unframed paragraph" RTF measures
`\pvpara` from. `\pard` ends a frame, and a frame in a table is read as the
table's text, as LibreOffice reads it.

- **Across:** `\posx` from the column (`\phcol`, the default), the margin
  (`\phmrg`) or the page's edge (`\phpg`); `\posxl`, `\posxc`, `\posxr` place it
  by its width in that reference, inside and outside taken as left and right.
  Whichever of position and alignment comes last wins. `\posnegx` is not read,
  nor by LibreOffice.
- **Down:** `\posy` from the top margin (`\pvmrg`, the default), the page
  (`\pvpg`) - both `DocumentShape.OffsetFromPage` - or the paragraph
  (`\pvpara`). `\posyt` is the reference's top. `\posyc` and `\posyb` need the
  frame's height, which a frame sized by its text lacks until layout, so they
  are set at the top and reported (`rtf.frame.position`). `\posyil` is the
  paragraph's own place.
- **Size:** `\absw`, or the column's width when not stated. `\absh` above nought
  is the least the frame is and nought leaves it to the text, both
  `DocumentShape.GrowsToText`; below nought is exact.
- **Wrap:** around the frame (`\wraparound`, the default) at `\dfrmtxtx`, above
  and below it (`\nowrap`) at `\dfrmtxty`, or through it (`\overlay`,
  `\wrapthrough`). `\dxfrtext` states both distances.

The writer writes such a shape as a `\shp` with its text, which reads back as
the same box; it writes no frame words.

## The end of the document

The document is the `{\rtf1 ...}` group and ends at its closing brace; what
follows is not read, as in Word and LibreOffice (the grammar is
`'{' <header> <document> '}'`). So a stray `}` mid-file ends the document, and a
section repeated past the brace does not add its footer and picture data again.
Trailing content is reported once as `rtf.trailing` (informational). Blank text
(spaces, tabs, a NUL) and further closing braces are not reported. A closing
brace with no group open closes nothing and does not end the read, and a root
group that never closes is read to the end of the input.

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

The writer emits the honoured subset as pure ASCII: a
`{\rtf1\ansi\ansicpg1252\deffN\uc1` header; `\fonttbl`/`\colortbl` built from
the styles used and the declared default family; the page geometry and the
header and footer destinations the document states, before the body, where a
reader takes them as section properties; one group-wrapped run per style; and a
`\par` after every paragraph. A document that states no page writes no
`\paperw`. Non-ASCII characters are escaped as `\uN?`; there is no raw-byte
mode, so `AsciiOnly=false` is reported once as
`document.capability.not-composed`.

- **Resources.** Font and colour collection covers body paragraphs, body-shape
  text and all six header/footer paragraph variants before either table is
  emitted, so a resource used only in running content or a shape has a table
  index and its family, foreground colour and highlight read back.
- **Inherited size.** A run inheriting a document default size other than 12
  points gets that size stated explicitly, in body, header/footer and shape
  text (issue #180); explicit run sizes take precedence. It reads back as a run
  property rather than a document default, at RTF's half-point precision.
- **Page breaks** are written as `\page` at the head of the paragraph: `\page`
  is in the character stream every reader walks, while a simpler reader steps
  over `\pagebb` silently, and writing both would make a reader that honours
  both break twice. The document's first paragraph takes `\pagebb` instead: a
  leading `\page` makes Writer draw a blank first page, while a break before the
  first paragraph is already satisfied (this codec's layout ignores it there
  too). Either word reads back, so the flag round-trips. A page or column break
  on a header, footer or shape paragraph is not written (`rtf.pagebreak.dropped`).
- **Pictures** are written as a `\pict` destination - `\pngblip` or
  `\jpegblip`, sized with `\picwgoal`/`\pichgoal` - so they survive into Word
  and WordPad. Other image formats are dropped (`rtf.image.format`); a picture
  the caller's resource policy keeps out of the output is left out
  (`rtf.image.omitted`, naming the denial). Because the reader skips `\pict`,
  an image does **not** survive a model → RTF → model round trip; DOCX is the
  format that round-trips pictures.
- **Tables** are written natively (`\trowd ... \cell ... \row`), preserving the
  cell grid, column widths, borders, shading, cell padding, horizontal and
  vertical merges, and floating table positioning.
- **Shapes** are written as a `\shp` group at the head of the anchored
  paragraph, carrying box, fill, outline and text - all the reader takes back.
  A floating picture is instead written into its paragraph
  (`rtf.image.anchored`): a picture in a shape is a `pib` property the reader
  does not read, so such a shape would come back with no picture and no paint
  and be dropped.
- **Hidden sections.** Hidden body-section visibility is not written; the
  content is exported as visible paragraphs, with `rtf.section.hidden`. ODT
  retains the flag; the shared raster paginator omits hidden body sections.
- **Approximated paint and frame flow** are reported as `rtf.fill.extended` and
  `rtf.shape.flow` ([Diagnostics](#diagnostics)).

Round trip is otherwise lossless for any document the reader can produce, with
three further exceptions, all safe (never lossy for text, formatting, colours,
sizes, alignment, or hyperlinks): a table's grid; list kind, which is neither
written nor read and stays at its default; and a page break inside running
content or a shape, read but not written back.

## Diagnostics

Every code this codec reports, on both sides: a code in the source and not
here is an unreported loss, and a code here and not in the source is a broken
promise. `rtf.link` is raised on both sides because the writer re-asks the URL
rule; `document.capability.not-composed` is the shared code for anything a
caller asked for that this codec does not do.

| Code | Severity | Side | Meaning |
| --- | --- | --- | --- |
| `rtf.size` | Warning | Read | Input exceeded `MaxDocumentBytes` and was truncated. |
| `rtf.depth` | Warning | Read | Group nesting exceeded `MaxGroupDepth`; tokenization stopped there. |
| `rtf.bin` | Warning | Read | A `\binN` length exceeded `MaxBinBytes`; the binary data was skipped. |
| `rtf.hex` | Warning | Read | A malformed `\'hh` hex escape was skipped. |
| `rtf.paragraphs` | Warning | Read | The document exceeded `MaxParagraphCount`; the text past the cap was dropped. |
| `rtf.codepage` | Info | Read | A double-byte code page, or one the runtime does not carry, decoded its high bytes through a Latin-1 fallback; `\uN` text is unaffected. Once per read. |
| `rtf.field.ruby` | Warning | Read | An empty-result ruby equation field was read as its base text; phonetic annotation layout is not represented. Once per read. |
| `rtf.list` | Warning | Read | List tables were detected and skipped; list numbering and marker semantics are not imported. Once per read. |
| `rtf.shape.line` | Info | Read | A legacy drawing-object slanted line was not read. Modern `shapeType` 20 lines retain their geometry. Once per read. |
| `rtf.shape.table` | Warning | Write | Tables inside a text box are written as paragraphs. Their text is retained, but their cell layout is not written. Once per write. |
| `rtf.shape.padding` | Warning | Write | Text-box inner padding was not written; the text and outer frame are retained. Once per write. |
| `rtf.shape.flow` | Warning | Write | Linked text frames, minimum-width sizing, vertical text alignment or explicit overflow were not written; frame text is retained. |
| `rtf.fill.extended` | Warning | Write | Bitmap and hatch fills, centred gradients, gradient borders or page-fill bounds were approximated. |
| `rtf.frame.position` | Info | Read | A frame aligned to the centre or foot of its reference was set at its top, its height not being known until layout. Once per read. |
| `rtf.embedded` | Info | Read | Pictures and objects were skipped. Once per read, and replaced by `document.capability.not-composed` when decoding was asked for. |
| `rtf.page.geometry` | Warning | Read | The section properties described a page with no room to write on, so the page was not read. |
| `rtf.columns.geometry` | Warning | Read | `\cols` and `\colsx` left no room to write in a column; the page was kept and read as one column. |
| `rtf.pagebreak.empty` | Info | Read | A page break with no paragraph after it asks for a blank page, which this model cannot hold; the break was dropped. Once per read. |
| `rtf.trailing` | Info | Read | Content followed the root group's closing brace; it is not part of the document and was not read. Once per read. |
| `rtf.link` | Warning | Read, write | A hyperlink target the shared rule refuses. Reading drops the link; writing keeps the run as plain text. |
| `rtf.pagebreak.dropped` | Warning | Write | A page or column break on a header, footer, or shape paragraph was not written; there is no page for it to start. |
| `rtf.sections` | Warning | Write | The document's sections, each with columns and margins of its own, were not written; the page and its columns were written for the whole document. |
| `rtf.section.hidden` | Warning | Write | Hidden section visibility was not written; the retained content is exported as visible paragraphs. |
| `rtf.table.flattened` | Warning | Write | A table was written as its cell paragraphs, in row order; this codec carries no table structure. |
| `rtf.note.nested` | Warning | Write | A note cited inside a note was dropped; RTF cannot nest them. |
| `rtf.revision` | Warning | Write | Tracked changes were written accepted: the deleted text left out and the inserted text written as plain text. |
| `rtf.image.format` | Warning | Write | RTF carries PNG and JPEG pictures; one in another format was dropped. |
| `rtf.image.omitted` | Warning | Write | The caller's resource policy did not permit a picture into the output; the message names the denial. |
| `rtf.image.anchored` | Warning | Write | A floating picture was written into its paragraph; its position beside the text was not kept. |
| `rtf.shape.page` | Warning | Write | A shape anchored to a page by number was written beside its paragraph with `\shpbypage`; RTF can measure from the page but not name it. A shape measured from the page its paragraph is on (`DocumentShape.OffsetFromPage`) is written with `\shpbypage` too, and is not reported: that is exactly what RTF says. |
| `document.capability.not-composed` | Warning | Write | Something the caller asked for that this codec does not compose: `AsciiOnly=false` on a write. |

## Standards and rights

RTF is a vendor format with no standards body. Its rights position (the Microsoft
Open Specification Promise names `[RTF]` itself), the approved labels and the
provenance findings are in [the RTF register](../ip/rtf.md). Decided is not
cleared: no lawyer reviewed it, and patent-freedom is not claimed.
