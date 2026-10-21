# DOCX Conformance

`Broiler.Documents.Docx` reads and writes a dependency-free DOCX subset using
Open XML WordprocessingML package parts.

## Supported Read/Write Subset

- Main document package discovery through `_rels/.rels` with fallback to
  `word/document.xml`.
- Paragraphs, empty paragraphs, tabs, and soft line breaks.
- The paragraph mark's formatting, `w:pPr/w:rPr`, as
  `RichTextParagraph.MarkStyle`: the run formatting the paragraph style gives,
  then the mark's own. An empty paragraph has no run, and the mark is what its
  line is as tall as; a document whose blank lines are 10-point marks under a
  12-point default drew each a fifth too tall without it. Written back as
  `w:rPr` at the end of `w:pPr`, before a section's `w:sectPr`.
- Block-level containers, walked recursively so their paragraphs are read rather
  than dropped: tables (`w:tbl`/`w:tr`/`w:tc`, including nested tables),
  structured document tags (`w:sdt`), accepted revisions (`w:ins`, `w:moveTo`),
  `w:customXml`/`w:smartTag` wrappers, and `mc:AlternateContent` (first
  `mc:Choice`, else `mc:Fallback`). A container's paragraphs are read in
  document order, which for a table is row-major.
- Tables, as a `DocumentTable` over the paragraphs of their cells: the
  `w:tblGrid` column widths, `w:gridSpan` and `w:vMerge` spans, per-cell and
  per-table borders (`w:tcBorders`, `w:tblBorders`, including the `insideH` and
  `insideV` edges), cell shading (`w:shd`), the `w:tblCellMar` left margin as the
  cell padding, header rows (`w:tblHeader`), the height a row states
  (`w:trHeight`), and tables nested in a cell. The
  cells hold no text of their own: a cell names a range of the document's
  paragraphs, so a caret, a selection, a style, and every codec's text handling
  go on working through one flat list. Written back as `w:tbl`.
- Direct inline formatting: bold, italic, underline, strikethrough, font
  family, font size, foreground color, and background shading. A
  `w:color w:val="auto"` is the automatic colour (ECMA-376 §17.3.2.6), which
  Word and LibreOffice draw black, so on a run it clears the colour its style
  gave rather than being passed over; a report whose Normal style is blue and
  whose every run says `auto` drew blue without it.
- Capitalization: `w:caps` and `w:smallCaps`, read and written as a style
  attribute. The text keeps the casing the author typed and the capitals are
  applied when drawing, so an open-and-save does not rewrite it. An explicit
  `w:val="0"` clears only the kind it names; a run declaring both draws all caps,
  as Word does.
- Named styles from `word/styles.xml`, resolved per ECMA-376 §17.7.2: document
  defaults (`w:docDefaults`), then the `w:basedOn` chain from its root down to
  the style named by `w:pStyle` (paragraphs) or `w:rStyle` (runs), then direct
  formatting. The default style (`w:default="1"`) applies only to content that
  names no style of its own. Template documents carry nearly all of their
  formatting here rather than inline.
- Theme fonts from `word/theme/theme1.xml`: `w:rFonts` theme references such as
  `w:asciiTheme="majorHAnsi"` resolve to the theme's major/minor latin typeface.
  An explicit font name on the same element wins.
- A run's family is the font its Latin text is drawn in: `w:ascii`, `w:hAnsi`
  or the theme fonts they name. `w:cs` and `w:eastAsia` name the fonts for
  complex-script and East Asian text (ECMA-376 §17.3.2.26), so a style that
  names only those keeps the Latin font it inherited; they give a family only to
  a run that has inherited none. They used to be taken for the Latin font, and
  `tdf170602_exportContext.docx`'s headings, whose styles say only
  `w:cs="Arial"`, were drawn in Arial over a Times New Roman document.
