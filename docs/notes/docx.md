# DOCX Conformance

`Broiler.Documents.Docx` reads and writes a dependency-free DOCX subset using
Open XML WordprocessingML package parts.

## Supported Read/Write Subset

### Package, units and property order

- The main document is found through `_rels/.rels`, falling back to
  `word/document.xml`.
- ISO/IEC 29500 Strict packages are read (`docx.strict`); writing always
  produces Transitional. Strict is the same WordprocessingML under
  `http://purl.oclc.org/ooxml/` names, so each XML part and relationship type
  is renamed as it loads: `purl.oclc.org/ooxml/X/Y` becomes
  `schemas.openxmlformats.org/X/2006/Y`, with `extendedProperties` and
  `customProperties` spelled with hyphens. Content types and the OPC
  namespaces are the same in both.
- Universal measures (a decimal and one of `mm`, `cm`, `in`, `pt`, `pc`, `pi`,
  such as `w:before="18pt"` or `w:start="-14.15pt"`) are read wherever twips
  or half-points are: paragraph spacing and auto line spacing, the leading
  indent, font size, page size and margins, columns, grid column widths, cell
  margins, row heights and floating table positions. Strict writes all lengths
  this way and Transitional allows it. An auto `w:line` is in 240ths of a
  line, which Strict writes as if it were twips, so single spacing is `12pt`.
- `CT_PPr` and `CT_RPr` are `xsd:sequence`s, so each property has one legal
  position, and Word can refuse a whole file whose property container is out
  of order. Written order, asserted by `DocxPropertyOrderTests` over whatever
  properties are present:

  | Container | Order written | Positions in the sequence |
  | --- | --- | --- |
  | `w:pPr` | `w:pageBreakBefore`, `w:numPr`, `w:shd`, `w:spacing`, `w:ind`, `w:jc` | 4, 7, 10, 22, 23, 27 |
  | `w:rPr` | `w:rFonts`, `w:b`, `w:i`, `w:caps`/`w:smallCaps`, `w:strike`, `w:color`, `w:sz`, `w:u`, `w:shd` | 2, 3, 5, 7/8, 9, 19, 24, 27, 30 |

### Text, runs and links

