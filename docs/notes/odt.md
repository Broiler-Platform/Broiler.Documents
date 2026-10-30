# ODT Conformance

`Broiler.Documents.Odt` reads and writes a dependency-free ODT subset over OASIS
OpenDocument text packages (ODF 1.0 through 1.3, published as ISO/IEC 26300). It
uses nothing but `System.IO.Compression` and `System.Xml.Linq`; there is no ODF
toolkit behind it.

## Supported Read/Write Subset

### Package and XML

- Package layout: the `mimetype` entry, `META-INF/manifest.xml`, `content.xml`,
  and `styles.xml`. Part paths are the normative ones, so nothing is resolved
  through relationships the way an OPC package does.
- The flat single-file form (`.fodt`), read only: one XML file whose
  `office:document` root holds `office:meta`, `office:styles`,
  `office:automatic-styles`, `office:master-styles` and `office:body` side by
  side; the rest of the read is the same. A flat file has no package, so a
  picture in it is inline `office:binary-data` or a link that is not followed
  (`odt.image.external`). Its root's `office:mimetype`, when present, must be
  the text or text template media type (`odt.flat.media-type`). Writing always
  produces a package, so the descriptor claims `.odt` only and `-o out.fodt` is
  not accepted.
- A prefix a part or flat file uses without declaring it. That is not
  well-formed XML, but LibreOffice reads it and some of its test documents rely
  on it (`tdf142130.fodt` declares neither `fo:` nor `style:`). When the first
  parse fails, the reader parses again with the prefixes LibreOffice declares on
  every document it writes already bound, and reports `odt.xml.prefix`. A
  declaration the file does make still wins; a prefix outside that set still
  fails the read with `odt.xml`.
- A written package is deterministic: two writes of one document are
  byte-identical. Every entry carries a fixed timestamp, `mimetype` is first and
  stored uncompressed per ODF 1.3 part 2 §3.3, and `meta.xml` names the
  generator and nothing else (a creation date would make two writes differ).

### Paragraphs, text and inline content

- Paragraphs (`text:p`) and headings (`text:h`). A heading is an ordinary
  paragraph carrying whatever its named style resolves to; the model has no
  heading rank. ODF has no paragraph mark of its own, so
  `RichTextParagraph.MarkStyle` is the paragraph style's text properties (what
  an empty paragraph is as tall as). It is not written back: an empty paragraph
  written here takes its height from the document's defaults.
- Block-level containers, walked recursively so their paragraphs are read in
  document order (row-major for a table): lists (`text:list`, nested lists,
  `text:list-header`), tables (`table:table` with its header-row and row-group
  wrappers), sections (`text:section`), the `text:index-body` of a generated
  index or table of contents, and the body of a `draw:text-box` frame.
- White space per ODF 1.3 part 3 §3.17: a run of white space in a text node is
  one space, white space at either edge of a paragraph is nothing, and
  significant spaces arrive as `text:s`. The writer reverses the rule, deciding
  per paragraph rather than per run which spaces need protecting, so a leading,
  trailing, or repeated space is written as `text:s` and survives a round trip
  even when a style boundary splits the run.

  The surviving space carries the character style of the text node it was
  **written in**, not of whatever follows it: in
  `<text:span>a</text:span> <text:span>b</text:span>` the space is the
  paragraph's and only the two letters are the spans'. A white-space run that
  straddles a span boundary takes the style it ends in; ODF settles what such a
  run collapses to, not what it wears, so that is this codec's choice,
  pinned by
  `OdtReaderStyleTests.A_White_Space_Run_That_Straddles_A_Span_Boundary_Takes_The_Style_It_Ends_In`.
- Inline constructs: `text:span`, `text:a`, `text:s`, `text:tab`,
  `text:line-break`, and `text:ruby` (the `text:ruby-base` text). Fields and
  marks (`text:date`, `text:bookmark-ref`, and the rest) contribute the value
  they last computed.
- `text:page-number` and `text:page-count` keep their last computed value too,
  in a run marked with the field it shows (`InlineStyle.PageField`): what it
  counts, its `style:num-format` (`1`, `i`, `I`, `a`, `A`, or empty for none),
  and the adjustment `text:select-page` (`previous`, `next`) and
  `text:page-adjust` make. A header or footer draws each page's own value; body
  text keeps the saved one. A field with no `style:num-format` is decimal, where
  LibreOffice would take the page style's format. A write puts the field back
  with its value.
- Hyperlinks: absolute `http`, `https` and `mailto`, plus a non-empty
  `#fragment` whose name carries no whitespace, quote or second `#`. Every other
  scheme, every relative target and a bare `#` are refused on read and write,
  the link written as plain text (`odt.link`). One predicate decides it for all
  five codecs (`DocumentLinkTarget`). A fragment preserves the reference, not a
  working jump: no codec here reads or writes a bookmark, so the name it points
  at is not carried.
- Character formatting: `fo:font-weight` (including the numeric CSS form),
  `fo:font-style`, `style:text-underline-style`/`-type`,
  `style:text-line-through-style`/`-type`, `style:font-name` resolved through
  `office:font-face-decls`, `fo:font-family` (CSS syntax, quoted names and
  fallback lists), `fo:font-size` (absolute lengths and percentages of the
  inherited size), `fo:color`, and `fo:background-color`.