- Paragraph formatting: left/center/right alignment, line spacing, spacing
  before/after, indentation, bullet lists, and numbered lists. `w:spacing w:line`
  is read under all three of its rules (ECMA-376 §17.3.1.33): 240ths of a line
  under `auto`, which an omitted `w:lineRule` means, and a height in twips under
  `exact` and `atLeast` (`ParagraphStyle.LineHeightRule` and `LineHeight`, kept
  within Word's 1584 points). Each replaces what a style below stated, and the
  writer writes the rule back. The layout sets an exact line's baseline four
  fifths of the way down it, as LibreOffice does.
- List numbering from `word/numbering.xml` (ECMA-376 §17.9). Each paragraph
  names the list it is numbered in (`ParagraphStyle.List`), with all nine levels
  of its abstract numbering: `w:start`, `w:numFmt` (decimal, `decimalZero`,
  letters, roman numerals, `bullet`, `none`; any other is decimal and reported as
  `docx.numbering.format`), `w:lvlText`, `w:isLgl`, `w:lvlRestart` and the
  level's `w:ind`. Every `w:num` naming the same abstract numbering is one list
  and counts on from the others; one with a `w:lvlOverride` (`w:startOverride`,
  or a replacement `w:lvl`) is a list of its own. An abstract numbering with a
  `w:numStyleLink` takes the levels of the one whose `w:styleLink` names that
  style. The list and the level are each taken from the nearest `w:numPr` that
  states them, the paragraph's own or its style chain's; with no `w:ilvl`, the
  level is the one whose `w:pStyle` is the paragraph's style, which is how
  heading styles are numbered. `w:numId` 0 takes a paragraph out of its style's
  list. An absent `w:start` starts at one, as LibreOffice reads it, where
  §17.9.25 says nought. A Symbol or Wingdings bullet in the private-use area is
  read as the character it draws. The label starts where the level's `w:ind`
  hangs it (its `w:left` less its `w:hanging`), the paragraph's own `w:ind`
  overriding attribute by attribute, at indent level one at least. An empty
  paragraph that only carries a section's `w:sectPr` takes no number, as
  LibreOffice, which keeps no such paragraph, gives it none
  (`tdf170602_exportContext.docx`).
- Lists are written back as an abstract numbering and a `w:num` per list, with
  the paragraph's `w:ind` stated as the text start and a 360-twip hang. A list
  that names no definition is written in plain lists that draw it as the layout
  does (bullets solid, hollow, square; numbers decimal, letters, roman), a new
  `w:num` restarting each run of the numbered one. A list item that restarts its
  list's count (ODF's `text:start-value`) is written without the restart
  (`docx.list.restart`).
- Page breaks, read from both spellings WordprocessingML has for the one thing
  and written back as one:

  | WordprocessingML | Read | Written |
  | --- | --- | --- |
  | `w:pPr/w:pageBreakBefore` | `ParagraphStyle.PageBreakBefore` on that paragraph | `w:pPr/w:pageBreakBefore`, first in `w:pPr` |
  | `w:r/w:br w:type="page"` at the end of a paragraph | `PageBreakBefore` on the **following** paragraph | as `w:pageBreakBefore` on that following paragraph |
  | `w:r/w:br w:type="column"` | a page break in a section of one column; `ParagraphStyle.ColumnBreakBefore` on the **following** paragraph in a section of several | `w:br w:type="column"` opening the paragraph that has `ColumnBreakBefore` |
  | `w:br` with no type, or `w:type="textWrapping"` | the line break it has always been (`U+2028`) | `w:br` |

  `w:pageBreakBefore` follows the on/off rule the character properties do:
  present means on, and `w:val="false"`, `"0"` or `"off"` turns off a break the
  style chain applied — which is how a template that opens every chapter on a
  fresh page states the one heading that must not. A break stated in a paragraph
  style is resolved through the same chain everything else is, so a template
  that keeps it there rather than on the paragraph still paginates.
  `w:br w:type="page"` is what Word writes when a user presses Ctrl+Enter, and
  it sits at the end of the paragraph *before* the page it starts, which is why
  it is read onto the next one. The write is always the property form, because
  that is what the model holds: a break written back into a run would make one
  paragraph's markup depend on the paragraph after it, and would have nowhere to
  put a break on the first paragraph of a document. It is written first in
  `w:pPr` because that is the position `CT_PPr` gives it - see the next bullet
  for the rule, and for how long this writer stated it without keeping it.
- Property order, which the schema fixes rather than leaves to taste. `CT_PPr`
  and `CT_RPr` are both `xsd:sequence`, so a property is not merely present or
  absent: it has exactly one legal position. Word does not shrug at an
  out-of-order property container the way it does at an element it has never
  heard of - it can refuse the file, and a refusal costs the whole document
  rather than the one property. What is written, in the order it is written:

  | Container | Order written | Positions in the sequence |
  | --- | --- | --- |
  | `w:pPr` | `w:pageBreakBefore`, `w:numPr`, `w:shd`, `w:spacing`, `w:ind`, `w:jc` | 4, 7, 10, 22, 23, 27 |
  | `w:rPr` | `w:rFonts`, `w:b`, `w:i`, `w:caps`/`w:smallCaps`, `w:strike`, `w:color`, `w:sz`, `w:u`, `w:shd` | 2, 3, 5, 7/8, 9, 19, 24, 27, 30 |

  `DocxPropertyOrderTests` asserts both sequences. It walks whatever
  properties are present rather than checking an exact list, so a property added
  to the writer later is checked in its right place rather than failing a list
  it was never named in.
