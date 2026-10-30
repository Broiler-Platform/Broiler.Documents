# Office fidelity fixes, October 2026

Small RTF, HTML, ODT and pagination fixes made on 2026-10-19 and 2026-10-20
while working through the office suite's open render rows. Each has its own
regression tests.

Render measurements were taken locally on Windows with LibreOffice 26.2.6.3
and Poppler 26.07.0 at 144 DPI, with the local Liberation fonts explicitly
mapped unless an entry says otherwise, using the developer-only
`--allow-pending-tools` switch. The official baseline comparison was skipped
every time: its stamp requires Linux, LibreOffice 24.2.7.2, Poppler 24.02.0
and `office-fonts-1`. No tool approval, baseline measurement or classification
changed, so the `suspected-defect` rows for `line-break-within-paragraph` and
`letterhead-frames` stay open for verification on the pinned toolchain.

Entries without a measurement are codec fixes verified by model → format →
model round trips. They do not change any office render classification.
Measurement columns: ink-box difference, ink-profile similarity, blurred
difference ratio and ink coverage difference.

## Pagination and fonts

### Line height from actual pieces

A populated line's ascent and descent are the maxima over its actual pieces;
the default font's metrics apply only to empty lines. Before, a line entirely in
10-point text could inherit a 24-point default's height.

This did not move the four `line-break-within-paragraph` rows. Without pinned
fonts, before and after were identical: DOCX 66 px / 0.869914, ODT 66 px /
0.869913, HTML 66 px / 0.871518, RTF 8 px / 0.935313. With Liberation mapped,
DOCX measured 32 px / 0.900049, close to the stored baseline. The investigation
pointed at the Graphics fallback's vertical estimate (`GetLineHeight` returns
`Math.Ceiling(font.Size * 1.25)`, ascent `font.Size * 0.8`), which the next two
entries replace for mapped fonts.

### Mapped-font vertical metrics

