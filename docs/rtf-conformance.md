# RTF Conformance And Limits

This document is the authoritative statement of what `Broiler.Documents.Rtf`
honours, approximates, ignores, and skips when reading RTF into the rich-text
document model (ADR 0005), and what it emits when writing. The reader is total
(never throws on malformed input) and safe by default (ADR 0004). The categories
below are exercised by `RtfConformanceTests`, `RtfLimitTests`,
`RtfPageBreakTests`, `RtfSecurityTests`, `RtfCapitalizationTests`,
`RtfRunningContentTests`, `RtfPageGeometryTests`, `RtfShapeTests`, and
`RtfRoundTripTests`.

## Honoured — read into the model

| Group | Control words |
|---|---|
| Inline character | `\b` `\b0` `\i` `\i0` `\ul` `\ul0` `\ulnone` `\strike` `\strike0` `\caps` `\caps0` `\scaps` `\scaps0` `\plain` `\fsN` `\fN` `\cfN` `\cbN` `\highlightN` |
| Paragraph | `\pard` `\ql` `\qc` `\qr` `\qj` `\liN` `\sbN` `\saN` `\pagebb` `\pagebb0` |
| Breaks & entities | `\par` `\line` `\page` (→ `PageBreakBefore` on the paragraph that follows) `\tab` `\cell` (→ tab) `\row` (→ paragraph) `\lquote` `\rquote` `\ldblquote` `\rdblquote` `\bullet` `\endash` `\emdash` `\enspace` `\emspace` |
| Control symbols | `\\` `\{` `\}` `\~` (nbsp) `\_` (nb-hyphen) `\-` (optional hyphen, dropped) `\`+CR/LF (→ paragraph) `\*` (destination marker) |
| Encoding | `\'hh` `\uN` `\ucN` `\ansicpgN` `\fcharsetN` (per font) |
| Tables | `\fonttbl` `\colortbl` |
| Fields | `\field` `\fldinst` `\fldrslt` (HYPERLINK only, including the `\l` switch that spells a same-document reference: it is read as `#name` and written back the same way. The name may not contain a quote — the field argument is quoted and RTF has no escape for one inside it, so a quote would truncate the target silently; the shared rule refuses it for every format. Targets otherwise follow that rule, or `rtf.link`) |
| Running content | `\header` `\headerf` `\headerl` `\headerr` `\footer` `\footerf` `\footerl` `\footerr` (→ `RunningContent`, not the body flow) |
| Page geometry | `\paperwN` `\paperhN` `\marglN` `\margrN` `\margtN` `\margbN` `\headeryN` `\footeryN` (→ `PageGeometry`) |
| Drawings | `\shp` `\*\shpinst` `\shpleftN` `\shptopN` `\shprightN` `\shpbottomN` `\shptxt`, and the `{\sp{\sn name}{\sv value}}` pairs `fFilled` `fillColor` `fillBackColor` `fillType` `fillAngle` `fLine` `lineColor` (→ `DocumentShape`) |

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
  is read - see below - but the section is not. The
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

A page break inside running content is read, because `\pagebb` is a paragraph
property wherever a paragraph is. It cannot be written back, which the writing
section says out loud rather than leaving to be discovered.

## Page geometry

The section control words that state the paper and its margins - `\paperw`,
`\paperh`, `\margl`, `\margr`, `\margt`, `\margb`, and the `\headery`/`\footery`
distances - are read into `PageGeometry`. They are twips, twenty to the point,
and RTF states them one control word at a time, so they are held aside and made
into a page when the document ends.

A document that states no paper size gets no page rather than a guessed one: a
default invented here would be indistinguishable from a size the author chose. A
document whose margins leave no column to write in gets none either, and is told
so as `rtf.page.geometry` - the numbers are real, but the page they describe is
not one anything can lay out.

This is the page, not sections. Everything else a section states is still
ignored, including the section break kinds above.

## Drawings

A `\shp` group is read into a `DocumentShape` anchored to the paragraph it sits
in. Its box comes from `\shpleft`/`\shptop`/`\shpright`/`\shpbottom` in twips
and its paint from `{\sp{\sn name}{\sv value}}` pairs, of which seven are
understood: `fFilled`, `fillColor`, `fillBackColor`, `fillType` and `fillAngle`
for a solid or gradient fill, `fLine` and `lineColor` for the outline. A shape
colour is one integer holding blue, green and red in that order, which is the
reverse of how the rest of the format writes one and the easiest thing here to
get backwards. `\shptxt` carries the shape's own text, which stays out of the
body for the same reason a header's does.

Every other shape property is ignored, `pib` - a picture inside a shape - among
them. A shape that ends with neither fill nor text is dropped, and so is one
whose box has no width or height: what would be left is a box with nothing in
it, or nothing in a box.

`\shpinst` is the single exception to the `\*` rule below. A shape arrives as
`{\*\shpinst …}`, and the star says to ignore what the reader does not
understand, which this is not.

## Approximated

| Construct | Approximation |
|---|---|
| `\headerr` / `\footerr` | Read as the every-page destination: the model names the first page, the even pages, and the rest, and the right-hand page is the rest |
| `\liN` (twips) | Mapped to a discrete indent level (`round(N / 360)`, capped) |
| `\fs` on write | Rounded to the nearest half-point (`\fs` is integer half-points) |
| `\'hh` under a non-Windows-1252 code page | Decoded via a Latin-1 fallback for `0x80`-`0xFF`, reported once as `rtf.codepage`. Windows-1252 (incl. smart quotes) is exact. `\uN` text is always exact. |

## Ignored — parsed, no effect on the model