- External hyperlinks for `http`, `https`, and `mailto`, plus internal anchor
  links, written as `w:hyperlink w:anchor`. Absolute `http`, `https` and `mailto`, plus a non-empty `#fragment` whose name carries no whitespace, quote or second `#`; every other scheme, every relative target and a bare `#` are refused, on write as well as on read, with the link written as plain text. One predicate decides it for all five codecs (`DocumentLinkTarget`), which is what stopped them disagreeing. A fragment preserves the reference the source made and not a working jump: no codec here reads or writes a bookmark and the model has nowhere to put one, so the name it points at is not carried.
- Embedded pictures, read and written as a single object replacement character
  (`U+FFFC`) whose run carries the image. Both shapes Word writes are read:
  DrawingML (`w:drawing`, inline and anchored, including a picture inside
  `mc:AlternateContent`) and the legacy VML shape (`w:pict`/`v:imagedata`). The
  display size comes from `wp:extent` in EMUs, or from the VML shape's CSS
  `width`/`height`; alternative text comes from `wp:docPr@descr`. A write stores
  each distinct image once under `word/media`, with its relationship and a
  content-type default for its extension. Raster formats only: PNG, JPEG, GIF,
  BMP, TIFF, WebP, and ICO.
- `w:titlePg`, which is what makes a `first` header or footer mean anything and
  what makes a band the first page does not name *empty* there rather than the
  default one. A letterhead is the case: a first-page header, a default footer,
  and no footer on page one. Unread, the empty first-page slot fell back and a
  page number was drawn under the letterhead. It is written back whenever a First
  part exists or the model says the first page is different. A `first` part in a
  section without it is not read (`docx.header.first`): Word draws the default
  there, and the model would draw the first.
- `w:evenAndOddHeaders` in `word/settings.xml`, which is what makes an `even`
  part mean anything. Without it Word and LibreOffice draw the default header
  and footer on every page, so an `even` part is not read
  (`docx.header.even`); a write that has an Even part writes a settings part
  turning it on, without which Word would ignore the part it was given.
- A header's and a footer's shapes, read onto the running content rather than
  into the body. They are placed against the page: `svg`-style horizontal offsets
  stay measured from the text column, while the vertical one is measured from the
  top of the page, because running content repeats and has no paragraph of the
  body to hang from. Every vertical `relativeFrom` therefore converts for these —
  `page` and `topMargin` from the page's top edge, `margin` and `bottomMargin`
  through the geometry — and they are written back into the header part with
  `positionV relativeFrom="page"`. A part that holds only a stripe and no words
  is still a part. They were anchored to a body paragraph before, which put a
  letterhead on page one only and dropped it entirely when the body was shorter
  than its header.
- The page background, `w:background` (ECMA-376 §17.2.1), is
  `RichTextDocument.PageBackground`, read only where `w:settings` holds
  `w:displayBackgroundShape` (§17.15.1.25): Word keeps a page colour it is not
  told to show, and LibreOffice reads neither the colour nor the shape without
  the switch. The VML shape inside, `v:background`, is the background Word draws,
  and `w:color` the colour it falls back to. A VML fill is converted the way
  LibreOffice converts it (`oox/source/vml/vmlformatting.cxx`): its angle runs
  counter-clockwise from the bottom, a quarter turn from the model's, a linear
  gradient starts from `color2`, a focus of 100% or a negative angle reverses
  it, and a focus near 50% is the mirrored gradient the model calls axial, with
  `fillcolor` at the edges. A rectangular gradient (`gradientRadial`) is read as
  its outer colour, which is most of the page; a pattern of one colour is that
  colour; a pattern of two or a picture is `w:color`. Those three are reported
  as `docx.background.fill`. A write puts `w:color` - a gradient's middle colour -
  on `w:background`, a `v:background` for a gradient, and turns the switch on.
  A DrawingML gradient of three stops whose first and last agree is the same
  axial fill, and an axial shape fill is written that way.
- VML shapes in a `w:pict` - `v:rect`, `v:roundrect`, `v:oval` and a `v:shape`
  that holds no picture - are floating shapes, which is how Word wrote a text box
  before 2010. The box is the shape's CSS `style`: `width`, `height`, and
  `margin-left`/`margin-top` from the column and the paragraph by default.
  `mso-position-horizontal-relative` and `mso-position-vertical-relative` name
  the other frames in VML's own words (`page`, `margin`, `left-margin-area`,
  `top-margin-area` and so on), and they convert as a DrawingML anchor's do:
  across into the column, down into `OffsetFromPage`. A negative `z-index` puts
  it behind the text. Its fill is read as the page background's
  is, reported as `docx.shape.fill` where it had to be approximated; its outline
  is `strokecolor`, VML's default black, or none where `stroked` turns it off;
  its text is the `w:txbxContent` in its `v:textbox`. They were read as nothing
  before, and the text in them was lost.