- Capitalization: `fo:text-transform="uppercase"` and
  `fo:font-variant="small-caps"`, read and written as a style attribute. The
  text keeps the casing the author typed and capitals are applied when drawing,
  so an open-and-save does not rewrite it. A style declaring both draws all
  capitals. `fo:text-transform` values `lowercase` and `capitalize` are dropped
  (`odt.text.transform`); the model draws upper case only.
- Hidden text (`text:display="none"`) is retained and written back; normal
  rendering omits it. OpenOffice.org 2.x wrote `text:display` the wrong way
  round (`true` for hidden, `none` for shown); LibreOffice recognises such a
  document by the build id in `meta:generator` (`…_project/680m14$Build-9134`)
  and reads its values inverted (`txtimppr.cxx`, `CTF_TEXT_DISPLAY`), and so
  does this reader: `fdo114306_2.odt`'s "Скрытый текст" (hidden text) style
  says `true`. A paragraph whose mark and text are all hidden takes no room in
  the layout, as in LibreOffice; one whose text is hidden under a shown mark is
  still an empty line.

### Styles and paragraph formatting

- Styles from `styles.xml` and `content.xml`, resolved per ODF 1.3 §16.2: the
  family's `style:default-style`, then the `style:parent-style-name` chain from
  its root down to the style the content names, then the next more specific
  style. Automatic and named styles resolve through the same chain, and a
  content automatic style wins a name collision with a named style. Only the
  attributes `RichTextDocument` models are resolved; character spacing and
  scaling, text position, borders, tab stops, drop caps, table and page styles,
  and list label geometry are ignored.
- Paragraph formatting: `fo:text-align` (`start`/`left`/`center`/`end`/`right`;
  `style:writing-mode` is not consulted, so `start`/`end` resolve as
  left-to-right, and `justify` is read as left-aligned with
  `odt.align.justify`, the model having no justified alignment),
  `fo:margin-top`, `fo:margin-bottom`, `fo:margin-left`, the single-value
  `fo:margin` shorthand, and `fo:background-color` as
  `ParagraphStyle.Background`, read and written, with `transparent` taking away
  an inherited one. Lengths are read in `pt`, `in`, `cm`, `mm`, `pc`, and `px`.
  Exact left/right margins and first-line or hanging offsets are retained for
  non-list paragraphs. Indentation is one level per quarter inch, matching the
  DOCX codec; a list paragraph takes its indent from the list nesting rather
  than `fo:margin-left`, so one written at indent level 0 reads back at level 1.
- Line height in all three ODF forms: `fo:line-height` as a percentage is a
  multiple (`ParagraphStyle.LineSpacing`), as a length an exact height, and
  `style:line-height-at-least` the least a line is
  (`ParagraphStyle.LineHeightRule` and `LineHeight`). Each replaces whatever
  form a parent stated, and a write states the same form back. Leading between
  lines (`style:line-spacing`) is a fourth kind the model does not hold
  (`odt.linespacing.leading`).

### Page and column breaks

- `fo:break-before` on `style:paragraph-properties` is
  `ParagraphStyle.PageBreakBefore`, resolved through the inheritance chain, the
  most specific declaration winning. `page` is a break; `auto` is not and
  cancels an inherited one; `column` moves to the next column of the same page
  (`ParagraphStyle.ColumnBreakBefore`, a page break only where the page has one
  column). It is read wherever stated, inside a table cell included; what a
  break mid-grid does is the renderer's decision. A write states `page` or
  `column` and nothing otherwise: an automatic style here inherits from
  `Standard`, which states none, and writing `auto` would give every paragraph
  an automatic style of its own.
- `fo:break-after="page"` (or `"column"`) is **read onto the paragraph that
  follows**: a break after paragraph five and one before paragraph six are one
  break in one gap, and the model holds the near end. Nothing is written back as
  `fo:break-after`. Where both ends of a gap speak, either asking for a page
  gets one: a `break-after` above beats a `break-before` of `auto` below.