- Paragraphs, empty paragraphs, tabs, and soft line breaks.
- Run text (`w:t`, `w:delText`) reads as Word and LibreOffice show it
  (`OOXMLFastContextHandler::text`, tdf#108806, tdf#108995): a line feed in it
  is a space, since only `w:br` breaks a line, and unless
  `xml:space="preserve"` is in scope (on the element or the nearest ancestor
  stating it) white space at either end is dropped and a tab is a space.
  `tdf123163-1.docx` starts every `w:t` with a line feed (issue #182).
- The paragraph mark's formatting, `w:pPr/w:rPr`, is
  `RichTextParagraph.MarkStyle`: the paragraph style's run formatting, then
  the mark's own. An empty paragraph's line is as tall as its mark. Written
  back at the end of `w:pPr`, before a section's `w:sectPr`.
- Direct formatting: bold, italic, underline, strikethrough, font family, font
  size, foreground color, and background shading. `w:color w:val="auto"` is
  the automatic colour (ECMA-376 §17.3.2.6), drawn black by Word and
  LibreOffice, so on a run it clears the colour its style gave.
- `w:caps` and `w:smallCaps` are a style attribute: the text keeps the casing
  typed and capitals are applied when drawing, so an open-and-save does not
  rewrite it. `w:val="0"` clears only the kind it names; a run declaring both
  draws all caps, as Word does.
- `w:vanish` is inherited and can be explicitly disabled. Hidden text remains
  editable and is written back, but is omitted from normal rendering; CLI text
  dumps retain it, as LibreOffice's plain-text export does.
- Block-level containers are walked recursively so their paragraphs are read:
  tables (`w:tbl`/`w:tr`/`w:tc`, including nested tables), structured document
  tags (`w:sdt`), tracked changes (`w:ins`, `w:del`, `w:moveTo`,
  `w:moveFrom`), `w:customXml`/`w:smartTag` wrappers, and
  `mc:AlternateContent` (first `mc:Choice`, else `mc:Fallback`). Paragraphs are
  read in document order, row-major for a table.
- Hyperlinks: absolute `http`, `https` and `mailto`, and internal anchor links
  (`w:hyperlink w:anchor`) whose `#fragment` is non-empty and carries no
  whitespace, quote or second `#`. Every other scheme, every relative target
  and a bare `#` are refused on read and write, the link written as plain text.
  One predicate decides this for all five codecs (`DocumentLinkTarget`). A
  fragment preserves the reference, not a working jump: no codec here reads or
  writes bookmarks, so the name it points at is not carried. A link gets no
  renderer link look (`DocumentStyleDefaults.UndecoratedLinks`): one naming no
  character style is plain text in Word and LibreOffice.

### Styles and paragraph formatting

- Named styles from `word/styles.xml` resolve per ECMA-376 §17.7.2: document
  defaults (`w:docDefaults`), the `w:basedOn` chain from its root down to the
  style named by `w:pStyle` or `w:rStyle`, then direct formatting. The default
  style (`w:default="1"`) applies only to content naming no style. Template
  documents carry nearly all their formatting here. What neither the defaults
  nor the default style states is LibreOffice's (`StyleSheetTable`,
  tdf#108350): with a `w:rPrDefault`, 10 point Times New Roman; without, Calibri
  11 (`DocxStyles.HasRunDefaults`; issue #180).
- Only attributes `RichTextDocument` models are resolved: character
  spacing/scaling is ignored, and so are theme colors (`w:themeColor`), since
  Word writes the computed RGB into `w:val` beside them.
- `w:rFonts` theme references such as `w:asciiTheme="majorHAnsi"` resolve to
  the major/minor latin typeface of `word/theme/theme1.xml`; an explicit font
  name on the same element wins.
- A run's family is its Latin font: `w:ascii`, `w:hAnsi` or the theme fonts
  they name. `w:cs` and `w:eastAsia` name complex-script and East Asian fonts
  (ECMA-376 §17.3.2.26) and give a family only to a run that inherited none, so
  `tdf170602_exportContext.docx`'s headings, whose styles say only
  `w:cs="Arial"`, stay in the document's Times New Roman.
- Paragraphs: left/center/right alignment, spacing before/after, indentation,
  bullet and numbered lists, line spacing. `w:spacing w:line` is read under all
  three rules (ECMA-376 §17.3.1.33): 240ths of a line under `auto` (also an
  omitted `w:lineRule`), twips under `exact` and `atLeast`
  (`ParagraphStyle.LineHeightRule` and `LineHeight`, capped at
  Word's 1584 points). Each replaces what a style below stated, and the rule is written
  back. An exact line's baseline sits four fifths down it, as in LibreOffice.
- Left/right and first-line/hanging indents keep their exact point values,
  including explicit zero overrides.
- `w:pPr/w:shd` (ECMA-376 §17.3.1.31), direct or styled, is
  `ParagraphStyle.Background`: the fill behind a `clear` pattern, the pattern
  colour for `solid`, nothing for `nil` or an `auto` fill (how a paragraph
  removes its style's shading); other patterns read as their fill. Written
  back as a `clear` `w:shd`. The layout paints it as LibreOffice does,
  measured: over the lines from the indent to the column edge, space before
  and after left white unless the next paragraph is shaded the same. The PDF
  writer does not paint it yet.
- `w:widowControl`, on a paragraph or its style chain, is `OrphanLines` and
  `WidowLines`, two or none. Unstated, a document whose `w:docDefaults` holds a
  `w:pPrDefault` - nearly every DOCX - keeps two, as Word does and LibreOffice
  imports it (`StyleSheetTable::applyDefaults`, tdf#87533), recorded once as
  `DocumentStyleDefaults.WidowOrphanLines`. The layout applies that default in
  floating-table cells, where Writer keeps it for Word documents (`widorp.cxx`,
  `TABLE_ROW_KEEP`): a row whose two-line cell has room for one line at a page
  foot moves whole, as in `fdo68607.docx`. Body paragraphs and inline-table
  cells do not take it yet; see the roadmap.

### Lists

- `word/numbering.xml` (ECMA-376 §17.9): each paragraph names its list
  (`ParagraphStyle.List`) with all nine levels of the abstract numbering:
  `w:start`, `w:numFmt` (decimal, `decimalZero`, letters, roman numerals,
  `bullet`, `none`; any other is decimal, reported as
  `docx.numbering.format`), `w:lvlText`, `w:isLgl`, `w:lvlRestart` and the
  level's `w:ind`.
- Every `w:num` naming one abstract numbering is one list counting on from the
  others; one with a `w:lvlOverride` (`w:startOverride` or a replacement
  `w:lvl`) restarts that shared counter on first use, and returning to another
  instance continues from the restarted count. Both survive a round trip.
- An explicit empty bullet `w:lvlText` stays empty. A `w:numStyleLink` takes
  the levels of the abstract numbering whose `w:styleLink` names that style. An
  absent `w:start` starts at one, as LibreOffice reads it, where §17.9.25 says
  nought. A Symbol or Wingdings private-use bullet reads as the character it
  draws.
- List and level each come from the nearest `w:numPr` stating them, the
  paragraph's or its style chain's; with no `w:ilvl`, the level is the one
  whose `w:pStyle` is the paragraph's style (how headings are numbered).
  `w:numId` 0 takes a paragraph out of its style's list.
- The label starts where the level's `w:ind` hangs it (`w:left` less
  `w:hanging`), the paragraph's `w:ind` overriding attribute by attribute, at
  indent level one at least; level offsets resolve beneath paragraph style and
  direct formatting. An empty paragraph carrying only a section's `w:sectPr`
  takes no number, as LibreOffice keeps no such paragraph
  (`tdf170602_exportContext.docx`).
- Written as an abstract numbering and a `w:num` per list, `w:ind` stating the
  text start and a 360-twip hang. A list naming no definition is written as
  plain lists drawn as the layout does (bullets solid, hollow, square; numbers
  decimal, letters, roman), a new `w:num` restarting each numbered run. A
  restarting item (ODF's `text:start-value`) loses the restart
  (`docx.list.restart`).

### Tracked changes

Tracked changes (ECMA-376 §17.13.5) are a `TextRevision` with `w:author` and
`w:date` on the runs and paragraph marks they mark:

| WordprocessingML | Read as |
| --- | --- |
| `w:ins`, `w:del` around runs or whole paragraphs | insertion, deletion; a deleted run keeps its `w:delText`, as in LibreOffice |
| `w:moveTo`, `w:moveFrom` | the two sides of a move |
| `w:rPrChange` | a formatting change, unless an insertion or deletion marks the run already |
| `w:pPrChange` | the whole paragraph's text marked so, as LibreOffice draws it |
| `w:ins`, `w:del` in a mark's `w:rPr` | a change to the paragraph break |

- `w:revisionView` hides the changes where it turns `w:insDel` or `w:markup`
  off without turning `w:formatting` on, as LibreOffice reads it
  (`RichTextDocument.ShowsRevisions`).
- Written back: each change around its runs with its own `w:id`, a format
  change as `w:rPrChange` with an empty `w:rPr` (the former look is not kept),
  a paragraph-property change as each run's, and
  `w:revisionView w:insDel="0"` when hidden.
- A change to a table row or cell (`w:trPr`, `w:tcPr`) is not read; the row's
  text is marked by its runs' own `w:ins` and `w:del`, as Word writes it.

### Footnotes and endnotes

- A `w:footnoteReference` or `w:endnoteReference` in the body, a table cell or
  a frame becomes one citation character holding a `DocumentNote`, read from
  the `w:footnote` or `w:endnote` with that id in the related part; Word's
  separators there are not notes.
- The leading `w:footnoteRef` label and the space after it are left out;
  whether it is superscript (Word's FootnoteReference style) is
  `NoteNumbering.RaisedLabels`. `w:customMarkFollows` makes the following text
  the note's own mark. `w:footnotePr` and `w:endnotePr`, in settings and the
  last section, give each kind's `w:numFmt` and `w:numStart`.
- A reference in a header, a text box or a note is reported
  (`docx.note.placement`), as is one naming an absent note
  (`docx.note.missing`); a table or shape in a note is read as its text
  (`docx.note.content`).
- Written as `word/footnotes.xml` and `word/endnotes.xml`, each with Word's two
  separators, the citation raised with `w:vertAlign`.

### Fields

- **Fields are drawn from their saved result**, the runs between a complex
  field's `separate` and `end` (or a `w:fldSimple`'s runs); the instruction is
  not text. The writer writes the text, not the field. Fields other than these
  keep their saved result:
- `PAGE` and `NUMPAGES` (with `\* roman`, `ROMAN`, `alphabetic`,
  `ALPHABETIC`, `Arabic`, `MERGEFORMAT`): result runs carry a `PageField`,
  drawn in a header or footer as the page number or count, formatted as the run
  holding the separator, since LibreOffice inserts the field there and ignores
  the result (issue #180). One saved without a result shows the first page's
  number.
- In a `TOC` field's result, runs take no character style, as in LibreOffice,
  so Word's blue, underlined `Hyperlink` entries draw in their paragraph style.
- `STYLEREF` naming a character style, with `\l` and the `\* MERGEFORMAT`/
  `CHARFORMAT` switches: result runs carry an `InlineStyle.StyleReference`,
  and a header or footer shows the text in that style on each page: the first
  stretch (the last with `\l`), else the nearest before the page, else the
  first after. That is Word's search, and LibreOffice's since tdf#163894
  (master, February 2026). Runs keep their character style's name for it
  (`InlineStyle.CharacterStyle`, from `w:rStyle`'s `w:name`). A `STYLEREF`
  naming a paragraph style, `STYLEREF 1`, or with any other switch keeps its
  saved result.

### Compatibility settings

- **Word 2013 justification.** A `compatibilityMode` of 15 or more in
  `w:compat` (Word's URI; the highest stated wins, as in Word and LibreOffice's
  `SettingsTable::GetWordCompatibilityMode`) sets
  `DocumentStyleDefaults.ShrinksJustifiedLines`, LibreOffice's
  `JustifyLinesWithShrinking` (tdf#130088): a justified line that would break
  may exceed its column by a quarter space per space, its spaces then
  narrowed. The writer writes `compatibilityMode` 15 when the flag is set.
- **Word 2010 and earlier.** A mode of 14 or less, or none, sets
  `DocumentStyleDefaults.TabsOverMargin`, LibreOffice's `TabOverMargin`
  (`SettingsTable::ApplyProperties`): a tab stop past the right indent keeps
  its place and alignment, and text after it runs past the indent, up to
  55.8 cm, instead of wrapping. A stated mode of 11 to 14 also drops the
  spaces, tabs and no-break spaces ending a paragraph in a table cell, as
  LibreOffice's import does (tdf#77417, `DomainMapper_Impl::finishParagraph`),
  with the info diagnostic `docx.table.trailingspace`. `tdf77417.docx` needs
  both: its amount tables end cells in a tab and no-break spaces, with stops
  past narrow cells' right indents.

### Tables

- A table is a `DocumentTable` over its cells' paragraphs: `w:tblGrid` column
  widths, `w:gridSpan` and `w:vMerge` spans, cell and table borders
  (`w:tcBorders`, `w:tblBorders` with `insideH` and `insideV`; a cell takes
  the outer edge at the table boundary and the inside edge between cells),
  cell shading (`w:shd`), the `w:tblCellMar` left margin as padding, header
  rows (`w:tblHeader`), stated row height (`w:trHeight`), and nested tables.
  Written back as `w:tbl`.
- Cells hold no text: each names a range of the document's paragraphs, so
  caret, selection, styles and every codec's text handling work on one flat
  list. Ranges move through the one place paragraph counts change, so typing,
  splitting and deleting in a cell keep the table; an edit spanning out of a
  cell into the body does not keep the grid over what it merged.
- Writing a vertical merge supplies continuation cells for model rows that
  omit covered cells, as ODT readers do; later cells keep their grid columns
  and explicit continuations are not duplicated.
- Cell vertical alignment, text direction, and table indent and alignment are
  not represented; a table starts at the left margin.
- DrawingML and VML text-box tables keep their grid in the shape's paragraph
  coordinate space, are written inside `w:txbxContent`, and the shared and PDF
  renderers place their cells side by side, in drawing groups too; affine font
  scaling and full group fidelity remain approximate.

#### Table styles

Everything above but row height can come from the table's style, resolved as
LibreOffice's `DomainMapperTableHandler` does:

- The style's `w:pPr` and `w:rPr` apply in its cells between the document
  defaults and the paragraph style (ECMA-376 §17.7.2): `Table Grid` takes
  Word's default ten points after and 1.15 line back out of every cell, and a
  paragraph style or direct formatting still wins. A table naming no style
  takes the default table style; a nested table takes its own, not its
  parent's; a `w:tblStyle` naming a paragraph style applies nothing. Word's
  rule letting a table style override an 11 or 12 point size or left alignment
  from the default paragraph style is not modelled.
- The style chain's `w:tblBorders` sit under the table's own, edge by edge,
  and those under a row's `w:tblPrEx` borders. A style's cell `w:tcPr`
  `w:tcBorders` and `w:shd` sit under the cell's own; a cell turning off a
  styled edge gives it back to the table, as Word does. The style's `w:tblPr`
  `w:shd` shades cells stating none, and its `w:tblCellMar` pads a table
  stating none.
- Conditional formats (`w:tblStylePr`) reach first and last row and column,
  the four corners, and horizontal and vertical bands, with paragraph, run,
  border and shading properties; a first-row format marking a header
  (`w:tblHeader`) repeats it. `w:tblLook` selects them as LibreOffice reads
  it: no look means first row, first column and horizontal bands (`04A0`); the
  2010 attributes, if any is present, are read alone, each on unless it says
  otherwise; a row or column with no format defined is off, and banding counts
  from below it when it is on. Formats merge bands, columns, rows, corners, so
  the first row beats a band. A style's `w:cantSplit` keeps every row whole
  unless the row says otherwise.
- Not applied, reported as `docx.table.style`: cell vertical alignment,
  diagonal borders, bands of several rows or columns
  (`w:tblStyleRowBandSize`), row heights, table alignment. A style's indent
  and width are read no more than a table's own.

#### Rows

- `w:trHeight` is a minimum unless `w:hRule="exact"` fixes it
  (`docx.table.rowheight`), which the writer preserves; fixed rows keep all
  their model text and the shared and PDF renderers clip the overflow.
  `w:hRule="auto"` asks for nothing. A height with **no** rule is a minimum
  too, deliberately against the specification's default of `auto`: no
  producer means `auto` by omission, Word renders a bare height as a floor,
  and CV and letterhead layout tables state tall empty rows to place the block
  beneath, which would collapse as `auto`.
- `w:cantSplit` is `TableRow.CanSplit`, an on/off property, written back. A
  row without it may break across a page, as in Word, a cell's nested table
  breaking between its rows, each kept whole.
- `w:tblHeader` is `TableRow.IsHeader`, an on/off property, written back.
  Leading header rows repeat atop every page or column the table continues
  onto, unless as tall as the page; a table of only header rows repeats none.
  Nested tables and the PDF writer do not repeat them.

#### Placement

- A floating table, `w:tblpPr` (ECMA-376 §17.4.57), is
  `DocumentTable.Position`: anchors across and down, offsets or the
  `tblpXSpec`/`tblpYSpec` alignment replacing them, distances from the text,
  and `w:tblOverlap`, written back with both anchors stated. An omitted anchor
  reads as LibreOffice's `TablePositionHandler` reads it: margin down, text
  across. The layout stands it there at its own width, out of the flow; text
  runs down the side with room, or below where neither side has an inch; an
  inline table that would run into it moves below, as LibreOffice master does
  (`testInlineTableShiftDown`; 24.2 overlaps them). Absolute and percentage
  preferred widths resize the grid consistently, percentages against the page
  text extents. Intersecting floating tables move apart when either forbids
  overlap. One taller than the room left continues atop the next page,
  repeating header rows when body content still fits (replaying list labels
  without consuming the body's counters); multiple continuations are kept and
  finish on their section's paper before a following section opens a page. A
  non-splittable first row moves to a fresh page when it fits there. A
  floating table nested in a cell stays in it (`docx.table.floating`), and the
  PDF writer lays floating tables out in the flow
  (`pdf.write.feature-unsupported`).
- A non-nested table's horizontal place follows LibreOffice 24.2's
  `DomainMapperTableHandler` (`DocxTableReader.PlaceAsWord`);
  `DocumentTable.LeftIndent` and `TablePosition.X` are its left edge. Word
  2010 and earlier (`compatibilityMode` 1 to 14) put the first column's text
  at `w:tblInd`, so the edge is the left cell margin (or half the left border,
  if wider) left of it; Word 2013 and no stated mode put the edge at
  `w:tblInd`. A table placed by `w:tblpX` stands its left cell margin left of
  the offset below mode 15 (no mode included), and half its first cell's left
  border further in every mode; one aligned by `w:tblpXSpec` is not moved. The
  writer states no mode and adds the floating offset back, so tables come back
  where they were. Word's default cell margin is 5.4 points.

### Page and column breaks

| WordprocessingML | Read | Written |
| --- | --- | --- |
| `w:pPr/w:pageBreakBefore` | `ParagraphStyle.PageBreakBefore` on that paragraph | `w:pPr/w:pageBreakBefore`, first in `w:pPr` |
| `w:r/w:br w:type="page"` at the end of a paragraph | `PageBreakBefore` on the **following** paragraph | as `w:pageBreakBefore` on that following paragraph |
| `w:r/w:br w:type="column"` | a page break in a section of one column; `ParagraphStyle.ColumnBreakBefore` on the **following** paragraph in a section of several | `w:br w:type="column"` run first in the paragraph that has `ColumnBreakBefore` |
| `w:br` with no type, or `w:type="textWrapping"` | a line break (`U+2028`) | `w:br` |

- `w:pageBreakBefore` follows the character properties' on/off rule: present
  means on, and `w:val="false"`, `"0"` or `"off"` turns off a break the style
  chain applied (a template opening every chapter on a fresh page exempts one
  heading so). A break in a paragraph style resolves through the chain.
- `w:br w:type="page"` (Word's Ctrl+Enter) sits at the end of the paragraph
  *before* the page it starts, hence it is read onto the next. The write is
  always the property form the model holds: a break in a run would make one
  paragraph's markup depend on the next, and could not express a break on the
  first paragraph. It is written first in `w:pPr`, where `CT_PPr` puts it.
- A break *inside* a paragraph splits it into two fragments at the boundary,
  as Word and LibreOffice do, the second starting the new page with no
  first-line indent or space before (`docx.pagebreak.fragment`). A break
  opening its paragraph just starts it on the new page; nothing is split or
  demoted.
- Two breaks with nothing between are two boundaries and a blank page: the
  first is kept and `docx.pagebreak.repeated` reports the rest, since
  honouring the later would move the paragraph a page further than the
  document did. So also where a paragraph already starts a page, from a break
  held over or `w:pageBreakBefore`.
- A break in a part's last paragraph has none to start and is dropped
  (`docx.pagebreak.trailing`); a document deliberately ending on a blank page
  loses it.
- `w:br w:type="column"` in a one-column section is a page break, read exactly
  as `w:type="page"` with all the cases above: ECMA-376's `ST_BrType` restarts
  the text on the next page there, and Word and LibreOffice both do. With
  several columns it moves to the next column, travelling and splitting like a
  page break. A paragraph's section is found before its content is read,
  since section properties follow their paragraphs. In a header, a footer or a
  text box it stays a line break.
- A page break run in a table cell is dropped (`docx.pagebreak.table`) with
  nothing in its place, not even a space, as LibreOffice 24.2 imports it
  (`DomainMapper_Impl::deferBreak`, after the binary filter's
  `HandlePageBreakChar`). A column break there splits its paragraph where
  something precedes it (`m_bIsSplitPara`) and starts no column, as Writer
  ignores it in a cell. Word splits the row at such a break; the layout here
  breaks rows only where the page runs out. `fdo68607.docx` is a glossary
  pasted from a PDF with a page break ending every definition line
  (issue #185). A `w:pageBreakBefore` in a cell is read and written back, and
  the layout ignores it.
- A pending section or trailing run break before a table goes onto the
  table's leading page/column-break flag, not its first cell paragraph; an
  empty table leaves it pending for the next block. The writer does not
  serialize table breaks and reports `docx.table.break`.

### Sections and page setup

- `w:type` on a `w:sectPr` says how its section starts: `nextPage` (also
  absent `w:type`), `oddPage` and `evenPage` start a new page, as
  `PageBreakBefore`; `continuous` runs on below. A section break and a page
  break ending the paragraph before are one boundary.
- `oddPage` and `evenPage` are kept as `DocumentSection.BreakType` and
  round-trip through `DocxWriter`; the layout adds blank pages for parity,
  even before the document's first page. A change of orientation
  (`w:orient="landscape"`, `"portrait"`) or paper size between sections forces
  a page break (`SectionBreakType.NextPage`). `nextColumn` reads as
  `NextColumn` and forces a page break when the column count changes.
- **Page numbering.** `w:pgNumType`'s `w:start` is
  `DocumentSection.PageNumberStart`, written back. An `oddPage` or `evenPage`
  section starts on a page whose number, not place, has that parity, so a sole
  `evenPage` section numbered from 2 opens with no blank page.
- An empty body paragraph holding only the `w:sectPr` is the section mark, not
  a paragraph, as in LibreOffice's import: the section ends at the paragraph
  before. It stays when it is all its section holds, when nothing follows it,
  and right after a table, which it closes. `tdf170602_exportContext.docx`
  puts its next-page mark in an empty Heading 1 after a page break.
- `w:cols` gives a column count and gap, `w:space`, half an inch if absent.
  Listed columns (`w:equalWidth="0"`) read as their count and mean space;
  widths differing by over a point become equal columns
  (`docx.columns.unequal`). If all sections agree, the page holds the columns;
  otherwise each is a `DocumentSection` over its paragraphs with its own
  (`docx.section.columns`), and the page records the section with the most
  paragraphs, because producers often end a two-column document with a
  continuous break before one empty paragraph. Columns leaving no room are
  dropped and the page kept (`docx.columns.geometry`). `w:sep` is not read.
- The last section's `w:pgSz` and `w:pgMar` are the document's page; a section
  stating others keeps them as `DocumentSection.Page`. A section starting a
  page is laid out between its own margins there and on the pages it runs
  onto, so a magazine whose sections are set two inches in, or six, keeps its
  column widths. A section a `continuous` break opens keeps the margins of its
  page and spreads its columns over that page's text width, as LibreOffice
  lays it out (a text section inside the page style before it); Word would
  use its own. In `tdf90697_complexBreaksHeaders.docx` a narrow two-column
  section that begins a page sets that page's header in its own width.
- Each section's header and footer references are read; a type a section does
  not name continues from the nearest earlier section that does
  (ECMA-376 §17.10.5), while `w:titlePg` (§17.10.6) is per section. The last
  section's set is the document's `RunningContent`, a differing set is
  `DocumentSection.RunningContent`. A page carries the set of the section that
  started it, the first-page part on its first page; a section following on
  the same page leaves the pages it runs onto unchanged, as LibreOffice keeps
  the page style - unless it has its own set and a hard page break inside it
  starts a page, which carries that set with no first page, as LibreOffice
  puts the section's page style on that break. A contract naming its header
  and footer in the first of nine sections carries them on every page;
  `tdf167527_title_letters_cut_from_below.docx`, whose title page is its first
  section's first-page header, reaches it. A part several sections name is
  read once.
- Writing: `w:cols` states count and gap. Each section boundary is a
  `w:sectPr` in the `w:pPr` of the section's last paragraph, with its page (or
  the document's), its columns and `w:type="continuous"`; a section starting a
  page says so only by its first paragraph's `w:pageBreakBefore`. A section
  ending in a table gets an empty paragraph after it to hold the `w:sectPr`, as
  Word writes. The body's `w:sectPr` closes the last section, `continuous`
  too. Each `w:sectPr` names its section's header and footer parts, with an
  empty part for a type one set lacks and another has, so it does not
  continue.
- Not written: section background colours (`docx.section.background`);
  hidden body-section visibility, exported as visible paragraphs
  (`docx.section.hidden`; ODT keeps the flag and the shared raster paginator
  omits hidden body sections); automatic page-style sequences, of which each
  section's first style is written without later automatic paper and margin
  changes (`docx.section.page-sequence`).

### Headers and footers

- `w:titlePg` gives a `first` part meaning and makes a band the first page
  does not name *empty* rather than the default (a letterhead: first-page
  header, default footer, no footer on page one). Written whenever a First
  part exists or the model says the first page differs. A `first` part in a
  section without it is not read (`docx.header.first`), since Word draws the
  default there.
- `w:evenAndOddHeaders` in `word/settings.xml` gives an `even` part meaning;
  without it Word and LibreOffice use the default on every page, so `even` is
  not read (`docx.header.even`). A write with an Even part turns it on in a
  settings part, or Word would ignore the part.
- Header and footer shapes belong to the running content, placed against the
  page: `svg`-style horizontal offsets from the text column, vertical ones
  from the page top, as running content has no body paragraph to hang from.
  So every vertical `relativeFrom` converts — `page` and `topMargin` from the
  top edge, `margin` and `bottomMargin` through the geometry — and is written
  back as `positionV relativeFrom="page"`. A part holding only a stripe and no
  words is still a part.

### Pictures

- A picture is one object replacement character (`U+FFFC`) whose run carries
  the image. Read from DrawingML (`w:drawing`, inline and anchored, also inside
  `mc:AlternateContent`) and legacy VML (`w:pict`/`v:imagedata`); size from
  `wp:extent` in EMUs or the VML CSS `width`/`height`; alternative text from
  `wp:docPr@descr`. Raster formats only: PNG, JPEG, GIF, BMP, TIFF, WebP, and
  ICO.
- A missing, external or unsupported-format picture is kept as an
  `InlineImage` placeholder with its stated size and alternative text
  (`docx.image.missing`, `docx.image.external`, `docx.image.format`). EMF/WMF
  metafiles — Word's charts, SmartArt and shape fallbacks — cannot be decoded
  to pixels here and report `docx.image.format` rather than drawing as
  nothing. An `r:link` image is not fetched, as that would be a network or
  file request driven by document content (ADR 0004).
- A write stores each distinct image once under `word/media`, with its
  relationship and a content-type default; payload-less placeholders are
  omitted with `docx.image.omitted`.
- Source crop (`a:srcRect`) and `a:prstGeom` are read; the ellipse is
  deliberately the only preset represented, for the round portraits of CV and
  letterhead templates, and others draw as their bounding rectangle. Crops are
  fractions of the source, so they survive resizing; crops that meet or cross
  are ignored rather than losing the picture. The crop applies before the
  mask, inscribing the ellipse in the part drawn.
- An anchored (`wp:anchor`) picture is a floating shape at the `posOffset` of
  its `wp:positionH` and `wp:positionV`, converted from the anchor's frame into
  the text column and paragraph — the same box a `wps:wsp` shape gets.
  Horizontally every frame converts exactly: nothing from the left margin and
  minus the margin from the column are the same place. Written back anchored,
  so a logo over a letterhead stays over it instead of pushing the text down.
  An anchor with no `wp:extent` stays in the text. Rotation and picture effects
  are dropped.
- `behindDoc` (shape against text) and `relativeHeight` (against other shapes)
  are read and written, so stripes stay behind and stamps on top; a missing
  `relativeHeight` reads as zero, keeping read order. `wrapSquare`,
  `wrapTight` and `wrapThrough` all wrap the shape's box, not the outline, so
  text clears the frame. A wrapped line keeps one span down the side with more
  room (`wrapText="largest"`), so `bothSides` gets the larger side. `distL`
  and `distR` become one clearance on both sides. A shape anchored to a later
  paragraph does not narrow lines above it, and an indented paragraph's band
  is measured from its own left edge, so an overlapping shape is out by the
  indent.
- A vertical `relativeFrom` of `page`, `topMargin`, `margin` or
  `bottomMargin` measures from the page, and which page a paragraph lands on is
  a layout result, so the model holds the frame: `DocumentShape.OffsetFromPage`,
  converted to an offset from the page's top edge, placed that far down
  whichever page holds the paragraph. A write states `relativeFrom="page"`.
  `insideMargin` and `outsideMargin` depend on page parity: kept as stated and
  measured from the paragraph vertically, read as an odd page's horizontally,
  reported as `docx.anchor.relativefrom`.

### Shapes and text boxes

- VML shapes in a `w:pict` - `v:rect`, `v:roundrect`, `v:oval` and a
  picture-less `v:shape` - are floating shapes (Word's pre-2010 text boxes).
  The box is the CSS `style`: `width`, `height`, and
  `margin-left`/`margin-top` from the column and paragraph by default;
  `mso-position-horizontal-relative` and `mso-position-vertical-relative` name
  other frames (`page`, `margin`, `left-margin-area`, `top-margin-area` and so
  on), converted as a DrawingML anchor's: across into the column, down into
  `OffsetFromPage`. A negative `z-index` puts it behind the text. The fill
  reads as the page background's, approximations reported as
  `docx.shape.fill`; the outline is `strokecolor`, VML's default black, or none
  where `stroked` is off; the text is the `w:txbxContent` in its `v:textbox`.
- The column any shape frame converts to is that of the text's page: the
  margins of the last section that started a page, which a following
  continuous section keeps as LibreOffice lays it out. `tdf123163-1.docx`
  places a backdrop against its first section's 134.9 points margin, not its
  last section's 275.5 points (issue #182).
- A shape in a table cell positioned from the column is placed from the cell's
  text area (`DocumentShape.OffsetXFromCell`) unless its anchor says
  `layoutInCell="0"`; a line always is (issue #180).
- DrawingML fill, gradient-stop and outline colours read from `a:srgbClr`,
  `a:sysClr` (its `lastClr`), some `a:prstClr` names, and `a:schemeClr` through
  the theme scheme (ECMA-376 §20.1.6.2), `bg1`/`tx1`/`bg2`/`tx2` mapping to
  `lt1`/`dk1`/`lt2`/`dk2` as Word's default `w:clrSchemeMapping` does; `phClr`,
  a style-supplied colour, is not resolved. Transforms apply in order
  (§20.1.2.3) with LibreOffice's arithmetic, measured against its rendering:
  `a:shade` and `a:tint` toward black and white in linear RGB at gamma 2.3,
  `a:lumMod`, `a:lumOff` and `a:satMod` in HSL, and `a:alpha` as opacity,
  written back. Translucent gradient bands are painted edge to edge, not
  overlapping, and the PDF writer, having no transparency group, blends
  translucent fills over white.
- Pattern fills (`a:pattFill`), radial and rectangular gradients
  (`a:gradFill` with `a:path`) and bitmap fills (`a:blipFill`) are read and
  written as `ShapeFill`, which the common model carries for ODT rendering. A
  pattern fill is drawn as LibreOffice draws it, hatch lines in `a:fgClr` over
  `a:bgClr`: a solid of its background (its foreground if no background) with
  a `HatchFill` in the foreground whose spacing and angle come from the
  preset. A three-stop gradient whose ends agree is the axial fill, and axial
  fills are written so. Writing gradient borders or page-fill bounds reports
  `docx.fill.extended` where approximated as a base colour or linear gradient.
- A drawing group, `wpg:wgp`, reads as a floating shape per `wps:wsp`, nested
  `wpg:grpSp` included. Each child's `a:xfrm` maps through the `a:chOff`/
  `a:chExt` child space of every group above into the anchor's extent
  (ECMA-376 §20.1.7.6); children share the group's anchor, `behindDoc`,
  `relativeHeight` and wrap, drawing in listed order. Rotation and flips are
  not applied, and a picture in a group is not read (`docx.shape.group`). A
  write puts each box back as its own shape.
- Empty, unfilled DrawingML and VML shapes keep their outlines: rectangles,
  ellipses and DrawingML lines, including degenerate horizontal/vertical rules
  and flipped directions. DrawingML `a:ln@w` and VML `strokeweight` keep point
  widths, default 0.75 pt. Export keeps paths and widths and writes no fill
  for unfilled shapes. Group rotations and arbitrary preset paths remain
  outside this subset; VML line endpoints are not read by this path.
- Text boxes keep DrawingML `bodyPr` insets and VML `inset`, explicit zero
  included; defaults are 7.2 points across and 3.6 down, and their paragraph
  spacing and list numbering lie inside. A box too short for its first line
  still shows it, cut at the text area, as Writer formats a frame's first line
  whatever room it has (`fdo68607.docx`'s title, a 24 point line in a box
  26.5 points high inside its insets); later lines that do not fit are left out.
  DrawingML top/centre/bottom anchoring and explicit vertical overflow are
  kept and rendered; VML middle/bottom anchors and visible overflow are read.
  Writing minimum-width auto-sizing or linked ODT frames reports
  `docx.shape.flow`: they become independent fixed-width boxes.
- `w:background` (ECMA-376 §17.2.1) is `RichTextDocument.PageBackground`,
  read only where `w:settings` holds `w:displayBackgroundShape`
  (§17.15.1.25): Word keeps a page colour it is not told to show, and
  LibreOffice reads neither colour nor shape without it. The VML
  `v:background` inside is what Word draws, `w:color` its fallback. VML fills
  convert as LibreOffice's (`oox/source/vml/vmlformatting.cxx`): the angle runs
  counter-clockwise from the bottom, a quarter turn from the model's; a linear
  gradient starts from `color2`; a focus of 100% or a negative angle reverses
  it; a focus near 50% is the mirrored gradient the model calls axial, with
  `fillcolor` at the edges. A rectangular gradient (`gradientRadial`) reads as
  its outer colour, most of the page; a one-colour pattern as that colour; a
  two-colour pattern or picture as `w:color`, these three reported as
  `docx.background.fill`. A write puts `w:color` (a gradient's middle colour)
  on `w:background`, a `v:background` for a gradient, and turns the switch on.

## Intentional Limits

Each feature's limits are stated with it above. Beyond those:

- Embedded objects, comments, and other fields, header, footer and section
  features are skipped or approximated, with diagnostics where applicable.
- Block nesting deeper than `DocumentLimits.MaxGroupDepth` is abandoned
  (`docx.limit.depth`).
- DOCX packages above `DocumentLimits.MaxDocumentBytes` are not parsed.
- XML parts above `DocumentLimits.MaxBinBytes` are skipped.
- Color alpha has no DOCX colour value outside DrawingML's `a:alpha`; RGB
  channels are written with `docx.color.alpha`.

## Read Diagnostics

Every read ends with `docx.read.summary`, so a document that opens blank can be
told apart from one that *is* blank:

| Code | Severity | Meaning |
| --- | --- | --- |
| `docx.read.summary` | Info | Paragraph, table, style, image, and skipped-block counts for the read. |
| `docx.strict` | Info | The package used Strict namespaces, read as their Transitional equivalents. |
| `document.metadata.emitted` | Info | Caller-supplied document properties reached `docProps`; the message names the fields. |
| `document.metadata.dropped` | Warning | A property without an OOXML equivalent was not written: `CreatorApplication`, as OOXML names a producing application only. |
| `docx.document.empty` | Warning | The body held block-level content but produced no paragraphs — a reader gap, not an empty file. |
| `docx.table.style` | Warning | A table style stated a property not applied (listed under Table styles); the rest of the style was applied. |
| `docx.table.rowheight` | Warning | A row stated an exact height; its text is kept and its rendered overflow clipped. |
| `docx.table.floating` | Warning | A table nested in a cell stated a floating position; it was laid out in its cell. |
| `docx.block.unsupported` | Warning | A block-level element was not understood; named in the message, once per name. |
| `docx.pagebreak.fragment` | Warning | A page or column break with text after it split the paragraph into two fragments. |
| `docx.pagebreak.repeated` | Warning | A paragraph stated more than one page break; the first was kept, the blank pages the others make dropped. |
| `docx.pagebreak.trailing` | Warning | A page break in a part's last paragraph was dropped. |
| `docx.table.trailingspace` | Info | A Word 2010-or-earlier document (`compatibilityMode` 11 to 14) ended a cell paragraph with spaces, tabs or no-break spaces, dropped as Word ignores and LibreOffice removes them. |
| `docx.pagebreak.table` | Warning | A page or column break in a table cell started no page or column: a page break was dropped, a column break split its paragraph; rows break only where the page runs out. |
| `docx.section.columns` | Info | Sections differ in columns; each keeps its own, the page records those of the section with the most paragraphs. |
| `docx.header.first` | Info | A section has a `first` header or footer, own or continued, without `w:titlePg`; its first page carries the default. |
| `docx.header.even` | Info | An `even` header or footer was not read, as `w:evenAndOddHeaders` is not set; even pages carry the default. |
| `docx.section.parity` | Warning | A section starting on an odd or even page was read as starting on the next page; the blank page it can put before itself is not made. |
| `docx.columns.unequal` | Info | A section's unequal columns were read as equal. |
| `docx.numbering.format` | Info | A list level used a format other than decimal, letters or roman numerals (ordinals, number words, a script's own digits) and was numbered in decimal. Once per read. |
| `docx.list.restart` | Warning | Write: a list item restarting its count was written without the restart. Once per write. |
| `docx.columns.geometry` | Warning | A section's columns left no room; read as one column. |
| `docx.limit.depth` | Warning | Block nesting hit `MaxGroupDepth`; the deepest content was skipped. |
| `docx.styles.missing` | Warning | Content named styles but the package has no styles part. Once. |
| `docx.styles.unknown` | Warning | A `w:pStyle`/`w:rStyle` named an undefined style. Once per id. |
| `docx.styles.cycle` | Warning | A `w:basedOn` chain was cyclic and was cut short. |
| `docx.styles.depth` | Warning | A `w:basedOn` chain exceeded `MaxGroupDepth` and was cut short. |
| `docx.part.headerfooter` | Info | The package has headers or footers, which are not part of the body. |
| `docx.properties.xml` | Error | `docProps/core.xml` or `docProps/app.xml` could not be parsed, or exceeded `MaxBinBytes` (`.limit` suffix); the document still reads. |
| `docx.properties.date` | Warning | An unreadable `dcterms:created` or `dcterms:modified` timestamp was dropped rather than guessed at. |
| `docx.image.missing` | Warning | A picture referenced a media part the package does not contain. |
| `docx.image.relationship` | Warning | A picture named an undefined relationship. |
| `docx.image.external` | Warning | A picture linked to an image outside the package, which is not fetched. |
| `docx.image.format` | Warning | A picture used a format this codec does not carry (EMF/WMF and the like). |
| `docx.image.limit` | Warning | An image part exceeded `MaxBinBytes` and was skipped. |
| `docx.image.shape` | Warning | A drawing held no embedded picture. |
| `docx.image.anchored` | Warning | A floating picture was anchored to its paragraph. |
| `docx.anchor.relativefrom` | Warning | A mirrored-margin anchor frame was approximated (odd page horizontally, paragraph vertically). |
| `docx.background.fill` | Warning | The page background's VML fill was read as one colour (`gradientRadial`, two-colour pattern or picture fill). |
| `docx.shape.group` | Warning | A drawing group held a picture, or groups nested past sixteen deep, which were not read; its drawn boxes were. |
| `docx.shape.fill` | Warning | A VML shape's fill was read as one colour, for the same reasons. |
| `docx.note.placement` | Warning | A note reference in a header, a footer, a text box or a note was skipped: only the body cites notes. |
| `docx.note.missing` | Warning | A note reference named an id its notes part does not hold, and was skipped. |
| `docx.note.content` | Warning | A table or shape in a note was read as its text; a note holds paragraphs only. |
| `docx.notes` | Error | A notes part could not be parsed, or exceeded `MaxBinBytes` (`.limit` suffix). |

Write diagnostics are `docx.color.alpha`, `docx.image.omitted`,
`docx.image.placeholder`, `docx.image.size`, `docx.link`, `docx.note.nested`,
`docx.shape.page`, and `docx.text.control`, besides `docx.table.break`,
`docx.fill.extended`, `docx.shape.flow` and the `docx.section.*` codes above:

- `docx.note.nested`: a note cited inside a note was dropped;
  WordprocessingML cannot nest them.
- `docx.shape.page`: a shape anchored to a page by number was written beside
  the paragraph it was read with, its vertical offset measured from the page
  (`relativeFrom="page"`); WordprocessingML anchors every drawing to a
  paragraph and cannot name a page.
- `docx.text.control`, the counterpart of ODT's `odt.text.control`: XML 1.0
  cannot represent most control characters, not even escaped, so one reaching
  the writer is dropped and reported. They arrive from ordinary input: the
  HTML reader decodes `&#7;` and the RTF reader passes `\u7` through. Every
  model string passes the filter, picture descriptions as well as run text.

`broilerdoc info <file>` prints all of them, the quickest way to see what a
problem document lost. In the Writer, `BROILER_WRITER_DOCUMENT_LOG=1` writes
the same list to stderr on every open; the status bar always reports a read
that produced no text.

## Probe Policy

DOCX probing is conservative because DOCX is a ZIP-based OPC package:

- ZIP signature plus DOCX filename/MIME hint is high confidence.
- A visible `word/document.xml` local ZIP entry is high confidence.
- Generic ZIP files are not claimed without a DOCX hint or WordprocessingML
  package evidence.

## Standards and rights

DOCX is ECMA-376, republished as ISO/IEC 29500. Its rights position (the
Microsoft Open Specification Promise and its three self-stated limits), the
approved labels and the provenance findings are in
[the DOCX register](../ip/docx.md). Decided is not cleared: no lawyer reviewed
it, and patent-freedom is not claimed. No format label may name a vendor or its
product, so `.docx` is offered as `DOCX Document (*.docx)`.