- A DrawingML shape's colours - its fill, its gradient's stops and its outline -
  are read from `a:srgbClr`, `a:sysClr` (its `lastClr`), a few `a:prstClr` names,
  and `a:schemeClr` resolved through the theme's colour scheme (ECMA-376
  §20.1.6.2), with `bg1`/`tx1`/`bg2`/`tx2` mapped to `lt1`/`dk1`/`lt2`/`dk2` as
  Word's default `w:clrSchemeMapping` maps them. The transforms under any of them
  are applied in order (§20.1.2.3) with LibreOffice's arithmetic, measured against
  its rendering: `a:shade` and `a:tint` toward black and white in linear RGB at a
  gamma of 2.3, `a:lumMod`, `a:lumOff` and `a:satMod` on the HSL colour, `a:alpha`
  as the colour's opacity, which the writer writes back. The page painter lays a
  translucent gradient's bands edge to edge rather than overlapping them, and the
  PDF writer, which emits no transparency group, blends a translucent fill over
  white. A template's themed box
  was read as having no fill before, and was dropped. `phClr` - a colour a
  style supplies - is not resolved. A pattern fill, `a:pattFill`, is two colours
  the model cannot hold together - LibreOffice draws hatch lines in `a:fgClr`
  over `a:bgClr` - and is read as its background colour, which is nearly all of
  what shows, or as its foreground where it states no background; it is reported
  as `docx.shape.fill`.
- Paragraph shading, `w:pPr/w:shd` (ECMA-376 §17.3.1.31), directly and through
  the style chain, is `ParagraphStyle.Background`: the fill behind a `clear`
  pattern, the pattern's colour for `solid`, and nothing for `nil` or an `auto`
  fill, which is how a paragraph takes away its style's shading. Other patterns
  are read as their fill. It is written back as a `clear` `w:shd`. The layout
  paints it as LibreOffice does, measured: over the paragraph's lines from its
  indent to the column's edge, its space before and after left white unless the
  next paragraph is shaded the same, when the space between is shaded too. The
  PDF writer does not paint it yet. It was read for runs and cells only before.
- A drawing group, `wpg:wgp`, is read as a floating shape for each `wps:wsp`
  box in it, nested `wpg:grpSp` groups included. Each child's `a:xfrm` is mapped
  through the `a:chOff`/`a:chExt` child space of every group above it into the
  anchor's extent (ECMA-376 §20.1.7.6), and the children share the group's
  anchor, `behindDoc`, `relativeHeight` and wrap, drawing in the order the group
  lists them. The group was read as its first box at the size of the whole group
  before. Rotation and flips are not applied, and a picture inside a group is not
  read (`docx.shape.group`). A write puts each box back as a shape of its own.
- Floating pictures: an anchored (`wp:anchor`) picture is read as a floating
  shape carrying the image, placed at the `posOffset` of its `wp:positionH` and
  `wp:positionV`, converted from whichever frame the anchor names into the text
  column and its paragraph — the same box a `wps:wsp` shape gets, because it is
  the same anchor. Horizontally every frame converts exactly: a stripe stated at
  nothing from the left margin and one stated at minus the margin from the
  column are the same stripe. Its `behindDoc` is read
  and written back, so a stripe the letter is written on top of and a stamp
  meant to cover it each stay on the side of the text they were authored on.
  The picture is written back as an anchored picture. A logo hung over a
  letterhead therefore stays over it rather than being pushed into the first
  line, which moved the whole letter down by the height of the picture.

## Intentional Limits

- Tracked deletions, embedded objects, fields, comments, headers, footers,
  footnotes, and section layout are skipped or approximated with diagnostics
  where applicable.
- A floating picture keeps its position, its layer, its stacking and its
  wrapping. `behindDoc` is the shape against the text and `relativeHeight` is the
  shape against the other shapes; both are read and both are written. An anchor
  that states no `relativeHeight` reads as zero, which leaves the order the
  shapes were read in.
  `wrapSquare`, `wrapTight` and `wrapThrough` all wrap around the shape's box:
  the outline the last two follow is not, so text clears the frame rather than
  the picture inside it. A wrapped line keeps one span — it runs down whichever
  side has more room, which is `wrapText="largest"` — so `bothSides` gets the
  larger side rather than text down both. `distL` and `distR` become one
  clearance, applied on both sides. A shape anchored to a later paragraph does
  not narrow a line above it, and an indented paragraph's band is measured from
  its own left edge rather than the column's, so a shape overlapping one is out
  by the indent. A picture whose anchor states no
  `wp:extent` has no box to float at and stays in the text. Rotation and picture
  effects are dropped.
- A picture's source crop (`a:srcRect`) and its `a:prstGeom` are read, and the
  ellipse is the only preset represented. That is a deliberate choice rather than
  a stopping point: the round portrait is what every CV and letterhead template
  puts its photograph in, and drawn square it is the first thing anyone notices.
  Every other preset draws as the rectangle it is inscribed in, which is what
  this codec did for all of them before. A crop is stated as fractions of the
  source, so it survives a resize; crops that meet or cross leave no rectangle to
  draw and are ignored rather than losing the picture. The crop is applied before
  the mask, so the ellipse is inscribed in the part actually drawn.