- The carry goes one paragraph and stays inside one flow: the body, each
  header, each footer, and each shape with its own text read their own (a text
  box read as body content is in the body's flow). Three positions drop the
  break with an `odt.break.after` warning: after the last paragraph of a flow;
  immediately before a table (the break belongs to the table, and the first
  cell would break inside the grid); and at the end of a table cell (the next
  paragraph is across the grid).
- A break *implied* by a non-empty `style:master-page-name` is not read as one:
  every document names a master page on its first block, and the first page is
  not a break.

### Lists and outline numbering

- The kind comes from the `text:list-style` the list names:
  `text:list-level-style-number` with a `style:num-format` is numbered,
  `text:list-level-style-bullet` and `text:list-level-style-image` are
  bulleted; the level is the nesting depth. Each paragraph names the list it is
  numbered in (`ParagraphStyle.List`): the list style's levels, read with
  `style:num-format` (`1`, `a`, `A`, `i`, `I`; an empty one draws no number),
  `style:num-prefix`, `style:num-suffix`, `text:display-levels`,
  `text:start-value` and `text:bullet-char` (ODF 1.3 §16.30-16.32).
- Each top-level `text:list` counts afresh unless
  `text:continue-numbering="true"` carries on the last list in its style or
  `text:continue-list` names the `xml:id` of the one it carries on; a sub-list
  counts in the list it is nested in; a `text:list-item`'s `text:start-value`
  restarts the count there. A `text:list-header` paragraph is in no list.
- Outline numbering: a `text:h` outside any list is numbered by the document's
  `text:outline-style` at its `text:outline-level`, unless it is a list header;
  `text:restart-numbering` restarts the count. A level above a heading's that
  has had no heading yet counts as its start value, as LibreOffice numbers it:
  `tdf142694-1.odt` opens at level two with "1.1", and its next level-one
  heading is "2".
- A write groups each run of paragraphs in the same list into one `text:list`,
  nesting deeper levels inside the `text:list-item` they belong to, with a list
  style (`LD1`, `LD2` ...) per list and `text:continue-numbering` on a later
  run of the same list. List paragraphs that name no list get a plain list style
  (`L1` bullets, `L2` numbers) that draws them as the layout does. Headings are
  written as paragraphs, so the outline style is not written; their numbers go
  out as a list. List positioning uses the level's default layout. What ODF
  cannot state is reported (see [Write Diagnostics](#write-diagnostics)).

### Tables

- A `DocumentTable` over the paragraphs of its cells: column widths from each
  `table:table-column`'s style, `table:number-columns-spanned` and
  `table:number-rows-spanned`, the background and borders a cell's
  `style:table-cell-properties` state, header rows (`table:table-header-rows`,
  repeated on every page the table continues onto, as LibreOffice draws them),
  row heights (`style:table-row-properties`), and nested tables. A
  `table:covered-table-cell` is read as the grid position a merge covers and
  nothing more. A cell holds no text of its own but names a range of the
  document's paragraphs, so carets, selections, styles and every codec's text
  handling work through one flat list. Written back as `table:table`, with a
  covered cell for every position a span swallowed.
- Row height: `style:min-row-height` is a floor, `style:row-height` is fixed,
  and if both are present the last attribute wins, as LibreOffice imports it.
  Fixed rows keep their text in the model and clip its rendered overflow; the
  writer preserves the distinction. `style:use-optimal-row-height="true"` asks
  for the height the content needs.
- A row's `fo:keep-together="always"` keeps it on one page (`TableRow.CanSplit`
  false); `auto`, or nothing, lets it break across a page, as LibreOffice does.
  A write states `always` in the row's style and nothing otherwise.
- A table's `fo:margin-top` and `fo:margin-bottom` are
  `DocumentTable.SpacingBefore` and `SpacingAfter`, read and written; as above a
  paragraph, the space above opens no page or column.
  `style:may-break-between-rows="false"` (LibreOffice's "allow table to split"
  turned off) keeps the table together, as `fo:keep-together="always"` does; a
  nested table kept with what follows it is held to it at a cut.
- A cell whose style states no padding has none: ODF has no default, and
  LibreOffice sets such text against the edge. Text running past a cell's edge
  is cut there, as Writer cuts a text portion (`SwTextPainter::DrawTextLine`); a
  line holding a picture is drawn whole.
- A plain, single-paragraph numeric cell with a General number style (inherited
  cell number styles resolved) displays its finite `office:value` in the
  application's current culture; the CLI enables system locale support, while
  command-line, JSON and protocol numbers stay invariant. Rich content,
  explicit decimal or language-specific formats, string cells and invalid
  values keep their cached text. This is display only: no formula evaluation,
  and numeric-cell metadata is not written.
- Tables inside text boxes keep their grid and local paragraph indices, round
  trip as frame text-box content, and render through the shared and PDF table
  layouts.

### Notes, tracked changes and settings

- Footnotes and endnotes (`text:note`), as a `DocumentNote` held by one citation
  character where the note stands. `text:note-class` gives the kind and
  `text:note-body`'s paragraphs the text; a table or shape in one is read as its
  text (`odt.note.content`), a note inside a note (not allowed by ODF) as
  nothing (`odt.note.nested`). `text:note-citation`'s number is not read: notes
  are numbered in citation order, as LibreOffice does, and a typed `text:label`
  is kept as the note's own mark.
  `text:notes-configuration` in `office:styles` gives each kind's
  `style:num-format` and `text:start-value` (LibreOffice counts from zero), and
  whether its `text:citation-style-name` raises the label at the head of the
  note. All of it is written back the same way.
- Tracked changes (ODF 1.3 §5.5), as a `TextRevision` on the runs and paragraph
  marks they mark. `text:tracked-changes` lists the regions, each with its
  `dc:creator` and `dc:date`; the text between a `text:change-start` and its
  `text:change-end` is the insertion or format change it records, a paragraph
  break inside an insertion inserted with it. A deletion's text is put back
  where its `text:change` stands, as LibreOffice does: its first paragraph joins
  the text before the mark, its last the text after it, those between keep their
  styles, and every paragraph break inside comes back deleted. A table or list
  in a deletion is read as block content there, and a page break handed on by
  the paragraph before goes to the text after the mark.
- The writer lists a region per change and marks the body the same way, a
  deleted paragraph break joining its two paragraphs as ODF holds them. ODF has
  no move, so each side is written as its insertion or deletion. Header and
  footer changes are written accepted: those parts go into `styles.xml`, where
  no body region can be named.
- `settings.xml` (or a flat file's `office:settings`): `ShowRedlineChanges` says
  whether the document shows its changes (`RichTextDocument.ShowsRevisions`),
  and the writer writes `settings.xml` when they are hidden.
  `JustifyLinesWithShrinking`, kept for documents from Word 2013 or later, is
  `DocumentStyleDefaults.ShrinksJustifiedLines` (justified lines may shrink
  their spaces, as the DOCX note describes). `TabOverMargin`, kept for documents
  from Word 2010 or earlier, is `DocumentStyleDefaults.TabsOverMargin` (a tab
  stop past the right indent keeps its place and the line runs on past it).
  Both are read and written.

### Pictures

- Embedded pictures, read and written as one object replacement character
  (`U+FFFC`) whose run carries the image. A `draw:frame` holding a `draw:image`
  is read whether the picture is a package entry named by `xlink:href` or inline
  `office:binary-data`, and whether or not a `draw:a` wraps it. Display size is
  `svg:width`/`svg:height`, alternative text `svg:title` (falling back to
  `svg:desc`). Raster formats only: PNG, JPEG, GIF, BMP, TIFF, WebP, and ICO;
  SVG, EMF and WMF (what producers store for charts, formulas and object
  replacement images) cannot be decoded to pixels here and are reported as
  `odt.image.format` rather than kept as a picture that draws nothing. A write
  stores each distinct image once under `Pictures`, declares it in the manifest,
  and anchors the frame `as-char`.
- A frame's alternative `draw:image` representations, including those inside
  `draw:a` and flat binary-data ones, are tried in source order until the loader
  can carry one (so an embedded PNG can stand in for an unreadable SVM); a
  supported first representation keeps priority, and skipped ones keep their
  diagnostics. Resource policy denial stops extraction rather than trying
  another representation.
- Floating pictures: `text:anchor-type="char"` floats at a character anchor and
  `as-char` is inline; paragraph and page anchors float too, and a missing
  anchor-type keeps the reader's legacy inline fallback. A floating picture is
  read as a shape carrying the image, boxed by `svg:x`/`svg:y` against the text
  column and its paragraph and by `svg:width`/`svg:height`, keeping its graphic
  style's fill, outline and `style:run-through`; a frame standing between
  paragraphs is read this way too. It is written back as a paragraph-anchored
  `draw:frame` at the same box with its run-through stated, so a stripe under
  the text and a stamp over it stay on their side. Crops, rotation, and frame
  effects are dropped.

### Shapes, frames and fills

- Drawings keep unfilled outlines, `draw:ellipse`, and `draw:line` endpoints in
  either direction. Stroke width is `svg:stroke-width`; a solid stroke with no
  colour is black. A frame's `fo:border` is a uniform outline, explicit `none`
  included, for text and floating-image frames. Basic paths and widths survive
  ODT export. Asymmetric borders, dash patterns, arrow heads, rotated drawings
  and arbitrary enhanced paths are approximations; unrepresented nested
  drawings take the existing fallback path.
- A graphic style inherits through `style:parent-style-name`, the nearest
  statement winning, so a frame whose automatic style says only where it stands
  takes paint and wrapping from the named style beneath it, as LibreOffice
  writes text boxes. Document default graphic properties apply below named and
  inherited ones, an explicit default `run-through` included.
- Wrapping: an omitted wrap is top-and-bottom. `style:wrap`'s two names that
  read backwards are read as ODF means them: `run-through` is text through the
  shape, `none` is no text beside it. A wrapped line keeps one span down
  whichever side has more room, so `parallel` gets the larger side.
- `style:run-through` is the shape against the text and `draw:z-index` the shape
  against other shapes; both are read and written, and a drawing with no index
  reads as zero, keeping read order. A style that states no run-through is read
  as `background` (not ODF's default; a box wrongly in front would hide the
  text).
- Text frames keep chain names and empty link destinations; empty positioned
  ones stay frames rather than adding body paragraphs. A final
  paragraph-anchored frame keeps a body anchor; page anchors need none. The
  raster layout flows styled paragraphs between linked body frames, paragraph
  splits and list counters included; cycles are bounded, and tables within
  linked frames and links inside running content are not routed (PDF export
  reports `pdf.write.feature-unsupported`). Writing linked frames reports
  `odt.shape.linked`: an authored LibreOffice PDF export clipped text in the
  source frame while placing its overflow in the destination, so only model
  round trips and raster layout are covered, not identical exported clipping.
- Text boxes: a frame with no `svg:width` takes its text box's `fo:min-width`,
  and minimum-width frames grow to their text. A text box's `fo:min-height`
  makes the frame grow to its text from there (`DocumentShape.GrowsToText`); a
  frame stating no height either grows from nothing, the layout measuring its
  lines. A write puts such a box in a frame with no `svg:height` around a text
  box stating its minimum, as LibreOffice does. A frame that states no box
  stays in the text. Top/middle/bottom text alignment, `fo:padding` and side
  overrides round trip, the padding shaping the text area; paragraph spacing
  and lists are composed in the box's own flow. Fixed-height frames clip
  overflow; growing frames measure their table content. Explicit text overflow
  cannot be written (`odt.shape.overflow`).
- A frame anchored to a page by number (`text:anchor-type="page"` with
  `text:anchor-page-number`) is `DocumentShape.AnchorPage`: it stands on that
  page, and the layout makes the pages needed, as LibreOffice does
  (`tdf146955.odt`, a book whose three hundred boxes stand on the pages they
  name). Its position comes from the area the graphic style names
  (`style:horizontal-rel`/`style:vertical-rel` of `page` or `page-content`,
  `page` when unstated) and from `style:horizontal-pos` and
  `style:vertical-pos`, turned into the model's terms: across from the text
  column, down from the page's top edge. A page anchor with no number is the
  paragraph anchor it amounts to. A write puts a page-anchored drawing in the
  body ahead of the text, as LibreOffice does, with its number and a style
  stating both areas.
- `style:print-content="false"` on a drawing's graphic style is
  `DocumentShape.Printable` false, read and written. The layout places the
  shape and keeps text clear of it, but leaves it off the page unless the host
  sets `LayoutSettings.DrawNonPrintingShapes`, which is how LibreOffice prints
  and exports to PDF: `fdo114306_2.odt` prints without its crest and heading
  frame, its title as far down the page as if they were there.
- A `draw:control` placing a label (`form:fixed-text` in `office:forms`) is a
  shape holding the label's words in the control's `draw:text-style-name`,
  centred as `draw:textarea-vertical-align` says and drawn whole even where the
  control is shorter than its line, with no fill or border. Without
  `form:multi-line="true"` it stays on one line, its box growing to its text;
  `form:printable="false"` makes it non-printable. Other controls are not read.
- Fills (pages and shapes alike): embedded bitmap patterns with
  tiled/stretch/single placement, repeat mode and simple reference-point
  alignment; single/double/triple hatches with line directions, angle and
  spacing; gradients with centred geometry, centre, border and endpoint
  intensity, sampled with bounds; uniform colour opacity; page-margin bounds.
  An `axial` gradient puts its start colour along the middle and its end colour
  at the edges, as LibreOffice draws it. A gradient's `draw:angle` is read with
  or without a unit (ODF 1.2 typed it as tenths of a degree, ODF 1.3 as an angle
  with `deg`, `grad` or `rad`, and LibreOffice has written both) and turned a
  quarter, because ODF measures it from down the page and the model from along
  it. Percentage bitmap dimensions, repeat offsets and staggered tiling are not
  applied. Unresolved definitions report `odt.fill`, unknown gradient styles
  `odt.gradient`. `odt.fill.opacity` reports bitmap opacity the painter cannot
  apply on read. Standalone PDF export reports `pdf.write.feature-unsupported`
  for bitmap fills; the raster painter supports them.
- A bitmap fill whose picture does not load (none named, a missing definition or
  package entry, a link outside the package) is painted in its
  `draw:fill-color`, else the default graphic style's, else `#729FCF`, the
  colour LibreOffice keeps under every bitmap fill (`sdrattributecreator.cxx`),
  and reports `odt.fill`. Legacy `style:background-image` passes the same
  embedded-image admission gate and is never fetched: one on a page or
  paragraph linking outside the package is painted `#729FCF` over any
  `fo:background-color` beside it, and one naming a missing package entry
  leaves that colour. Issue #178's first sample is a page of it in LibreOffice
  24.2's reference ([office-issue-178.md](office-issue-178.md)).

### Page, master pages and sections

- The page comes from the `style:page-layout` a master page names: size and the
  four margins. The master page is the one named by the `style:master-page-name`
  of the style on the first block, resolved through the inheritance chain (most
  specific wins; an empty one cancels an inherited one, per ODF's rule that
  empty means no page break occurs), falling back to the master page called
  `Standard`, then to the first defined. A master page never says it is the
  first: LibreOffice turning HTML into ODF writes `Standard` with its default
  paper and `HTML` with the source's page, and lays the body out on the second.
  A name further down starts a later page. Text inside a positioned drawing, a
  deletion that `text:tracked-changes` keeps ahead of the body, a note or a
  comment does not count as the first block.
- Body paragraph and table styles can switch master pages, with their paper,
  margins, headers and footers. The shared paginator follows bounded
  master-page chains and their repeating suffix, alternating paper sizes
  included. A master page named inside a table cell does not switch the body
  page. These changes are read and rendered only; writing them reports
  `odt.section.page`.
- Running content follows the master-page chain one link: the master page the
  body begins on supplies the *first* page and the one its
  `style:next-style-name` names the rest (a letterhead's band on page one, the
  page number after). The model holds three selections rather than a sequence
  of pages, so a third master page that differs again is not represented. A
  master page naming itself is not a chain.
- A first page of its own makes a band it does not state *empty* there, not the
  default one (`RunningContent.DifferentFirstPage`). An empty
  `style:header-first` or `style:footer-first` on one master page says the same
  and is read as a band carrying nothing; it is written as the element holding
  one empty paragraph, because LibreOffice reads a wholly empty element as no
  band on *any* page.
- Header and footer come from the same master page as the paper.
  `style:header-style` and `style:footer-style` in
  `style:page-layout-properties` contribute height and spacing
  (`fo:min-height`, `fo:margin-bottom`, `fo:margin-top`) to
  `PageGeometry.MarginBottom`, `MarginTop` and the band distances; the writer
  emits matching styles. Header/footer paragraphs anchoring drawings or shapes
  are kept even when empty, so their frames stay attached to the band.
- Header band: for a master page with an enabled header, the outer top margin
  is `PageGeometry.HeaderDistance`, and the header style's `fo:height` or
  `fo:min-height` is the band reserved below it; their sum is the body
  `MarginTop`. The paginator can move body text clear of a header whose content
  exceeds that minimum. A header style without an enabled header reserves
  nothing. A write reverses the mapping (outer margin is the header distance,
  `style:header-style` keeps the remaining minimum band); independent
  LibreOffice probes verify header/body positions before and after the round
  trip, page-content anchored objects included. Footer bands are not
  converted and header padding is not written.
- A band of fixed height states `svg:height` and no `fo:min-height`, and its
  height already holds the spacing to the body, as LibreOffice reads it: a
  0.4 cm header with 0.3 cm spacing starts the body 0.4 cm below the header's
  top. `PageGeometry.FixedHeaderHeight` and `FixedFooterHeight` say so; the
  paginator then leaves the body where the margin puts it, and a header or
  footer taller than its band is cut at the band's edge rather than left out. A
  fixed band is written back as one that grows.
- Header and footer shapes are read onto the running content and written back
  into the master page. They are placed against the page, so a header's `svg:y`
  is from the top of the page. Their graphic styles are written into
  `styles.xml` as well as `content.xml`, because a style reference resolves only
  within its own part, and an unpainted shape holding no text is not read as a
  shape at all.
- Page columns: `style:columns`' `fo:column-count`, and `fo:column-gap` or, when
  a producer lists the columns, the end indent of the first and the start indent
  of the second. Listed columns whose `style:rel-width` differs by more than a
  percent are held as equal (`odt.columns.unequal`); LibreOffice's 10922 against
  10925 is rounding and not reported. Columns leaving no room keep the page and
  lose the columns (`odt.columns.geometry`). A write states count and gap. The
  separator (`style:column-sep`) is not read.
- `style:page-number` on the paragraph or table style that starts a master page
  restarts the count there (`DocumentSection.PageNumberStart`); `auto` and `0`
  carry it on. A write has one master page, so it cannot keep a restart
  (`odt.section.page-number`).
- Sections (`text:section`) take columns from their section style's
  `style:section-properties`, as a page layout does. One with more than one
  column, custom margins, or `dontBalanceColumns: true` is a `DocumentSection`
  over its paragraphs; any other is only its paragraphs. A columned section
  inside another is laid out in the outer one's columns, so the outer one is
  kept. As in LibreOffice, a section's columns are balanced where the text
  after it continues on the same page, unless
  `text:dont-balance-text-columns="true"` (`DocumentSection.DontBalanceColumns`).
  Section margins (`fo:margin-left`, `fo:margin-right`, `fo:margin`) are
  `DocumentSection.LeftIndent` and `RightIndent`, read and written, measured in
  from the text area of whatever page the section is on, as LibreOffice applies
  them; nested indents add up, and a section's page margins apply only on a
  page it starts. Section background colours are read, painted by the shared
  paginator, and written, nested overrides included; the standalone PDF layout
  reports that it does not paint them.
- A stretch of the body between two section edges follows on the page it is on;
  only a paragraph or table whose style names a master page opens a page
  (`fdo114306_2.odt`, a form of a dozen text sections, keeps its four pages).
- A write puts each columned section in a `text:section` named `Section1`,
  `Section2` and on, with an automatic section style stating its columns. ODF
  lays section columns inside the page's, where the model lets them replace the
  page's, so a document with sections and a multi-column page is written with a
  one-column page and each stretch outside a section goes in a `text:section`
  with the page's columns.
- Unconditionally hidden sections: `text:section text:display="none"` is
  `DocumentSection.Hidden`. Its paragraphs and tables stay in the model and in
  plain-text projections, and ODT writing keeps the flag, cell-local sections
  included. Nested visible sections cannot reveal a hidden parent. The shared
  paginator omits hidden body content, anchored objects and note numbering
  contributions; hidden paragraph breaks and page-style selections do not
  affect visible text, but a break from a preceding visible paragraph still
  reaches the next visible block. Conditional visibility is not evaluated.
  Running parts and text frames carry no section collections: a hidden section
  in a header or footer is left out when read, as LibreOffice never shows it (so
  a template's file-name footer stays hidden) and does not round trip; one in a
  text frame is not covered. Other writers report their visibility limitation
  rather than claiming to keep the flag.
- The page background is `RichTextDocument.PageBackground`, read from the master
  page: the `drawing-page` style its `draw:style-name` names (ODF 1.3 §16.10)
  where that states a `draw:fill`, else the page layout's own `draw:fill`, else
  its `fo:background-color`. LibreOffice writes all three. A drawing-page fill
  states its own `draw:background-size`. A page layout's own fill covers the
  whole page unless the document's `meta:generator` is old: OpenOffice.org, AOO
  and LibreOffice before 6.3 keep it inside the margins, and LibreOffice 6.3/6.4
  keep only a single or stretched picture there (LibreOffice's import rule,
  `PageMasterImportContext.cxx`, with the producer read as `xmlimp.cxx` reads
  it). A write states the drawing-page fill and its bounds, plus a page-layout
  fill and flattened `fo:background-color` fallback for colours and gradients,
  with named gradients and bitmap/hatch definitions in `office:styles`.

## Intentional Limits

- **Encrypted packages are rejected.** ODF password protection really encrypts
  the parts; there is nothing to read, and this codec asks for no key.
- A change of formatting is marked, but what the text looked like before is not
  read (ODF does not require it, and LibreOffice does not write it).
  `text:change` marks in headers and footers are not read.
- Comments (`office:annotation`) are not part of the body and are not imported.
- Table styles beyond a cell's own background and borders are not applied, and
  cell vertical alignment and a table's own alignment and indent are not
  represented; a table starts at the left margin. A cell's paragraphs are a
  range, so an edit spanning out of a cell into the body does not keep the grid
  over what it merged.
- A linked (not embedded) image or other external resource is never fetched;
  that would be a network or file request driven by document content
  (ADR 0004).
- Packages above `DocumentLimits.MaxDocumentBytes` are not parsed. XML parts and
  pictures above `DocumentLimits.MaxBinBytes` are skipped, the limit applied to
  what actually decompresses rather than to the size the ZIP header claims.
  Block and inline nesting stops at `DocumentLimits.MaxGroupDepth`.
- Inline DTDs are prohibited in every part, so entity expansion is not an attack
  surface.
- The other OpenDocument types (`.ods`, `.odp`, `.odg`), packaged or flat, are
  out of scope. A text *template* (`.ott`) probes as ODT and reads correctly,
  because its body is the same, but the descriptor claims only `.odt`. Flat
  text documents are read but never written.

## Read Diagnostics

Every read ends with an `odt.read.summary` info diagnostic, so a document that
opens blank can be told apart from one that *is* blank.
`broilerdoc info <file>` prints all diagnostics, the quickest way to see what a
problem document lost.

| Code | Severity | Meaning |
| --- | --- | --- |
| `odt.read.summary` | Info | Paragraph, table, style, list-style, image, and skipped-block counts for the read. |
| `odt.document.empty` | Warning | The body held block-level content but produced no paragraphs — a reader gap, not an empty file. |
| `odt.package.encrypted` | Error | The package is encrypted; nothing was read. |
| `odt.package.content` | Error | The package has no `content.xml`. |
| `odt.package.zip` | Error | The bytes are not a readable ZIP package. |
| `odt.flat.root` | Error | The bytes are XML, but the root element is not `office:document`. |
| `odt.flat.media-type` | Error | A flat file's `office:mimetype` names another OpenDocument type, such as a spreadsheet. |
| `odt.document.body` | Error | `content.xml` has no `office:text` body. |
| `odt.xml` | Error | A part's XML, or a flat document, could not be parsed; the message names the exception type. |
| `odt.xml.prefix` | Warning | A part or flat document used namespace prefixes it does not declare, and was read with LibreOffice's prefixes bound. |
| `odt.limit.bytes` | Error | Input exceeded `MaxDocumentBytes` and was not parsed. |
| `odt.content.xml` / `odt.styles` / `odt.manifest` / `odt.meta.xml` | Error | A part could not be parsed, or exceeded `MaxBinBytes` (`.limit` suffix). |
| `odt.meta.date` | Warning | An unreadable `meta:creation-date` or `dc:date` timestamp was dropped rather than guessed at. |
| `odt.block.unsupported` | Warning | A block-level element was not understood; once per distinct name, which the message gives. |
| `odt.frame.textbox` | Warning | A text box was read as body content; its frame position is not represented. |
| `odt.frame.block` | Warning | A page-anchored frame held neither body text nor a picture and was skipped. |
| `odt.annotation` | Warning | Comment content is not part of the body and was skipped. |
| `odt.note.nested` | Warning | A note inside a note was read as nothing, or, writing, dropped: ODF cannot nest them. |
| `odt.note.content` | Warning | A table or shape in a note was read as its text; a note holds paragraphs only. |
| `odt.settings.xml` | Warning | The settings part could not be read; default view, tracked changes shown. |
| `odt.link` | Warning | A hyperlink with a disallowed or relative target was dropped. |
| `odt.align.justify` | Warning | A justified paragraph was read as left-aligned. |
| `odt.linespacing.leading` | Warning | Line spacing stated as leading between lines (`style:line-spacing`) was not represented. |
| `odt.linespacing.range` | Warning | A positive line-height percentage over- or underflowed the float multiplier; the inherited spacing was kept. |
| `odt.break.after` | Warning | A `fo:break-after="page"` had no following paragraph in its flow (the flow or cell ended, or a table followed) and was dropped. |
| `odt.text.transform` | Warning | A `lowercase`/`capitalize` transform was dropped. |
| `odt.styles.unknown` | Warning | A style reference named an undefined style. Once per family and name. |
| `odt.styles.cycle` | Warning | A `style:parent-style-name` chain was cyclic and was cut short. |
| `odt.styles.depth` | Warning | A `style:parent-style-name` chain exceeded `MaxGroupDepth` and was cut short. |
| `odt.styles.list-unknown` | Warning | A `text:list` named an undefined list style; it was read as a bullet list. |
| `odt.image.missing` | Warning | A picture referenced a package entry that is not there. |
| `odt.image.external` | Warning | A picture linked to an image outside the package, which is not fetched. |
| `odt.image.format` | Warning | A picture used an image format this codec does not carry (SVG, EMF, WMF and the like). |
| `odt.image.limit` | Warning | A picture exceeded `MaxBinBytes` and was skipped. |
| `odt.image.binary` | Warning | An inline picture payload was not valid base64. |
| `odt.image.shape` | Warning | A frame held no embedded picture. |
| `odt.image.anchored` | Warning | A floating picture was anchored to its paragraph. |
| `odt.image.denied` | Warning | The caller's resource policy did not permit reading a picture; the message names the denial. |
| `odt.page.geometry` | Warning | A page layout described a page with no room to write on, so the page was not read. |
| `odt.gradient` | Warning | An unknown gradient style was approximated by its start colour. |
| `odt.fill` | Warning | A bitmap or hatch definition did not resolve, or a background picture outside the package was not fetched; a bitmap area is painted in its fill colour. |
| `odt.fill.opacity` | Warning | A bitmap fill's opacity cannot be painted; the bitmap is kept at full opacity. |
| `odt.columns.geometry` | Warning | A page layout's `style:columns` left no room in a column; the page was read as one column. |
| `odt.columns.unequal` | Info | Listed columns of different relative widths were read as equal. |
| `odt.limit.depth` | Warning | Block or inline nesting hit `MaxGroupDepth`; the deepest content was skipped. |
| `odt.limit.run` | Warning | A paragraph hit `MaxRunLength` and was truncated. |
| `odt.limit.spaces` | Warning | A `text:s` count exceeded `MaxRunLength` and was clamped. |
| `odt.limit.paragraphs` | Warning | Input exceeded `MaxParagraphCount`; remaining paragraphs were dropped. |

## Write Diagnostics

| Code | Meaning |
| --- | --- |
| `document.metadata.emitted` | Document properties the caller supplied reached `meta.xml` (Info); the message names the fields. |
| `document.metadata.narrowed` | A property reached `meta.xml` in a reduced form — ODF's `meta:initial-creator` holds one name, so authors past the first are not written. |
| `document.metadata.dropped` | A property ODF states no equivalent for was not written; the message names the fields. |
| `odt.link` | A hyperlink target the shared predicate refuses was written as plain text. |
| `odt.image.placeholder` | An object replacement character with no image attached was dropped. |
| `odt.image.size` | An image carried no display size and was written one inch square. |
| `odt.image.omitted` | The caller's resource policy did not permit passing a picture on; the writer's counterpart to `odt.image.denied`. |
| `odt.color.alpha` | ODF colours hold no alpha; the RGB channels were written. |
| `odt.text.control` | A control character XML cannot hold was dropped, from any string the model supplies, a picture's description as well as run text. |
| `odt.fill.opacity` | Unequal gradient endpoint opacities were written with the start opacity for both. |
| `odt.section.page` | A section states paper size or top/bottom margins, or the body switches master pages; an ODF `text:section` cannot state either. Relative left and right margins are written as `fo:margin-left` and `fo:margin-right` on `style:section-properties`. |
| `odt.section.page-number` | A section restarts the page count, which a document written with one master page cannot state. |
| `odt.list.label` | A label whose numbers are not the levels ending at its own joined by full stops lost the text between them, or a multi-character bullet kept only its first character. |
| `odt.list.format` | Leading-zero numbering was written as plain decimal. |
| `odt.list.legal` | DOCX legal numbering has no ODF form. |
| `odt.list.indent` | Exact per-paragraph list offsets were written with the list level's default positioning; non-list indents are kept. |
| `odt.list.shared-counter` | A list restart that also resets another list's shared counters (a DOCX relationship) was written as an independent restart. |
| `odt.shape.overflow` | Explicit text-box overflow was not written; the frame and its text are kept. |
| `odt.shape.linked` | Linked text-frame chain names were kept, but exported clipping of linked text differs between applications. |
| `odt.note.nested` | A note inside a note was dropped. |

## Probe Policy

ODF was designed to be identifiable from its first bytes, so probing is cheap:

- A leading `mimetype` entry (first in the archive, stored uncompressed, per
  ODF 1.3 part 2 §3.3) holding the OpenDocument text media type is **certain**,
  with no filename or MIME hint needed.
- The text *template* media type is high confidence: a template holds the same
  body a document does.
- Any other OpenDocument media type is **not** claimed, even with an `.odt`
  hint. The package says what it is.
- A ZIP holding `content.xml` and `META-INF/manifest.xml` but no `mimetype`
  entry is high confidence.
- A ZIP with an `.odt` filename or MIME hint and nothing else is high; a generic
  ZIP with no hint is not claimed.
- Non-ZIP bytes with an `.odt` or `.fodt` hint are low confidence.
- XML whose root is `office:document` in the OpenDocument office namespace is a
  flat document: **certain** with the text media type in `office:mimetype`, high
  with the text template type, not claimed with another type even with a
  `.fodt` hint. When the media type is not in the probe prefix (the prefix ended
  first or the attribute is absent) it is medium, or high with an `.odt` or
  `.fodt` hint. LibreOffice writes the attribute after its namespace
  declarations, at about byte 2,150, so the default 4,096-byte prefix sees it.

## Standards and rights

ODF is an OASIS standard, republished as ISO/IEC 26300. Its rights position (the
OASIS royalty-free mode and the covenants on top of it), the approved labels and
the provenance findings are in [the ODT register](../ip/odt.md). The position is
assessed green, which is a risk judgement and not a clearance. Implementing a
documented subset is not conformance, so nothing describes this codec as
ODF-conformant, and `OdtClaimGuardTests` fails the build over it.
