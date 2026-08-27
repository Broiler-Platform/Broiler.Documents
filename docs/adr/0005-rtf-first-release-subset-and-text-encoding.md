# ADR 0005: RTF First-Release Subset And Text Encoding

**Status:** Accepted for Phase 1 scaffolding
**Date:** 2026-07-05

## Context

The Phase 0 exit gate requires that the first-release RTF subset be explicit and
that no public API commits to a code-page/indexing model that would block correct
Unicode work later. The target model is fixed by RichEdit ADR 0014 (promoted per
ADR 0002): fully-resolved `InlineStyle` and `ParagraphStyle` over opaque
positions. The RTF subset must map onto exactly those attributes and skip
everything else (ADR 0004), rather than approximate richer constructs.

## Decision

- **Authoritative inline (character) subset.**

  | RTF control word | Model (`InlineStyle`) | Notes |
  |---|---|---|
  | `\b` / `\b0` | `Bold` | |
  | `\i` / `\i0` | `Italic` | |
  | `\ul` / `\ulnone` | `Underline` | underline variants (`\uld`, `\ulw`, …) collapse to `Underline` |
  | `\strike` / `\strike0` | `Strikethrough` | |
  | `\fsN` | `FontSize = N/2` | RTF font size is in half-points |
  | `\fN` | `FontFamily` | resolved through the `\fonttbl` |
  | `\cfN` | `Foreground` | resolved through the `\colortbl` |
  | `\highlightN` / `\cbN` | `Background` | |
  | `\field{\*\fldinst{HYPERLINK "url"}}{\fldrslt …}` | `LinkHref` | hyperlink fields only (URL policy per ADR 0004); other field types dropped |

- **Authoritative paragraph subset.**

  | RTF control word | Model (`ParagraphStyle`) | Notes |
  |---|---|---|
  | `\pard` | reset to `ParagraphStyle.Default` | |
  | `\ql` `\qc` `\qr` `\qj` | `Alignment` | justify maps to the nearest supported value |
  | `\slN\slmultN` | `LineSpacing` | multiple form → `LineSpacing`; absolute twip spacing approximated |
  | `\liN` | `IndentLevel` | twips → discrete levels (round `N/360` per 0.25"), capped |
  | `\sbN` / `\saN` | `SpacingBefore` / `SpacingAfter` | twips → logical units |
  | `\par` | paragraph break | new `RichTextParagraph` |
  | `\line` | soft line break | maps to the model's `U+2028` soft break |
  | list tables / `\pntext` / `{\*\pn}` | `ListKind`, `IndentLevel` | bullet/number detection **deferred** to a later phase |

- **Text encoding model.**
  - Header `{\rtf1 …}`, character set (`\ansi`/`\mac`/`\pc`/`\pca`), `\ansicpgN`,
    default font `\deffN`, and per-font `\fcharsetN` (in `\fonttbl`) establish the
    active code page.
  - `\'hh` hex bytes decode against the active code page; `\uN` Unicode is honored
    with its `\ucN` skip count; writing is surrogate-safe (a supplementary
    character emits the correct `\uN` pair with an ASCII fallback char).
  - Twips constant: 1 inch = 1440 twips; 1 point = 20 twips.

- **Indexing-model neutrality.** The codec targets the opaque
  `RichTextPosition`/`RichTextRange` and the model's surrogate-safe boundaries
  (ADR 0002/0014); it does not expose byte or UTF-16 offsets in its public API,
  leaving grapheme/bidi correctness free to improve later.

- **Explicitly out of first release.** Tables, floating objects, sections,
  columns, headers/footers, footnotes, style sheets (`\stylesheet` is parsed only
  enough to resolve `\sN` references), revision marks, and non-hyperlink fields
  are skipped safely (ADR 0004), not approximated. This mirrors ADR 0014's
  out-of-scope list.

## Consequences

- The reader (roadmap Phase 2) and writer (Phase 3) implement exactly this table;
  a documented "honored / approximated / ignored" control-word matrix is a Phase 4
  deliverable.
- Because the subset equals the ADR 0014 style set, round-trip cannot introduce
  styles the model cannot represent (the same guarantee ADR 0016 gives HTML).
- Encoding correctness (`\u`/`\uc`/code pages/surrogates) gets a dedicated test
  corpus; the model's existing surrogate-safe boundaries are reused.
- This ADR satisfies the Phase 0 exit-gate items "first-release RTF subset is
  explicit" and "no public API commits to an unchosen code-page/indexing model."