- A vertical `relativeFrom` of `page`, `topMargin`, `margin` or `bottomMargin`
  measures from the page, and where a paragraph sits on a page is a layout
  result the reader does not have — a shape on the fortieth paragraph could land
  on any page. So the model holds the frame rather than a converted offset:
  `DocumentShape.OffsetFromPage`, with the offset turned into one from the page's
  top edge through the geometry, and the layout places the shape that far down
  whichever page its paragraph is on. The offset used to be kept as stated and
  measured from the paragraph, which hung a full-page backdrop anchored at
  nought from the page's top below its paragraph. A write states
  `relativeFrom="page"`. `insideMargin` and `outsideMargin` name a side that
  depends on the page's parity: vertically they are kept as stated and measured
  from the paragraph, horizontally they are read as an odd page's, and both are
  reported as `docx.anchor.relativefrom`.
- EMF/WMF metafiles — what Word embeds for charts, SmartArt, and shape fallbacks
  — are not carried, because they cannot be decoded to pixels here. They are
  reported as `docx.image.format` rather than kept as a picture that would draw
  as nothing.
- An image linked with `r:link` rather than embedded is not fetched; reading it
  would mean a network or file request driven by document content (ADR 0004).
- Style resolution covers the attributes `RichTextDocument` models. Style
  features outside it — character spacing/scaling, conditional table
  formatting — are ignored, as are theme colors
  (`w:themeColor`); Word writes the computed RGB into `w:val` alongside them,
  which is what the reader uses.
- A table style's own `w:pPr` and `w:rPr` are applied to the paragraphs and
  runs in its cells, between the document defaults and the paragraph style, as
  ECMA-376 §17.7.2 orders them: `Table Grid` takes Word's default ten points
  after and 1.15 line back out of every cell, and a paragraph style or direct
  formatting that says otherwise still wins. A table that names no style takes
  the default table style; a nested table takes its own, not its parent's; and a
  `w:tblStyle` that names a paragraph style applies nothing. Word's compatibility
  rule that lets a table style override an 11 or 12 point size or a left
  alignment stated by the default paragraph style is not modelled. The rest of a
  table style — banding, conditional first-row and first-column formatting
  (`w:tblStylePr`), and the borders and shading it states — is not applied, and
  a table naming a style reports `docx.table.style`. Cell vertical alignment,
  text direction, and table indent and alignment are not represented either; a
  table starts at the left margin.
- A table's cells are paragraph ranges, so an edit that adds or removes
  paragraphs moves them and one that spans out of a cell into the body does not
  keep the grid over what it merged. The ranges are moved through the one place
  paragraph counts change, so typing in a cell, splitting its paragraph, and
  deleting inside it all keep the table around the text.
- A row's `w:trHeight` is a **minimum** and never a ceiling: the row is at least
  that tall and grows for content that does not fit. An `w:hRule="exact"` is
  therefore read as a floor too, with `docx.table.rowheight`, because a row that
  clipped its own text would lose content the document has. An explicit
  `w:hRule="auto"` asks for nothing and gets nothing. A height carrying **no**
  rule is read as a minimum as well, which is a deliberate departure from the
  specification's stated default of `auto`: no producer means `auto` by omission,
  Word renders a bare height as a floor, and the page-layout tables that CV and
  letterhead templates are built from state a tall empty row precisely to place
  the block beneath it. Read as `auto` those rows collapse and the layout falls
  in on itself.
- A page break *inside* a paragraph is not carried. Word honours a
  `w:br w:type="page"` with text after it in the same paragraph by splitting the
  paragraph across the boundary; a paragraph here holds one flag saying where it
  starts, and nothing in the model says "the first half of me is on the page
  before". Such a break is demoted to the line break every other `w:br` becomes,
  with `docx.pagebreak.split`, rather than carried to the next paragraph — which
  would pull the text that followed the break onto the page before the one the
  document put it on. Two breaks with nothing between them are two boundaries
  and a blank page in the middle: the first is kept and
  `docx.pagebreak.repeated` reports the rest, because honouring the later one
  would move the paragraph a page further on than the document did. A break in
  the last paragraph of a part has no paragraph to start and is dropped with
  `docx.pagebreak.trailing`; a document that deliberately ends on a blank page
  loses that page.
- A page break at the end of a table cell is dropped, with
  `docx.pagebreak.table`. Every cell's paragraphs live in the document's one
  flat list, so the paragraph after a cell's last one is the neighbouring cell's
  or the body's past the table, and neither is the row Word splits there. A
  break on a paragraph *within* a cell is read and written back as stated, but
  the layout breaks a row only where the page runs out, so nothing acts on it.
