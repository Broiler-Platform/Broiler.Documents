# Issue #152: five WordprocessingML theme-color corrections

Starting from `8335698`, this change resolves theme colors in five separately
tested contexts: **run foreground, run background, paragraph background,
table/cell backgrounds, and table/cell borders**. All previously relied on
literal RGB in at least one of their reading paths. Existing DrawingML theme
color support is separate and keeps its existing conversion.

A shared WordprocessingML resolver now maps Word's theme names (including
text/background/dark/light and hyperlink slots), looks up the document palette,
and applies byte-valued tint/shade luminance modifiers. A resolved theme takes
precedence over its cached RGB value, as the format specifies; missing/unknown
themes fall back to RGB without applying theme-only modifiers to that fallback.
The [python-docx color analysis](https://github.com/python-openxml/python-docx/blob/master/docs/dev/analysis/features/text/font-color.rst)
also explains the distinction between a theme reference and its RGB cache.

Word luminance conversion uses truncated byte channels; it is tested against
Text 2 `1F497D` with tint `99` -> `548DD4` and Accent `4F81BD` with shade `BF`
-> `366091`. Invalid tone bytes are ignored. The DrawingML gamma/transform path
is unchanged.

Shading consistently respects `nil`, chooses foreground for `solid`, and keeps
highlight precedence. Explicit `auto` text color still clears inherited color.
Resolved colors round-trip as explicit RGB through the existing DOCX writer;
live theme bindings are not retained by the fully resolved document model.

## Validation

The Release solution build and **4,173 unit tests** pass, with three additional
skips. The DOCX project has **693 passing tests**, including **27 new cases**.
Against the isolated pre-change DOCX assembly, **23 cases fail and four unchanged
paths pass**. Coverage includes stale cache precedence, Word slot names,
tints/shades and byte conversion, RGB fallback, nil/auto resets, solid shading,
highlighting, table/cell scope and export round trips.

The CLI corpus passes **2,320/2,320**, with 105 documented skips and no baseline
changes.

The native controls use a complete theme part and relationship, explicit content
types, valid shading declarations and deliberately contradictory RGB caches to
make the precedence visible. Both sides use the same input paths/hashes,
LibreOffice 26.8.1.1, poppler 26.05.0, 72 DPI, the 80% threshold, a 40-page cap
and 30-second reference timeout. Fonts are unpinned.

All six native input controls retain **18/18** passing checks. This does **not**
prove that theme resolution matches the tested LibreOffice importer: it paints
the deliberately stale RGB cache on these inputs, while Broiler resolves the
live palette. Narrow color/blur comparisons can therefore differ or worsen.
No newly passing native input check is claimed.

Native import of the **exported** DOCX files verifies that the resolved colors
are retained and painted as explicit RGB:

| Context | Native exported color |
| --- | --- |
| Text foreground | `#336699` |
| Paragraph background | `#8BB2D8` |
| Run background | `#19334C` |
| Table background | `#8BB2D8` |
| Top/bottom table or cell borders | `#8BB2D8` / `#19334C` |

A supplemental combined-tint/shade control records the resolver's tint-first
fallback; it is not certification of all consumers' handling of simultaneous
modifiers. Custom `clrSchemeMapping` remains unsupported; the existing standard
mapping is used. Patterned shading beyond the existing solid/clear projection
and dynamic theme editing also remain limitations.

## Original control group

The 30 issue #152 originals retain **40/89** passing checks, 49 failures and the
same skipped reference, with no lost pass. Only
`ooxmlexport/data/tdf153964_numberingAfterBreak14.docx` has a minute recorded
profile observation change (0.00928856 -> 0.00928693); page counts, ink-box delta,
coverage and blurred difference are unchanged. No additional ranked original is
claimed fully resolved. Local comparisons do not reproduce the issue's older
native versions or 144-DPI settings.

Local artifacts under `/workspace/.setup`: `make-issue152-word-color-probes.py`,
`issue152-word-color-probes`, isolated `issue152-word-colors-before-bin`,
`issue152-word-colors-final-before/after.json` and matching triage/work directories,
`issue152-word-colors-final-originals.json`, explicit-color exports and their
native PDFs/rasters, old-assembly tests, and final build/unit/corpus logs.
Initial shading probes without explicit `w:val` are not the final comparison.
No threshold, baseline classification or tool approval was changed.
