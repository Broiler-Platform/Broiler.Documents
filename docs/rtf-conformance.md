# RTF Conformance And Limits

This document is the authoritative statement of what `Broiler.Documents.Rtf`
honours, approximates, ignores, and skips when reading RTF into the rich-text
document model (ADR 0005), and what it emits when writing. The reader is total
(never throws on malformed input) and safe by default (ADR 0004). The categories
below are exercised by `RtfConformanceTests`, `RtfLimitTests`,
`RtfPageBreakTests`, and `RtfSecurityTests`.

## Honoured — read into the model

| Group | Control words |
|---|---|
| Inline character | `\b` `\b0` `\i` `\i0` `\ul` `\ul0` `\ulnone` `\strike` `\strike0` `\caps` `\caps0` `\scaps` `\scaps0` `\plain` `\fsN` `\fN` `\cfN` `\cbN` `\highlightN` |
| Paragraph | `\pard` `\ql` `\qc` `\qr` `\liN` `\sbN` `\saN` `\pagebb` `\pagebb0` |
| Breaks & entities | `\par` `\line` `\page` (→ `PageBreakBefore` on the paragraph that follows) `\tab` `\cell` (→ tab) `\row` (→ paragraph) `\lquote` `\rquote` `\ldblquote` `\rdblquote` `\bullet` `\endash` `\emdash` `\enspace` `\emspace` |
| Control symbols | `\\` `\{` `\}` `\~` (nbsp) `\_` (nb-hyphen) `\-` (optional hyphen, dropped) `\`+CR/LF (→ paragraph) `\*` (destination marker) |
| Encoding | `\'hh` `\uN` `\ucN` `\ansicpgN` `\fcharsetN` (per font) |
| Tables | `\fonttbl` `\colortbl` |
| Fields | `\field` `\fldinst` `\fldrslt` (HYPERLINK only, including the `\l` switch that spells a same-document reference: it is read as `#name` and written back the same way. The name may not contain a quote — the field argument is quoted and RTF has no escape for one inside it, so a quote would truncate the target silently; the shared rule refuses it for every format. Targets otherwise follow that rule, or `rtf.link`) |

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
  words: sections are out of the first-release subset, so a document that starts
  its new page by starting a new section reads as one continuous flow. The
  exporter this mapping was checked against does not do that for a
  paragraph-level break - LibreOffice writes `\sectd\sbknone` for the one section
  and `\pagebb` on the paragraph - but a document from elsewhere may.

## Approximated

| Construct | Approximation |
|---|---|
| `\qj` (justify) | Mapped to left (nearest supported alignment) |
| `\liN` (twips) | Mapped to a discrete indent level (`round(N / 360)`, capped) |
| `\fs` on write | Rounded to the nearest half-point (`\fs` is integer half-points) |
| `\'hh` under a non-Windows-1252 code page | Decoded via a Latin-1 fallback for `0x80`-`0xFF`, reported once as `rtf.codepage`. Windows-1252 (incl. smart quotes) is exact. `\uN` text is always exact. |

## Ignored — parsed, no effect on the model

Unrecognized formatting control words are silently ignored (predictable
degradation), for example `\sl`/`\slmult` (line spacing), `\deff`, `\lang`,
`\viewkind`, `\widowctrl`, `\kerning`, `\sect`/`\sectd` and the section break
kinds (`\sbkpage` and the rest), and the many document/section options.
List markup does not set `ListKind` in this release (it stays `None`); list
tables are skipped destinations.

## Skipped destinations — content dropped

`\pict` and `\object`/`\*\objdata` (reported once as `rtf.embedded`), and the
following non-content destinations: `\info` `\stylesheet` `\header` `\footer`
`\footnote` `\annotation` `\colorschememapping` `\latentstyles` `\datastore`
`\themedata` `\generator` `\listtable` `\listoverridetable` `\revtbl` `\pntext`,
plus any unknown ignorable destination introduced by `\*`. `\binN` binary data is
skipped at the tokenizer (reported as `rtf.bin` when it exceeds `MaxBinBytes`).

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
  `LinkHref`; other schemes (`javascript:`, `data:`, `file:`, `vbscript:`, …) are
  dropped and reported as `rtf.link`.
- **Bounded.** Every limit above is enforced; binary is skipped, not tokenized.
- **Total.** Malformed input yields a best-effort document plus diagnostics, never
  an exception across the API.

## Writing (model → RTF)

The writer emits the honoured subset above as pure ASCII: a `{\rtf1\ansi\ansicpg1252\deff0\uc1`
header, `\fonttbl`/`\colortbl` built from the styles used, one group-wrapped run
per style, and a `\par` after every paragraph. Non-ASCII characters are escaped
as `\uN?`. A page break is written as `\page`, at the head of the paragraph it
belongs to: both spellings say the same thing and Word and Writer honour both,
but `\page` lives in the character stream that any reader has to walk to get the
text out at all, where `\pagebb` is a paragraph property that a simpler reader
steps over without a word. Writing both would tell a reader that honours both to
break twice for one break. The first paragraph of a document is the exception and
takes `\pagebb` instead: a `\page` at the head of the body is not a break between
two things, and a reader that meets one draws a blank first page nobody asked
for. The same one-line document renders on two pages in Writer with `\page` and
on one with `\pagebb`, because a break before the first paragraph is already
satisfied - which is also why this codec's own layout ignores it there. The word
is read back either way, so the flag round-trips. A page break on a header, footer, or shape
paragraph is **not** written, and says so once as `rtf.pagebreak.dropped`: there
is no page for it to start. An embedded image is written as a `\pict` destination — `\pngblip`
or `\jpegblip`, sized with `\picwgoal`/`\pichgoal` — so a picture survives into
Word and WordPad; any other image format is dropped with `rtf.image.format`.
Because `\pict` is a skipped destination on the read side, an image does **not**
survive a model → RTF → model round-trip; DOCX is the format that round-trips
pictures. A table is written as its cell paragraphs in row order, with
`rtf.table.flattened`: RTF states a table as `\trowd` and `\cellx` runs, and this
codec's reader knows neither, so a table written that way would come back as
nothing at all. A floating picture is written into the paragraph it is anchored
to, with `rtf.image.anchored`: a picture inside a shape is a `pib` shape
property, and this reader knows only the fill and line properties — so a shape
written that way would come back with no picture and no paint, which is a shape
it drops. In both, what is on the page is worth more than the structure around
it. Round-trip is otherwise lossless for any document the reader can
produce, with three further documented exceptions: line spacing and list kind
are not written (they are not read either, so they stay at their defaults); a
**non-ASCII font-family name** is escaped as `\uN` on write, which the font-table
reader does not decode (font names are read as their literal bytes) — so such a
name does not survive a round-trip; and a **page break inside running content or
a shape** is read (`\pagebb` is honoured in those destinations too) but not
written back, per `rtf.pagebreak.dropped` above. All three are rare and safe
(never lossy for text, formatting, colors, sizes, alignment, or hyperlinks).


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