- A row's `w:cantSplit` is `TableRow.CanSplit`, read as the on/off property it
  is and written back. A row without it may break across a page, as in Word,
  including one whose cell holds a nested table: that breaks between the
  nested table's rows, each of which goes to one page whole.
- A row's `w:tblHeader` is `TableRow.IsHeader`, read as an on/off property and
  written back; it used to be written and never read. The header rows a table
  opens with are drawn again at the top of every page or column the table
  continues onto, unless they are as tall as the page; a table of nothing but
  header rows repeats none. A table nested in a cell, and the PDF writer, do
  not repeat them.
- A floating table, `w:tblpPr` (ECMA-376 §17.4.57), is `DocumentTable.Position`:
  its anchors across and down, its offsets or the `tblpXSpec`/`tblpYSpec`
  alignment that replaces them, its distances from the text, and
  `w:tblOverlap`. An anchor left out is read as LibreOffice's importer reads it
  (`TablePositionHandler`): from the margin down, from the text across. The
  layout stands the table there at its own width, out of the flow; text runs
  down the side of the column with room for it, or below the table where
  neither side has an inch, and an inline table that would run into it moves
  below it, as LibreOffice master does (`testInlineTableShiftDown`; 24.2 drew
  the two over each other). A floating table taller than the room left
  continues at the top of the next page. Written back with both anchors
  stated. A floating table nested in a cell stays in its cell, with
  `docx.table.floating`, and the PDF writer lays a floating table out in the
  flow (`pdf.write.feature-unsupported`).
- A page break that opens its paragraph - nothing of the paragraph before it -
  starts that paragraph on the new page. Nothing is split, so nothing is
  demoted: the paragraph is what the page begins with. If the paragraph already
  starts a page, from a break held over from the one before or from
  `w:pageBreakBefore`, the boundary is kept once and `docx.pagebreak.repeated`
  reports the blank page the second would make.
- A column break, `w:br w:type="column"`, is a page break in a section of one
  column. ECMA-376's `ST_BrType` says the text restarts on the next page when the
  section is not divided into columns, and Word and LibreOffice both start one;
  it is read exactly as a `w:type="page"` break is, including the cases above.
  In a section of several columns it moves to the next column, and is
  `ParagraphStyle.ColumnBreakBefore` on the paragraph it starts, travelling
  there exactly as a page break does; one with text on both sides of it in its
  paragraph is a line break and reported as `docx.break.column`, as a page
  break there is. Which section a paragraph is in is found before the content
  is read, because a section's properties follow its paragraphs. In a header,
  a footer or a text box it stays a line break. A write puts a
  `w:br w:type="column"` run first in the paragraph that has the break.
- Text columns. `w:cols` gives the page a column count and the gap between
  columns, `w:space`, or half an inch when that is absent. A section that lists
  its columns (`w:equalWidth="0"`) is read as their count and the mean of their
  spaces; widths that differ by more than a point are held as equal columns and
  reported as `docx.columns.unequal`. Where every section has the same
  columns, the page holds them and that is all. Where sections differ, each
  is a `DocumentSection` over its own paragraphs with its own columns, and
  `docx.section.columns` says so; the page then records the columns of the
  section holding the most paragraphs rather than the last's, because a
  two-column document whose producer ended it with a continuous break before
  one empty paragraph has a one-column last section holding nothing. Columns
  that leave no room keep the page and lose the columns
  (`docx.columns.geometry`). The separator line (`w:sep`) is not read.
- A section break starts a page unless it says it does not. `w:type` on the
  `w:sectPr` of the section that follows says how that section starts:
  `nextPage`, which is also what an absent `w:type` means, `oddPage` and
  `evenPage` put its first paragraph on a new page, as `PageBreakBefore`, and
  `continuous` runs it on below the section before. A section's own break and a
  page break the paragraph before it ended with are one boundary, not two.
  `oddPage` and `evenPage` can put a blank page in front of the section to
  land it on the side they name; that page is not made, and
  `docx.section.parity` says so. `nextColumn` is read as `continuous`.
  An empty body paragraph holding nothing but the `w:sectPr` is the section
  mark and not a paragraph, as in LibreOffice's import: the section ends at the
  paragraph before it. It stays where it is all its section holds, where no
  paragraph follows it, and right after a table, below which it closes the
  section. `tdf170602_exportContext.docx` puts its next-page mark in an empty
  Heading 1 after a page break, which drew as a page of its own.
