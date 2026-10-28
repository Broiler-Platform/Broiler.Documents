# Shared Layout Foundations: Unicode Boundaries and Cluster-Aware Fitting

This document defines the shared Unicode text segmentation and cluster-aware fitting architecture bridging rich-text document pagination (`Broiler.Documents.Pagination`), PDF export layout (`Broiler.Documents.Pdf`), and font/shaping contracts (`Broiler.Graphics.Text`).

---

## 1. Pagination Gaps Identified & Resolved

1. **Non-Breaking Spaces Classified as Breakable Whitespace**:
   - *Defect*: `DocumentLayout.Fragments` previously checked `char.IsWhiteSpace(character)`. In .NET, `char.IsWhiteSpace` returns `true` for U+00A0 (NO-BREAK SPACE), U+202F (NARROW NO-BREAK SPACE), and U+2007 (FIGURE SPACE). This resulted in non-breaking spaces being converted into whitespace `LayoutToken` instances, causing `LineWrapper` to split lines at non-breaking spaces when available width ran out.
   - *Fix*: Delegated whitespace classification to `UnicodeBoundaryClassifier.IsBreakableWhitespace`. Non-breaking spaces (`\u00A0`, `\u202F`, `\u2007`, `\u2060`, `\uFEFF`) return `false` and remain grouped with word characters as non-breaking word glue.

2. **Emergency `char` Splitting in Surrogate Pairs & Combining Sequences**:
   - *Defect*: In both `LineWrapper.BreakToken` and `PdfPageLayout.SplitOversizedWord`, over-wide words were broken by iterating `foreach (char c in text)`. This sliced 32-bit surrogate pairs (e.g. emojis U+1F600 😀, supplementary characters) between their high (`\uD83D`) and low (`\uDE00`) halves, emitting isolated surrogate characters onto separate lines. It also severed combining diacritics (e.g. `e\u0301`) from their base letters.
   - *Fix*: Refactored emergency token splitting to iterate by extended grapheme clusters via `UnicodeBoundaryClassifier.GetGraphemeClusters(text)`. Surrogate pairs and base+combining mark sequences are treated as indivisible units. If a single indivisible cluster exceeds the column width on an empty line, it is placed on its own row and terminates without hanging.

3. **Shaped Run Advance Derivation**:
   - *Defect*: Summing isolated UTF-16 character measurements (`_textMeasurer.MeasureAdvance(character.ToString(), ...)` or fallback widths) failed for surrogate halves (producing zero advance or double fallback).
   - *Fix*: Advance measurements now operate on full codepoints and complete grapheme clusters in `ResolvedFontFace`, `LineWrapper`, and `PdfPageLayout`.

4. **Independent PDF Path Verification**:
   - While `PdfPageLayout.SplitWords` already isolated breaking spaces with `IsBreakSpace(c) => c is ' ' or '\t'`, its emergency break method `SplitOversizedWord` shared the UTF-16 `char` splitting flaw. `PdfPageLayout.SplitOversizedWord` now walks `UnicodeBoundaryClassifier.GetGraphemeClusters` to guarantee cluster integrity in PDF output.

---

## 2. Shared Boundary Contract & Unicode Standards

- **Declared Unicode Version**: Unicode 15.1
- **Supported Rule Subsets**:
  - **UAX #29 (Unicode Text Segmentation - Extended Grapheme Clusters)**:
    - Surrogate pair preservation (U+10000..U+10FFFF)
    - Base characters + Combining marks (`Mn`, `Mc`, `Me`)
    - CRLF sequence (`\r\n`) as a single cluster
    - Emoji ZWJ sequences (`\u200D`) and variation selectors (`\uFE0F`)
  - **UAX #14 (Unicode Line Breaking Algorithm pragmatic subset)**:
    - Mandatory breaks (`BK`, `CR`, `LF`, `NL`): U+2028 (LINE SEPARATOR), `\r`, `\n`, U+2029 (PARAGRAPH SEPARATOR)
    - Break opportunities (`SP`, `BA`, `ZW`): `' '`, `'\t'`, `'\u3000'` (Ideographic space), `'\u200B'` (Zero-width space), `'\u00AD'` (Soft hyphen)
    - Prohibited breaks (`GL`): U+00A0 (NBSP), U+202F (NNBSP), U+2007 (Figure space), U+2060 (Word joiner), U+FEFF (ZWNBSP)
    - CJK Ideographs (`ID`): Unified ideographs, Hiragana, Katakana, and Hangul permit line break opportunities before and after, unless inhibited by opening (`OP`) or closing (`CL`) punctuation.

---

## 3. Complex Text Shaping Contract & Script Declarations

### 3.1 Shaped Data Contract

Defined in [`Broiler.Documents.Model.Text.ShapedTextContract`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Text/ShapedTextContract.cs):
- [`TextDirection`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Text/ShapedTextContract.cs#L9): `LeftToRight`, `RightToLeft`.
- [`ShapedGlyphInfo`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Text/ShapedTextContract.cs#L18): `(int GlyphIndex, double Advance, double XOffset, double YOffset)`.
- [`ShapedCluster`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Text/ShapedTextContract.cs#L28): Links a group of shaped glyphs to its source-text cluster range `(SourceOffset, SourceLength)`, direction, and immutable [`FontFaceIdentity`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Fonts/FontFaceIdentity.cs).

### 3.2 Script Support Catalog

- **Declared Supported Scripts**:
  - Simple LTR: Latin, Cyrillic, Greek, Armenian, Georgian
  - CJK: Chinese Han ideographs, Japanese Hiragana/Katakana, Korean Hangul syllables
  - Cursive RTL: Arabic and Persian (contextual joining forms `isol`/`init`/`medi`/`fina`, standard ligatures `liga`/`rlig`, and bidi level reordering via `ComplexTextShaper`)
  - Hebrew: Bidirectional visual reordering without cantillation
- **Declared Unsupported Scripts** (`ScriptSupportStatus.IsUnsupportedScript`):
  - Complex Indic scripts (Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada, Malayalam, Sinhala) requiring full OpenType `indic` syllable reordering and GPOS mark attachment.
  - Southeast Asian scripts (Khmer, Myanmar, Thai, Lao) requiring dictionary-based syllable boundary analysis.
  - Tibetan and Mongolian vertical scripts.

---

## 4. Verification Evidence

- **`UnicodeBoundaryClassifierTests`** (`Broiler.Documents.Model.Tests`):
  - UAX #29 extended grapheme clusters: ASCII, surrogate pairs (emojis), combining sequences, CRLF pairs.
  - UAX #14 line break classification: breakable whitespace vs. non-breaking spaces (`\u00A0`, `\u202F`, `\u2007`, `\u2060`).
  - Mandatory breaks (`\u2028`, `\n`, `\r`).
  - CJK ideograph break opportunities and prohibited boundary punctuation.
  - Script support and unsupported script filter validation.
- **`SharedLayoutUnicodePriority3Tests`** (`Broiler.Documents.Pagination.Tests`):
  - Non-breaking spaces (`\u00A0` and `\u202F`) verified to keep words together without wrapping across lines.
  - Emergency fitting with surrogate pairs verified to never split surrogate halves across layout pieces.
  - Combining marks verified to remain bound to their base character.
  - Indivisible cluster termination on extremely narrow columns without hanging.
  - Cross-style words verified to stay within a single unbreakable word token.
- **`SharedLayoutPdfUnicodePriority3Tests`** (`Broiler.Documents.Pdf.Tests`):
  - PDF layout with non-breaking spaces verified.
  - Oversized words with combining marks and surrogate pairs in PDF layout verified to process cleanly without corrupting UTF-16 code units.