Unrecognized formatting control words are silently ignored (predictable
degradation), for example `\sl`/`\slmult` (line spacing), `\deff`, `\lang`,
`\viewkind`, `\widowctrl`, `\kerning`, `\sect`/`\sectd` and the section break
kinds (`\sbkpage` and the rest), and the document and section options other than
the page geometry above. The character-set words `\ansi`, `\mac`, `\pc` and
`\pca` are ignored too: the active code page comes from `\ansicpgN`, from a
font's `\fcharsetN`, or from the caller's default, and nothing else sets it.
Shape properties other than the seven named above are ignored as well.
List markup does not set `ListKind` in this release (it stays `None`); list
tables are skipped destinations.

## Skipped destinations — content dropped

`\pict` and `\object`/`\*\objdata` are skipped and reported once as
`rtf.embedded` - or once as `document.capability.not-composed` when the caller
asked for embedded decoding, because a caller that asked for something this
reader does not compose is owed an answer to the question rather than a note
about the document. Skipped with them are the following non-content
destinations: `\info` `\stylesheet` `\footnote` `\annotation`
`\colorschememapping` `\latentstyles` `\datastore` `\themedata` `\generator`
`\listtable` `\listoverridetable` `\revtbl` `\pntext`, plus any unknown
ignorable destination introduced by `\*` - `\*\shpinst` excepted, since the
reader understands that one. `\binN` binary data is skipped at the tokenizer
(reported as `rtf.bin` when it exceeds `MaxBinBytes`).

`\header` and `\footer` are not on that list. They were, and their content was
dropped; it is now read onto the document's running content, above. None of
their text reaches the body - which is what a check for content loss looks at -
but none of it is lost either.

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
`{\rtf1\ansi\ansicpg1252\deff0\uc1` header, `\fonttbl`/`\colortbl` built from
the styles used, the page geometry and the header and footer destinations the
document states - before the body, where a reader takes them as section
properties - one group-wrapped run per style, and a `\par` after every
paragraph. A document that states no page writes no `\paperw`, because
inventing one would put words in the author's mouth. Non-ASCII characters are
escaped as `\uN?`; there is no raw-byte mode, so a caller that sets
`AsciiOnly=false` is told once as `document.capability.not-composed` rather than
quietly handed the escaped form it did not ask for.

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
states a table as `\trowd` and `\cellx` runs, and this codec's reader knows
neither, so a table written that way would come back as nothing at all. A shape
is written as a `\shp` group at the head of the paragraph it is anchored to,
carrying its box, its fill, its outline and its text, which is the whole of what
the reader takes back out of one. A floating picture is the exception: it is
written into the paragraph instead, with `rtf.image.anchored`, because a picture
inside a shape is a `pib` shape property, and this reader knows only the fill and
line properties — so a shape written that way would come back with no picture and
no paint, which is a shape it drops. In the table and the floating picture alike,
what is on the page is worth more than the structure around it.

Round-trip is otherwise lossless for any document the reader can
produce, with three further documented exceptions: line spacing and list kind
are not written (they are not read either, so they stay at their defaults); a
**non-ASCII font-family name** is escaped as `\uN` on write, which the font-table
reader does not decode (font names are read as their literal bytes) — so such a
name does not survive a round-trip; and a **page break inside running content or
a shape** is read (`\pagebb` is honoured in those destinations too) but not
written back, per `rtf.pagebreak.dropped` above. All three are rare and safe
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
| `rtf.codepage` | Info | Read | A code page this build does not carry decoded its high bytes through a Latin-1 fallback; `\uN` text is unaffected. Once per read. |
| `rtf.embedded` | Info | Read | Pictures and objects were skipped. Once per read, and replaced by `document.capability.not-composed` when decoding was asked for. |
| `rtf.page.geometry` | Warning | Read | The section properties described a page with no room to write on, so the page was not read. |
| `rtf.pagebreak.empty` | Info | Read | A page break with no paragraph after it asks for a blank page, which this model cannot hold; the break was dropped. Once per read. |
| `rtf.link` | Warning | Read, write | A hyperlink target the shared rule refuses. Reading drops the link; writing keeps the run as plain text. |
| `rtf.pagebreak.dropped` | Warning | Write | A page break on a header, footer, or shape paragraph was not written; there is no page for it to start. |
| `rtf.table.flattened` | Warning | Write | A table was written as its cell paragraphs, in row order; this codec carries no table structure. |
| `rtf.image.format` | Warning | Write | RTF carries PNG and JPEG pictures; one in another format was dropped. |
| `rtf.image.omitted` | Warning | Write | The caller's resource policy did not permit a picture into the output; the message names the denial. |
| `rtf.image.anchored` | Warning | Write | A floating picture was written into its paragraph; its position beside the text was not kept. |
| `document.capability.not-composed` | Warning | Read, write | Something the caller asked for that this codec does not compose: `DecodeEmbeddedObjects` on a document that carries a picture or object, or `AsciiOnly=false` on a write. |

## Standards And Rights

RTF is a vendor format with no standards body: Microsoft published the
specification and last revised it in 2008. The **Microsoft Open Specification
Promise** names `[RTF]: Rich Text Format` in its covered list, and the promise is
an irrevocable non-assertion of Microsoft Necessary Claims against a conforming
implementation, with defensive termination as its only condition.

That is recorded, with the promise's own three limits, in
[the RTF IP and licensing register](rtf-ip-licensing-register.md), where **every
row is now decided**. Decided is not cleared: no lawyer reviewed it,
patent-freedom is not claimed, and no freedom-to-operate determination has been
made. The promise says the same about itself, and the register quotes it rather
than summarising.

What is settled by inspection rather than assertion: this codec embeds no
third-party RTF code, takes no package reference, reproduces no specification
text, and no `.rtf` file is committed anywhere in the repository. Guard tests fail
the build if any of that stops being true.