- A section's own paper and margins. The last section's `w:pgSz` and
  `w:pgMar` are the document's page; a section that states others keeps them
  as `DocumentSection.Page`. A section that starts a page is laid out between
  its own margins there and on the pages its text runs onto. That is what
  makes a magazine whose sections are set two inches in, or six, lay out in
  columns of the width it was set in rather than the last section's. A section
  a `continuous` break opens keeps the margins of the page it is on, and its
  columns are spread across that page's text width, as LibreOffice lays it out
  (a text section inside the page style before it, with columns and no
  margins of its own); Word would set it between its own. In
  `tdf90697_complexBreaksHeaders.docx` a narrow two-column section that a
  page began in set that page's header in its own width.
- A section's own headers and footers. Each section's references are read,
  each header and footer type a section does not name continuing from the
  nearest section before it that does, as ECMA-376 §17.10.5 says, and each
  section's `w:titlePg` (§17.10.6) is its own and not continued. The last
  section's set is the document's `RunningContent`; a section whose set differs
  keeps it as `DocumentSection.RunningContent`. A page carries the set of the
  section that started it, the first-page part on that section's first page,
  and a section that follows on the same page leaves the pages it runs onto as
  they were, as LibreOffice keeps the page style there - unless it has a set of
  its own and a hard page break inside it starts a page, which then carries
  its set with no first page of its own, as LibreOffice puts the section's page
  style on that break. A contract that names
  its header and footer in the first of nine sections carries them on every
  page; `tdf167527_title_letters_cut_from_below.docx`, whose title page is its
  first section's first-page header, reaches it. A part several sections name
  is read once. A write names every section's parts in its `w:sectPr`, and
  where sections carry different sets, names an empty part for a type one set
  lacks and another has, so that it does not continue from the section before.
- A write states `w:cols` with the count and the gap. A document with sections
  writes each boundary as a `w:sectPr` in the `w:pPr` of the section's last
  paragraph, with that section's page, or the document's, its columns and
  `w:type="continuous"`; a section
  that starts a page says so with its first paragraph's `w:pageBreakBefore`,
  which is where the model keeps it, so the `w:sectPr` does not say it twice.
  A section ending in a table gets an empty paragraph after the table to hold
  its `w:sectPr`, as Word writes it. The body's own `w:sectPr` closes the last
  section, `continuous` too.
- Block nesting deeper than `DocumentLimits.MaxGroupDepth` is abandoned with a
  `docx.limit.depth` diagnostic.
- DOCX packages above `DocumentLimits.MaxDocumentBytes` are not parsed.
- XML parts above `DocumentLimits.MaxBinBytes` are skipped.
- Color alpha is not represented by DOCX; RGB channels are written with a
  diagnostic.

## Read Diagnostics

Every read ends with a `docx.read.summary` info diagnostic carrying the
paragraph, table, style, image, and skipped-block counts. It exists so
a document that opens blank can be told apart from a document that *is* blank:

