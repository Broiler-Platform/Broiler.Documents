# RTF Conformance And Limits

This document is the authoritative statement of what `Broiler.Documents.Rtf`
honours, approximates, ignores, and skips when reading RTF into the rich-text
document model (ADR 0005), and what it emits when writing. The reader is total
(never throws on malformed input) and safe by default (ADR 0004). The categories
below are exercised by `RtfConformanceTests`, `RtfLimitTests`, and
`RtfSecurityTests`.

## Honoured — read into the model

| Group | Control words |
|---|---|
| Inline character | `\b` `\b0` `\i` `\i0` `\ul` `\ul0` `\ulnone` `\strike` `\strike0` `\caps` `\caps0` `\scaps` `\scaps0` `\plain` `\fsN` `\fN` `\cfN` `\cbN` `\highlightN` |
| Paragraph | `\pard` `\ql` `\qc` `\qr` `\liN` `\sbN` `\saN` |
| Breaks & entities | `\par` `\line` `\tab` `\cell` (→ tab) `\row` (→ paragraph) `\lquote` `\rquote` `\ldblquote` `\rdblquote` `\bullet` `\endash` `\emdash` `\enspace` `\emspace` |
| Control symbols | `\\` `\{` `\}` `\~` (nbsp) `\_` (nb-hyphen) `\-` (optional hyphen, dropped) `\`+CR/LF (→ paragraph) `\*` (destination marker) |
| Encoding | `\'hh` `\uN` `\ucN` `\ansicpgN` `\fcharsetN` (per font) |
| Tables | `\fonttbl` `\colortbl` |
| Fields | `\field` `\fldinst` `\fldrslt` (HYPERLINK only) |

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
`\viewkind`, `\widowctrl`, `\kerning`, and the many document/section options.
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
as `\uN?`. An embedded image is written as a `\pict` destination — `\pngblip`
or `\jpegblip`, sized with `\picwgoal`/`\pichgoal` — so a picture survives into
Word and WordPad; any other image format is dropped with `rtf.image.format`.
Because `\pict` is a skipped destination on the read side, an image does **not**
survive a model → RTF → model round-trip; DOCX is the format that round-trips
pictures. Round-trip is otherwise lossless for any document the reader can
produce, with two further documented exceptions: line spacing and list kind
are not written (they are not read either, so they stay at their defaults); and a
**non-ASCII font-family name** is escaped as `\uN` on write, which the font-table
reader does not decode (font names are read as their literal bytes) — so such a
name does not survive a round-trip. Both are rare and safe (never lossy for text,
formatting, colors, sizes, alignment, or hyperlinks).