The CLI supplies a mapped face's `hhea` ascent and descent through the
instance-local `LayoutSettings.FontVerticalMetrics` callback, read with the
existing Graphics font reader and cached per mapped file. Text runs, tabs,
empty lines and the initial wrap-height estimate use them; the resolved
baseline is shared by differently sized runs. Missing mappings or invalid
metrics keep the fallback. There is no ambient font discovery or global
metrics registration. (Unmapped families are measured through their host
fallback face since the
[third extras run](office-extras-runs.md#third-ranked-run-marks-and-bands).)

| `line-break-within-paragraph/docx` | Ink box | Profile | Blur | Coverage |
|---|---:|---:|---:|---:|
| Before | 32 px | 0.900049 | 0.053109 | 0.000189 |
| After | 14 px | 0.940642 | 0.051809 | 0.000316 |

One page in both renderers; same categorical bands.

### Mapped-font line gap

The CLI also reads the first mapped face's `hhea` line gap (TTF, OTF or TTC)
and supplies it separately. The gap extends the line box below the baseline, so
the first baseline stays put and later lines advance by the leading. Missing,
malformed, negative or unmapped gaps fall back to zero; a mixed-font line uses
the largest gap. Liberation Serif declares 87 units at 2048 per em, about 0.51
pt at 12 pt or one pixel per line at 144 DPI, which matched the drift.

| `line-break-within-paragraph` | Ink box | Profile | Blur |
|---|---:|---:|---:|
| DOCX | 14 → 1 px | 0.940642 → 0.999856 | 0.051809 → 0.000003 |
| ODT | 14 → 1 px | 0.940559 → 0.999854 | 0.051799 → 0.000003 |
| HTML | 14 → 0 px | 0.940074 → 0.999817 | 0.052162 → 0.000003 |
| RTF | 10 → 6 px | 0.958964 → 0.992545 | 0.056613 → 0.025866 |

Local reports: `D:\office-next-before.json`, `D:\office-linegap-after.json`.
The RTF residue is the default-font fix under [RTF](#rtf).

### Footer distance

The paginator anchors the bottom of the footer's line block at
`PageGeometry.FooterDistance` above the effective page's bottom edge; without
page geometry it centres the footer in the margin. This applies to every codec
that supplies the distance.

`letterhead-frames/docx`, without pinned fonts, two pages in both renderers:
worst-page ink box 52 → 32 px, profile 0.892125 → 0.893072, blur 0.024102 →
0.023547. Page two's footer ink bottom (any channel below 200, bottom 20%) moved
from 1490 to 1434 px against LibreOffice's 1438 px.

### Header distance

The paginator anchors the top of the header's complete line block at
`PageGeometry.HeaderDistance` below the effective page's top edge; without page
geometry it centres it. Header shapes keep their own coordinates. Invalid
distances are diagnosed and the band omitted. (Since the
[third extras run](office-extras-runs.md#third-ranked-run-marks-and-bands) a
header or footer that overruns its margin is drawn and pushes the text; only
one taller than half the text height is omitted.) LibreOffice's HTML header
convention and ODT's geometry mapping are outside this fix.

`letterhead-frames/docx` (with the two preceding fixes) did not move: 29 px,
0.832161, blur 0.023630, coverage 0.000289 before and after (local reports
`D:\office-header-before.json`, `D:\office-header-after.json`).

### Running-content paragraph spacing

Header and footer blocks honour their paragraphs' `SpacingBefore` and
`SpacingAfter` as the body does: nonnegative
before-spacing between paragraphs (suppressed on the first) and nonnegative
after-spacing after each. The gaps count towards margin fit and are preserved
when the block is anchored at its distance or centred.

`letterhead-frames` did not move (before = after): DOCX 29 px / 0.847907 /
blur 0.019846; ODT 144 px / 0.998436 / 0.001104; RTF 1202 px / 0.877629 /
0.077538; HTML 143 px / 0.353663 / 0.058659. Local reports
`D:\office-letterhead-next-before.json`, `D:\office-running-spacing-after.json`.

## RTF

### Multiplier line spacing

Positive `\slN\slmult1` reads as a `N / 240` multiplier (`\sl276\slmult1` is
1.15), respecting group restoration and paragraph resets, and a final
paragraph without `\par` keeps its properties. The writer emits multipliers in
1/240 steps. Interpretation follows
[Microsoft's RTF paragraph control-word documentation](https://learn.microsoft.com/en-us/windows/win32/controls/about-text-object-model);
[rtf.md](rtf.md) has the current rules, including stated heights.

| `line-break-within-paragraph/rtf` | Ink box | Profile | Blur | Coverage |
|---|---:|---:|---:|---:|
| Before | 58 px | 0.796916 | 0.063473 | 0.004101 |
| After | 10 px | 0.958964 | 0.056613 | 0.004158 |

Ink box loose → near, profile poor → fair (local reports
`D:\office-rtf-spacing-before.json`, `D:\office-rtf-spacing-after.json`).

### Default font

An explicit `\deffN` resolves into `DocumentStyleDefaults.FontFamily`, so runs
with no `\fN` (including `\plain` runs) inherit it; the writer puts the family
in the font table and emits `\deffN`. Explicit run families win; missing,
unknown or blank entries leave the family unspecified. The fixture declares
`\deff3` (Liberation Serif) and uses `\plain` body runs.

| `line-break-within-paragraph/rtf` | Ink box | Profile | Blur | Coverage |
|---|---:|---:|---:|---:|
| Before | 6 px | 0.992545 | 0.025866 | 0.004046 |
| After | 1 px | 0.999856 | 0.000320 | 0.000262 |

Profile and coverage bands are now exact and the blur clean (local report
`D:\office-rtf-default-font-after.json`).

### `\plain` restores the code page

The reader keeps the document code page apart from the active font's. `\plain`
restores the declared default font's charset, else the document/caller code
page; both follow group state. Tests distinguish the states through the
Latin-1 fallback for unsupported code pages.

### Font-table names

- Font-table names decode `\uN` with fallback counts, hex bytes and escaped
  braces/backslashes, so `München` no longer reads back as `M?nchen`. Names stay
  metadata; no font file is loaded.
- Literal high bytes in a name use the same code-page decoder and diagnostics
  as hex bytes and body text: a Windows-1252 `0x92` becomes U+2019, not a
  control character.
- The writer escapes `;` inside a name as `\u59?`, so it no longer ends the
  table entry.

### Resource tables cover running content and shapes

Font and colour tables are collected from body paragraphs, body-shape text and
every header/footer variant (default, first, even). Before, a resource used only
there resolved to index zero, losing its family, colour and highlight.

### Inherited font size on export

The writer emits a non-12-point `DocumentStyleDefaults.FontSizePoints` on every
run without an explicit `\fs` (body, header/footer, shape text). Read-back keeps
the effective size as a run property, not as a document default; half-point
precision applies. The office fixtures use 12 points, so no render row moves.

### Unicode fallback skipping

- A character-producing control symbol (`\{`, `\\`, `\~`, `\_`) consumes one
  pending fallback character: `\uc1\u233\{tail` reads `étail`, not `é{ail`. A
  zero `\ucN` keeps the escaped character.
- A control word and its argument count as one fallback unit and are skipped
  without effect (no tab, break or bold).
- An opening or closing group brace ends pending fallback skipping:
  `\uc2\u233?{tail}` keeps its `t`, and a header's trailing escape no longer
  eats the body's first character. Escaped braces stay character tokens.

The last two follow the
[Microsoft RTF 1.7 specification, mirrored PDF](https://latex2rtf.sourceforge.net/RTF-Spec-1.7.pdf).

## HTML

### Decode once

The DOM parser already resolves character references in text and attributes;
the codec no longer decodes either again. Literal entity spellings survive
(`&amp;lt;tag&amp;gt;` reads as `&lt;tag&gt;`, not `<tag>`), as do link query
values and font names. Raw stylesheet text gets no HTML entity decoding.

### Quoted font names

- A quoted family loses only its matching outer quote pair; escaped quotes and
  backslashes inside are decoded, and literal quotes at the edges kept. General
  CSS hexadecimal escapes are outside the supported subset.
- Declaration splitting and family-list splitting ignore `;` and `,` inside
  quotes (escaped quotes respected), so `'Alpha;Beta'` and `'Alpha,Beta'` stay
  one name in inline styles and type rules.

### Nonfinite numbers

NaN, infinity, numeric overflow and overflowing unit conversions are rejected,
keeping the inherited font size, margin or line spacing.

## ODT

### Line-spacing range

A positive percentage that overflows or underflows the model's float keeps the
inherited spacing and reports `odt.linespacing.range`.