| Code | Severity | Meaning |
| --- | --- | --- |
| `docx.read.summary` | Info | Paragraph, table, style, image, and skipped-block counts for the read. |
| `document.metadata.emitted` | Info | Document properties the caller supplied reached `docProps`; the message names the fields. |
| `document.metadata.dropped` | Warning | A property OOXML states no equivalent for was not written. `CreatorApplication` is the one: OOXML names a producing application only. |
| `docx.document.empty` | Warning | The body held block-level content but produced no paragraphs — a reader gap, not an empty file. |
| `docx.table.style` | Warning | A table named a table style; its paragraph and run properties were applied, banding, conditional formatting and the borders it states were not. |
| `docx.table.rowheight` | Warning | A row stated an exact height; it was applied as a minimum so its text is not clipped. |
| `docx.table.floating` | Warning | A table nested in a cell stated a floating position; it was laid out in its cell. |
| `docx.block.unsupported` | Warning | A block-level element was not understood; the message names the element. Reported once per distinct name. |
| `docx.pagebreak.split` | Warning | A `w:br w:type="page"` had text after it in the same paragraph; it was read as a line break, because pages break between paragraphs here and not inside one. |
| `docx.pagebreak.repeated` | Warning | A paragraph stated more than one page break; the first was kept and the blank pages the others make were dropped. |
| `docx.pagebreak.trailing` | Warning | A page break in the last paragraph of a part was dropped; there is no following paragraph for it to start. |
| `docx.pagebreak.table` | Warning | A page break at the end of a table cell was dropped; a row breaks only where the page runs out, not where a paragraph in it asks. |
| `docx.break.column` | Warning | A column break with text before and after it in the same paragraph was read as a line break; columns break between paragraphs here, not inside one. |
| `docx.section.columns` | Info | The sections differ in their columns; each section keeps its own, and the page records those of the section holding the most paragraphs. |
| `docx.header.first` | Info | A section has a `first` header or footer, its own or continued, but does not set `w:titlePg`, so it was not read for that section; its first page carries the default one. |
| `docx.header.even` | Info | An `even` header or footer was not read, because the document does not set `w:evenAndOddHeaders`; even pages carry the default one. |
| `docx.section.parity` | Warning | A section starting on an odd or even page was read as starting on the next page; the blank page it can put before itself is not made. |
| `docx.columns.unequal` | Info | A section listed columns of unequal width; they were read as equal columns. |
| `docx.numbering.format` | Info | A list level was numbered in a format other than decimal, letters or roman numerals (ordinals, number words, a script's own digits); it was numbered in decimal. Once per read. |
| `docx.list.restart` | Warning | Write: a list item that restarts its list's count was written without the restart. Once per write. |
| `docx.columns.geometry` | Warning | A section's columns left no room to write in a column; the page was kept and read as one column. |
| `docx.limit.depth` | Warning | Block nesting hit `MaxGroupDepth`; the deepest content was skipped. |
| `docx.styles.missing` | Warning | Content named styles but the package has no styles part. Reported once. |
| `docx.styles.unknown` | Warning | A `w:pStyle`/`w:rStyle` named a style the table does not define. Once per id. |
| `docx.styles.cycle` | Warning | A `w:basedOn` chain was cyclic and was cut short. |
| `docx.styles.depth` | Warning | A `w:basedOn` chain exceeded `MaxGroupDepth` and was cut short. |
| `docx.part.headerfooter` | Info | The package has headers or footers, which are not part of the body. |
| `docx.properties.xml` | Error | `docProps/core.xml` or `docProps/app.xml` could not be parsed, or exceeded `MaxBinBytes` (`.limit` suffix). The document itself still reads. |
| `docx.properties.date` | Warning | A `dcterms:created` or `dcterms:modified` stated a timestamp this build could not read, so it was dropped rather than guessed at. |
| `docx.image.missing` | Warning | A picture referenced a media part the package does not contain. |
| `docx.image.relationship` | Warning | A picture named a relationship the package does not define. |
| `docx.image.external` | Warning | A picture linked to an image outside the package, which is not fetched. |
| `docx.image.format` | Warning | A picture used an image format this codec does not carry (EMF/WMF and the like). |
| `docx.image.limit` | Warning | An image part exceeded `MaxBinBytes` and was skipped. |
| `docx.image.shape` | Warning | A drawing held no embedded picture. |
| `docx.image.anchored` | Warning | A floating picture was anchored to its paragraph. |
| `docx.anchor.relativefrom` | Warning | A frame an anchor stated its offset against was approximated: a mirrored margin read as an odd page's horizontally, or kept as stated and measured from the paragraph vertically. |
| `docx.background.fill` | Warning | The page background's VML fill was one the model cannot hold, and was read as one colour: a rectangular (`gradientRadial`) gradient as its outer colour, a pattern of two colours or a picture fill as `w:background`'s own colour. |
| `docx.shape.group` | Warning | A drawing group held a picture, or groups nested past sixteen deep, which were not read; its drawn boxes were. |
| `docx.shape.fill` | Warning | A VML shape's fill was read as one colour, for the same reasons, or a DrawingML shape's pattern fill (`a:pattFill`) as its background colour. |

Write diagnostics are `docx.color.alpha`, `docx.image.omitted`, `docx.image.placeholder`, `docx.image.size`, `docx.link`, `docx.shape.page`, and `docx.text.control`.
`docx.shape.page` says a shape anchored to a page by number was written beside
the paragraph it was read with, its vertical offset measured from the page
(`relativeFrom="page"`): WordprocessingML anchors every drawing to a paragraph
and has no way to name the page.
`docx.text.control` is the counterpart to ODT's `odt.text.control`: XML 1.0 has
no representation for most control characters, not even an escape, so one that
reaches the writer is dropped and reported. It is not a contrived input — the
HTML reader decodes `&#7;` into the model and the RTF reader passes `\u7`
through. Every string that comes from the model passes the filter, a picture's
description as well as run text.

`broilerdoc info <file>` prints all of them, which is the
quickest way to see what a problem document lost. In the Writer, set
`BROILER_WRITER_DOCUMENT_LOG=1` to have the same list written to stderr on every
open; the status bar always reports a read that produced no text.

## Probe Policy

DOCX probing is conservative because DOCX is a ZIP-based OPC package:

- ZIP signature plus DOCX filename/MIME hint is high confidence.
- A visible `word/document.xml` local ZIP entry is high confidence.
- Generic ZIP files are not claimed without a DOCX hint or WordprocessingML
  package evidence.

## Standards and rights

DOCX is ECMA-376, republished as ISO/IEC 29500. Its rights position (the Microsoft
Open Specification Promise and its three self-stated limits), the approved labels
and the provenance findings are in [the DOCX register](../ip/docx.md). Decided is
not cleared: no lawyer reviewed it, and patent-freedom is not claimed. No format
label may name a vendor or its product, so `.docx` is offered as
`DOCX Document (*.docx)`.
